Some of my non-commercial projects are open-source and available online. Here's links:

*   [HenchLua](https://github.com/henchmeninteractive/HenchLua)
    HenchLua is a performance-oriented implementation of the Lua runtime in C# (with support for constrained .NET environments).

    This wasn't technically a non-commercial project, as it was developed for Henchmen Interactive Inc, but it's still open-source (MIT license). I [wrote](/posts/series/HenchLua) about it (a little) on the blog.

*   [NvTT.NET](http://github.com/pdjonov/NvTT.NET/wiki)
    This is a simple C++/CLI wrapper around [NVidia's Texture Tools](http://developer.nvidia.com/object/texture_tools.html) library. I wrote it since the core wrapper is missing features (like `nvtt::OutputHandler`) and has a fairly ugly interface by .NET standards.

    This library may be somewhat out of date with respect to the NVidia Texture Tools as I only update it when I upgrade my projects to a new version of NvTT.

*   [HqNX](/posts/hqnx)
    This is a GPU-based implementation of Maxim Stepin's hqNx family of image upscaling algorithms. You can read more about it on its page.

*   [Cobalt-3D](http://code.google.com/p/cobalt-3d/)
    This is the name of an old side project of mine. It's a set of XNA libraries I put together when Microsoft came out with their XNA XBOX homebrew platform. It's somewhat abandoned for the time being, but has some interesting features including the ability to take a Quake 3 level and compile it into something that renders efficiently on the XBOX, even compiling the Quake 3 'shaders' into XNA Effects (a small number of Quake 3's shader features are unsupported - fortunately, they're not commonly used).

*   [x42](http://github.com/pdjonov/x42/wiki)
    The x42 project is the skinned-animation model format, the accompanying runtime libraries, and a few related tools (including exporters from both Maya and FBX files). It started as part of my work at Hermitworks Entertainment, originally intended for Space Trader, but ultimately used in other titles as well. It was largely open-sourced (under the GLP) fairly early on in its development, and I've had it hosted on GitHub ever since.

    It's nothing fancy by today's standards, but it's a good example of the basics and of the sort of tools one has to build to keep development running smoothly. It's also my reference code every time I need to export a skinned mesh from an FBX file.