using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html;
using AngleSharp.Html.Dom;
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

		var parser = new HtmlParser(
			new HtmlParserOptions()
			{
				SkipComments = false,
			});

		var dom = await parser.ParseDocumentAsync(content);

		var blurbCommentFinder = dom.CreateTreeWalker(dom, FilterSettings.Comment,
			(node) =>
			{
				var marker = ((IComment)node).NodeValue.AsSpan().Trim();
				return marker.Equals("blurb", StringComparison.OrdinalIgnoreCase) ?
					FilterResult.Accept :
					FilterResult.Reject;
			});

		var marker1 = blurbCommentFinder.ToNext();
		var marker2 = blurbCommentFinder.ToNext();

		if (marker1 != null)
		{
			if (marker1.Parent == null)
				throw new InvalidDataException("Invalid blurb comment location.");

			var walker = dom.CreateTreeWalker(marker1.Parent);

			INode? it;
			INode end;

			if (marker2 != null)
			{
				if (marker1.Parent != marker2.Parent)
					throw new InvalidDataException("Blurb markers must be placed at the same level.");

				it = marker1.NextSibling;
				end = marker2;
			}
			else
			{
				it = marker1.Parent.FirstChild;
				end = marker1;
			}

			var blurbBuilder = new StringBuilder();

			while (it != null && it != end)
			{
				if (it is IElement elem)
					blurbBuilder.Append(elem.OuterHtml);
				it = it.NextSibling;
			}

			blurb = blurbBuilder.ToString();
		}
		else if (dom.GetElementsByTagName("p").FirstOrDefault() is IHtmlParagraphElement firstPara)
		{
			blurb = firstPara.OuterHtml;
		}
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

	private string? blurb;
	public async Task<string> GetBlurb()
	{
		ThrowIfNotInitialized();

		await PrepareContent();
		Debug.Assert(blurb != null);

		return blurb;
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
		catch
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