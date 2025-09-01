using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace Vec3.Site.Generator;

public abstract class HtmlContentItem : FileContentItem, IHtmlContent
{
	protected HtmlContentItem(InputFile origin) : base(origin) { }

	protected abstract Task<HtmlLiteral> CoreGenerateContent();

	protected override async Task CorePrepareContent()
	{
		Debug.Assert(content.IsNull);
		content = await CoreGenerateContent();

		blurb = await GenerateBlurb(content);

		if (!blurb.IsNullOrEmpty)
		{
			var blurbElems = new HtmlParser().ParseFragment(blurb.Content, null!);
			blurbText = string.Concat(blurbElems.Select(e => e.Text()));
		}
		else
		{
			blurb = HtmlLiteral.Empty;
			blurbText = "";
		}
	}

	protected virtual async Task<HtmlLiteral> GenerateBlurb(HtmlLiteral content)
	{
		if (content.IsNullOrEmpty)
			return HtmlLiteral.Empty;

		var parser = new HtmlParser(
			new HtmlParserOptions()
			{
				SkipComments = false,
			});

		var dom = await parser.ParseDocumentAsync(content.Content);

		var noblurbCommentFinder = dom.CreateTreeWalker(dom, FilterSettings.Comment,
			(node) =>
			{
				var marker = ((IComment)node).NodeValue.AsSpan().Trim();
				return marker.Equals("no-blurb", StringComparison.OrdinalIgnoreCase) ?
					FilterResult.Accept :
					FilterResult.Reject;
			});
		if (noblurbCommentFinder.ToNext() != null)
			return HtmlLiteral.Empty;

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

			return HtmlLiteral.Create(blurbBuilder.ToString());
		}
		else if (dom.GetElementsByTagName("p").FirstOrDefault() is IHtmlParagraphElement firstPara)
		{
			return HtmlLiteral.Create(firstPara.OuterHtml);
		}

		return HtmlLiteral.Empty;
	}

	private HtmlLiteral content;
	public HtmlLiteral Content
	{
		get
		{
			ThrowIfNotPrepared();
			Debug.Assert(!content.IsNull);
			return content;
		}
	}
	string IHtmlLiteral.Content => Content.Content;

	private HtmlLiteral blurb;
	public async Task<HtmlLiteral> GetBlurb()
	{
		ThrowIfNotInitialized();

		await PrepareContent();
		Debug.Assert(!blurb.IsNull);

		return blurb;
	}

	private string? blurbText;
	public async Task<string> GetBlurbText()
	{
		ThrowIfNotInitialized();

		await PrepareContent();
		Debug.Assert(blurbText != null);

		return blurbText;
	}

	protected virtual bool ShouldApplyLayout => OutputPath != null;

	private Task<HtmlLiteral>? postProcessTask;
	private async Task<HtmlLiteral> PostProcess()
	{
		Debug.Assert(!this.content.IsNull);

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
		await writer.WriteAsync((await postProcessTask).Content);
		await writer.FlushAsync();
	}
}