---
title: "Rendering an Infinite Grid"
tags:
  - graphics
  - code
  - shaders
  - slang
  - Vulkan
  - WebGL
notes:
  - type: info
    date: 2025-09-12
    text: >
        The preview image of the grid in this post is now interactive (on most browsers). Click around to see the algorithm outlined below in action.
---

I've been working on a little VR project for a while, and one of the first things I needed at the start was something to _stand_ on. So I made an infinite grid to be my floor. Here's how it works.

<script>
	sketch3d.load(class extends sketch3d.webglSketch {
		static get fallbackImage() { return '/assets/img/liminal-plane.webp'; }
		static get captionHTML() { return '<em>Un-limited <s>powah!</s> grid.</em>'; }

		constructor(canvas) {
			super(canvas);

			this._clearColor = [0, 0, 0, 0];

			this.#prog = this.compileProgram(
				//vertex shader
				`#version 300 es

				const float GridSize = 0.5;
				const int GridMinorsPerMajor = 4;

				uniform mat4 uView;
				uniform mat4 uInvView;
				uniform mat4 uProj;
				uniform vec2 uFogRange;

				out vec2 vGridCoord;
				out mediump vec3 vViewPos;

				void main() {
					int bit0 = gl_VertexID & 0x1;
					int bit1 = (gl_VertexID & 0x2) >> 1;

					vec2 mPos = vec2(
						// 0, 1, 1, 0
						bit0 ^ bit1,
						// 1, 1, 0, 0
						1 - bit1) * 2.0 - 1.0;

					mPos = mPos * uFogRange.y + uInvView[3].xz;

					vec4 wPos = vec4(mPos.x, -0.001, mPos.y, 1);
					vec4 vPos = uView * wPos;

					gl_Position = uProj * vPos;
					vGridCoord = wPos.xz / GridSize + 0.5;
					vViewPos = vPos.xyz;
				}`,
				//fragment shader
				`#version 300 es
				precision highp float;

				const float GridSize = 0.5;
				const int GridMinorsPerMajor = 4;

				uniform vec2 uFogRange;

				in vec2 vGridCoord;
				in mediump vec3 vViewPos;

				out mediump vec4 oColor;

				void main() {
					// figure out our fogging values
					float viewDist = length(vViewPos);
					float majorFog = 1.0 - smoothstep(uFogRange.x, uFogRange.y, viewDist);
					float minorFog = 1.0 - smoothstep(uFogRange.x * 0.5, uFogRange.y* 0.85, viewDist);

					// find the index of the closest grid line to this pixel
					ivec2 lineIndex = ivec2(floor(vGridCoord));

					// pick an appropriate width and color for the closest line (in *each* of X and Y!)
					vec2 lineWidth;
					vec3 lineColor[2];
					vec2 lineFog;
					for (int i = 0; i < 2; i++)
					{
						float width;
						vec3 color;
						float fog;

						if (lineIndex[i] == 0)
						{
							width = 5.0;
							color = vec3(i == 1, 0, i == 0);
							fog = majorFog;
						}
						else if (lineIndex[i] % GridMinorsPerMajor == 0)
						{
							width = 3.0;
							color = vec3(0.5);
							fog = majorFog;
						}
						else
						{
							width = 2.0;
							color = vec3(0.25);
							fog = minorFog;
						}

						lineWidth[i] = width;
						lineColor[i] = color;
						lineFog[i] = fog;
					}

					vec2 lineDist = abs(0.5 - fract(vGridCoord)) * 2.0;
					vec2 lineMask = 1.0 - clamp(lineDist /
						(fwidth(vGridCoord) * lineWidth), 0.0, 1.0);

					vec2 blendFactors = lineMask * lineFog;
					for (int i = 0; i < 2; i++)
						if (lineIndex[1 - i] == 0 && lineIndex[i] != 0)
							blendFactors[i] *= smoothstep(0.0, 0.5, lineDist[1 - i]);

					vec3 finalColor = max(lineColor[0] * blendFactors.x, lineColor[1] * blendFactors.y);
					float finalAlpha = max(blendFactors.x, blendFactors.y);

					oColor = vec4(finalColor, finalAlpha);
				}`);
		}

		#prog;

		_draw(gl) {
			super._draw(gl);

			const math = sketch3d.math;

			const mView = math.ma4x4.viewLookAt(
				this.#viewPosition(),
				this.#viewLookAt,
				this.#viewUp());
			const mProj = math.m4x4.projectPerspectiveFov(
				90 * Math.PI / 180, this.width / this.height, 0.1);

			const p = this.#prog;
			gl.useProgram(p);
			p.uniforms.uView.set(mView);
			p.uniforms.uInvView.set(sketch3d.math.ma4x4.inverse(mView));
			p.uniforms.uProj.set(mProj);
			p.uniforms.uFogRange.set([10,16]);

			gl.drawArrays(gl.TRIANGLE_FAN, 0, 4);
		}

		//camera

		#viewLookAt = new sketch3d.math.vec3(-0.5685110092163086, 0, 0.13670504093170166);
		#viewRotation = new sketch3d.math.quat(0.41593441367149353, -0.1826382577419281, -0.10707172751426697, -0.8844079375267029);
		#viewRange = 5;

		#viewPosition() {
			return this.#viewLookAt.clone().sub(this.#viewForward().scaleBy(this.#viewRange));
		}
		#viewForward() {
			return this.#viewRotation.rotatePoint([0, 0, -1]);
		}
		#viewRight() {
			return this.#viewRotation.rotatePoint([1, 0, 0]);
		}
		#viewUp() {
			return this.#viewRotation.rotatePoint([0, 1, 0]);
		}

		#pdX = 0;
		#pdY = 0;
		#pdLookAt = null;
		#pdLookRotation = null;
		#pdMode = null;

		_pointerDown(x, y) {
			this.#pdX = x;
			this.#pdY = y;
			this.#pdLookAt = this.#viewLookAt.clone();
			this.#pdLookRotation = this.#viewRotation.clone();

			this.#pdMode = 'pan';
			if (x * x + y * y > 0.9)
				this.#pdMode = 'rot';

			return {capture: true};
		}
		_pointerUp(x, y) {
			this.#pdLookAt = null;
			this.#pdLookRotation = null;
			this.#pdMode = null;
		}

		_pointerMove(x, y, cap) {
			if (!cap)
				return;

			const dx = x - this.#pdX;
			const dy = y - this.#pdY;

			if (this.#pdMode === 'pan') {
				const vf = this.#viewForward();
				vf[1] = 0;
				const vr = this.#viewRight();
				vr[1] = 0;

				const s = -10 / this.#viewRange;

				this.#viewLookAt = this.#pdLookAt.
					clone().
					add(vf.scaleBy(dy * s)).
					add(vr.scaleBy(dx * s));
			} else if (this.#pdMode === 'rot') {
				const vd = screenToArc(this.#pdX, this.#pdY);
				const vc = screenToArc(x, y);

				const q = quatFromVecs(vd, vc);

				this.#viewRotation = this.#pdLookRotation.
					clone().
					mul(q);

				function screenToArc(x, y) {
					var lsq = x * x + y * y;

					var z;
					if (lsq > 1)
					{
						var s = 1 / Math.sqrt(lsq);

						x *= s;
						y *= s;

						z = 0;
					}
					else
					{
						z = Math.sqrt(1 - lsq);
					}

					return new sketch3d.math.vec3(x, y, z);
				}

				function quatFromVecs(s, e) {
					const ijk = sketch3d.math.vec3.cross(s, e);
					const w = -sketch3d.math.vec3.dot(s, e);

					var ret = new sketch3d.math.quat();
					ret[0] = ijk[0];
					ret[1] = ijk[1];
					ret[2] = ijk[2];
					ret[3] = w;
					return ret;
				}
			}

			return {redraw: true};
		}
	});
</script>

Read on to find out _exactly_ how that image is rendered.

# Implementation

This discussion will be using some Vulkan terminology, and the shader code is in [Slang](https://shader-slang.org/), but it should all be trivial to translate to other languages and APIs. For instance, the image above is powered by a WebGL implementation, which you can find right in this page's source.

First of all, it's an infinite plane. It doesn't have any real geometric structure, so it doesn't need vertex buffers and so on. To kick this off, I just bind the pipeline and kick off a `vkCmdDraw` for 4 vertices (since what I want is logically a _quad_). The pipeline uses a triangle _fan_ topology, but this could just as well have been a triangle strip (with appropriate adjustments in the vertex shader).

## The vertex shader

```slang
// The width, in world-space units, of one grid cell.
static const float GridSize = 0.5;
// The number of minor grid lines between each major.
static const uint GridMinorsPerMajor = 4;

struct FragIn
{
	float4 Position : SV_Position;
	float2 GridCoord;
	float3 ViewPos;
};

[shader("vertex")]
FragIn vert(uint vertexId: SV_VulkanVertexID)
{
	var bit0 = vertexId & 0x1;
	var bit1 = (vertexId & 0x2) >> 1;

	var mPos = float2(
		// 0, 1, 1, 0
		bit0 ^ bit1,
		// 1, 1, 0, 0
		1 - bit1);
	mPos = mPos * 2 - 1;
	mPos = mPos * Fog.FarDistance;
	mPos = mPos + ViewTransform.InvView._14_34;

	var wPos = float3(mPos.x, 0, mPos.y);

	FragIn out;

	out.Position = mul(ViewTransform.ProjView, float4(wPos, 1));

	out.GridCoord = wPos.xz / GridSize + 0.5;
	out.ViewPos = mul(ViewTransform.View, float4(wPos, 1));

	return out;
}
```

Alright, what's going on here?

### Finding the quad's corners

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
mPos = mPos * Fog.FarDistance;
```

`Fog` just holds my fog distance parameters. `Fog.NearDistance` is where things start fading out, `Fog.FarDistance` is the range at which they're fully faded.

### Placing the quad

And finally I want the plane to always be centered _directly_ under the camera:

```slang
mPos = mPos + ViewTransform.InvView._14_34;
```

Okay, that needs some explaining. `ViewTransform.InvView` is my inverse-view matrix (it hangs out with `Fog` somewhere in a uniform buffer). That is, it transforms _from_ view space _to_ world space. In view space, the camera is located at the origin, $0$, so multiplying that through the _inverse-view_ matrix produces _world_-space coordinates:

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

With that done, the quad will happily follow the camera _wherever_ it goes. It's time to convert the that to _proper_ world space.

```slang
var wPos = float3(mPos.x, 0, mPos.y);
```

Why all this mucking about with world space? Surely this could all just have been done in view space. Well, yeah, _kind of_. Getting a quad centered under the camera would have been _easier_ if I'd worked in view space, but I'd still need the _world_-space coordinates because the _lines_ that I'm going to draw on that quad need to be pinned to that space, specifically.

### Preparing to rasterize and shade

Speaking of the fragment shader, it's time to start setting up for rasterization and shading:

```slang
FragIn out;

out.Position = mul(ViewTransform.ProjView, float4(wPos, 1));

out.GridCoord = wPos.xz / GridSize + 0.5;
out.ViewPos = mul(ViewTransform.View, float4(wPos, 1));

return out;
```

The calculation of `out.Position` is just the standard world-to-clip transformation.

Then the shader takes the _world_ position of the plane and scales it so that integer coordinates mark out the grid lines. _That_ value is then offset by half a grid unit so that the grid lines pass where either the `x` or `y` value is $0.5$. Why? Well, it simplifies a bit of math down in the fragment shader, which receives the computed value through `out.GridCoord`.

Finally, the fragment shader will still need the _view_-space coordinates so that it can fog things up correctly.

## The fragment shader

This shader has a lot more going on, but it's actually just a bunch of simple stuff stacked up in a trenchcoat, _pretending_ to be complex.

```slang
[shader("fragment")]
float4 frag(in FragIn in)
	: SV_Target
{
	// figure out our fogging values
	var viewDist = length(in.ViewPos);
	var majorFog = Fog.Fade(viewDist);
	var minorFog = Fog.ScaledFade(viewDist, float2(0.5, 0.85));

	// find the index of the closest grid line to this pixel
	var lineIndex = (int2)floor(in.GridCoord);

	// pick an appropriate width and color for the closest line (in *each* of X and Y!)
	float2 lineWidth;
	float3[2] lineColor;
	float2 lineFog;
	for (var i = 0; i < 2; i++)
	{
		float width;
		float3 color;
		float fog;

		if (lineIndex[i] == 0)
		{
			width = 5;
			color = GetKeyColor(1 - i).rgb;
			fog = majorFog;
		}
		else if (lineIndex[i] % GridMinorsPerMajor == 0)
		{
			width = 3;
			color = 0.5;
			fog = majorFog;
		}
		else
		{
			width = 2;
			color = 0.25;
			fog = minorFog;
		}

		lineWidth[i] = width;
		lineColor[i] = color;
		lineFog[i] = fog;
	}

	var lineDist = abs(0.5 - frac(in.GridCoord)) * 2;
	var lineMask = 1 - saturate(lineDist /
		(fwidth(in.GridCoord) * lineWidth));

	var blendFactors = lineMask * lineFog;
	for (var i = 0; i < 2; i++)
		if (lineIndex[1 - i] == 0 && lineIndex[i] != 0)
			blendFactors[i] *= smoothstep(0, 0.5, lineDist[1 - i]);

	var finalColor = max(lineColor[0] * blendFactors.x, lineColor[1] * blendFactors.y);

	return float4(finalColor, 1);
}
```

Let's break this down:

### Fog

The first thing here is pair of quick fogging calculations. This yields a blending factor that I can use to fade out the lines before they hit the edges of the quad (or worse, get too crowded in the distance).

```slang
var viewDist = length(in.ViewPos);
var majorFog = Fog.Fade(viewDist);
var minorFog = Fog.ScaledFade(viewDist, float2(0.5, 0.85));
```

`Fog.Fade` and `Fog.ScaledFade` are just a couple of utility functions that call `smoothstep`:

```slang
//1 - smoothstep(NearDistance, FarDistance, value)
public float Fade(float value)
{
	return 1 - smoothstep(NearDistance, FarDistance, value);
}

//scales the near and far fog distances by fogScale.x and .y, respectively
//and then applies the regular Fade equation
//undefined if fogScale.x > fogScale.y
public float ScaledFade(float value, float2 fogScale)
{
	return 1 - smoothstep(NearDistance * fogScale.x, FarDistance * fogScale.y, value);
}
```

Why two _different_ fade factors? Well, I want the minor grid lines to vanish before the major ones go, so I need two fade factors.

### Finding out where the current pixel is in grid space

```slang
// find the index of the closest grid line to this pixel
var lineIndex = (int2)floor(in.GridCoord);
```

Simple! Remember when I said the extra math in the vertex shader's `GridCoord` calculation was going to simplify some stuff? That's one of the stuff.

How does this work, exactly? Well, think about the origin. The division by `GridSize` didn't move it at all, but adding $0.5$ _does_, and the origin is now at $(0.5, 0.5)$. But when takeing the _floor_ of that value, the result is $\lfloor(0.5, 0.5)\rfloor = (0, 0)$, as expected. But what about values _near_ the line?

Without the shift by $0.5$, there's a problem:

$$
\begin{split}
\lfloor(0.1, 0.1)\rfloor &= (0, 0) \\
\lfloor(0, 0)\rfloor &= (0, 0) \\
\lfloor(-0.1, -0.1)\rfloor &= (-1, -1)
\end{split}
$$

Oops! Can't have pixels _just to the left_ of the center line registering as being part of the next grid line over! _With_ the shift, things work as they should:

$$
\begin{split}
\lfloor(0.1 + 0.5, 0.1 + 0.5)\rfloor = \lfloor(0.6, 0.6)\rfloor &= (0, 0) \\
\lfloor(0 + 0.5, 0 + 0.5)\rfloor = \lfloor(0.5, 0.5)\rfloor &= (0, 0) \\
\lfloor(-0.1 + 0.5, -0.1 + 0.5)\rfloor = \lfloor(0.4, 0.4)\rfloor &= (0, 0)
\end{split}
$$

### Figure out how to draw the line

Now that the shader knows which line it's on, it needs to figure out what that line should look like.

There are three cases:
* The axes should be colorful and **bold**.
* Every `GridMinorsPerMajor`th line should be a bit brigher and a touch bolder.
* Every other line is a minor line that should be dim and unobtrusive. These lines also fade out earlier than the major lines and the axes.

```slang
// pick an appropriate width and color for the closest line (in *each* of X and Y!)
float2 lineWidth;
float3[2] lineColor;
float2 lineFog;
for (var i = 0; i < 2; i++)
{
	float width;
	float3 color;
	float fog;

	if (lineIndex[i] == 0)
	{
		width = 5;
		color = GetKeyColor(1 - i).rgb;
		fog = majorFog;
	}
	else if (lineIndex[i] % GridMinorsPerMajor == 0)
	{
		width = 3;
		color = 0.5;
		fog = majorFog;
	}
	else
	{
		width = 2;
		color = 0.25;
		fog = minorFog;
	}

	lineWidth[i] = width;
	lineColor[i] = color;
	lineFog[i] = fog;
}
```

Now there's one important thing to note here: the shader works in terms of the _coordinates_ that define a line. And these run _perpendicularly_ to the line itself (since the line covers _all_ parallel coordinate values). Or another way of saying that: the line whose bounds are found by scanning along the _x_-coordinate runs parallel to the _y_ axis (and has no bounds along _y_).

This is why the call to `GetKeyColor` (which does exactly what you think it does) uses `1 - i` as its index: the axis line bounded by changes in the _x_ is the _y_-axis, and it needs the correct color _for that one_.

### Finding the edges of the lines

Now that `lineWidth` is available, it's time to figure out whether the current pixel is actually _in_ its nearest line or not.

This will need some discussion:

```slang
var lineDist = abs(0.5 - frac(in.GridCoord)) * 2;
var lineMask = 1 - saturate(lineDist /
	(fwidth(in.GridCoord) * lineWidth));
```

The first thing to note, is that those expressions are computing `float2` values. That is, the math is checking _both_ the _x_ and the _y_ coordinates at the same time.

#### Computing `lineDist`

First, there's `lineDist`, which is the distance(ish) from the center of the nearest line (`lineDist.x` is the distance to the nearest line parallel to the _y_-axis, and `lineDist.y` is the same for the nearest _x_-parallel line). Why is that `* 2` in there? I dunno, it looks good. Don't overthink it.

How does this work? Well, let's consider the _y_-axis. Pixels belonging to it will be some _small_ distance _in x_ away from $0.5$. (Why $0.5$? This is that offset from the vertex shader helping out again.)

The first thing that happens is the shader computes the _fractional_ part of that coordinate. That is, it takes just the decimals. Mathematically, this looks like subtracting the _floor_ of the value from the value. Then that's subtracted from $0.5$ (again, that's our coordinate shift in action), producing a signed distance which is converted into a regular distance by taking the absolute value.

Let's see what that looks like near the _y_-axis (defined by looking at _x_-coords):

$$
\begin{split}
\mathit{dist}(x) = |0.5 - (x - \lfloor x \rfloor)|
\\
\mathit{dist}(0.1 + 0.5) = \mathit{dist}(0.6) = |0.5 - 0.6| &= 0.1 \\
\mathit{dist}(0 + 0.5) = \mathit{dist}(0.5) = |0.5 - 0.5| &= 0 \\
\mathit{dist}(-0.1 + 0.5) = \mathit{dist}(0.4) = |0.5 - 0.4| &= 0.1 \\
\end{split}
$$

There, nice and symmetrical.

#### Computing `lineMask`

```slang
var lineMask = 1 - saturate(lineDist /
	(fwidth(in.GridCoord) * lineWidth));
```

This is the tricky one. It's called the line _mask_ because this is what defines the boundary of the line. If it's _one_, the pixel is _on_ the line. If it's _zero_, the pixel is _outside_ the line. Values in between mean it's _near_ the line and that a bit of antialiasing should take place.

Starting from the _inside_ of the `saturate` function:

```slang
lineDist / (fwidth(in.GridCoord) * lineWidth)
```

What's going on here? Well, this expression needs to have a _small_ value _near_ the line and a _large_ value _away_ from the line (because it'll be subtracted from $1$ to make the _mask_). And `lineDist` already does that. So all that's really needed is to _scale_ `lineDist` by some value and job done.

But these are _lines_. Their thickness _shouldn't_ vary with distance from the camera or at glancing angles, so the scale factor needs to take into account the orientation of the quad surface to the pixel being rendered. So the scaling factor needs to be _larger_ up close (which exaggerates `lineDist` and makes the line center appear farther away from the current pixel, thus _thinning_ the line when it's right up against the camera) and _smaller_ in the distance (which does the opposite, shrinking the apparent distance to the line and making more pixels shade as if they are inside it).

That scaling factor is produced by borrowing some of the magic silicon that powers texture filtering (specifically, the bit that does the math for mip-selection), [`fwidth`](https://shader-slang.org/stdlib-reference/global-decls/fwidth.html). What does `fwidth` do? It calculates an approximate screen-relative derivative of whatever value you pass to it. You can think of it as computing the _difference_ in the given value for _this_ pixel as compared to the same value for (one of) its neighbors (and that's almost certainly how your GPU will actually compute it).

By taking the screen-space derivative of a smoothly varying value such as `GridCoord` (and really _any_ smoothly varying value would have done, up to a difference of some constant factor), the shader produces numbers which are _small_ up close and _large_ far away. Why? Well, up close the value changes _less_ from one pixel to the next because the object's all zoomed in, so adjacent pixels map to points which are closer together on the surface. Far away where perspective transformation has made the surface render _smaller_, so neighboring pixels correspond to surface patches which are farther apart, and thus the value will have changed _more_.

Multiplying _that_ value by `lineWidth` just exaggerates the effect.

But the effect is backwards: `fwidth` is bigger in the distance and smaller up close when it must be _small_ in the distance and _large_ up close. And it needs to vary over that distance not linearly, but in a manner which cancels out the perspective transformation. So for those reasons, the shader _divides_ by the scaled `fwidth`.

And that's _still_ backwards because the expression is _small_ near the line and _big_ away from it when the goal is to make a _mask_ which is _bigger_ on the line and _small_ away from it. That's what the rest of the expression is for:

```slang
var lineMask = 1 - saturate(lineDist /
	(fwidth(in.GridCoord) * lineWidth));
```

Clamping the scaled `lineDist` value to $[0, 1]$ (which is what `saturate` does) and subtracting it from $1$ then yields exactly the desired mask.

### Putting it all together

Almost there, I promise. All that's left is to deal with the fact that a pixel can fall on more than one grid line.

```slang
var blendFactors = lineMask * lineFog;
for (var i = 0; i < 2; i++)
	if (lineIndex[1 - i] == 0 && lineIndex[i] != 0)
		blendFactors[i] *= smoothstep(0, 0.5, lineDist[1 - i]);

var finalColor = max(lineColor[0] * blendFactors.x, lineColor[1] * blendFactors.y);

return float4(finalColor, 1);
```

Quick recap:
* Any pixel has the potential to be part of up to two grid lines, and thus to have color contributed to it by each of those lines.
* `lineMask` tracks whether the pixel is in or out of its nearest two grid lines.
  * `lineMask.x` is nonzero along lines parallel to the _y_-axis and zero elsewhere.
  * `lineMask.y`, similarly, is nonzero along the _x_-axis and zero elsewhere.
* The two lines contributing to the pixel may have _different_ fog values applied to them. Each contributing line's fog factor is tracked in `lineFog`'s `x` and `y` components.
* A pixel may thus have color contributed to it by up to two lines, and those colors are tracked in `lineColor`.

With those inputs, the final color is computed by scaling and adding together the two contributing colors (trusting `lineMask` and `lineFog` to scale the colors to black wherever they shouldn't be visible).

The simple approach to computing the scaling factors is just this:
```slang
var blendFactors = lineMask * lineFog;
```

That works, but it looks silly near the axes since the gray gridlines overpower the monochromatic axes and appear to be _in front_ of the axes. To prevent that, an additional correction is applied:

```slang
for (var i = 0; i < 2; i++)
	if (lineIndex[1 - i] == 0 && lineIndex[i] != 0)
		blendFactors[i] *= smoothstep(0, 0.5, lineDist[1 - i]);
```

It looks complicated but it really isn't. Read it as follows:

1. For each of the two gridlines which may be part of this pixel:
2. If the _other_ contributing gridline is an axis, and the current one is _not_ an axis,
3. dim the current gridline by an amount proportional to how close it is to the axis line.

And with the `blendFactors` ready, the shader mixes the contributing colors and ~~declares victory~~ returns the final pixel color:

```slang
var finalColor = max(lineColor[0] * blendFactors.x, lineColor[1] * blendFactors.y);

return float4(finalColor, 1);
```

Why take the (component-wise) `max` of the two instead of adding them together? Well, adding them produces distracting bright spots at all the grid intersections, and `max` is both cheap and it looks good. Who needs more reason than that?

## Slangisms

Finally, I'll explain a couple of Slangisms that might look a little odd here.

### SV_*Vulkan*VertexID

```slang
FragIn vert(uint vertexId: SV_VulkanVertexID)
```

Why `SV_VulkanVertexID` and not just the more familiar (at least to HLSL enjoyers) `SV_VertexID`? Well, HLSL specifies that `SV_VertexID` behaves differently from the nearest SPIR-V builtin when rendering is _instanced_. So Slang, which is committed to staying _close enough_ to HLSL, has to emulate the behavior by binding to _two_ builtins and subtracting one from the other in the body of the shader. Specifically, it's having to subtract `BaseVertex` from `VertexIndex`. And that `BaseVertex` SPIR-V builtin isn't baseline required functionality in Vulkan, so the capability has to be _enabled_ using [`VkPhysicalDeviceVulkan11Features::shaderDrawParameters`](https://registry.khronos.org/vulkan/specs/latest/man/html/VkPhysicalDeviceVulkan11Features.html#features-shaderDrawParameters), which is an annoying hoop to have to jump through just to get a simple vertex ID.

You can read more about the mapping between Slang's `SV_` variables and the underlying SPIR-V features [here in the Slang docs](https://docs.shader-slang.org/en/latest/external/slang/docs/user-guide/a2-01-spirv-target-specific.html).

### Structured types? Functions?

This is a VR project, and, because it uses [multiview rendering](https://www.saschawillems.de/blog/2018/06/08/multiview-rendering-in-vulkan-using-vk_khr_multiview/), the uniform buffer is a bit gnarly. I also keep changing my mind about its layout and whether certain things should go in other uniform buffers or push constants or...

In order to avoid having to edit _every_ shader _every time_ I muck with this stuff, I have it all tucked away behind a clean interface in a Slang [_module_](https://shader-slang.org/slang/user-guide/modules). Here's what some of that _actually_ looks like:

```slang
public struct ViewMatrices
{
	public float4x4 ProjView;
	public float3x4 View;
	public float3x4 InvView;
	public float4x4 Proj;
	public float4x4 InvProjView;
}

struct _SceneParams
{
	ViewMatrices ViewTransforms[2];
	uint4 KeyColors[2];
	float2 Fog; // x = near dist, y = far dist
};

[vk::binding(0, 0)]
[EngineBinding]
uniform ConstantBuffer<_SceneParams> _sceneParams;

in uint _viewportIndex : SV_ViewID;

public property ViewMatrices ViewTransform
{
	get { return _sceneParams.ViewTransforms[_viewportIndex]; }
}

public namespace Fog
{
	public property float2 NearAndFarDistance { get { return _sceneParams.Fog; } }

	public property float NearDistance { get { return _sceneParams.Fog.x; } }
	public property float FarDistance { get { return _sceneParams.Fog.y; } }

	//1 - smoothstep(NearDistance, FarDistance, value)
	public float Fade(float value)
	{
		var p = _sceneParams.Fog;
		return 1 - smoothstep(p.x, p.y, value);
	}

	//scales the near and far fog distances by fogScale.x and .y, respectively
	//and then applies the regular Fade equation
	//undefined if fogScale.x > fogScale.y
	public float ScaledFade(float value, float2 fogScale)
	{
		var p = _sceneParams.Fog * fogScale;
		return 1 - smoothstep(p.x, p.y, value);
	}
}
```

Isn't that neat?

### Weird array syntax, bro

```slang
float3[2] lineColor;
```

That just means this:

```slang
float3 lineColor[2];
```

I don't know why I like it better than the C-ish syntax, but I do. Maybe because it reminds me of `float2x3` (in a weirdly transposed sort of way).

### Indexing vectors?

This isn't even a Slangism, HLSL can do the same thing, but it doesn't seem to show up much online, so it's worth calling out. It works as you expect, `v[1]` is the same as `v.y`.