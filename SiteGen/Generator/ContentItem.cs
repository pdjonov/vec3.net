using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.FileSystemGlobbing;

namespace Vec3.Site.Generator;

public abstract class ContentItem
{
	public ContentOrigin Origin { get; }
	public Project Project => Origin.Project;

	protected ContentItem(ContentOrigin origin)
	{
		ArgumentNullException.ThrowIfNull(origin);

		this.Origin = origin;
	}

	private string? outputPath;
	public string? OutputPath
	{
		get
		{
			ThrowIfNotInitialized();
			return outputPath;
		}
		protected set
		{
			if (value != null)
			{
				if (value == "")
					throw new ArgumentException(paramName: nameof(OutputPath), message: "The output path must not be empty.");
				Helpers.ValidateRootedPath(value, paramName: nameof(OutputPath));
			}

			ThrowIfNotInitializing();
			outputPath = value;
		}
	}
	private object? frontMatter;
	public object? FrontMatter
	{
		get
		{
			ThrowIfNotInitialized();
			return frontMatter;
		}
		protected set
		{
			ThrowIfNotInitializing();
			frontMatter = value;
		}
	}

	protected Lock SyncLock { get; } = new();

	private Task? coreInitializeTask;
	private Task? corePrepareTask;

	private bool isInitializing;

	/// <summary>
	/// Ensures that the output paths, front matter, and other metadata are ready.
	/// </summary>
	public Task Initialize()
	{
		lock (SyncLock)
			return coreInitializeTask ??= DoInitialize();

		async Task DoInitialize()
		{
			try
			{
				lock (SyncLock)
					isInitializing = true;

				await CoreInitialize();
			}
			finally
			{
				lock (SyncLock)
					isInitializing = false;
			}
		}
	}

	protected virtual Task CoreInitialize() => Task.CompletedTask;

	protected void ThrowIfNotInitialized()
	{
		lock (SyncLock)
		{
			if (coreInitializeTask == null || !coreInitializeTask.IsCompleted)
				throw new InvalidOperationException("This content item has not been initialized.");

			if (!coreInitializeTask.IsCompletedSuccessfully)
				throw new InvalidOperationException("This content item failed to initialize correctly.", innerException: coreInitializeTask.GetException());
		}
	}

	protected void ThrowIfNotInitializing()
	{
		lock (SyncLock)
		{
			if (!isInitializing)
				throw new InvalidOperationException("This member can only be used during initialization.");
		}
	}

	/// <summary>
	/// Called once to prepare the content item's contents to be written.
	/// </summary>
	public Task PrepareContent()
	{
		lock (SyncLock)
		{
			ThrowIfNotInitialized();
			return corePrepareTask ??= CorePrepareContent();
		}
	}

	protected virtual Task CorePrepareContent() => Task.CompletedTask;

	protected void ThrowIfNotPrepared()
	{
		lock (SyncLock)
		{
			if (corePrepareTask == null || !corePrepareTask.IsCompleted)
				throw new InvalidOperationException("This content item hasn't been prepared.");

			if (!corePrepareTask.IsCompletedSuccessfully)
				throw new InvalidOperationException("This content item failed to prepare correctly.", corePrepareTask.GetException());
		}
	}

	/// <summary>
	/// Called once for each output path to generate the final file data.
	/// </summary>
	/// <param name="outStream">The stream to write the data to.</param>
	/// <param name="outputPath">The path to which data is being written.</param>
	public Task WriteContent(Stream outStream, string outPath)
	{
		ArgumentNullException.ThrowIfNull(outStream);
		if (!outStream.CanWrite)
			throw new ArgumentException(paramName: nameof(outStream), message: "The output stream must be writable.");
		ArgumentNullException.ThrowIfNullOrWhiteSpace(outPath);

		ThrowIfNotPrepared();

		return CoreWriteContent(outStream, outPath);
	}

	protected abstract Task CoreWriteContent(Stream outStream, string outputPath);

	protected string ResolveRelativePath(string relativePath)
	{
		return Helpers.CombineContentRelativePaths(
			relativeTo: Origin is InputFile inputFileOrigin ?
				Path.GetDirectoryName(inputFileOrigin.ContentRelativePath)! : "",
			path: relativePath); ;
	}

	protected InputFile? TryGetInputFile(string relativePath)
	{
		var contentPath = ResolveRelativePath(relativePath);

		//ToDo: track the dependency

		var fullPath = Project.GetFullContentPath(contentPath);
		if (!File.Exists(fullPath))
			return null;

		return new(Project, contentPath);
	}

	protected Task<string> LoadText(string relativePath)
	{
		var contentPath = ResolveRelativePath(relativePath);

		//ToDo: track the dependency

		var fullPath = Project.GetFullContentPath(contentPath);
		return File.ReadAllTextAsync(fullPath);
	}
}

public abstract class FileContentItem : ContentItem
{
	public new InputFile Origin => (InputFile)base.Origin;

	public string ContentRelativePath => Origin.ContentRelativePath;
	public string FullPath => Origin.FullPath;

	protected FileContentItem(InputFile origin)
		: base(origin)
	{
	}
}

public interface IContent
{
	ContentOrigin Origin { get; }
	Project Project { get => Origin.Project; }

	object? FrontMatter { get; }
}

public interface IHtmlLiteral
{
	string Content { get; }
}

public interface IHtmlContent : IContent, IHtmlLiteral
{
	Task<string> GetBlurb() => Task.FromResult("");
	Task<string> GetBlurbText() => Task.FromResult("");
}

public interface IPage : IContent
{
	string? Title { get; }
}

public interface IEnumeratedContent
{
	bool IsEnumeratorInstance { get; } //must return a real value *before* Initialize runs
	IEnumerable<object>? Enumerator { get; }
	ContentItem CreateInstance(object item);
}

public static class ContentItemExtensions
{
	public static IEnumerable<ContentItem> WhereSourcePathMatches(this IEnumerable<ContentItem> items, string? include, string? exclude = null)
	{
		var matcher = Helpers.CreateMatcher(include, exclude);

		return items.Where(it =>
			it.Origin is InputFile inFile &&
			matcher.Match(inFile.ContentRelativePath.Substring(1)).HasMatches);
	}
}

public readonly struct HtmlLiteral : IHtmlLiteral
{
	private readonly string content;

	public static HtmlLiteral Null => default;
	public bool IsNull => content == null;

	public static HtmlLiteral Empty => new("");
	public bool IsEmpty => content == "";

	public bool IsNullOrEmpty => string.IsNullOrEmpty(content);

	public string Content => content ?? throw new InvalidOperationException();

	public static HtmlLiteral Create(string? content) => content != null ? new(content) : default;
	private HtmlLiteral(string content) => this.content = content ?? throw new ArgumentNullException(nameof(content));
}