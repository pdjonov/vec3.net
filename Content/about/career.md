This is a quick synopsis of my professional career. The commercial projects I've worked on are listed below, along with a brief description of the work I did on them.

Some of these are big projects where I clearly worked with a big team. But none of these were a one-man show, and no one should take away the impression that I made any of these without the help of designers, artists, even other programmers.

# Unannounced Survival Game _(Odyssey)_
I had a great time working with the tools team on Blizard's (alas) cancelled survival game. Sadly, it wasn't meant to be and the project is now dead.

Highlights for me included DCC workflow integrations, learning a bit about how Houdini works (and how to use it as an in-editor tool), and writing a library to let us turn gnarly async code based on callbacks and lambdas into `co_routine` magic. Not much more to say since we never managed to ship.

# [Gears of War](https://gearsofwar.com): Gears of War: Ultimate Edition (2016), Gears of War 4 (2016), Gears 5 (2019)
I was happy to be a member of The Coalition's tools team. With the exception of a few (small) contributions, I didn't work on the games themselves. I worned on studio infrastructure to make everyone else's jobs easier. This included a build distribution system, community management tools, a CMS system for our online content, optimization tools, and other odds and ends.

It was an honor to be a part of these projects.

# Pop Bugs (2014)
Pop Bugs was the best little phone game ...that died because nowhere near enough folks played it. Obviously not every startup (particularly mobile startup) will succeed, but this project had a lot of heart, and it was dear to me.

I'm one of two full-time programmers who worked on Pop Bugs. I wrote our game engine from scratch (so that it's lean and mean) and I wrote all of the supporting tools we use to build the data our artists create into the finished product. I also did a _teensy_ bit of work with the gameplay and UI, mainly putting in some skeleton code which my colleague turned into all the quirky behaviors of the bugs you'll find when you launch a level and the logic underlying the interface.

Writing the tools and engine together also created many opportunities for optimization. Our artists made hundreds of megabytes of source data, yet that all got packed alongside the engine into a 50 MB APK file. And it all loaded lightning fast, too. Researching and implementing better strategies for packing, storing, and then loading our data has been one of my primary and most rewarding responsibilities here (telling our lead to not waste his time designing a loading screen that nobody would se and then actually making good on that promise was a high point for me). And while it was (and is) my preference to make the tools do the heavy lifting when it comes to optimizing art, I did also play the tech half of the role of a technical artist.

Oh and one more cool highlight. We did a PSP release of the game which required the whole thing be ported to C#. Yes, really, the whole engine, _including the Lua interpreter_. That was a fun thing to work on.

# [Baldur's Gate: Enhanced Edition](http://www.baldursgate.com/) (2012)
Baldur's Gate: Enhanced Edition is a really really cool update and re-release of a really really cool game.

One of the more interesting things we wanted to do with BG:EE is support high resolution screens and allow a degree of camera zooming. However, for various technical reasons (Baldur's Gate originally released in 1998 - draw your own inferences), we couldn't just scale up all of the _source_ art. So we ended up scaling the final rendered image, using a high(er) quality image scaling algorithm. I'm the one who made sure it didn't tank the framerate on older machines (read a bit aobut that [here](/posts/bicubic-filtering-in-fewer-taps)).

I'm also responsible for a number of enhancements throughout the game (such as fixing cursor lag). And then there was bug-squishing duty.

I'm almost solely responsible for coding the game launcher, which is responsible for downloading and updating the game, for the Beamdog release. (I'm referring to the launcher itself. Others wrote the infrastructure it connects to in order to pull down the data.)

# Sonic Office Smash (2012 ...ish?)
This was a fun little mobile toy that turns your phone into a sort of sonic stress ball.

I did much of the programming on this app, focusing mainly in the audio engine (supporting compressed sound clips to get our download size small) and on cleaning up and extending the UI code to iPad resolution.

# MDK2 HD (2012)
We took an old game and made it look a lot better (well, relatively speaking - we couldn't just remake all the art from scratch, so much of this was programmer tricks). We didn't even have all of the old source data to work with, so I worked on tools to convert the data that we did have into something our artists could work with and then stitch their new art together with the old.

I also ended up rewriting the audio system as we had lost access to the libraries that the old code was built around. In the process I ended up integrating support for newer, better formats and learning a lot about audio.

# MDK2 on WiiWare (2011)
Yes, that says _WiiWare_, as in the original Nintendo Wii console. To call this a learning experience would be a hilarious understatement. While this game doesn't look _nearly_ as good as intended, I'm still immensely proud of what we achieved on this project given the constraints we were under (such as half the team suddenly becoming unavailabile for half the project).

The hardest challenge was shrinking the game data down to _less than a tenth_ of its original size. I ended up writing a fairly involved set of build tools to strip unused data out of the game assets and compress what remained. A lot of time and effort went into researching the best techniques to accomplish this. Ultimately, we had to sacrifice a lot of the source art's quality (particularly the textures and the sound) in order to fit the game to the amount of space we were allowed, and we unfortunately didn't quite have the time we needed to really tweak the art in order to work around those constraints. Some of the successes here involved rewriting the material system so that differently tinted or UV-wrapped versions of the same texture (there were _a lot_ of these) could be deduplicated by moving the tint operation to runtime.

Porting the sound and graphics code and working on the data-loading code was interesting, but I'd have to say I'm most proud of my work on the controls. MDK2 is a game that seems to have been made for the Wii remote, and while this version of the game may not look amazing, it's surprisingly fun to play.

# [The Beamdog Windows Client](http://www.beamdog.com/) (2011)
No, not the current client (do they even still have one?), the one from back when Beamdog ran a tiny Steam-esque startup online game store.

I really enjoyed this project. The Beamdog client is nothing like a game - it's a massive GUI on top of an online database and content distribution system. It was also my first significant foray into using [WPF](http://en.wikipedia.org/wiki/Windows_Presentation_Foundation) and working with a UI designer (designer meaning "a human who produces designs", not the visual editor that ships with Visual Studio) to create a UI that's not just functional but which also looked great at the time (alas, Aero glass was but a passing fad).

Whereas games are largely linear in their execution (gather input, simulate physics, simulate AI, update sounds, draw frame, repeat), the Beamdog client is _anything_ but. Dealing with a large number of systems that all run at their own speed (pulling game data, fetching product info from the store, waiting for data to save to or load from disk, etc) while the user is clicking about unpredictably and not having everything in a constant state of crashing made for some interesting challenges. It's even more challenging given that most of these processes can fail in many ways without warning (network errors, virus scanners locking cache files on disk, etc), and the app has to detect these errors and retry the operation or fall back to some other method without interrupting the user experience. And all of this before `async` and `await` came on the scene.

# [Rhythm Spirit](http://monadgames.com/?p=3) (2010)
This is the first project I programmed solo. The engine, the game code, the tools, and the build system that coordinates the process of making the game were all my doing.

This is also the first project I worked on remotely. That put a big focus on making the tools robust and easy to use so that work could be done on the art and levels without the artists constantly stalling to wait for me to fix bugs or explain their use. The size of the game data also made it take a fairly long time to upload new builds to the device, so I maintained an almost fully featured PC build throughout the course of development so that changes to the art could be seen in seconds rather than minutes.

There were of course other technical challenges, such as fitting a huge amount of uncompressible textures (the iPhone's compressed texture format ruined the clean lines on the art and couldn't be used) into the (original) iPhone's teeny tiny allotment of video memory.

And the game was actually pretty fun, too. Maybe one day someone will dust it off and get it running again.

# Saturday Night Fever: Dance (2008)
This was my first iPhone app and my first significant work on a Mac.

It's also the time I was lead programmer. It was a great learning experience and a significant milestone in my career. We actually started building this game on the Space Trader engine (which was in the process of being ported to iOS), but we came to a point where I had to kill that idea and build a stripped down purpose-built engine for the game. It reused a lot of the Space Trader tech, but it was important to get everything unnecessary out of the way since rhythm games _really_ make you feel even very slight hitches.

# Space Trader, Space Trader: Merchant Marine, Space Trader: Moon Madness (2008)
These games were my first real work in game development. Not counting a long-forgotten cancelled game that even I can't recall the name of, it's where I began.

I did a few interesting things here. The first was upgrading a _very_ old rendering engine that had been written before programmable shaders even existed and boosting our framerates (so that artists could crater them again by making things look nice) with the power of the amazing new ARB assembly language. (For those of you who know: yes, it was as painful as it sounds.)

The biggest highlight which built on top of that was figuring out how vertex skinning works and then building an entire pipeline to support character models that weren't just a stack of blobs. That meant writing a Maya plugin with a little UI (who remembers MEL scripting?) to extract the data, a data format to store it in, and runtime components which built on the earlier upgrade to the rendering engine to actually draw the models.

And then the last thing was that we decided for whatever reason that of all the languages we'd localize the game into, we'd do Russian. I'm the guy who UTF-8ified the engine and completely rewrote text rendering to make that work. Sorry to the Russians who had to read badly pasted-together bits of text. I tried to explain about cases. I tried.

And then we ported it all to the (original) iPhone and that was a cool challenge, too.