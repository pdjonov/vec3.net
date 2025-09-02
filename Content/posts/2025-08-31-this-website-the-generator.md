---
title: "This Website: The Generator"
time: 13:00
series: this-website
seriestitle: "The Making of This Website"
tags:
  - web
  - C#
  - Razor
---
All of the code I'm going to discuss here is [available on GitHub](https://github.com/pdjonov/vec3.net/). Links to specific bits of code will point at what is _currently_ the `HEAD` commit - things will naturally move and change as time passes.

This post is a high level overview of the project's implementation. More detailed discussions of things like Razor customizations or the blurb extractor will follow.

# Why though?

So, why did I write my own site generator instead of just using an off-the-shelf solution like [Hugo](https://gohugo.io/)? There are a few reasons:

The first is _control_. If you read about this site's [history](/posts/this-website-the-story-so-far), you'll see that I've been burned a few times by taking dependencies on things that get abandoned, broken, monetized, so on. I don't ever want to have to start from scratch.

The second is _familiarity_. I already _know_ Razor and I like it, so I wanted to use it to define my theme and layout logic.

Also, I have simple needs. I don't need some big thing with loads and loads of bells and whistles that I have to wade through when trying to find answers to problems, I want just a few thousand lines of code that I can confidently edit on my own.

And finally, it was kinda fun.

## Features

Here's the feature set I settled on:

* Markdown content.
* Yaml front matter. (Actually _not_ my favorite thing in the world, but everybody else is doing it so it's just what's easy.)
* Plain old C# to define site structure.
* Razor templating logic for structure and theming. (`_layout.cshtml` _let's gooo!_)
* Razor (and even Markdown) partials.
* Automatic page generation which _isn't_ tied to arcane dependency management.
* It needs to run _fast_.

# Implementation

So, how is it built?

Well, let's trace the execution stages:

## Finding all the content

### Enumerating the source directory

The first thing I do is [scan](https://github.com/pdjonov/vec3.net/blob/69f45beda6f30a00a35f77dbdf644619a99a4468/SiteGen/Generator/Project.cs#L68) the source directory. This just builds a list of input files, prepresented as `ContentItem` objects.

_Some_ of the input files aren't _actual_ content and they're ultimately all going to be removed from the input item set.

### Compiling the `site` assembly

The first of these are the `.cs` files which make up the `site` assembly, which is automatically referenced in all of the Razor templates. They're [pulled](https://github.com/pdjonov/vec3.net/blob/69f45beda6f30a00a35f77dbdf644619a99a4468/SiteGen/Generator/Project.cs#L107) out of the input set and compiled _first_.

### Compiling the Razor _pages_

The next thing to do is compile the Razor _pages_. The initial scan tracks these as dummy objects since the full `RazorPage` class can't initialize itself properly without the `site` assembly.

### Handling `IEnumeratedContent`

The last part of this process involves _enumerated_ content. These are Razor templates that produce _multiple_ output pages based on some sort of Linq query, like the one in my [`tags.cshtml`](https://github.com/pdjonov/vec3.net/blob/69f45beda6f30a00a35f77dbdf644619a99a4468/Content/posts/tags.cshtml#L3) file, which generates the per-tag post listing pages:

```cshtml
@enumerate IGrouping<string, (MarkdownPage Page, PostFrontMatter FrontMatter)> Tag
{
	Items = Project.Content.GetPosts().ByTag();
	OutputPath = t => Posts.TagPath(t.Key);
}
```

The `@enumerate` directive drives some custom Razor logic that generates the necessary code to tell the generator _what_ we're generating, how to enumerate it, and how to construct an output path for each of the generated items. The rest of the Razor template doesn't run until _after_ items are enumerated, and then it runs once per item, with the `Tag` property (as defined in the `@enumerate` directive itself - this could be named differently in different templates) set to the item.

There's a bit of an awkward dance that has to be done here in order to make everything make sense from the perspective of inter-page dependencies.

1. First, all of the enumerated templates are pulled from the iput set and set aside.
2. Then, everything which remains is _initialized_, which loads in its basic metadata (such as front matter). This is important because that metadata is likely to be criteria for the enumeration queries.
3. The initialized pages are added to the input set.
4. The enumerated templates are initialized.
5. The enumerated templates are executed, and new content items are generated for each of their outputs.

And then for some reason I thought that maybe enumerated pages might spawn other enumerated pages, so this is all in a weird loop and ...I don't really know what I was thinking here. This is a thing that's _complicated_ in all the other static site generators I looked at, and that loop is mostly a placeholder for "future multiphase/dependency logic here".

## Generating output

Once all the content has been identified, the tool just executes its custom logic and dumps the output in the `.out` folder.

This happens in two stages: `PrepareContent` and `WriteContent`.

The first, `PrepareContent` is where Markdown is turned into HTML, blurbs are extracted, Razor _page_ templates are run, and so on. It's designed to do as much processing as might be required by _other_ items. Naturally, this requires items to form an _acyclic_ dependency graph (and the tool doesn't do anything special if they don't - it just deadlocks).

`PrepareContent` is basically the scaffolding that the blurb-extraction logic (which drives the little extracts you can see in the post listing) rests on.

`WriteContent` actually writes the prepared output to disk. I'm a bit inconsistent on where, exactly, I apply layouts and run minification and so on. That'll probably get cleaned up eventually.

Finally, leftover files from previous runs which no longer belong in `.out` are deleted.

## Layout and theming

In addition to the Markdown and Razor _pages_, the tool supports certain auxiliary Razor templates. The most important are the `_layout.cshtml` files.

A _page_, in essence, consists of _body text_. But we need more than that to make a web page. The rest of the scaffolding comes from the layout files. These are processed in order, starting in the page's source directory and moving up to the project root. (So,for instance, the layout file in the `posts/` directory adds the publication date and tags structure, as well as editor's notes, and then the root layout glues in all the CSS, scripts, so on.)

Layouts can also restrict themselves to only a subset of the content in their folder. For instance, [`/posts/_layout{%2A.md}.cshtml`](https://github.com/pdjonov/vec3.net/blob/69f45beda6f30a00a35f77dbdf644619a99a4468/Content/posts/_layout%7B%252A.md%7D.cshtml) has a weird thing going on in the `{%2A.md}`. If you unencode it, it reads `{*.md}`, which means it applies _only_ to the markdown files and not also to the output the post listings.