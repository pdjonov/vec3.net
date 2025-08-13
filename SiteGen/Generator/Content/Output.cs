using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Vec3.Site.Generator.Content;

public class Output
{
	public string ContentDirectory { get; }
	public string OutputDirectory { get; }

	public Dictionary<string, InputItem> Items { get; } = new(StringComparer.OrdinalIgnoreCase);
	public List<string> OldFilesDeleted { get; } = new();
	public List<string> OldDirectoriesDeleted { get; } = new();

	private Output(string contentDirectory)
	{
		this.ContentDirectory = contentDirectory;
		this.OutputDirectory = Path.Combine(ContentDirectory, ".out");
	}

	public static async Task<Output> Generate(string contentDirectory, InputItems input)
	{
		ArgumentNullException.ThrowIfNull(input);
		ArgumentNullException.ThrowIfNullOrWhiteSpace(contentDirectory);

		var ret = new Output(contentDirectory);

		//make sure we have a coherent file structure

		var conflictingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var f in input.Items)
			foreach (var o in f.OutputPaths)
				if (!ret.Items.TryAdd(o, f))
					conflictingPaths.Add(o);

		if (conflictingPaths.Count != 0)
			throw new InvalidDataException("Multiple items are conflicting over the following output paths: " + string.Join(", ", conflictingPaths.Order()));

		//generate the content

		await Task.WhenAll(input.Items.Select(i => i.GenerateContent()));

		//write the content

		foreach (var (path, item) in ret.Items)
		{
			var fullPath = Path.Combine(ret.OutputDirectory, path);

			Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

			using var outStream = File.Create(fullPath);
			await item.WriteContent(path, outStream);
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