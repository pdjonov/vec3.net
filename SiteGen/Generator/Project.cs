using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Vec3.Site.Generator;

public partial class Project
{
	public string ContentDirectory { get; }

	public string OutputDirectory { get; }

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

		await Task.WhenAll(inputItems.Select(i => i.Initialize()));

		content.AddRange(inputItems);

		//ToDo: generated content
	}

	private async Task<List<ContentItem>> ScanInputDirectory()
	{
		var ret = new List<ContentItem>();

		await ScanDirectory(ContentDirectory);

		async Task ScanDirectory(string path)
		{
			foreach (var f in Directory.GetFiles(path))
			{
				var name = Path.GetFileName(f);
				if (name.StartsWith('.'))
					//skip "hidden" and utility dot-files
					continue;

				var relPath = Path.GetRelativePath(relativeTo: ContentDirectory, f).Replace('\\', '/');
				var origin = new InputFile(this, contentRelativePath: relPath);
				var item = Path.GetExtension(name) switch
				{
					_ when name.StartsWith('_') => null, //layouts, partials, utility files
					".cshtml" => (ContentItem)await GetRazorPage(origin),
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
				await ScanDirectory(dir);
			}
		}

		return ret;
	}

	public async Task<OutputLayout> GenerateOutput()
	{
		var ret = new OutputLayout(this);

		//make sure we have a coherent file structure

		var conflictingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var f in content)
			foreach (var o in f.OutputPaths)
				if (!ret.Items.TryAdd(o, f))
					conflictingPaths.Add(o);

		if (conflictingPaths.Count != 0)
			throw new InvalidDataException("Multiple items are conflicting over the following output paths: " + string.Join(", ", conflictingPaths.Order()));

		//generate the content

		await Task.WhenAll(content.Select(i => i.PrepareContent()));

		//write the content

		foreach (var (path, item) in ret.Items)
		{
			var fullPath = Path.Combine(ret.OutputDirectory, path);

			Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

			using var outStream = File.Create(fullPath);
			await item.WriteContent(outStream, path);
		}

		//clean up old files

		CleanDirectory("");

		bool CleanDirectory(string dir)
		{
			var fullPath = Path.Combine(ret.OutputDirectory, dir);

			var numFiles = 0;

			var filesToRemove = new List<string>();
			foreach (var f in Directory.EnumerateFiles(fullPath))
			{
				var relPath = Path.GetRelativePath(relativeTo: ret.OutputDirectory, path: f);
				numFiles++;

				if (!ret.Items.ContainsKey(relPath))
					filesToRemove.Add(relPath);
			}

			foreach (var f in filesToRemove)
			{
				File.Delete(Path.Combine(ret.OutputDirectory, f));
				ret.OldFilesDeleted.Add(f);
			}

			var numDirectories = 0;

			var dirsToRemove = new List<string>();
			foreach (var d in Directory.EnumerateDirectories(fullPath))
			{
				var relPath = Path.GetRelativePath(relativeTo: ret.OutputDirectory, path: d);
				numDirectories++;

				if (!CleanDirectory(relPath))
					dirsToRemove.Add(relPath);
			}

			foreach (var d in dirsToRemove)
			{
				Directory.Delete(d, recursive: false);
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