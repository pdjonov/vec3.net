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

This post isn't about building content _generally_, but it is about a problem which I ultimately fixed (well, _worked around_) with code inside the build pipeline. Things like this do tend to accumulate in and around asset compilers, so my deep dive into this one is included in the series on building content.

## The problem

This started off pretty simple. After getting a (very) basic Android build set up and tested using my phone, I decided it was time to push my little VR side project to my HMD (head-mounted display) and start debugging it in its intended environment. The app would start, but then it would promptly crash before rendering a single frame.

# The investigation

The crash turned out to be caused by a failure in [`vkCreateGraphicsPipelines`](https://docs.vulkan.org/refpages/latest/refpages/source/vkCreateGraphicsPipelines.html). The result code was less than helpful: `VK_ERROR_UNKNOWN`. I had one additional hint - right before returning `ERROR_UNKNOWN`, the driver printed a warning to the Android log: "Failed to link shaders." With nothing else to go on, I started deleting bits of the shaders, focusing my efforts on their interfaces, until I narrowed down the culprit:

```slang
out float[1] ClipDistance : SV_ClipDistance;
```

It didn't matter what I wrote to that output or where and how I declared it, any use of `SV_ClipDistance` and the pipeline wouldn't link on my VR set. Now,  the Quest 3 _does_ [support](https://vulkan.gpuinfo.org/displayreport.php?id=33588#features) the `shaderClipDistance` feature, and my one clip plane is well within its `maxClipDistances` value of 8. So what could be the problem?

A quick aside: this sort of "just try things to get clues about the issue" approach is just something that we have to fall back on at times. There isn't _always_ a nice tool that can just hand you the answer to an obscure problem. (No, not even LLMs can do that until after they've indexed somebody else's solution to that same problem.) This _will_ happen to any engineer who's working on a sufficiently complex project at some point, and it's one of the reasons why it _always_ pays off to invest early on (and on an ongoing basis) in fast builds and deploys.

## Checking my API usage

As it turned out, I had checked but forgotten to _enable_ the `shaderClipDistance` feature. No other driver that I test on had complained, but maybe this one was just being strict? So I fixed that, and it did not fix the issue.

Looking at the Vulkan spec, I couldn't find anything else I might have done wrong, so I went back to poking at the shader.

## Looking at the SPIR-V

At some point, I started examining the SPIR-V, which I grabbed easily enough from a [RenderDoc](https://renderdoc.org/) capture on PC. I'm using [Slang](https://shader-slang.org/), and I thought that maybe I'd hit a subtle compiler bug. Trimming away everything except the references to `ClipDistance` (and the things that those references refer to) left me with this:

```spvasm
; SPIR-V
; Version: 1.3
; Generator: NVIDIA Slang Compiler; 0
; Bound: 862
; Schema: 0
               OpCapability MultiView
               OpCapability Shader
         %78 = OpExtInstImport "GLSL.std.450"
               OpMemoryModel Logical GLSL450
               OpEntryPoint Vertex %vert "vert" %gl_Position %entryPointParam_vert_ViewPos %entryPointParam_vert_Normal %in_Position %in_Normal %gl_ClipDistance %18
               OpEntryPoint Fragment %frag "frag" %entryPointParam_frag
               OpExecutionMode %frag OriginUpperLeft
               OpSource Slang 1
; snip - lots of OpName
; snip - lots of Op[Member]Decorate
               OpDecorate %gl_ClipDistance BuiltIn ClipDistance
; snip
      %float = OpTypeFloat 32
        %int = OpTypeInt 32 1
      %int_1 = OpConstant %int 1
%_arr_float_int_1 = OpTypeArray %float %int_1
; snip
      %int_0 = OpConstant %int 0
; snip
%_ptr_Output__arr_float_int_1 = OpTypePointer Output %_arr_float_int_1
; snip
%gl_ClipDistance = OpVariable %_ptr_Output__arr_float_int_1 Output
; snip
       %vert = OpFunction %void None %3
; snip
        %851 = OpCompositeConstruct %_arr_float_int_1 %801
               OpStore %gl_ClipDistance %851
               OpReturn
               OpFunctionEnd
       %frag = OpFunction %void None %3
; snip
               OpReturn
               OpFunctionEnd
```

So, what does that all mean?

* First, there's a preamble that declares what compiler made the code, what version of SPIR-V it produced, and what optional features the code requires (those are the `OpCapability` lines).
* Then there's a list of entry points (shaders), each of which lists its input and output bindings, which strongly implies that only the vertex shader (`"vert"`) touches anything that looks like a `ClipDistance`.
* Then there's a bit more metadata.
* Then there's a bunch of `OpName` debug info, which I snipped.

The first interesting line is this one:
```spvasm
               OpDecorate %gl_ClipDistance BuiltIn ClipDistance
```

That is an instruction to the driver to link the `%gl_ClipDistance` symbol to wherever it is the GPU wants shaders to put `ClipDistance` values. And it's the only reference to `BuiltIn ClipDistance`, so the `%gl_ClipDistance` is the main thing to focus on. So, what is a `%gl_ClipDistance` and where is it used?

```spvasm
%gl_ClipDistance = OpVariable %_ptr_Output__arr_float_int_1 Output
; snip
        %851 = OpCompositeConstruct %_arr_float_int_1 %801
               OpStore %gl_ClipDistance %851
```

Okay, it's just some arbitrary variable, as far as SPV's concerned. Probably the only reason it renders as `%gl_ClipDistance` is that the disassembler saw the `OpDecorate` on it and chose a friendlier name than, say `%851`.

And finally, there were no traces of `%gl_ClipDistance` anywhere in the fragment shader at all.

### Examining `%gl_ClipDistance`'s type

I'll divert briefly to discuss `%gl_ClipDistance`'s type (`%_ptr_Output__arr_float_int_1`) and how it's declared in SPIR-V, because it's going to turn out to be relevant. Starting with `%_ptr_Output__arr_float_int_1` itself and then tracing backwards through what it references:

```spvasm
%_ptr_Output__arr_float_int_1 = OpTypePointer Output %_arr_float_int_1
%_arr_float_int_1 = OpTypeArray %float %int_1
      %float = OpTypeFloat 32
      %int_1 = OpConstant %int 1
        %int = OpTypeInt 32 1
```

Reading that, you can see that:
* `%_ptr_Output__arr_float_int_1` is an _output_ pointer to a `%_arr_float_int_1`.
* `%_arr_float_int_1` is, in turn, an array of `%int_1` `%float` values.
* `%int_1` is a constant of type `%int` whose value is 1.
* `%int` is a 32-bit _signed_ (that's what the 1 at the end of the `OpTypeInt` means) integer.
* `%float` is a 32-bit `float`.

It's kind of interesting how SPIR-V builds up even very primitive types like `int32` as a series of declarations rather than having them built in. I suppose this is to leave room to expose lower-precision types on GPUs that support them without having to pile on lots and lots of extensions.

It's also interesting to note that array types do _not_ automatically "decay" to pointers as they do in C and C++. When SPIR-V says something is an array of `N` values, that means there's actually `N` (probably: terms and conditions may apply when it comes to builtins) contiguous values.

## So, what's the problem?

Well, looking up `BuiltIn ClipDistance` I see mention of a `ClipDistance` _capability_, and I don't see an `OpCapability` for that. None of my other devices care that the compiler missed it (just like they didn't care that I'd forgotten to enable `shaderClipDistance`), but maybe that's the issue.

Otherwise, everything seems fine to me.

## Surely, working around this little problem will be easy...

Now, the question is _how_ to work around this issue. I _don't_ want to commit the time necessary to forking the Slang compiler and making and maintaining my own patches. That would be _excessive_ unless I'm absolutely _forced_ into doing so.

Fortunately, the same SPIR-V disassembler that RenderDoc is using is available as a library. And I already have it available in my tools pipeline as a leftover from earlier experimentation and debugging. So it should be a simple matter of taking Slang's output, disassembling it, editing the text, and then reassembling the resulting shader module. A little annoying, but easy enough to do, and something I could plug into the build system so it just runs automatically.

To get there, I wrote two regular expressions. One to find `OpDecorate ... BuiltIn ...` instructions and another to find `OpCapability`.

```csharp
[GeneratedRegex(@"^\s*OpCapability\s+(?<name>\w+)\s*$", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
private static partial Regex OpCapabilityMatcher();

[GeneratedRegex(@"^\s*OpDecorate\s+(?<name>%\w+)\s+BuiltIn\s+(?<decorator>\w+)\s*$", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
private static partial Regex OpDecorateBuiltInMatcher();
```

The logic was simple. If `OpDecorateBuiltInMatcher` found a hit for `ClipDistance` but `OpCapabilityMatcher` didn't, then inject an `OpCapability ClipDistance` into the text and reassemble the shader. Took almost no time to code it up...

But it didn't help. The missing `OpCapability ClipDistance` was _not_, in fact, the problem. The Quest 3's driver happily ignores its absence just like every other driver seems to. There is _something else_ that it objects to, so the search went on.

## Trying different things

The first thing to do at this point was confirm that `ClipDistance` actually _works_ on the Quest 3. It's such a basic feature and important for some very powerful _and common_ rendering techniques, so I couldn't imagine _that_ being broken, but I had to check regardless. To do that, I dusted off my old GLSL-based pre-Slang graphics pipeline compilation model.

Step one: Go back to my RenderDoc capture and ask it to _decompile_ my shader module into (very ugly) GLSL.

Step two: Paste that into a file and put it in my build in place of (well, next to) the Slang source. Point the pipeline definition at the GLSL source.

Step three: Fix the bitrot in the GLSL-based content builder. The main problem here was that Slang supports user-defined attributes in its source, its reflection API makes them visible to users of the Slang libraries, and I had built my material-pipelinine binding logic on top of these attributes. GLSL and [SPIRV-Reflect](https://github.com/KhronosGroup/SPIRV-Reflect), obviously, have no analog. The short version is I patched up the GLSL reflection code to synthesize the information the new Slang reflection code produces directly, mostly based on some silly ad-hoc naming conventions.

And with that out of the way, I compiled a set of GLSL shaders equivalent to the Slang ones, and they worked.

At this point I entertained a few options:
* Switch this one material over to GLSL.
* Get Slang to transpile to GLSL (that is a thing that it can do) and then compile that.
* Put in a compatibility mode that _decompiles_ the shader all the way to GLSL before recompiling it.

I didn't like any of these options, so I decided to dig a bit deeper. But it was good to know that I'd have approaches to fall back on if the further investigation proved fruitless.

## Figuring out what was wrong

So now that I know that `ClipDistance` _does_ work on the device and that the problem is with the Slang version of the shaders, I had to work out _exactly_ what the relevant difference is between the two compiler outputs. I've already gone over Slang's output, so now it was time to do the same with `glslang`'s:

```spvasm
               OpCapability Shader
               OpCapability ClipDistance
               OpCapability MultiView
; snip
               OpEntryPoint Vertex %2 "vert" %3 %4 %5 %6 %7 %8
; snip
               OpDecorate %_arr_v4float_uint_3 ArrayStride 16
               OpMemberDecorate %_struct_12 0 Offset 0
               OpMemberDecorate %_struct_13 0 Offset 0
               OpDecorate %_struct_14 Block
               OpMemberDecorate %_struct_14 0 Offset 0
;snip
               OpDecorate %_struct_15 Block
               OpMemberDecorate %_struct_15 0 BuiltIn Position
               OpMemberDecorate %_struct_15 1 BuiltIn PointSize
               OpMemberDecorate %_struct_15 2 BuiltIn ClipDistance
               OpMemberDecorate %_struct_15 3 BuiltIn CullDistance
; snip
      %int_2 = OpConstant %int 2
; snip
     %uint_1 = OpConstant %uint 1
%_arr_float_uint_1 = OpTypeArray %float %uint_1
 %_struct_15 = OpTypeStruct %v4float %float %_arr_float_uint_1 %_arr_float_uint_1
%_ptr_Output__struct_15 = OpTypePointer Output %_struct_15
          %5 = OpVariable %_ptr_Output__struct_15 Output
; snip
%_ptr_Output__arr_float_uint_1 = OpTypePointer Output %_arr_float_uint_1
; snip
        %136 = OpCompositeConstruct %_arr_float_uint_1 %135
        %137 = OpAccessChain %_ptr_Output__arr_float_uint_1 %5 %int_2
               OpStore %137 %136
; snip
```

Well _this_ is interesting.

A few things jumped out at me immediately as I scanned the assembly:
* The compiler put in an `OpCapability ClipDistance`.
* The compiler put in an _unused_ reference to `BuiltIn CullDistance`, but no `OpCapability CullDistance`.
* The `BuiltIn ClipDistance` appears in an `OpMemberDecorate`, _not_ an `OpDecorate`...
* ... because the ClipDistance isn't in a plain variable, it's in a _struct_, `%_struct_15`.
* `%_struct_15` contains some other stuff as well, like the output vertex position and another unused field for `BuiltIn PointSize`.
* The final `CullDistance` value is _still_ written through an `OpStore` which writes the value through a pointer in the same way Slang's code does, but that pointer is just formed a little differently.

For those who don't follow where I got all that from, here's how `OpTypeStruct` works:
```spvasm
 %_struct_15 = OpTypeStruct %v4float %float %_arr_float_uint_1 %_arr_float_uint_1
          %5 = OpVariable %_ptr_Output__struct_15 Output
```

The first line means that `%_struct_15` is a _struct_ type which has _four_ fields which are, respectively, of the following types: `float4`, `float`, `float[1]`, and `float[1]`. The second line declares `%5` (which is in the `vert` shader's list of inputs and outputs) as a pointer to a `%_struct_15`.

This pairs with the `OpMemberDecorate`s:

```spvasm
               OpMemberDecorate %_struct_15 2 BuiltIn ClipDistance
```

That reads "`%_struct_15`'s _third_ field (two because they're zero-indexed) binds to `BuiltIn ClipDistance`".

And finally there's `OpAccessChain`:
```spvasm
        %137 = OpAccessChain %_ptr_Output__arr_float_uint_1 %5 %int_2
```

That means "compute a pointer of type `%_ptr_Output__arr_float_uint_1` by loading the pointer in `%5` and adding the offset to the _third_ (again, zero-based) field of `%5`'s pointed-to struct.

That's a bunch of extra ceremonly and indirection which the driver (I certainly hope) optimizes away at pipeline creation time. But the question is why _it_ works and the much simpler Slang output above _doesn't_. Well - spoiler alert - I have no idea. My best guess is that whoever built the Quest's Vulkan driver is only testing againt `glslang`'s output, and so its shader compiler looks specificaly for _this_ pattern and can't handle any other. (That's really not atypical as far as GPU drivers go, alas.)

## Narrowing it down

So that's quite the list of differences. It'd be good if I could find which ones, exactly, are the relevant ones. To do that, I needed a way to experiment. Fortunately, I already had all of the pieces that I needed:

* I was already set up to assemble SPIR-V from text as part of my quick hack to patch in the `OpCapability ClipDistance`.
* I had just fixed the reflection logic for GLSL shaders. And since that runs off of the GLSL compiler's SPIR-V output rather than the GLSL source, it could just as easily accept SPIR-V from other sources.

So I added _another_ shader format to the build tool's pipeline compiler:

```csharp
ShaderModuleSpirvBytecode BuildSpirvAsmModule()
{
	// pseudo(ish)code

	// use the assembler routine from earlier
	var bytecode = Assemble(spirvAsmSource);
	var ret = new ShaderModuleSpirvBytecode(bytecode);

	// run SPIR-V module relection, shared with GLSL
	var refls = ShaderInterface.ReflectModule(bytecode);

	// snipped: repacking the data in refls into the format downstream code expects

	return ret;
}

var module = Args.InputFile.Extension switch
{
	".slang" => BuildSlangModule(),
	".spvasm" => BuildSpirvAsmModule(), // new!
	".vkmod" => BuildGlslModule(),
	_ => throw new ArgumentException("Unrecognized file extension. Unable to infer the shader language."),
};
```

With that in place, I took the disassembly from the broken Slang shaders, pasted it into a `.spvasm` file (as before, next to the Slang source), and pointed the pipeline definition at it. I then started making little changes to it by hand, moving it in the direction of the GLSL compiler's output, until I got something that works.

## What worked

If you're curious what parts of the diffs the Quest's VK driver likes so much, here's the minimal(ish) set of changes that got the pipeline to work on my HMD. (This isn't in source order, it's been rearranged for exposition.)

First, I wrapped the `ClipDistance` variable in a struct:
```spvasm
%_ClipDistance_wrapper_struct = OpTypeStruct %_arr_float_int_1
OpDecorate %_ClipDistance_wrapper_struct Block
OpMemberDecorate %_ClipDistance_wrapper_struct 0 BuiltIn ClipDistance

%_ptr_ClipDistance_wrapper_struct = OpTypePointer Output %_ClipDistance_wrapper_struct
```

I didn't need anything else in the struct besides the `float[1]` array for the output. Whatever the GLSL compiler was doing with `PointSize` and `CullDistance` fields wasn't relevant here.

Then I deleted the old `%gl_ClipDistance` (and its `BuiltIn ClipDistance` decoration) and replaced it with this:

```spvasm
%gl_ClipDistance = OpVariable %_ptr_ClipDistance_wrapper_struct Output
```

The last thing that needed updating is how that variable is written (and I've already covered how to read this, only the field index has changed):

```spvasm
%_p_clipDist = OpAccessChain %_ptr_Output__arr_float_int_1 %gl_ClipDistance %int_0
OpStore %_p_clipDist %851
```

And, finally, I threw in an `OpCapability ClipDistance`, just to keep things tidy.

# Automating the solution

So, that's nice. I can disassembly Slang's output and patch it by hand and... yeah there's no way I'm making that my workflow. Absolutely not. _Ephatically: **Hell no.**_

The actual list of patches that the shader required is pretty short, so I decided to see how far I could push my regex-based approach from the earlier `OpCapability` experiment. But I'd need more regexes for this:

```csharp
[GeneratedRegex(@"^\s*OpCapability\s+(?<name>\w+)\s*$", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
private static partial Regex OpCapabilityMatcher();

// OpDecorateBuiltInMatcher got expanded to also find OpMemberDecorate
[GeneratedRegex(@"^\s*Op(?<member>Member)?Decorate\s+(?<name>%\w+)\s+(?:(?<membernum>\d+)\s+)?BuiltIn\s+(?<decorator>\w+)\s*$", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
private static partial Regex OpMaybeMemberDecorateBuiltInMatcher();

[GeneratedRegex(@"^\s*(?<name>%\w+)\s+=\s+OpVariable\s+(?<type>%\w+)(?:\s+(?<decorator>\w+))\s*$", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
private static partial Regex OpVariableMatcher();

[GeneratedRegex(@"^\s*(?<name>%\w+)\s+=\s+OpTypePointer\s+(?<modifier>\w+)\s+(?<base>%\w+)\s*$", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
private static partial Regex OpTypePointerMatcher();

[GeneratedRegex(@"^\s*OpStore\s+(?<dst>%\w+)\s+(?<src>%\w+)\s*$", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
private static partial Regex OpStoreMatcher();

[GeneratedRegex(@"^\s*(?<name>%\w+)\s+=\s+OpTypeInt\s+(?<width>\d+)\s+(?<signed>[01])\s*$", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
private static partial Regex OpTypeIntMatcher();

[GeneratedRegex(@"^\s*(?<name>%\w+)\s+=\s+OpConstant\s+(?<type>%\w+)\s+(?<value>\w+)\s*$", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
private static partial Regex OpConstantMatcher();
```

And also some helper functions like the following to hlep drive them:

```csharp
private static MatcherFunc IsOpMaybeMemberDecorateBuiltIn(bool? isMember = null, string? name = null, int? memberNum = null, string? decorator = null)
	=> (string line, out Match match) =>
	{
		match = OpMaybeMemberDecorateBuiltInMatcher().Match(line);
		return match.Success &&
			(isMember == null || match.Groups["member"].Success == isMember) &&
			(name == null || match.Groups["name"].ValueSpan.SequenceEqual(name)) &&
			(memberNum == null || int.Parse(match.Groups["membernum"].ValueSpan, CultureInfo.InvariantCulture) == memberNum) &&
			(decorator == null || match.Groups["decorator"].ValueSpan.SequenceEqual(decorator));
	};
```

The overall logic is fairly simple:
1. Look for an `Op[Member]Decoration <something> BuiltIn ClipDistance`.
2. Look for an `OpCapability ClipDistance`. If it's missing, inject it.
3. If the `BuiltIn ClipDistance` decoration was found on a struct member, then return. Otherwise, `<something>` is the `%varName` going forward.
4. Find the `%varName = OpVariable <ptrType> Output` declaration.
5. Find the `%ptrType = OpTypePointer Output <baseType>` declaration.
6. Find the `OpStore %varName <src>` instruction.

If any of the declarations can't be found, then this isn't the bad pattern from Slang and nothing further is done. Otherwise, the regex captures contain everything I need to replace the "bad" lines with "good" ones:

```csharp
var i0Name = lines.FindIntConstant(value: 0);

var storeSrc = matOpStore.Groups["src"].Value;

var wrapperTypeName = $"{varName}_patchWrapperStruct";
var wrapperTypePtrName = $"{varName}_patchWrapperStruct_ptr";

lines.Replace(
	(clipDistanceDec, [
		$"OpDecorate {wrapperTypeName} Block",
		$"OpMemberDecorate {wrapperTypeName} 0 BuiltIn ClipDistance",
	]),
	(varDecl, [
		$"{wrapperTypeName} = OpTypeStruct {baseType}",
		$"{wrapperTypePtrName} = OpTypePointer Output {wrapperTypeName}",
		$"{varName} = OpVariable {wrapperTypePtrName} Output",
	]),
	(opStore, [
		$"{varName}_addr = OpAccessChain {ptrType} {varName} {i0Name}",
		$"OpStore {varName}_addr {storeSrc}",
	])
);
```

Is it _horrifying?_ Yes.

But does it work? _Yes._

It's integrated into my regular content builds, and a warning prints every time the code triggers. So I don't have to fall back to writing some subset of my shaders in GLSL or (ugh) raw SPIR-V assembly. The problem-matching criteria in my code _should_ be tight enough to prevent it messing up as the Slang compiler evolves, but if something breaks it's usually pretty clear/easy to catch (at least at the scale of a one man hobby project). It's not _great_, but it's _good enough_. (And it's not even the worst hack I've had to make to deal with GPU drivers being GPU drivers.)

If I'm lucky, someone will do something about my bug report before I ever have to touch this code again, and it can one day simply be deleted. For now, do I have other things I'd rather work on _before_ I make this any prettier/more robust than it has to be? Yes. Otherwise, if the need arises for even more of these patches, I may have to learn to properly parse SPIR-V without first disassembling it to human-readable text and start manipulating it in that way. (May that day never come. Or, at least not for _this_ reason.)