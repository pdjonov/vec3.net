using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Vec3.Site.Generator;

public abstract class HtmlContentItem : FileContentItem, IHtmlContent
{
	protected HtmlContentItem(InputFile origin) : base(origin) { }

	protected abstract Task<string> CoreGenerateContent();

	protected override async Task CorePrepareContent()
	{
		Debug.Assert(content == null);
		content = await CoreGenerateContent() ?? "";
	}

	private string? content;
	public string Content
	{
		get
		{
			ThrowIfNotPrepared();
			Debug.Assert(content != null);
			return content;
		}
	}

	protected virtual bool ShouldApplyLayout => OutputPath != null;

	private Task<string>? applyLayoutTask;

	protected override async Task CoreWriteContent(Stream outStream, string outputPath)
	{
		lock (SyncLock)
		{
			Debug.Assert(content != null);
			applyLayoutTask ??= ShouldApplyLayout ?
				Task.Run(() => Project.ApplyLayout(this)) :
				Task.FromResult(content);
		}

		var writer = new StreamWriter(outStream);
		await writer.WriteAsync(await applyLayoutTask);
		await writer.FlushAsync();
	}
}