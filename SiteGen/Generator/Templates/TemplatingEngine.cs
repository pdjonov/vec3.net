using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Extensions;
using Microsoft.AspNetCore.Razor.Language.Intermediate;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Vec3.Site.Generator.Templates;

public partial class TemplatingEngine
{
	public TemplatingEngine(string contentPath)
	{
		if (!Directory.Exists(contentPath))
			throw new DirectoryNotFoundException($"The content directory '{contentPath}' cannot be found.");

		CompiledTemplateCachePath = Path.Combine(contentPath, ".cache/cshtml");
		Directory.CreateDirectory(CompiledTemplateCachePath);

		ContentPath = contentPath;

		razorEngine = RazorProjectEngine.Create(
			configuration: RazorConfiguration.Create(
				languageVersion: RazorLanguageVersion.Latest,
				configurationName: "Site",
				extensions: []),
			fileSystem: RazorProjectFileSystem.Create(contentPath),
			configure: b =>
			{
				b.Features.Add(new FixupTemplateClass(this));
				b.Features.Add(new FullyQualifyBaseType());

				SectionDirective.Register(b);
			});
	}

	public string ContentPath { get; }
	public string CompiledTemplateCachePath { get; }

	private readonly Lock razorEngineLock = new();
	private readonly RazorProjectEngine razorEngine;

	private class FixupTemplateClass(TemplatingEngine engine) : RazorEngineFeatureBase, IRazorDocumentClassifierPass
	{
		private readonly TemplatingEngine engine = engine;

		public int Order => int.MaxValue;

		public void Execute(RazorCodeDocument codeDocument, DocumentIntermediateNode documentNode)
		{
			TemplateInfo info;
			lock (engine.templateInfosLock)
				info = engine.templateInfos[codeDocument.Source.RelativePath];

			var namespaceNode = documentNode.FindPrimaryNamespace();
			namespaceNode.Content = info.Namespace;

			var classNode = documentNode.FindPrimaryClass();
			classNode.ClassName = info.TypeName;
			classNode.BaseType = info.BaseTypeName;

			var methodNode = documentNode.FindPrimaryMethod();
			methodNode.MethodName = "ExecuteCore";
			methodNode.Modifiers.RemoveAt(0);
			methodNode.Modifiers.Insert(0, "protected");
		}
	}

	private class FullyQualifyBaseType : RazorEngineFeatureBase, IRazorDirectiveClassifierPass
	{
		public int Order => int.MaxValue;

		public void Execute(RazorCodeDocument codeDocument, DocumentIntermediateNode documentNode)
		{
			var classNode = documentNode.FindPrimaryClass();
			switch (classNode.BaseType)
			{
				case nameof(Template):
				case nameof(LayoutTemplate):
					classNode.BaseType = $"global::{typeof(Template).Namespace}.{classNode.BaseType}";
					break;

				default:
					throw new InvalidDataException($"Template has invalid base type '{classNode.BaseType}'.");
			}
		}
	}

	private static readonly MetadataReference[] metadataReferences =
		[
			MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(Microsoft.AspNetCore.Razor.Hosting.RazorCompiledItemAttribute).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(TemplatingEngine).Assembly.Location),

			//netcore stuff...?
			MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")),
			MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "netstandard.dll"))
		];

	public async Task<Func<Template>> GetTemplate(string path)
	{
		var info = GetTemplateInfo(path);
		await info.InitializationTask;

		return info.Create;
	}

	public async Task<Func<T>> GetTemplate<T>(string path)
		where T : Template
	{
		var info = GetTemplateInfo(path);
		await info.InitializationTask;

		if (!typeof(T).IsAssignableFrom(info.TemplateType))
			throw new ArgumentException($"The template '{path}' is not derived from {typeof(T).Name}");

		return () => (T)info.Create();
	}

	private TemplateInfo GetTemplateInfo(string path)
	{
		lock (templateInfosLock)
		{
			if (!templateInfos.TryGetValue(path, out var info))
				templateInfos.Add(path, info = new(this, path));

			return info;
		}
	}

	private readonly Lock templateInfosLock = new();
	private readonly Dictionary<string, TemplateInfo> templateInfos = new(StringComparer.Ordinal);

	private class TemplateInfo
	{
		public TemplateInfo(TemplatingEngine engine, string path)
		{
			if (!File.Exists(path))
				throw new FileNotFoundException("The requested template was not found.", fileName: path);

			SourcePath = path;
			InitializationTask = Task.Run(() => Initialize(engine));
		}

		private void Initialize(TemplatingEngine engine)
		{
			//process the Razor template

			if (Path.GetFileName(SourcePath) == "_layout.cshtml")
				BaseTypeName = typeof(LayoutTemplate).Name;

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
				baseCachePath = Path.Combine(engine.CompiledTemplateCachePath, baseCachePath);

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
					if (ns != Namespace && !ns.StartsWith(typeof(Template).Namespace + "."))
						throw new InvalidDataException($"Templates mustn't move themselves out of the '{Namespace}' namespace");

					Namespace = ns;
				}

				TypeName = finalDoc.FindPrimaryClass().ClassName;

				BaseTypeName = finalDoc.FindPrimaryClass().BaseType;

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
				throw new InvalidDataException($"Unable to compile template '{SourcePath}'.");
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

			//load the template assembly

		assemblyIsUpToDate:
			assembly = Assembly.LoadFrom(assemblyFile);

			templateType = assembly.GetType($"{Namespace}.{TypeName}");
		}

		public string SourcePath { get; }

		public string Namespace { get; private set; } = typeof(TemplatingEngine).Namespace + ".Templates";
		public string TypeName { get; private set; } = "Template";
		public string BaseTypeName { get; private set; } = typeof(Template).Name;

		public Task InitializationTask { get; }

		private Assembly? assembly;

		private Type? templateType;
		public Type TemplateType => templateType ?? throw new InvalidOperationException();

		public Template Create()
		{
			if (!InitializationTask.IsCompleted)
				throw new InvalidOperationException();

			if (InitializationTask.IsFaulted || InitializationTask.IsCanceled)
				InitializationTask.GetAwaiter().GetResult(); //rethrows for us

			return (Template)Activator.CreateInstance(templateType!)!;
		}
	}
}