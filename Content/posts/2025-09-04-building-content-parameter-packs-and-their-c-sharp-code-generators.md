---
title: "Building Content: Parameter Packs and their C# Code Generators"
series: content-builder
tags:
  - content
  - programming
  - C#
  - code
---

In an earlier post in this series I worte about _content identity_ being tied to the parameters used to build that content. This means that the parameter packs which drive builders are one of the major load-bearing elements of the overall design. In this post I'll loosely describe the C# compiler plugin which makes their implementation both easier and _much_ more reliably correct.

# Requirements

Specifically, I [wrote](/posts/building-content-just-in-time-dependencies#content-identity) the following:

> Content builders must explicitly declare their input parameters. And these parameters can be anything serializable: strings, ints, enums, arrays of such, etc. These parameters can (and commonly do) name input files, but there's no rule against a content builder programmatically producing noise based on some input integer seed value.

To expand on that, a parameter pack should have the following properties:

* It must only contain serializable values, possibly restricted only to types which make sense.
* It must be value-comparable and have a well-behaved `GetHashCode` method.
* It must be an immutable type (and so must all of its parameter values).
* It must have serialize and deserialize methods.

# Design

C#'s [`record class`](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/record) feature meets some of these requirements right out of the box. They're immutable by default and they have well-behaved `Equals` and `GetHashCode` methods that implement value-equality. Leveraging `record`s leaves the following:

* Property type restrictions must be imposed.
* Default-immutability must be made mandatory.
* Serialization methods must be implemented.

# Implementation

One way to meet the design requirements would be simply to do it by hand. That means lots of careful code reviews. It also means occasionally letting a bug slip through and dealing with _very_ gnarly bugs as a consequence (especially if those bugs lead to corruption in the dependency cache).

However, the requirements actually map onto very simple algorithmic checks which could be automated if only it was possible to inject code into the middle of the compilation process. And it is, in fact, possible to do just that.

The C# compiler supports plugins which come in two flavors:

* Analyzers, which can impose additional restrictions on code structure, reporting not only warnings but even hard errors.
* Code generators, which can do everything that analyzers do _and also_ produce additional code which becomes part of the compilation.

And they aren't very difficult to write.

## Setting up a compiler plugin project

Compiler plugins are just regular .NET assemblies. The basic project scaffolding looks like this:

```xml
<Project Sdk="Microsoft.NET.Sdk">

	<PropertyGroup>
		<TargetFramework>netstandard2.0</TargetFramework>
		<Nullable>enable</Nullable>
		<LangVersion>latest</LangVersion>

		<NoWarn>RS2008</NoWarn>
	</PropertyGroup>

	<ItemGroup>
		<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="3.11.0" />
	</ItemGroup>

</Project>
```

* The `TargetFramework` is `netstandard2.0` in order to remain maximally compatible as the compiler itself migrates from one version of `netcore` to the next.
* The `PackageReference` brings in the compiler API. These APIs are, in my experience, quite stable and there's no reason to pull in the absolute latest version of the package if there isn't something in it that you need. Supporting older compilers might seemsilly, but it leaves options open if you ever find yourself having to go back to an older toolchain (perhaps to check for a regression or repro a bug).
* The compiler API makes useof the `Nullable` feature, and having it active makes it easier to correctly work with that API.
* The `LangVersion` doesn't have to be `latest`, but it helps to have it at least reasonably current.
* The `RS2008`suppression turns off a warning about release tracking (documentation) that isn't relevant for this sort of project where the compiler plugin lives in the same solution as the code it's intended for.

