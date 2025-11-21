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
      2. If that byte was itself `0xFF`, read another _extended-length_ byte and also add its value to _literal-length_ (and keep looping until we get an extended-length byte that's less than `0xFF`).
   3. Copy _literal-length_ bytes from the input stream to the output.
3. Copy a _match:_
   1. Read two bytes of _match-distance_. These are a little-endian encoded 16-bit unsigned integer which must not be zero. (This is where the 64K buffer size comes from.)
   2. Add 4 to the _match-length_ decoded from the _token_. 
   3. If _match-length_ is now `0xFF + 4`, read an _extended-match-length_ sequence in exactly the manner we handled _extended-literal-length_ sequences above.
   4. Copy _match-length_ bytes from _match-distance_ bytes _behind_ the current end of the output stream to the output stream.

      Note that _match-length_ can be _greater_ than _match-distance_, in which case you'll be copying data which was just decoded as a part of this token. This gives a sort of run-length encoding.

## Knowing when to stop

But wait! If there's _always_ a match and the match is _always_ at least 4 bytes long, then how could one encode a file that's _less_ than 4 bytes, or one which simply doesn't have any matching sequences?

Well, here's the trick: as far as the _match-distance_ is concerned, the last _token_ might contain gibberish (generally _truncated_) instructions. You have to know how long the _output_ data is and stop processing the final token once you've written that many bytes.