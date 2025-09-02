---
title: "This Website: Optimizing Script Usage"
series: this-website
tags:
  - web
  - html
  - C#
  - Razor
---
One of the perks of making your own stuff is that some days you get to make it do neat things. Yesterday was such a day. I've long been annoyed at having _every_ page linking in _every_ possible script ([highlight.js](https://highlightjs.org/), [MathJax](https://www.mathjax.org/), etc) _just in case_ there might be content on that page that needs it, but now that I control my site generator fully it was _easy_ to go in and fix that.

My goals were simple:
* No extra scripts on pages that don't need them.
* No going through annotating every page with a list of features that it needs.

Here's what I made.

# Implementation

This has a few parts:
* Getting the layout template to omit scripts the content doesn't need.
* Figuring out what it even is that the content _does_ need.
* Organizing things such that the core generator code doesn't need deep knowledge of each referenced resource.

## Switching stuff on and off in `_layout.cshtml`

This is the easy part. If you look at the top of [`_index.cshtml`](https://github.com/pdjonov/vec3.net/blob/a153eef9b818bf501a2fe8debb0197e0475328dd/Content/_layout.cshtml) you'll see stuff similar to this:

```cshtml
@{
	var features = Site.GetPageFeatures(await GetBodyDom());
}

<!DOCTYPE html>

<head>
	@* mathjax *@
	@if (features.Contains("math"))
	{
		<script>
		MathJax = /* snip */;
		</script>
		<script id="MathJax-script" async src="https://cdn.jsdelivr.net/npm/mathjax@4.0.0/tex-mml-chtml.js"></script>
	}

	@* highlightjs *@
	@if (features.Contains("hljs"))
	{
		<link rel="stylesheet" href="/assets/hljs/hljs.css">
		<script src="https://cdnjs.cloudflare.com/ajax/libs/highlight.js/10.1.2/highlight.min.js"></script>

		if (features.Contains("language-hlsl"))
		{
			<script src="/assets/hljs/hlsl.js"></script>
		}
		if (features.Contains("language-slang"))
		{
			<script src="/assets/hljs/slang.js"></script>
		}
	}

	@* canvas *@
	@if (features.Contains("vec3.sketch"))
	{
		<script src="/assets/sketch.js"></script>
	}
```

Very straightforward. Does exactly what it looks like it does. The only question is where `features` comes from and what magic it contains.

## Figuring out _what_ to switch off (or on)

```cshtml
var features = Site.GetPageFeatures(await GetBodyDom());
```

Looking at the call to `Site.GetPageFeatures` it's immediately clear that we're going to be doing something with the body content's HTML, represented as a [DOM tree](https://developer.mozilla.org/en-US/docs/Web/API/Document_Object_Model/Introduction). (I mean, _what else_ could `GetBodyDom` be doing?)

Well, let's start with [`GetBodyDom`](https://github.com/pdjonov/vec3.net/blob/a153eef9b818bf501a2fe8debb0197e0475328dd/SiteGen/Generator/RazorTemplate.cs#L343):

```csharp
protected async Task<IElement> GetBodyDom()
{
	var body = await RenderBody();

	var parser = new HtmlParser();

	var doc = await parser.ParseDocumentAsync(body?.Content ?? "");
	return doc.Body ?? doc.CreateElement("body");
}
```

`GetBodyDom` is a method in the `RazorLayout` class which is the base class of all the compiled `_layout.cshtml` templates, and that's how the template gets access to it. It doesn't do a lot. It awaits `RenderBody` which deals with whatever (lazy) processing needs to be done to make the page content available, and then returns it. Once it has the body HTML it throws it at [AngleSharp](https://anglesharp.github.io/).

This was an easy thing to add. The deferred rendering infrastructure already existed because, well, the whole rest of the site generator needs it. And there was already an AngleSharp reference in the project since it's used for HTML minification.

## Going from DOM to feature set

[This bit](https://github.com/pdjonov/vec3.net/blob/a153eef9b818bf501a2fe8debb0197e0475328dd/Content/Site.cs#L48) is also straightforward:

```csharp
public static HashSet<string> GetPageFeatures(IElement pageBody)
{
	var ret = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	foreach (var child in pageBody.GetDescendants())
	{
		switch (child)
		{
		case IElement code when code.TagNameIs("code"):
			foreach (var c in code.ClassList)
				if (c.StartsWith("language-", StringComparison.OrdinalIgnoreCase))
					if (ret.Add(c))
						ret.Add("hljs");
			break;

		case IElement math when math.TagNameIs("span", "div") && math.HasClass("math"):
			ret.Add("math");
			break;

		case IHtmlScriptElement script when script.IsJavaScript():
			if ((script.Text ?? "").Contains("sketch.load"))
				ret.Add("vec3.sketch");
			break;
		}
	}

	return ret;
}
```

There's nothing special in these rules. The code just scans all the tags, checking for patterns that indicate the need for a plugin. In detail, the rules are:

* Any `<code>` element with a `language-*` class is going to be processed by highlight.js. Each of these generate a `"hljs"` feature entry _and_ another entry for the specific language (in case it's one of the ones I wrote a custom ruleset for).
* Any `<span>` or `<div>` element with the `math` class is for MathJax. The associated feature is `"math"`.
* Any script that contains the text `sketch.load` is _almost certainly_ referencing my interactive diagramming library. And the feature is `"vec3.sketch"`.

The `HashSet` takes care of deduplicating the feature list.

## A few utility functions

Finally, helper functions like `TagNameIs` and `IsJavaScript` are in [`Helpers.cs`](https://github.com/pdjonov/vec3.net/blob/a153eef9b818bf501a2fe8debb0197e0475328dd/SiteGen/Generator/Helpers.cs#L268). I won't list them all (you can read the code if you're interested), but I'll just show one as an example:

```csharp
public static bool TagNameIs(this IElement element, string tagName)
{
	ArgumentNullException.ThrowIfNull(element);
	ArgumentNullException.ThrowIfNull(tagName);

	return string.Equals(element.TagName, tagName, StringComparison.OrdinalIgnoreCase);
}
```

## Organizing things

The third goal was maintaining some semblance of a reasonable separation of concerns between the generator and the site's content. (And layout defines the theme and manages the site's dependencies, so it's _content_.)

Here's how things are structured now:

`_index.cshtml` knows how to glue the dependencies to the final output, so it's responsible for handling the feature switches.

`GetPageFeatures` has to know some things about how all the dependencies are implemented. And at least one of those dependencies is code that I wrote which lives in the `Content` folder, so... that's a content-side responsibility. The `Site` class is in the `Content/Site.cs` file (which goes into `site.dll` which all the Razor templates get a reference to).

Stuff like `TagNameIs` doesn't know anything about any particular site feature or dependency. It's just syntactic convenience for working with the DOM. It goes in the generator project. (I really need to fish these out of `Helpers.cs`, which may as well be named `Miscellany.cs`.)

And, as I already mentioned, `GetBodyDom` lives in the base class where it's available to all layout templates.