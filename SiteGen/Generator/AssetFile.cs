using System.IO;
using System.Threading.Tasks;

namespace Vec3.Site.Generator;

public class AssetFile(InputFile origin) : FileContentItem(origin)
{
	protected override Task CoreInitialize()
	{
		OutputPath = Origin.ContentRelativePath;
		return Task.CompletedTask;
	}

	protected override async Task CoreWriteContent(Stream outStream, string outputPath)
	{
		using var inFile = File.OpenRead(Origin.FullPath);
		await inFile.CopyToAsync(outStream);
	}
}