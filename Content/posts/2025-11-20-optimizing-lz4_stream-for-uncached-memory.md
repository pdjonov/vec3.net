---
title: "Optimizing lz4_stream for Uncached Memory"
time: 15:00
series: loading-content
tags:
  - C
  - code
  - performance
  - programming
---

The loading side of my content framework relies heavily on compression, but it also relies on the compression library being able to pause and resume (in the way that the standard zlib and LZMA interfaces allow), which isn't (wasn't?) available with LZ4 back when I first adopted the format. So I wrote my [own LZ4 decoder](https://github.com/pdjonov/Lz4Stream) to support just that. Unfortunately, I recently hit a really bad performance snag.

The short version of the problem is that my (ancient) phone has a somewhat buggy Vulkan driver which reports that the only memory type available for staging buffers is [`HOST_CACHED`](/posts/vulkan-memory-types#caching-flags), but it actually is _not_ cached. I, being the sort of man who refuses to throw away "working" hardware until I've driven it into the ground, decided I could work around this myself.

The basic workaround honestly isn't _that_ bad - I just decompress to a `malloc`ed buffer and then copy from that to my staging buffer before the driver does the last copy into the GPU's preferred memory layout. But, LZ4 is my most commonly used compression option _and_ I wrote the decoder myself, so surely I could do better...

# How lz4_stream works

The stock LZ4 implementation (and I'm going from memory here - I wrote the first version of lz4_stream over a decade ago) is very straightforward. It just reads input tokens/literals and copies them directly into the output stream, sometimes copying from previously decoded bits of the output stream (which is the part that's _suuuuuuper_ slow in uncached memory).

In order to support arbitrary pause/resume in the way that zlib does, I had to break the algorithm's assumption that it could just read arbitrary distances backwards in the output buffer. To do that, when pausing the decoder, I capture the last 64K of output (64K is the maximum distance it can copy from) in a block of memory in the decoder state, and if I need to look back past the start of the current output buffer, I can find that in the captured block.

Obviously, there are details about how the buffer is laid out which make this a bit more efficient than it sounds. The original implementation actually performed pretty well - better than that time's zlib - so I've kept it around all these years.

<div class="alert alert-warning">

The discussion which follows assumes you've read the [previous post](/posts/how-to-decode-lz4) in this series outlining the operation of an LZ4 decoder, and that you therefore know what a _match-distance_ and _match-length_ are supposed to be.
</div>

## How to fix it for uncached memory

Given that I already have the backrefernce buffer in my decoder state, decoding to uncached memory should be a simple change. Instead of decoding to the output buffer and then capturing the end of that buffer into the decoder state, I could just decode into the decoder state and then copy from there to the output buffer. Uncached memory is slow to _read_ from, but only _writing_ to it sequentially isn't slow at all.

The first iteration of this was very straightforward. It was _much_ faster in uncached memory (my test scene's load time went from more than a minute to about ten seconds), but pretty slow in cached memory. So I decided to make it _better_.

## Copying literals

This is the easiest of the two main loops in the algorithm since `memcpy` can do all the hard work.

1. Clamp the (remaining) literal length to the size of the input and output buffers.
2. If the clamped length is greater than 64K:
   1. Copy all but the last 64K directly to the output buffer.
   2. Copy the rest to the internal buffer.
   3. Copy from the internal buffer to the output stream.
   4. Reset the internal buffer's current position. (It's a circular buffer, and the whole thing was just overwritten with new content so, wherever it was at before, it now restarts at _zero_.)
3. Otherwise:
   1. Copy to the internal buffer, perhaps splitting the copy into two parts if it has to go around the end of the buffer. (Again, circular buffer.)
   2. Copy that from the internal buffer to the output.

Since `memcpy` is already perfectly suitable for writing to uncached memory, there's really nothing further to be done here.

## Copying matches

This is where things get tricky. There are two main cases to consider:

1. The _match-length_ is less than (or equal to) the _match-distance_. In this case there's no overlap between what's being copied and where it's being copied _to_.
2. The _match-length_ is greater than the _match-distance_. Here there's an overlap which has to be respected (this is the RLE case).

### No overlap

In the first case, there's no RLE stuff going on, the source and destination regions do not overlap, and I just `memcpy` (taking care to not go past the end of the internal decode buffer).

```c
static unsigned int lz4_dec_cpy_mat_no_overlap(
	unsigned int copy_mat_len, //the match length
	unsigned int o_inpos, //the read position from the 64K buffer
	unsigned int o_pos, //the write position in the 64K buffer
	uint8_t* restrict o_buf, //the 64K buffer
	uint8_t* restrict out //the output buffer
	)
{
	for (unsigned int copy_len, copy_mat_len_left = copy_mat_len;
		copy_mat_len_left != 0;
		copy_mat_len_left -= copy_len)
	{
		copy_len = copy_mat_len_left;

		unsigned int pos_avail = O_BUF_LEN - o_pos;
		if (UNLIKELY(copy_len > pos_avail))
			copy_len = pos_avail;

		unsigned int inpos_avail = O_BUF_LEN - o_inpos;
		if (UNLIKELY(copy_len > inpos_avail))
			copy_len = inpos_avail;

		memcpy(o_buf + o_pos, o_buf + o_inpos, copy_len);
		memcpy(out, o_buf + o_inpos, copy_len);

		o_pos = WRAP_OBUF_IDX(o_pos + copy_len);
		o_inpos = WRAP_OBUF_IDX(o_inpos + copy_len);

		out += copy_len;
	}

	return copy_mat_len;
```

This loop will usually complete in just one iteration. However, if either the source or target region (or both) clip against the end of the 64K buffer, then the overall operation needs to be cut up into several smaller copies.

### RLE

It's the second case where things get interesting. Becasue the source and destination regions overlap, I can't use `memcpy`. And `memmove` also wasn't designed for RLE shenanigans, so it's also out of the picture. (I think my original implementation got this wrong, but it just so happened to work on the standard libraries I run with - I'll fix this eventually.)

The simplest way to write this is just copying one byte at a time:

```c
static void lz4_dec_cpy_mat_bytes(
	unsigned int copy_mat_len,
	unsigned int o_inpos, unsigned int o_pos,
	uint8_t* restrict o_buf,
	uint8_t* restrict out
	)
{
	for (unsigned int i = 0; i < copy_mat_len; i++)
	{
		uint8_t c = o_buf[o_inpos];
		o_inpos = WRAP_OBUF_IDX(o_inpos + 1);

		o_buf[o_pos] = c;
		o_pos = WRAP_OBUF_IDX(o_pos + 1);

		*out++ = c;
	}
}
```

This is also very slow.

A much faster approach is one that copies entire machine words at a time.

#### Unaligned reads and writes near the 64K buffer's edges

First, let's deal with a small complication. Reading and writing near the edges of the 64K buffer is annoying since I'd have to go byte-by-byte. Better if I could just do a pair of unaligned read-word instructions than a whole loop of read-byte. To allow this, I've added some padding around the internal buffer, which lets me read and write a little past its end on either side without crashing or corrupting anything:

```c
// old
uint8_t			o_buf[0x10000];

// new
uint8_t			o_buf[32 + 0x10000 + 32];
```

Why 32 bytes and not just `sizeof(uintptr_t)`? Well, I might eventually use SIMD instructions for this stuff, and that's there to remind me to do so. Also the cost is trivial - an extra 48 bytes is nothing next to the 64K I've already signed  up for.


#### Bit-twiddling hacks

When working with machine words, it's also important to remember that the logic for assembling bytes into words is different on different processors. Fortunately, in this case, the only thing that changes when switching the byte order is the direction of the shifts, and I capture this with a few simple macros:

```c
#if LZ4_BYTE_ORDER == LITTLE_ENDIAN
	#define RBOS >>	// right-shift on little endian; left-shift on BE
	#define LBOS <<	// left-shift on little endian; right-shift on BE
#elif LZ4_BYTE_ORDER == BIG_ENDIAN
	#define RBOS <<	// left-shift on big endian; right-shift on LE
	#define LBOS >>	// right-shift o nbig endian; left-shift on LE
#else
	#error "No fallback available for unknown endianness."
#endif
```

And I also need to make masks to zero out all but a few bytes within a word:

```c
// mask off all but the N right-most (little-endian; leftmost on BE) bytes
#define MASK_N(type, n) ((type)-1 RBOS (sizeof(uintptr_t) - (n)) * 8)
```

#### The easy case

When _match-distance_ is greater than the word size, there's really nothing special to worry about.

First, while there's at least a full machine word's left of bytes to copy, I read a word from the input cursor. If that happened to go off the edge of the 64K buffer, I do another read from the start of the buffer and stitch together the good bytes from both reads. Then I write the word back to the internal buffer (again, taking care in the case where I go off the end and have to wrap back around). And then I finally write the word to the output stream.

Note that I'm using `memcpy` with a constant size to do all the reads and writes. That is just the C idiom to emit whatever the correct instruction is for an _unaligned_ load or store. These aren't actual calls to the actual library function, and you can verify that using [Matt Godbolt's Compiler Explorer](https://godbolt.org/), if you like.

```c
static unsigned int lz4_dec_cpy_mat_rle_long_dst(
	unsigned int copy_mat_len,
	unsigned int o_inpos, unsigned int o_pos,
	uint8_t* restrict o_buf, uint8_t* restrict out)
{
	_Static_assert(sizeof(uintptr_t) <= O_BUF_PAD, "padding insufficient for sloppy reads");

	unsigned int n_copied = 0;

	while (copy_mat_len >= sizeof(uintptr_t))
	{
		uintptr_t c;

		//read the next word from o_buf's read cursor

		memcpy(&c, o_buf + o_inpos, sizeof(c));

		unsigned int n_read = O_BUF_LEN - o_inpos;
		if (UNLIKELY(n_read < sizeof(c)))
		{
			//we read off the end of o_buf's active area into the scratch space
			//read from the beginning and patch the read values

			uintptr_t c2;
			memcpy(&c2, o_buf, sizeof(c2));

			c &= MASK_N(uintptr_t, n_read);
			c |= c2 LBOS n_read * 8;
		}
		o_inpos = WRAP_OBUF_IDX(o_inpos + sizeof(c));

		//write the word back to o_buf's write cursor

		memcpy(o_buf + o_pos, &c, sizeof(c));

		unsigned int n_written = O_BUF_LEN - o_pos;
		o_pos = WRAP_OBUF_IDX(o_pos + sizeof(c));

		if (UNLIKELY(n_written < sizeof(c)))
			//some bytes went into the scratch pad past the end
			//need to copy those to the beginning of the buffer
			memcpy(o_buf + o_pos - sizeof(c), &c, sizeof(c));

		memcpy(out + n_copied, &c, sizeof(c));
		n_copied += sizeof(c);

		copy_mat_len -= sizeof(c);
	}

	return n_copied;
}
```

Note that `lz4_dec_cpy_mat_rle_long_dst` might return an `n_copied` which is _less_ than `copy_mat_len`. In this case, the slow `lz4_dec_cpy_mat_bytes` will be called to take care of the remainder.

#### The hard case

I start by just loading _match-distance_ bytes into a register, (represented here by the `uintptr_t` variable `c`), in exactly the same way `lz4_dec_cpy_mat_rle_long_dst` loads a word.

Next, I deal with the fact that only the first _match-distance_ bytes are valid by copying just those bytes again and again until they've overwritten everything else which was in the register:

```c
c &= MASK_N(uintptr_t, mat_dst);
for (unsigned int n = mat_dst; n < sizeof(uintptr_t); n *= 2)
	c |= c LBOS n * 8;
```

If _match-distance_ is a factor of `sizeof(uintptr_t)`, then this is all that's needed. But there's no guarantee that it will be, so I have to deal with that. This is a bit tricky.

For illustration, I'll represent bytes as single letters and machine words as containing 8 bytes. Imagine that _match-distance_ is _3_. After filling the register, it will look something like this:
```
ABCABCAB
```

But if I copied that end to end a few times, I'd get the wrong pattern of bytes (which I've placed side below with the correct pattern):
```
bad:  ABCABCABABCABCABABCABCAB
good: ABCABCABCABCABCABCABCABC
```

To fix this, every time I write the contents of `c`, I'll shuffle its bytes around to make sure the _next_ write starts on the correct byte. So after writing an `ABCABCAB`, the _next_ write needs to start not on an `A` but rather a `C`. That means the register needs to be shifted by two bytes to get the incorrect leading `AB` out of the way:

```
CABCAB00
```

Then the two zero bytes that I just shifted in need to be filled with real data. Extending the pattern, those bytes should be another `CA`. And, what a coincidence, that happens to be the first two bytes of the just-shifted register!

Okay, that's actually not a coincidence at all. The two bytes I just shifted out of the way were the left over _remainder_ of the fact that the word size isn't a multiple of _match-distance_. After they're out of the way, the length of what's left (up to the zeroes I just shifted in) _is_ a multiple of _match-distance_, and so the correct place to start repeating is at the beginning of the adjusted register. So those last two zero bytes should be filled in with the _first_ two bytes.

Putting that all together in code makes a function that's a bit like this:
```c
static unsigned int lz4_dec_cpy_mat_rle_short_dst(
	unsigned int copy_mat_len, unsigned int mat_dst,
	unsigned int o_inpos, unsigned int o_pos,
	uint8_t* restrict o_buf, uint8_t* restrict out)
{
	_Static_assert(sizeof(uintptr_t) <= O_BUF_PAD, "padding insufficient for sloppy reads");
	assert(mat_dst < sizeof(uintptr_t));
	ASSUME(mat_dst < sizeof(uintptr_t));

	if (copy_mat_len < sizeof(uintptr_t))
		return 0;

	unsigned int n_copied = 0;

	uintptr_t c;
	memcpy(&c, o_buf + o_inpos, sizeof(c));
	unsigned int n_read = O_BUF_LEN - o_inpos;
	if (UNLIKELY(n_read < mat_dst))
	{
		uintptr_t c2;
		memcpy(&c2, o_buf, sizeof(c2));

		c &= MASK_N(uintptr_t, n_read);
		c |= c2 LBOS n_read * 8;
	}

	c &= MASK_N(uintptr_t, mat_dst);
	for (unsigned int n = mat_dst; n < sizeof(uintptr_t); n *= 2)
		c |= c LBOS n * 8;

	unsigned int shift = (sizeof(uintptr_t) % mat_dst) * 8;

	while (copy_mat_len >= sizeof(c))
	{
		memcpy(o_buf + o_pos, &c, sizeof(c));

		unsigned int n_written = O_BUF_LEN - o_pos;
		o_pos = WRAP_OBUF_IDX(o_pos + sizeof(c));

		if (UNLIKELY(n_written < sizeof(c)))
			//some bytes went into the scratch pad past the end
			//need to copy those to the beginning of the buffer
			memcpy(o_buf + o_pos - sizeof(c), &c, sizeof(c));

		memcpy(out + n_copied, &c, sizeof(c));
		n_copied += sizeof(c);

		c = c RBOS shift;
		c |= c LBOS (sizeof(uintptr_t) * 8 - shift);

		copy_mat_len -= sizeof(c);
	}

	return n_copied;
}
```

Again, this can return an `n_copied` less than `copy_mat_len` and, again, that'll be handled by running the byte-at-a-time loop to make up the difference.

And with these two optimizations in place, my load time is now only _three_ seconds, which isn't annoying enough to keep me glued to this code for the moment, though I'm sure I'll be back later at some point. In the mean time, I've got a buggy VK driver on my Quest 3 to contend with. (Yeah, another problem with an Android Vulkan implementation. Yay mobile dev...)