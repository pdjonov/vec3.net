using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html;
using AngleSharp.Html.Parser;

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

	private Task<string>? postProcessTask;
	private async Task<string> PostProcess()
	{
		Debug.Assert(this.content != null);

		var content = ShouldApplyLayout ?
			await Project.ApplyLayout(this) :
			this.content;

		try
		{
			content = await Minify.Html(content);
		}
		catch (Exception ex)
		{
			//ToDo: log this
		}

		return content;
	}

	protected override async Task CoreWriteContent(Stream outStream, string outputPath)
	{
		lock (SyncLock)
			postProcessTask ??= Task.Run(PostProcess);

		var writer = new StreamWriter(outStream);
		await writer.WriteAsync(await postProcessTask);
		await writer.FlushAsync();
	}
}