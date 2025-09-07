---
title: "Vulkan Memory Types"
series: memory-and-caching
tags:
  - hardware
  - performance
  - graphics
  - Vulkan
---

Picking up the discussion on memory and caching: how does this all interact with an _external_ processor such as a GPU? GPUs are an intersting addition to this discussion because their operation is _very_ memory-bound and _very very_ multi-core. They also run alongside CPUs. This makes things ..._complicated_.

A quick overview:

* GPUs _usually_ have lots and lots of their own main RAM, which we call VRAM (for _video_-RAM).
* GPUs can _usually_ read data from main RAM as well. This is generally a little slower than VRAM since the accesses have to be coordinated such that they don't overlap with the CPU's.
* CPUs may or may not be able to directly access VRAM (again, because this shared access to the resource poses a coordination problem). Often a CPU will be limited to just a small piece of VRAM which is controlled by the driver and used to transfer instructions to the GPU which then accesses other memory on the CPU's behalf.
* But this isn't true of all GPUs - some of them, like mobile and integrated GPUs, have no VRAM and just share main RAM with a CPU.
* In addition to their internal caches, GPUs can have _special_ regions of _extremely fast_ (and, like CPU cache: power-hungry, hot, and thus _limited_) memory.
* The CPU and GPU might share not only main RAM, they might share a CPU package. They might sit _right next to one another_ in _one_ chip and have super secret best-friends-only special handshakes that they can do with one another to make coordinating over memory access _fast_.
* GPUs may or may not virtualize their memory. (Modern ones generally _do_, for robustness and security reasons.)
* There might be _rules_ about certain types of resources having to go in certain parts of memory, or about having to havethat memory mapped a certain way.

And this list could go on...

# How do we deal with this?

The old answer to this question was "we don't, the graphics driver is made of tiny demons who figure it all out for us". But, as it turned out, tiny demons require special care and feeding (and sometimes they have to be _paid!_), so let's look at the current state of things.

Modern APIs like Vulkan expose this stuff to applications as a set of different _memory types._

A memory type contains three bits of information:

* The memory region, or _heap_, where it can be allocated.
* Information about how the application is allowed to interact with this memory.
* Secret configuration flags that only the graphics driver knows about. These might effect the resource types that the memory type is compatible with, and that's all that concerns the application about this stuff.

## The Vulkan memory API

In Vulanese, that's all wrapped up in [this](https://registry.khronos.org/vulkan/specs/latest/man/html/VkMemoryType.html):

```cpp
typedef struct VkMemoryType {
    VkMemoryPropertyFlags    propertyFlags;
    uint32_t                 heapIndex;
} VkMemoryType;
```

`heapIndex` tells us which heap this is associated with. `propertyFlags` tell us the memory's overall performance characteristics and how the CPU may _and may not_ interact with it.

## Memory type flags

These tell us (well, these _strongly suggest_) the location of the memory:

* `DEVICE_LOCAL` means that the GPU can _efficiently_ access this memory. If this bit is missing, then the GPU's access to this memory might be _slow(er)_, and thus the memory type is only suitable for small bits of read-_once_ configuration data (like, perhaps, a command stream).
* `HOST_VISIBLE` means that the CPU can access this memory.
  * If it's paired with `DEVICE_LOCAL`, that usually indicates that the CPU and GPU can deconflict access to some region of VRAM and that the CPU is therefore allowed to map and access it directly.
  * If it's present but `DEVICE_LOCAL` is absent, then this is probably part of system RAM which the GPU can nevertheless access (if more slowly.)
  * If it's absent, then that's probably a part of VRAM which the CPU is _not_ allowed to touch because the cost of deconflicting external CPU access from internal GPU access to (that part of) VRAM is just too expensive.
* `LAZILY_ALLOCATED` is interesting. It represents those special small regions of ultrafast VRAM mentioned above. Those generally have to be carefully managed by the graphics driver, so this flag is incompatible with `HOST_VISIBLE`, the CPU is _not_ allowed to directly touch it. Assume the driver is deploying tiny demons to do magic on your behalf. (Okay, fine, I'll be serious: this has to do with how framebuffers are allocated on a GPU which uses a [tiled rendering](https://en.wikipedia.org/wiki/Tiled_rendering) approach, and these approaches require the aforementioned special small fast memories.)

## Caching flags

The rest of the flags have to do with how this memory will interact with the CPU's memory caches:

* `HOST_CACHED` means that this memory works like normal RAM as far as the CPU's memory cache is concerned.

  If this flag is _missing_, then the CPU must access this memory contiguously and sequentially, because any other access pattern is going to hurt _bad_, and _reading_ from this memory will hurt _extra bad_. No cache means each CPU instruction which touches memory is exposed to the full cost of dealing with the slowness of (V)RAM.
* `HOST_COHERENT` means that the CPU and GPU have a secret special handshake they can do to make sure they don't trip up on differences between the version of some data stored in the CPU cache and the underlying RAM.

  If this flag is missing then the application must _explicitly_ ask the CPU and GPU to shake hands using [`vkFlushMappedMemoryRanges`](https://registry.khronos.org/vulkan/specs/latest/man/html/vkFlushMappedMemoryRanges.html) after sending data to the GPU and [`vkInvalidateMappedMemoryRanges`](vkInvalidateMappedMemoryRanges) before reciving data. Obviously don't call these functions one byte (well, one cache line) at a time: that's slow and _bad_. And ideal use case is writing an entire buffer full of uniform data and then flushing the whole thing at once for the whoe frame before submitting commands which will read that data.

  If this flag is present, thenthose functions don't need to be called, but they don't hurt much if called regardless.

Sometimes there will be only one option in this regard. Sometimes drivers will offer multiple options, and the software chooses whichever it prefers. In such a case:

* When _writing_ data _contiguously_ and _sequentially_ and doing _no reading whatsoever:_ avoid `HOST_CACHED`. In all other cases, _insist_ on `HOST_CACHED` (and be prepared for things to run _sloooow_ if it isn't available).
* Whenever it's not burdensome to appropriately call `vkFlushMappedMemoryRanges` and `vkInvalidateMappedMemoryRanges`, avoid `HOST_COHERENT`. If you _can't_ call those functions then _insisit_ on `HOST_COHERENT`.

## The secret sauce

As mentioned, a memory type may also contain _hidden_ data. This manifests to applications as a cluster of memory types which share the same `heapIndex` and have exactly the same `propertyFlags` but which report _different_ compatibility with Vulkan's resource types, as reported by functions like [`vkGetBufferMemoryRequirements`](https://registry.khronos.org/vulkan/specs/latest/man/html/vkGetBufferMemoryRequirements.html) or [`vkGetImageMemoryRequirements`](https://registry.khronos.org/vulkan/specs/latest/man/html/vkGetImageMemoryRequirements.html) (specifically the `memoryTypeBits` field in their output structures).