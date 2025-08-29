using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Vec3.Site.Generator;

public partial class Project
{
	public string ContentDirectory { get; }

	public string GetFullContentPath(string contentRelativePath)
	{
		Helpers.ValidateRootedPath(contentRelativePath);
		return Path.Combine(ContentDirectory, contentRelativePath.Substring(1));
	}

	public string OutputDirectory { get; }

	public string GetFullOutputPath(string outputRelativePath)
	{
		Helpers.ValidateRootedPath(outputRelativePath);
		return Path.Combine(OutputDirectory, outputRelativePath.Substring(1));
	}

	public string CacheDirectory { get; }
	public string RazorCacheDirectory { get; }

	public ReadOnlyCollection<ContentItem> Content { get; }
	private readonly List<ContentItem> content;

	protected Project(string contentDirectory)
	{
		ArgumentNullException.ThrowIfNullOrWhiteSpace(contentDirectory);
		if (!Path.IsPathFullyQualified(contentDirectory))
			throw new ArgumentException(paramName: nameof(contentDirectory), message: "The content directory path must be fully qualified.");

		this.ContentDirectory = contentDirectory;
		this.OutputDirectory = Path.Combine(ContentDirectory, ".out");
		this.CacheDirectory = Path.Combine(ContentDirectory, ".cache");
		this.RazorCacheDirectory = Path.Combine(CacheDirectory, "cshtml");

		razorEngine = CreateRazorEngine(this);

		Content = new(content = []);
	}

	public static async Task<Project> Load(string contentDirectory)
	{
		var ret = new Project(contentDirectory);

		await ret.LoadCore();

		return ret;
	}

	protected virtual async Task LoadCore()
	{
		var inputItems = await Task.Run(ScanInputDirectory);

		await CompileSiteCode(inputItems.OfType<SiteCode>());
		inputItems.RemoveAll(it => it is SiteCode);

		for (var i = 0; i < inputItems.Count; i++)
			if (inputItems[i] is DeferredRazorPage d)
				inputItems[i] = await GetRazorPage(d.Origin);

		Debug.Assert(!inputItems.Any(i => i is DummyContent));

		await Task.WhenAll(inputItems.Select(i => i.Initialize()));

		content.AddRange(inputItems);

		//ToDo: generated content
	}

	private async Task<List<ContentItem>> ScanInputDirectory()
	{
		var ret = new List<ContentItem>();

		ScanDirectory(ContentDirectory);

		void ScanDirectory(string path)
		{
			foreach (var f in Directory.GetFiles(path))
			{
				var name = Path.GetFileName(f);
				if (name.StartsWith('.'))
					//skip "hidden" and utility dot-files
					continue;

				var relPath = Path.GetRelativePath(relativeTo: ContentDirectory, f).Replace('\\', '/');
				var origin = new InputFile(this, contentRelativePath: '/' + relPath);
				var item = Path.GetExtension(name) switch
				{
					_ when name.StartsWith('_') => null, //layouts, partials, utility files
					".cshtml" => (ContentItem)new DeferredRazorPage(origin),
					".cs" => (ContentItem)new SiteCode(origin),
					".md" => (ContentItem)new MarkdownPage(origin),
					_ => (ContentItem)new AssetFile(origin),
				};

				if (item != null)
					ret.Add(item);
			}

			foreach (var dir in Directory.EnumerateDirectories(path))
			{
				var name = Path.GetFileName(dir);
				if (name.StartsWith('.'))
					//skip "hidden" and utility dot-dirs
					continue;

				//DO NOT parallelize this without syncing access to shared vars!
				ScanDirectory(dir);
			}
		}

		return ret;
	}

	private abstract class DummyContent(InputFile origin) : FileContentItem(origin)
	{
		protected sealed override Task CoreInitialize() => Task.CompletedTask;
		protected sealed override Task CorePrepareContent() => Task.CompletedTask;
		protected sealed override Task CoreWriteContent(Stream outStream, string outputPath) => throw new NotSupportedException();
	}
	private sealed class DeferredRazorPage(InputFile origin) : DummyContent(origin) { }

	public async Task<OutputLayout> GenerateOutput()
	{
		var ret = new OutputLayout(this);

		//make sure we have a coherent file structure

		var conflictingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var f in content)
			if (f.OutputPath != null)
				if (!ret.Items.TryAdd(f.OutputPath, f))
					conflictingPaths.Add(f.OutputPath);

		if (conflictingPaths.Count != 0)
			throw new InvalidDataException("Multiple items are conflicting over the following output paths: " + string.Join(", ", conflictingPaths.Order()));

		//generate the content

		await Task.WhenAll(content.Select(i => i.PrepareContent()));

		//write the content

		foreach (var (path, item) in ret.Items)
		{
			var fullPath = GetFullOutputPath(path);

			Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

			using var outStream = File.Create(fullPath);
			await item.WriteContent(outStream, path);
		}

		//clean up old files

		CleanDirectory("/");

		bool CleanDirectory(string dir)
		{
			var fullPath = GetFullOutputPath(dir);

			var numFiles = 0;

			var filesToRemove = new List<string>();
			foreach (var f in Directory.EnumerateFiles(fullPath))
			{
				var relPath = '/' + Path.GetRelativePath(relativeTo: ret.OutputDirectory, path: f);
				numFiles++;

				if (!ret.Items.ContainsKey(relPath))
					filesToRemove.Add(relPath);
			}

			foreach (var f in filesToRemove)
			{
				File.Delete(GetFullOutputPath(f));
				ret.OldFilesDeleted.Add(f);
			}

			var numDirectories = 0;

			var dirsToRemove = new List<string>();
			foreach (var d in Directory.EnumerateDirectories(fullPath))
			{
				var relPath = '/' + Path.GetRelativePath(relativeTo: ret.OutputDirectory, path: d);
				numDirectories++;

				if (!CleanDirectory(relPath))
					dirsToRemove.Add(relPath);
			}

			foreach (var d in dirsToRemove)
			{
				Directory.Delete(GetFullOutputPath(d), recursive: false);
				ret.OldFilesDeleted.Add(d);
			}

			return numFiles > filesToRemove.Count ||
				numDirectories > dirsToRemove.Count;
		}

		return ret;
	}
}

public class OutputLayout
{
	public Project Project { get; }

	public string ContentDirectory => Project.ContentDirectory;
	public string OutputDirectory => Project.OutputDirectory;

	public Dictionary<string, ContentItem> Items { get; } = new(StringComparer.OrdinalIgnoreCase);
	public List<string> OldFilesDeleted { get; } = [];
	public List<string> OldDirectoriesDeleted { get; } = [];

	internal OutputLayout(Project project)
	{
		this.Project = project;
	}
}