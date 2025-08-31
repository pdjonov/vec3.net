---
title: "This Website: The Story So Far"
series: this-website
---

This post marks the move to my (own) new static site generator project, which is open-source and [available on GitHub](https://github.com/pdjonov/vec3.net/). Now that I've fixed all (haha, _all_) the bugs in this website, I'm writing a bit about where it came from and where it's going.

## The WordPress era

Back in 2009, for reasons I can no longer recall, I set up a little website for myself in [WordPress](https://wordpress.com/). It wasn't much, mostly just a blog and a place to dump bits of information for easy future reference. I had absolutely no idea what I was doing back then as far as web tech goes, so it was very much just "grab some plugins off the WP marketplace" and "type content in the online editor" and go.

That got me surprisingly far. I found [MathJax](https://www.mathjax.org/) early on, which made for beautiful math. [highlight.js](https://highlightjs.org/) made for pretty code snippets. And eventually I even used [Processing.js](https://en.wikipedia.org/wiki/Processing#Processing.js) to make some neat [interactive](/posts/gjk#three-points) [diagrams](/posts/implementing-gjk#when-n1) to help explain the [GJK](https://en.wikipedia.org/wiki/Gilbert%E2%80%93Johnson%E2%80%93Keerthi_distance_algorithm) algorithm.

But alas, it was not to last. The earliest snag was my hosting provider getting hacked and my site being vandalized. Well, I _did_ have backups, so that was recoverable.

And then the servers got hacked _again_. This time the damage was much worse, but no big deal, I still had current backups - or so I thought. Foolishly, I had backed up _my_ content, but I hadn't backed up all of its dependencies. Some of the plugins I relied on had broken or been pulled from the marketplace, and the Processing.js plugin was among them. Processing.js itself was still a viable product, but I didn't have the skill to rebuild the WP integration. And I also didn't have the time to learn. So my nice interactive diagrams broke and disappeared (or rather, without the plugin, the raw Processing source just got dumped into the HTML).

The final blow came a year later: the hosting company wanted me _gone_. As it was, I was a _very_ low-traffic site on an even cheaper plan. But WordPress (or maybe I should blame PHP) loves to burn up CPU cycles, _especially_ when some random bot starts aggressively scraping the site. It wasn't a _huge_ spike in resource usage (it didn't last even a day according to my host's logs). But it was big enough that I was told to accept either a large increase in my hosting costs or to leave.

So I left that host.

After that, the site stayed dead for a year or two.

## Recovering

So, I had no host and no site, but I _did_ still have my content. I can't recall in any detail _how_ I did this, but I recovered my content from my last backup.

This was an awkward process.

The first step was easy: open up the WP database and pull out all the page content. The next step... I found a WordPress to Markdown converter somewhere online and I ran everything through it. And it made a mess, so then I ran everything through a big heap of search-and-replace regexes which, somehow, actually worked. And then I reviewed and fixed the rest by hand.

So at this point I had a mass of HTML files sitting on my hard drive and not much to do with them.

Oh and my interactive diagrams were not only dead, but their source even got stripped from the Markdown. (The ones you see on the site today were recreated in a different language from dim memories.)

## Moving to Azure

Don't remember when I signed up for Azure. I think this was around the time I started at The Coalition, so maybe 2015 or so. One of the first things I did was try to get the site back up.

Well, a storage bucket and a CDN in front of it turns out to be pretty cheap and not _that_ hard to setup, even for someone who's never worked with the web or really dug into the details of how cloud hosting works.

I even got a certificate from [Let's Encrypt](https://letsencrypt.org/) and figured out how to make an Azure Functions app to periodically refresh it. Naturally, automatic schedule for the function app didn't work and I had to poke it by hand every other month. And naturally I forgot to do that a lot, so for much of the following period the site was available, but visitors (_both_ of them!) had to click through "expired certificate" warnings.

(Eventually, the schedule started to work on its own, which confirmed my suspicion that it had been an Azure bug the entire time.)

But at this stage, the site was still terribly broken. I didn't have good (any?) index pages. A lot of the links between pages were broken. The styling was _horrific_. But I had firmly moved to a _static_ content model.

## Statiq

Eventually, I stumbled across the concept of static site generators. (And if I hadn't found it, I'd have "invented" it myself.) Since my work at The Coalition had already familiarlized me with ASP.NET, I wanted something familiar, and [Statiq](https://www.statiq.dev/) was it!

The only downside with Statiq was the fact that it did _way_ more than I needed. I was constantly tripping over weird edge cases and then having to wade through lots of documentation (not all of it up to date, as happens with prerelease projects) to figure out what I'd done wrong and how to fix it.

Statiq also isn't exactly free. The _core_ packages are open source, but all the nice extras (which probably make it much nicer to use) are not. This wasn't a dealbreaker, but it was a point of hesitation every time I hit a snag and thought "I should just fork the project and fix this". So I never did fix those snags...

But Statiq still got me to the point where _most_ of the links worked.

## Starting to fix content

With the site _mostly_ functioning, I started thinking about making it look nice.

A basic theme came together pretty quickly. And then it got ripped up and rebuilt as I got back up to speed with CSS. (It had been a while since I had worked on web-based UIs, so there was a lot to recall.) I even figured out how to build a responsive layout that works on large monitors and small phones alike. (Not very groundbreaking, I know. But I wasn't working with a toolset where I could just grab a prebuilt theme, and so I had to actually learn the basics for myself.)

This is also the point where I _finally_ bit the bullet and rewrote my old Processing.js diagrams in raw JavaScript. This wasn't so much _difficult_ as it was tedious. Processing.js and the associated WordPress plugin had done a lot of heavy lifting setting up canvas elements, dealing with DPI/scaling issues, dealing with pointer interactions...

Still, I'm more or less happy with the final result. You can see it live on the [GJK](/posts/gjk#three-points) [posts](/posts/implementing-gjk#when-n1) (also linked above). There are a few rendering artifacts (especially visible with a light theme), but they're not world-ending by any means. And if you're curious, the JavaScript code is, at least _for now_, unminified and fairly easy to read.

## Writing my own!

What I wanted was to turn a bunch of Markdown into a bunch of HTML, linking the pages together and formatting them nicely with some Razor templates, and to do it _simply_.

So I wrote my own generator.

All together it took maybe a week's worth of effort. The hardest part was wrangling Razor since the core libraries are not terribly well documented (or the documentation is terribly well hidden).

Once that was on its way, the next-hardest bit was wrangling the C# compiler. But this isn't the first time I've worked with Roslyn, so it went pretty quick.

And the last problem worth note was figuring out how to handle dependencies among all my pages - particularly the generated ones. Statiq had some automagical thing going, but I wound up winging it with some hardcoded processing phases and a lot of lazy/deferred evaluation.

## Making it nicer

So now that I have not only my content, but also a generator and templates that I can understand and fix and generally bend to my will, what's left?

Well, for one thing the site could still be _prettier_. So that's on my ToDo list. (Part of this will require me learning how to make some actual art. That stylized image of my face on the front page is _terrible_ with a light theme - and you should've seen it when it was _abominable_ before that.)

Speaking of themes: CSS is neat. Variables weren't a thing when I wrote my first web pages, but things are _soooo_ much easier now that they exist. And with the pwoer of `@media(prefers-color-scheme: light)`, the site even adjusts itself to match your device's preferred theme settings (which means _I_ can now read it when I'm working on it outside - that was a big motivation).

The MathJax integration is also something that could use improvement. I'm not sure I'm the right guy to jump in and start making pull requests, but it's _very_ frustrating that its (really cool, BTW) accessibility features _thoroughly resist_ any attempt to style them nicely. And that might be a conscious choice from the developers to try to _force_ proper contrast onto the MathJax UI, but the default accessibility highlights made everything _much, much less_ readable against the site's (default) dark theme.

I should probably also start turning off components when I don't need them. I've got _two_ posts so far that use the interactive diagramming feature, that script doesn't need to load and parse in all the other pages. MathJax could probably also be turned off in a lot of pages (though it'll be tricky to correctly enable it in index pages if a _blurb_ contains formulas).