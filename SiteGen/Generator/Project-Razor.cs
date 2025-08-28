using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Extensions;
using Microsoft.AspNetCore.Razor.Language.Intermediate;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Vec3.Site.Generator;

partial class Project
{
	private readonly Lock razorEngineLock = new();
	private readonly RazorProjectEngine razorEngine;

	private static string RazorContentNamespace = typeof(Project).Namespace + ".GeneratedContent";

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

				SectionDirective.Register(b);
			});
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
			classNode.BaseType = info.BaseType.FullName;

			var methodNode = documentNode.FindPrimaryMethod();
			methodNode.MethodName = "GenerateContent";
			methodNode.Modifiers[0] = "protected";

			var baseCtor = info.BaseType.
				GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).
				Where(c => c.IsPublic | c.IsFamily).
				OrderBy(c => c.GetParameters().Length).
				FirstOrDefault();
			if (baseCtor != null && baseCtor.GetParameters().Length != 0)
			{
				var parms = baseCtor.GetParameters();
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

	private static readonly MetadataReference[] metadataReferences =
		[
			MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(Microsoft.AspNetCore.Razor.Hosting.RazorCompiledItemAttribute).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(Project).Assembly.Location),

			//netcore stuff...?
			MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")),
			MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "netstandard.dll"))
		];

	private static readonly Type[] supportedPageTypes =
		[
			typeof(RazorPage),
			typeof(RazorLayout),
		];

	const string GlobalPrefix = "global::";

	private async Task<RazorPage> GetRazorPage(InputFile origin)
	{
		var info = GetRazorPageInfo(origin.ContentRelativePath);
		await info.InitializationTask;

		return (RazorPage)info.Create(origin);
	}
		
	// public async Task<Func<Template>> GetRazorPage(string path)
	// {
	// 	var info = GetRazorPageInfo(path);
	// 	await info.InitializationTask;

	// 	return info.Create;
	// }

	// public async Task<Func<T>> GetRazorPage<T>(string path)
	// 	where T : Template
	// {
	// 	var info = GetRazorPageInfo(path);
	// 	await info.InitializationTask;

	// 	if (!typeof(T).IsAssignableFrom(info.PageType))
	// 		throw new ArgumentException($"The template '{path}' is not derived from {typeof(T).Name}");

	// 	return () => (T)info.Create();
	// }

	private RazorTemplateInfo GetRazorPageInfo(string path)
	{
		lock (razorTemplateInfosLock)
		{
			if (!razorTemplateInfos.TryGetValue(path, out var info))
				razorTemplateInfos.Add(path, info = new(this, path));

			return info;
		}
	}

	private readonly Lock razorTemplateInfosLock = new();
	private readonly Dictionary<string, RazorTemplateInfo> razorTemplateInfos = new(StringComparer.Ordinal);

	private class RazorTemplateInfo
	{
		public RazorTemplateInfo(Project engine, string path)
		{
			if (!File.Exists(path))
				throw new FileNotFoundException("The requested page was not found.", fileName: path);

			SourcePath = path;
			InitializationTask = Task.Run(() => Initialize(engine));
		}

		private void Initialize(Project engine)
		{
			//process the Razor page

			var fileName = Path.GetFileName(SourcePath);
			if (fileName == "_layout.cshtml")
				BaseType = typeof(RazorLayout);
			else if (fileName.StartsWith('_'))
				BaseType = typeof(RazorPartial);

			TypeName = $"{BaseType.Name}_{Helpers.GetHashString(SourcePath)}";

			string source;
			string baseCachePath;
			string assemblyName;
			string assemblyFile;
			lock (engine.razorEngineLock) //RazorProjectEngine is *probably* thread-safe, but I can't find the docs to prove it
			{
				var file = engine.razorEngine.FileSystem.GetItem(SourcePath);
				var code = engine.razorEngine.Process(file);

				baseCachePath = file.RelativePhysicalPath;
				//fix up the cache path
				baseCachePath = Path.ChangeExtension(baseCachePath, null);
				baseCachePath = Path.Combine(engine.RazorCacheDirectory, baseCachePath);

				assemblyFile = baseCachePath + ".dll";

				try
				{
					if (File.GetLastWriteTime(file.PhysicalPath) <= File.GetLastWriteTime(assemblyFile))
						goto assemblyIsUpToDate;
				}
				catch(IOException)
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
			Helpers.WriteAllTextInMaybeMissingDirectory(baseCachePath + ".cs", source);

			//compile the template code

			var compilation = CSharpCompilation.Create(
				assemblyName: assemblyName,
				syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
				references: metadataReferences,
				options: new(OutputKind.DynamicallyLinkedLibrary));

			var diagnostics = compilation.GetDiagnostics();
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

			if (hasError)
			{
				Helpers.DeleteNoThrowIoException(assemblyFile);
				throw new InvalidDataException($"Unable to compile page '{SourcePath}'.");
			}

			var peStream = Helpers.CreateFileInMaybeMissingDirectory(assemblyFile);
			try
			{
				compilation.Emit(
					peStream: peStream,
					options: new(debugInformationFormat: Microsoft.CodeAnalysis.Emit.DebugInformationFormat.Embedded));
			}
			catch
			{
				peStream.Dispose();
				Helpers.DeleteNoThrowIoException(assemblyFile);
			}
			finally
			{
				peStream.Dispose();
			}

			//load the page assembly

		assemblyIsUpToDate:
			assembly = Assembly.LoadFrom(assemblyFile);

			pageType = assembly.GetType($"{Namespace}.{TypeName}");
		}

		public string SourcePath { get; }

		public string Namespace { get; private set; } = RazorContentNamespace;
		public string TypeName { get; private set; } = "Page";
		public Type BaseType { get; private set; } = typeof(RazorPage);

		public Task InitializationTask { get; }

		private Assembly? assembly;

		private Type? pageType;
		public Type PageType => pageType ?? throw new InvalidOperationException();

		public object Create(params object[] args)
		{
			if (!InitializationTask.IsCompleted)
				throw new InvalidOperationException();

			if (InitializationTask.IsFaulted || InitializationTask.IsCanceled)
				InitializationTask.GetAwaiter().GetResult(); //rethrows for us

			return Activator.CreateInstance(pageType!, args: args)!;
		}
	}

}