---
title: Introducing HenchLua
time: 13:00
series: HenchLua
tags:
  - 'C#'
  - code
  - HenchLua
  - Lua
  - programming
---
This is the first of a series of posts on the subject of [HenchLua](https://github.com/henchmeninteractive/HenchLua). HenchLua is an implementation of the Lua VM in C#. It's targeted at projects running in otherwise limited .NET contexts, such as web-based Unity games (the Unity plugin, I believe, requires pure verifiable CIL), mobile apps (which are memory-limited) and must meet the limitations of Mono's [full AOT compiler](http://www.mono-project.com/AOT), or apps that run on the .NET Compact Framework (whose garbage collector has some serious performance issues, as anyone who's written an XNA game targeted at the Xbox can attest).

Studying the standard Lua runtime and reimplementing it in a fundamentally different environment has been an enlightening (and at times maddening) experience. I'm writing this series to share some of the insights I've had along the way, both with respect to .NET programming and in relation to the standard Lua implementation.

<div class="alignright caption-box">
  <img class="henchie" src="/assets/img/henchies/guv.webp" />
</div>

## Design Goals

Unlike [KopiLua](https://github.com/NLua/KopiLua), which aims for the highest possible degree of compatibility with standard Lua, HenchLua's first goal is efficiency, followed closely by _a useful_ degree of compatibility with the standard. To that end, I've made a number of compromises. So, what exactly does that mean?

First, HenchLua is designed to be gentle to the garbage collector. HenchLua avoids transient objects at all costs (as even small and short-lived allocations can trigger expensive collection cycles at inopportune times on some .NET runtimes). The rule is simple: if standard Lua doesn't touch the heap when executing a given operation, then (apart from a few _limited_ exceptions) it's a bug for HenchLua to do otherwise. What this means for the user is that if you're careful about how you structure your scripts, you can be reasonably sure that HenchLua won't be the trigger of an unexpected collection cycle.

Further, when a collection cycle _does_ happen, HenchLua does its best to maintain a minimal impact. This is mainly achieved by keeping the object graph _small_ and _simple_. So in addition to avoiding the construction of ephemeral objects, HenchLua also avoids creating wrapper objects (read: bloat) and unnecessary references among objects (read: cache misses). And while we're on the subject of garbage collection, HenchLua directly uses the .NET GC. Apart from avoiding the silliness of implementing a garbage collector in a garbage-collected language, this immensely reduces the number of inter-object references.

In addition, HenchLua compiles to pure, verifiable, CIL, it needs no special permissions to run, and it avoids advanced features of the .NET framework. As awesome as it would have been to use `Reflection.Emit` or `Expression.Compile`, those techniques don't work on the Compact Framework or with Mono's AOT compiler, and broad compatibility is definitely a goal.

Of course, some of these goals complicates the implementation of the VM. Fortunately, the situation isn't all _that_ bad since Lua is incredibly simple to begin with.

The API is also vastly different from Lua's. Since Lua objects and .NET objects live in the same conceptual memory space and are both subject to the same garbage collector, there's no need to firewall Lua objects behind the standard runtime's stack API. Lua objects are directly accessible to .NET code, to the extent that HenchLua's `Table` type can _almost_ be used like a `Dictionary<Value, Value>` (there are some semantic differences concerning the way `nil` keys and values are treated).

The only exception to this is Lua's function objects. While strings and tables can be directly constructed and manipulated, Lua functions can't be called directly. There's a good deal of state that needs to be tracked when running a Lua function, and for that we have the `Thread` object, whose job it is to execute the Lua bytecode contained in Lua `Function` objects.

<div class="alignright caption-box">
  <img class="henchie" src="/assets/img/henchies/train.webp" />
</div>

## What Works

HenchLua is a work in progress. As of today, you can set up an environment with (parts of) the Lua standard library and your own callbacks, load compiled bytecode with respect to that environment, and execute that code - provided that it doesn't rely on missing features. The reality is that HenchLua is being developed as part of a larger project, and it's getting features on an as-needed basis, so apart from the core VM, the current feature set is somewhat eclectic.

Don't be too put off by the missing features. The codebase is clean, and it's not very difficult to add the missing bits in. Furthermore, as limited as the current feature set is, it's sufficient to run some fairly complex code. If you don't rely too heavily on features in the _What's Missing_ list, chances are HenchLua would be useful to you, even in its current state, with only minimal effort.

## What's Missing

A lot is missing at this stage. Most notably, the following are absent:

  * Coroutines
  * Metamethods that aren't `__index`
  * The Lua compiler (HenchLua loads bytecode produced by the standard Lua 5.2 compiler)
  * Weak keys and values (HenchLua uses the .NET GC directly - working around it to implement these features would be burdensome, to say the least)
  * Some of the implicit string-number conversions
  * The debug libraries (Debug info is loaded, and it's even useful in a debugger, but the routines to parse it and implement the actual debug API haven't been ported)
  * Most of the standard libraries