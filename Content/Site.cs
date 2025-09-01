using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using AngleSharp.Dom;
using AngleSharp.Html.Dom;

public static class Site
{
	public static readonly string RootUri = "https://vec3.net";

	public static string? GetCanonicalUrl(this ContentItem item)
	{
		ArgumentNullException.ThrowIfNull(item);

		if (item is RazorLayout layout)
			item = (ContentItem)(layout.InnermostBody ?? throw new ArgumentNullException(nameof(item)));

		var ret = item.OutputPath;

		if (ret == null)
			return null;

		Debug.Assert(ret.StartsWith('/'));

		const string IndexFile = "index.html";
		if (Path.GetFileName(ret) == IndexFile)
			ret = ret.Substring(0, ret.Length - IndexFile.Length);

		if (ret.EndsWith('/'))
			ret = ret.Substring(0, ret.Length - 1);

		Debug.Assert(ret == "" || ret.StartsWith('/'));

		return RootUri + ret;
	}

	public static string? NullIfEmpty(this string? str)
	{
		if (string.IsNullOrEmpty(str))
			return null;

		return str;
	}

	public static HashSet<string> GetPageFeatures(IElement pageBody)
	{
		var ret = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var child in pageBody.GetDescendants())
		{
			switch (child)
			{
				case IElement code when code.TagNameIs("code"):
					foreach (var c in code.ClassList)
						if (c.StartsWith("language-", StringComparison.OrdinalIgnoreCase))
							if (ret.Add(c))
								ret.Add("hljs");
					break;

				case IElement math when math.TagNameIs("span", "div") && math.HasClass("math"):
					ret.Add("math");
					break;

				case IHtmlScriptElement script when script.IsJavaScript():
					if ((script.Text ?? "").Contains("sketch.load"))
						ret.Add("vec3.sketch");
					break;
			}
		}

		return ret;
	}
}