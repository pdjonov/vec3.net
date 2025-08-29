---
title: "Ring Buffers"
date: 2025-07-29
author: phill
permalink: /posts/ring-buffers
tags:
  - code
  - programming
---
Circular buffers (or queues): super useful, but also hard to implement without getting snagged up on little off-by-one errors in the code that tracks the difference between the used and free regions. So here's a quick overview of the algorithm I use for stuff like dynamic vertex and uniform buffers when I'm doing 3D programming.

This class doesn't actually interact with 3D APIs or anything outside itself. All it does is track the indices that mark the start and end of the free regions in a buffer that's being suballocated by something else. This could be vertices and indices being written into a dynamic vertex buffer, it could be the bytes in a uniform buffer, it could be anything.

# Overview

```cpp
template <typename Size = std::size_t>
    requires std::is_unsigned_v<Size>
class ring_buffer_allocator
{
public:
    using size_type = Size;

    explicit ring_buffer_allocator(size_type capacity = 0) noexcept;
    void reset(size_type new_capacity) noexcept;

    [[nodiscard]]
    bool empty() const noexcept;
    [[nodiscard]]
    bool size() const noexcept;
    [[nodiscard]]
    bool capacity() const noexcept;

    bool try_begin_write(size_type min_contiguous_elements, size_type& offset, size_type& size, size_type alignment = 1) noexcept;
    void end_write(size_type offset, size_type size) noexcept;

    class marker;
    [[nodiscard]]
    marker current_used_marker() const noexcept;
    void free_up_to(marker&& m) noexcept;

private:
    //see the implementation notes below
};
```

## Type info and state

```cpp
using size_type = Size;
```

The `Size` template parameter (retrievable with the `size_type` alias) is the unsigned integer type that'll be used to track elements in the underlying buffer. By default it's `std::size_type` since that can address the largest possible object at the byte level, but if you'll be managing lots of small buffers then you can save a bit of memory by taking this down to a smaller uint size.

```cpp
bool empty() const noexcept;
bool size() const noexcept;
bool capacity() const noexcept;
```

These do what you'd expect. `empty` returns true when there's nothing in the buffer, `size` returns the number of elements currently in the buffer, and `capacity` returns the size of the underlying buffer.

## Initialization

```cpp
explicit ring_buffer_allocator(size_type capacity = 0) noexcept;
void reset(size_type new_capacity) noexcept;
```

The constructor and the `reset` method yield an empty allocator. The capacity is simply the number of elements that need to be tracked in the underlying buffer.

## Producing data

Two functions are used to track data being _written_ to the buffer:

```cpp
bool try_begin_write(size_type min_contiguous_size, size_type& offset, size_type& size, size_type alignment = 1) noexcept;
void end_write(size_type offset, size_type size) noexcept;
```

`try_begin_write` will _reserve_ a block of space for _writing_.
* `min_contiguous_size` is the minimum number of elements for which _contiguous_ (in memory) space is required. If the free region wraps around the end of the underlying buffer, the allocator might skip the write cursor past the end of the buffer back to its beginning if there's sufficient space there. Otherwise, if there simply isn't enough contiguous free space available, the function will fail and return `false`.
* `offset` will, when the function returns `true`, hold the buffer offset where the new data should be written.
* `size` will, when the function returns `true`, hold the amount of contiguous space that's available starting at `offset`. This value _may_ be larger than what was requested in `min_contiguous_size`.
* `alignment` is the required alignment of the returned `offset` value. This is most useful when the allocator is tracking _bytes_ in an unstructured buffer rather than _elements_ in an array.

`end_write`'s job is to record the amount of data _actually_ written after a successful call to `try_begin_write`.
* `offset` must be the `offset` value returned by `try_begin_write`.
* `size` is the number of elements _actually_ written. It must be no larger than the `size` returned by `try_begin_write` (it _may_ be smaller than the requested `min_contiguous_size`).

The basic use of this part of the API looks like this:

```cpp
size_type size = /* however many contiguous need to be added */;

size_type offset, actual_size;
if (!allocator.try_begin_write(size, offset, actual_size))
{
    //The underlying buffer hasn't got spare capacity to meet this request.
    //Reallocate it, reset the allocator, and call try_begin_write again.
}

for (size_type i = 0; i < size; i++)
    underlying_buffer[offset + i] = /* some value */;

buf_alloc.end_write(offset, size);
```

Note that it would also be valid to call `end_write` _before_ the loop that writes the data. The allocator does not, after all, effect the underlying buffer storage in any way - it just tracks used and free regions. Using it to indicate that you're _committed_ to writing `size` elements at `offset` is as valid as using it to indicate that you've _already_ written those elements.

And the reason there's a distinction between `size` and `actual_size` is that, for some uses, it's possible that the caller might be _able_ to split the data being written across the wrapping point from the end of the buffer back to the beginning, or even across a buffer reallocation (this is common with dynamic vertex buffers), but that it's undesirable to do so when it isn't necessary. For these uses, you call `try_begin_write` with the _smallest_ number of elements that have to be written before a split, and the returned `actual_size` will tell you if you actually _need_ to split the data.

## Consuming data

The allocator is designed to interface with consumers that run asynchronously and only occasionally report progress back to the producer. The API looks like this:

```cpp
class marker
{
public:
    marker() noexcept = default;

private:
    //see implementation notes below
};

marker current_used_marker() const noexcept;
void free_up_to(marker&& m) noexcept;
```

The `marker` class is an opaque data block that tracks the _current_ state of the _non-free_ region in the buffer. `current_used_marker` returns this value. `free_up_to` marks the part of the _non-free_ region _up to_ the given marker as now free.

The way this is used is as follows:
* Write data to the buffer (and track the writes) as outlined [above](/posts/ring-buffers#producing-data).
* At the end of a frame (or whatever you call the sync point between the producer and consumer), call `current_used_marker` and store the returned value somewhere.
* When you receive the notification that the consumer has _finished_ reading the data for a frame, take _that_ frame's stored marker and pass it to `free_up_to`.

In my case, I'm feeding data to a GPU through Vulkan. So the "frame" is an actual rendered frame and the notification I'm interested in is a frame-in-flight's `VkFence` becoming signaled.

And the final note: markers must be passed to `free_up_to` no more than once, and in the order they were created. That's why `free_up_to` takes its argument as an rvalue - having to type `std::move` is a way to remind the caller of that rule.

# Implementation

The class implementation follows:

## Allocator state

```cpp
template <typename Size = std::size_t>
    requires std::is_unsigned_v<Size>
class ring_buffer_allocator
{
public:
    //see above

private:
    size_type buffer_size;
    size_type free_size;

    size_type free_cursor;
    size_type used_cursor;
};
```

The allocator works in _elements_ of the underlying buffer. These could be indices in an array, they could be bytes.
* `buffer_size` is just the size of the underlying buffer.
* `free_size` is the number of _unused_ elements.
* `free_cursor` tracks where the next bit of data can be written.
* `used_cursor` tracks where the _consumer_ will pull _its_ next bit of data _from_.

The way `free_cursor` and `used_cursor` should be interpreted follows:
* If `used_cursor < free_cursor` then:
  * The _used_ region is contiguous and it extends _from_ `used_cursor` and _up to_ `free_cursor`.
  * The _free_ region is broken up, extending from `free_cursor` to the end of the buffer and then from the start of the buffer up to `used_cursor`.
* If `free_cursor < used_cursor` then the situation is reversed:
  * The _used_ region is split, extending from `used_cursor` to the end of the buffer and then from the start of the buffer up to `free_cursor`.
  * The _free_ region is contiguous, extending from `free_cursor` up to `used_cursor`.
* If `free_cursor == used_cursor` then one of the following will be true:
  * The buffer may be _empty_, and `free_size == buffer_size`.
  * The buffer may be _full_, and `free_size == 0`.

The state-querying functions are then perfectly straightforward to implement:
```cpp
bool empty() const noexcept { return free_size == buffer_size; }
size_type size() const noexcept { return buffer_size - free_size; }
size_type capacity() const noexcept { return buffer_size; }
```

## Implementing initialization

This is also very straightforward.

```cpp
explicit ring_buffer_allocator(size_type capacity = 0) noexcept
{
    reset(capacity);
}

void reset(size_type new_capacity) noexcept
{
    buffer_size = free_size = new_capacity;
    free_cursor = used_cursor = 0;
}
```

## Implementing the producer API

Here's where things get gnarly...

```cpp
bool try_begin_write(size_type min_contiguous_size, size_type& offset, size_type& size, size_type alignment = 1) noexcept
{
    assert(min_contiguous_size <= buffer_size);
    assert(is_pow2(alignment));

    if (min_contiguous_size > free_size)
        //The buffer hasn't got enough free space.
        return false;

    if (used_cursor < free_cursor)
    {
        //The free region is split, wrapping around the end of the buffer.

        if (min_contiguous_size <= buffer_size - align_up(free_cursor, alignment))
        {
            //We've got enough space to fit the new data
            //right at the start of the free region.

            offset = align_up(free_cursor, alignment);
            size = buffer_size - offset;
        }
        else if (min_contiguous_size <= used_cursor)
        {
            //We can't fit the new data at the start of the free region,
            //but if we skip that part of the free region then we *can*
            //fit the new data in the free space at the start of the buffer.

            offset = 0; //no alignment required at offset = 0
            size = used_cursor;
        }
        else
        {
            //The buffer has enough free space, but it's not contiguous
            //and we can't make the minimum required contiguous allocation.

            return false;
        }
    }
    else if (free_cursor < used_cursor)
    {
        //The free region is contiguous.

        offset = align_up(free_cursor, alignment);
        size = used_cursor - offset;

        //We checked above that we have enough space for the allocation,
        //but we didn't check that we have enough space for the *aligned*
        //allocation. We check for that here:
        if (size < min_contiguous_size)
            return false;
    }
    else
    {
        //The buffer is either empty or full.
        //The only way we can get here with a full buffer is if min_contiguous_size == 0.
        //That's silly, but no reason not to support it.

        if (free_size == buffer_size)
            //The buffer is empty. Start allocating over from the beginning.
            free_cursor = used_cursor = 0;
        else
            assert(min_contiguous_size == 0);

        offset = free_cursor;
        size = free_size;
    }

    return true;
}

void end_write(size_type offset, size_type size) noexcept
{
    if (!size)
      //If nothing was written, then nothing needs to be tracked.
      //Calling end_write with size==0 means "forget the last try_begin_write".
      return;

    if (offset < free_cursor)
    {
        assert(offset == 0);

        //The free region is split, and there wasn't enough room in the fragment
        //at the end of the buffer to fit the new allocation in the region at the
        //beginning of the buffer.

        //Don't forget to count the elements we skipped to get to a contiguous block!
        free_size -= buffer_size - free_cursor;

        free_cursor = offset + size;
        free_size -= free_cursor;
    }
    else
    {
        //This is a normal allocation at the start of the free region.
        //Take care to track of space that was skipped to meet alignment requirements.

        auto old_free_cursor = free_cursor;
        free_cursor = offset + size;
        free_size -= free_cursor - old_free_cursor;
    }

    if (free_cursor == buffer_size)
        //Wrap the free cursor around to the start of the buffer
        free_cursor = 0;
}
```

## Implementing the consumer API

This bit is, fortunately, much simpler.

```cpp
class marker
{
public:
    marker() noexcept = default;

private:
    marker(const dynamic_buffer_allocator& buf) noexcept
        : free_cursor{buf.free_cursor}
    {
        if (buf.free_size == buf.buffer_size)
            free_cursor = (size_type)-1;
    }
    size_type free_cursor = (size_type)-1;
    friend ring_buffer_allocator;
};

marker current_used_marker() const noexcept { return {*this}; }

void free_up_to(marker&& m) noexcept
{
    if (m.free_cursor == (size_type)-1)
        //If the marker was default-constructed or if it was made
        //when the buffer was empty then there's nothing to do.
        return;

    //Don't accept a marker that falls outside the used region.
    assert(!(used_cursor < free_cursor) ||
        (m.free_cursor >= used_cursor && m.free_cursor <= free_cursor));
    assert(!(free_cursor < used_cursor) ||
        (m.free_cursor >= used_cursor || m.free_cursor <= free_cursor));

    //If the allocator is currently empty, then it must have been empty
    //when the marker was created (and applying the marker should be a no-op).
    assert(!(used_cursor == free_cursor && free_size == buffer_size) ||
        (m.free_cursor == used_cursor));

    size_type freed_size;
    if (m.free_cursor != used_cursor)
    {
        freed_size = m.free_cursor > used_cursor ?
            m.free_cursor - used_cursor :
            (buffer_size - used_cursor) + m.free_cursor;
    }
    else
    {
        //We're either advancing used_cursor all the way around
        //the buffer back to where it is (thus freeing the whole buffer),
        //or we're not moving it at all.

        freed_size = free_size == 0 ?
            buffer_size :
            0;
    }

    assert(freed_size <= buffer_size - free_size);
    free_size += freed_size;
    used_cursor = m.free_cursor;

    //The marker has been consumed, erase its value.
    m.free_cursor = (size_type)-1;

    assert(!buffer_size || free_cursor != buffer_size);
    assert(!buffer_size || used_cursor != buffer_size);
    assert(free_size <= buffer_size);
}
```

Alright, this is subtle.

A refresher:
* `used_cursor` points to the beginning of the used block (which is also the end of the free block).
* `free_cursor` points to the end of the used block (which is also the beginning of the free block).

What `free_up_to` must do is advance `used_cursor` to where the _end_ of the used block was at the time when `current_used_marker` was called. So `current_used_marker` needs to snapshot the value of `free_cursor`, and then `free_up_to` advances `used_cursor` and increments `free_size` by the number of elements that that caused to become free.

The subtlety is in the case when `m.free_cursor == used_cursor`. That means that the free region _used_ to start where the used region currently begins. And since the beginning of the used region only moves when `free_up_to` is called, that means one of the following:
* The buffer was full when `current_used_marker` was called, and it still is. In this case, we free the entire buffer.
* Two markers in a row were created with no new allocations between them, and this is the second one being passed to `free_up_to` after the first. In this case, we do nothing since the first of the two markers already freed what needed to be freed.