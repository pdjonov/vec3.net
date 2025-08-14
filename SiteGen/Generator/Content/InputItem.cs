using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;

namespace Vec3.Site.Generator.Content;

using Templates;

public abstract class InputItem
{
	public ContentOrigin Origin { get; }

	protected InputItem(ContentOrigin origin)
	{
		ArgumentNullException.ThrowIfNull(origin);
		this.Origin = origin;
	}

	public ImmutableArray<string> OutputPaths { get; protected set; }

	public virtual Task Initialize() => Task.CompletedTask;

	public virtual Task GenerateContent() => Task.CompletedTask;
	public abstract Task WriteContent(string outputPath, Stream stream);
}

public abstract class ContentOrigin
{
	public sealed class InitialFileScan : ContentOrigin
	{
		public string FullPath { get; }
		public string RelativePath { get; }

		public InitialFileScan(string fullPath, string relativePath)
		{
			ArgumentNullException.ThrowIfNullOrWhiteSpace(fullPath);
			ArgumentNullException.ThrowIfNull(relativePath);

			FullPath = fullPath;
			RelativePath = relativePath;
		}

		public override string ToString() => RelativePath;
	}

	public sealed class ContentGenerator : ContentOrigin
	{
		/// <summary>
		/// The content item which generated this item.
		/// </summary>
		public InputItem GeneratedBy { get; }
		/// <summary>
		/// The generator pass in which this item was generated.
		/// Content items generated directly from file items are in generation zero,
		/// items generated from *those* items are generation one, etc.
		/// </summary>
		public int Generation { get; }

		public ContentGenerator(InputItem generatedBy, int generation)
		{
			ArgumentNullException.ThrowIfNull(generatedBy);
			ArgumentOutOfRangeException.ThrowIfNegative(generation);
			if (generation != ((generatedBy.Origin as ContentGenerator)?.Generation ?? 0) + 1)
				throw new ArgumentException(paramName: nameof(generation), message: "Content generations must increment by one");

			this.GeneratedBy = generatedBy;
			this.Generation = generation;
		}
	}
}

public abstract class FileItem(ContentOrigin.InitialFileScan origin) : InputItem(origin)
{
	public new ContentOrigin.InitialFileScan Origin => (ContentOrigin.InitialFileScan)base.Origin;

	public string RelativePath => Origin.RelativePath;

	public override Task Initialize()
	{
		OutputPaths = [RelativePath];
		return Task.CompletedTask;
	}
	public override string ToString() => Path.GetFileName(Origin.RelativePath);
}

public class AssetFileItem(ContentOrigin.InitialFileScan origin)
	: FileItem(origin)
{
	public override async Task WriteContent(string outputPath, Stream stream)
	{
		using var data = File.OpenRead(Origin.FullPath);
		await data.CopyToAsync(stream);
	}
}

public class RazorFileItem(ContentOrigin.InitialFileScan origin, TemplatingEngine templatingEngine) : FileItem(origin)
{
	public TemplatingEngine TemplatingEngine { get; } = templatingEngine;

	private Template? template;
	private Content? finalContent;

	public override async Task Initialize()
	{
		if (template != null)
			throw new InvalidOperationException();

		var templateFactory = await TemplatingEngine.GetTemplate<Template>(RelativePath);
		template = templateFactory();

		OutputPaths = [Path.ChangeExtension(RelativePath, ".html")];
	}

	public override async Task GenerateContent()
	{
		if (template == null || finalContent != null)
			throw new InvalidOperationException();

		finalContent = await TemplatingEngine.ApplyLayout(RelativePath, template);
	}
	public override async Task WriteContent(string outputPath, Stream stream)
	{
		if (finalContent == null)
			throw new InvalidOperationException();

		var content = finalContent.GetOutput();

		var writer = new StreamWriter(stream);
		await writer.WriteAsync(content);
		await writer.FlushAsync();
	}
}