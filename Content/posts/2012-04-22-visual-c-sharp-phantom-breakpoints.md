---
title: 'Visual C#: Phantom Breakpoints'
tags:
  - 'C#'
  - programming
---
So I've got a large solution with a lot of projects (mostly build tools) and every now and then I would get suspicious phantom breakpoints, meaning that the debugger would break where no breakpoint had been placed. This would usually happen after closing and reopening the project, and it only ever seemed to afflict the executables in my solution - never the libraries.

It turns out there's a bug in how Visual C# saves and restores breakpoints. While they restore correctly in the UI, they can "leak" into other identically named files behind the scenes, creating phantom breakpoints in those files.

So if your debugger won't stop breaking on an unassuming line in `Program.cs`, take a peek at the `Program.cs` files in your solution's other projects - chances are one has a breakpoint set on that line, and clearing it will fix the phantom for you.