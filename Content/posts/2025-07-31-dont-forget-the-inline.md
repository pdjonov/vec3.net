---
title: "Don't Forget the Inline!"
date: 2025-07-30
author: phill
permalink: /posts/dont-forget-the-inline
tags:
  - code
  - programming
  - C
  - C++
---
If you're writing a header file and you're at global or namespace scope, then you almost certainly _do not_ mean to declare bare `const` or `constexpr` variables.

## Background

So, C++, like C, has a concept called _linkage_. The _linkage_ of a symbol (function or a variable) controls two things:
* Whether the symbol has any visibility outside of the translation unit it's declared in. That is, whether the linker (thus the name "linkage") cares about it at all.
* _If_ the symbol is visible outside of its translation unit, it controls how the linker reacts when _multiple_ translation units declare the same thing.

_At global or namespace scope_ (and nowhere else), linkage is determined as follows (this is a simplification, you can go be a language lawyer on [cppreference](https://en.cppreference.com/w/cpp/language/storage_duration.html#Linkage) if you need the nitty gritty):
* `static` means the symbol has _internal_ linkage. It can't be seen outside the current translation unit, and the linker doesn't care about it.
* Otherwise, the symbol has _external_ linkage. The linker will look for other external symbols that have the same name and _link_ them as follows:
  * If a variable or function is marked `extern` then that means that the linker _must_ find another one with a matching name which is _not_ also marked `extern`. All references to the `extern` symbol are then rewritten to point to the matching non-`extern` one (that is, they are _linked_ together), and the `extern` one is thrown away.
  * If a variable or function is marked `inline` then the linker will take it and every other matching one which is _also_ marked `inline`, pick one, and then throw away the rest (rewriting all references to the thrown away copies to refer, instead, to the one which was kept - again, this is _linking_ and it's what a linker is _for_).
  * If, after ignoring the `extern` symbols and merging together the `inline` symbols, the linker finds multiple symbols that all share the same name, then that's a link error and your build fails.

After linking, all of the remaining functions and variables which aren't discarded as being unreferenced or dead code are then written to the output executable binary.

## Consequences

Note the rules above: _after_ the linker does its thing (which may include eliminating unreferenced symbols), _whatever_ remains goes into the final executable. If linking succeeds, then we know that the symbols with _external_ linkage are all unique, because it's an error to have more than one copy left of any of them after the linker's done its thing. However we know no such thing about symbols with _internal_ linkage, which the linker left as-is.

So what happens if you declare a bunch of `static` things in a header file at global or namespace scope? Well, _every_ CPP file that includes that header (even transitively) gets _its own copy_ of that symbol. (And things get _really_ interesting when a code generator is spitting out massive headers full of absurd number of `static` symbols.) The linker _will_ happily stuff as many redundant copies of a symbol with internal linkage as you (knowingly or otherwise) produce into your executable.

I have seen projects where _tens_ of megabytes were wasted on nothing but this. And it's not that hard to do. You just make a few headers with a few hundred such declarations each and then include them in hundreds of CPP files (possibly by including them in a project-wide precompiled header or something like that). And hardly anyone these days looks at linker maps, so in a big project it goes totally unnoticed (until the team runs face-first into a hard memory limit on a target platform).

So, y'know, don't do that.

## Okay, but what does that have to do with `const` and `constexpr`?

This variable isn't marked `static`, so it has _external_ linkage:

```cpp
int foo = 8;
```

However _this_ variable, which _also_ isn't marked `static`, has _internal_ linkage - _as if_ it was marked `static`:

```cpp
const int foo = 8;
```

And so does this one:

```cpp
constexpr int foo = 8;
```

Why do `const` and `constexpr` change the default linkage in this manner? Well, `const` started it and `constexpr` is probably just trying to be consistent with the existing convention. But why is `const` like this...?

I don't actually know the answer, but I'll venture a guess. It probably goes back to C (if not to some predecessor of C I don't know about). See, in ages past, `inline` was _only_ valid on _functions_ (which probably contributes to some people _still_ thinking `inline` refers to _inlining_, the optimization - _it doesn't!_). Variable declarations _could not_ be `inline`. So if you wanted to put a bunch of constants in a header file, then you'd have to _also_ mark them all `static` in order to prevent the linker from seeing duplicate declarations if that header was then included in multiple C files. That would be annoying, so _my guess_ is that someone decided that `const` could just _imply_ `static` and that would be good enough.

And in the past, it probably wasn't all that bad. A compiler would likely just copy the value of a `const int` into places where it's referenced instead of referencing the symbol itself. That makes the declaration unreferenced dead code, and it gets eliminated. And as far as I've ever seen, the original advice when promoting the use of `const` over macros was exactly that - to use them for basic things like integers.

But now we don't just want constant integers. We want bigger, chonkier constants: configuration blocks, binary resources, _things that have constructors_... A `const int` can get completely eliminated, but a `const SomethingWithAConstructor` probably can't (unless the compiler can prove in all translation units that the constructor has no side effects). And in order to _run_ that constructor before `main`, as the language requires, the compiler needs to generate a function to _call_ it, and then a pointer to that function probably needs to be put into an array somewhere where the C/C++ runtime will find and call it before calling `main`. And maybe the constructor can't be proven not to throw, so exception-handling tables need to be set up for the little function... And sure, that's all just a few bytes, but in a nontrivial project, those few bytes can easily be multiplied out by a large number, and they _do_ add up.

As I said, I've seen megabytes lost because of this and, in one case, those "mere" megabytes were a straw among a handful of others which nearly broke our camel's back.

## Neat, how do I fix it?

To avoid this problem in modern C++, the solution is to just add `inline` to all of these declarations. Instead of `const int foo` it's now `inline const int foo`; similarly, `constexpr int foo` becomes `inline constexpr int foo`. The terrible default is terrible and it's annoying to have to override it every single time, but that's how things are and we gotta deal with it.