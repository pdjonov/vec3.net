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

Specifically, I [previously wrote](/posts/building-content-just-in-time-dependencies#content-identity) the following:

> Content builders must explicitly declare their input parameters. And these parameters can be anything serializable: strings, ints, enums, arrays of such, etc. These parameters can (and commonly do) name input files, but there's no rule against a content builder programmatically producing noise based on some input integer seed value.

With the addition of some practical concerns, a parameter pack should have the following properties:

* It must only contain serializable values, possibly restricted only to types which make sense.
* It must be value-comparable and have a well-behaved `GetHashCode` method.
* It must be an immutable type (and so must all of its parameter values).
* It must have serialize and deserialize methods.

# Design

C#'s [`record class`](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/record) feature meets some of these requirements right out of the box. They're immutable by default and they have well-behaved `Equals` and `GetHashCode` methods that implement value-equality. Leveraging `record`s leaves the following:

* Property type restrictions must be imposed.
* Default-immutability must be made mandatory.
* Serialization methods must be implemented.

Further, it would be useful to allow parameter packs to contain (appropriately restricted) structured values themselves. So the need for serialization needs to be separated and available as something that can be inherited by tyepes other than just the top-level parameter pack types.

This results in two abstract base classes:

```csharp
public abstract record SerializableArgsRecord
{
	protected SerializableArgsRecord() { }

	protected SerializableArgsRecord(BuildArgsReader reader) { }
	public virtual void Serialize(BuildArgsWriter writer) { }
}

public abstract record BuildArgs : SerializableArgsRecord
{
	protected BuildArgs() { }
	protected BuildArgs(BuildArgsReader reader) : base(reader) { }

	protected internal virtual ulong BuilderVersion => 0;
	protected internal abstract Builder CreateBuilder(BuildItem buildItem);
}
```

The base `SerializableArgsRecord` class has a `Serialize` function that writes out its contents. This method will be overridden and written automatically by the code generator. It also has a constructor which can be used to deserialize the written values and, again, derived classes will have this written for them by the code generator.

The `BuildArgs` class is the root of content identity and the stepping-off point into the build system itself (`BuilderVersion` and `CreateBuilder`, respectively).

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

		<NoWarn>RS2008</NoWarn>
	</PropertyGroup>

	<ItemGroup>
		<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="3.11.0" />
	</ItemGroup>

</Project>
```

* The `TargetFramework` is `netstandard2.0` in order to remain maximally compatible as the compiler itself migrates from one version of `netcore` to the next.
* The `PackageReference` brings in the compiler API. As of writing I'm sitting on an older package simply because there's no compelling reason to upgrade. For new projects, start with whatever's the lastest package available at the time(of those supported by the oldest .NET SDK you care to support).
* The compiler API has nullability annotations, so the `Nullable` feature makes using it smoother and less bug-prone.
* The `RS2008`suppression turns off a warning about release tracking (documentation) that isn't relevant for this sort of project where the compiler plugin lives in the same solution as the code it's intended for.

## Writing the code generator

Here's the outer scaffolding:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

[Generator]
public class BuildArgsSerializationCodeGenerator : ISourceGenerator
{
	public void Initialize(GeneratorInitializationContext context)
	{
		context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
	}

	private class SyntaxReceiver : ISyntaxContextReceiver
	{
		public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
		{
			// ...
		}
	}

	public void Execute(GeneratorExecutionContext context)
	{
		// ...
	}
}
```

The `BuildArgsSerializationCodeGenerator` is where everything starts. When the compiler loads the plugin assembly it'll scan over its contents and find this type. The `[Generator]` attribute and the `ISourceGenerator` interface tell it that it should instantiate the class and call its members at the appropriate compilation stages.

Source generation then works in two passes. In the first pass, the source generators are given a representation of the parsed source as it was when given to the compiler. They can traverse the syntax tree to find whatever information it is that they need to do their code-generation work. In the second pass, they generate code.

```csharp
context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
```

This just means that the generator needs to participate in the first pass, and it tells the compiler where to send information about the parse tree.

## Finding the parameter packs

In this case, a parameter pack is defined as just a `record class` which derives from a specific type, called `BuildArgs`. To find them all, the `SyntaxReceiver` does this:

```csharp
private class SyntaxReceiver : ISyntaxContextReceiver
{
	public List<SerializableRecordTypeInfo> SerializableRecordTypes { get; } = [];

	public INamedTypeSymbol? SerializableArgsRecordType;
	public INamedTypeSymbol? BuildArgsType;

	public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
	{
		if (context.Node is not RecordDeclarationSyntax recDecl)
			return;

		SerializableArgsRecordType ??= context.SemanticModel.Compilation.GetTypeByMetadataName("Content.Build.SerializableArgsRecord")!;
		BuildArgsType ??= context.SemanticModel.Compilation.GetTypeByMetadataName("Content.Build.BuildArgs")!;

		var sym = context.SemanticModel.GetDeclaredSymbol(recDecl)!;

		for (var b = sym.BaseType; b != null; b = b.BaseType)
		{
			if (SymbolEqualityComparer.Default.Equals(b, BuildArgsType))
			{
				SerializableRecordTypes.Add(new(recDecl, sym, true));
				break;
			}

			if (SymbolEqualityComparer.Default.Equals(b, SerializableArgsRecordType))
			{
				SerializableRecordTypes.Add(new(recDecl, sym, false));
				break;
			}
		}
	}
}

private sealed class SerializableRecordTypeInfo(RecordDeclarationSyntax declaration, INamedTypeSymbol type, bool isBuildArgs)
{
	public readonly RecordDeclarationSyntax Declaration = declaration;
	public readonly INamedTypeSymbol Type = type;

	public readonly bool IsBuildArgs = isBuildArgs;

	public bool IsValid;

	public readonly List<IPropertySymbol> Properties = [];
	public string Signature = "ERROR ERROR ERROR";
	public string SignatureHash = "ERROR ERROR ERROR";
}
```

There are a few notable things to start:

* The syntax receiver can store state.
* The syntax receiver's `OnVisitSyntaxNode` method will be called once for every syntax node in the compilation.
* The syntax receiver can look at more than _just_ one node at a time.

What this code does is fairly straightforward.

First, the generator is interested in types derived from `SerializableArgsRecord`, with possibly some special attention paid to `BuildArgs`. Going in order:

```csharp
if (context.Node is not RecordDeclarationSyntax recDecl)
	return;
```

The generator isn't interested in anything that's not a `record` type.

```csharp
	SerializableArgsRecordType ??= context.SemanticModel.Compilation.GetTypeByMetadataName("Content.Build.SerializableArgsRecord")!;
	BuildArgsType ??= context.SemanticModel.Compilation.GetTypeByMetadataName("Content.Build.BuildArgs")!;
```

If it hasn't already looked up the compiler's internal representation of the `SerializableArgsRecord` and `BuildArgs` types, then it does so the first time it needs them.

```csharp
var sym = context.SemanticModel.GetDeclaredSymbol(recDecl)!;

for (var b = sym.BaseType; b != null; b = b.BaseType)
{
	if (SymbolEqualityComparer.Default.Equals(b, BuildArgsType))
	{
		SerializableRecordTypes.Add(new(recDecl, sym, true));
		break;
	}

	if (SymbolEqualityComparer.Default.Equals(b, SerializableArgsRecordType))
	{
		SerializableRecordTypes.Add(new(recDecl, sym, false));
		break;
	}
}
```

It looks up the semantic representation of the syntax node and goes through its base types. If it finds one of the bases that it's interested in, then it records that information for the generation phase.

## Validating the parameter packs

Validation is done in the second phase, because that's where the API provides a `ReportDiagnostic` method.

To report a diagnostic (warning or error), its format needs to be described to the compiler by setting up a `DiagnosticDescriptor` like this:

```csharp
private static readonly DiagnosticDescriptor BuildArgsMustBePartialRecord = new(
	id: "CTBA0001",
	title: "Bad BuildArgs type declaration",
	messageFormat: "BuildArgs class '{0}' must be a partial record class",
	category: "ContentBuild",
	DiagnosticSeverity.Error,
	isEnabledByDefault: true);
```

There needs to be a unique descriptor instance for every possible diagnostic. Once those are ready to go, the generator starts inspecting and validating the record types:

```csharp
public void Execute(GeneratorExecutionContext context)
{
	var syntaxReceiver = (SyntaxReceiver)context.SyntaxContextReceiver!;
	var serializableRecordTypes = syntaxReceiver.SerializableRecordTypes;

	foreach (var typeInfo in serializableRecordTypes)
	{
		var (decl, type) = (typeInfo.Declaration, typeInfo.Type);
		var properties = typeInfo.Properties;

		//enforce use of the `partial` modifier

		if (!decl.Modifiers.Any(SyntaxKind.PartialKeyword))
		{
			context.ReportDiagnostic(Diagnostic.Create(BuildArgsMustBePartialRecord, decl.GetLocation(), decl.Identifier.ValueText));
			continue;
		}

		//scan over the members looking for errors and building a list of properties to serialize

		var isValid = true;

		foreach (var m in type.GetMembers().OrderBy(m => m.Name))
		{
			if (m.IsStatic)
				continue;
			if (m.IsImplicitlyDeclared)
				continue;

			switch (m)
			{
			case IPropertySymbol prop:
				//don't complain about something like an overridden BuilderVersion
				if (prop.IsOverride && SymbolEqualityComparer.Default.Equals(prop.OverriddenProperty!.ContainingType, syntaxReceiver.BuildArgsType))
					continue;

				//enforce immutability

				if (prop.GetMethod == null || prop.SetMethod == null || !prop.SetMethod.IsInitOnly)
				{
					context.ReportDiagnostic(Diagnostic.Create(BuildArgsPropNotReadonly, prop.Locations[0], type.MetadataName, prop.Name));
					isValid = false;
				}

				//enforce other constraints

				//snip - other constraints omitted for clarity

				//property passes all constraints, accept it
				properties.Add(prop);
				break;

			case IFieldSymbol field:
				//fields are only allowed if they're private and readonly!

				if (field.DeclaredAccessibility != Accessibility.Private)
				{
					context.ReportDiagnostic(Diagnostic.Create(BuildArgsMayNotHaveNonPrivateFields, field.Locations[0], type.MetadataName, field.Name));
					isValid = false;
				}
				if (!field.IsReadOnly)
				{
					context.ReportDiagnostic(Diagnostic.Create(BuildArgsMayNotHaveNonReadonlyFields, field.Locations[0], type.MetadataName, field.Name));
					isValid = false;
				}
				break;

			default:
				//method or something
				continue;
			}
		}

		typeInfo.IsValid = isValid;
		if (!isValid)
			//keep scanning after finding an invalid type
			//report as many errors at once as possible
			continue;

		//continued below...
	}
}
```

The gist should be pretty clear: the generator just looks at the semantic model of each candidate type, complains about things which aren't allowed, and builds up a per-type list of the properties which will need to be serialized.

## Generating a type signature

Content builders change all the time, and so do their parameter packs. _This_ generator doesn't do any fancy versioning on the serialized format. Instead, it computes a signature for the type layout and any time that signature fails to match what's cached on disk, the cached version is taken to be out of date and thus unparseable.

_That_ looks like this:

```csharp
//...continued from above (this is that same typeInfo loop)

foreach (var typeInfo in serializableRecordTypes)
{
	//snipping out the stuff discussed above

	var signatureBuilder = new StringBuilder();
	foreach (var prop in properties)
	{
		signatureBuilder.Append(prop.Name);
		signatureBuilder.Append(": ");
		signatureBuilder.Append(prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
		signatureBuilder.AppendLine();
	}

	typeInfo.Signature = signatureBuilder.ToString();

	var signatureStringBytes = Encoding.UTF8.GetBytes(typeInfo.Signature);
	var signatureHash = hasher.ComputeHash(signatureStringBytes);
	typeInfo.SignatureHash = Convert.ToBase64String(signatureHash);
}

//continued below...
```

This is easy. It just goes through the properties in order to build a string of all their names and types. That string is then hashed, and the hash is the parameter pack's signature. `hasher` can be just about anything that won't produce accidental collisions. And no one's going to be attacking it from across the internet, so there's no need to reach for the latest most secure thing ever. I happen to have picked `SHA1`, but `MD5` would have done as well.

Also note that the signatures of the nested property types _don't_ need to be incorporated into this signature. Signature mismatches in nested types are detected at runtime. It might be an optimization to incorporate those signatures here since they could then be omitted from the serialized nested values, but that's a future enhancement.

This concludes the validation pass over the parameter pack types.

## Generating serialization logic

Finally, the core of the plugin, generating an overload of the `Serialize` method and its corresponding deserialization constructor.

```csharp
//...continued from above (following that typeInfo loop)
foreach (var typeInfo in syntaxReceiver.SerializableRecordTypes)
{
	var (decl, type) = (typeInfo.Declaration, typeInfo.Type);

	//calculate the signature for this type from the properties

	var escapedSignatureString = SyntaxFactory.Literal(typeInfo.Signature).ToFullString();
	var escapedSignatureHashString = SyntaxFactory.Literal(typeInfo.SignatureHash).ToFullString();

	//emit the code for the type

	var implFile = new StringBuilder();
	implFile.AppendLine("#nullable enable");
	implFile.AppendLine("using System;");
	implFile.AppendLine("using Content.Build;");
	//snip

	//reopen the type's namespace

	var nsList = new List<INamespaceSymbol>();
	for (var ns = type.ContainingNamespace; !ns.IsGlobalNamespace; ns = ns.ContainingNamespace)
		nsList.Add(ns);
	nsList.Reverse();
	implFile.AppendLine($"namespace {string.Join(".", nsList.Select(ns => ns.Name))};");

	var bareTypeName = decl.Identifier.ValueText;
	var typeDeclName = $"{decl.Identifier.ValueText}{decl.TypeParameterList?.ToString()}";
	implFile.AppendLine($"partial record {typeDeclName}");
	implFile.AppendLine("{");

	implFile.AppendLine($"\tprivate const string RawSignatureText = {escapedSignatureString};");
	implFile.AppendLine($"\tprivate const string Signature = {escapedSignatureHashString};");

	//add the serialization method

	implFile.AppendLine("public override void Serialize(BuildArgsWriter writer)");
	implFile.AppendLine("{");
	implFile.AppendLine("base.Serialize(writer);");
	implFile.AppendLine($"writer.BeginType(typeof({typeDeclName}), Signature);");
	foreach (var prop in typeInfo.Properties)
	{
		implFile.AppendLine($"writer.BeginProperty(\"{prop.Name}\");");

		EmitWrite(prop.Name, prop.Type);

		void EmitWrite(string valName, ITypeSymbol type, int level = 0, bool isRefChecked = false)
		{
			if (IsNullable(type, out var nulledType))
			{
				//if the type is nullable then serialize its HasValue property
				//and then recurse to emit code that writes its Value

				implFile.AppendLine($"writer.WriteValue({valName}.HasValue);");
				implFile.AppendLine($"if ({valName}.HasValue) {{");
				var nulledName = $"v{level}"; //need a unique name for the temporary value
				implFile.AppendLine($"var {nulledName} = {valName}.Value;");
				EmitWrite(nulledName, nulledType!, level: level + 1);
				implFile.AppendLine("}");
			}
			//snip: similar handling for nullable reference types
			//snip: something similar for ImmutableArray
			//snip: something similar for enums
			//snip: etc, etc
			else
			{
				//emit a call to BuildArgsWriter.WriteValue
				//let the compilation fail if there's no appropriate overload
				implFile.AppendLine($"writer.WriteValue({valName});");
			}
		}

		implFile.AppendLine("writer.EndProperty();");
	}
	implFile.AppendLine("writer.EndType();");
	implFile.AppendLine("}");

	//add a deserializing-constructor which just reverses the above

	var ctorAccess = type.IsAbstract ? "protected" : "public";

	//add a default constructor if there isn't one already
	//this prevents the one we're about to add from suppressing the default one

	if (!type.InstanceConstructors.Any(ctor => ctor.Parameters.Length == 0 && !ctor.IsImplicitlyDeclared))
		implFile.AppendLine($"{ctorAccess} {bareTypeName}() {{ }}");

	implFile.AppendLine($"{ctorAccess} {bareTypeName}(BuildArgsReader reader) : base(reader)");
	implFile.AppendLine("{");
	implFile.AppendLine($"reader.BeginType(typeof({typeDeclName}), Signature);");
	foreach (var prop in typeInfo.Properties)
	{
		//snip: basically a mirror-imageof everything above
	}
	implFile.AppendLine("reader.EndType();");
	implFile.AppendLine("}");

	implFile.AppendLine("}");

	context.AddSource($"BuildArgs.{type.MetadataName}.Generated.cs", implFile.ToString());
}
```

This is all very gnarly, and there might even be bugs lurking about (possibly caused by my aggressive pruning of the actual code). But the gist should be clear.

For each of the build args types, the generator makes a code file that adds an implementation of `Serialize` and the deserializing constructor. The implementation body is just a list of calls to the appropriate methods on `BuildArgsWriter` (or `BuildArgsReader`, when deserializing).

Certain things like `Nullable`, nullable reference types, and arrays (which must be _immutable_) are handled by generating code which just unwraps that layer of the type before writing (or reading) the inner value. There's special handling as well for enums.

The final most-unwrapped value is then written by calling the `BuildArgsWriter.WriteValue` method, which is overloaded _only_ for the root set of allowable types. That includes anoverload for types derived from `SerializableArgsRecord`, which allows for recursive nesting of these types.

But ultimatey, _whatever_ code is generated and _however_ it is structured, it's injected into the compilation with `context.AddSource`, and then the compiler does its magic after that.

# Using the plugin

Two things are required to use the plugin.

The first is a reference in the project file for whatever's going to be using it. This could be a `PackageReference` to a NuGet package that contains the plugin. Or, it can just be a `ProjectReference` if they're both in the same solution:

```xml
<ProjectReference Include="..\BuildContent.CSharpCompilerPlugin\BuildContent.CSharpCompilerPlugin.csproj" PrivateAssets="all" ReferenceOutputAssembly="false" OutputItemType="Analyzer" />
```

The second is some code that matches the criteria which triggers the code generator's logic:

```csharp
public partial record PackFile : BuildArgs
{
	public string Prefix { get; init; } = "";
	public ImmutableValueList<PackFileExport> Exports { get; init; }
	public Compression.Codec? CompressionCodec { get; init; } = Compression.Codec.Lz4;

	//snip
}
```

And that's all it takes to get automagic rule enforcement and generated code to handle serialization, deserialization, etc.