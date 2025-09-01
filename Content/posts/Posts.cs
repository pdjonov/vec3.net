using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

[FrontMatterOf("/posts/*.md")]
public class PostFrontMatter : IFrontMatter
{
	public string? Title { get; set; } //IFrontMatter.Title
	public string? Permalink { get; set; } //IFrontMatter.Permalink
	public DateTime Date;
	public TimeSpan Time; //only used when multiple posts share a date
	public DateTime[]? Updated;
	public string Author = "phill";
	public string? Series;
	public string? SeriesTitle;
	public string[]? Tags;
	public Note[]? Notes;

	public DateTime LastUpdated
	{
		get
		{
			var ret = Date;
			if (Updated != null)
				foreach (var u in Updated)
					if (u > ret)
						ret = u;
			return ret;
		}
	}

	void IFrontMatter.Populate(FileContentItem page)
	{
		var fileName = Path.GetFileNameWithoutExtension(page.ContentRelativePath);
		var match = parseFileName.Match(fileName);
		if (!match.Success)
			throw new InvalidDataException($"Malformed post filename '{fileName}'.");

		var dateGroup = match.Groups["date"];
		if (dateGroup.Success)
		{
			var parsed = DateTime.ParseExact(dateGroup.ValueSpan, "yyyy-MM-dd", CultureInfo.InvariantCulture);
			if (Date == default)
				Date = parsed;
			if (Date != parsed)
				throw new InvalidDataException("The front matter and filename must not disagree on the date.");
		}
		else if (Date == default)
		{
			throw new InvalidDataException("If a date is not provided in the front matter then it must be inferrable from the filename.");
		}

		Permalink ??= match.Groups["link"].Value;
	}

	private static readonly Regex parseFileName = new Regex(@"^(?:(?<date>\d{4}-\d{2}-\d{2})-)?(?<link>.+)$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);
}

public class Note
{
	public string Type = "info";
	public DateTime Date;
	public string Text;
}

public static class Posts
{
	public static readonly int ExcerptsPerListing = 50;

	public static IEnumerable<(MarkdownPage Page, PostFrontMatter FrontMatter)> GetPosts(this IEnumerable<ContentItem> content)
	{
		return
			from it in content.WhereSourcePathMatches("/posts/*.md")
			let ret = (Page: it as MarkdownPage, FrontMatter: it.FrontMatter as PostFrontMatter)
			where ret.Page != null && ret.FrontMatter != null && !string.IsNullOrWhiteSpace(ret.Page.OutputPath)
			orderby ret.FrontMatter.Date descending, ret.FrontMatter.Time descending
			select ret;
	}

	public static IEnumerable<IGrouping<string, (MarkdownPage Page, PostFrontMatter FrontMatter)>> ByTag(this IEnumerable<(MarkdownPage Page, PostFrontMatter FrontMatter)> posts)
	{
		return
			from p in posts
			from t in p.FrontMatter.Tags ?? []
			orderby t
			group p by t;
	}

	public static IEnumerable<(MarkdownPage Page, PostFrontMatter FrontMatter)> ByTag(this IEnumerable<(MarkdownPage Page, PostFrontMatter FrontMatter)> posts, string tag)
	{
		return
			from p in posts
			where (p.FrontMatter.Tags ?? []).Contains(tag)
			select p;
	}

	public static IEnumerable<IGrouping<string, (MarkdownPage Page, PostFrontMatter FrontMatter)>> BySeries(this IEnumerable<(MarkdownPage Page, PostFrontMatter FrontMatter)> posts)
	{
		return
			from p in posts
			where !string.IsNullOrWhiteSpace(p.FrontMatter.Series)
			group p by p.FrontMatter.Series;
	}

	public static string SeriesTitle(IGrouping<string, (MarkdownPage Page, PostFrontMatter FrontMatter)> series)
	{
		var ret = (string?)null;
		foreach (var p in series)
		{
			var title = p.FrontMatter.SeriesTitle;
			if (string.IsNullOrWhiteSpace(title))
				continue;

			if (ret != null && ret != title)
				throw new Exception($"Posts disagree about the correct title of series {series.Key}.");

			ret = title;
		}

		return ret ?? series.Key;
	}

	public static string TagPath(string tag)
	{
		return "/posts/tags/" + tag;
	}

	public static string TagUrl(string tag)
	{
		return "/posts/tags/" + Uri.EscapeDataString(tag);
	}
}