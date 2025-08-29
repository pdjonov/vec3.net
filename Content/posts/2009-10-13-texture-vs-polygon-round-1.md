---
id: 166
title: 'Texture vs. Polygon: Round 1'
date: 2009-10-13T14:53:05-07:00
author: phill
guid: http://vec3.ca/?p=166
permalink: /posts/texture-vs-polygon-round-1
categories:
  - graphics
---
I recently critiqued some work-in-progress game art for an (I guess) art student, and it struck me that it can be fairly hard for a newcomer to judge where to put their details. Some details don't work very well as geometry. Others don't work well in a texture. Knowing where to put a given mark or line is the difference between effectively using your polygon budget to produce mind-blowingly good scenes and a mess of blurry textures and too-perfect railings.

When working from references, there's one simple rule that helps make a lot of it clear: check how details respond to distance and viewing angle.

Get a close-up shot facing your object dead-on. Get another one from a great distance. Get another one from a middle distance, but from a glancing view angle. Now look, not at the object itself, but at the lines that compose it. Any line that stays both visible and reasonably crisp in all three images must be modeled into the geometry as a hard polygon edge. Any line that vanishes in the distance or at a glancing angle belongs in a texture. Lines that remain visible but become indistinct far away can go in either category - generally keep them geometry if they have to look exceptionally crisp from very close, else paint them into your texture.

Also pay attention to how your object contributes to the overall scene, and how it reacts to the intended lighting. A typical old Parisian building has fairly subtle horizontal detailing between the floors (excepting the ones with the ridiculously bold lines), and you might be tempted to throw it all into a bump map. That's not right though, because the horizontal lines aren't there for the building - they're there for the street. Take [a look down a Paris street](http://en.wikipedia.org/wiki/File:Rue_St_Jacques_Louis_Le_Grand_DSC09316.jpg) and it suddenly becomes apparent that the subtle little borders are there to pick up the light, cast small shadows, and create a series of horizontal lines that continue from building to building for as far as you can see. If you paint them into textures the detail will vanish on you much closer than it would in the real world, and your scene will look odd.

One thing to be careful of: make sure the images you base your decisions on are taken from positions that the player is going to actually be looking from (and I mean looking, glancing briefly as you run by chasing an enemy doesn't count); if the player is stuck on the ground, then anything above the third story of a building is effectively a background object (unless your design calls for long, careful upward glances). Don't waste details on it - save that for the final detail pass and add another tree, lamp post, trash can, piece of litter, or sign, etc. You'll do less work, your frame rate will be higher, and your player will feel as though they're in a much cooler and more immersive environment.