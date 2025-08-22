using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace Vec3.Site.Generator;

public abstract class Template : ContentItem
{
	private StringWriter? writer;

	protected Template(ContentOrigin origin) : base(origin)
	{
	}

	protected TextWriter Writer => writer ?? throw new InvalidOperationException();

	protected void Write(object obj) => Writer.Write(obj);
	protected void WriteLiteral(string literal) => Writer.Write(literal);
}

public abstract class LayoutTemplate : Template
{
	protected LayoutTemplate(ContentOrigin origin) : base(origin)
	{
	}
}