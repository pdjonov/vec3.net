using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;

using YamlDotNet.RepresentationModel;

namespace Vec3.Site.Generator;

public class MarkdownPage(InputFile origin) : HtmlContentItem(origin), IPage
{
	private MarkdownDocument? source;

	public string? Title
	{
		get
		{
			ThrowIfNotInitialized();
			return title;
		}
		protected set
		{
			ThrowIfNotInitializing();
			title = value;
		}
	}
	private string? title;

	protected override async Task CoreInitialize()
	{
		var sourceText = await LoadText(ContentRelativePath);

		source = await Project.ParseMarkdown(sourceText);

		OutputPath = Path.ChangeExtension(ContentRelativePath, Path.GetFileNameWithoutExtension(ContentRelativePath) == "index" ? ".html" : null);

		var frontMatterType = Project.GetFrontMatterTypeFor(Origin);

		var frontMatter = (object?)null;
		var permalink = (string?)null;
		var title = (string?)null;

		var frontMatterYamlBlock = source.
			Descendants<YamlFrontMatterBlock>().
			FirstOrDefault();
		if (frontMatterYamlBlock != null)
		{
			var yamlSourceSpan = sourceText.AsSpan(frontMatterYamlBlock.Span.Start, frontMatterYamlBlock.Span.Length);
			yamlSourceSpan = yamlSourceSpan.TrimStart("---");
			yamlSourceSpan = yamlSourceSpan.TrimEnd("---");

			var yamlText = yamlSourceSpan.ToString();

			if (frontMatterType != null)
				frontMatter = Project.YamlDeserializer.Deserialize(yamlText, frontMatterType);

			if (frontMatter == null || permalink == null || title == null)
			{
				var yaml = new YamlStream();
				yaml.Load(new StringReader(yamlText));

				var yamlRoot = (YamlMappingNode)yaml.Documents[0].RootNode;
				frontMatter ??= yamlRoot;

				if (permalink is null &&
					yamlRoot.Children.TryGetValue("permalink", out var permalinkNode) &&
					permalinkNode is YamlScalarNode typedPermalinkNode &&
					typedPermalinkNode.Value is not null)
					permalink = typedPermalinkNode.Value;

				if (title is null &&
					yamlRoot.Children.TryGetValue("title", out var titleNode) &&
					titleNode is YamlScalarNode typedTitleNode)
					title = typedTitleNode.Value;
			}
		}
		else if (frontMatterType != null)
		{
			frontMatter = Activator.CreateInstance(frontMatterType);
		}

		if (frontMatter is IFrontMatter asFrontMatter)
		{
			asFrontMatter.Populate(this);

			permalink = asFrontMatter.Permalink;
			title = asFrontMatter.Title;
		}

		if (!string.IsNullOrEmpty(permalink))
			OutputPath = Helpers.CombineContentRelativePaths(Helpers.RemoveLastPathSegment(Origin.ContentRelativePath)!, permalink);
		this.FrontMatter = frontMatter;
		this.Title = title;
	}

	protected override Task<HtmlLiteral> CoreGenerateContent()
	{
		Debug.Assert(source != null);
		return Project.RenderMarkdown(source);
	}
}