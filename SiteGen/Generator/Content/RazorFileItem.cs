using System;
using System.IO;
using System.Threading.Tasks;

namespace Vec3.Site.Generator.Content;

using Templates;

public class RazorFileItem(ContentOrigin.InitialFileScan origin, TemplatingEngine templatingEngine) : FileItem(origin)
{
	public TemplatingEngine TemplatingEngine { get; } = templatingEngine;

	private Template? template;
	private Content? finalContent;

	public override async Task Initialize()
	{
		if (template != null)
			throw new InvalidOperationException();

		var templateFactory = await TemplatingEngine.GetTemplate<Template>(ContentRelativePath);
		template = templateFactory();

		OutputPaths = [Path.ChangeExtension(ContentRelativePath, ".html")];
	}

	public override async Task GenerateContent()
	{
		if (template == null || finalContent != null)
			throw new InvalidOperationException();

		finalContent = await TemplatingEngine.ApplyLayout(ContentRelativePath, template);
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