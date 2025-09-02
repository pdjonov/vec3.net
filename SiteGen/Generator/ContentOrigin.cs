using System;

namespace Vec3.Site.Generator;

public abstract class ContentOrigin
{
	public Project Project { get; }

	protected ContentOrigin(Project project)
	{
		ArgumentNullException.ThrowIfNull(project);

		this.Project = project;
	}
}

public class InputFile : ContentOrigin
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

public class EnumeratedTemplateInstance : InputFile
{
	public InputFile Inner { get; }
	public object Item { get; }

	public EnumeratedTemplateInstance(InputFile inner, object item)
		: base((inner ?? throw new ArgumentNullException(paramName: nameof(inner))).Project, inner.ContentRelativePath)
	{
		ArgumentNullException.ThrowIfNull(item);

		Inner = inner;
		Item = item;
	}

	public override string ToString() => $"{ContentRelativePath}[{Item}]"; //ToDo: figure out how to quickly pretty-print IGrouping<,> (uuuuugh)
}