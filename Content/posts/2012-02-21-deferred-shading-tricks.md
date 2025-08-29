---
title: Deferred Shading Tricks
tags:
  - code
  - graphics
  - shaders
---
[Deferred shading](http://en.wikipedia.org/wiki/Deferred_shading) is a useful technique available on modern GPUs that allows one to decouple scene and lighting complexity. I'm not really writing a for-beginners article here. This is aimed at people who've got a basic engine set up and want to tune it.

# Conventions

This page assumes Direct3D clip-space conventions (the z-axis points into the screen, and the near clip distance maps to $z=0$ in clip space) and a right-handed view space. In other words, the [projection matrix and its inverse](/code/math/projection-direct3d) look like this:

$$
P=\begin{bmatrix}
P_{11} & 0 & 0 & 0 \\
0 & P_{22} & 0 & 0 \\
0 & 0 & P_{33} & P_{34} \\
0 & 0 & -1 & 0
\end{bmatrix},\;
P^{-1}=\begin{bmatrix}
P_{11}^{-1} & 0 & 0 & 0 \\
0 & P_{22}^{-1} & 0 & 0 \\
0 & 0 & 0 & -1 \\
0 & 0 & P_{34}^{-1} & \frac{P_{33}}{P_{34}}
\end{bmatrix}
$$

$$
P_{33}=\frac{z_f}{z_n-z_f},\;P_{34}=\frac{z_nz_f}{z_n-z_f}
$$

$z_n$ and $z_f$ are, respectively, the distances to the near and far clip planes in view space units. See the linked page for more on $P_{11}$ and $P_{22}$.

I'm also going to be writing HLSL shader code. It's trivially convertible to GLSL, but you must correct for the difference between the Direct3D and OpenGL clip-space conventions!

# Shrinking the G-Buffer

Modern GPUs can burn through <a>ALU</a> operations at an absurd rate. What they still _can't_ do is move data in and out of memory at anything approaching the same rate. And deferred shading is, unfortunately, extremely data-hungry, mainly due to the large size of the G-buffer. Anything that shrinks the G-buffer is likely to be a performance win (if not in speed, then at least in terms of video memory available to models and textures).

## Who Needs a Position Buffer, Anyway?

One of the most infuriating parts of many G-buffer setups I've seen (particularly in educational literature) is the _position buffer_. This is typically a four-component floating-point format buffer storing view-space object positions. It's totally redundant.

Think about it - the GPU knows which screen pixel it's shading. It also has that pixel's depth value sitting in memory in the depth buffer, which you already have to have. And modern GPUs expose the target pixel location and the ability to read from the depth buffer to shaders. Everything you need to reconstruct your view-space position is _right there_ - so, unless you have some absurd precision requirements, why waste another 64-128 bits storing it all over again?

So, how do we reconstruct the position? Well, let's start by binding the z-buffer and the screen coordinate to HLSL variables and reading the depth value:

```hlsl
Texture2D<float> g_Depth;

struct PsIn
{
    float4 Pos : SV_Position;
};

float4 ps_main(PsIn px) : SV_Target0
{
    int3 screenCoord = int3(px.Pos.xy, 0);

    float clipDepth = g_Depth.Load(screenCoord);

    //...
}
```

So... Now what?

Well, for starters, `clipDepth` is already in clip space (OpenGL users: no it isn't! Your clip space is different!). We could simply pair it with the clip-space x and y coordinates (trivial to derive from `px.Pos` and the viewport) and run it through the inverse of our projection matrix... well, what the heck, let's see what that gives us.

First, we need to convert the screen-space `pt.Pos` values to clip-space. Or do we? I mean the vertex (or maybe geometry) shader has the value right there - we can just lazily capture it to another variable in our `PsIn` structure:

```hlsl
float4x4 g_InverseProjection;
Texture2D<float> g_Depth;
 
struct PsIn
{
    float2 ClipPos : MY_CLIP_POS;
    float4 Pos : SV_Position;
};
 
PsIn vs_main(float2 pos : POSITION /* in clip space */)
{
    PsIn ret;

    //...

    ret.Pos = float4(pos, 0, 1);
    ret.ClipPos = pos;

    return ret;
}
 
float4 ps_main(PsIn px) : SV_Target0
{
    int3 screenCoord = int3(px.Pos.xy, 0);

    float clipDepth= g_Depth.Load(screenCoord);

    float4 viewPosH = mul(g_InverseProjection, float4(px.ClipPos, clipDepth, 1));
    float3 viewPos = viewPosH.xyz / viewPos.w;

    //...
}
```

Well, it works! But we're not finished here. Look at that expression - a full matrix multiply _per pixel_. I know I said GPUs are good at ALU operations, but it's still silly to be spending them wastefully like this, especially in a shader which is already going to be quite ALU-heavy. So let's start picking that expression apart and see if we can't optimize things any...

Let's call `px.ClipPos` $\mathbf{c_{xy}}$, `clipDepth` $\mathbf{c_z}$ (collectively just $\mathbf{c}$), and `viewPosH` $\mathbf{h}$. That gives us:

$$
\mathbf{h}=P^{-1}\mathbf{c}=\begin{bmatrix}  
\mathbf{c_x}P_{11}^{-1} \\  
\mathbf{c_y}P_{22}^{-1} \\  
-1 \\  
\frac{\mathbf{c_z}}{P_{34}}+\frac{P_{33}}{P_{34}}  
\end{bmatrix}
$$

Take a look at $\mathbf{h}$: all of its components are completely linear. More than that, $\mathbf{h_x}$ and $\mathbf{h_y}$ are independent of any values computed in the pixel shader ($\mathbf{c_z}$), and can be computed in the vertex shader and trivially interpolated. The only tricky element is $\mathbf{c_w}$ (the one we're going to divide by), and it consists of a multiplication followed by an addition - which is, on many architectures, fused into a single instruction.

So that gives us:

```hlsl
float4 g_InvProj; // = < 1.0F/P11, 1.0F/P22, 1.0F/P34, P33/P34 >
Texture2D<float> g_Depth;

struct PsIn
{
    float2 ClipPos : MY_CLIP_POS;
    float4 Pos : SV_Position;
};

PsIn vs_main(float2 pos : POSITION /* in clip space */)
{
    PsIn ret;

    //...

    ret.Pos = float4(pos, 0, 1);
    ret.ClipPos = pos * g_InvProj.xy;

    return ret;
}
 
float4 ps_main(PsIn px) : SV_Target0
{
    int3 screenCoord = int3(px.Pos.xy, 0);

    float clipDepth = g_Depth.Load(screenCoord);

    float3 viewPos = float3(px.ClipPos, -1) / (clipDepth * g_InvProj.z + g_InvProj.w);

    //...
}
```

And there we are. We've reconstructed our view-space position from the value in the depth buffer in just a handful of instructions (a `mad` and a `div`, along with a `mov` or two depending on the surrounding code) at the cost of a small interpolator. And that interpolater could easily be removed at the cost of another `mad` instruction, though I've been unable to measure a good reason to do so when profiling.