---
title: "Building Content: Just-In-Time Dependency Graphs"
series: content-builder
tags:
  - content
  - programming
---
So, I've got a little toy hobby game project that I've been working on for ages, and it has content. And that content has to be built. And building content is _very annoying_, mostly because it tends to have dependencies that're really hard to quickly extract ahead of time. So... I decided not to, and I built myself a build system that builds the dependency graph and the content _at the same time_.

This isn't some brand new original idea, but I think it's kinda neat. I've enjoyed building and working with my content builder, so I'm writing about it in case anyone else also thinks it's kinda neat.

## Inspiration

There are two major sources of inspiration for my content builder: JamPlus and XNA. Both are ancient, but interesting in their own way.

### JamPlus

[JamPlus](https://github.com/jamplus/jamplus) tries to be a better Make (and I would argue it succeeds). The way it works is that your project is described not by a purely declarative dependency graph, but by a script which _builds_ a dependency graph. It's got variables and flow control and subroutines and everything you'd expect from a basic scripting language all wrapped up in a hilariously obtuse and irritating syntax. (And if you don't like that, you can switch into Lua and be _differently_ annoyed by syntax.)

Now, in order to build the dependency graph you need some way of scanning input files for references to other files. In the same way that a CPP file might include some headers, building a texture atlas will depend on image files. JamPlus requires this to be done in the graph-building phase, and to make it work you need to make executables that it can call which then report back the dependencies.

The problem is that this is slow _and it also duplicates_ a lot of the work that the content _builder_ executable will need to do. So in order to deal with that, JamPlus can (should) be configured to _cache_ the dependency graph, which it then uses to detect changes that might require _partial_ rebuilds of the overall graph. (So, basically, we wind up with dependencies for our dependencies.)

The upside is that, since you can scan the source tree and then run code to decide what to do about those files, it's easy to set up workflows for artists. They just have to drop their files in the right folders, name them properly, and everything Just Works.

### XNA

[XNA](https://en.wikipedia.org/wiki/Microsoft_XNA) (long since abandoned by Microsoft, but living on in spirit as [MonoGame](https://monogame.net/)) was Microsoft's attempt to appeal to hobby coders who want to build games. This was at a time when proper game engines weren't really available to that crowd, so there was an actual niche for it. I had a lot of fun messing around with it on weekends, especially since it was pretty cool seeing stuff I built run on my Xbox 360.

XNA came with an ..._interesting_ content pipeline. The content builders were written in C# and they ran all together in one process. It had some heavy-handed restrictions on the content layout and some annoying caching that couldn't be opted out of (even for very lightweight content where serializing to/from the cache format was slower than just rerunning the import from source). But it also had the ability to discover [and report](https://docs.monogame.net/api/Microsoft.Xna.Framework.Content.Pipeline.ContentImporterContext.html#Microsoft_Xna_Framework_Content_Pipeline_ContentImporterContext_AddDependency_System_String_) additional dependencies _while importing_ content.

# Architecture

So, what have I built?

Well, I _really_ like XNA's "discover and report dependencies while importing" model. Not having to parse the source files twice (which, for some formats, can be pretty involved) is _really_ nice. So I'm having that.

And that, of course, doesn't work without the ability to _cache_ the dependency graph (and having dependencies for the dependencies so that you know when to invalidate the cached dependencies), as JamPlus does, so I have that, too. And then the last step is to merge the caching of intermediate build outputs with the caching of dependencies, so that the complex cache-invalidation code doesn't have to be written and debugged twice.

## Content _identity_

Since caching is central to my content builder, being able to _identify_ bits of content is _critical_. _Content identity_ rests on the following pillars:
* Content builders must explicitly declare their input parameters. And these parameters can be anything _serializable_: strings, ints, enums, arrays of such, etc. These parameters can (and commonly do) name input files, but there's no rule against a content builder programmatically producing noise based on some input integer seed value.
* Content builders must be _deterministic_. They must produce _exactly_ the same output given the same input arguments, _unless_ their dependencies (such as the contents of a referenced input file) have changed.

Serializing and hashing the set of input parameters (but _not_ the state of any dependencies!) thus produces the _identity_ of the output content.

Further, output from a builder can be split into discrete _parts_. This allows a single invocation of a builder to produce multiple output files (such as model data ready to be loaded in the game and an associated metadata file intended for other parts of the build pipeline) whose state is tracked together as a unit. That's something I've long wanted from other build systems.

## Discovering dependencies while building

So then how do dependencies work? Well, there are two types of dependency:

### Files

This is the most straightforward type of dependency.

Whenever the builder opens a file, it registers that file as a dependency with the build system. (There are convenient helper methods that open-and-register so this isn't something that has to be done manually.) When a file dependency is registered, the build system adds it to the dependency graph, along with its timestamp. (And if the file doesn't exist, then it's _still_ a dependency so that the build will be rerun if the file is added later.)

### Other content builds

The other thing a builder can register as a dependency is the output of another content builder.

To do this, the builder simply creates the appropriate parameter pack for the other builder to run with. That parameter pack is then passed to the build system which derives the appropriate content identity, adds it to the current build's list of dependencies, and then gets the requested output data.

If the cache contains up to date data for the requested content, then it's returned directly. If not, the current builder is suspended (`async` and `await` are _fantastic_ tools) while the depended-on content is produced.

## The dependency cache

In order to make this work at all, the dependency graph is cached along with the intermediate build artifacts. The graph isn't cached in one big file (though it _could_ be). Rather, each bit of content has its own spot in the cache for build metadata and its intermediate outputs.

Build metadata consists of a few things:
* The name of the that content's builder. This is just the builder's fully qualified class name.
* The content builder's version number. This is compared to a number _in code_ that gets incremented every time the builder's code is changed in a way that requires rebuilding all the output of that type. I use this mainly when tweaking data formats.
* The builder's input arguments (from which the content's identity is derived).
* The list of outputs that the builder produced the last time it ran.
* The list of files that the builder opened (or tried to open) while building that output, and their timestamps.
* The IDs of _other_ content that the builder referenced.

The rules are then fairly simple when opening a bit of content:
* If the build metadata for a piece of content is missing, run the builder.
* If any of the output files listed in the metadata are missing, or if their timestamps are newer than the metadata file's, then something has messed with the cache and we need to rerun the builder.
* If any of the depended-on files timestamps have changed, rerun the build.
* If any of the depended-on content items would have to be rebuilt in order to load _them_, then rerun the builder. (This rule is, naturally, recusrive all the way up the dependency graph.)

If none of the above conditions are met, then the cached build output is up to date and it can be loaded without running the builder again.

## The downsides

The one thing I _don't_ like about the content builder is how hard it is to find the cache files that belong to a specific build item. But that's what happens when everything is named with something like a hash. At some point I might have to make myself some tools with which to inspect the cache. They'd be useful for debugging.