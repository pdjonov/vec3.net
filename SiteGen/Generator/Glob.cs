using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.FileSystemGlobbing;

namespace Vec3.Site.Generator;

public readonly struct Glob
{
	private readonly Matcher? matcher;
	private Glob(Matcher m) => matcher = m;

	public static Glob Create(IEnumerable<string>? include, IEnumerable<string>? exclude = null, bool mustBeAbsolute = true)
	{
		if (mustBeAbsolute)
		{
			if (include != null && !include.Any(i => i.StartsWith('/')))
				throw new ArgumentException(paramName: nameof(include), message: "Patterns must be absolute.");
			if (exclude != null && !exclude.Any(i => i.StartsWith('/')))
				throw new ArgumentException(paramName: nameof(exclude), message: "Patterns must be absolute.");
		}

		var ret = new Matcher(StringComparison.Ordinal);

		if (include != null)
			foreach (var p in include)
				ret.AddInclude(p.TrimStart('/'));

		if (exclude != null)
			foreach (var p in exclude)
				ret.AddExclude(p.TrimStart('/'));

		return new(ret);
	}

	public static Glob Create(string? include, string? exclude = null, bool mustBeAbsolute = true)
	{
		return Create(
			include: include != null ? [include] : null,
			exclude: exclude != null ? [exclude] : null,
			mustBeAbsolute: mustBeAbsolute);
	}

	public bool IsMatch(string path)
	{
		ArgumentNullException.ThrowIfNull(path);

		if (matcher == null)
			return false;

		path = path.TrimLeadingSlash(); //uggggg...

		return matcher.Match(path).HasMatches;
	}
}