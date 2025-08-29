---
title: Simple Flip Book Animation in WPF
tags:
  - 'C#'
  - code
  - programming
  - Windows
  - WPF
  - XAML
---
WPF makes it easy to animate numbers, colors, sizes, and a host of other properties. Unfortunately, it isn't easy to animate an `ImageSource` property, which is what we're usually looking for when implementing a flip book animation. The closest we get out of the box is [ObjectAnimationUsingKeyFrames](http://msdn.microsoft.com/en-us/library/system.windows.media.animation.objectanimationusingkeyframes.aspx), which works, but it's very tedious to set up all of the individual key frame times.

What we really want is a more specialized animation type, and we're going to have to make it ourselves. The full code is available [right here](/assets/bits/FlipBookAnimation.cs).

Following the [official guidelines](http://msdn.microsoft.com/en-us/library/aa970564.aspx), we begin by creating an abstract animation timeline base for `ImageSource`s. This is all pretty much boilerplate, so I'm not going to go into any detail about `ImageSourceAnimationBase`.

The implementation of our actual `FlipBookAnimation` class is split into three main sections.

## The Properties

The first is the class's properties. Because these types are [Freezable](http://msdn.microsoft.com/en-us/library/ms750509.aspx), we need to take care when we define these.

The first thing to note is that, because freezable objects are _deeply_ frozen, all of our properties must themselves be freezable. So we aren't going to use a simple collection to store our frames. We're going to use a [FreezableCollection<T>](http://msdn.microsoft.com/en-us/library/vstudio/aa346595(v=vs.90).aspx). This also affects our actual `Frames` property, as we need to disallow changing it once our object is frozen.

The `Frames` property comes with two other related bits of code. One is at the very top of our class:

```csharp
[ContentProperty( "Frames" )]
```

This tells the XAML parser that our element can have `ImageSource` declarations nested directly within its element, and that those declarations should be routed to the `Frames` property.

The other bit of code which implements the [IAddChild interface](http://msdn.microsoft.com/en-us/library/vstudio/system.windows.markup.iaddchild(v=vs.90).aspx) can be ignored - it's the old way of accomplishing what the `ContentProperty` attribute does, and is just there for compatibility.

The `FrameTime` property is, thankfully, much easier. Dependency properties automatically work for all `Freezable` types, so we only need to define it and we're done.

## Implementing Freezable

Types derived from `Freezable` are required to override `CreateInstanceCore`, plus several others if they store information outside of dependency properties. We do store information outside of dependency properties, so we need to implement the whole lot.

This is, again, boilerplate. All four methods are very similar, just optimized to different tasks, so I'll just look at one:

```csharp
protected override void CloneCore( Freezable sourceFreezable )
{
    base.CloneCore( sourceFreezable );
 
    var source = (FlipBookAnimation)sourceFreezable;
 
    if( source.frames != null )
    {
        frames = (FreezableCollection<ImageSource>)
            source.frames.Clone();
        OnFreezablePropertyChanged( null, frames );
    }
}
```

This is very straight-forward. We start by calling the base implementation, which takes care of all of our dependency properties. All that's left is the `Frames` property, so we clone it manually and ensure that the clone is correctly linked to its parent. That's it.

## Evaluating the Animation

And, finally, we can compute our actual animation:

```csharp
protected override ImageSource GetCurrentValueCore(
    ImageSource defaultOriginValue,
    ImageSource defaultDestinationValue,
    AnimationClock animationClock )
{
    if( frames == null || frames.Count == 0 )
        return defaultDestinationValue;
 
    var now = animationClock.CurrentTime.Value;
 
    long frame = now.Ticks / FrameTime.Ticks;
 
    if( frame <= 0 )
        return frames[0];
 
    return frames[(int)(frame % frames.Count)];
}
```

We start by taking care of the trivial case. If we don't have any frames defined, we do nothing, and simply return the default value.

Otherwise, all we're doing is some simple math. We start by dividing the running time of the animation by `FrameTime`, the amount of time we're devoting to displaying each frame. This gives us the index of the frame we should be displaying. We do a quick sanity check, in case the animation clock supplied us with a negative time value (can that even happen?), and then we wrap the frame number by the number of frames we have defined, causing the animation to repeat if the clock runs on past the end.

```csharp
protected override Duration GetNaturalDurationCore( Clock clock )
{
    int numFrames = frames != null ? frames.Count : 0;
    return new Duration( new TimeSpan(
        FrameTime.Ticks * numFrames ) );
}
```

And the natural length of our animation is simply our frame time multiplied by the number of frames we have.

## Putting It to Use

And that's it. Using the animation type is just like using any other. You create a definition that looks something like this:

```xml
<vec3:FlipBookAnimation
    FrameTime="0:0:0.042"
 
    Storyboard.TargetName="TickingImage"
    Storyboard.TargetProperty="Source"
    >                            
    <ImageSource>./Tick00.png</ImageSource>
    <ImageSource>./Tick01.png</ImageSource>
    <ImageSource>./Tick02.png</ImageSource>
    <ImageSource>./Tick03.png</ImageSource>
    <ImageSource>./Tick04.png</ImageSource>
 
    <!-- etc -->
</vec3:FlipBookAnimation>
```

And then you just drop it into any storyboard you like, and off you go.

Here's another [link to the code](/assets/bits/FlipBookAnimation.cs) for those that skimmed right past the first one.