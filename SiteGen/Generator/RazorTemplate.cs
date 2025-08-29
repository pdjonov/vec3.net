using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Vec3.Site.Generator;

public abstract class RazorTemplate : HtmlContentItem
{
	protected RazorTemplate(InputFile origin) : base(origin) { }

	protected TextWriter Writer => writer ?? throw new InvalidOperationException();
	private StringWriter? writer;

	protected void Write(object obj) => Writer.Write(obj);
	protected void WriteLiteral(string literal) => Writer.Write(literal);

	protected abstract Task ExecuteTemplate();
	private void BeginGeneratingContent()
	{
		if (writer != null)
			throw new InvalidOperationException();

		writer = new();
	}
	private void CancelGeneratingContent()
	{
		if (writer == null)
			throw new InvalidOperationException();

		writer = null;
	}
	private string EndGeneratingContent()
	{
		if (writer == null)
			throw new InvalidOperationException();

		var ret = writer.ToString();
		writer = null;
		return ret;
	}

	protected override async Task<string> CoreGenerateContent()
	{
		BeginGeneratingContent();
		try
		{
			await ExecuteTemplate();
		}
		catch
		{
			CancelGeneratingContent();
			throw;
		}
		return EndGeneratingContent();
	}

	protected void DefineSection(string name, Func<Task> body) => sections.Add(name, body);
	private readonly Dictionary<string, Func<Task>> sections = new(StringComparer.OrdinalIgnoreCase);

	public virtual Func<Task<string>>? GetSection(string name, bool isRequired = true)
	{
		ThrowIfNotPrepared();

		if (!sections.TryGetValue(name, out var sectionFunc))
		{
			if (isRequired)
				throw new KeyNotFoundException($"Required section '{name}' is not defined.");
			return null;
		}

		return async () =>
		{
			BeginGeneratingContent();
			try
			{
				await sectionFunc();
			}
			catch
			{
				CancelGeneratingContent();
				throw;
			}
			return EndGeneratingContent();
		};
	}
}

public abstract class RazorPage : RazorTemplate
{
	protected RazorPage(InputFile origin) : base(origin) { }

	protected override Task CoreInitialize()
	{
		var fileName = Path.GetFileNameWithoutExtension(Origin.ContentRelativePath);
		OutputPaths = [Path.ChangeExtension(Origin.ContentRelativePath, fileName == "index" ? ".html" : "")];

		return Task.CompletedTask;
	}
}

public abstract class RazorLayout : RazorTemplate
{
	protected RazorLayout(InputFile origin) : base(origin) { }

	public IHtmlContent? Body { get; set; }

	public override Func<Task<string>>? GetSection(string name, bool isRequired = true)
	{
		var ret = base.GetSection(name, isRequired: false);
		if (ret == null && Body is RazorTemplate razorBody)
			ret = razorBody.GetSection(name, isRequired: false);

		if (ret == null && isRequired)
			throw new KeyNotFoundException($"Required section '{name}' is not defined.");

		return ret;
	}

	protected override Task CoreWriteContent(Stream outStream, string outputPath)
		=> throw new NotSupportedException();

	protected Task<string> RenderSection(string name, bool required = true)
	{
		if (Body is not RazorTemplate body)
		{
			if (required)
				throw new KeyNotFoundException($"Required section '{name}' is not defined and cannot be because the body is not a Razor page.");

			return Task.FromResult("");
		}

		var section = body.GetSection(name, required);
		if (section == null)
		{
			Debug.Assert(!required);
			return Task.FromResult("");
		}

		return section();
	}

	protected Task<string> RenderBody() => Task.FromResult(Body?.Content ?? "");

	[Obsolete("Did you forget to await a RenderBody or RenderSection expression?", error:true)]
	protected void Write(Task<string> value) => throw new NotSupportedException();
}

public abstract class RazorPartial : RazorTemplate
{
	protected RazorPartial(InputFile origin) : base(origin) { }

	protected override Task CoreWriteContent(Stream outStream, string outputPath)
		=> throw new NotSupportedException();
}