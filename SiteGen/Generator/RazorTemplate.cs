using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

namespace Vec3.Site.Generator;

public abstract class RazorTemplate : HtmlContentItem
{
	protected RazorTemplate(InputFile origin) : base(origin) { }

	protected TextWriter Writer => writer ?? throw new InvalidOperationException();
	private StringWriter? writer;

	protected void Write(object obj)
	{
		if (obj == null)
			return;

		if (obj is IHtmlLiteral literal)
		{
			WriteLiteral(literal.Content);
			return;
		}

		var str = obj.ToString();
		if (str == null)
			return;

		str = HtmlEncoder.Default.Encode(str);

		Writer.Write(str);
	}
	protected void Write(HtmlLiteral literal)
	{
		if (literal.IsNullOrEmpty)
			return;

		WriteLiteral(literal.Content);
	}
	protected void WriteLiteral(string literal) => Writer.Write(literal);

	private string? currentAttrSuffix;
	protected void BeginWriteAttribute(string name,
		string prefix, int prefixOffset,
		string suffix, int suffixOffset,
		int attributeValuesCount)
	{
		Debug.Assert(currentAttrSuffix == null);
		currentAttrSuffix = suffix;

		WriteLiteral(prefix);
	}

	protected void WriteAttributeValue(string prefix, int prefixOffset,
		object value,
		int valueOffset, int valueLength,
		bool isLiteral)
	{
		WriteLiteral(prefix);
		if (isLiteral && value is string str)
			WriteLiteral(str);
		else
			Write(value);
	}

	protected void EndWriteAttribute()
	{
		Debug.Assert(currentAttrSuffix != null);

		WriteLiteral(currentAttrSuffix);
		currentAttrSuffix = null;
	}

	[Obsolete("Did you forget to await an expression?", error: true)]
	protected void Write(Task value) => throw new NotSupportedException();

	protected virtual Task InitializeTemplate() => Task.CompletedTask;
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
	private HtmlLiteral EndGeneratingContent()
	{
		if (writer == null)
			throw new InvalidOperationException();

		var ret = writer.ToString();
		writer = null;
		return HtmlLiteral.Create(ret);
	}

	protected override Task CoreInitialize() => InitializeTemplate();
	protected override async Task<HtmlLiteral> CoreGenerateContent()
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

	public virtual Func<Task<HtmlLiteral>>? GetSection(string name, bool isRequired = true)
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

	protected HtmlLiteral RenderRaw(string? html) => HtmlLiteral.Create(html);
	protected HtmlLiteral RenderRaw(HtmlLiteral html) => html;

	protected Task<HtmlLiteral> RenderMarkdown(string? markdown)
		=> Project.RenderMarkdown(markdown);

	protected Task<IHtmlLiteral> RenderPartial(string path, object? model = null)
	{
		return Path.GetExtension(path) switch
		{
			".md" => RenderMarkdownPartial(),
			".cshtml" => RenderRazorPartial(),
			_ => throw new NotSupportedException(),
		};

		async Task<IHtmlLiteral> RenderMarkdownPartial()
		{
			var source = await LoadText(path);
			var ret = await Project.RenderMarkdown(source);
			Debug.Assert(!ret.IsNull);
			return ret;
		}

		async Task<IHtmlLiteral> RenderRazorPartial()
		{
			var inFile = TryGetInputFile(path);
			if (inFile == null)
				throw new FileNotFoundException(message: "The partial source could not be found.", fileName: path);

			var part = await Project.GetRazorPartial(inFile, model);

			await part.Initialize();
			await part.PrepareContent();

			return part;
		}
	}

	protected string UrlTo(ContentItem page)
	{
		ArgumentNullException.ThrowIfNull(page);
		if (page.OutputPath is null)
			throw new ArgumentException(paramName: nameof(page), message: "Can't form a link to content which isn't part of the output.");

		return page.OutputPath;
	}
}

public abstract class RazorPage : RazorTemplate
{
	protected RazorPage(InputFile origin) : base(origin) { }

	protected override Task CoreInitialize()
	{
		var fileName = Path.GetFileNameWithoutExtension(Origin.ContentRelativePath);
		OutputPath = Path.ChangeExtension(Origin.ContentRelativePath, fileName == "index" ? ".html" : null);

		return InitializeTemplate();
	}
}

public abstract class EnumeratedRazorPage : RazorPage, IEnumeratedContent
{
	protected EnumeratedRazorPage(InputFile origin) : base(origin) { }

	protected EnumeratedRazorPage(EnumeratedTemplateInstance origin, Func<object, string> outputPath) : base(origin)
	{
		ArgumentNullException.ThrowIfNull(outputPath);

		this.getOutputPathForItem = outputPath;
	}

	protected abstract ContentItem CreateInstance(EnumeratedTemplateInstance origin, Func<object, string> outputPath);
	ContentItem IEnumeratedContent.CreateInstance(object item)
	{
		Debug.Assert(getOutputPathForItem != null);
		return CreateInstance(new(Origin, item), getOutputPathForItem);
	}

	protected override Task CoreInitialize()
	{
		string? outputPath;
		if (IsEnumeratorInstance)
		{
			outputPath = null;
		}
		else
		{
			Debug.Assert(getOutputPathForItem != null);

			outputPath = getOutputPathForItem(Item);
			getOutputPathForItem = null;

			outputPath = ResolveRelativePath(outputPath);
		}
		OutputPath = outputPath;

		return InitializeTemplate();
	}

	public bool IsEnumeratorInstance => Origin is not EnumeratedTemplateInstance;
	protected object Item => (Origin as EnumeratedTemplateInstance)?.Item ?? throw new InvalidOperationException("Item cannot be accessed on the enumerator instance.");

	private IEnumerable<object>? items;
	IEnumerable<object>? IEnumeratedContent.Enumerator => items;

	private Func<object, string>? getOutputPathForItem;

	protected void InitializeEnumerator<T>(IEnumerable<T> Items, Func<T, string> OutputPath)
	{
		ArgumentNullException.ThrowIfNull(Items);
		ArgumentNullException.ThrowIfNull(OutputPath);

		ThrowIfNotInitializing();

		Debug.Assert(IsEnumeratorInstance);
		Debug.Assert(items == null);

		this.items = Items.Cast<object>();
		this.getOutputPathForItem = it => OutputPath((T)it);
	}
}

public abstract class RazorLayout : RazorTemplate
{
	protected RazorLayout(InputFile origin) : base(origin) { }

	public IHtmlContent? Body { get; set; }
	public IHtmlContent? InnermostBody
	{
		get
		{
			var ret = (IHtmlContent)this;
			while (ret is RazorLayout layout)
				ret = layout.Body;
			return ret;
		}
	}

	public override Func<Task<HtmlLiteral>>? GetSection(string name, bool isRequired = true)
	{
		var ret = base.GetSection(name, isRequired: false);
		if (ret == null && Body is RazorTemplate razorBody)
			ret = razorBody.GetSection(name, isRequired: false);

		if (ret == null && isRequired)
			throw new KeyNotFoundException($"Required section '{name}' is not defined.");

		return ret;
	}

	protected Task<HtmlLiteral> RenderSection(string name, bool required = true)
	{
		if (Body is not RazorTemplate body)
		{
			if (required)
				throw new KeyNotFoundException($"Required section '{name}' is not defined and cannot be because the body is not a Razor page.");

			return Task.FromResult(HtmlLiteral.Empty);
		}

		var section = body.GetSection(name, required);
		if (section == null)
		{
			Debug.Assert(!required);
			return Task.FromResult(HtmlLiteral.Empty);
		}

		return section();
	}

	protected async Task<IHtmlLiteral> RenderBody()
	{
		var body = Body;

		if (body == null)
			return HtmlLiteral.Empty;

		if (body is ContentItem item)
			await item.PrepareContent();

		return body;
	}

	protected string? GetTitle()
	{
		return Body switch
		{
			IPage page => page.Title,
			RazorLayout inner => inner.GetTitle(),
			_ => null,
		};
	}

	protected override Task CoreWriteContent(Stream outStream, string outputPath)
		=> throw new NotSupportedException();
}

public abstract class RazorPartial : RazorTemplate
{
	protected RazorPartial(InputFile origin, object? model)
		: base(origin)
	{
		Model = model;
	}

	public object? Model { get; }

	protected override Task<HtmlLiteral> GenerateBlurb(HtmlLiteral content) => Task.FromResult(HtmlLiteral.Empty);

	protected override Task CoreWriteContent(Stream outStream, string outputPath)
		=> throw new NotSupportedException();
}