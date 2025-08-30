---
title: Implementing GJK
time: 15:00
series: GJK
notes:
  - type: warning
    date: 2025-06-07
    text: >
        This content has been through a few migrations. Typos and other small errors may have crept in.
---
# Implementing a GJK intersection query

So, a while ago, I needed to write some intersection queries, and a bit of research naturally led me to a [GJK](http://en.wikipedia.org/wiki/Gilbert%E2%80%93Johnson%E2%80%93Keerthi_distance_algorithm)-based solution. Further research led me to [Casey's _excellent_ explanation](http://mollyrocket.com/849) of the algorithm (go watch it, it's quite good), along with [some interesting insight](https://mollyrocket.com/forums/viewtopic.php?t=271) on how to implement it simply and efficiently. Unfortunately, some of the diagrams in the posts are missing, and it's still a little daunting for a newbie to jump in and make sense of it. Hopefully, I can fix that.

Now, this isn't the "how GJK works" page. That page is [over that way](/gjk). _This_ page is all about the actual implementation, including a number of high-level simplifications which naturally fall out of the implementation.

Also note that this is an _intersection_ query. It's still GJK, but it's somewhat simplified since all we care about is whether the shapes intersect, not precisely where. If you want where, you want a variation of this algorithm which I'm not covering, as I haven't got the code for it (never needed it, never written it).

And I'm only going to deal with 3D, because that's the most useful case. It's fairly trivial to adapt the algorithm to 2D, and I'll make a note of how to do that. _Don't_ ask me about 4-or-more-D, I'm taking a geometric approach to this, and I can't visualize that many dimensions.

# Storage

GJK is an iterative algorithm, so we need to track some data from one iteration to the next. The newest point to be added will always be called $A$ in the prose and `a` in the code. Points $B$, $C$, and $D$ (in code these are `b`, `c`, and `d`) refer to previously considered and retained points. I'm going to call our search vector $\vec{v}$ (or `v`). The last thing is a flag telling us which of the points are valid: `n` is the number of points retained in the simplex from the previous step (so if `n == 2`, then $B$ and $C$ make up the simplex and $D$ is unused).

I'm going to wrap the state of the GJK solver in a simple object, which gives the following prototype:

```c++
class gjk
{
public:
    template <typename SupportMapping>
    bool intersect(SupportMapping support);
 
    template <typename SupportMappingA, typename SupportMappingB>
    bool intersect(SupportMappingA support_a, SupportMappingB support_b);
 
private:
    vec3 v;
    vec3 b, c, d;
    unsigned int n;
 
    bool update(const vec3 &a);
};
```

Of course, this doesn't _have_ to be a class. These could all just be function variables living on the stack. But writing it this way makes it easier to break up what would otherwise be a _very_ long and difficult to read update loop. And you can (and probably should) always just make the `gjk` object itself a stack variable.

The first of the `intersect` overloads is the one that's going to do all of the work. It takes the composite support mapping (which I called $S_G(\vec{v})$ in the [high-level overview](/gjk)). The second just wraps its arguments into a single support mapping and calls the first.

The intended usage is something along the lines of the following:

```c++
shape s1, s2;

frust f = camera.frustum();

gjk g;

if (g.intersect(f.support_mapping(), s1.support_mapping()))
    //render s1
    ;

if (g.intersect(f.support_mapping(), s2.support_mapping()))
    //render s2
    ;

//so on
```

# The outer loop

As Casey points out, a lot of important information is encoded in the process of getting our next point. One of the most important bits, which he doesn't stress in his video (it's sort of brought up on the forums) is right here.

So let's take a first stab at GJK's main loop. The algorithm is:

  1. Start with an empty simplex and an arbitrary search direction $\vec{v}$.
  2. Loop: 
      1. Get a new point $A = S_G(\vec{v})$.
      2. See if $A$ is further along $\vec{v}$ than the origin. If it isn't, then return "no intersection".
      3. Add $A$ to the simplex.
      4. If the simplex now encloses the origin, return "found intersection".
      5. Find the feature ($F$) of the simplex closest to the origin.
      6. Remove all points from the simplex which are not part of $F$.
      7. Based on $F$, compute a new $\vec{v}$ which is both perpendicular to $F$ and oriented towards the origin with respect to $F$.

Updating the simplex is fairly involved, since there are several cases to consider, so we'll push that part of the loop into another function (`update`). The rest comes out to something like the following:

```c++
template <typename SupportMapping>
bool gjk::intersect(SupportMapping support)
{
    v = vec3{1, 0, 0}; //some arbitrary starting vector
    n = 0; //flag our simplex as "empty"
 
    for (;;)
    {
        vec3 a = support(v);
 
        if (dot(a, v) < 0)
            //no intersection
            return false;
 
        if (update(a))
            //adding the new point resulted in a
            //simplex which encloses the origin
            return true;
   }
}
```

See it? It's the dot product. If `update` is called, it will only be with a new point $A$ such that its dot with $\vec{v}$ is positive (well, OK, points aren't vectors, so that's actually $a-0$). That little fact is going to keep popping up and it's going to make our lives a lot easier when it's time to write `update`.

# Updating the Simplex

OK. It's time to write `update`.

Its job is to look at the union of the existing simplex (stored in variables `b`, `c`, `d`, and `n`) and the new point ($A$), and then decide what to keep and where to look for the next $A$. How it does this differs based on the size of the new simplex, so let's look at each case individually.

## When `n==0`:

This is the most trivial case. The simplex was empty in the previous iteration. We add whatever point we got and update $\vec{v}$ to point from it directly at the origin:

```c++
b = a;
v = -a;
n = 1;
 
//can't enclose anything with just one point
return false;
```

## When `n==1`

So, in this case, `n` indicated that we had one point left from the last iteration. The addition of the new point $A$ gives us two, which forms a line segment.

<script type="text/javascript">
    sketch.load(class {
        constructor(canvas) {
            canvas.add(this.grid = new sketch.grid())

            this.pa = new sketch.point(0.55, 0.35, {fill: '#F00', label: 'A'})
            this.pb = new sketch.point(-0.25, -0.65, {fill: '#00F', label: 'B'})

            canvas.add(new sketch.line(this.pa, this.pb, {stroke: '#AAA'}))

            canvas.add(new sketch.line(this.pb, {x: 0, y: 0}, {stroke: '#0A0'}))
            this.la0 = new sketch.line(this.pa, {x: 0, y: 0}, {stroke: '#00F'})
            canvas.add(this.la0)

            canvas.add(new sketch.halfspace(this.pa, this.pb, {stroke: '#F00', fill: '#400A'}))
            canvas.add(new sketch.halfspace(this.pb, this.pa, {stroke: '#00F', fill: '#004A'}))

            canvas.add(this.h0 = new sketch.halfspace(this.pb, {x: 0, y: 0}, {stroke: '#0F0', fill: '#040A'}))
            canvas.add(this.h1 = new sketch.halfspace({x: 0, y: 0}, this.pb, {stroke: '#0F0', fill: '#040A', side: -1}))

            canvas.add(this.pa, true)
            canvas.add(this.pb, true)
        }

        update() {
            var isValid = !this.h1.contains(this.pa)
            this.grid.stroke = isValid ? '#444' : '#600'
            this.la0.stroke = isValid ? '#00F' : '#F00'
        }
    });
</script>

So let's look at what we've got on the diagram:
* Our old point $B$ is in blue. You can drag it around if you like.
* Our new point $A$ is in red. It can also be moved.
* The origin $0$ is at the center of the grid.
* Our search vector $\vec{v}$ (the one we passed to the support mapping to get $A$) is the vector from $B$ to $0$. We represent it using a line segment, shown here in green, from $B$ to the origin.
* The line segment from $A$ back to the origin in either blue - when the configuration is valid - or red - when it is invalid. The meaning of "valid" is discussed below.
* The grid will also turn red if the configuration is invalid.

In addition to the basics, we have a number of half-spaces. This is a side-on view into 3D space, so the halfspace's bounding plane is drawn as a colored line and its "back" side is indicated with the gradient shading.
* The space behind the blue plane contains the points closest to $B$ (rather than $A$ or $\overline{AB}$).
* The space behind the red plane contains the points closest to $A$ (rather than $B$ or $\overline{AB}$).
* The spaces behind the two green planes are places where we $A$ _cannot_ appear in any valid configuration.

### Where $A$ must never be

Let's expand on the meaning of the green planes and why $A$ can't ever be behind them.

The one which passes through $B$ is trivial. That indicates the space which is in the opposite direction of the search vector $\vec{v}$. That is, in an earlier step we had a simplex consisting of $B$. We asked the support mapping for a point _closer_ to the origin and instead got a point _farther away_ than the one we already had. If that ever happens, the support mapping is simply buggy and broken (and it's not this algorithm's job to magically figure out what the programmer _intended_).

The one which passes through the origin is also pretty straightforward. If the new point $A$ is not _at least_ as far along $\vec{v}$ as the origin itself, then there's no way we're going to be able to enclose the origin in a simplex, because the support mapping has just told us that we can't get to the other side of it. So this is exactly the case we trivially reject in our loop.

### Where the origin can never be

Note that there are some further consequences which we can derive from the above.

First, the origin can never be in the Voronoi region closest to $B$. Why? Well, if it were then that would imply that $A$ is in the first forbidden region (the one that indicates a broken support mapping).

Second, the origin can never be in the Voronoi region closest to $A$. Again, this is because if it were then $A$ would have to be in the second forbidden region (the one that fails the whole intersection test without running the `update` step).

So the only place where the origin _can_ be when a 2-simplex is being updated is in the Voronoi region closest to the line segment.

You can play around with the diagram to quickly see the relationship between the exclusion of $A$ from the two green halfspaces and the exclusion of $0$ from the red and blue halfspaces. If you want to convince yourself more rigorously, pay attention to the sides of the triangle formed by $B$, $A$, and $0$ and the places where interior angles at $A$ and $B$ go through $90^{\circ}$.

And, since two of the three Voronoi regions are now excluded, the update from one point in the simplex to two is trivial. There's nothing to check. The result is a new simplex containing both points, and we can just update our variables and compute a new $\vec{v}$.

```c++
v = cross_aba(b - a, -a);
 
c = b;
b = a; //silly, yes, we'll come back to this
n = 2;
 
//can't possibly contain the origin unless we've
//built a tetrahedron, so just return false
return false;
```

Where `cross_aba` is a simple helper function defined as follows:

```c++
//return a vector perpendicular to a and
//parallel to (and in the direction of) b
inline vec3 cross_aba(const vec3 &a, const vec3 &b)
{
    return cross(cross(a, b), a);
}
```


## When `n==2`

This case is a little more involved, but here goes:

<script type="text/javascript">
    sketch.load(class {
        constructor(canvas) {
            canvas.add(this.grid = new sketch.grid())

            this.pa = new sketch.point(0.45, 0.35, {fill: '#F00', label: 'A'})
            this.pb = new sketch.point(-0.25, -0.65, {fill: '#00F', label: 'B'})
            this.pc = new sketch.point(-0.55, 0.15, {fill: '#00F', label: 'C'})

            canvas.add(new sketch.line(this.pb, this.pc, {stroke: '#04C', toInfinity: true}))
            canvas.add(new sketch.line(this.pa, this.pb, {stroke: '#AAA'}))
            canvas.add(new sketch.line(this.pa, this.pc, {stroke: '#AAA'}))

            canvas.add(new sketch.halfspace(this.pa, this.pb, {stroke: '#F00', fill: '#400A', _slice: 1}))
            canvas.add(new sketch.halfspace(this.pa, this.pc, {stroke: '#F00', fill: '#400A', _slice: -1}))

            canvas.add(new sketch.halfspace(this.pb, this.pc, {stroke: '#00F', fill: '#004A', _slice: 1}))
            canvas.add(new sketch.halfspace(this.pb, this.pa, {stroke: '#00F', fill: '#004A', _slice: -1}))

            canvas.add(new sketch.halfspace(this.pc, this.pa, {stroke: '#00F', fill: '#004A', _slice: 1}))
            canvas.add(new sketch.halfspace(this.pc, this.pb, {stroke: '#00F', fill: '#004A', _slice: -1}))

            this.pl = {x: 0, y: 0}
            canvas.add(new sketch.line(this.pl, {x: 0, y: 0}, {stroke: '#0A0'}))

            canvas.add(this.h0 = new sketch.halfspace({x: 0, y: 0}, this.pl, {stroke: '#0F0', fill: '#040A', side: -1}))

            canvas.add(this.pa, true)
            canvas.add(this.pb, true)
            canvas.add(this.pc, true)
        }

        update() {
            //search vector from the line segment
            var dx = this.pc.x - this.pb.x
            var dy = this.pc.y - this.pb.y

            var s = 1 / Math.sqrt(dx * dx + dy * dy)
            dx *= s
            dy *= s

            var dpx = -this.pb.x
            var dpy = -this.pb.y

            var dotP = dpx * dx + dpy * dy
            this.pl.x = this.pb.x + dx * dotP
            this.pl.y = this.pb.y + dy * dotP

            var isValid = !this.h0.contains(this.pa)
            this.grid.stroke = isValid ? '#444' : '#600'

            var w = sketch.math.winding(this.pa, this.pb, this.pc)
            for (var d of this.canvas.drawables)
                if (d._slice !== undefined)
                    d.slice = d._slice * w
        }
    });
</script>

So what have we got here?
* We've got our old $n=2$ simplex in blue, its points now labeled $B$ and $C$.
* The new point under consideration is $A$, in red.
* The search vector, $\vec{v}$, is perpendicular to the line between them and oriented towards the origin (represented here as the green line segment).
* The green halfspace indicates the region where $A$ cannot appear (as in the previous case, this is because that would have triggered the early-out condition in the GJK loop _or_ it would indicate a buggy support mapping).
* Again, invalid configurations (where $A$ is in the green halfspace) turn the grid red.

### Where the origin can never be

Again, we can deduce a few things from the setup here:

The regions closest to $B$ and $C$ are now _a subset_ of the regions that were closest to the endpoints of that line segment during the $n=1$ update which built the line segment in the first place. Adding $A$ doesn't change what we already knew about $B$ and $C$, so the origin can't be in either of those spaces.

The origin can't be in the region _outside_ the triangle and closest to line segment $\overline{BC}$. If it were, then that would mean that $A$ is located in the opposite direction of $\vec{v}$, which is an invalid configuration, and we'd have hit the early-out before even attempting to run an `update` step.

The origin can't be in the (red) region closest to $A$. As in the `n==1` case, there's no way for the origin to be there without $A$ being on the wrong side of the green haflspace boundary.

So we have only three valid cases to check:
* The origin might be in $\triangle{ABC}$.<br>
  This is actually two subcases. Remember, we're looking at an edge-on view of 3D space, and while the projection of the origin may fall withhin the triangle, the origin itself may be _above_ or _below_ the triangle's plane. (_On_ the plane can be folded into one of those cases.)
* The origin might be closest to $\overline{AB}$.
* The origin might be closest to $\overline{AC}$.

```c++
vec3 ao = -a; //silly, yes, clarity is important

//compute the vectors parallel to the edges we'll test
vec3 ab = b - a;
vec3 ac = c - a;

//compute the triangle's normal
vec3 abc = cross(ab, ac);

//compute a vector within the plane of the triangle,
//pointing away from the edge ab
vec3 abp = cross(ab, abc);

if (dot(abp, ao) > 0)
{
    //the origin lies outside the triangle,
    //near the edge ab
    c = b;
    b = a;

    v = cross_aba(ab, ao);

    return false;
}

//perform a similar test for the edge ac

vec3 acp = cross(abc, ac);

if (dot(acp, ao) > 0)
{
    b = a;
    v = cross_aba(ac, ao);

    return false;
}

//if we get here, then the origin must be
//within the triangle, but we care whether
//it is above or below it, so test

if (dot(abc, ao) > 0)
{
    d = c;
    c = b;
    b = a;

    v = abc;
}
else
{
    d = b;
    b = a;

    v = -abc;
}

n = 3;

//again, need a tetrahedron to enclose the origin
return false;
```

Note that the points are stored in a different order depending on which side of the triangle the origin is found on. This is so that when we find our next point, the triangle of old points will be wound such that the origin is "above" it, which simplifies things greatly.

## When `n==3`

<div class="alignright caption-box">
  <img src="/assets/img/gjk-qtips-300x227.jpg" alt="A model tetrahedron made of Q-tips and scotch tape." title="Model Tetrahedron" width="300" height="227" />

  A model tetrahedron I built out of Q-tips in order to wrap my brain around this case.
</div>

Yeah, I'm not going to diagram this one out. When I was learning, I actually ended up building a little model and then folding bits of paper and standing them up beside it in order to visualize everything. (It's _almost_, but thankfully not quite, too silly to work.)

Anyway, let's think about this.

We've got our incoming simplex, which is a triangle, with its vertices $B$, $C$, and $D$ (note again how at each step we shift the old vertices so that the new one is always $A$). Each of its edges has been tested during its construction, and we know that the origin is not in any edge's Voronoi region. That leaves us with an infinitely long triangular prism (aligned with the triangle's face normal - again, those diagrams above were edge-on views of 3D configurations) in which the origin may be found.

But is it even infinitely long? Well, no. We were careful to store our triangle such that it was wound with respect to which side the origin falls on - the origin isn't "below" the triangle, so half of our infinitely tall prism is gone - cut off by the plane the base triangle sits in. Further, we have our new point $A$, which is "above" not only the triangle but also the origin (by the dot product in the outer loop). This leaves us a proper triangular prism.

As was the case in the `n==1` and `n==2` cases, and for exactly the same reason, $A$'s Voronoi region is excluded in all valid configurations.

That leaves us with the following cases that we have to handle: three new edges, three new faces, and the volume within the tetrahedron.

The final (and most interesting, since it lets us terminate the algorithm) case is when the point is inside the tetrahedron. The only way we're going to find it there is if we find it behind all three of the new faces, so it's natural to test the three planes first. And if it's not behind the planes, then it's in front of one or at most two of them. When the point is in front of one face, we consider it and its edges. When it's in front of two face, we have to consider them both and all three edges.

```c++
/*
We've got a tetrahedron ABCD.

BCD is the base, and we know that the origin
is NOT below its plane.

A is the tip, and we know that the origin is
NOT above the plane passing through A and parallel
with that of BCD.

First we need to test if the origin is in front
of the planes of faces ABC, ACD, or ADB. If it's
behind all of them, then it's inside the tetrahedron.

If the origin is in front of ONE of the faces, then
we need to check if it's in the vornoi volume of the
face or if it's in the region of one of its edges.

If the origin is in front of TWO of the faces, then
we may have to do some extra checks around the region
of the shared triangle edge.

If the origin is in front of all three, then the
whole thing is a weird degenerate mess. Everything
is so close together that we call the origin "in"
and bail.
*/

vec3 ao = -a;

vec3 ab = b - a;
vec3 ac = c - a;
vec3 ad = d - a;

vec3 abc = cross(ab, ac);
vec3 acd = cross(ac, ad);
vec3 adb = cross(ad, ab);

vec3 tmp;

const int over_abc = 0x1;
const int over_acd = 0x2;
const int over_adb = 0x4;

int plane_tests =
    (dot(abc, ao) > 0 ? over_abc : 0) |
    (dot(acd, ao) > 0 ? over_acd : 0) |
    (dot(adb, ao) > 0 ? over_adb : 0);

switch (plane_tests)
{
case 0:
    //behind all three faces, thus inside the tetrahedron - we're done
    return true;

    /*
    The checks we do are structured the same no matter
    how things are oriented. So instead of writing them
    out three times each, we simply treat one orientation
    as the usual case to be tested. If we find ourselves
    in another orientation, we rotate the vertices around
    into the expected formation and carry on normally.

    We jump to different parts of the test depending on
    whether we've found the origin in front of one or two
    faces.
    */

case over_abc:
    goto check_one_face;

case over_acd:
    //rotate ACD into ABC

    b = c;
    c = d;

    ab = ac;
    ac = ad;

    abc = acd;

    goto check_one_face;

case over_adb:
    //rotate ADB into ABC

    c = b;
    b = d;

    ac = ab;
    ab = ad;

    abc = adb;

    goto check_one_face;

case over_abc | over_acd:
    goto check_two_faces;

case over_acd | over_adb:
    //rotate ACD, ADB into ABC, ACD

    tmp = b;
    b = c;
    c = d;
    d = tmp;

    tmp = ab;
    ab = ac;
    ac = ad;
    ad = tmp;

    abc = acd;
    acd = adb;

    goto check_two_faces;

case over_adb | over_abc:
    //rotate ADB, ABC into ABC, ACD

    tmp = c;
    c = b;
    b = d;
    d = tmp;

    tmp = ac;
    ac = ab;
    ab = ad;
    ad = tmp;

    acd = abc;
    abc = adb;

    goto check_two_faces;

default:
    //degenerate case
    return true;
}

check_one_face:

/*
We have:
    A CCW wound triangle ABC

The point is:
    In front of the plane ABC
    Above the base triangle (containing BC)
    NOT in the Voronoi region of BC

The point may be:
    In the region of AB or AC
    In the region of ABC
*/

if (dot(cross(abc, ac), ao) > 0)
{
    //in the region of AC

    b = a;

    v = cross_aba(ac, ao);

    n = 2;

    return false;
}

check_one_face_part_2:

/*
We have:
    A CCW wound triangle ABC

The point is:
    In front of the plane ABC
    Above the base triangle (containing BC)
    NOT in the Voronoi region of BC
    NOT in the Voronoi region of AC

The point may be:
    In the region of AB
    In the region of ABC
*/

if (dot(cross(ab, abc), ao) > 0)
{
    //in the region of edge AB

    c = b;
    b = a;

    v = cross_aba(ab, ao);

    n = 2;

    return false;
}

//in the region of ABC

d = c;
c = b;
b = a;

v = abc;

n = 3;

return false;

check_two_faces:
/*
We have:
    A CCW wound triangle ABC
    A CCW wound triangle ACD

The point is:
    In front of the plane ABC
    In front of the plane ACD
    Above the base of triangle BCD (containing BC and CD)
    NOT in the Voronoi region of BC or CD

The point may be:
    In the region of AB, AC, or AD
    In the region of ABC or ACD
*/

if (dot(cross(abc, ac), ao) > 0)
{
    //the origin is beyond AC from ABC's
    //perspective, effectively excluding
    //AB and ABC from consideration

    //we thus need test only ACD

    b = c;
    c = d;

    ab = ac;
    ac = ad;

    abc = acd;

    goto check_one_face;
}

//at this point we know we're either over
//ABC or over AB - all that's left is the
//second half of the one-fase test

goto check_one_face_part_2;
```

It's a big block of code, but look closely. All we're doing in the first part is testing the planes of the three new faces. These checks are simplified because we took care in the `n==2` case to make sure we stored our triangle with a consistent winding (no fiddling around figuring out which side of these plans is the "inside"). Based on these checks, we find ourselves in one of the following cases:

  * 1 way for the point to be in the tetrahedron
  * 3 ways for the point to be in front of a single face
  * 3 ways for the point to be in front of two faces

While there are seven individual cases, there are only three _kinds_ of cases. Each of the subcases within the latter two types is just a rotation of the others, so we pick one to solve, and if we detect a rotated version we just shuffle our points around, effectively rotating the case we have into the case we've solved. That simplifies things immensely.

And then things get even simpler. Once we've rotated into a standard case and jumped to the part of our function that handles that case, we quickly notice that a lot of the subcases are highly similar. For instance, if the point is in front of a single face, then we've basically got a triangle (our $n=2$ case), except we know the origin won't be under the triangle. If it's in front of two faces, we check the shared edge first, with respect to one of the faces. If the origin is out past that edge, then it could be anywhere in the region of the other face or its edges - but that's just a single face now, so we can treat that like the previous case where the point is only in front of one face's plane. If that test doesn't exclude the face we're testing relative to, then again we're testing a single face, only we don't have to bother retesting that shared edge.

# Robustness, precision, performance

This code is a _boolean_ intersection query. It doesn't tell you _where_ the convex objects intersect. Further, it's intended to be used as a rendering optimization (is this object in view? can I skip rendering that shadow map? etc.), so it isn't even a hundred percent precise (that's the code you've been seeing in particular that's not precise, not the logic of the algorithm itself). It's designed this way for performance reasons, so let's discuss exactly what these limitations are and how to deal with them (because if you run it as is on any nontrivial inputs it will eventually get stuck in an infinite loop).

First off, since we don't care about _where_ the intersection is, we can avoid normalizing a lot of our intermediate values since we're only interested in doing a series of plane side tests. That said, depending on the scale of your world, `cross_aba` can produce some monstrously large vectors. Your support mapping functions either need to deal with this, or you can normalize $\vec{v}$ at the end of each update and not worry about it beyond that.

Second, these are floating point numbers we're working with. You can get into situations where rounding errors leave you in an infinite loop as your support mapping bounces back and forth between a small number of nearby points. You could write a bunch of code to detect this, maybe sprinkle in some epsilons ([which are not just 0.00001](http://realtimecollisiondetection.net/pubs/)), but in practice it's such a vanishingly rare case that - for the simplified motivating case we're working with here - it's easier to just cap the total number of iterations through the outer loop and conservatively declare that there's an intersection if you blow past that limit (which it almost certainly is if your support mapping is reasonably well behaved). Obviously this won't work if you're doing physics calculations, but that necessitates some additional work to extract contact points anyway, and the added work of getting this 100% right probably folds together nicely with that.

Finally, take a look back over the three cases up above. The $n=0$ case always exits with $n$ set to $1$, and the $n=1$ case always exits with $n$ equal to $2$. And no case after that ever sets $n$ to anything less than $2$. So the $n=1, 2$ cases can be collapsed into the setup code above the loop, leaving only two cases ($n=2$, $n=3$) for the actual `update` function to deal with.

Putting that all together and cleaning up a bit, we get:

```c++
template <typename SupportMapping>
bool gjk::intersect(SupportMapping support)
{
    v = vec3c{1, 0, 0}; //some arbitrary starting vector

    c = support(v);
    if (dot(c, v) < 0)
        return false;

    v = -c;
    b = support(v);

    if (dot(b, v) < 0)
        return false;

    v = cross_aba(c - b, -b);
    n = 2;

    for (int iterations = 0; iterations < 32; iterations++)
    {
        vec3 a = support(v);

        if (dot(a, v) < 0)
            return false;

        if (update(a))
            return true;
    }

    //out of iterations, be conservative
    return true;
}
```

# GJK in 2D

The algorithm is fairly trivial to reduce to 2D. For one thing, the $n=3$ case is entirely gone, since a triangle is enough to enclose the origin in 2D. The `cross` and `cross_aba` calls need to be replaced with the appropriate "find me a vector perpendicular to $\vec{x}$" formula, and the end of the $n=2$ case (where we check if we're above/below the triangle) simply becomes `return true`.