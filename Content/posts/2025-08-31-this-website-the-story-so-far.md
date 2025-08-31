---
title: "This Website: The Story So Far"
time: 11:00
series: this-website
---

This site's source is now [available on GitHub](https://github.com/pdjonov/vec3.net/) and the generator component is even open-source. To mark the occasion, I thought I'd write a bit about its history and the journey from WordPress to a fully static site.

If you don't care about the site's history, you can skip this and just read about the [current implementation](/posts/this-website-the-story-so-far) instead.

## The WordPress era

Back in 2009, for reasons I can no longer recall, I set up a little website for myself in [WordPress](https://wordpress.com/). It wasn't much, mostly just a blog and a place to dump bits of information for easy future reference. I had absolutely no idea what I was doing back then as far as web tech goes, so I just grabbed some plugins off the WP marketplace and typed prose into its built in page editor.

That got me farther than I had expected. I found [highlight.js](https://highlightjs.org/) and [MathJax](https://www.mathjax.org/) plugins early on, which made for nice code snippets (shame about my old code style) and beautiful math. Eventually, I even hooked up [Processing.js](https://en.wikipedia.org/wiki/Processing#Processing.js) to make some neat [interactive](/posts/gjk#three-points) [diagrams](/posts/implementing-gjk#when-n1) to help explain the [GJK](https://en.wikipedia.org/wiki/Gilbert%E2%80%93Johnson%E2%80%93Keerthi_distance_algorithm) algorithm.

But it wasn't all roses.

The earliest snag was my hosting provider getting hacked and my site being vandalized. And yes, I mean _the server_ and not just my WP admin console - some of the damage was in folders _I_ didn't have write access to. Well, I _did_ have backups, so that was at least recoverable.

At some point spam bots also found my site and started throwing garbage at the blog comments. At first it was just a few and I delt with them by hand. Then I tried requiring manual review before comments became visible, thinking that the bots would learn (yeah, lol) to direct their efforts elsewhere. After that got too annoying, I grabbed a spam-blocking plugin and set it up to filter aggressively.

Some time in 2013 (I think), the servers got hacked _again_. This time the damage was much worse, but no big deal, I'd been through this before and I still had current backups. Sadly, though I had backed up _my_ content, but I hadn't backed up all of its dependencies. Some of the plugins I relied on had broken or just fallen off the marketplace. The Processing.js plugin was among them. Processing.js itself was still a real thing at the time, but I didn't have the skill to rebuild the WP integration, and I was too busy flirting with burnout on another project to sit down and just learn PHP and the WP API and... So my nice interactive diagrams stopped rendering.

Another lost plugin was my comment moderator. The free version of the plugin was now gone and rather than pay an annual subscriptioni to filter 99% spam comments on my small, low-traffic site, I just turned off all comments. Sad, but from an objective point of view, vanishingly little of value was lost. And people who _really_ have things to say to me about my site would (and occasionally still do) just reach out via email.

The final blow came a year after that when the hosting company decided they were done with my old low-cost (to them: low-value) subscription. As it was, I was a _very_ low-traffic site on a very cheap plan. But WordPress (or maybe I should blame PHP) loves to burn up CPU cycles, _especially_ when some random bot starts aggressively scraping the site. It wasn't a _huge_ spike in resource usage, but that one weekend was enough to put me on notice that I had to either pay way more or leave.

So I left.

After that, the site stayed dead for a year or two.

## Recovering

So, I had no host and no site, but I _did_ still have my content. I can't recall in any detail _how_ I did this, but I recovered my content from my last backup.

This was an awkward process.

The first step was easy: open up the WP database and pull out all the page content. Next, I found a WordPress to Markdown converter somewhere online and I ran everything through it. It, naturally, made a mess, so then I went through and fixed everything by hand (greatly aided by regex-powered global find/replace, of course).

So at this point I had a bunch of HTML files sitting on my hard drive and no idea how to host them anywhere.

Oh and my interactive diagrams were not only unrendered, but their source somehow got stripped from the Markdown. (The ones you see on the site today were recreated in a different language from dim memories.)

## Moving to Azure

Don't remember when I signed up for Azure. I think this was not long before I started at The Coalition, so maybe 2015 or so. One of the first things I did was try to get the site back up.

Well, a storage bucket, a DNS zone, and a CDN in front of it turns out to be pretty cheap and not all _that_ hard to configure. It made a for a good introduction to the platform and it worked.

I even got a certificate from [Let's Encrypt](https://letsencrypt.org/) and figured out how to make an Azure Functions app to periodically refresh it. The automatic schedule for the function app didn't work and I had to poke it by hand every month to keep things running. And of course I forgot to do that a lot, so for several years the site was available, but visitors (_both_ of them!) had to click through "expired certificate" warnings.

(Eventually, the schedule started to work on its own. I guess the Azure team finally got around to whatever the bug had been.)

But at this stage, the site was still terribly broken. I didn't have good (any?) index pages. A lot of the links between pages were broken. The styling was _horrific_. But it was _online_ and I'd decided that static pages are great and that I should stick with them.

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

## Future plans

So now that I have not only my content, but also a generator and templates that I can understand and fix and generally bend to my will, what's left?

Well, for one thing the site could still be _prettier_. So that's on my ToDo list. Part of this will require me learning how to make some actual art. For example, that stylized image of my face on the front page is _terrible_ with a light theme (just be glad you didn't have to see it when it was _abominable_ before that).

Speaking of themes: CSS is neat. Variables weren't a thing when I wrote my first web pages, but things are _soooo_ much easier now that they exist. And with the pwoer of `@media(prefers-color-scheme: light)`, the site even adjusts itself to match your device's preferred theme settings (which means _I_ can now read it when I'm working on it outside - that was a big motivation). I need to spend some more time in the stylesheets, maybe there are other new cool things I can learn.

The MathJax integration is also something that could use improvement. I'm not sure I'm the right guy to jump in and start making pull requests, but it's _very_ frustrating that its (actually really cool) accessibility features _thoroughly resist_ any attempt to style them nicely. That might be a conscious choice from the developers to try to _force_ proper contrast onto the MathJax UI, but the default accessibility highlights make everything _much, much less_ readable against the site's (default) dark theme.

I should probably also start turning off components when I don't need them. I've got _two_ posts so far that use the interactive diagramming feature, that script doesn't need to load and parse in all the other pages. MathJax could probably also be turned off in a lot of pages (though it'll be tricky to correctly enable it in index pages if a _blurb_ contains formulas).

And finally, speaking of Azure, it looks like Microsoft is going to boot me off of Azure before long. The pricing changes they've announced for Azure's CDN services are frankly absurd for something as tiny and low-traffic as this site. Adding a $35/month _base_ fee to a bill that's yet to break $5 in any month is just not reasonable. (That also means I need to figure out how to get my certificate-refresh bot _off_ of Azure, and it's currently quite tightly integrated with their services...)