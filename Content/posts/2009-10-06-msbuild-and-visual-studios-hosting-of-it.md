id: 146
title: MSBuild and Visual Studio's Hosting of It
date: 2009-10-06T14:37:15-07:00
author: phill
guid: http://vec3.ca/?p=146
permalink: /posts/msbuild-and-visual-studios-hosting-of-it
categories:
  - build
  - code
---
This is an interesting issue that might have bitten you if you've been trying to get code generators to play nicely in C# projects.

You get your generator. You get your project. You open up the project and add an item group:

```xml
<ItemGroup>
    <GenerateParser Include="MyGrammar.y" />
    <Compile Include="MyGrammar.p.cs">
        <DependentUpon>MyGrammar.y</DependentUpon>
    </Compile>
</ItemGroup>
```

And then you make a target:

```xml
<Target Name="GenerateParsers" Inputs="@(GenerateParser)"
    Outputs="@(GenerateParser->'%(Filename).p.cs')">
    <Exec Command="gppg @(GenerateParser) > %(Filename).p.cs" />
    <!-- the next bit is probably not necessary... -->
    <Touch Files="%(GenerateParser.Filename).p.cs" />
</Target>
```

And you hook it up:

```xml
<PropertyGroup>
    <BuildDependsOn>
        GenerateParsers;$(BuildDependsOn)
    </BuildDependsOn>
</PropertyGroup>
```

You load that project up and so far it looks perfect. You've got your grammar file in the solution tree, with the generated code file neatly tucked away beneath it (like already happens with the .Designer.cs file for WinForms components). You hit build, and your generator only runs when its source is out of date. Perfect, right?

_**Wrong!**_

It's wrong because the C# compiler doesn't seem to catch the freshly generated file unless you build _and then **rebuild**_ the project. It gets even more confusing when you crank up the MSBuild debug level and you see that the compiler is being executed when it should. It's almost as if it were reading the old code file...

Actually, it is. And this is a consequence of the fact that Visual Studio isn't using the MSBuild you read about in the documentation. It's using a special hosted version, which ties into some sort of file-caching mechanism to make project builds go faster. That caching mechanism is, however, broken when it comes to detecting situations like the above. The easiest way to work around it I've found is to add this line into a `PropertyGroup` somewhere:

```xml
<UseHostCompilerIfAvailable>False</UseHostCompilerIfAvailable>
```

Your build gets a tad slower, but hey - at least it now works properly.

Incidentally: [GPPG](http://plas.fit.qut.edu.au/projects/LanguageProcessingTools.aspx) is an awesome tool.