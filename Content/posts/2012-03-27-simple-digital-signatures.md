---
id: 536
title: Simple Digital Signatures
date: 2012-03-27T16:49:11-07:00
author: phill
guid: http://vec3.ca/?p=536
permalink: /posts/simple-digital-signatures
categories:
  - code
  - security
tags:
  - code
  - cryptography
  - security
EditorsNotes:
  - Type: error
    Date: 2020-10-07
    Text: >
        <b>Warning:</b> this post is, as you can see, a bit dated, and it's only getting moreso as time passes. The fundamental principles here are sound, but you should use SHA-2 (or whatever is current at the date you're reading this) rather than SHA-1 (which is now known to have some weaknesses) or MD5 (which is known to be even less secure) and make sure you're using an up to date crypto library for the basic operations.
---
One of the things that comes up when sending data over the internet is verifying that it hasn't been corrupted. This is generally a simple thing to resolve: send the data and a good [hash](http://en.wikipedia.org/wiki/Hash_function) ([MD5](http://en.wikipedia.org/wiki/MD5) or [SHA-1](http://en.wikipedia.org/wiki/SHA-1)) of the data together. Recompute the hash on the client side and compare it to the hash you sent. If any bits have changed, the two won't match, and you know you need to redownload the file. I suppose it's possible both the data and hash could be corrupted in such a way that they match, but if your hash function is any good then the likelihood of this happening by chance is so astonishingly low that it doesn't bear consideration.

But what if you're worried that someone might be tampering with your file? An attacker editing the data could also replace the hash, leaving your app none the wiser. This is what [digital signatures](http://en.wikipedia.org/wiki/Digital_signature) were made for.

Digital signatures are based on [public key cryptography](http://en.wikipedia.org/wiki/Public-key_cryptography), for instance the [RSA algorithm](http://en.wikipedia.org/wiki/RSA_(algorithm)). In a nutshell, what you produce is a pair of keys (these are basically a related pair of enormously large numbers, though the details vary depending on your encryption algorithm of choice), and data which is encrypted with one key can only be decrypted with the other. One key, the private key, is kept secret. The other key, the public key, is attached to your program.

Once you've got your keys set up, you produce a digital signature by encrypting the hash you originally computed with your private key. Your app then decrypts it with its copy of the public key and compares it to the hash of the data which it computes. So long as you keep your private key closely guarded, an attacker will be unable to create a signature which decrypts with your app's public key.

## Implementation

So how do we implement this? Well, it's not particularly difficult to do, but be warned, **you must be very careful when implementing as it is very easy to make mistakes which defeat the entire system**. In fact, the sample code below should _not_ be used verbatim. It's been pulled from a larger block of code and trimmed and modified significantly for public consumption. **Don't assume I haven't broken something in the process**. _You have been warned!_

Another note before I begin: the toolchain accompanying the engine I've integrated this into is written in C#. That means I'm going to be using the standard .NET cryptography API to create my signature. The details may change if you use a different API. I'll be using SHA-1 hashes and RSA encryption.

### Creating the Signature

Creating the signature is fairly straight-forward. Generate a hash and encrypt it:

```csharp
using System.IO;
using System.Security.Cryptography;
 
//...
 
byte[] CreateSignature( Stream data, RSAParameters privateKey )
{
	//first we compute the hash of the data
 
	byte[] hash;
	using( var hasher = new SHA1Managed() )
		hash = hasher.ComputeHash( stream );
 
	//and then we sign it
 
	using( var rsa = new RSACryptoServiceProvider() )
	{
		rsa.ImportParameters( privateKey );
		return rsa.SignHash( hash, "SHA1" );
	}
}
```

Creating the hash is fairly straightforward, but if we're going to use that signature later then we need to know what `RSACryptoServiceProvider.SignHash` is doing. Well, the first thing it does is it takes our hash and appends it to a little block of data describing the hash algorithm we've used to create it (that's why it needs the last parameter). After that, it attaches [PKCS 1.5](http://en.wikipedia.org/wiki/PKCS) padding to the data in order to make it large enough to encrypt (RSA encrypts blocks of data equal in size to the key you use). Once that's done, it encrypts the data using the private key you supplied earlier and returns the encrypted blob (also equal in size to the key you're using).

Now in my case I attach both the signature _and_ the public key (making it easier to use multiple keys) to the data that I'm sending, but this isn't required. If you'll only ever support one key you can omit that portion, and there's really no rule that says the data has to be attached to the same data stream - it could be stored anywhere.

```csharp
void SignStream( Stream data, RSAParameters key )
{
	var signature = CreateSignature( data, key );
 
	//creating the signature leaves the stream's position
	//at the end of the data, so we're good to write more
 
	var writer = new BinaryWriter( data );
 
	//write some header info (the key size)
 
	writer.Write( key.Modulus.Length );
	writer.Write( key.Exponent.Length );
 
	//write the public portion of the key
 
	writer.Write( key.Modulus );
	writer.Write( key.Exponent );
 
	//and write the signature
 
	writer.Write( signature );
}
```

Great, so now we've got our signature tacked onto our file. Now what?

### Verifying the Signature

Well, first we'll need an implementation of SHA-1 and RSA. The former is easy. The latter, well, not so much. Many of the common system libraries are set up such that they only accept keys from their own secure stores, making it a pain to set them up for use. In my case I also want this code to be portable, and rewriting (and testing!) it for each and every platform just isn't something I'm about to do. Fortunately, there are several free RSA implementations available.

For this example, I'll be using the one in [the axTLS project](http://axtls.sourceforge.net/). I've picked this one because its RSA implementation is small and self-contained (many others depend on _massive_ math libraries) which makes it easy to integrate (take `rsa.c` and `bigint.c` plus the headers they need and make sure `CONFIG_SSL_CERT_VERIFICATION` is defined). It's also got a handy SHA-1 implementation sitting right next to its RSA code. Another good option is [LibTomCrypt](http://libtom.org/?page=features&whatfile=crypt) (but be warned, it takes some effort to get it compiling on Windows).

```c++
#include "axTLS/crypto/crypto.h"
#define HASH_SIZE SHA1_SIZE //SHA1_SIZE = 20
#define MAX_KEY_LEN 512 //max possible key.Modulus.Length
```

Once that's done, we need to load our file and parse out the signature. Let's skip the tedious IO code and assume we've got everything in memory as follows:

```c++
const void *data = /* the signed data */ ;
size_t data_size = /* the size of the data */ ;
 
size_t mod_size = /* key.Modulus.Length */ ;
size_t exp_size = /* key.Exponent.Length */ ;
 
const void *mod = /* key.Modulus data */ ;
const void *exp = /* key.Exponent data */ ;
 
const void *sig = /* the signature */ ;
```

The first step is to make sure that this is indeed our public key (you can skip this if you're using only one key and haven't got it attached to each signature).

```c++
//valid_keys is a list of all the public
//keys that match trusted private keys
 
bool is_trusted_key = false;
 
for( auto p = valid_keys.begin(); p != valid_keys.end(); ++p )
{
	if( p->mod_size != mod_size || p->exp_size != exp_size )
		continue;
 
	if( memcmp( p->mod, mod, mod_size ) != 0 )
		continue;
 
	if( memcmp( p->exp, exp, exp_size ) != 0 )
		continue;
 
	//found a matching key
	is_trusted_key = true;
	break;
}
 
if( !is_trusted_key )
	return ERR_KEY_NOT_TRUSTED;
```

The next step is computing the SHA-1 hash of the data.

```C++
unsigned char hash[SHA1_SIZE];
 
SHA1_CTX md;
SHA1_Init( &md );
SHA1_Update( &md, (const uint8_t*)data, data_size );
SHA1_Final( hash, &md );
```

Next up, we decrypt the signature.

```C++
//initialize an RSA context
 
RSA_CTX *rsa = NULL;
RSA_pub_key_new( &rsa, mod, mod_size, exp, exp_size );
 
if( !rsa )
	return ERR_OUT_OF_MEMORY;
 
//decrypt the data
 
uint8_t sig_bytes[MAX_KEY_LEN];
int len = RSA_decrypt( rsa, (const uint8_t*)sig, sig_bytes, 0 );
 
//clean up
 
RSA_free( rsa );
 
//check for errors
 
if( len == -1 )
	return ERR_INVALID_SIGNATURE;
```

Now `RSA_decrypt` takes care of unpadding the data, but the signature is still preceded by the hash identifier. So we need to take just the last `HASH_SIZE` bytes of the decoded buffer. (Note: it's not a bad idea to validate hash ID, too. I'm keeping things simple to illustrate the basic process.)

```C++
if( len < HASH_SIZE )
	return ERR_INVALID_SIGNATURE;
 
uint8_t *sig_hash = sig_bytes + len - HASH_SIZE;
```

And all that's left now is to compare the hashes:

```C++
if( memcmp( hash, sig_hash, HASH_SIZE ) != 0 )
	//they don't match, therefore the data
	//has changed since we made the signature
	return ERR_INVALID_DATA;
 
//everything checks out, the hashes match
 
return SUCCESS;
```

And that's that. If we get a value of `SUCCESS` then we can be certain of the following:

  * The data isn't randomly corrupted.
  * The data hasn't been tampered with.
  * The data was produced by someone who has a trusted private key.

And if we know that we've kept our private keys safe, then we can be sure that we're the ones that made the data.