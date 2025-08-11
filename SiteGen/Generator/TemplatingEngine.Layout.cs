using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Vec3.Site.Generator;

public partial class TemplatingEngine
{

	public async Task<Content[]> GenerateContent(string path)
	{
		ArgumentNullException.ThrowIfNull(path);
		if (Path.GetFileName(path).StartsWith('_'))
			throw new ArgumentException(paramName: nameof(path), message: "Template files can't be generated as content.");

		var content = Path.GetExtension(path) switch
			{
				".cshtml" => await LoadCshtmlContent(path),

				_ => throw new ArgumentException($"Unsupported content type '{path}'."),
			};

		var layouts = await GetLayoutStack(path);

		var ret = new List<Content>();
		foreach (var innermostContent in content)
		{
			var c = innermostContent;

			foreach (var layout in layouts)
			{
				var l = layout();
				l.Body = c;
				c = l;
			}

			await c.Execute();

			ret.Add(c);
		}

		return ret.ToArray();
	}

	private async Task<ImmutableArray<Func<LayoutTemplate>>> GetLayoutStack(string path)
	{
		var ret = new List<Func<LayoutTemplate>>();

		do
		{
			var dir = Path.GetDirectoryName(path);
			if (dir == null)
				break;

			var layoutFile = Path.Combine(dir!, "_layout.cshtml");
			try
			{
				var layoutFactory = await GetTemplate<LayoutTemplate>(layoutFile);
				ret.Add(layoutFactory);
			}
			catch(FileNotFoundException)
			{
			}

			path = dir;
		}
		while (!string.IsNullOrEmpty(path));

		return ret.ToImmutableArray();
	}

	private async Task<IEnumerable<Content>> LoadCshtmlContent(string path)
	{
		var templateFactory = await GetTemplate(path);

		var rootInstance = templateFactory();

		//ToDo: what if rootInstance says this is an _enumerating_ template?

		return [rootInstance];
	}

}