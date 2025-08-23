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

public class MarkdownPage(InputFile origin) : FileContentItem(origin)
{
	private MarkdownDocument? source;

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

			base.FrontMatter = frontMatter;
		}
	}

	private string? finalContent;

	protected override async Task CorePrepareContent()
	{
		Debug.Assert(source != null);

		var html = Markdown.ToHtml(source, Project.MarkdownPipeline);

		finalContent = html;
	}

	protected override async Task CoreWriteContent(Stream outStream, string outputPath)
	{
		Debug.Assert(finalContent != null);

		var writer = new StreamWriter(outStream);
		await writer.WriteAsync(finalContent);
		await writer.FlushAsync();
	}
}