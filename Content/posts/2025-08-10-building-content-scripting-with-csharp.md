---
title: "Building Content: Scripting With C#"
series: content-builder
tags:
  - content
  - programming
  - C#
  - code
---
A few weeks ago I wrote about [my toy content builder](/posts/building-content-just-in-time-dependencies). Today, I'm writing a little bit more about it.

## Scripting a build

A content builder should have the following properties:
* Input and output layouts should be flexible.
* It must be possible to define individual resources.
* It must be possible to define _sets_ of resources by applying _rules_ to sets of inputs.
* You shouldn't have to recompile the builder to change layouts.

That implies the need for some sort of build _script_.

## Selecting a language

But this is a hobby project and I don't feel like writing an entire specialized language like Jam or Make has (or the XML pseudolanguage MSBuild uses) just for it. That's a lot of work. And anyway, over time (as I take breaks from the project and come back to it) I'd forget how it works and I'd be sad having to relearn its syntax by reading the parser code.

So let's use an existing language. I want it to have the following properties:
* Easy integration with a .NET app.
* Convenient syntax for parsing and assembling strings.
* Convenient syntax for working with _lists_.
* It should be easy to debug.
* It should be reasonably fast to load and run.

Conveniently, C# happens to check all of those boxes:
* The C# compiler is available _in library form_ as [a NuGet package](https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp.Scripting/).
* Parsing is a bit weak, but interpolated strings are pretty good.
* The Linq library (and the SQL-y syntactic sugar for it) is plenty convenient.
* Debugging works out of the box. Run the content build in a debugger and breakpoints in the script file itself will just work.
* It's pretty quick, too.

## Setting up the project

First, you need a project and a `ProjectReference` to the compiler's NuGet package. We use the one that's targeted at interactive scripting:

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net9.0</TargetFramework>

        <Platforms>x64</Platforms>

        <!-- Yeah, this works on Linux, too. -->
        <RuntimeIdentifiers>linux-x64; win-x64</RuntimeIdentifiers>
        <!-- Work around a VS...ism -->
        <RuntimeIdentifier Condition="'$(RuntimeIdentifier)' == '' and '$(BuildingInsideVisualStudio)' == 'true'">win-x64</RuntimeIdentifier>

        <!-- Nice things are nice. -->
        <LangVersion>13.0</LangVersion>
        <Nullable>enable</Nullable>

        <!-- NuGet, chill. It's an offline tool, not a web server. -->
        <NuGetAudit>false</NuGetAudit>

        <OutputType>Exe</OutputType>
    </PropertyGroup>

    <ItemGroup>
        <!-- Not the currentest version, but the one I'm currently sitting on. -->
        <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Scripting" Version="4.9.2" />
    </ItemGroup>
</Project>
```

## Preparing to run scripts

Then, you need a little bit of code:

```csharp
public class ScriptableBuildContext : BuildContext
{
    public ScriptableBuildContext(string buildDirectory)
        : base(buildDirectory)
    {
        var builderAssemblies =
            Directory.GetFiles(
                path: Path.GetDirectoryName(GetType().Assembly.Location)!,
                searchPattern: "*.Content.Builders.dll").
            Select(path => Assembly.LoadFile(path)).
            OrderBy(asm => asm.GetName().FullName).
            ToArray();
        var builders =
            builderAssemblies.
            SelectMany(asm => asm.
                GetExportedTypes().
                Where(t =>
                    t.Namespace!.EndsWith(".Builders") &&
                    t.IsSubclassOf(typeof(SerializableArgsRecord))).
                OrderBy(t => t.FullName)).
            ToArray();

        var aliasedTypes =
            new[] {
                typeof(Vector2),
                typeof(Vector3),
                /* snip */
            }.Concat(
                builders.
                SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)).
                Where(p => p.CanWrite).
                Select(p => p.PropertyType).
                Select(t =>
                {
                    if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ImmutableValueList<>))
                        return t.GetGenericArguments()[0];

                    return t;
                }).
                Where(t =>
                    !t.IsGenericType &&
                    t.Namespace?.StartsWith("System") == false)).
            Distinct().
            ToArray();

        scriptContext = CSharpScript.Create(
            code: string.Join("\n", aliasedTypes.Select(t => $"using {t.Name} = {t.FullName};")),
            options: ScriptOptions.Default.
                WithEmitDebugInformation(true).
                WithAllowUnsafe(false).
                WithCheckOverflow(true).
                WithOptimizationLevel(OptimizationLevel.Debug).
                WithLanguageVersion(LanguageVersion.Latest).
                AddReferences(typeof(BuildContext).Assembly).
                AddReferences(typeof(ScriptableBuildContext).Assembly).
                AddReferences(builderAssemblies).
                AddImports(
                    "System",
                    "System.Collections.Generic",
                    /* snip */),
            globalsType: typeof(ScriptGlobals));
    }

    private readonly Script scriptContext;

    //more below
```

Alright, what's going on here?

Don't worry too much about the `BuildContext` base type. That just has the core build engine which handles things like dependency management, and it's not the focus of this post.

### Builder assemblies

First, we populate `builderAssemblies` with the assembly DLLs which contain the specific builder types for all of the content types we need to handle. Structuring the project like this has two benefits:
* Having multiple DLLs is convenient from the perpective of project management and code reuse.
* Having the builders outside the main builder assembly allows for some _very cool_ compiler tricks that I'll write about later.

### Builder and aliased types

From the assemblies, we extract the builder types. A builder's public API is its _arguments_ (which, as discussed previously, _identify_ the output it will produce). That's why we're looking for stuff derived from something called `SerializableArgsRecord`.

From the array of `builders`, we compose the `aliasedTypes` array. These are types that should be accessible in scripts without requiring full qualification or a `using` statement. But we want to be surgical and not import _entire_ namespaces, so we just select:
* A hand-curated list of specific base types.
* Types which form the public APIs of the builder argument records.

### Creating the script context

Finally, we create our script context.

The _initial_ `code` with which it's initialized is just a bunch of `using` statements which import all of the `aliasedTypes` into the script's execution context.

The `options` parameter is all the stuff that goes into what would otherwise be compiler parameters or the big `<PropertyGroup>` at the top of a `.csproj` file. This is also where we _reference_ the assemblies that contain all of the builder types and so on.

And the last parameter, `globalsType`, is how the script is going to communicate with the builder. The script context needs to know its _type_ up front.

## Running actual scripts

To run a script, we need to load it from disk and then compile and execute it with the `scriptContext` we set up above:

```csharp
    //continued from above

    public async Task RunBuildScriptFromFile(string fileName)
    {
        var scriptPath = Path.Combine(BuildDirectory, fileName);

        string scriptCode;
        Encoding scriptEncoding;
        try
        {
            using var reader = new StreamReader(scriptPath, detectEncodingFromByteOrderMarks: true);
            scriptCode = reader.ReadToEnd();
            scriptEncoding = reader.CurrentEncoding;
        }
        catch (IOException ex) when (ex is DirectoryNotFoundException or FileNotFoundException)
        {
            throw new FileNotFoundException($"Can't find the build script ({scriptPath}).");
        }

        var fileScript = scriptContext.ContinueWith(
            code: scriptCode,
            options: scriptContext.Options.
                WithFilePath(scriptPath).
                WithFileEncoding(scriptEncoding));

        var compileErrors = fileScript.Compile();
        if (compileErrors.Any(d => d.Severity == DiagnosticSeverity.Error))
            throw new CompilationErrorException(message: null, compileErrors);

        try
        {
            await fileScript.RunAsync(globals: new ScriptGlobals(this));
        }
        catch (Exception ex)
        {
            throw new BuildScriptExecutionException(ex, fileName);
        }
    }

    //more below
```

Alright, there are a few steps here.

First, we need the path to our script. This goes in `scriptPath`. If your program is going to run with a different current directory from your debugger, then this should be a _full_ path.

Second, we load the contents of the script file.

Next we _compile_ the script and, if there are errors, we report them.

Finally, we _run_ the script. Again, we report any errors. And there's an interesting thing here, the `globals`. This is where the runtime object which corresponds to the `globalsType` above goes. The globals type can be whatever the hosting application needs, and in my content builder's case it looks like this:

```csharp
    //continued from above

    public sealed class ScriptGlobals
    {
        internal ScriptGlobals(BuildContext buildContext)
        {
            this.buildContext = buildContext;
        }
        private readonly BuildContext buildContext;

        public SourceFileSet SourceFiles => buildContext.SourceFiles;
        public BuildItem Build(BuildArgs args) => buildContext.Build(args);
        public OutputFileSet Output => buildContext.Output;
    }
}
```

The idea here is that every _public_ member in this class is a _global_ symbol in the script file. That is, a script can read the `SourceFiles` and `Output` properties and it can call the `Build` method from, well, anywhere.

## Last, we need a script

What follows is a snippet from an actual build script which demonstrates the point of having written all this code:

```csharp
var corePipelineSettings = new VkPipeline()
{
	ShaderCompilationSettings = new()
	{
		ModuleDirectories = new(SourceFiles.Get("system/3d/shader-modules/")),
	},
};

var coreMaterialCompilation = new VkMaterial()
{
	PipelineCompilationSettings = corePipelineSettings,
};

Output.AddFile("sys/mtl.pak", Build(new PackFile()
{
	Prefix = "sys/gfx/",
	Exports = new(
		from file in SourceFiles.Glob("system/3d/materials/*.gpipe")
		let obj = Build(corePipelineSettings with
		{
			InputFile = file,
		})
		select new PackFileExport("3d/mtl/pipe/" + file.FileNameWithoutExtension, obj),

		from file in SourceFiles.Glob("system/3d/materials/*.mtl")
		let obj = Build(coreMaterialCompilation with
		{
			InputFile = file,
		})
		select new PackFileExport("3d/mtl/" + file.FileNameWithoutExtension, obj)
	),
}));
```

On its own, this doesn't make much sense, so I'll explain.

At the top, we define some default (`core`) builder settings. These are _parameter records_ for the `VkPipeline` and `VkMaterial` content builders. But simply constructing a parameter record doesn't produce any output (in fact, these records _can't_ produce any output as they're missing the required `InputFile` property, which we'll provide later using C#'s convenient `with` syntax). These are just regular instances of regular C# classes.

 Following that, we define an actual _output_ file called `sys/mtl.pak`. This file's contents are the output of a call to the `Build` method (seen above in the `ScriptGlobals` type). This method takes a builder parameter pack and it produces an output `BuildItem`.

Now, these names are _slightly_ misleading if you aren't in the right mindset. The script is written from the perspective of someone who wants to cleanly build the entire project. However, a call to `Build` may not actually build anything. If the output data is already available and up to date in the cache, then the builder doesn't run. Also, if no attempt is made to actually read the contents of the returned `BuildItem`, the builder doesn't run. (This improves iteration time when changes are being made to the build script.)

The _type_ of content here is a `PackFile`, which is a file that contains many individual _resources_, each of which is itself produced by a builder. The `PackFile` type here is the _argument record_ for the pack file builder. It has two key properties. The first is a `Prefix` which is logically prepended onto the names of all of its exported resources (I may write more about my resource management scheme in future). The second is the list of `Exports`.

There's some library sugar here to make things convenient, but the stuff in the `Exports = new(...)` block is all just going to get flattened into one big list. In this case, the contents of the list will come from two _lists_ (well `IEnumerable`s) of `PackFileExport` values which pair a _name_ under which a resource should be exported with the resource data itself.

And the resource data comes from another builder. The first list produces a set of _pipeline_ resources (these are basically binary-serialized `VkGraphicsPipelineCreateInfo`s, along with all of their pointed-to data). The second is a list of _materials_ which pair a pipeline with a binary-serialized blob that lets us create a `VkDescriptorSet` that points to all of the material's textures, uniforms, and other resources (excluding the ones provided by the engine itself).

And with a bit of library magic, we don't even have to list each individual input file, we can just glob them all together. And yes, the dependencies work properly, because if the file list changes then the contents of the `PackFile` parameter record change, and _that_ changes the _identity_ of the pack data which forces a new build. And if the file list stays the same but a pointed-to _file_ changes then that'll get picked up as a dependency having changed which will also force content to rebuild.

### Recursive building

One interesting thing to note: there's a _nested_ structure to the `Build` calls. Remember, `Build` doesn't _immediately_ build anything, it just returns a `BuildItem` which can be used later to get the build output on demand.

You might also note that the `coreMaterialCompilation` settings contain a reference to the `corePipelineSettings`. _Those_ exist because the material (`.mtl`) files going into the material builder will contain references to graphics _pipeline_ (`.gpipe`) files. And the material builder will need to be able to request a `Build` of those pipelines, and it wants to know what parameters it should use. (And that's not the only type of thing the material compiler might request a `Build` of. There are also things like textures, but in this case we don't override the built-in defaults so you don't see them in the script.)

Further, the `corePipelineSettings` have a `ShaderCompilationSettings` property which also happens to be another defaults-only set of builder parameters. The reason for that is similar to the story with materials referencing pipelines - pipelines reference _shaders_. Shaders have to be compiled to SPIR-V bytecode, and the shader language (in this case Slang) compiler has settings of its own.

### What about duplicated Build requess?

It's absolutely possible (in fact, it's very _probable_) that multiple materials will reference the same graphics pipeline. What happense in that case?

Well, nothing special. As long as they're all using the same pipeline builder settings, they'll all wind up requesting the exact same resource. They may all construct different `VkPipeline` parameter record _objects_, but if their properties have the same values (recursively), then they produce the same content _identity_. The first time one of those parameter sets is passed to `Build` a new `BuiltItem` object will be constructed to track the request and eventually produce output. The second and subsequent times an equivalent parameter set is passed to `Build`, the same `BuiltItem` object is returned.

There's further deduplication as well. If two objects which come from different sets of input data happen to produce identical output data, the `PackFile` builder will only store the one and rewrite references to the second one appropriately.