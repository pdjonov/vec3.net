using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Extensions;
using Microsoft.AspNetCore.Razor.Language.Intermediate;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Vec3.Site.Generator;

partial class Project
{
	private readonly Lock razorEngineLock = new();
	private readonly RazorProjectEngine razorEngine;

	private static string RazorContentNamespace = "GeneratedSiteContent";

	private static RazorProjectEngine CreateRazorEngine(Project project)
	{
		return RazorProjectEngine.Create(
			configuration: RazorConfiguration.Create(
				languageVersion: RazorLanguageVersion.Latest,
				configurationName: "Site",
				extensions: []),
			fileSystem: RazorProjectFileSystem.Create(project.ContentDirectory),
			configure: b =>
			{
				b.Features.Add(new FixupRazorClass(project));
				b.Features.Add(new FullyQualifyBaseType());

				Directives.Register(b, project);
				SectionDirective.Register(b);
			});
	}

	private class Directives : IntermediateNodePassBase, IRazorDirectiveClassifierPass
	{
		private Directives(Project project)
		{
			this.project = project;
		}

		private readonly Project project;

		public static void Register(RazorProjectEngineBuilder builder, Project project)
		{
			builder.AddDirective(PageDirective);
			builder.AddDirective(ModelDirective);
			builder.AddDirective(InitializeDirective);
			builder.AddDirective(EnumerateDirective);
			builder.Features.Add(new Directives(project));
		}

		public static readonly DirectiveDescriptor PageDirective = DirectiveDescriptor.CreateSingleLineDirective(
			"page",
			builder =>
			{
				builder.Usage = DirectiveUsage.FileScopedSinglyOccurring;
				builder.AddOptionalStringToken("title", "The page's title.");
			});

		public static readonly DirectiveDescriptor ModelDirective = DirectiveDescriptor.CreateSingleLineDirective(
			"model",
			builder =>
			{
				builder.Usage = DirectiveUsage.FileScopedSinglyOccurring;
				builder.AddTypeToken("type", "The model type.");
			});

		public static readonly DirectiveDescriptor InitializeDirective = DirectiveDescriptor.CreateCodeBlockDirective(
			"initialize",
			builder =>
			{
				builder.Usage = DirectiveUsage.FileScopedMultipleOccurring;
			});

		public static readonly DirectiveDescriptor EnumerateDirective = DirectiveDescriptor.CreateCodeBlockDirective(
			"enumerate",
			builder =>
			{
				builder.Usage = DirectiveUsage.FileScopedSinglyOccurring;
				builder.AddTypeToken("type", "The model type for each Instance.");
				builder.AddOptionalMemberToken("name", "The name of the instance model field.");
			});

		protected override void ExecuteCore(RazorCodeDocument codeDocument, DocumentIntermediateNode documentNode)
		{
			var classNode = documentNode.FindPrimaryClass();
			if (classNode == null)
				return;

			var pageDirective = documentNode.FindDirectiveReferences(PageDirective).SingleOrDefault();
			if (pageDirective.Node is DirectiveIntermediateNode pageNode)
			{
				classNode.Interfaces.Add(GlobalPrefix + typeof(IPage).FullName);
				classNode.Children.Add(new PropertyDeclarationIntermediateNode()
				{
					Modifiers = { "public" },
					PropertyType = "string",
					PropertyName = nameof(IPage.Title),
				});

				if (pageNode.Tokens.FirstOrDefault() is DirectiveTokenIntermediateNode titleToken)
				{
					var initMethod = GetInitializeMethod(classNode);

					initMethod.Children.Add(new CSharpCodeIntermediateNode()
					{
						Children =
					{
						new IntermediateToken()
						{
							Kind = TokenKind.CSharp,
							Content = "Title = ",
						},
						new IntermediateToken()
						{
							Kind = TokenKind.CSharp,
							Content = titleToken.Content,
						},
						new IntermediateToken()
						{
							Kind = TokenKind.CSharp,
							Content = ";",
						},
					},
						Source = titleToken.Source,
					});
				}
			}

			var modelDirective = documentNode.FindDirectiveReferences(ModelDirective).SingleOrDefault();
			if (modelDirective.Node is DirectiveIntermediateNode modelNode)
			{
				var typeName = modelNode.Tokens.Single().Content;

				classNode.Children.Add(new CSharpCodeIntermediateNode()
				{
					Children =
					{
						new IntermediateToken()
						{
							Kind = TokenKind.CSharp,
							Content = $"public new {typeName} Model => ({typeName})base.Model;",
						}
					}
				});
			}

			foreach (var initializeDirective in documentNode.FindDirectiveReferences(InitializeDirective))
			{
				var initMethod = GetInitializeMethod(classNode);

				initMethod.Children.AddRange(initializeDirective.Node.Children);
			}

			var enumerateDirective = documentNode.FindDirectiveReferences(EnumerateDirective).SingleOrDefault();
			if (enumerateDirective.Node is DirectiveIntermediateNode enumerateNode)
			{
				RazorTemplateInfo info;
				lock (project.razorTemplateInfosLock)
					info = project.razorTemplateInfos[codeDocument.Source.RelativePath];

				classNode.BaseType = typeof(EnumeratedRazorPage).FullName;

				var tokens = enumerateNode.Tokens.ToArray();
				var typeName = tokens[0].Content;
				var name = tokens.Length >= 2 ? tokens[1].Content : "Item";

				classNode.Children.Add(new CSharpCodeIntermediateNode()
				{
					Children =
					{
						new IntermediateToken()
						{
							Kind = TokenKind.CSharp,
							Content = $"public {(name == "Item" ? "new" : "")} {typeName} {name} => ({typeName})base.Item;",
						}
					}
				});

				var innerEnumerateMethod = new MethodDeclarationIntermediateNode()
				{
					Modifiers = { "async" },
					ReturnType = $"{GlobalPrefix}{typeof(Task).FullName}",
					MethodName = "EnumerateImpl",
				};
				innerEnumerateMethod.Children.AddRange(enumerateNode.Children.SkipWhile(c => c is DirectiveTokenIntermediateNode));

				var enumerateBlock = new CSharpCodeIntermediateNode()
				{
					Children =
					{
						new IntermediateToken()
						{
							Kind = TokenKind.CSharp,
							Content =
								"if (base.IsEnumeratorInstance) {\n" +
								$"{GlobalPrefix}{typeof(IEnumerable<>).FullNameWithoutGenericTag()}<{typeName}> Items = null!;\n" +
								$"{GlobalPrefix}{typeof(Func<,>).FullNameWithoutGenericTag()}<{typeName}, string> OutputPath = null!;\n",
						},
						innerEnumerateMethod,
						new IntermediateToken()
						{
							Kind = TokenKind.CSharp,
							Content =
								$"await {innerEnumerateMethod.MethodName}();\n" +
								"base.InitializeEnumerator(Items, OutputPath);\n" +
								"return;\n" +
								"}",
						},
					}
				};

				var initMethod = GetInitializeMethod(classNode);
				initMethod.Children.Insert(0, enumerateBlock);

				var originTypeName = GlobalPrefix + typeof(EnumeratedTemplateInstance).FullName;
				var outputPathTypeName = $"{GlobalPrefix}{typeof(Func<,>).FullNameWithoutGenericTag()}<object, string>";

				classNode.Children.Add(
					new MethodDeclarationIntermediateNode()
					{
						Modifiers = { "protected", "override" },
						ReturnType = GlobalPrefix + typeof(ContentItem).FullName,
						Parameters =
						{
							new MethodParameter()
							{
								TypeName = originTypeName,
								ParameterName = "origin",
							},
							new MethodParameter()
							{
								TypeName = outputPathTypeName,
								ParameterName = "outputPath",
							},
						},
						MethodName = "CreateInstance",
						Children =
						{
							new CSharpCodeIntermediateNode()
							{
								Children =
								{
									new IntermediateToken()
									{
										Kind = TokenKind.CSharp,
										Content = $"return new {classNode.ClassName}(origin, outputPath);"
									},
								}
							}
						},
					});

				classNode.Children.Add(new CSharpCodeIntermediateNode()
				{
					Children =
					{
						new IntermediateToken()
						{
							Kind = TokenKind.CSharp,
							Content = $"private {classNode.ClassName}({originTypeName} origin, {outputPathTypeName} outputPath) : base(origin, outputPath) {{ }}"
						}
					}
				});
			}
		}
	}

	private class FixupRazorClass(Project engine) : RazorEngineFeatureBase, IRazorDocumentClassifierPass
	{
		private readonly Project engine = engine;

		public int Order => int.MaxValue;

		public void Execute(RazorCodeDocument codeDocument, DocumentIntermediateNode documentNode)
		{
			RazorTemplateInfo info;
			lock (engine.razorTemplateInfosLock)
				info = engine.razorTemplateInfos[codeDocument.Source.RelativePath];

			var namespaceNode = documentNode.FindPrimaryNamespace();
			namespaceNode.Content = info.Namespace;

			var classNode = documentNode.FindPrimaryClass();
			classNode.ClassName = info.TypeName;
			classNode.BaseType ??= info.BaseType.FullName;

			var methodNode = documentNode.FindPrimaryMethod();
			methodNode.MethodName = "ExecuteTemplate";
			methodNode.Modifiers[0] = "protected";

			foreach (var ctor in info.BaseType.
				GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).
				Where(c => c.IsPublic | c.IsFamily))
			{
				var parms = ctor.GetParameters();
				classNode.Children.Insert(0, new CSharpCodeIntermediateNode()
				{
					Children =
					{
						new IntermediateToken()
						{
							Kind = TokenKind.CSharp,
							Content =
								$"public {classNode.ClassName}" +
								$"({string.Join(", ", parms.Select(p => $"global::{p.ParameterType.FullName} {p.Name}"))})" +
								$": base({string.Join(", ", parms.Select(p => p.Name))}) {{ }}\n"
						}
					}
				});
			}
		}
	}

	private class FullyQualifyBaseType : RazorEngineFeatureBase, IRazorDirectiveClassifierPass
	{
		public int Order => int.MaxValue;

		public void Execute(RazorCodeDocument codeDocument, DocumentIntermediateNode documentNode)
		{
			var classNode = documentNode.FindPrimaryClass();

			var type = supportedPageTypes.FirstOrDefault(t =>
				classNode.BaseType == t.Name ||
				classNode.BaseType == t.FullName ||
				classNode.BaseType == GlobalPrefix + t.FullName);

			if (type == null)
				throw new InvalidDataException($"Razor page has invalid base type '{classNode.BaseType}'.");

			classNode.BaseType = GlobalPrefix + type.FullName;
		}
	}

	private static MethodDeclarationIntermediateNode GetInitializeMethod(ClassDeclarationIntermediateNode classNode)
	{
		var ret = classNode.Children.
			OfType<MethodDeclarationIntermediateNode>().
			FirstOrDefault(tok => tok.MethodName == "InitializeTemplate");
		if (ret == null)
		{
			ret = new MethodDeclarationIntermediateNode()
			{
				Modifiers = { "protected", "override", "async" },
				ReturnType = GlobalPrefix + typeof(Task).FullName,
				MethodName = "InitializeTemplate",
			};

			classNode.Children.Add(ret);
		}

		return ret;
	}

	private readonly List<MetadataReference> metadataReferences = LoadMetadataReferences();

	private static List<MetadataReference> LoadMetadataReferences()
	{
		var ret = new List<MetadataReference>();

		var binDir = Path.GetDirectoryName(typeof(Project).Assembly.Location)!;
		var refDir = Path.Combine(binDir, "refs");

		foreach (var asm in Directory.EnumerateFiles(refDir, "*.dll"))
			ret.Add(MetadataReference.CreateFromFile(asm));

		Type[] referenceAssembliesOf =
			[
				typeof(Project),
				typeof(Microsoft.AspNetCore.Razor.Hosting.RazorCompiledItemAttribute),
				typeof(AngleSharp.Dom.Document),
			];
		foreach (var typ in referenceAssembliesOf)
			ret.Add(MetadataReference.CreateFromFile(typ.Assembly.Location));

		return ret;
	}

	private readonly AssemblyLoadContext assemblyLoadContext = new("Site Content", isCollectible: true);
	private Assembly? siteCodeAssembly;
	private DateTime siteCodeAssemblyTimestamp;

	private sealed class SiteCode(InputFile origin) : DummyContent(origin) { }

	private void CompileSiteCode(IEnumerable<SiteCode> code)
	{
		code = code.ToArray();

		var siteAssemblyPath = Path.Combine(CacheDirectory, "site.dll");
		var siteDllTime = File.Exists(siteAssemblyPath) ?
			File.GetLastWriteTime(siteAssemblyPath) :
			DateTime.MinValue;

		var selfRefTime = File.GetLastWriteTime(typeof(Project).Assembly.Location);

		var siteSourceTime = siteDllTime;

		if (selfRefTime > siteSourceTime)
			siteSourceTime = selfRefTime;

		foreach (var c in code)
		{
			var time = File.GetLastWriteTime(c.FullPath);
			if (time > siteSourceTime)
				siteSourceTime = time;
		}

		if (siteSourceTime > siteDllTime)
		{
			Console.WriteLine($"Compiling '{Path.GetRelativePath(CacheDirectory, siteAssemblyPath)}'");

			var compilation = CSharpCompilation.Create(
				assemblyName: "site",
				syntaxTrees: code.Select(c => ParseCSharpSource(c.FullPath)).
				Append(CreateGlobalUsingsSource(typeof(Project).Namespace!)).
				ToArray() /* do the file reading outside the Create call */,
				references: metadataReferences,
				options: new(
					OutputKind.DynamicallyLinkedLibrary,
					nullableContextOptions: NullableContextOptions.Enable));

			if (!CheckDiagnostics(compilation.GetDiagnostics()))
			{
				Helpers.DeleteNoThrowIoException(siteAssemblyPath);
				throw new InvalidDataException($"Unable to compile site source.");
			}

			EmitAssembly(compilation, siteAssemblyPath);

			siteDllTime = File.GetLastWriteTime(siteAssemblyPath);
		}
		else if (siteSourceTime == DateTime.MinValue)
		{
			//there's no source, there should be no assembly
			Helpers.DeleteNoThrowIoException(siteAssemblyPath);

			//still update the timestamp to force Razor templates to rebuild
			siteCodeAssemblyTimestamp = selfRefTime;

			return;
		}

		metadataReferences.Add(MetadataReference.CreateFromFile(siteAssemblyPath));
		siteCodeAssembly = assemblyLoadContext.LoadFromAssemblyPath(siteAssemblyPath);

		siteCodeAssemblyTimestamp = siteDllTime;

		FindFrontMatterTypes();
	}

	private static readonly CSharpParseOptions csParseOptions = new(preprocessorSymbols: ["DEBUG"]);

	private static CSharpSyntaxTree ParseCSharpSource(string path, CancellationToken cancellationToken = default)
	{
		using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
		var sourceText = reader.ReadToEnd();
		return (CSharpSyntaxTree)CSharpSyntaxTree.ParseText(sourceText,
			path: path,
			encoding: reader.CurrentEncoding,
			options: csParseOptions,
			cancellationToken: cancellationToken);
	}

	private static SyntaxTree CreateGlobalUsingsSource(params IEnumerable<string> namespaces)
	{
		var source = new StringBuilder();

		foreach (var ns in namespaces)
		{
			source.Append("global using ");
			if (!ns.StartsWith(GlobalPrefix))
				source.Append(GlobalPrefix);
			source.Append(ns);
			source.Append(";\n");
		}

		return CSharpSyntaxTree.ParseText(source.ToString());
	}

	private static bool CheckDiagnostics(ImmutableArray<Diagnostic> diagnostics)
	{
		var hasError = false;
		foreach (var d in diagnostics)
		{
			if (d.Severity < DiagnosticSeverity.Warning)
				continue;

			if (d.Severity == DiagnosticSeverity.Error)
				hasError = true;

			lock (Console.Error)
			{
				if (!Console.IsErrorRedirected)
					Console.ForegroundColor = d.Severity switch
					{
						DiagnosticSeverity.Error => ConsoleColor.Red,
						DiagnosticSeverity.Warning => ConsoleColor.DarkYellow,
						DiagnosticSeverity.Info => ConsoleColor.Gray,
						DiagnosticSeverity.Hidden => ConsoleColor.DarkGray,

						_ => ConsoleColor.White,
					};

				Console.Error.WriteLine(d.ToString());

				if (!Console.IsErrorRedirected)
					Console.ResetColor();
			}
		}

		return !hasError;
	}

	private static void EmitAssembly(CSharpCompilation compilation, string assemblyFile)
	{
		using var peStream = Helpers.CreateFileInMaybeMissingDirectory(assemblyFile);
		try
		{
			var res = compilation.Emit(
				peStream: peStream,
				options: new(debugInformationFormat: Microsoft.CodeAnalysis.Emit.DebugInformationFormat.Embedded));
			if (!CheckDiagnostics(res.Diagnostics) || !res.Success)
				throw new InvalidDataException($"Failed to emit '{assemblyFile}'.");
		}
		catch
		{
			peStream.Dispose();
			Helpers.DeleteNoThrowIoException(assemblyFile);

			throw;
		}
	}

	private static readonly Type[] supportedPageTypes =
		[
			typeof(RazorPage),
			typeof(EnumeratedRazorPage),
			typeof(RazorLayout),
			typeof(RazorPartial),
		];

	private const string GlobalPrefix = "global::";

	private Task<RazorPage> GetRazorPage(InputFile origin)
		=> GetRazorTemplate<RazorPage>(origin);

	public Task<RazorPartial> GetRazorPartial(InputFile origin, object? model = null)
		=> GetRazorTemplate<RazorPartial>(origin, model);

	private async Task<T> GetRazorTemplate<T>(InputFile origin, params object?[] additionalArgs)
		where T : RazorTemplate
	{
		var info = GetRazorPageInfo(origin);
		await info.WaitForInitialization();

		Debug.Assert(info.PageType.IsAssignableTo(typeof(T)));

		return (T)info.Create(additionalArgs);
	}

	public async Task<HtmlLiteral> ApplyLayout(HtmlContentItem content)
	{
		ArgumentNullException.ThrowIfNull(content);

		var fullContentPath = content.FullPath;

		var body = content;
		for (string dir, path = content.ContentRelativePath; path != "/"; path = dir)
		{
			dir = Path.GetDirectoryName(path)!;

			var fullDirPath = GetFullContentPath(dir);

			var layout = await GetLayout(
				fullDirectoryPath: fullDirPath,
				relPath: Path.GetRelativePath(relativeTo: fullDirPath, fullContentPath));
			if (layout == null)
				continue;

			layout.Body = body;

			await layout.Initialize();
			await layout.PrepareContent();

			body = layout;
		}

		return body.Content;
	}

	[GeneratedRegex("^_layout(?:{(?<pattern>.+)})?.cshtml$", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
	private static partial Regex LayoutTemplateNameMatcher();

	private struct DirectoryLayoutTemplates
	{
		private (Matcher FilenameMatcher, RazorTemplateInfo Template)[] filteredTemplates;
		private RazorTemplateInfo? defaultTemplate;

		public readonly bool IsEmpty => filteredTemplates == null || (filteredTemplates.Length == 0 && defaultTemplate == null);

		public readonly RazorTemplateInfo? GetLayoutFor(string relPath)
		{
			if (filteredTemplates != null)
				foreach (var (m, t) in filteredTemplates)
					if (m.Match(relPath).HasMatches)
						return t;

			return defaultTemplate;
		}

		public static DirectoryLayoutTemplates ForDirectory(Project project, string fullDirectoryPath)
		{
			var ret = new DirectoryLayoutTemplates();

			var filtered = new List<(Matcher FilenameMatcher, RazorTemplateInfo Template)>();

			foreach (var f in Directory.EnumerateFiles(fullDirectoryPath, "_layout*.cshtml").Order(StringComparer.Ordinal))
			{
				var parts = LayoutTemplateNameMatcher().Match(Path.GetFileName(f));

				Debug.Assert(parts.Success);

				var layoutSource = new InputFile(project, '/' + Path.GetRelativePath(project.ContentDirectory, f));
				var template = project.GetRazorPageInfo(layoutSource);

				var pattern = parts.Groups["pattern"];
				if (pattern.Success)
				{
					var glob = Uri.UnescapeDataString(pattern.Value);
					var matcher = Helpers.CreateMatcher(glob, mustBeAbsolute: false);
					filtered.Add((matcher, template));
				}
				else
				{
					ret.defaultTemplate = template;
				}
			}

			ret.filteredTemplates = filtered.ToArray();

			return ret;
		}
	}

	private readonly Lock layoutTemplateInfosLock = new();
	private readonly Dictionary<string, DirectoryLayoutTemplates> layoutTemplateInfos = new(StringComparer.Ordinal);

	private async Task<RazorLayout?> GetLayout(string fullDirectoryPath, string relPath)
	{
		bool hasInfos;
		DirectoryLayoutTemplates infos;
		lock (layoutTemplateInfosLock)
			hasInfos = layoutTemplateInfos.TryGetValue(fullDirectoryPath, out infos);

		if (!hasInfos)
		{
			infos = DirectoryLayoutTemplates.ForDirectory(this, fullDirectoryPath);
			lock (layoutTemplateInfosLock)
				if (!layoutTemplateInfos.TryAdd(fullDirectoryPath, infos))
					infos = layoutTemplateInfos[fullDirectoryPath];
		}

		var templateInfo = infos.GetLayoutFor(relPath);
		if (templateInfo == null)
			return null;

		await templateInfo.WaitForInitialization();

		if (!templateInfo.PageType.IsAssignableTo(typeof(RazorLayout)))
			throw new InvalidDataException("A layout file has an invalid base type.");

		return (RazorLayout)templateInfo.Create();
	}

	private RazorTemplateInfo GetRazorPageInfo(InputFile origin)
	{
		ArgumentNullException.ThrowIfNull(origin);

		lock (razorTemplateInfosLock)
		{
			var key = origin.ContentRelativePath.Substring(1);
			if (!razorTemplateInfos.TryGetValue(key, out var info))
				razorTemplateInfos.Add(key, info = new(origin));

			return info;
		}
	}

	private readonly Lock razorTemplateInfosLock = new();
	private readonly Dictionary<string, RazorTemplateInfo> razorTemplateInfos = new(StringComparer.Ordinal);

	private class RazorTemplateInfo
	{
		public RazorTemplateInfo(InputFile origin)
		{
			this.Origin = origin;
		}
		
		private Task? initializationTask;
		public Task WaitForInitialization()
		{
			lock (Project.razorTemplateInfosLock)
				return initializationTask ??= Task.Run(Initialize);
		}

		private void Initialize()
		{
			var sourcePath = Origin.ContentRelativePath.Substring(1);

			//process the Razor page

			var fileName = Path.GetFileName(sourcePath);
			if (LayoutTemplateNameMatcher().IsMatch(fileName))
				BaseType = typeof(RazorLayout);
			else if (fileName.StartsWith('_'))
				BaseType = typeof(RazorPartial);

			TypeName = $"{BaseType.Name}_{Helpers.GetHashString(sourcePath)}";

			string source;
			string baseCachePath;
			string assemblyName;
			string assemblyFile;
			lock (Project.razorEngineLock) //RazorProjectEngine is *probably* thread-safe, but I can't find the docs to prove it
			{
				var file = Project.razorEngine.FileSystem.GetItem(sourcePath);
				var code = Project.razorEngine.Process(file);

				baseCachePath = file.RelativePhysicalPath;
				//fix up the cache path
				baseCachePath = Path.ChangeExtension(baseCachePath, null);
				baseCachePath = Path.Combine(Project.RazorCacheDirectory, baseCachePath);

				assemblyFile = baseCachePath + ".dll";

				try
				{
					var cachedTime = File.Exists(assemblyFile) ?
						File.GetLastWriteTime(assemblyFile) :
						DateTime.MinValue;
					if (Project.siteCodeAssemblyTimestamp <= cachedTime &&
						File.GetLastWriteTime(file.PhysicalPath) <= cachedTime)
						goto assemblyIsUpToDate;
				}
				catch (IOException)
				{
				}

				Console.WriteLine($"Compiling '{file.RelativePhysicalPath}'");

				assemblyName = $"_{code.Source.GetChecksumAlgorithm()}_{Convert.ToHexString(code.Source.GetChecksum())}";

				var finalDoc = code.GetDocumentIntermediateNode();
				{
					var ns = finalDoc.FindPrimaryNamespace().Content;
					if (ns != Namespace && !ns.StartsWith(RazorContentNamespace + "."))
						throw new InvalidDataException($"Templates mustn't move themselves out of the '{Namespace}' namespace");

					Namespace = ns;
				}

				TypeName = finalDoc.FindPrimaryClass().ClassName;

				{
					var baseTypeName = finalDoc.FindPrimaryClass().BaseType;

					if (baseTypeName.StartsWith(GlobalPrefix))
						baseTypeName = baseTypeName.Substring(GlobalPrefix.Length);

					var baseType = typeof(Project).Assembly.GetType(baseTypeName);
					if (baseType == null || !supportedPageTypes.Contains(baseType))
						throw new InvalidDataException("Invalid base type for razor template.");

					BaseType = baseType;
				}

				source = code.GetCSharpDocument().GeneratedCode;
			}

			//makes it easier to debug if we can see what's happening
			var csSourcePath = baseCachePath + ".cs";
			Helpers.WriteAllTextInMaybeMissingDirectory(csSourcePath, source, Encoding.UTF8);

			//compile the template code

			var compilation = CSharpCompilation.Create(
				assemblyName: assemblyName,
				syntaxTrees: [
					CSharpSyntaxTree.ParseText(source, path: csSourcePath, encoding: Encoding.UTF8, options: csParseOptions),
					CreateGlobalUsingsSource(
						"System",
						"System.Collections.Generic",
						"System.Linq",
						typeof(Project).Namespace!),
					],
				references: Project.metadataReferences,
				options: new(OutputKind.DynamicallyLinkedLibrary));

			if (!CheckDiagnostics(compilation.GetDiagnostics()))
			{
				Helpers.DeleteNoThrowIoException(assemblyFile);
				throw new InvalidDataException($"Unable to compile page '{Origin}'.");
			}

			EmitAssembly(compilation, assemblyFile);

		//load the page assembly

		assemblyIsUpToDate:
			assembly = Project.assemblyLoadContext.LoadFromAssemblyPath(assemblyFile);

			pageType = assembly.GetType($"{Namespace}.{TypeName}");
		}

		public InputFile Origin { get; }
		public Project Project => Origin.Project;

		public string Namespace { get; private set; } = RazorContentNamespace;
		public string TypeName { get; private set; } = "Page";
		public Type BaseType { get; private set; } = typeof(RazorPage);

		private Assembly? assembly;

		private Type? pageType;
		public Type PageType => pageType ?? throw new InvalidOperationException();

		public object Create(params ReadOnlySpan<object?> extraArgs)
		{
			var initializationTask = WaitForInitialization();

			if (!initializationTask.IsCompleted)
				throw new InvalidOperationException();

			if (initializationTask.IsFaulted || initializationTask.IsCanceled)
				initializationTask.GetAwaiter().GetResult(); //rethrows for us

			return Activator.CreateInstance(pageType!, args: [Origin, ..extraArgs])!;
		}
	}

}