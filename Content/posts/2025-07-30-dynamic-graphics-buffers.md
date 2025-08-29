---
title: "Dynamic Graphics Buffers"
tags:
  - code
  - programming
  - graphics
  - Vulkan
---
Yesterday, I described a [ring buffer allocator](/posts/ring-buffers) suitable for pushing data to a GPU. Today, I'm using that allocator to manage a dynamic buffer suitable for sending things like blocks of uniforms up to the GPU which automatically grows when necessary.

This is written for [Vulkan](https://www.vulkan.org/), but it should translate easily to D3D12, Metal, or other similarly low-level APIs.

# Overview

A `dynamic_buffer` manages not only the contents, but also the lifetime of one or more `VkBuffer` objects that are used to send data up to the GPU. In normal operation, a single buffer is used as a simple ring buffer. However, if that buffer is too small to contain all of the requested allocations, then a new (larger) buffer is allocated to hold allocations going forward while the old one is destroyed as soon as the GPU is done with the data already written to it.

Here's a synopsis of the public API. You'll see references to a `device` type, but that's just a thin wrapper over `VkDevice`; its methods do exactly what you'd expect from their names. The `span` type isn't `std::span` in _my_ code, but it may as well be for how similar they are. And the remaining stuff like `queue` is pretty self-explanatory.

```cpp
class dynamic_buffer
{
public:
    explicit dynamic_buffer(VkBufferUsageFlags usage, VkDeviceSize initial_size = 0) noexcept;

    void initialize(device& dev, VkDeviceSize initial_size = 0);
    void shutdown();

    bool is_initialized() const noexcept;

    void frame_resource_barrier(uint32_t frame_index) noexcept;

    template <typename T, std::size_t Extent = dynamic_extent>
    struct block : span<T, Extent>, VkDescriptorBufferInfo
    {
        T* operator->() noexcept
            requires (Extent == 1);
        const T* operator->() const noexcept
            requires (Extent == 1);

    private:
        //see implementation notes below
    };

    block<std::byte> allocate(std::size_t size, std::size_t align = 16);

    template <typename T>
    block<T, 1> allocate();
    template <typename T>
    block<T> allocate_array(std::size_t count);

    template <typename T>
    block<T, 1> push(no_flush_t, const T& value);
    template <typename T>
    block<T, 1> push(const T& value);

    template <typename T>
    block<T> push(no_flush_t, span<const T> values);
    template <typename T>
    block<T> push(span<const T> values);

    void flush();

private:
    //see implementation notes below
};
```

Alright, so what've we got?

## Initialization

```cpp
void initialize(device& dev, VkDeviceSize initial_size = 0);
void shutdown();
```

Vulkan resource lifetimes don't map very nicely to RAII principles, so we have explicit `initialize` and `shutdown` methods. There is a destructor (rather, some of the private members have destructors), but most of what it does is just assert that `shutdown` has been called.

```cpp
explicit dynamic_buffer(VkBufferUsageFlags usage, VkDeviceSize initial_size = 0) noexcept;
```

The constructor itself does very little, leaving the real work to `initialize`. It is used to set the buffer usage flags for the underlying `VkBuffer`(s), and it can also be used to override the buffer's initial size.

And speaking of `initial_size`, if the buffer grows above that size and then `shutdown` and `initialize` are called in sequence, that larger size will become the new `initial_size`. The idea here is to have the class remember the application's actual need across a `VK_ERROR_DEVICE_LOST` recovery sequence. (Yes. Some of us _do_ actually handle that error.) But if you don't want that, then pass some non-zero value for `initial_size` in `initialize`.

`shutdown` must not be called while the GPU still has any outstanding frames-in-flight.

## Tracking what the GPU is using

```cpp
void frame_resource_barrier(uint32_t frame_index) noexcept;
```

Vulkan (and similar) applications typically have a few (almost always _two_) _frames in flight_. Each FiF represents a frame's worth of data that's being sent from the CPU to the GPU in order to render a frame. The idea is that while _one_ FiF's data is being written by the CPU, the _other_ FiF is being read by the GPU. This allows the two processors to run in parallel without them getting in one another's way.

However, because the buffer is _growable_, the class needs to know when the GPU finishes with the old underlying `VkBuffer`. And since it needs to know that _anyway_, it takes very little extra bookkeeping for the `dynamic_buffer` to _also_ keep track of which bytes in the `VkBuffer` are potentially in use on the GPU. This is all driven by notifying the `dynamic_buffer` when the CPU is finished with one FiF and starting on the next. This is done by calling `frame_resource_barrier` with the index of the FiF that's just been started, but only _after_ getting the signal from Vulkan (via fence or timeline semaphore) that the GPU is actually _done_ with that FiF's data.

That looks a bit like this:
```cpp
fif_data& begin_frame_in_flight()
{
    auto& fif = frames_in_flight[frame_index];

    vk_dev.wait_for_and_reset_fence(fif.done_fence);

    vk_dev.reset(fif.command_pool);
    my_dynamic_buffer.frame_resource_barrier(frame_index);

    return fif;
}
```

## The `block` type

```cpp
template <typename T, std::size_t Extent = dynamic_extent>
struct block : span<T, Extent>, VkDescriptorBufferInfo;
```

A `block` represents a single contiguous allocation in the buffer. It doesn't, however, need to be contiguous with the _previous_ allocation, and it may not even be in the same `VkBuffer`!

The interface is very simple: it's `span`-like so that it's easy to write data _into_ the block, and it's `VkDescriptorBufferInfo`-like so that it's easy to pass to the descriptor API (if needed, this base also has the public members needed to extract a buffer device address for the allocation).

```cpp
T* operator->() noexcept
    requires (Extent == 1);
const T* operator->() const noexcept
    requires (Extent == 1);
```

For single-element allocations, you also get an `operator->`, which is very convenient when writing uniform buffer data since that tends to look like a single struct.

_However_ you prefer to write to a `block`, you should aim to write its bytes sequentially and to never read them back. This is because the `dynamic_buffer` won't insist on memory that's cached (or even host-coherent) and accessing uncached memory in any other way is _slooooooow!_

## Allocating storage

```cpp
block<std::byte> allocate(std::size_t size, std::size_t align = 16);
```

This is the simplest allocation method. It just gives you a span of `size` contiguous bytes to write to.

**Warning**: Because _any_ call to `allocate` can _reallocate_ the underlying `VkBuffer`, it is important to finish writing one allocation's data _before_ calling `allocate` again to create the next!

```cpp
template <typename T>
block<T, 1> allocate();
template <typename T>
block<T> allocate_array(std::size_t count);
```

Very often the contents of uniform buffers are represented in the C++ code as a `struct`. This gives you an easy way to allocate contiguous storage for one or more such values.

```cpp
template <typename T>
block<T, 1> push(no_flush_t, const T& value);
template <typename T>
block<T, 1> push(const T& value);

template <typename T>
block<T> push(no_flush_t, span<const T> values);
template <typename T>
block<T> push(span<const T> values);
```

The `push` family of methods first `allocate` sufficient storage for the given `value` or `values` and then they _write_ the given data to that storage. The deal with the `no_flush_t` overloads is discussed below.

## To `flush` or not to `flush`?

```cpp
//Tag dispatch is cool:
struct no_flush_t { };
inline constexpr no_flush_t no_flush{};

//And in the body of dynamic_buffer:
void flush();
```

Vulkan supports a concept called _non-coherent memory_. That means that while the CPU and GPU are able to access the same RAM, they _don't_ constantly inform one another when they _write_ to that memory, and so their memory caches can easily fall out of sync. This is generally good for performance, but it does mean that the application is responsible for notifying the GPU after it's written data to the buffer from the CPU. This is done explicitly using [`vkFlushMappedMemoryRanges`](https://registry.khronos.org/vulkan/specs/latest/man/html/vkFlushMappedMemoryRanges.html).

The `dynamic_buffer` handles the memory address math and can call that function for you, but you need to tell it when. `allocate` just reserves a region of bytes and flushes nothing (how can it? nothing has yet been written), so `flush` must explicitly be called. A series of allocations can all be flushed as a unit by explicitly calling `flush` after they've all been allocated and written (it flushes everything allocated since the last time it was called).

`push` (without the `no_flush_t` argument) calls `flush` automatically after it's done copying the given data over.

`push` _with_ the `no_flush_t` argument _just_ allocates and copies the data _without_ flushing it. This overload exists because a sequence of pushes can be flushed together as a unit by explicitly calling `flush` once for all of them (just as is the case for `allocate`).

# Implementation

The class implementation follows:

## Private members

```cpp
class dynamic_buffer
{
public:
    //see the overview above

private:
    device* dev = nullptr;

    vk_handle<VkDeviceMemory> mem;
    vk_handle<VkBuffer> buf;
    void* mem_mmap = nullptr;

    VkDeviceSize buf_size;

    void set_initial_size(VkDeviceSize requested_initial_size) noexcept;

    void allocate_buffer(std::size_t min_size = 0);

    ring_buffer_allocator<VkDeviceSize> buf_alloc;
    VkDeviceSize flush_offset, flush_range;
    std::uint32_t min_alignment{0};
    VkBufferUsageFlags usage;

    //more below
};
```

This stuff is fairly straightforward.
* We have a pointer to the `device`, which is used to create and destroy the underlying Vulkan resources, when necessary.
* Then there are some `vk_handle<>` templates. These are _incredibly_ thin wrappers around the various Vulkan handle types, and they do more or less nothing besides being move-only and triggering an assert in their destructor if their value isn't `VK_NULL_HANDLE`. Why does this exist? It helps the code express which handle variables track _ownership_ of a resource and which ones don't, and it lets me know if I'm about to leak a Vulkan object.
* The `mem_map` variable tracks where `buf` has been mapped in virtual memory.
* Then there are some helper methods...
* `buf_alloc` tracks used and free regions within `buf`.
* `flush_offset` and `flush_range` power `flush`.
* `min_alignment` tracks _Vulkan's_ alignment requirements, superimposing them on the alignment requested from `allocate`.
* And the `usage` flags are used to create and reallocate the `VkBuffer` when necessary.

As for the `more below` stuff, that's the guts of `frame_resource_barrier`:

```cpp
class dynamic_buffer
{
public:
    //see the overview above

private:
    //see above

    enum class usage_marker_type
    {
        fence,
        usage,
        free_buffer,
    };

    struct usage_marker
    {
        usage_marker_type type;

        struct fence_
        {
            uint32_t frame_index;
        };

        struct usage_
        {
            VkBuffer buffer;
            decltype(buf_alloc)::marker buf_mark;
        };

        struct free_buffer_
        {
            //this *is* an owning reference, but it's tedious to stick a nontrivial type in a union
            VkBuffer buffer;
            VkDeviceMemory memory;
        };

        union
        {
            struct fence_ fence;
            struct usage_ usage;
            struct free_buffer_ free_buffer;
        };
    };

    queue<usage_marker> usage_markers;
};
```

So, the `usage_markers` queue just holds an ordered list of things to be cleaned up.
* If the `type` is `usage_marker_type::fence` then that marks a boundary between consecutive frames-in-flight.
* A `usage_marker_type::usage` entry tells the buffer when it's safe to mark space in the ring buffer as now free (because the GPU is done reading it).
* A `usage_marker_type::free_buffer` entry tells the buffer when it's safe to delete an old `VkBuffer` after it's been forced to reallocate to a bigger one.

## Implementing initialization

```cpp
explicit dynamic_buffer(VkBufferUsageFlags usage, VkDeviceSize initial_size = 0) noexcept
    : usage{usage}
{
    set_initial_size(initial_size);
}

void initialize(device& dev, VkDeviceSize initial_size = 0)
{
    assert(dev.is_initialized());

    assert(!is_initialized());

    if (initial_size)
        set_initial_size(initial_size);

    dynamic_buffer::dev = &dev;

    VkDeviceSize min_alignment = 4;
    auto dev_limits = dev.physical_device().properties().limits;
    if (usage & VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT)
        min_alignment = max(min_alignment, dev_limits.minUniformBufferOffsetAlignment);
    if (usage & VK_BUFFER_USAGE_STORAGE_BUFFER_BIT)
        min_alignment = max(min_alignment, dev_limits.minStorageBufferOffsetAlignment);
    if (usage & (VK_BUFFER_USAGE_UNIFORM_TEXEL_BUFFER_BIT | VK_BUFFER_USAGE_STORAGE_TEXEL_BUFFER_BIT))
        min_alignment = max(min_alignment, dev_limits.minTexelBufferOffsetAlignment);

    assert(is_pow2(min_alignment) && min_alignment <= UINT32_MAX);

    dynamic_buffer::min_alignment = (uint32_t)min_alignment;
    allocate_buffer();
}

bool is_initialized() const noexcept { return dev != nullptr; }

void set_initial_size(VkDeviceSize requested_initial_size) noexcept
{
    if (buf_size = requested_initial_size; buf_size)
        return;

    //add up some default numbers I picked out of the ether

    if (usage & VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT)
        buf_size += 16 * 1024;

    if (usage & VK_BUFFER_USAGE_INDEX_BUFFER_BIT)
        buf_size += 640 * 1024;

    if (usage & VK_BUFFER_USAGE_VERTEX_BUFFER_BIT)
        buf_size += 4 * 1024 * 1024;

    //never allocate an empty buffer

    if (!buf_size)
        buf_size = 1 * 1024 * 1024;
}

```

So far it's just some straightforward bookkeeping. We take the given `initial_size` or, if it's zero, we make up a default of our own. We also query the device properties to see what the _actual_ minimum alignment is for allocations that'll be bound on the device to the requested descriptor types.

## Implementing buffer creation

Here things get more interesting:

```cpp
void allocate_buffer(std::size_t min_size = 0)
{
    assert_arg(min_size <= UINT32_MAX);

    if (buf)
    {
        debug_print("WARNING: dynamic_buffer was forced to grow its buf!!!\n");

        //free the old buffer

        dev->unmap_memory(mem);
        usage_markers.push({.type = usage_marker_type::free_buffer, .free_buffer = {buf.release(), mem.release()}});

        //the new buffer should be a bit bigger
        //growing slowly since we're trying to cover a rare overshot by _a little_,
        //not an arbitrarily increasing upper bound

        auto new_size = align_up(buf_size + buf_size / 2,
            dev->physical_device().properties().limits.nonCoherentAtomSize);
        //ToDo: check against https://registry.khronos.org/vulkan/specs/latest/man/html/VkPhysicalDeviceMaintenance4Properties.html
        //note: min limit is 2^30
        assert(new_size > buf_size && new_size <= UINT32_MAX);
        buf_size = new_size;
    }

    if (buf_size < min_size)
    {
        auto new_size = align_up(min_size,
            dev->physical_device().properties().limits.nonCoherentAtomSize);
        demand_is(new_size > buf_size && new_size <= UINT32_MAX);
        buf_size = new_size;
    }

    {
        VkBufferCreateInfo ci{};
        ci.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
        ci.usage = usage;
        ci.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
        ci.size = buf_size;

        buf = dev->create_buffer(ci);
        dev->set_object_debug_name(buf, u8"dynamic_buffer");
    }

    {
        auto req = dev->get_memory_requirements(buf);
        auto mt = dev->memory().memory_types().select(req.memoryTypeBits,
            {VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT | VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT},
            VK_MEMORY_PROPERTY_HOST_CACHED_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT);

        VkMemoryAllocateInfo mai{};
        mai.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        mai.memoryTypeIndex = mt;
        mai.allocationSize = req.size;

        mem = dev->allocate_memory(mai);

        dev->bind_memory(buf, mem, 0);

        mem_mmap = dev->map_memory(mem);
        buf_alloc.reset(buf_size);
        flush_offset = 0;
        flush_range = 0;
    }
}
```

### Retiring old buffers

The `allocate_buffer` function is responsible for _both_ creating the initial buffer during initialization _and_ for creating new buffers when reallocation is required. So the first thing it does is see if there's already an active buffer and, if so, retire it.

The first step is to emit a diagnostic so that I know I need to tweak my initial buffer sizes.

Then the buffer is unmapped _but not destroyed_. After all, the caller is probably still putting references to data in the old buffer into descriptor sets or commands. The old buffer is queued up for deferred destruction, which takes place _after_ the current frame in flight has cleared the GPU.

Finally we increase `buf_size` so that the next allocated buffer will be larger. The new size is rounded up to be a multiple of `VkPhysicalDeviceLimits::nonCoherentAtomSize`, because that's required by the API.

### Creating a new buffer

This is very straightforward Vulkan API usage, if a bit hidden behind helper methods.

First we check that the new buffer size is _at least_ large enough to contain the single allocation that's provoked reallocation.

After that it's the standard VK buffer creation flow:
1. Make the buffer.
2. Get any memory type restrictions for the buffer from Vulkan.
3. Pick an appropriate memory type.
  * We _require_ `VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT` and `VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT` (since pushing data in this way is the whole point of `dynamic_buffer`).
  * We _avoid_ `VK_MEMORY_PROPERTY_HOST_CACHED_BIT` and `VK_MEMORY_PROPERTY_HOST_COHERENT_BIT` (since that's overhead which we don't need).
4. Allocate some actual memory.
5. Attach the memory to the new `VkBuffer`.

Finally, we map the new buffer and do some bookkeeping: `buf_alloc` is told the size of the new buffer and the `flush_*` variables are reset.

## Implementing `shutdown`

```cpp
void shutdown()
{
	assert(is_initialized());

	dev->unmap_memory(mem);

	frame_resource_barrier((uint32_t)-1 /* shutdown sentinel */);

	dev->destroy(std::move(buf));
	dev->free(std::move(mem));

	dev = nullptr;
	min_alignment = 0;
}
```

This is also straightforward. The expectation is that the caller has ensured that the device is idle (or at least not holding onto any outstanding references to these resources) before calling `shutdown`. The currently active buffer is unmapped and destroyed, and `frame_resource_barrier` is called with a special sentinel value that causes it to ignore frame boundaries and just clean up _everything_.

## Implementing `frame_resource_barrier`; handling frames-in-flight

```cpp
void frame_resource_barrier(uint32_t frame_index) noexcept
{
	while (!usage_markers.empty())
	{
		auto& m = usage_markers.front();

		switch (m.type)
		{
		case usage_marker_type::fence:
			if (m.fence.frame_index != frame_index && frame_index != (uint32_t)-1)
				//do NOT pop the fence, we'll check it again next time
				goto done_freeing;
			break;

		case usage_marker_type::usage:
			if (m.usage.buffer != buf) [[unlikely]]
				break;
			buf_alloc.free_up_to(std::move(m.usage.buf_mark));
			break;

		case usage_marker_type::free_buffer:
			dev->destroy(buffer_handle{m.free_buffer.buffer, take_ownership});
			dev->free(device_memory_handle{m.free_buffer.memory, take_ownership});
			break;

		NO_DEFAULT_CASE;
		}

		usage_markers.pop();
	}

done_freeing:
	if (frame_index != (uint32_t)-1)
	{
		if (!buf_alloc.empty())
			usage_markers.push({.type = usage_marker_type::usage, .usage = {buf, buf_alloc.current_used_marker()}});
		usage_markers.push({.type = usage_marker_type::fence, .fence={frame_index}});
	}
}
```

Again, pretty straightforward. Entries are popped from the `usage_markers` queue and checked.
* `fence` entries tell the loop when to stop.
* `usage` entries have their payload routed to `buf_alloc`. Note that we check that the buffer is still _current_ and hasn't been reallocated since the usage marker was recorded.
* `free_buffer` cause a now no longer referenced old `VkBuffer` to be destroyed and its underlying memory to be freed.

At the end of the loop we do two things:
* If the buffer isn't empty, grab a usage marker from `buf_alloc` so that we can mark the currently allocated region as free once we're informed that the GPU is done with that data.
* We add a `fence` entry so that the next call to `frame_resource_barrier` knows where to stop.

And, of course, the special "shut it all down" sentinel slightly modifies the logic to suit `shutdown`'s needs.

## Implementing `allocate`

```cpp
block<std::byte> allocate(std::size_t size, std::size_t align = 16)
{
    align = clamp_min(align, min_alignment);

    VkDeviceSize offset, actual_size;
    if (!buf_alloc.try_begin_write(size, offset, actual_size, align)) [[unlikely]]
    {
        flush();
        allocate_buffer(size);
        auto res = buf_alloc.try_begin_write(size, offset, actual_size, align);
        assert(res);
    }

    buf_alloc.end_write(offset, size);

    if (offset < flush_offset)
    {
        flush();
        flush_offset = offset;
    }

    flush_range = (offset + size) - flush_offset;

    assert((offset & (min_alignment - 1)) == 0);

    return {mem_mmap, offset, size, buf};
}
```

We start off by forcing alignment to be _at least_ what the device insists on.

Then we try to allocate that much space in the ring buffer. If we fail, then we need to grow the buffer:
1. First, the old buffer is flushed. We do this because, after we reallocate, we won't be able to go back and flush it later. There might be unflushed data in that buffer if the user is calling `push` over and over with a `no_flush` argument intending to flush the whole lot all at once, so we deal with that here.
2. Then we grow the buffer, making sure it's _at least_ `size` bytes big.
3. After that, we call `try_begin_write` again, and this time it _must_ succeed.

Once we've had a successful call to `try_begin_write` we tell `buf_alloc` that we are _committed_ to using those bytes and that we won't immediately cancel the allocation request.

After that, if we've just wrapped around the end of the buffer we flush any pending data. This doesn't _have_ to be like this, we could be doing the `flush_*` bookkeeping differently so that it could track discontiguous ranges. However, it's easy and it's cheap enough.

Then we do a little bookkeeping and bundle up and return information about the requested memory in a `block<std::byte>`.

The `block` constructors are also pretty simple:

```cpp
template <typename T, std::size_t Extent = dynamic_extent>
struct block : span<T, Extent>, VkDescriptorBufferInfo
{
    //public members in the class overview above

private:
    block(void* mapped_buffer_memory, std::size_t offset, std::size_t size, VkBuffer buffer) noexcept
        requires(std::is_same_v<T, std::byte> && Extent == dynamic_extent)
        : span<std::byte>{(std::byte*)mapped_buffer_memory + offset, size}
        , VkDescriptorBufferInfo{buffer, (VkDeviceSize)offset, (VkDeviceSize)size}
    {
        assert(mapped_buffer_memory);
        assert(buffer);
    }

    block(const block<std::byte>& mem, std::size_t count = Extent) noexcept
        : span<T, Extent>{(T*)mem.data(), count}
        , VkDescriptorBufferInfo{mem}
    {
        assert(count != dynamic_extent);
        assert(block::size_bytes() <= mem.size_bytes());
        assert(mem::is_aligned(mem.offset, mem::align_of<T>) && mem::is_aligned(mem.data(), mem::align_of<T>));
    }

    friend dynamic_buffer;
};
```

The first overload is used by the core `allocate` overload for its return. It does a little bit of pointer math to initialize its `span` base and the rest of the info gets pushed into the `VkDescriptorBufferInfo` variables.

The second overload is used by the functions discussed below to "cast" a `block<std::byte>` to a more strongly typed `block<T>`.

### The other `allocate` overloads

```cpp
template <typename T>
block<T, 1> allocate()
{
    static_assert(std::is_trivially_default_constructible_v<T> && std::is_trivially_destructible_v<T>);
    auto mem = allocate(sizeof(T), alignof(T));
    return {mem};
}

template <typename T>
block<T> allocate_array(std::size_t count)
{
    static_assert(std::is_trivially_default_constructible_v<T> && std::is_trivially_destructible_v<T>);
    auto mem = allocate(sizeof(T) * count, alignof(T));
    return {mem, count};
}
```

Nothing fancy here. We figure out how many bytes we need and how they need to be aligned, ask the core `allocate` overload to do the work for us, and then "cast" the returned block appropriately.

The only _other_ interesting thing is the `static_assert` which prevents the caller from trying to put types that _need_ to have their constructors and destructors called into a memory buffer which won't do either.

## Implementing `push`

```cpp
template <typename T>
block<T, 1> push(no_flush_t, const T& value)
{
    static_assert(std::is_trivially_copyable_v<T>);

    auto ret = allocate<T>();
    std::memcpy(ret.data(), &value, sizeof(T));

    return ret;
}

template <typename T>
block<T> push(no_flush_t, span<const T> values)
{
    static_assert(std::is_trivially_copyable_v<T>);

    auto ret = allocate_array<T>(values.size());
    std::memcpy(ret.data(), values.data(), values.size_bytes());

    return ret;
}
```

These functions just call the matching typed `allocate` overload and then copy the provided data into the buffer. Note that we use `std::memcpy` instead of `T`'s assignment operator. That goes back to the preference for _uncached_ memory, which is best written sequentially and not in whatever random order an assignment operator might move data in. In order to avoid surprising the caller, we have another `static_assert`.

```cpp
template <typename T>
block<T, 1> push(const T& value)
{
    auto ret = push(no_flush, value);
    flush();
    return ret;
}

template <typename T>
block<T> push(span<const T> values)
{
    auto ret = push(no_flush, values);
    flush();
    return ret;
}
```

Is there even anything to say about these overloads?

## Implementing `flush`

```cpp
void flush()
{
	assert(flush_offset <= buf_size && flush_range <= buf_size - flush_offset);

	dev->flush_memory_mapping(mem, {{flush_offset, flush_range}});
	flush_offset += flush_range;
	flush_range = 0;

	assert(flush_offset <= buf_size);
}
```

All we're doing here is taking the range of memory that's been allocated since the last `flush` and passing it to `flush_memory_mapping`, and then adjusting the `flush_*` bookkeeping to note what we did.

`flush_memory_mapping` is _a bit_ more than a thin wrapper around `vkFlushMappedMemoryRanges`. It also adjusts the given offsets so that they're even multiples of `VkPhysicalDeviceLimits::nonCoherentAtomSize`, as is required by the Vulkan specification.