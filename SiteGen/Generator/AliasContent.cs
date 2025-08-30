using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Vec3.Site.Generator;

public sealed class AliasContent(InputFile file) : FileContentItem(file)
{
	protected override Task CoreInitialize()
	{
		OutputPath = Path.ChangeExtension(Origin.ContentRelativePath, null);
		return Task.CompletedTask;
	}

	protected override async Task CorePrepareContent()
	{
		var source = await File.ReadAllTextAsync(Origin.FullPath);
		var data = Project.YamlDeserializer.Deserialize<AliasData>(source);

		sourceContent = Project.Content.FirstOrDefault(c => c.OutputPath == data.Source);
		if (sourceContent == null)
			throw new InvalidDataException($"Unable to find referenced output {data.Source}.");
	}

	private ContentItem? sourceContent;

	protected override async Task CoreWriteContent(Stream outStream, string outputPath)
	{
		Debug.Assert(sourceContent != null);

		await sourceContent.PrepareContent();
		await sourceContent.WriteContent(outStream, outputPath);
	}

	private struct AliasData
	{
		public string Source;
	}
}