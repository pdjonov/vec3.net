using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

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
				Helpers.ValidateRelativePath(value, paramName: nameof(OutputPath));
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

	protected Task<string> LoadText(string relativePath)
	{
		relativePath = Helpers.CombineContentRelativePaths(
			relativeTo: Origin is InputFile inputFileOrigin ?
				Path.GetDirectoryName(inputFileOrigin.ContentRelativePath)! : "",
			path: relativePath);

		//ToDo: track the dependency

		var fullPath = Path.Combine(Project.ContentDirectory, relativePath);
		return File.ReadAllTextAsync(fullPath);
	}
}

public abstract class ContentOrigin
{
	public Project Project { get; }

	protected ContentOrigin(Project project)
	{
		ArgumentNullException.ThrowIfNull(project);

		this.Project = project;
	}
}

public interface IHtmlContent
{
	string Content { get; }
}

public interface IPage
{
	string? Title { get; }
}