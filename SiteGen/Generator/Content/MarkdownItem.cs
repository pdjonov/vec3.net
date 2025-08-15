using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;

using YamlDotNet.RepresentationModel;

namespace Vec3.Site.Generator.Content;

using Templates;

public class MarkdownFileItem(ContentOrigin.InitialFileScan origin, TemplatingEngine templatingEngine, MarkdownPipeline pipeline) : FileItem(origin)
{
	public MarkdownPipeline Pipeline { get; } = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
	public TemplatingEngine TemplatingEngine { get; } = templatingEngine;

	private MarkdownDocument? source;
	private Content? finalContent;

	public override async Task Initialize()
	{
		if (source != null)
			throw new InvalidOperationException();

		var sourceText = await File.ReadAllTextAsync(Origin.FullPath);

		source = Markdown.Parse(sourceText, Pipeline);

		OutputPaths = [Path.ChangeExtension(ContentRelativePath, ".html")];

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
				OutputPaths = [Helpers.CombineContentRelativePaths(Path.GetDirectoryName(ContentRelativePath)!, typedPermalinkNode.Value)];
		}
	}

	public override async Task GenerateContent()
	{
		if (source == null)
			throw new InvalidOperationException();

		var html = Markdown.ToHtml(source, Pipeline);
		finalContent = new TextContent(html);

		finalContent = await TemplatingEngine.ApplyLayout(ContentRelativePath, finalContent);
	}

	public override async Task WriteContent(string outputPath, Stream stream)
	{
		if (finalContent == null)
			throw new InvalidOperationException();

		var content = finalContent.GetOutput();

		var writer = new StreamWriter(stream);
		await writer.WriteAsync(content);
		await writer.FlushAsync();
	}
}