---
title: "The Player's Name vs Localization"
tags:
  - design
  - language
---
From a game developer's perspective, the English language is incredibly simple. Our grammar is only minimally [inflected](http://en.wikipedia.org/wiki/Inflection), making it easy to author strings like "_@(PLAYER)_ runs away!" and "Give this to _@(TARGET)_." and use simple text substitution to replace tokens like "_@(TARGET)_" with the name of a player, NPC, or object as needed. There are some places where this doesn't quite work (dealing with numbers and plurals or dialog which might be referring to either males or females), but they're either uncommon or easy to ignore (in many cases it's unlikely the player will have just one of something and we can just use the plural).

However, this is far from the case in many other languages. Now that's obviously common sense, but I've seen authors underestimate the magnitude of the problem many times, so an example is in order. Let's take Russian and assume we've got two players, Антон (Anton) and Юлия (Julia). Let's take a look at how some simple examples play out, and mind the italics.

The first example I gave works easily with both: <acronym title="Anton runs away.">"Антон убегает."</acronym> and <acronym title="Julia runs away.">"Юлия убегает."</acronym> are both correct. However, if we put the sentence in the past tense, things change: <acronym title="Anton ran away.">"Антон убежа<em>л</em>."</acronym> and <acronym title="Julia ran away.">"Юлия убежа<em>ла</em>."</acronym> It's no longer enough to just have the player name around, you now need a system that knows the player's gender and can match the appropriate verb form to it. You've now got a template that includes little functions like "_@(PLAYER)_ _@(match-gender, PLAYER, убежал, убежала)_." and the substitution code just got a hell of a lot more complex.

The second example is even worse, because it's not just the verbs that change in Russian. The nouns do too, including the ones you're trying to drop into sentences. Let's take a look: <acronym title="Give this to Julia.">"Дайте это Юли<em>и</em>."</acronym> and <acronym title="Give this to Anton.">"Дайте это Антон<em>у</em>."</acronym> Awesome. More endings changing. These ones reflect the noun's role in the sentence, and they're particularly nasty because the rules depend not just on the word's gender and role in the sentence, but also on the particular word itself. Oh, and adjectives also change along with the noun they're attached to, too.

And then there's numbers. One year, two year<em>s</em>, three year<em>s</em>, so on. In Russian that's один год, два год<em>а</em>, три год<em>а</em>, so far so good, the plural's a bit more complex than just adding an S (it varies by gender) but no big deal... until we get to the number five: пять _лет_. _Huh?!_ Well, "года" wasn't a simple plural like we're used to; in Russian they count like this: "one _year_, two _of year_, three _of year_, four _of year_, five _of years_..." (and then at 21, 31, 51, etc the pattern repats: "21 year, 22 of year, 31 year...").

And that's just a few examples in one language. Other languages present their own difficulties and will need their own sets of complex substitution rules (like our gender-matching example above). So how do we deal with this?

Well, we can ignore it. The translation team will usually be able to work around the worst offenses, though the resulting sentences may come out stilted or ambiguous. It's unfortunate and looks _much_ worse in some languages than "Anton picks up one boxes." does to us, but sometimes that's just life.

Alternately, we can develop a sophisticated system of substitution functions. This makes it possible to author sentences that correctly morph to accommodate different words. However, this is an _incredibly_ difficult task in some languages. Furthermore, the more complex the system gets, the more the translators need to be able to think like programmers in order to really leverage the functions (and maybe you'll need programmers fluent in the target language to really nail the framework). So, realistically, this approach only goes so far, and we'll want to work with the localization team to come up with the minority of features which will cover the majority of cases.

More likely, we can grab a localization library, cross our fingers, and just trust that there aren't any bugs in there that'll offend folks far away or otherwise embarrass us.

And finally, we can plan ahead and author the content to be less reliant on dynamically assembled text in the first place. The less we manipulate display strings in code, the less there is to worry about and test.