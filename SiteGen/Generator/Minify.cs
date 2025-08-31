using System;
using System.Threading.Tasks;

using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html;
using AngleSharp.Html.Parser;

using NUglify;
using NUglify.Css;

namespace Vec3.Site.Generator;

public static class Minify
{
	public static Task<string> JavaScript(string content)
	{
		ArgumentNullException.ThrowIfNull(content);

		//Uglify.Js is broken and maintenance is at the "you fix it, make a PR" stage of development
		//therefore, minifying JS content is opt-*in* since otherwise stuff just breaks
		//ToDo: figure out how to shell out to one of the actually-maintained minifiers

		const string MinifyDirective = "/*!!minify!!*/";
		if (!content.StartsWith(MinifyDirective))
			return Task.FromResult(content);
		content = content.Substring(MinifyDirective.Length);

		var res = Uglify.Js(content);
		if (res.HasErrors)
			throw new Exception("Unable to minify JS:\n" + string.Join("\n", res.Errors));
		return Task.FromResult(res.Code);
	}

	public static Task<string> Css(string content) => Css(content, CssType.FullStyleSheet);

	public static Task<string> Css(string content, CssType type)
	{
		ArgumentNullException.ThrowIfNull(content);

		var res = Uglify.Css(content, new CssSettings() { CssType = type });
		if (res.HasErrors)
			throw new Exception("Unable to minify CSS:\n" + string.Join("\n", res.Errors));
		return Task.FromResult(res.Code);
	}

	public static async Task<string> Html(string content)
	{
		ArgumentNullException.ThrowIfNull(content);

		var parser = new HtmlParser(
			new HtmlParserOptions()
			{
				DisableElementPositionTracking = true,
				SkipComments = true,
			});
		var dom = await parser.ParseDocumentAsync(content);

		foreach (var s in dom.GetElementsByTagName("script"))
			s.TextContent = await Minify.JavaScript(s.TextContent);

		foreach (var s in dom.GetElementsByTagName("style"))
			s.TextContent = await Minify.Css(s.TextContent);

		foreach (var n in dom.All)
		{
			var style = n.GetAttribute("style");
			if (style != null)
				n.SetAttribute("style", await Minify.Css(style, CssType.DeclarationList));
		}

		return dom.ToHtml(new MinifyMarkupFormatter() { PreservedTags = [TagNames.Pre, TagNames.Code, TagNames.Textarea] });
	}
}