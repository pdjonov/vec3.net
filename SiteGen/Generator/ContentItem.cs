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

	private ImmutableArray<string> outputPaths = [];
	public ImmutableArray<string> OutputPaths
	{
		get
		{
			ThrowIfNotInitialized();
			return outputPaths;
		}
		protected set
		{
			ThrowIfNotInitializing();
			outputPaths = !value.IsDefault ? value : [];
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

	private readonly Lock syncObj = new();

	private Task? coreInitializeTask;
	private Task? corePrepareTask;

	private bool isInitializing;

	/// <summary>
	/// Ensures that the output paths, front matter, and other metadata are ready.
	/// </summary>
	public Task Initialize()
	{
		lock (syncObj)
			return coreInitializeTask ??= DoInitialize();

		async Task DoInitialize()
		{
			try
			{
				lock (syncObj)
					isInitializing = true;

				await CoreInitialize();
			}
			finally
			{
				lock (syncObj)
					isInitializing = false;
			}
		}
	}

	protected virtual Task CoreInitialize() => Task.CompletedTask;

	protected void ThrowIfNotInitialized()
	{
		lock (syncObj)
		{
			if (coreInitializeTask == null || !coreInitializeTask.IsCompleted)
				throw new InvalidOperationException("This content item has not been initialized.");

			if (!coreInitializeTask.IsCompletedSuccessfully)
				throw new InvalidOperationException("This content item failed to initialize correctly.", innerException: coreInitializeTask.GetException());
		}
	}

	private void ThrowIfNotInitializing()
	{
		lock (syncObj)
		{
			if (!isInitializing)
				throw new InvalidOperationException("These properties can only be modified during initialization.");
		}
	}

	/// <summary>
	/// Called once to prepare the content item's contents to be written.
	/// </summary>
	public Task PrepareContent()
	{
		lock (syncObj)
		{
			ThrowIfNotInitialized();
			return corePrepareTask ??= CorePrepareContent();
		}
	}

	protected virtual Task CorePrepareContent() => Task.CompletedTask;

	protected void ThrowIfNotPrepared()
	{
		lock (syncObj)
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