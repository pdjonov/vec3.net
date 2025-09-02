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
		var inputItems = new List<ContentItem>();

		//do the initial directory scan

		ScanDirectory(ContentDirectory);

		void ScanDirectory(string path)
		{
			foreach (var f in Directory.GetFiles(path))
			{
				var name = Path.GetFileName(f);
				if (name.StartsWith('.'))
					//skip "hidden" and utility dot-files
					continue;

				var relPath = Helpers.GetProjectRelativePath(relativeTo: ContentDirectory, f);
				var origin = new InputFile(this, contentRelativePath: relPath);
				var item = Path.GetExtension(name) switch
				{
					_ when name.StartsWith('_') => null, //layouts, partials, utility files
					".cshtml" => (ContentItem)new DeferredRazorPage(origin),
					".cs" => (ContentItem)new SiteCode(origin),
					".md" => (ContentItem)new MarkdownPage(origin),
					".alias" => (ContentItem)new AliasContent(origin),
					_ => (ContentItem)new AssetFile(origin),
				};

				if (item != null)
					inputItems.Add(item);
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

		//pull out the contents of site.dll

		CompileSiteCode(inputItems.OfType<SiteCode>());
		inputItems.RemoveAll(it => it is SiteCode);

		//now we can start compiling razor pages...

		for (var i = 0; i < inputItems.Count; i++)
			if (inputItems[i] is DeferredRazorPage d)
				inputItems[i] = await GetRazorPage(d.Origin);

		Debug.Assert(!inputItems.Any(i => i is DummyContent));

		//initialize the items

		while (inputItems.Count != 0)
		{
			var deferredItems = new List<ContentItem>();

			for (var i = 0; i < inputItems.Count; i++)
			{
				var item = inputItems[i];
				switch (item)
				{
				case IEnumeratedContent enumContent when enumContent.IsEnumeratorInstance:
					deferredItems.Add(item);
					inputItems.RemoveAt(i--);
					break;
				}
			}

			await Task.WhenAll(inputItems.Select(i => i.Initialize()));
			content.AddRange(inputItems);
			inputItems.Clear();

			await Task.WhenAll(deferredItems.Select(i => i.Initialize()));
			foreach (var deferred in deferredItems)
			{
				switch (deferred)
				{
				case IEnumeratedContent enumContent:
					{
						var items = enumContent.Enumerator;
						if (items == null)
							goto default;

						foreach (var newIt in items)
						{
							var content = enumContent.CreateInstance(newIt);
							if (content != null)
								inputItems.Add(content);
						}
					}
					break;

				default:
					inputItems.Add(deferred);
					break;
				}
			}
		}
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
		//make sure we have a coherent file structure

		var outputItemsByPath = new Dictionary<string, ContentItem>(StringComparer.OrdinalIgnoreCase);
		var conflictedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var f in content)
			if (f.OutputPath != null)
				if (!outputItemsByPath.TryAdd(f.OutputPath, f))
					conflictedPaths.Add(f.OutputPath);

		if (conflictedPaths.Count != 0)
			throw new InvalidDataException("Multiple items are conflicting over the following output paths: " + string.Join(", ", conflictedPaths.Order()));

		//generate the content

		await Task.WhenAll(outputItemsByPath.Values.Select(i => i.PrepareContent()));

		//track what we're doing in the output

		var entriesByPath = new Dictionary<string, OutputLayout.Entry>(StringComparer.OrdinalIgnoreCase);

		//the output directory has to exist before we can try enumerating it

		Directory.CreateDirectory(OutputDirectory);
		entriesByPath.Add("/", OutputLayout.Entry.ForDirectoryUnchanged("/"));

		//build a map of all the directories we want

		var outputDirectoriesFromContent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var (path, _) in outputItemsByPath)
			outputDirectoriesFromContent.Add(Helpers.RemoveLastPathSegment(path)!);

		string GetOutputRelativePath(string fullPath) => Helpers.GetProjectRelativePath(relativeTo: OutputDirectory, fullPath);

		//bulid a map of all the directories we have and then exclude the ones we want (to get a map of what we must delete)

		var outputDirectoriesFromInitialFileScan = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		ScanForOutputDirectoriesOnDisk("/");
		void ScanForOutputDirectoriesOnDisk(string path)
		{
			outputDirectoriesFromInitialFileScan.Add(path);
			foreach (var dir in Directory.EnumerateDirectories(GetFullOutputPath(path)))
				ScanForOutputDirectoriesOnDisk(GetOutputRelativePath(dir));
		}

		var directoriesToDelete = new HashSet<string>(outputDirectoriesFromInitialFileScan, StringComparer.OrdinalIgnoreCase);
		directoriesToDelete.ExceptWith(outputDirectoriesFromContent);

		//delete any directories we don't want kept

		foreach (var remDir in directoriesToDelete.
			OrderByDescending(d => d.Length)) //a child path can't be shorter than its parent
		{
			var fullPath = GetFullOutputPath(remDir);

			foreach (var file in Directory.EnumerateFiles(fullPath))
			{
				File.Delete(file);

				var relPath = GetOutputRelativePath(file);
				entriesByPath.Add(relPath, OutputLayout.Entry.ForFileDeleted(relPath));
			}

			Directory.Delete(fullPath);

			entriesByPath.Add(remDir, OutputLayout.Entry.ForDirectoryDeleted(remDir));
		}

		//delete old files from the directories we're keeping

		foreach (var dir in outputDirectoriesFromContent)
		{
			if (!outputDirectoriesFromInitialFileScan.Contains(dir))
				continue;

			foreach (var file in Directory.EnumerateFiles(GetFullOutputPath(dir)))
			{
				var relPath = GetOutputRelativePath(file);
				if (outputItemsByPath.TryGetValue(relPath, out var source))
				{
					//speculatively assume the contents won't change (we'll update this later)
					entriesByPath.Add(relPath, OutputLayout.Entry.ForFileUnchanged(source));
				}
				else
				{
					File.Delete(file);

					entriesByPath.Add(relPath, OutputLayout.Entry.ForFileDeleted(relPath));
				}
			}
		}

		//write the new content out to disk

		foreach (var (path, item) in outputItemsByPath.
			OrderBy(p => p.Key.Length)) //child can't have a shorter path than its parent
		{
			var dir = Helpers.RemoveLastPathSegment(path)!;
			if (entriesByPath.TryAdd(dir, OutputLayout.Entry.ForDirectoryUnchanged(dir)))
			{
				//first time we're looking at this dir during the writing phase
				//make sure it's *actually* "unchanged" and not in need of initial creation

				var fullDirectoryPath = GetFullOutputPath(dir);
				if (!Directory.Exists(fullDirectoryPath))
				{
					Directory.CreateDirectory(fullDirectoryPath);

					entriesByPath[dir] = OutputLayout.Entry.ForDirectoryAdded(dir);
				}
			}

			var fullPath = GetFullOutputPath(path);

			var fileAdded = !entriesByPath.ContainsKey(path);
			var fileChanged = fileAdded;
			if (fileAdded)
			{
				using var outStream = File.OpenWrite(fullPath);

				await item.WriteContent(outStream, path);
			}
			else
			{
				//need to do change detection, and avoid touching filestamps without cause

				using var scanStream = File.OpenRead(fullPath);
				var oldLength = scanStream.Length;

				Debug.Assert(oldLength < int.MaxValue); // looooooool, if this ever...

				var newData = new MemoryStream(capacity: (int)oldLength); //guess an initial capacity close to the old size
				await item.WriteContent(newData, path);

				if (oldLength != newData.Length)
				{
					fileChanged = true;
					//nothing to diff
				}
				else
				{
					newData.Position = 0;
					fileChanged = !await Helpers.StreamContentsEqual(scanStream, newData);
				}

				if (fileChanged)
				{
					scanStream.Close(); //reopening the file
					using var outStream = File.Create(fullPath);

					newData.Position = 0;
					await newData.CopyToAsync(outStream);
				}
			}

			if (fileAdded)
				entriesByPath.Add(path, OutputLayout.Entry.ForFileAdded(item));
			else if (fileChanged)
				entriesByPath[path] = OutputLayout.Entry.ForFileUpdated(item);
			else
				Debug.Assert(entriesByPath[path].Action == OutputLayout.Action.Unchanged);

			if (fileAdded || fileChanged)
			{
				var dirState = entriesByPath[dir];
				Debug.Assert(dirState.Action != OutputLayout.Action.Deleted);

				if (dirState.Action == OutputLayout.Action.Unchanged)
					entriesByPath[dir] = OutputLayout.Entry.ForDirectoryUpdated(dir);
			}
		}

		//report what we did

		var ret = new OutputLayout(this);

		ret.Entries.AddRange(entriesByPath.
			OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase).
			Select(e => e.Value));

		return ret;
	}
}

public class OutputLayout
{
	public Project Project { get; }
	public string ContentDirectory => Project.ContentDirectory;
	public string OutputDirectory => Project.OutputDirectory;

	public List<Entry> Entries { get; } = [];

	public readonly struct Entry
	{
		/// <summary>
		/// The path this entry represents.
		/// </summary>
		public string Path { get; }

		/// <summary>
		/// The entry's type.
		/// </summary>
		public EntryType Type { get; }

		public bool IsFile => Type == EntryType.File;
		public bool IsDirectory => Type == EntryType.Directory;

		/// <summary>
		/// The item which generated the entry.
		/// </summary>
		/// <remarks>
		/// Null for directories and for files which were deleted.
		/// </remarks>
		public ContentItem? Source { get; }

		/// <summary>
		/// What happened to this entry.
		/// </summary>
		public Action Action { get; }
		public bool WasAdded => Action == Action.Added;
		public bool WasUpdated => Action == Action.Updated;
		public bool WasUnchanged => Action == Action.Unchanged;
		public bool WasDeleted => Action == Action.Deleted;

		private Entry(ContentItem source, Action action)
		{
			ArgumentNullException.ThrowIfNull(source);
			if (source.OutputPath == null)
				throw new ArgumentException(paramName: nameof(source), message: "Entries for output files must come from sources with a non-null OutputPath.");
			Debug.Assert(action != Action.Deleted);

			Source = source;
			Path = source.OutputPath;
			Type = EntryType.File;
			Action = action;
		}

		private Entry(string path, EntryType type, Action action)
		{
			ArgumentNullException.ThrowIfNull(path);
			Debug.Assert(type != EntryType.File || action == Action.Deleted); //otherwise must supply Source

			Source = null;
			Path = path;
			Type = type;
			Action = action;
		}

		public static Entry ForFileAdded(ContentItem source) => new(source, Action.Added);
		public static Entry ForFileUpdated(ContentItem source) => new(source, Action.Updated);
		public static Entry ForFileUnchanged(ContentItem source) => new(source, Action.Unchanged);
		public static Entry ForFileDeleted(string path) => new(path, EntryType.File, Action.Deleted);

		public static Entry ForDirectoryAdded(string path) => new(path, EntryType.Directory, Action.Added);
		public static Entry ForDirectoryUpdated(string path) => new(path, EntryType.Directory, Action.Updated);
		public static Entry ForDirectoryUnchanged(string path) => new(path, EntryType.Directory, Action.Unchanged);
		public static Entry ForDirectoryDeleted(string path) => new(path, EntryType.Directory, Action.Deleted);
	}

	public enum EntryType
	{
		/// <summary>
		/// A default-initialized <see cref="Entry"/> instance which contains no real data.
		/// </summary>
		Invalid,

		File,
		Directory,
	}

	public enum Action
	{
		Added,
		Updated,
		Unchanged,
		Deleted,
	}

	internal OutputLayout(Project project)
	{
		this.Project = project;
	}
}