using System;
using System.IO;

namespace Vec3.Site.Generator;

public abstract class RazorPage : FileContentItem
{
	private StringWriter? writer;

	protected RazorPage(InputFile origin) : base(origin)
	{
	}

	protected TextWriter Writer => writer ?? throw new InvalidOperationException();

	protected void Write(object obj) => Writer.Write(obj);
	protected void WriteLiteral(string literal) => Writer.Write(literal);
}

public abstract class LayoutTemplate : RazorPage
{
	protected LayoutTemplate(InputFile origin) : base(origin)
	{
	}
}