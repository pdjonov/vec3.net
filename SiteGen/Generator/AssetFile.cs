using System;
using System.Diagnostics;
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

	protected override async Task CorePrepareContent()
	{
		Debug.Assert(processedContent == null);

		var content = await File.ReadAllBytesAsync(Origin.FullPath);

		switch (Path.GetExtension(ContentRelativePath))
		{
		case ".css":
			await MinifyText(Minify.Css);
			break;

		case ".js":
			await MinifyText(Minify.JavaScript);
			break;
		}

		async Task MinifyText(Func<string, Task<string>> minifier)
		{
			var oldLen = content.Length;

			var reader = new StreamReader(new MemoryStream(content), detectEncodingFromByteOrderMarks: true);
			var sourceText = await reader.ReadToEndAsync();

			try
			{
				var minified = await minifier(sourceText);
				content = reader.CurrentEncoding.GetBytes(minified);
			}
			catch
			{
				//ToDo: log this
			}

			Console.WriteLine($"Minified '{ContentRelativePath}': {oldLen} -> {content.Length}");
		}

		processedContent = content;
	}

	private byte[]? processedContent;

	protected override Task CoreWriteContent(Stream outStream, string outputPath)
	{
		Debug.Assert(processedContent != null);

		return outStream.WriteAsync(processedContent).AsTask();
	}
}