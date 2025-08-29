using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

using Microsoft.Extensions.FileSystemGlobbing;

using YamlDotNet.Serialization;

using Markdig;

namespace Vec3.Site.Generator;

partial class Project
{
	public MarkdownPipeline MarkdownPipeline { get; } = new MarkdownPipelineBuilder().
		UseAdvancedExtensions().
		UseYamlFrontMatter().
		Build();

	public IDeserializer YamlDeserializer { get; } =
		new DeserializerBuilder().
		WithCaseInsensitivePropertyMatching().
		IgnoreUnmatchedProperties().
		Build();

	public Type? GetFrontMatterTypeFor(InputFile origin)
	{
		var ret = (Type?)null;

		foreach (var (matcher, type) in frontMatterTypes)
		{
			if (!matcher.Match(origin.ContentRelativePath).HasMatches)
				continue;

			if (ret != null)
				throw new InvalidDataException($"Multiple front matter types match '{origin.ContentRelativePath}'.");

			ret = type;
		}

		return ret;
	}

	private List<(Matcher Pattern, Type FrontMatterType)> frontMatterTypes = [];
	private void FindFrontMatterTypes()
	{
		Debug.Assert(siteCodeAssembly != null);

		frontMatterTypes.AddRange(
			from type in siteCodeAssembly.GetExportedTypes()
			let attr = type.GetCustomAttribute<FrontMatterOfAttribute>()
			where attr != null && type.GetConstructor(BindingFlags.Instance | BindingFlags.Public, []) != null
			select (CreateMatcher(attr.Patterns, exclude: attr.Exclude), type));

		static Matcher CreateMatcher(IEnumerable<string>? include, IEnumerable<string>? exclude = null)
		{
			var ret = new Matcher(StringComparison.Ordinal);

			if (include != null)
				foreach (var p in include)
					ret.AddInclude(p);

			if (exclude != null)
				foreach (var p in exclude)
					ret.AddExclude(p);

			return ret;
		}
	}
}