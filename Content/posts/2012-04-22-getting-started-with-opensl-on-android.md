---
id: 584
title: Getting Started with OpenSL on Android
date: 2012-04-22T18:34:32-07:00
author: phill
guid: http://vec3.ca/?p=584
permalink: /posts/getting-started-with-opensl-on-android
categories:
  - Android
  - code
tags:
  - Android
  - audio
  - code
  - OpenSL
---
Edit: This article, as originally posted, has an error in it, which has been corrected. Anyone who just wants to see the fix can check out [this update](/posts/update-getting-started-with-opensl-on-android).

So, you want to play some audio on an Android device. You've got your NDK set up, you grab your code, you hit compile and there's a problem: OpenAL's not supported on this platform. What we have instead is partial support for [OpenSL ES 1.0](http://www.khronos.org/opensles/).

And the problem with SLES is that it's very hard to find a decent tutorial on how to use it. The [specification](http://www.khronos.org/registry/sles/specs/OpenSL_ES_Specification_1.0.1.pdf) has some examples at the end, but they're not entirely easy to follow, and they reference features not supported on Android. So, without further ado, a brief OpenSL ES 1.0 tutorial:

Before I get into actually using the API, a brief word on how it's structured, so that everything is easy to follow.

## Objects

OpenSL is object-oriented. Objects are exposed via interfaces, which are somewhat similar to [COM](http://en.wikipedia.org/wiki/Component_Object_Model). Like COM objects, OpenSL objects expose a number of interfaces, each of which is identified and retrieved using a predefined <acronym title="Interface ID">IID</acronym>.

```c++
//COM
 
IUnknown *obj;
IDXGIObject *ifc;
 
HRESULT res = obj->QueryInterface( IID_IDXGIObject, &ifc );
 
if( SUCCEEDED( res ) )
    //got our object
 
//OpenSL ES
 
SLObjectItf obj;
SLEngineItf ifc;
 
SLresult res = (*obj)->GetInterface( obj, SL_IID_ENGINE, &ifc );
 
if( res == SL_RESULT_SUCCESS )
    //got our object
```

One major difference between the two is that OpenSL doesn't declare its objects as C++ classes, so you have to dereference the object and follow the link to the [v-table](http://en.wikipedia.org/wiki/Virtual_method_table) yourself (like when accessing a COM object from plain C). The generated code is identical in both cases.

Another key difference between the two is that OpenSL interfaces do not inherit from `SLObjectItf` the way COM interfaces inherit from `IUnknown`, so you have to store the `SLObjectItf` alongside any useful interfaces you get from it so that you can manage the object's lifetime and query it for other interfaces.

### Creating Objects

OpenSL objects are created by a root engine object (except, of course, the engine object itself). Creating the engine is simple:

```c++
SLObjectItf engine_obj;
slCreateEngine( &engine_obj, 0, nullptr, 0, nullptr, nullptr );
```

The zeroes and `nullptr` arguments tell the API to use default options and that we don't require any special interfaces (more on that later).

Creating other objects is, as mentioned earlier, done through the engine object. Once the engine is set up and you've retrieved its `SLEngineItf` interface you can create other objects by using its family of Create methods.

```c++
SLEngineItf engine; //initialized elsewhere
 
SLObjectItf output_mix_obj;
 
const SLInterfaceID ids[] = { SL_IID_VOLUME };
const SLboolean req[] = { SL_BOOLEAN_FALSE };
(*engine)->CreateOutputMix( engine, &output_mix_obj, 1, ids, req );
```

Alright, time to get to those parameters, which you'll find on every Create function, that I glossed over before. OpenSL objects usually expose at least one interface besides `SLObjectItf`, but _all other interfaces are optional and may not be supported_. Furthermore, you need to tell OpenSL up front which interfaces you're going to be requesting (the `ids` array), whether you absolutely need them or not (the `req` array), and obviously how many there are (the `1`). If you mark an interface as required, but the implementation cannot provide it, object creation will fail and return `SL_RESULT_FEATURE_UNSUPPORTED`.

### Realizing Objects

OpenSL objects come in three basic states. The initial state is `SL_OBJECT_STATE_UNREALIZED`. An object in this state has not allocated the resources it will ultimately use. An object in this state is useless until a call to `Realize` succeeds. And by useless I mean you can't do _anything_ with it except check the state, register a state callback, or call `Realize`. You can't even retrieve the object's main interface when it's in this state.

The second state, `SL_OBJECT_STATE_SUSPENDED`, is something you may or may not encounter normally. It's like the unrealized state, in that the object is useless, except that it still holds its resources, maintains the values of its properties, and the caller can query them. You get out of this state by calling `Resume`.

Successfully calling either `Realize` or `Resume` puts the object in the `SL_OBJECT_STATE_REALIZED`. An object in this state is fully useable. Objects can transition out of this state if there's a system event (speakers unplugged, hardware error) or if another application takes control of the audio device. You can register a callback function to be notified if that happens, or you can catch the error you'll get from a subsequent command to that object.

Realized objects can transition _either to the suspended or to the unrealized state_. It's up to you to handle either case. The main difference between the two is that any interface pointers you hold on this object remain valid while it's in `SL_OBJECT_STATE_SUSPENDED` (though you can't set its properties or make it do anything useful). If an object falls into the `SL_OBJECT_STATE_UNREALIZED` state, you need to discard all interface pointers (except to the main `SLObjectItf`), get them back after a call to `Realize` succeeds, and then fully reinitialize all of the object's properties.

Anyway, we've created an object, and it's in the unrealized state. Let's realize it so that we can use it.

```c++
SLObjectItf obj;
(*obj)->Realize( obj, SL_BOOLEAN_FALSE );
```

The second parameter to `Realize` enables asynchronous activation. If you set it to `SL_BOOLEAN_TRUE` then `Realize` will return immediately, but the object won't actually be realized until some later time. You can register a call back or poll the object via `GetState` to see when that is.

### Destroying Objects

OpenSL also differs from COM in that its objects aren't reference-counted. When you're done with an object, call `Destroy` on its object interface:

```c++
SLObjectItf obj;
(*obj)->Destroy( obj );
```

The object and all interfaces you got from it are invalid after that call is made.

### Error Checking

I'm omitting error checking for the sake of clarity. Always be sure to check return codes, especially from functions which create objects and from `Realize`.

## The Audio Graph

If you've used [XAudio2](http://en.wikipedia.org/wiki/XAudio2), you'll be right at home with OpenSL. The API is structured around the concept of an audio graph, where AudioPlayer objects read sources and stream to other objects (such as the output mixer).

I'm not really going to go into much detail here, mainly because Android only supports a small subset of the possible features, and I think things will become clear as I go through the example.

## Using OpenSL

Right! On to the code! Again, and I can't possibly stress this enough, _check the return values_. You _will_ run into errors and unsupported features, especially on a platform as diverse as Android. _Don't_ expect things to just work.

Since each object will be accessed by a number of interfaces, I'm using a simple naming convention to make it clear when multiple interfaces all refer to the same object. The object's interface variables will all have a common root. The main `SLObjectItf` bears the suffix `_obj`, the main object-specific interface has no suffix, and any secondary or optional interfaces get their own suffix.

### Initializing OpenSL

The first thing we need to do is initialize OpenSL for playback. That means creating an Engine and an OutputMix.

```c++
//create the Engine object
 
SLObjectItf engine_obj;
SLEngineItf engine;
 
slCreateEngine( &engine_obj, 0, nullptr, 0, nullptr, nullptr );
(*engine_obj)->Realize( engine_obj, SL_BOOLEAN_FALSE );
(*engine_obj)->GetInterface( engine_obj, SL_IID_ENGINE, &engine );
 
//create the main OutputMix, try to get a volume interface for it
 
SLObjectItf output_mix_obj;
SLVolumeItf output_mix_vol;
 
const SLInterfaceID ids[] = { SL_IID_VOLUME };
const SLboolean req[] = { SL_BOOLEAN_FALSE };
 
(*engine)->CreateOutputMix( engine, &output_mix_obj, 1, ids, req );
 
(*output_mix_obj)->Realize( output_mix_obj, SL_BOOLEAN_FALSE );
 
if( (*output_mix_obj)->GetInterface( output_mix_obj,
    SL_IID_VOLUME, &output_mix_vol ) != SL_RESULT_SUCCESS )
    output_mix_vol = nullptr;
```

Alright, so we create and realize our engine. So far, so good.

The output mix is a little trickier. We'd like to have a global volume control in one place, but the OutputMix object doesn't necessarily support it (and on current Android builds it doesn't), so we ask for it, and if we don't get it then we set `output_mix_vol` to NULL.

### Playing a Clip

To play a clip, we create an AudioPlayer object linking our source data and our OutputMix, set the volume, and send it a command to start playback. For this example I'm going to assume we've got 16-bit mono audio samples stored in a single buffer in memory:

```c++
const void *clip_samples;             //the raw samples
unsigned int clip_num_samples;        //how many samples there are
unsigned int clip_samples_per_sec;    //the sample rate in Hz
```

#### Creating an AudioPlayer

Given that, let's set up our player. First we need to set up our input link to the audio buffer, which OpenSL calls a DataLocator.

```c++
#ifdef TARGET_ANDROID
SLDataLocator_AndroidSimpleBufferQueue in_loc;
in_loc.locatorType = SL_DATALOCATOR_ANDROIDSIMPLEBUFFERQUEUE;
in_loc.numBuffers = 1;
#else
SLDataLocator_Address in_loc;
in_loc.locatorType = SL_DATALOCATOR_ADDRESS;
in_loc.pAddress = clip_samples;
in_loc.length = clip_num_samples * 2;
#endif
```

While OpenSL defines a simple data locator designed for in-memory buffers, Android doesn't actually support it, so when compiling for Android we'll have to use an Android extension called the Simple Buffer Queue.

Once we have our data locator defined, we need to define what's in it and link the two together into an `SLDataSource` structure:

```c++
SLDataFormat_PCM format;
format.formatType = SL_DATAFORMAT_PCM;
format.numChannels = 1;
format.samplesPerSec = clip_samples_per_sec() * 1000; //mHz
format.bitsPerSample = SL_PCMSAMPLEFORMAT_FIXED_16;
format.containerSize = 16;
format.channelMask = SL_SPEAKER_FRONT_CENTER;
format.endianness = SL_BYTEORDER_LITTLEENDIAN;
 
SLDataSource src;
src.pLocator = &in_loc;
src.pFormat = &format;
```

Fairly straighforward. One odd thing is that the `samplesPerSec` is misnamed. OpenSL actually requires the sample rate in millihertz.

Moving on to the output. This link goes straight to our OutputMix object.

```c++
SLDataLocator_OutputMix out_loc;
out_loc.locatorType = SL_DATALOCATOR_OUTPUTMIX;
out_loc.outputMix = output_mix_obj;
 
SLDataSink dst;
dst.pLocator = &out_loc;
dst.pFormat = nullptr;
```

Alright. Now we create our AudioPlayer object. We want volume controls and if we're on Android then we need it to support the Simple Buffer Queue extension as well:

```c++
//some variables to store interfaces
 
SLObjectItf player_obj;
SLPlayItf player;
SLVolumeItf player_vol;
 
#ifdef TARGET_ANDROID
SLAndroidSimpleBufferQueueItf player_buf_q;
#endif
 
//create the object
 
#ifdef TARGET_ANDROID
const SLInterfaceID ids[] = { SL_IID_VOLUME,
    SL_IID_ANDROIDSIMPLEBUFFERQUEUE };
const SLboolean req[] = { SL_BOOLEAN_TRUE, SL_BOOLEAN_TRUE };
#else
const SLInterfaceID ids[] = { SL_IID_VOLUME };
const SLboolean req[] = { SL_BOOLEAN_TRUE };
#endif
 
(*engine)->CreateAudioPlayer( engine,
    &player_obj, &src, &dst, lengthof( ids ), ids, req );
 
(*player_obj)->Realize( player_obj, SL_BOOLEAN_FALSE );
 
(*player_obj)->GetInterface( player_obj,
    SL_IID_PLAY, &player );
(*player_obj)->GetInterface( player_obj,
    SL_IID_VOLUME, &player_vol );
 
#ifdef TARGET_ANDROID
(*player_obj)->GetInterface( player_obj,
    SL_IID_ANDROIDSIMPLEBUFFERQUEUE, &player_buf_q );
#endif
```

#### Knowing When to Stop

Alright, here's where it gets interesting. AudioPlayer objects don't automatically transition their state to `SL_PLAYSTATE_STOPPED` when they reach the end of their data source. So we'll need to register a callback in order to find out when playback is actually complete. And this callback, unfortunately, runs on a background thread and is required to return _very_ quickly, so we can't do much from directly inside of it.

```c++
//some flags to keep track of playback state
//I have those next to my player interface variables
//make sure you read the note below about thread safety!
bool is_playing, is_done_buffer;
 
//define our callback
 
void SLAPIENTRY play_callback( SLPlayItf player,
    void *context, SLuint32 event )
{
    if( event & SL_PLAYEVENT_HEADATEND )
        is_done_buffer = true;
}
 
//register the callback
 
(*player)->RegisterCallback( player, play_callback, nullptr );
(*player)->SetCallbackEventsMask( player, SL_PLAYEVENT_HEADATEND );
```

That last parameter to `RegisterCallback` is passed to the callback in the `context` parameter, so if you've got all these variables embedded in a custom object instead of sitting around as globals (as I do) you can just pass your object's pointer there.

The callback simply sets a flag, which we'll pick up on our main thread which will be regularly polling the playback state.

#### Checking up on Playback

While the AudioPlayer is playing, we need to periodically check up on it to see if the sound is finished. If we don't do this, then we won't know when to release the object. Once a frame, we do something like the following:

```c++
if( is_playing && is_done_buffer )
{
    (*player)->SetPlayState( player, SL_PLAYSTATE_STOPPED );
 
#ifdef TARGET_ANDROID
    (*player_buf_q)->Clear( player_buf_q );
#endif
 
    is_playing = false;
}
```

Note that you might have to guard the `is_done_buffer` variable with some sort of synchronization primitive, as it will be accessed from multiple threads. While the code written above will _generally_ work when compiled by the NDK, it's technically incorrect, and might break under aggressive optimization levels. If you're using C++ and aren't stuck on an old version of the NDK, it suffices to declare `is_done_buffer` as `std::atomic<bool>`.

#### Oh, Android...

Unfortunately, there's one little wrinkle. On Android, the callback can sometimes (depending on the sample rate of the audio clip and the particulars of the device's audio hardware) fire before the sound has finished playing. This happens when the system plays the audio through an intermediate buffer. When the last bit of the sound is processed into the intermediate buffer, OpenSL is done with the input buffer, and it fires the event we're looking for then, despite the fact that it might be a good half second or more before the intermediate buffer's contents make it out to the speakers. So we also need to set a timer for ourselves.

We do this by adding another variable (`play_start_time`) next to `is_done_buffer` to track the time we started playing the sound, and then modifying our periodic check as follows:

```c++
if( is_playing && is_done_buffer &&
    current_time() - play_start_time > length_of_clip )
```

You can use whatever timer you like to implement `current_time`. Just make sure it's based on the system's run time and is monotonic (that is, it won't suddenly jump if the user turns their phone on after a flight and the system clock adjusts to a new time zone).

#### Play!

And now we're ready to start playing the clip:

```c++
#ifdef TARGET_ANDROID
(*player_buf_q)->Enqueue( player_buf_q, clip_buffer, clip_size );
#endif
 
is_playing = true;
is_done_buffer = false;
 
(*player)->SetPlayState( player, SL_PLAYSTATE_PLAYING );
play_start_time = current_time();
```

Again, on Android we have to use the special buffer queue interface, so set that up first off.

After that we set up our state tracking variables, so that we know what's going on.

And finally, set the AudioPlayer to the `SL_PLAYSTATE_PLAYING` state, which begins actual playback.

#### Stop!

```c++
(*player)->SetPlayState( player, SL_PLAYSTATE_STOPPED );
 
#ifdef TARGET_ANDROID
(*player_buf_q)->Clear( player_buf_q );
#endif
 
is_playing = false;
```

Pretty straightforward. The only interesting thing here is the `Clear` command, which resets the input source for a subsequent play command.

### Volume Controls

Controlling the volume on an individual sound is easy. The global volume, however, is tricky since Android doesn't give us a volume control interface on our OutputMix.

I'm assuming the volume setting is coming in as a "gain" value (that is, as a linear 0-1 "loudness").

```c++
//assuming you have these values kicking around
float sound_gain, global_gain;
 
//update the gain on a sound
 
float g = sound_gain;
 
if( !output_mix_vol )
    g *= global_gain;
 
(*player_vol)->SetVolumeLevel( player_vol,
    (SLmillibel)(gain_to_attenuation( g ) * 100) );
```

OpenSL takes its volume as an attenuation, or as the number of decibels to change the volume from its default loudness. These values are typically going to be _negative_ decibels:

```c++
float gain_to_attenuation( float gain )
{
    return gain < 0.01F ? -96.0F : 20 * log10( gain );
}
```

Also note the check against `output_mix_vol`. If we don't have a global volume control, then we need to run this code to adjust every active AudioPlayer's volume whenever `global_gain` changes.

### Cleaning Up

Audio resources are limited, and some Android devices have buggy firmware, so it's important that we're very careful about cleaning up and that we don't just blindly trust the OS to do it for us.

#### AudioPlayer Objects

Timely cleanup is _especially_ important when it comes to the AudioPlayer objects. You can only create so many of them before you run out of system resources and `Realize` starts to fail. So when we're doing our status polling and we notice that an AudioPlayer is not playing and we're sure it won't be asked to play again, we do the following:

```c++
(*player_obj)->Destroy( player_obj );
 
player_obj = nullptr;
player = nullptr;
player_vol = nullptr;
 
#ifdef TARGET_ANDROID
player_buf_q = nullptr;
#endif
```

This ensures that we don't run out of available playback resources.

#### The OutputMix and Engine

And when we're done playing sound in general (say at application shutdown), we destroy _all_ AudioPlayers and then the OutputMix and Engine objects:

```c++
(*output_mix_obj)->Destroy( output_mix_obj );
output_mix_obj = nullptr;
output_mix_vol = nullptr;
 
(*engine_obj)->Destroy( engine_obj );
engine_obj = nullptr;
engine = nullptr;
```
