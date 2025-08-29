using System;

[FrontMatterOf("posts/*.md")]
class PostModel
{
	public string? Title;
	public DateTime? Date;
	public string? Permalink;
	public string[]? Tags;
}