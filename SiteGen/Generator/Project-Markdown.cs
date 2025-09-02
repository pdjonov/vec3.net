using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

using YamlDotNet.Serialization;

using Markdig;
using System.Threading.Tasks;
using Markdig.Syntax;

namespace Vec3.Site.Generator;

partial class Project
{
	public MarkdownPipeline MarkdownPipeline { get; } = new MarkdownPipelineBuilder().
		UseAdvancedExtensions().
		UseYamlFrontMatter().
		Build();

	public Task<MarkdownDocument> ParseMarkdown(string markdownSource)
	{
		ArgumentNullException.ThrowIfNull(markdownSource);

		var ret = Markdown.Parse(markdownSource, MarkdownPipeline);
		return Task.FromResult(ret);
	}

	public Task<HtmlLiteral> RenderMarkdown(MarkdownDocument? document)
	{
		var ret = (string?)null;

		if (document != null)
			ret = Markdown.ToHtml(document, MarkdownPipeline);

		return Task.FromResult(HtmlLiteral.Create(ret));
	}

	public Task<HtmlLiteral> RenderMarkdown(string? markdown)
	{
		var ret = (string?)null;

		if (markdown != null)
		{
			var doc = Markdown.Parse(markdown, MarkdownPipeline);
			ret = Markdown.ToHtml(doc, MarkdownPipeline);
		}

		return Task.FromResult(HtmlLiteral.Create(ret));
	}

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
			if (!matcher.IsMatch(origin.ContentRelativePath))
				continue;

			if (ret != null)
				throw new InvalidDataException($"Multiple front matter types match '{origin.ContentRelativePath}'.");

			ret = type;
		}

		return ret;
	}

	private List<(Glob Pattern, Type FrontMatterType)> frontMatterTypes = [];
	private void FindFrontMatterTypes()
	{
		Debug.Assert(siteCodeAssembly != null);

		frontMatterTypes.AddRange(
			from type in siteCodeAssembly.GetExportedTypes()
			let attr = type.GetCustomAttribute<FrontMatterOfAttribute>()
			where attr != null && type.GetConstructor(BindingFlags.Instance | BindingFlags.Public, []) != null
			select (Glob.Create(attr.Patterns, exclude: attr.Exclude), type));
	}
}