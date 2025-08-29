---
title: "Camera Math"
updated:
  - 2025-08-10
tags:
  - math
  - graphics
  - code
  - Vulkan
  - OpenXR
---
Nothing special here, just stashing away some math I don't enjoy having to look up again and again.

Some notes for readers:
* I use _zero-indexed, column-major_ matrix layouts. If you see `m.c[2][1]` that means "matrix `m`'s _third_ column, _second_ row. In standard mathematical notation, that would be written something like $M_{23}$.
* The `m4x4` type is a regular 4x4 matrix, whereas the `ma4x4` type is a 4x4 matrix in which the last row _is not stored_ because it is defined as simply $[0, 0, 0, 1]$.
* I multiply vectors in _from the right_. Transpose all the matrices if you multiply from the left.

# View

View matrices transform _world_ space into _view_ space. Both of these spaces are defined to be whatever the particular application says they are. These are the matrices which match my preferred convention, which is:

* The x-axis points right.
* The y-axis points _up_.
* The z-axis points _out_ of the screen.
* This is a _right_-handed coordinate space.

### Look-at matrix

$$
V=\begin{bmatrix}
\hat{r_x} & \hat{r_y} & \hat{r_z} & -\hat{r} \cdot p \\
\hat{u_x} & \hat{u_y} & \hat{u_z} & -\hat{u} \cdot p \\
\hat{f_x} & \hat{f_y} & \hat{f_z} & -\hat{f} \cdot p \\
0 & 0 & 0 & 1 \\
\end{bmatrix}
$$

Following on from the convention above:
* $\hat{r}$ is a world-space unit vector pointing _right_.
* $\hat{u}$ is a world-space unit vector pointing _up_.
* $\hat{f}$ is a world-space unit vector pointing _out_ of the screen.
* $p$ is the position of the camera (or eye).

This can be constructed as follows:

```cpp
ma4x4 view_look_at(const vec3& pos, const vec3& target, const vec3& up) noexcept
{
    auto vf = normalize(pos - target);
    auto vr = normalize(cross(up, vf));
    auto vu = cross(vf, vr);

    ma4x4 ret;

    ret.c[0][0] = vr.x;
    ret.c[0][1] = vu.x;
    ret.c[0][2] = vf.x;

    ret.c[1][0] = vr.y;
    ret.c[1][1] = vu.y;
    ret.c[1][2] = vf.y;

    ret.c[2][0] = vr.z;
    ret.c[2][1] = vu.z;
    ret.c[2][2] = vf.z;

    ret.c[3][0] = -dot(vr, pos);
    ret.c[3][1] = -dot(vu, pos);
    ret.c[3][2] = -dot(vf, pos);

    return ret;
}
```

* `pos` is where the camera (or eye) is, in world space.
* `target` is a point the camera is _looking at_ (thus the function name), again in worldspace.
* `up` is a world-space vector that points towards the top of the screen. This must not be parallel to the camera's forward view direction.

Another way of constructing a view matrix is to invert the matrix that translates the camera from the origin to its actual world-space position. For instance, here's how you'd do that with OpenXR's _pose_ information which gives the world transform corresponding to the eyes:

```cpp
ma4x4 view_from_xr_eye(const XrView& eye) noexcept
{
    auto& p = eye.pose.position;
    auto t = ma4x4::translation(p.x, p.y, p.z);

    auto& o = eye.pose.orientation;
    auto r = (ma4x4)quat{o.x, o.y, o.z, o.w};

    return inverse(t * r);
};
```

# Projection

Projection matrices transform _view space_ into _clip space_. And while _view space_ is defined according to application-specific conventions, _clip-space_ is defined differently depending on which API one is using. So projection matrices are not only application but _also_ API-specific.

Further, there are variations depending on what combination of the following options an application needs:

* Perspective vs. Orthographic projection
* Standard vs. [reversed](https://iolite-engine.com/blog_posts/reverse_z_cheatsheet) z-buffering
* Fixed vs. infinite far clipping planes

I'm not going to list every combination, just the ones I find useful (and I may go back and edit this post, adding new ones over time). All of the projection matrices which follow transform _from_ my preferred view convention:

* The x-axis points right.
* The y-axis points _up_.
* The z-axis points _out_ of the screen.

## Why reversed z-buffering?

Z-buffer store values in the range $[0, 1]$. When using a floating point depth buffer format, _most_ of the precision is near zero - _very_ near to zero. Around the near-clipping plane, this is a waste since geometry typically isn't drawn _right_ up against the near-clip plane. But in the distance this is a _disaster_ as it introduces really nasty z-fighting artifacts in your nice panoramic scenes.

However, with a little tweak to the projection matrix and a quick flip of the depth test (and depth buffer clear value), we can get a better match between the floats storedin the depth buffer and the scene.

And, happily, this doesn't really degrade quality when using an integer depth buffer format.

Read more here:
* [Depth Precision Visualized](https://developer.nvidia.com/content/depth-precision-visualized)
* [Reverse Z Cheatsheet](https://iolite-engine.com/blog_posts/reverse_z_cheatsheet)
* [Reverse Z (and why it's so awesome)](https://pr0g.github.io/mathematics/graphics/2023/08/06/reverse-z.html)

To make this work in OpenGL you'll also need:
```cpp
glClipControl(GL_LOWER_LEFT, GL_ZERO_TO_ONE);
```

## Infinite far clip

Unless there's a natural "end" to your game world, the far clipping plane tends to make for a pretty ugly horizon. One way to get rid of it is to just push the far plane out to a sufficiently, well, _far_ distance and then hide it in fog. But what does "sufficiently" really mean? And do we actually _have_ to figure that out?

Well, _no_. We can, in fact, have an _infinitely_ far far-clip plane. We do this by taking the terms in the projection matrix which depend on the far-clip distance $z_f$ and finding the _limit_ of the value of those terms as $z_f$ approaches infinity.

These terms look vaguely like this (in different APIs they might differ by a minus sign):

$$
\begin{split}
P_{33}&=\frac{z_n}{z_f-z_n} \\
P_{34}&=\frac{z_nz_f}{z_f-z_n}
\end{split}
$$

$P_{33}$ is pretty straightforward. There's no $z_f$ in the numberator, so as $z_f$ grows the fraction's value shrinks, approaching zero as $z_f$ approaches infinity.

$P_{34}$ is less straightforward...

$$
\begin{split}
P_{34}&=\frac{z_nz_f}{z_f-z_n} \\
\lim\limits_{z_f\to\infty}P_{34}&=\frac{\infty}{\infty}=\,???
\end{split}
$$

Hmm. No, that's clearly not right... This calls for [L'Hôpital's rule](https://en.wikipedia.org/wiki/L%27H%C3%B4pital%27s_rule).

The numerator is clearly _linear_ with respect to $z_f$, and the slope of that line (thus the derivative of the expression) is just $z_n$.

The denominator is just a sum of terms, so the derivative is the sum of the derivatives of the terms. The constant $z_n$ term's derivative is zero, the variable $z_f$ term's derivative is one.

$$
\begin{split}
\lim\limits_{z_f\to\infty}P_{34}&=\lim\limits_{z_f\to\infty}\frac{z_nz_f}{z_f-z_n} \\
&=\lim\limits_{z_f\to\infty}\frac{\frac{d}{dz_f}z_nz_f}{\frac{d}{dz_f}(z_f-z_n)} \\
&=\lim\limits_{z_f\to\infty}\frac{z_n}{1-0} \\
&=z_n
\end{split}
$$

And applying _that_ logic to our projection matrix is how we get rid of the far clip plane altogether.

## Vulkan

The following projection matrices follow Vulkan's clip-space convention, which is:

* The x-axis points right, the y-axis points _down_, the z-axis points _into_ the screen.
* The near clipping plane is at $z=0$.

### Perspective projection, reversed-z

$$
\begin{split}
P=\begin{bmatrix}
s_x & 0 & 0 & 0 \\
0 & -s_y & 0 & 0 \\
0 & 0 & \frac{z_n}{z_f-z_n} & \frac{z_nz_f}{z_f-z_n} \\
0 & 0 & -1 & 0
\end{bmatrix}
\end{split}
\hspace{3em}
\begin{split}
s_y&=1/{tan(\tfrac{1}{2}f_y)} \\
s_x&=s_y/a \\
a&=v_w/v_h
\end{split}
$$

* $s_x$ and $s_y$ are the vertical and horizontal scaling factors which should be derived from the FoV angle and the dimensions of the screen. One way to compute these is (as listed above):
  * $f_y$ is the vertical FoV angle.
  * $a$ is the aspect ratio, defined as _viewport width_ ($v_w$) divided by _viewport height_ ($v_h$).
* $z_n$ is the distance to the near clipping plane.
* $z_f$ is the distance to the far clipping plane.

```cpp
m4x4 proj_persp_fov(
    angle fov, float aspect_w_over_h,
    float z_near, float z_far, reversed_z_t) noexcept
{
    m4x4 ret;

    auto sy = 1.0F / tan(fov * 0.5F);
    auto sx = sy / aspect_w_over_h;

    auto z_range_inv = 1.0F / (z_far - z_near);

    ret.c[0][0] = sx;
    ret.c[0][1] = 0;
    ret.c[0][2] = 0;
    ret.c[0][3] = 0;

    ret.c[1][0] = 0;
    ret.c[1][1] = -sy;
    ret.c[1][2] = 0;
    ret.c[1][3] = 0;

    ret.c[2][0] = 0;
    ret.c[2][1] = 0;
    ret.c[2][2] = z_near * z_range_inv;
    ret.c[2][3] = -1;

    ret.c[3][0] = 0;
    ret.c[3][1] = 0;
    ret.c[3][2] = z_near * z_far * z_range_inv;
    ret.c[3][3] = 0;

    return ret;
}
```

### Perspective projection, reversed-z, infinite-far-clip

Applying the [infinite far clip](/posts/camera-math#infinite-far-clip) equations to the [matrix above](/posts/camera-math#perspective-projection-reversed-z) we get:

$$
\begin{split}
P=\begin{bmatrix}
s_x & 0 & 0 & 0 \\
0 & -s_y & 0 & 0 \\
0 & 0 & 0 & z_n \\
0 & 0 & -1 & 0
\end{bmatrix}
\end{split}
\hspace{3em}
\begin{split}
s_y&=1/{tan(\tfrac{1}{2}f_y)} \\
s_x&=s_y/a \\
a&=v_w/v_h
\end{split}
$$

* $s_x$ and $s_y$ are the vertical and horizontal scaling factors which should be derived from the FoV angle and the dimensions of the screen. One way to compute these is (as listed above):
  * $f_y$ is the vertical FoV angle, measured between the top and bottom planes.
  * $a$ is the aspect ratio, defined as _viewport width_ ($v_w$) divided by _viewport height_ ($v_h$).
* $z_n$ is the distance to the near clipping plane.
* There is no far clipping plane.

```cpp
m4x4 proj_persp_fov(
    angle fov, float aspect_w_over_h,
    float z_near, infinite_z_far_t, reversed_z_t) noexcept
{
    m4x4 ret;

    auto sy = 1.0F / tan(fov * 0.5F);
    auto sx = sy / aspect_w_over_h;

    ret.c[0][0] = sx;
    ret.c[0][1] = 0;
    ret.c[0][2] = 0;
    ret.c[0][3] = 0;

    ret.c[1][0] = 0;
    ret.c[1][1] = -sy;
    ret.c[1][2] = 0;
    ret.c[1][3] = 0;

    ret.c[2][0] = 0;
    ret.c[2][1] = 0;
    ret.c[2][2] = 0;
    ret.c[2][3] = -1;

    ret.c[3][0] = 0;
    ret.c[3][1] = 0;
    ret.c[3][2] = z_near;
    ret.c[3][3] = 0;

    return ret;
}
```

### Perspective projection with assymetric FoV, reversed-z, infinite-far-clip

Same as above, but with a skewed, assymetric view frustum. This is useful in VR applications.

$$
P=\begin{bmatrix}
\frac{2}{\tan{f_r}-\tan{f_l}} & 0 & \frac{\tan{f_r}+\tan{f_l}}{\tan{f_r}-\tan{f_l}} & 0 \\
0 & \frac{2}{\tan{f_d}-\tan{f_u}} & \frac{\tan{f_d}+\tan{f_u}}{\tan{f_d}-\tan{f_u}} & 0 \\
0 & 0 & 0 & z_n \\
0 & 0 & -1 & 0 \\
\end{bmatrix}
$$

* There's now a separate FoV angle for each side of the frustum (measured from the z-axis):
  * To the _left:_ $f_l$.
  * To the _right:_ $f_r$.
  * _Up_ above: $f_u$.
  * _Down_ below: $f_d$.
* $z_n$ is the distance to the near clipping plane.
* There is no far clipping plane.

```cpp
m4x4 proj_persp_asymmetric_fov(
    angle left, angle right, angle up, angle down,
    float z_near, infinite_z_far_t, reversed_z_t) noexcept
{
    m4x4 ret;

    auto tan_l = tan(left);
    auto tan_r = tan(right);
    auto tan_d = tan(down);
    auto tan_u = tan(up);

    auto tan_w_inv = 1.0F / (tan_r - tan_l);
    auto tan_h_inv = 1.0F / (tan_d - tan_u);

    ret.c[0][0] = 2.0F * tan_w_inv;
    ret.c[0][1] = 0.0F;
    ret.c[0][2] = 0.0F;
    ret.c[0][3] = 0.0F;

    ret.c[1][0] = 0.0F;
    ret.c[1][1] = 2.0F * tan_h_inv;
    ret.c[1][2] = 0.0F;
    ret.c[1][3] = 0.0F;

    ret.c[2][0] = (tan_r + tan_l) * tan_w_inv;
    ret.c[2][1] = (tan_d + tan_u) * tan_h_inv;
    ret.c[2][2] = 0;
    ret.c[2][3] = -1.0F;

    ret.c[3][0] = 0.0F;
    ret.c[3][1] = 0.0F;
    ret.c[3][2] = z_near;
    ret.c[3][3] = 0.0F;

    return ret;
}
```

If you're using OpenXR, you use it like this:

```cpp
m4x4 proj_from_xr_eye(const XrView& eye,
    float z_near, infinite_z_far_t, reversed_z_t) noexcept
{
    auto& fov = eye.fov;
    return proj_persp_asymmetric_fov(
        angle::rads(fov.angleLeft),
        angle::rads(fov.angleRight),
        angle::rads(fov.angleUp),
        angle::rads(fov.angleDown),
        z_near, infinite_z_far,
        reversed_z);
}
```