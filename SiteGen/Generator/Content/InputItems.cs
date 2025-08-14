using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;

namespace Vec3.Site.Generator.Content;

using System.Linq;
using Templates;

public class InputItems
{
	private readonly List<InputItem> innerItems;
	public ReadOnlyCollection<InputItem> Items { get; }

	/// <summary>
	/// The root content directory from which items are loaded
	/// </summary>
	public string ContentDirectory { get; }

	public TemplatingEngine TemplatingEngine { get; }

	private InputItems(string contentDirectory)
	{
		ArgumentNullException.ThrowIfNull(contentDirectory);

		Items = new(innerItems = []);

		ContentDirectory = contentDirectory;
		TemplatingEngine = new(contentDirectory);
	}

	public static async Task<InputItems> Load(string contentDirectory)
	{
		var ret = new InputItems(contentDirectory);

		ret.ScanInputDirectory();

		await Task.WhenAll(ret.innerItems.Select(it => it.Initialize()));

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
				var origin = new ContentOrigin.InitialFileScan(fullPath: f, relativePath: relPath);
				var item = Path.GetExtension(name) switch
				{
					".cshtml" when name.StartsWith('_') => null,
					".cshtml" => (FileItem)new RazorFileItem(origin, TemplatingEngine),
					_ => (FileItem)new AssetFileItem(origin),
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