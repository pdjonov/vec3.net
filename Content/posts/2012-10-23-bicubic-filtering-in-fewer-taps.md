---
title: Bicubic Filtering in Fewer Taps
tags:
  - code
  - graphics
  - hlsl
  - math
  - programming
  - shaders
---
## Author's Note

This post is based on the technique described in [GPU Gems 2, chapter 20, Fast Third-Order Texture Filtering](http://http.developer.nvidia.com/GPUGems2/gpugems2_chapter20.html). While that's certainly a good read, I found that the authors skipped over a lot of detail and optimized a little prematurely, making the result rather difficult to parse. If you've read _and understood_ their paper, then this isn't going to be news to you.

## Bicubic Filtering Has a Terrible Worst Case

The trouble with naively implementing Bicubic filtering in a shader is that you end up doing sixteen texture taps. That's rather inefficient. A simple improvement might be to separate the horizontal and vertical passes, bringing it down to eight taps, however you then incur an extra render target swap, as well as having to come up with the memory for that extra render target, which can be a deal-breaker on some architectures.

## Exploiting Bilinear Filtering

However, GPUs come with texture sampling hardware that take and blend four individual taps at once. We call this bilinear filtering, and it's the most commonly used texture filtering in 3D. And by carefully selecting our coordinates, we can take up to four of our taps at once, bringing the best case for bicubic filtering down to four taps - even better than separating the filter out into horizontal and vertical passes.

The rest of this post will show how to exploit bilinear filtering to implement a 4-tap B-Spline bicubic filter and a 9-tap Catmull-Rom filter.

### The Reference Implementation

How does this work?

Let's start with the naive implementation:

```hlsl
Texture2D g_Tex; //the texture we're zooming in on
SamplerState g_Lin; //a sampler configured for bilinear filtering
 
float4 ps_main( float2 iTc : TEXCOORD0 )
{
    //get into the right coordinate system
 
    float2 texSize;
    g_Tex.GetDimensions( texSize.x, texSize.y );
    float2 invTexSize = 1.0 / texSize;
 
    iTc *= texSize;
```

This bit could be replaced with a couple of multiplications and some global uniforms. I'm including it here so it's utterly clear what coordinate space we're in, as that's very important.

```hlsl
    //round tc *down* to the nearest *texel center*
 
    float2 tc = floor( iTc - 0.5 ) + 0.5;
```

The one-half offsets are important here. We're doing our own filtering here, so we want each of our samples to land directly on [a texel center](http://msdn.microsoft.com/en-us/library/windows/desktop/cc308049(v=vs.85).aspx#Texel), so that no filtering is done by the hardware, even if our sampler is set to bilinear.

```hlsl
    //compute the fractional offset from that texel center
    //to the actual coordinate we want to filter at
 
    float2 f = iTc - tc;
 
    //we'll need the second and third powers
    //of f to compute our filter weights
 
    float2 f2 = f * f;
    float2 f3 = f2 * f;
 
    //compute the filter weights
 
    float2 w0 = //...
    float2 w1 = //...
    float2 w2 = //...
    float2 w3 = //...
```

Remember, we've got two sets of four weights. One set is horizontal, one vertical. We can generally compute the corresponding horizontal and vertical pairs at once.

So `w0.x` is the first horizontal weight, `w0.y` is the first vertical weight. Similarly, `w1.x` is the second horizontal weight, and so on.

The actual weight equations vary depending on the filtering curve you're using, so I'm just going to omit that detail for now.

We also need to compute the coordinates of our sixteen taps. Again, these are separable in the horizontal and vertical directions, so we just have four coordinates for each, which we'll combine later on:

```hlsl
    //get our texture coordinates
 
    float2 tc0 = tc - 1;
    float2 tc1 = tc;
    float2 tc2 = tc + 1;
    float2 tc3 = tc + 2;
 
    /*
        If we're only using a portion of the texture,
        this is where we need to clamp tc2 and tc3 to
        make sure we don't sample off into the unused
        part of the texture (tc0 and tc1 only need to
        be clamped if our subrectangle doesn't start
        at the origin).
    */
 
    //convert them to normalized coordinates
 
    tc0 *= invTexSize;
    tc1 *= invTexSize;
    tc2 *= invTexSize;
    tc3 *= invTexSize;
```

And finally, we take and blend our sixteen taps.

```hlsl
   return
        g_Tex.Sample( g_Lin, float2( tc0.x, tc0.y ) ) * w0.x * w0.y
      + g_Tex.Sample( g_Lin, float2( tc1.x, tc0.y ) ) * w1.x * w0.y
      + g_Tex.Sample( g_Lin, float2( tc2.x, tc0.y ) ) * w2.x * w0.y
      + g_Tex.Sample( g_Lin, float2( tc3.x, tc0.y ) ) * w3.x * w0.y
 
      + g_Tex.Sample( g_Lin, float2( tc0.x, tc1.y ) ) * w0.x * w1.y
      + g_Tex.Sample( g_Lin, float2( tc1.x, tc1.y ) ) * w1.x * w1.y
      + g_Tex.Sample( g_Lin, float2( tc2.x, tc1.y ) ) * w2.x * w1.y
      + g_Tex.Sample( g_Lin, float2( tc3.x, tc1.y ) ) * w3.x * w1.y
 
      + g_Tex.Sample( g_Lin, float2( tc0.x, tc2.y ) ) * w0.x * w2.y
      + g_Tex.Sample( g_Lin, float2( tc1.x, tc2.y ) ) * w1.x * w2.y
      + g_Tex.Sample( g_Lin, float2( tc2.x, tc2.y ) ) * w2.x * w2.y
      + g_Tex.Sample( g_Lin, float2( tc3.x, tc2.y ) ) * w3.x * w2.y
 
      + g_Tex.Sample( g_Lin, float2( tc0.x, tc3.y ) ) * w0.x * w3.y
      + g_Tex.Sample( g_Lin, float2( tc1.x, tc3.y ) ) * w1.x * w3.y
      + g_Tex.Sample( g_Lin, float2( tc2.x, tc3.y ) ) * w2.x * w3.y
      + g_Tex.Sample( g_Lin, float2( tc3.x, tc3.y ) ) * w3.x * w3.y;
}
```

Again, this bears repeating: it doesn't matter that `g_Lin` is set for bilinear filtering. All of these taps are landing dead center on a single texel, so no filtering is being done in any of them.

### Collapsing Adjacent Taps

OK. So, starting with that. What have we got? Well, as mentioned, these filters are fully separable, so we can carry right on treating both dimensions identically, and things will just work. So let's keep things simple and work with just one dimension for now.

We're blending the values of four adjacent texels $T$, at offsets $-1$, $0$, $+1$, and $+2$. Let's call these values $T_{-1}$, $T_0$, $T_{+1}$, and $T_{+2}$ (these are sampled from our texture at `tc0.x`, `tc1.x`, etc - the subscripts correspond to the offsets, I'm just switching to math-friendly notation). Each of those gets multiplied by the corresponding weight, $w_{-1}$, $w_0$, $w_{+1}$, $w_{+2}$. We also know that our weights add up to $1$ (because we're using a well-behaved weight function).

If we look at just the last two adjacent samples, we've got the following:

$$
C_{+1,+2} = w_{+1}T_{+1} + w_{+2}T_{+2}  
$$

Now, if we did a linear (not bilinear, we're working in 1D at the moment) sample somewhere between those two texels, at coordinate $+(1+t)$ (that's $t$ units to the right of the offset $+1$, where $0 \le t \le 1$), we'd end up with the following:

$$
L_{+(1+t)} = (1-t)T_{+1} + tT_{+2}
$$

And that's pretty close to the equation that we want. We just need to find a $t$, that yields an equivalent expression.

First, we take a look at the weights in the linear blend ($t$ and $1-t$) which clearly add up to $1$, whereas $w_{+1}$ and $w_{+2}$ clearly don't. To start, we'll need to scale our weights by some value $s$ so that they have the same property:

$$
\begin{align}
s(w_{+1} + w_{+2}) &= 1 \\
s &= \frac{1}{w_{+1} + w_{+2}}
\end{align}
$$

Playing with these a little more we get:

$$
\begin{align}
sw_{+1} + sw_{+2} &= 1 \\
sw_{+1} &= 1 - sw_{+2}
\end{align}
$$

And that makes $sw_{+2}$ look suspiciously like the $t$ we're looking for. Plugging it in to check (remembering to multiply left side of the blend equation by $s$ to match what we did to our weights):

$$
\begin{align}
C_{+1,+2} &= w_{+1}T_{+1} + w_{+2}T_{+2} \\
sC_{+1,+2} &= sw_{+1}T_{+1} + sw_{+2}T_{+2} \\
sC_{+1,+2} &= (1-sw_{+2})T_{+1} + sw_{+2}T_{+2}
\end{align}
$$

Substituting $t=sw_{+2}$:

$$
\begin{align}
sC_{+1,+2} &= (1-t)T_{+1} + tT_{+2} \\
sC_{+1,+2} &= L_{+(1+t)} \\
C_{+1,+2} &= s^{-1}L_{+(1+t)} \\
&= (w_{+1} + w_{+2})L_{+(1+t)}
\end{align}
$$

And we've just turned two individual taps into a single linear tap. If we apply this in two dimensions at once, we can turn four taps into a single bilinear tap, reducing the original sixteen-sample shader to a much more manageable four samples:

```hlsl
    //get our texture coordinates
 
    float2 s0 = w0 + w1;
    float2 s1 = w2 + w3;
 
    float2 f0 = w1 / (w0 + w1);
    float2 f1 = w3 / (w2 + w3);
 
    float2 t0 = tc - 1 + f0;
    float2 t1 = tc + 1 + f1;
 
    //and sample and blend
 
    return
        g_Tex.Sample( g_Lin, float2( t0.x, t0.y ) ) * s0.x * s0.y
      + g_Tex.Sample( g_Lin, float2( t1.x, t0.y ) ) * s1.x * s0.y
      + g_Tex.Sample( g_Lin, float2( t0.x, t1.y ) ) * s0.x * s1.y
      + g_Tex.Sample( g_Lin, float2( t1.x, t1.y ) ) * s1.x * s1.y;
}
```

We can also exploit the fact that these weights add up to one to turn most of those multiplies into a trio of `lerp` calls, if we know our hardware is better at executing those than a few extra multiplications.s

And there it is! Bicubic filtering in four taps.

## Not so Fast!

Now, we can't actually go blithly applying this optimization to just any bicubic filter. If you were paying attention, you'll note that there's actually a restriction that we _must_ satisfy, or the result will just be wrong. Going back to our example:

$$
\begin{array}{rcccl}
0 &\le& t &\le& 1 \\
0 &\le& sw_{+2} &\le& 1 \\
0 &\le& \frac{w_{+2}}{w_{+1} + w_{+2}} &\le& 1
\end{array}
$$

So we can't actually apply this optimization unless we know how our weights will vary as our fractional offset ($f$, corresponding to the value `f` from near the top of our shader) varies from zero to one. So let's look at some weighting functions:

## The B-Spline Weighting Function

The B-Spline weight function is defined as follows:

$$
W(d) =
\frac{1}{6}\cases{
4 + 3|d|^3 - 6|d|^2 & \text{for } 0 \le |d| \le 1 \\
(2 - |d|)^3 & \text{for } 1 \lt |d| \le 2 \\
0 & \text{otherwise}
}
$$

where $d$ is the texel to be weighted's distance from the sampling point.

Now, the piecewise nature of the function makes reasoning about this function a little daunting, as do all the absolute values we're taking, but it's actually not bad. We're sampling at four points, and we already know what the distances to those points from our sampling point are, because we computed those sampling points:

$$
\begin{align}
|d_{-1}| &= f + 1 \\
|d_0| &= f \\
|d_{+1}| &= 1 - f \\
|d_{+2}| &= 2 - f \\
\end{align}
$$

And given that $0 \le f \lt 1$, we can see that each weight cleanly falls into one case of the weighting function or another, and its piecewise definition no longer matters:

$$
\begin{align}
w_{-1} &= \frac{1}{6}(1 - f)^3 \\
w_0 &= \frac{1}{6}\left(4 + 3f^3 - 6f^2\right) \\
w_{+1} &= \frac{1}{6}\left(4 + 3(1 - f)^3 - 6(1 - f)^2\right) \\
w_{+2} &= \frac{1}{6}f^3
\end{align}
$$

In order to merge the sixteen taps down to four, we need to combine the first pair of taps into a linear tap and the second pair into another linear tap (remember, a linear 2:1 reduction becomes a is 4:1 reduction in 2D, taking 16 taps down to 4). So we need to prove that $0 \le \frac{w_0}{w_{-1} + w_0} \le 1$ and $0 \le \frac{w_{+2}}{w_{+1} + w_{+2}} \le 1$.

This is easy enough - just go to [this awesome graphing calculator](https://www.desmos.com/) and drop in the equations. You'll see that everything is well behaved over that range, and we can therefore reduce this filter to just four taps.

## The Catmull-Rom Weighting Function

This one's a little trickier. Here's the definition:

$$
W(d) =
\frac{1}{6}\cases{
9|d|^3 - 15|d|^2 + 6 & \text{for } 0 \le |d| \le 1 \\
-3|d|^3 + 15|d|^2 - 24|d| + 12 & \text{for } 1 \lt |d| \le 2 \\
0 & \text{otherwise}
}
$$

As above, this gives us four functions weighting each of our taps:

$$
\begin{align}
w_{-1} &= \frac{1}{6}\left(-3f^3 + 6f^2 - 3f\right) \\
w_0 &= \frac{1}{6}\left(6f^3 - 15f^2 + 6\right) \\
w_{+1} &= \frac{1}{6}\left(-9f^3 + 12f^2 +3f\right) \\
w_{+2} &= \frac{1}{6}\left(3f^3 - 3f^2\right)
\end{align}
$$

Unfortunately, plugging these equations into [Desmos](https://www.desmos.com/) (yes, it really is worth linking twice - check it out!) quickly shows that we can't optimize a Catmull-Rom filter down to four taps like we did the B-Spline filter.

$$
\begin{array}{rcl}
\frac{w_0}{w_{-1} + w_0} &\notin& [0, 1] \text{ for } 0 \le f \le 1 \\
\frac{w_{+2}}{w_{+1} + w_{+2}} &\notin& [0, 1] \text{ for } 0 \le f \le 1 \\
\end{array}
$$

Now, all is not lost. The reason those ratios escape from the range we're interested in is that the outer weights ($w_{-1}$ and $w_{+2}$) are negative, where the rest are positive. This makes the denominator smaller than the numerator, yielding a final value greater than one. However, the middle weights ($w_0$ and $w_{+1}$) are well-behaved and in the range $[0, 1]$. This means that $0 \le \frac{w_{+1}}{w_0 + w_{+1}} \le 1$.

So, in 1D, we can compute the filter in three taps - one for the leftmost texel, one for the center two, and one for the rightmost one. In 2D, that yields nine taps, which is a hell of a lot better than sixteen.

## Other Optimizations

These filters are separable, and the weighting functions are identical both vertically and horizontally, making it easy to compute both sets of offsets in one go:

```hlsl
    //compute the B-Spline weights
 
    float2 w0 = f2 - 0.5 * (f3 + f);
    float2 w1 = 1.5 * f3 - 2.5 * f2 + 1.0;
    float2 w2 = -1.5 * f3 + 2 * f2 + 0.5 * f;
    float2 w3 = 0.5 * (f3 - f2);
```

We also know that these weights add up to one, so we don't actually need to compute all four:

```hlsl
    float2 w0 = f2 - 0.5 * (f3 + f);
    float2 w1 = 1.5 * f3 - 2.5 * f2 + 1.0;
    float2 w3 = 0.5 * (f3 - f2);
    float2 w2 = 1.0 - w0 - w1 - w3;
```

And then there are some repeated multiplications down in our final blend, which can be factored out (though the compiler is probably already doing this for us):

```hlsl
    return
        (g_Tex.Sample( g_Lin, float2( t0.x, t0.y ) ) * s0.x
      +  g_Tex.Sample( g_Lin, float2( t1.x, t0.y ) ) * s1.x) * s0.y
      + (g_Tex.Sample( g_Lin, float2( t0.x, t1.y ) ) * s0.x
      +  g_Tex.Sample( g_Lin, float2( t1.x, t1.y ) ) * s1.x) * s1.y;
```