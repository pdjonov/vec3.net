---
title: "How Cache Impacts Software"
series: memory-and-caching
tags:
  - hardware
  - performance
---

Picking up right where the previous post left off...

<!-- blurb -->
Knowing that main memory is _slow_ (relative to what otherwise goes on in the CPU), software can do a few things to run faster. These things all boil down to just "work with the CPU's cache and don't _ever_ fight it".
<!-- blurb -->

First, software can keep variables which are accessed together near to one another in memory. That way, when the first variable is accessed the program will pay the slow-RAM price _once_ to fetch not only that variable but also its neighbors into cache. When the _next_ variable is accessed, the cost of touching RAM doesn't have to be paid _again_.

Second, software can avoid deep chains of pointers-to-pointers. These aren't _so_ bad if the first level of indirection points to something which is frequently accessed and thus likely to stay resident in cache. But CPUs don't actually robotically execute instructions in the order in which they're given. They do peek ahead. If it sees two memory reads back to back, it can schedule the long, slow process of fetching each block of data from RAM in parallel. But if the second memory address is _contained_ (or computed using data which is contained) in the data fetched by the first one, then the CPU can't do any of that parallelization.

Finally, software can just use less memory. Spending a few extra CPU cycles doing some bitshifts to pack data more densely can _easily_ save hundreds of cycles waiting for trips out to main RAM and back. Remember: the CPU is small, compact, hyper-optimized, and _blazingly_ fast compared to RAM.

In _very niche_ cases, a program might even want to use special memory mapping flags to _disable_ caching for some region of memory, or there're also [special instructions](https://stackoverflow.com/a/37092) which skip cache altogether. This is usually done when a program needs to write a big block of data which won't be read again any time "soon" in order to avoid pushing more immediately useful data out of cache to make space for the writes. But this sort of thing requires special attention to how that memory is accessed: it's good for _contiguous, sequential writes_ but it is _terrible_ for absolutely anything else.

# How this impacts multithreading

The fact that the CPU has its own copy of _some_ of the data in RAM means that there's potential for the CPU's copy of the data to disagree with the contents of main RAM. For instance, the CPU might have written data to its cached copy of some byte but not yet bothered to send an update to RAM.

If there's only _one_ CPU core, then there's no problem. It'll always check its own copy of any given byte before it spends time talking to main RAM, so the programs running on that CPU won't be aware of any discrepency.

If there are _multiple_ CPU cores, then things start to get ..._interesting_. In practice, different CPU architectures use different strategies to deal with this:

## Automagic consistency

This is the approach that the x86 family of CPUs uses: the CPU cores simply do _whatever is necessary_ to keep a _coherent_ view of memory. If that means the cores have to spend time sniffing around in one another's caches, so be it.

For a software developer, this is convenient because it's cognitively the simplest model from the perspective of ensuring program _correctness_. The code does stuff and the CPU does the work of making that stuff behave reasonably.

The exception is the case where multiple cores need to simultaneously access data which close together in RAM. In this case, they'll all run more slowly than expected _precisely because_ the CPU will constantly be doing extra work synchronizing the two cores' memory accesses (where they would otherwise operate in parallel). This is why highly contested `std::atomic` objects are sometimes explicitly padded and kept [a cache line's width](https://en.cppreference.com/w/cpp/thread/hardware_destructive_interference_size.html) apart from one another.

## Explicit consistency

Other CPUs, like the ARM family, require programs to _explicitly_ request consistency in those places where they need it.

This is what [memory ordering](https://en.cppreference.com/w/cpp/atomic/memory_order.html) is all about, or rather what `acquire` and `release` semantics are for. When an atomic variable is loaded with `acquire` semantics, that means the CPU should synchronize its caches _up to_ the last time the _same_ atomic was written to with `release` semantics. The C++ compiler takes care of emitting whatever special instructions or annotations are required to tell the CPU to do just that. And special synchronization objects like mutexes and semaphores also contain the necessary special instruction sequences to do a cache sync at the appropriate times as different cores interact with them.

Absent those special instructions, a multithreaded program on one of these platforms can absolutely observe inconsistencies between the values stored in the caches of different cores. Usually, this causes the program to then corrupt a bunch of data or (if the user's lucky) simply crash.