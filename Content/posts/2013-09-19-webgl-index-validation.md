---
id: 1000
title: WebGL Index Validation
date: 2013-09-19T19:27:26-07:00
author: phill
guid: http://vec3.ca/?p=1000
permalink: /posts/webgl-index-validation
categories:
  - code
  - graphics
tags:
  - code
  - graphics
  - OpenGL
  - OpenGL ES
  - programming
  - WebGL
---
If you've ever browsed through [the WebGL spec](https://www.khronos.org/registry/webgl/specs/1.0.2), you've likely seen [section 6: _Differences Between WebGL and OpenGL ES 2.0_](https://www.khronos.org/registry/webgl/specs/1.0.2/#6). Right at the top of that section, we find section 6.1: _Buffer Object Binding_. That section reads as follows:

> In the WebGL API, a given buffer object may only be bound to one of the `ARRAY_BUFFER` or `ELEMENT_ARRAY_BUFFER` binding points in its lifetime. This restriction implies that a given buffer object may contain either vertices or indices, but not both.
> 
> The type of a WebGLBuffer is initialized the first time it is passed as an argument to bindBuffer. A subsequent call to bindBuffer which attempts to bind the same WebGLBuffer to the other binding point will generate an INVALID_OPERATION error, and the state of the binding point will remain untouched.

This is in stark contrast to the language in the `glBindBuffer` documentation for both [OpenGL](http://www.opengl.org/sdk/docs/man/xhtml/glBindBuffer.xml) and [OpenGL ES](http://www.khronos.org/opengles/sdk/docs/man/xhtml/glBindBuffer.xml):

> Once created, a named buffer object may be re-bound to any target as often as needed. However, the GL implementation may make choices about how to optimize the storage of a buffer object based on its initial binding target.

The reason for the discrepancy is security, or rather the lack of security in most OpenGL (ES included) implementations. The basic OpenGL standard states that out-of-range access of any resource type results in undefined behavior, and performance-minded implementers the wold over historically took this to mean that it's OK to crash the process or even the entire operating system when given invalid indices. (I still remember a few years back when I could consistently blue-screen my computer with a given combination of small vertex buffers and an index buffer with many huge index values in it.)

While the situation's been improving ever since DX10-grade hardware started coming out (Microsoft mandated deterministic non-crashing behavior for out of range access on DX10-level hardware, and that safety net's been leaking into GL implementations ever since), we're still not at a point where OpenGL implementations could be considered secure against DoS (or worse) attacks. (In fact, newer OpenGL specifications make safe array access an _optional_ feature.)

Since web browsers deal with inherently untrustworthy content and have to support all sorts of GL implementations (everything from old implementations that didn't care at all about security to newer implementations with buggy safety features), the WebGL specification mandates that browsers strictly validate all input data before it's sent to the graphics driver. And that includes the contents of all index (`ELEMENT_ARRAY`) buffers.

The restriction in 6.1 exists to make it easier for WebGL implementers to validate input indices and cache the results of that validation.

You can find further discussion of the issue [here](https://www.khronos.org/webgl/public-mailing-list/archives/1001/msg00110.html), on the WebGL mailing list.

WebGL makes other restrictions in the name of security as well. Check out [section 4](https://www.khronos.org/registry/webgl/specs/1.0.2/#4) of the spec for details.