using System;

namespace Vec3.Site.Generator;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class FrontMatterOfAttribute : Attribute
{
	public FrontMatterOfAttribute(params string[] patterns)
	{
		Patterns = patterns ?? [];
	}

	public string[] Patterns { get; }

	public string[]? Exclude { get; set; }
}

public interface IFrontMatter
{
	/// <summary>
	/// Fill in any values which were not explicitly parsed from the front matter.
	/// </summary>
	void Populate(FileContentItem content) { }

	/// <summary>
	/// The permalink for the page (if specified in front matter).
	/// </summary>
	string? Permalink => null;

	/// <summary>
	/// The page title (if one is specified in the front matter).
	/// </summary>
	string? Title => null;
}