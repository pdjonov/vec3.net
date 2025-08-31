---
title: "Rendering an Infinite Grid"
tags:
  - graphics
  - code
  - shaders
  - slang

draft: true
---

I've been working on a little VR project for a while, now. And one of the very first things I needed was something to _stand_ on. Floating in a black void is neat and all, but without reference points I had no way of knowing if tracking (and, importantly, my rendering code's interpretation of the trackig data) was functioning correctly.

So I made an infinte plane to stand on.

<div class="caption-box aligncenter">
  <img src="/assets/img/liminal-plane.webp" alt="An infinite floor plane." />
  <em>Unlimited <strike>powah</strike> grid!</em>
</div>

Here's how it works.

# Implementation

This discussion will be using some Vulkan terminology, and the shader code is in [Slang](https://shader-slang.org/), but it should all be trivial to translate to other languages and APIs.

First of all, it's an infinite plane. It doesn't have any real geometric structure, so it doesn't need vertex buffers and so on. To kick this off, I just bind the pipeline and kick off a `vkCmdDraw` for 4 vertices (since what I want is logically a _quad_). The pipeline uses a triangle _fan_ topology, but this could just as well have been a triangle strip (with appropriate adjustments in the vertex shader).

## The vertex shader

```slang
struct FragIn
{
	float4 Position : SV_Position;
	float2 PlaneCoord;
	float3 ViewPos;
};

[shader("vertex")]
FragIn vert(uint vertexId: SV_VertexID)
{
	var bit0 = vertexId & 0x1;
	var bit1 = (vertexId & 0x2) >> 1;

	var mPos = float2(
		// 0, 1, 1, 0
		bit0 ^ bit1,
		// 1, 1, 0, 0
		1 - bit1);
	mPos = mPos * 2 - 1;
	mPos = mPos * FogRange.y;
	mPos = mPos + ViewTransform.InvView._14_34;

	var wPos = float3(mPos.x, 0, mPos.y);

	FragIn out;

	out.Position = mul(ViewTransform.ProjView, float4(wPos, 1));

	out.PlaneCoord = wPos.xz;
	out.ViewPos = mul(ViewTransform.View, float4(wPos, 1));

	return out;
}
```

Alright, what's going on here?

First, I need to compute the coordinates of the corners of my quad. I begin by turning our vertex ID into a coordinate on a 1x1 unit square extending from $(0, 0)$ to $(1, 1)$.

```slang
	var bit0 = vertexId & 0x1;
	var bit1 = (vertexId & 0x2) >> 1;

var mPos = float2(
	// 0, 1, 1, 0
	bit0 ^ bit1,
	// 1, 1, 0, 0
	1 - bit1);
```

Then I scale and translate to turn that into a 2x2 square centered on the origin, extending from $(-1, -1)$ to $(1, 1)$.

```slang
mPos = mPos * 2 - 1;
```

Next I scale up the quad to cover all the pixels I need to render. I want my quad to fade out at the edges as if it's disappearing into a "fog", so I just scale by the fog range:

```slang
mPos = mPos * FogRange.y;
```

`FogRange` just holds my fog distance parameters. `FogRange.x` is where things start fading out, `FogRange.y` is the distance at which they're fully faded.

And finally I want the plane to always be centered _directly_ under the camera:

```slang
mPos = mPos + ViewTransform.InvView._14_34;
```

Okay, that needs some explaining. `ViewTransform.InvView` is just my inverse-view matrix (hanging out with `FogRange` somewhere in a uniform buffer). That is, it transforms _from_ view space _to_ world space. In view space, the camera is located at the origin, $0$, so multiplying that through the _inverse-view_ matrix produces _world_-space coordinates:

$$
\begin{split}
C_w &= V^{-1}0 \\
&= \begin{bmatrix}
V^{-1}_{11} & V^{-1}_{12} & V^{-1}_{13} & V^{-1}_{14} \\
V^{-1}_{21} & V^{-1}_{22} & V^{-1}_{23} & V^{-1}_{24} \\
V^{-1}_{31} & V^{-1}_{32} & V^{-1}_{33} & V^{-1}_{34} \\
0 & 0 & 0 & 1 \\
\end{bmatrix}
\begin{pmatrix} 0 \\ 0 \\ 0 \\ 1 \end{pmatrix} \\
&= \begin{pmatrix}
V^{-1}_{14} \\ V^{-1}_{24} \\ V^{-1}_{34} \\ 1
\end{pmatrix}
\end{split}
$$

In this case, I want the plane flat on the floor, so I don't need the $y$-coordinate, so I just grab the `_14_34` swizzle to extract the $x$ and $z$. Adding that to my quad's corners works as expected because the quad was centered on the origin in the first place and now it's centered _under the camera_.

(For those not used to Slang, it follows HLSL notation, so matrix entries are named and accessed with _row_-major subscripts, as in standard mathematical notation. This doesn't mean they're _stored_ row-major.)

Here I'm also being a bit loose with my `m` prefix. `mPos` at this point is somewhere between being a model-space and world-space position. Think of it as a model-space where the artist had prophetic powers and _knew_ where the camera would be and just modeled the quad under that.

I then convert the plane's coordinates to _proper_ world space.

```slang
var wPos = float3(mPos.x, 0, mPos.y);
```

Why all this mucking about with world space? Surely this could all just have been done in view space. Well, yeah, _kind of_. Getting a quad centered under the camera would have been _easier_ if I'd worked in view space, but I'd still need the _world_-space coordinates because the _lines_ that I'm going to draw on that quad need to be pinned to that space, specifically.

Speaking of the fragment shader, it's time to start setting up for rasterization and shading:

```slang
FragIn out;

out.Position = mul(ViewTransform.ProjView, float4(wPos, 1));

out.PlaneCoord = wPos.xz;
out.ViewPos = mul(ViewTransform.View, float4(wPos, 1));

return out;
```

The calculation of `out.Position` is just the standard world-to-clip transformation.

`out.PlaneCoord` passes the _world_ position through to the fragment shader so it knows where to draw the grid lines.

Finally, the fragment shader will still need the _view_-space coordinates so that it can fog things up correctly.

## The fragment shader

This has more going on, but it's just a bunch of simple stuff in a trenchcoat pretending to be complex.

```slang
static const float GridSpacing = 1;
static const uint MajorSpacing = 4;

[shader("fragment")]
float4 frag(in FragIn in, bool isFront: SV_IsFrontFace)
	: SV_Target
{
	var fogRange = FogRange;

	var viewDist = length(in.ViewPos);
	var viewFog = smoothstep(fogRange.y, fogRange.x, viewDist);

	var gridCoord = in.PlaneCoord * GridSpacing;
	var widthScalar = fwidth(gridCoord);
	var lineWidth = min(widthScalar, 1);

	var grid = abs(fract(gridCoord - 0.5) - 0.5) / widthScalar;

	const float MajorGrid = GridSpacing * MajorSpacing;
	var majorCoord = gridCoord - MajorGrid * floor(gridCoord / MajorGrid); // GLSL's mod
	var isMajor = bool2(
		majorCoord.x < lineWidth.x || majorCoord.x > MajorGrid - lineWidth.x,
		majorCoord.y < lineWidth.y || majorCoord.y > MajorGrid - lineWidth.y);

	var baseShade = smoothstep(1, 0, grid);
	var shade = baseShade * smoothstep(fogRange.y * 0.85, fogRange.x * 0.5, viewDist);
	shade = max(shade, float2(isMajor) * baseShade * 4);

	shade *= 1 - min(max(widthScalar.x, widthScalar.y), 1);

	var backColor = float3(0);
	var foreColor = float3(0.1);

	if (!isFront)
		backColor = float3(0.25);

	if (gridCoord.x > -lineWidth.x && gridCoord.x < lineWidth.x)
		foreColor = Scene::GetKeyColor(1).rgb;
	if (gridCoord.y > -lineWidth.y && gridCoord.y < lineWidth.y)
		foreColor = Scene::GetKeyColor(0).rgb;

	var color = lerp(backColor, foreColor, max(shade.x, shade.y));

	return float4(color * viewFog, 1.0);
}
```

The first thing we do is a quick fogging calculation. We copute the distance to the camera and then use `smoothstep` to clamp it between zero if it's less than thedistance where the fog begins and one if it's greater than the distance where the fog maxes out (and the lines should be completelyy faded out), with a smooth falloff between the two.

```slang
var viewDist = length(in.ViewPos);
var viewFog = smoothstep(fogRange.y, fogRange.x, viewDist);
```