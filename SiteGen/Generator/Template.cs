using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace Vec3.Site.Generator;

public abstract class Template : Content
{
	private StringWriter? writer;
	protected TextWriter Writer => writer ?? throw new InvalidOperationException();

	protected void Write(object obj) => Writer.Write(obj);
	protected void WriteLiteral(string literal) => Writer.Write(literal);

	private void BeginWriting()
	{
		if (writer != null)
			throw new InvalidOperationException();

		writer = new(CultureInfo.InvariantCulture);
	}

	private string EndWriting()
	{
		Debug.Assert(writer != null);

		var ret = writer.ToString();
		writer = null;

		return ret;
	}

	protected override void BeforeExecuteCore() => BeginWriting();
	protected override void AfterExecuteCore(Exception? fault)
	{
		output = EndWriting();
		if (fault != null)
			output = null;
	}

	private string? output;
	protected override string GetOutputCore() => output ?? throw new InvalidOperationException();

	private sealed class SectionContent(Template owner, Func<Task> execFunc) : Content
	{
		private readonly Template owner = owner;
		private readonly Func<Task> execFunc = execFunc;

		protected override async Task ExecuteCore()
		{
			owner.BeginWriting();
			try
			{
				await execFunc();
			}
			catch
			{
				//discard the partial data
				_ = owner.EndWriting();
			}

			Output = owner.EndWriting();
		}

		internal string? Output;
		protected override string GetOutputCore() => Output ?? throw new InvalidOperationException();
	}

	private readonly Dictionary<string, SectionContent> sections = [];
	protected void DefineSection(string name, Func<Task> value)
	{
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(value);

		sections.Add(name, new(this, value));
	}

	public override Func<Task<Content>>? GetSection(string name)
	{
		ThrowIfNotExecutedSuccessfully();

		if (!sections.TryGetValue(name, out var sec))
			return null;

		return async () =>
			{
				await sec.Execute();
				return sec;
			};
	}
}

public abstract class LayoutTemplate : Template
{
	public Content? Body
	{
		get => body;
		set
		{
			if (value == null)
				throw new ArgumentNullException(nameof(Body));
			if (body != null)
				throw new InvalidOperationException();

			body = value;
		}
	}
	private Content? body;

	protected async Task<Content> RenderBody()
	{
		var b = body ?? throw new InvalidOperationException();
		await b.Execute();
		return b;
	}

	protected async Task<Content?> RenderSection(string name)
	{
		var b = body ?? throw new InvalidOperationException();

		await b.Execute();

		var sec = b.GetSection(name);
		if (sec == null)
			return null;

		return await sec();
	}

	protected void Write(Content? inner)
	{
		if (inner != null)
			Write(inner.GetOutput());
	}

	[Obsolete("Did you forget to await a @RenderBody or @RenderSection call?")]
	protected void Write(Task<Content> t) => throw new InvalidOperationException("Did you forget to await a @RenderBody or @RenderSection call?");
}