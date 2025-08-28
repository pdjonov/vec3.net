using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Vec3.Site.Generator;

public abstract class RazorTemplate : FileContentItem
{
	protected RazorTemplate(InputFile origin) : base(origin) { }

	protected TextWriter Writer => writer ?? throw new InvalidOperationException();
	private StringWriter? writer;

	protected void Write(object obj) => Writer.Write(obj);
	protected void WriteLiteral(string literal) => Writer.Write(literal);

	protected abstract Task GenerateContent();
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

	private string? bodyText;

	protected override async Task CorePrepareContent()
	{
		BeginGeneratingContent();
		try
		{
			await GenerateContent();
		}
		catch
		{
			CancelGeneratingContent();
			throw;
		}
		bodyText = EndGeneratingContent();
	}

	protected void DefineSection(string name, Func<Task> body) => sections.Add(name, body);
	private readonly Dictionary<string, Func<Task>> sections = new(StringComparer.OrdinalIgnoreCase);

	public Func<Task<string>>? RenderSection(string name, bool isRequired = true)
	{
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

	protected override async Task CoreWriteContent(Stream outStream, string outputPath)
	{
		Debug.Assert(bodyText != null);

		var writer = new StreamWriter(outStream);
		await writer.WriteAsync(bodyText);
		await writer.FlushAsync();
	}
}

public abstract class RazorPage : RazorTemplate
{
	protected RazorPage(InputFile origin) : base(origin) { }

	protected override Task CoreInitialize()
	{
		OutputPaths = [Path.ChangeExtension(Origin.ContentRelativePath, ".html")];

		return Task.CompletedTask;
	}
}

public abstract class RazorLayout : RazorTemplate
{
	protected RazorLayout(InputFile origin) : base(origin) { }
}

public abstract class RazorPartial : RazorTemplate
{
	protected RazorPartial(InputFile origin) : base(origin) { }

	protected override Task CoreWriteContent(Stream outStream, string outputPath)
		=> throw new NotSupportedException();
}