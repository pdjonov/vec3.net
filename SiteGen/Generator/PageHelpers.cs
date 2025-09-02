using System;
using System.Collections.Generic;
using System.Linq;

using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace Vec3.Site.Generator;

public static class PageHelpers
{
	public static string TrimPrefix(this string str, string prefix)
	{
		ArgumentNullException.ThrowIfNull(str);
		ArgumentNullException.ThrowIfNull(prefix);

		if (str.StartsWith(prefix))
			str = str.Substring(prefix.Length);
		return str;
	}

	public static string TrimSuffix(this string str, string suffix)
	{
		ArgumentNullException.ThrowIfNull(str);
		ArgumentNullException.ThrowIfNull(suffix);

		if (str.EndsWith(suffix))
			str = str.Substring(0, str.Length - suffix.Length);
		return str;
	}

	public static string TrimLeadingSlash(this string str) => TrimPrefix(str, "/");
	public static string TrimTrailingSlash(this string str) => TrimSuffix(str, "/");

	public static void Deconstruct<K, T>(this IGrouping<K, T> group, out K key, out IEnumerable<T> values)
	{
		key = group.Key;
		values = group;
	}

	public static bool TagNameIs(this IElement element, string tagName)
	{
		ArgumentNullException.ThrowIfNull(element);
		ArgumentNullException.ThrowIfNull(tagName);

		return string.Equals(element.TagName, tagName, StringComparison.OrdinalIgnoreCase);
	}

	public static bool TagNameIs(this IElement element, params ReadOnlySpan<string> tagNames)
	{
		ArgumentNullException.ThrowIfNull(element);
		if (tagNames!.Contains(null))
			throw new ArgumentNullException(nameof(tagNames));

		var tag = element.TagName;
		foreach (var acceptedName in tagNames)
			if (string.Equals(tag, acceptedName, StringComparison.OrdinalIgnoreCase))
				return true;

		return false;
	}

	public static bool HasClass(this IElement element, string @class)
	{
		ArgumentNullException.ThrowIfNull(element);
		ArgumentNullException.ThrowIfNull(@class);

		foreach (var c in element.ClassList)
			if (string.Equals(c, @class, StringComparison.OrdinalIgnoreCase))
				return true;

		return false;
	}

	public static bool IsJavaScript(this INode maybeScript, bool alsoCheckOldTypes = true)
		=> maybeScript is IHtmlScriptElement script && IsJavaScript(script, alsoCheckOldTypes: alsoCheckOldTypes);

	public static bool IsJavaScript(this IHtmlScriptElement script, bool alsoCheckOldTypes = true)
	{
		ArgumentNullException.ThrowIfNull(script);

		var type = script.Type;
		if (type == null || string.Equals(type, "text/javascript", StringComparison.OrdinalIgnoreCase))
			return true;

		if (alsoCheckOldTypes)
			foreach (var oldType in
				(ReadOnlySpan<string>)
				[
					"application/javascript",
					"application/ecmascript",
					"application/x-ecmascript",
					"application/x-javascript",
					"text/ecmascript",
					"text/javascript1.0",
					"text/javascript1.1",
					"text/javascript1.2",
					"text/javascript1.3",
					"text/javascript1.4",
					"text/javascript1.5",
					"text/jscript",
					"text/livescript",
					"text/x-ecmascript",
					"text/x-javascript",
				])
			{
				if (string.Equals(type, oldType, StringComparison.OrdinalIgnoreCase))
					return true;
			}

		return false;
	}
}