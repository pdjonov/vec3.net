---
id: 570
title: Abstracting Low-Level I/O
date: 2012-03-29T17:36:23-07:00
author: phill
guid: http://vec3.ca/?p=570
permalink: /posts/abstracting-low-level-io
categories:
  - code
tags:
  - code
  - io
  - loading
---
One of the common requirements in game development is that we need to load large blocks of (usually) compressed data in as little time as possible. This, however, is somewhat easier said than done. Ideally, what we're looking for is a simple asynchronous I/O API.

Unfortunately, PCs and consoles and mobile devices differ significantly on the sort of APIs they offer. Not only do the types of API differ, but the particular restrictions they make on read sizes also vary. And so do the performance characteristics of each. Some platforms don't even offer much in the way of options: different drives end up exposed through entirely different APIs.

I recently settled on a fairly simple abstraction to make it easy to work with all these different APIs in a consistent manner. Instead of treating a file as a string of bytes, the loader code treats it as a series of variably-sized chunks. The interface consists of one core function which returns the address and size of the next chunk of data. This lets me easily _and efficiently_ wrap up almost any API.

Got asynchronous I/O? Great! Allocate some buffers, kick off a bunch of requests, and hand the chunks to the decoder as they come in. It's trivial to make additional read requests or block the loader thread as needed when it asks for the next chunk.

No luck? Stuck with a synchronous stream? Well, alright. Allocate a chunk-sized buffer, fill it with data, and each time the loader asks for the next page just go ahead and block the thread with another read into that same buffer. Or go crazy and spawn another thread to emulate async I/O, if that's somehow faster.

Memory-mapped files the fastest way to go? Maybe just for certain files? No problem. Map the file and hand the loader a single large chunk.

Happen to somehow have your data sitting in memory already? That's no different from the memory-mapped case.

So long as the loader doesn't make assumptions about the number and size of chunks, the lower-level code is free to make those buffers in whatever number and at whatever size is the most efficient for the target device.