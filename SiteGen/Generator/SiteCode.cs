using System;
using System.IO;
using System.Threading.Tasks;

namespace Vec3.Site.Generator;

internal sealed class SiteCode(InputFile origin) : FileContentItem(origin)
{
	protected override Task CoreInitialize() => Task.CompletedTask;
	protected override Task CorePrepareContent() => Task.CompletedTask;
	protected override Task CoreWriteContent(Stream outStream, string outputPath) => throw new NotSupportedException();
}