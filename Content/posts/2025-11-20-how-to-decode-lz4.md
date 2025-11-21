---
title: "How to decode LZ4"
time: 13:00
series: loading-content
seriestitle: "Loading Content"
tags:
  - programming
---

As background for the following post in this series, I need to describe the LZ4 format and how to decode it. This was going to be part of the next post, but it got _way_ too big. And, anyway, maybe this will be of use to someone on its own, and it'll be easier to find here than in the middle of a wall of text.

# How to decode LZ4 data

The LZ4 format is very simple, and the decoder algorithm is (roughly) as follows:

1. Read a byte. This is called a _token_ and it contains two fields:
   * 4 bits of _literal-length_
   * 4 bits of _match-length_
2. Copy a _literal:_
   1. If _literal-length_ is `0xF` then read an _extended-literal-length_ sequence:
      1. Read another byte (an _extended-length_ byte) and add its value to _literal-length_.
      2. If that _extended-length_ byte was itself `0xFF`, read another _extended-length_ byte and also add its value to _literal-length_. Keep reading and adding _extended-length_ bytes until you hit one that's not `0xFF`.
   3. Copy _literal-length_ bytes from the input stream to the output.
3. Copy a _match:_
   1. Read two bytes of _match-distance_. These are a little-endian encoded 16-bit unsigned integer which must not be zero.
   2. If _match-length_ is `0xFF`, read an _extended-match-length_ sequence in exactly the manner as the _extended-literal-length_ sequences above.
   3. Add 4 to _match-length_.
   4. Copy _match-length_ bytes from _match-distance_ bytes _behind_ the current end of the output stream to the output stream.
4. That's it for the current token. Loop back to step 1 to handle the next one.

Note that _match-length_ can be _greater_ than _match-distance_, in which case you'll be copying data which was just decoded as a part of this token. This gives a sort of run-length encoding.

## Knowing when to stop

But wait! If there's _always_ a match and the match is _always_ at least 4 bytes long, then how could one encode a file that's _less_ than 4 bytes, or one which simply doesn't have any matching sequences?

Well, here's the trick: as far as the _match-distance_ is concerned, the last _token_ might contain gibberish (generally _truncated_) instructions. You have to know how long the _output_ data is and stop processing the final token once you've written that many bytes.

## The 2-byte _match-distance_

The encoder compressed data by finding _repetition_ in the data stream. But in order to keep encoding and decoding efficient, there needs to be some limit on how hard it tries to find and leverage these repeated sequences. In the case of this algorithm, that limit is a 64K sliding window, which is why _match-distance_ is just 16 bits.

This also means that if you wish to suspend and then resume an LZ4 decoder, you have to keep the last-written 64K of data around as state, since it might (in practice: _will_) want to look back into that window when it's resumed. And _that_ sets the stage for the next post in this series.