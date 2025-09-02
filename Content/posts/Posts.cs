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

/// <summary>
/// Just a convenience to wrap the results of posts queries in something more strongly typed.
/// </summary>
public readonly struct PostContent
{
	internal PostContent(MarkdownPage page)
	{
		this.page = page;
	}

	private readonly MarkdownPage page;

	public static PostContent Empty => default;
	public bool IsEmpty => page == null;

	public MarkdownPage Page => page ?? throw new InvalidOperationException();
	public PostFrontMatter FrontMatter => (PostFrontMatter)Page.FrontMatter;

	public string PageTitle => Page.Title;

	public void Deconstruct(out MarkdownPage page, out PostFrontMatter frontMatter)
	{
		if (IsEmpty)
			throw new InvalidOperationException();

		page = Page;
		frontMatter = FrontMatter;
	}

	public PostOfSeriesInfo FindInSeries()
	{
		if (IsEmpty)
			throw new InvalidOperationException();

		var series = FrontMatter.Series;
		if (string.IsNullOrEmpty(series))
			return PostOfSeriesInfo.Empty;

		var postsInSeries = Page.Project.Content.
			GetPosts().
			BySeries(series).
			ToArray();

		var thisPage = Page;
		var idx = Array.FindIndex(postsInSeries, p => p.Page == thisPage);
		if (idx == -1)
			return PostOfSeriesInfo.Empty;

		return new()
		{
			Key = series,
			Title = Posts.SeriesTitle(postsInSeries),

			//Note: postsInSeries is ordered in reverse-chronological order!
			Previous = idx + 1 < postsInSeries.Length ? postsInSeries[idx + 1] : PostContent.Empty,
			Next = idx > 0 ? postsInSeries[idx - 1] : PostContent.Empty,

			Index = idx,
		};
	}
}

/// <summary>
/// Information about a post's participation and location in a series.
/// </summary>
public readonly struct PostOfSeriesInfo
{
	public static PostOfSeriesInfo Empty => default;
	public bool IsEmpty => Key == null;

	public string Key { get; init; }
	public string Title { get; init; }

	public PostContent Previous { get; init; }
	public bool HasPrevious => !IsEmpty && !Previous.IsEmpty;
	public PostContent Next { get; init; }
	public bool HasNext => !IsEmpty && !Next.IsEmpty;

	public int Index { get; init; }
}

public static class Posts
{
	public static readonly int ExcerptsPerListing = 50;

	private static readonly Glob pathGlob = Glob.Create("/posts/*.md");

	public static PostContent TryGetPostInfo(this IContent item, bool checkSourcePath = true)
	{
		if (item is not MarkdownPage mdPage || item.FrontMatter is not PostFrontMatter)
			return PostContent.Empty;

		if (checkSourcePath && !pathGlob.IsMatch(mdPage.ContentRelativePath))
			return PostContent.Empty;

		if (string.IsNullOrEmpty(mdPage.OutputPath))
			return PostContent.Empty;

		return new(mdPage);
	}
	public static PostContent GetPostInfo(this IContent item, bool checkSourcePath = true)
	{
		var ret = TryGetPostInfo(item, checkSourcePath: checkSourcePath);
		if (ret.IsEmpty)
			throw new ArgumentException($"The given content item is expected to be a post, but it isn't.");
		return ret;
	}

	public static bool IsPost(this IHtmlContent item) => !TryGetPostInfo(item).IsEmpty;

	public static IEnumerable<PostContent> GetPosts(this IEnumerable<ContentItem> content)
	{
		return
			from it in content.WhereSourcePathMatches(pathGlob)
			let info = TryGetPostInfo(it, checkSourcePath: false)
			where !info.IsEmpty
			orderby info.FrontMatter.Date descending, info.FrontMatter.Time descending
			select info;
	}

	public static IEnumerable<IGrouping<string, PostContent>> ByTag(this IEnumerable<PostContent> posts)
	{
		return
			from p in posts
			from t in p.FrontMatter.Tags ?? []
			orderby t
			group p by t;
	}

	public static IEnumerable<PostContent> ByTag(this IEnumerable<PostContent> posts, string tag)
	{
		return
			from p in posts
			where (p.FrontMatter.Tags ?? []).Contains(tag)
			select p;
	}

	public static string TagPath(string tag) => "/posts/tags/" + tag;
	public static string TagUrl(string tag) => "/posts/tags/" + Uri.EscapeDataString(tag);

	public static IEnumerable<IGrouping<string, PostContent>> BySeries(this IEnumerable<PostContent> posts)
	{
		return
			from p in posts
			where !string.IsNullOrWhiteSpace(p.FrontMatter.Series)
			group p by p.FrontMatter.Series;
	}

	public static IEnumerable<PostContent> BySeries(this IEnumerable<PostContent> posts, string series)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(series);

		return
			from p in posts
			where p.FrontMatter.Series == series
			select p;
	}

	public static string SeriesTitle(this IEnumerable<PostContent> posts)
	{
		var series = (string?)null;
		var title = (string?)null;

		foreach (var (_, f) in posts)
		{
			if (string.IsNullOrWhiteSpace(f.Series) ||
				(series != null && f.Series != series))
				throw new ArgumentException("All elements of the series enumeration must have valid, matching series keys.");

			series = f.Series;

			var pTitle = f.SeriesTitle;
			if (string.IsNullOrWhiteSpace(pTitle))
				continue;

			if (title != null && title != pTitle)
				throw new Exception($"Posts disagree about the correct title of series {series}.");

			title = pTitle;
		}

		if (title != null)
			return title;

		if (series != null)
			//ToDo: prettify the series key...?
			return series;

		throw new ArgumentException("Can't get or infer a title for an empty series. Duh.");
	}

	public static string SeriesPath(string series) => "/posts/series/" + series;
	public static string SeriesUrl(string series) => "/posts/series/" + Uri.EscapeDataString(series);
}