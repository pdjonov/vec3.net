---
title: "Adjusting Shader Assembly With Regex"
series: content-builder
tags:
  - bugs
  - code
  - content
  - Vulkan
  - SPIR-V
  - shaders
  - Slang
  - programming
---

Yesterday I worked around a Vulkan driver bug on my Quest 3 by writing code that throws regular expressions at SPIR-V assembly until it's in the right form to make the driver happy. Some of you probably wish you hadn't just read that sentence, because it's full of tech jargon which you aren't familiar with, and that might have bored or annoyed you. You are the lucky ones. The rest of my readers have already felt my pain, and the first paragraph isn't even done yet. It's (horror) story time - and you might learn a little about SPIR-V along the way if you stick around. I sure did.

## The problem

This started off pretty simple. After getting a (very) basic Android build set up and tested using my phone, I decided it was time to push my little VR side project to my HMD (head-mounted display) and start debugging it in its intended environment. The app would start, but then it would promptly crash before rendering a single frame.

## Finding the cause

The crash turned out to be caused by a failure in [`vkCreateGraphicsPipelines`](https://docs.vulkan.org/refpages/latest/refpages/source/vkCreateGraphicsPipelines.html). The result code was less than helpful: `VK_ERROR_UNKNOWN`. I had one additional hint - right before returning `ERROR_UNKNOWN`, the driver printed a warning to the Android log: "Failed to link shaders." With nothing else to go on, I started deleting bits of the shaders, focusing my efforts on their interfaces, until I narrowed down the culprit:

```slang
out float[1] ClipDistance : SV_ClipDistance;
```

It didn't matter what I wrote to that output or where and how I declared it, any use of `SV_ClipDistance` and the pipeline wouldn't link on my VR set. Now,  the Quest 3 _does_ [support](https://vulkan.gpuinfo.org/displayreport.php?id=33588#features) the `shaderClipDistance` feature, and my one clip plane is well within its `maxClipDistances` value of 8. So what could be the problem?

## Checking my API usage

As it turned out, I had checked but forgotten to _enable_ the `shaderClipDistance` feature. No other driver that I test on had complained, but maybe this one was just being strict? So I fixed that, and it did not fix the issue.

Looking at the Vulkan spec, I couldn't find anything else I might have done wrong, so I went back to poking at the shader.

## Looking at the SPIR-V

At some point, I started examining the SPIR-V. I'm using [Slang](https://shader-slang.org/), and I thought that maybe I'd hit a subtle compiler bug. Trimming away everything except the references to `ClipDistance` (and the things that those references refer to) left me with this:

```spvasm

```