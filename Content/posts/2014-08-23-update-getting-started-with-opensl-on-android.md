---
id: 1017
title: 'Update: Getting Started with OpenSL on Android'
date: 2014-08-23T01:30:06-07:00
author: phill
guid: http://vec3.ca/?p=1017
permalink: /posts/update-getting-started-with-opensl-on-android
categories:
  - Android
  - code
tags:
  - Android
  - audio
  - code
  - OpenSL
---
A while ago I made [a post](/posts/getting-started-with-opensl-on-android) about the use of OpenSL on Android. That post has an error, outlined below:

While the OpenSL documentation for the `SL_PLAYEVENT_HEADATEND` event seems to suggest that the sound has been processed when the event is fired, this isn't actually the case, at least on Android. That event fires when the sound's underlying buffer has been processed, but that's different from the sound having actually been played out of the speakers. In most cases, the difference is negligible.

However, when playing sounds at certain frequencies (which likely vary from system to system, as I believe it's certain multiples of the hardware's native output sampling rate), the playback engine will render the audio in a mode where it will process the source data into some sort of intermediate buffer and then play the audio back from that. In that case, the `SL_PLAYEVENT_HEADATEND` event will be delivered a significant fraction of a second before the audio makes it to the speakers. Stopping the player immediately will, in those cases, clip off the end of your sound.

Unfortunately, there's no nice way to work around this. The correct solution is to first keep track of when you started playing the sound:

```c++
#ifdef TARGET_ANDROID
(*player_buf_q)->Enqueue( player_buf_q, clip_buffer, clip_size );
#endif
 
is_playing = true;
is_done_buffer = false;
 
(*player)->SetPlayState( player, SL_PLAYSTATE_PLAYING );
play_time = current_time();
```

and to then only stop the player when you have _both_ received the `SL_PLAYEVENT_HEADATEND` event _and_ enough time has elapsed for the sound to play _plus_ a few milliseconds (1-2ms seems sufficient) to account for latency within the audio pipeline:

```c++
if( is_playing && is_done_buffer &&
    current_time() - play_time > clip_length + two_milliseconds )
{
    (*player)->SetPlayState( player, SL_PLAYSTATE_STOPPED );
 
#ifdef TARGET_ANDROID
    (*player_buf_q)->Clear( player_buf_q );
#endif
 
    is_playing = false;
}
```

Make sure you implement `current_time` using a monotonic system-time based timer. You don't want to time this against a clock which might jump around if the user crosses between time zones or otherwise tinkers with the system clock.

One final note: the original code technically has a data race in it. The `is_done_buffer` variable is accessed on multiple threads and isn't protected in any way. The code, as written, shouldn't normally produce errors, but it might start miscompiling in new versions of GCC or under higher levels of optimization. If you're writing your code in C++, I'd strongly recommend redeclaring it as type `std::atomic<bool>`.