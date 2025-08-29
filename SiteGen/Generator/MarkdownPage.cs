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
		var sourceText = await File.ReadAllTextAsync(Origin.FullPath);

		source = Markdown.Parse(sourceText, Project.MarkdownPipeline);

		OutputPaths = [Path.ChangeExtension(Origin.ContentRelativePath, ".html")];

		var frontMatterYamlBlock = source.
			Descendants<YamlFrontMatterBlock>().
			FirstOrDefault();
		if (frontMatterYamlBlock != null)
		{
			var yaml = new YamlStream();
			yaml.Load(new StringReader(sourceText.Substring(frontMatterYamlBlock.Span.Start, frontMatterYamlBlock.Span.Length)));

			var frontMatter = (YamlMappingNode)yaml.Documents[0].RootNode;

			if (frontMatter.Children.TryGetValue("permalink", out var permalinkNode) &&
				permalinkNode is YamlScalarNode typedPermalinkNode &&
				typedPermalinkNode.Value is not null)
				OutputPaths = [Helpers.CombineContentRelativePaths(Path.GetDirectoryName(Origin.ContentRelativePath)!, typedPermalinkNode.Value)];

			if (frontMatter.Children.TryGetValue("title", out var titleNode) &&
				titleNode is YamlScalarNode typedTitleNode)
				Title = typedTitleNode.Value;

			base.FrontMatter = frontMatter;
		}
	}

	protected override async Task<string> CoreGenerateContent()
	{
		Debug.Assert(source != null);

		return Markdown.ToHtml(source, Project.MarkdownPipeline);
	}
}