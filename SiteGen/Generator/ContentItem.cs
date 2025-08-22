using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Vec3.Site.Generator;

public abstract class ContentItem
{
	public ContentOrigin Origin { get; }

	protected ContentItem(ContentOrigin origin)
	{
		ArgumentNullException.ThrowIfNull(origin);

		this.Origin = origin;
	}

	public ImmutableArray<string> OutputPaths { get; protected set; } = [];

	private readonly Lock syncObj = new();

	private Task? coreInitializeTask;
	private Task? corePrepareTask;

	/// <summary>
	/// Ensures that the output paths, front matter, and other metadata are ready.
	/// </summary>
	public Task Initialize()
	{
		lock (syncObj)
			return coreInitializeTask ??= CoreInitialize(); //possible lock hazard?
	}

	protected virtual Task CoreInitialize() => Task.CompletedTask;

	/// <summary>
	/// Called once to prepare the content item's contents to be written.
	/// </summary>
	public Task PrepareContent()
	{
		lock (syncObj)
		{
			if (coreInitializeTask == null || !coreInitializeTask.IsCompleted)
				throw new InvalidOperationException("A content item can't be prepared until it's been initialized.");

			if (!coreInitializeTask.IsCompletedSuccessfully)
				throw new InvalidOperationException("the content item can't be prepared because initialization failed.", innerException: coreInitializeTask.GetException());

			return corePrepareTask ??= CorePrepareContent(); //possible lock hazard?
		}
	}

	protected virtual Task CorePrepareContent() => Task.CompletedTask;

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

		lock (syncObj)
		{
			if (corePrepareTask == null || !corePrepareTask.IsCompleted)
				throw new InvalidOperationException("A content item's contents can't be written until it's been prepared.");

			if (!corePrepareTask.IsCompletedSuccessfully)
				throw new InvalidOperationException("The content item can't be written because preparation failed.", corePrepareTask.GetException());
		}

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

public sealed class InputFile : ContentOrigin
{
	public string ContentRelativePath { get; }

	public string FullPath => Path.Combine(Project.ContentDirectory, ContentRelativePath);

	public InputFile(Project project, string contentRelativePath)
		: base(project)
	{
		Helpers.ValidateRelativePath(contentRelativePath);

		this.ContentRelativePath = contentRelativePath;
	}
}