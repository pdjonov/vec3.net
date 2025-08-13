using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;

namespace Vec3.Site.Generator.Content;

public class InputItems
{
	private readonly List<InputItem> innerItems;
	public ReadOnlyCollection<InputItem> Items { get; }

	/// <summary>
	/// The root content directory from which items are loaded
	/// </summary>
	public string ContentDirectory { get; }

	private InputItems(string contentDirectory)
	{
		ArgumentNullException.ThrowIfNull(contentDirectory);

		Items = new(innerItems = []);

		ContentDirectory = contentDirectory;
	}

	public static async Task<InputItems> Load(string contentDirectory)
	{
		var ret = new InputItems(contentDirectory);

		ret.ScanInputDirectory();

		return ret;
	}

	private void ScanInputDirectory()
	{
		ScanDirectory(ContentDirectory);
		void ScanDirectory(string path)
		{
			foreach (var f in Directory.GetFiles(path))
			{
				var name = Path.GetFileName(f);
				if (name.StartsWith('.'))
					//skip "hidden" and utility dot-files
					continue;

				var relPath = Path.GetRelativePath(relativeTo: ContentDirectory, f);
				var item = Path.GetExtension(name) switch
				{
					".cshtml" => null,
					_ => new AssetFileItem(new(fullPath: f,relativePath: relPath)),
				};

				if (item != null)
					innerItems.Add(item);
			}

			foreach (var dir in Directory.EnumerateDirectories(path))
			{
				var name = Path.GetFileName(dir);
				if (name.StartsWith('.'))
					//skip "hidden" and utility dot-dirs
					continue;

				ScanDirectory(dir);
			}
		}
	}
}