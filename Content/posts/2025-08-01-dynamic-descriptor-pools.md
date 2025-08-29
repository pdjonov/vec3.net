---
title: "Dynamic Descriptor Pools"
tags:
  - code
  - programming
  - graphics
  - Vulkan
---
A couple days ago I wrote about my [`dynamic_buffer`](/posts/dynamic-graphics-buffers) helper which I use to push things like uniform blocks to the GPU without worrying _too_ much about preallocating the exact amount of memory I need at application startup. Here's another helper which I use to make allocating descriptor sets easy.

This one's also for [Vulkan](https://www.vulkan.org/). I'm not familiar enough with D3D12 or Metal to say if anything here would translate well to those APIs, but if it does, you're welcome.

# Overview

A `descriptor_pool` is a pool of `VkDescriptorPool`s, which are presented as if they're all just one.

It works by allocating a `VkDescriptorPool` and using it to satisfy requests for new descriptor sets until it is exhausted. Once exhausted, the pool goes into a queue where it waits for the last Frame in Flight that referenced it to be retired by the GPU, after which it is reset and reused. In the meantime, _another_ `VkDescriptorPool` is allocated to satisfy requests until the previous one is ready for reuse.

Like the `dynamic_buffer`, this class incrementally allocates as Vulkan resources until it has enough to meet the application's demands, and then resource usage stabilizes.

Note that this class _requires_ a Vulkan 1.1 device or, at the very least, a 1.0 device with the `VK_KHR_maintenance1` extension enabled.

```cpp
class descriptor_pool
{
public:
    void initialize(device& dev, const VkDescriptorPoolCreateInfo& ci);
    void initialize(device& dev, uint32_t max_sets, span<const VkDescriptorPoolSize> sizes, VkDescriptorPoolCreateFlags flags = 0);
    void shutdown();

    bool is_initialized() const noexcept;

    void frame_resource_barrier(uint32_t frame_index) noexcept;

    vk_handle<VkDescriptorSet> allocate(VkDescriptorSetLayout layout);

private:
    //see implementation details below
};
```

## Initialization

Similar to [`dynamic_buffer`'s initialization](/posts/dynamic-graphics-buffers#initialization), `descriptor_pool` doesn't use RAII principles to manage the underlying Vulkan resources. There are explicit `initialize` and `shutdown` methods.

```cpp
void initialize(device& dev, const VkDescriptorPoolCreateInfo& ci);
void initialize(device& dev, uint32_t max_sets, span<const VkDescriptorPoolSize> sizes, VkDescriptorPoolCreateFlags flags = 0);
void shutdown();
```

The `initialize` function needs to be told what each underlying individual `VkDescriptorPool` should be able to contain. This must be large enough to hold _at least one_ of any `VkDescriptorSetLayout` you'll allocate a descriptor set for (and ideally much more than that).

Usage looks something like this:
```cpp
descriptor_pool.initialize(vk_dev, 256, {
    {VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER, 256},
    {VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, 128}});
```

`shutdown` must not be called while the GPU still has any outstanding frames-in-flight.

## Tracking what the GPU is using

```cpp
void frame_resource_barrier(uint32_t frame_index) noexcept;
```

Again, this is just like [`dynamic_buffer`'s API](/posts/dynamic-graphics-buffers#tracking-what-the-gpu-is-using). Every time the application begins rendering a new _frame in flight_, the `frame_resource_barrier` function needs to be called with that FiF's index.

That looks a bit like this:
```cpp
fif_data& begin_frame_in_flight()
{
    auto& fif = frames_in_flight[frame_index];

    vk_dev.wait_for_and_reset_fence(fif.done_fence);

    vk_dev.reset(fif.command_pool);
    my_dynamic_buffer.frame_resource_barrier(frame_index);
    my_descriptor_pool.frame_resource_barrier(frame_index);

    return fif;
}
```

## Allocating descriptor sets

```cpp
vk_handle<VkDescriptorSet> allocate(VkDescriptorSetLayout layout);
```

This is probably the simplest function of them all. You ask for a descriptor set, and you get one. The caller then _owns_ the returned handle, so it's wrapped in the little `vk_handle` template which signifies this.

# Implementation

The class implementation follows:

```cpp
class descriptor_pool
{
public:
    //see above

private:
    device* dev = nullptr;

    vk_handle<VkDescriptorPool> descriptor_pool;
    std::uint32_t remaining_sets_in_pool;
    inline_vector<vk_handle<VkDescriptorPool>, 8> empty_descriptor_pools;

    enum class usage_marker_type
    {
        fence,
        reset_descriptor_pool,
    };

    struct usage_marker
    {
        usage_marker_type type;

        struct fence_
        {
            uint32_t frame_index;
        };

        struct reset_descriptor_pool_
        {
            VkDescriptorPool pool;
        };

        union
        {
            struct fence_ fence;
            struct reset_descriptor_pool_ reset_descriptor_pool;
        };
    };

    queue<usage_marker> usage_markers;

    VkDescriptorPoolCreateFlags create_info_flags;
    uint32_t create_info_max_sets;
    inline_vector<VkDescriptorPoolSize, 8> create_info_sizes;

    vk_handle<VkDescriptorPool> create_descriptor_pool() const;
};
```

This is similar in structure to `dynamic_buffer`.
* `descriptor_pool` tracks the pool we're currently allocating sets out of.
* `empty_descriptor_pools` are pools which _were_ exhausted but which, having cleared the GPU, are now available for reuse.
* `usage_markers` is the guts of the `frame_resource_barrier` function.
* `create_info` and `create_info_sizes` store the parameters given to `initialize`, and are used to allocate additional `VkDescriptorPool`s when necessary.

`inline_vector` is basically `std::vector`, except that as long as its size does not exceed the given size (in this case, 8) the elements are stored inline without the need for a heap allocation. You don't have to make your own `inline_vector` class to build something like this, but if you've got it, this is an ideal place to use it.

## Implementing initialization

```cpp
void initialize(device& dev, const VkDescriptorPoolCreateInfo& ci)
{
    assert(dev.is_initialized());
    assert(
        dev.api().api_version >= VK_MAKE_API_VERSION(0, 1, 1, 0) ||
        dev.api().has_VK_KHR_maintenance1);
    assert(ci.sType == VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO);
    assert(ci.pNext == nullptr);
    assert(ci.maxSets && ci.poolSizeCount && ci.pPoolSizes);

    assert(!is_initialized());

    create_info_flags = ci.flags;
    create_info_max_sets = ci.maxSets;
    create_info_sizes.assign(ci.pPoolSizes, ci.pPoolSizes + ci.poolSizeCount);

    descriptor_pool::dev = &dev;

    descriptor_pool = create_descriptor_pool();
    remaining_sets_in_pool = ci.maxSets;

    empty_descriptor_pools.push_back(create_descriptor_pool());
}

void initialize(device& dev, uint32_t max_sets, span<const VkDescriptorPoolSize> sizes, VkDescriptorPoolCreateFlags flags = 0)
{
    assert(sizes.size() < UINT32_MAX);

    VkDescriptorPoolCreateInfo ci{};
    ci.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;

    ci.flags = flags;
    ci.maxSets = max_sets;
    ci.poolSizeCount = (uint32_t)sizes.size();
    ci.pPoolSizes = sizes.data();

    initialize(dev, ci);
}

bool is_initialized() const noexcept { return dev != nullptr; }
```

This is fairly straightforward.

First we assert that we aren't being given invalid data or data which we're not equipped to handle (for instance, if `VkDescriptorPoolCreateInfo::pNext` is non-null, we can't deal with that because we don't have a spot in our `create_info*` members to store arbitrary extension parameters).

After that we store the given parameters in `create_info` and `create_info_sizes`.

Finally, we create the first pool we're going to allocate from and a second pool to take over for it after it becomes exhausted.

## Implementing descriptor pool creation

```cpp
vk_handle<VkDescriptorPool> create_descriptor_pool() const
{
    VkDescriptorPoolCreateInfo ci{};
    ci.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;

    ci.flags = create_info_flags;
    ci.maxSets = create_info_max_sets;
    ci.poolSizeCount = (uint32_t)create_info_sizes.size();
    ci.pPoolSizes = create_info_sizes.data();

    auto ret = dev->create_descriptor_pool(ci);
    dev->set_object_debug_name(ret, u8"descriptor_pool");
    return ret;
}
```

Also very straightforward. We just take our `create_info_*` variables, pack them up the way Vulkan wants them, and call `vkCreateDescriptorPool`. Easy.

## Implementing shutdown

```cpp
void shutdown()
{
    assert(is_initialized());

    frame_resource_barrier((uint32_t)-1 /* shutdown sentinel */);

    for (auto& p : empty_descriptor_pools)
        dev->destroy(std::move(p));
    empty_descriptor_pools.clear();

    dev->destroy(std::move(descriptor_pool));

    descriptor_pool::dev = nullptr;
    }
```

Nothing fancy here. We _expect_ the caller to have already put the device in an idle state or, at the very least, to have taken care to wait until the GPU is no longer touching any of the pool's resources. Given that there's little to do except just destroy all the things.

The only thing to note here is that calling `frame_resource_barrier` with the shutdown sentinel causes it to dump any descriptor pool references still being held in the `usage_markers` queue into the `empty_descriptor_pools` vector so that they can be cleared.

## Implementing `frame_resource_barrier`; handling frames-in-flight

Again, _very_ similar to [`dynamic_buffer`'s implementation](/posts/dynamic-graphics-buffers#implementing-frame_resource_barrier-handling-frames-in-flight).

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

        case usage_marker_type::reset_descriptor_pool:
            dev->reset(m.reset_descriptor_pool.pool);
            empty_descriptor_pools.push_back({m.reset_descriptor_pool.pool, take_ownership});
            break;

        NO_DEFAULT_CASE;
        }

        usage_markers.pop();
    }

    done_freeing:
    if (frame_index != (uint32_t)-1)
        usage_markers.push({.type = usage_marker_type::fence, .fence={frame_index}});
    }
```

We start popping entries from `usage_markers`.
* `fence` entries tell the loop when to stop.
* `reset_descriptor_pool` entries tell us when it's safe to reset and reuse a `VkDescriptorPool`.

At the end of the loop we just push a `fence` entry so that subsequent calls to `frame_resource_barrier` know when to stop.

And, of course, the special "shut it all down" sentinel slightly modifies the logic to suit `shutdown`'s needs.

## Implementing allocate

And finally, the whole point of this class, the `allocate` method:

```cpp
vk_handle<VkDescriptorSet> allocate(VkDescriptorSetLayout layout)
{
    assert(layout);

    assert(is_initialized());

    if (remaining_sets_in_pool) [[likely]]
    {
        remaining_sets_in_pool--;
        if (auto ret = dev->try_allocate_descriptor_set(descriptor_pool, layout)) [[likely]]
            return ret;
    }

    //this pool's out of juice, put it in the recycling bin
    usage_markers.push({
        .type = usage_marker_type::reset_descriptor_pool,
        .reset_descriptor_pool = {descriptor_pool.release()}});

    if (!empty_descriptor_pools.empty())
    {
        descriptor_pool = std::move(empty_descriptor_pools.back());
        empty_descriptor_pools.pop_back();
        remaining_sets_in_pool = create_info_max_sets;
    }
    else
    {
        debug_print("descriptor_pool: allocating an additional set\n");
        descriptor_pool = create_descriptor_pool();
        remaining_sets_in_pool = create_info_max_sets;
    }

    auto ret = dev->allocate_descriptor_set(descriptor_pool, layout);
    assert_is(ret);
    return ret;
}
```

The first thing we do is check `remaining_ssets_pool`. Why? Well, while `vkAllocateDescriptorSets` is _supposed_ to return `VK_ERROR_OUT_OF_POOL_MEMORY` when the pool is exhausted, _some_ vendors (_\*cough\*_ NVIDIA _\*cough\*_) are in the habit of _not_ doing that and simply growing the pool silently behind your back. If we didn't do our own check, then we would _never_ recycle our `VkDescriptorPool` and it would represent a silent memory leak.

_After_ we check that, we try to allocate a descriptor set matching the requested layout. My `device::try_allocate_descriptor_set` wrapper is simple: if it fails (without throwing) that means we got a `VK_ERROR_OUT_OF_POOL_MEMORY` because, while the pool still has space for more descriptor _sets_, it _doesn't_ have space left for the _descriptors_ we're asking to have in our set.

If we successfully allocated a set, we're done.

Once a pool is exhausted, we stick it in the `usage_markers` queue for recycling after we're notified that the current FiF is done. Then we need a new set. If we have a recycled one waiting in `empty_descriptor_pools` then we use it, otherwise we create a brand new one. Either way, we reset the `remaining_sets_in_pool` counter.

Then, finally, with a _fresh_ pool, allocation will succeed (unless the caller is doing something invalid like asking for a `VkDescriptorSet` that's too big to fit into a single one of our `VkDescriptorPool`s.)