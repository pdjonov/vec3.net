---
id: 297
title: 'iOS Compiler Bug: -O3 FIXES It?!'
date: 2012-02-17T15:48:41-08:00
author: phill
guid: http://vec3.ca/?p=297
permalink: /posts/ios-compiler-bug-o3-fixes-it
categories:
  - build
  - iOS
tags:
  - Apple
  - bugs
  - code
  - xcode
---
This is officially the strangest optimization-related compiler bug I've _ever_ seen.

It turned up as an error decompressing some data. Now, the decompression isn't some crazy custom algorithm. It's [zlib](http://zlib.net/), one of the most portable and well-tested bits of code in the world. And it's C, so it isn't as though the compiler's got any crazy templates or other such goodies to trip over.

The _really_ weird bit was that the bug would _only_ manifest on an iOS device. The data would decompress just fine on a PC or in the simulator. Normally, this would suggest some sort of uninitialized-memory bug, but after memsetting everything I could think of to zero in the code driving zlib, the bug persisted, and I was at a loss.

So off I went into the disassembly. It turns out that the compiler decided to turn this:

```c++
if (state->lens[256] == 0) {
        strm->msg = (char *)"invalid code -- missing end-of-block";
        state->mode = BAD;
        break;
}
```

into this:

```c++
if (state->lens[0] == 0) {
        strm->msg = (char *)"invalid code -- missing end-of-block";
        state->mode = BAD;
        break;
}
```

The variable `state` is a pointer to a struct, and `lens` is an array of 16-bit integers declared inline to the structure, so there isn't even an extra indirection. The compiler was just miscalculating the offset it needs to add to the pointer to get the address of the 256th element.

The fix? Turning optimization _on_ (even at `-O3`). That's something I've _never_ seen before. Optimization is usually the thing that _breaks_ code, not the thing that fixes it... Especially not in a language as simple and as old as _C_.

Spotted on XCode 4.2.1 using Apple's LLVM Compiler 3.0 targeting an armv6 device.

It'll be interesting to see if the bug reproduces on an armv7 device, to find out if it's a matter of Apple rushing out the new compiler or if they just decided to skip QA for systems targeting legacy devices.