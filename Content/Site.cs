using System;
using System.Diagnostics;
using System.IO;

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
}