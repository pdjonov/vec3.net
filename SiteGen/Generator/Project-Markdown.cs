using Markdig;

namespace Vec3.Site.Generator;

partial class Project
{
	public MarkdownPipeline MarkdownPipeline { get; } = new MarkdownPipelineBuilder().
		UseAdvancedExtensions().
		UseYamlFrontMatter().
		Build();
}