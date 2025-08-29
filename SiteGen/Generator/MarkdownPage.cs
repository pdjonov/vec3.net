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
		var sourceText = await LoadText(Origin.ContentRelativePath);

		source = await Project.ParseMarkdown(sourceText);

		OutputPaths = [Path.ChangeExtension(Origin.ContentRelativePath, ".html")];

		var frontMatterYamlBlock = source.
			Descendants<YamlFrontMatterBlock>().
			FirstOrDefault();
		if (frontMatterYamlBlock != null)
		{
			var yamlSourceSpan = sourceText.AsSpan(frontMatterYamlBlock.Span.Start, frontMatterYamlBlock.Span.Length);
			yamlSourceSpan = yamlSourceSpan.TrimStart("---");
			yamlSourceSpan = yamlSourceSpan.TrimEnd("---");

			var yamlText = yamlSourceSpan.ToString();

			var frontMatter = (object?)null;
			var permalink = (string?)null;
			var title = (string?)null;

			var frontMatterType = Project.GetFrontMatterTypeFor(Origin);
			if (frontMatterType != null)
			{
				var des = Project.YamlDeserializer.Deserialize(yamlText, frontMatterType);

				if (des is IFrontMatter asFrontMatter)
				{
					asFrontMatter.Populate(this);

					permalink = asFrontMatter.Permalink;
					title = asFrontMatter.Title;
				}

				frontMatter = des;
			}

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

			this.OutputPaths = !string.IsNullOrEmpty(permalink) ?
				[Helpers.CombineContentRelativePaths(Path.GetDirectoryName(Origin.ContentRelativePath)!, permalink)] :
				[Path.ChangeExtension(Origin.ContentRelativePath, ".html")];
			this.FrontMatter = frontMatter;
			this.Title = title;
		}
	}

	protected override async Task<string> CoreGenerateContent()
	{
		Debug.Assert(source != null);

		return Markdown.ToHtml(source, Project.MarkdownPipeline);
	}
}