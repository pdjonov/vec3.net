using System.IO;

namespace Vec3.Site.Generator;

public abstract class FileContentItem : ContentItem
{
	public new InputFile Origin => (InputFile)base.Origin;

	public string ContentRelativePath => Origin.ContentRelativePath;
	public string FullPath => Origin.FullPath;

	protected FileContentItem(InputFile origin)
		: base(origin)
	{
	}
}

public sealed class InputFile : ContentOrigin
{
	public string ContentRelativePath { get; }

	public string FullPath => Project.GetFullContentPath(ContentRelativePath);

	public InputFile(Project project, string contentRelativePath)
		: base(project)
	{
		Helpers.ValidateRootedPath(contentRelativePath);

		this.ContentRelativePath = contentRelativePath;
	}

	public override string ToString() => ContentRelativePath;
}