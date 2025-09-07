---
title: "Memory and Physics"
series: memory-and-caching
seriestitle: "Memory and Caching"
tags:
  - hardware
  - performance
---

Computers have both a superpower and a problem: they are _physical_ objects. And what the laws of physics give in terms of the massive potential to do work in parallel, they take away in the speed-of-light limit and heat. All of this has to be engineered around.

I'm writing this for an audience of programmers less experienced with low-level systems concepts: both junior devs and more senior devs who've spent their careers in higher-level code. Basically, I just want a link that I can share the next time I need to explain this stuff beginning from first principles. If this is all old news to you, skip on ahead to another section.

Let's consider RAM. Consider a 16GB stick of DRAM. That has $16 \times 2^{30} \times 8$ (which is $2^{37}$) _bits_ of storage space. And each of those bits needs its own _physical_ cluster of atoms to store its value. Now, atoms are tiny, so that's not actually all that much material, but we also need a way to route signals between _each_ of those bits and the CPU's working silicon. That's _a lot_ of (tiny) wires.

# The problem of space

The first problem physics presents is that physical things take up physical space, and that _finding_ a physical thing in physical space is not, in fact, a simple problem. Ever lost your keys in a small apartment, or just around your desk at work? Now go try to find them in an entire greater metro region.

## An analogy to cities

Hardware designers solve this problem the same way city planners solve it. In a city, there are many _rooms_, and people might need to go from any one of those rooms to any other. But nobody would try to build a path from each individual room to every possible other room in the region. That would be impossible - the paths _wouldn't fit_. Worse, they'd intersect one another in so many places that there would be constant gridlock.

Instead, the city connects its spaces like this:
* Rooms are _clustered_ into _buildings_ (or into apartments which are themselves clustered into buildings). This _dramatically_ reduces the number discrete entities that a city planner has to worry about.
* The clustering is applied _recursively_. Buildings are organized into _blocks_, blocks into _neighborhoods_, etc. At each level, the "connect everything to everything else" problem deals with a small,  and thus manageable number of entities.
* Protocols exist which allow the people of the city to smoothly _share_ the paths between each level of clustering. This lets people travel around without bumping into one another (_too_ much).
* And finally, we don't have to scale our path-sharing protocols beyond their natural capacity. If a connection has _too much_ trafic on it, we augment it by adding _other_ modes of transit like busses, metros, long-distance railways, etc.

This also influences how we _address_ space within the city.
* First, "room" isn't part of the address, it's just implied that people know how to navigate houses or apartments. If you'regoing to visit your friend, you'll probably hang out in the living room, but your maps app doesn't care about that. It'll get you to the door and you figure out the rest yourself.
* A city's buildings aren't all just given a unique number (though that _could_ be done - logically nothing prevents it). Instead we cluster the _address space_ by streets, regions, so on. This reduces the problem of "find one building among many in this metropolis" to a series of _smaller_ problems: find the correct suburb, then find the correct street within that, then find the building on that street. (And [post codes](https://www.youtube.com/watch?v=1K5oDtVAYzk) take this sort of thing to a whole new level beyond that.)

## Mapping analogy to hardware

RAM is structured similarly to the example of the city.

Individual bits are treated like rooms - no one bothers with them. The smallest addressible element of memory is logically the _byte_ which is (on all modern machines except maybe some _very_ special snowflake embedded devices) a cluster of _eight_ bits. But even that leaves the problem space too large since the total number of bits is _enormous_ and a factor of 8 only goes so far.

At the level of a RAM chip, _bytes_ are further clustered into larger units, which must be read or written _all together_. This is a bit like a city where everyone lives in an apartment: in order to get somewhere you need the address of the _building_, and you can figure out how to get to the individual unit (and then the correct room within that unit) later.

RAM chips are further grouped onto RAM _sticks_, where there are several chips. This is a bit like different suburbs on a city.

And, depending on the particulars of the machine, this hierarchical grouping of bits into clusters (of clusters (of clusters (of ...))) produces a structure which, like a building address, turns the problem of finding the value of a particular bit among a sea of bits into a series of smaller problems: talk to the right RAM stick, to the right chip on that stick, to the right row of memory cells within the chip. Going to the analogy, solving this series of smaller problems effectively navigates the CPU to the right apartment block, and then it gets to figure out how to get to the specific unit within that block (and room within that unit) on its own.

But the CPU isn't driving tiny cars on tiny roads. The CPU is sending and receiving signals. That means that what's _actually_ going on is _physical connections_ are being made between (speaking loosely here) _teeny tiny_ little wires. Each step of the hierarchical addressing problem results in groups of transistors _physically_ disconnecting one set of wires and instead connecting another.

# The problem of time

Solving the problems of _space_, we pick up problems in _time_.

In some sense, the speed of light being what it is, problems of space _are_ problems of time: it takes more time to send a signal down a long wire than a short one.

In order to deal with _space_ we wound up dividing it into a hierarchical structure. And at each level of that heirarchy, we decide which subdivision we're interested in by toggling transistors. Time is _gained_ by packing memory tightly and thus _reducing_ the total length of wire between any memory cell and the CPU. But time is also _lost_ it because each of those junctions between levels in the hierarchy _takes time_ to reconfigure between read and write operations.

Then there's the problem of electrons simply wandering away over time. Yeah, that'sa thing - physics is fun like that. And we can't just insulate our memory cells better to prevent it because that would make them slower (for several reasons). DRAM deals with this by looping through its contents, periodically reading and rewriting them in order to replenish the electrons in memory cells that have started losing them (and to flush excess electrons from memory cells that have started accumulating extras). Any request to read or write that the CPU makes has to be coordinated around the work of the refresh cycle. (Refresh cycles are relatively infrequent, so the CPU only very rarely has to wait for one to complete. However, the circuitry which _makes sure_ that the CPU's request doesn't _happen_ to collide with the refresh cycle is not, itself, free.)

# How this impacts the CPU

From the CPU's perspective, main RAM poses two problems:

* The CPU deals in _bytes_, but RAM deals in _clusters_ of bytes.
* RAM cannot be accessed _instantaneously_. Time is required for a memory address to be resolved to the switching of physical transistors, and time may also be required to fit the CPU's request around the work of the RAM's internal refresh cycle.

The first problem could be trivially solved _if not for the second one_. (After all, what could be the harm in reading _more_ memory than necessary and then just ignoring the unneeded bits?) The second problem is where all the action is, because the CPU can move data around internally [_hundreds_ of times faster](https://gist.github.com/jboner/2841832) than data can be moved between the CPU and RAM. That means that _anything_ a CPU can do to reduce the frequency with which it needs to talk to RAM is going to have a big payoff.

# How CPUs (and their designers) deal with this

The CPU handles the problems posed by memory being a physical thing by having a _cache_ of recently accessed memory. That is, the CPU keeps a copy of _some_ of the data that's in RAM so that it can be manipulated without paying the cost of a full trip out to the DRAM chips.

Cache memory is also built differently from DRAM - it is _much_ faster. The problem with it is that it's also _much_ more power-hungry, so there can only be a little bit of it before it becomes impractical to operate.

This memory is organized around a structure called a _cache line_. A cache line is just some number of bytes (typically 64) plus a little bit of extra storage where the CPU keeps track of exactly _which_ 64 bytes are stored there.

In addition to recently-used memory, the cache keeps a copy of memory that might _soon_ be used. Basically, the CPU's memory controller keeps track of _what_ is being read and written. When it spots certain common patterns in those memory accesses, it assumes that the pattern will continue. If there's then a lull in traffic between the CPU and RAM it'll fill that gap by _prefetching_ a copy of the next bit of data that the pattern predicts.

And finally, once the CPU has fetched its own copy of a given block of memory it can use its fast internal circuitry to _quickly_ isolate the individual byte (or even bit) that it was interested in.
