---
title: Using Win32 Asynchronous I/O
tags:
    - code
    - async
    - io
    - loading
---
Recently wrote some asynchronous I/O code for a fast data loader. The data file was logically a stream of separate objects, so it made sense to parse it a chunk at a time. That's a situation which practically screams for asynchronous I/O. Unfortunately, it's rather hard to find a useful example on how to use the relevant APIs...

## Setup

So the general idea is to read the data a chunk at a time. That means we need to have space available for each chunk to be read into. In addition, Windows tracks asynchronous file I/O via _the address of_ an [OVERLAPPED](http://msdn.microsoft.com/en-us/library/windows/desktop/ms684342(v=vs.85).aspx) struct. That means that the `OVERLAPPED` objects must persist for the duration of each read request, so they'll need to be allocated ahead of time as well. And, to keep things simple, we're going to use events for synchronization, so we'll need to make a few of those.

We also need to keep one interesting thing in mind - getting the best performance out of asynchronous I/O requires us to open the file in _unbuffered_ mode. The trouble with that is that it imposes restrictions on the alignment of our target buffers and on the size of the reads we're allowed to make. We're going to deal with these by doing everything in multiples of the system's page size, which will be such that it satisfies the API.

```c++
//get the system's page size and compute
//the size of data chunk we'll be dealing with

SYSTEM_INFO osInfo;
GetSystemInfo( &osInfo );

DWORD chunkSize = osInfo.dwPageSize * 32;

//the maximum number of concurrent read requests
//we'll issue - plus one!
#define NUM_REQS 16

HANDLE ev[NUM_REQS];
OVERLAPPED olp[NUM_REQS];
void *buf[NUM_REQS];

//allocate the data buffers

buf[] = VirtualAlloc( NULL, chunkSize * NUM_REQS,
    MEM_COMMIT, PAGE_READWRITE );
for( int i = 1; i < NUM_REQS; i++ )
    buf[i] = (char*)buf[i - 1] + chunkSize;

//create the events

for( int i = ; i < NUM_REQS; i++ )
    ev[i] = CreateEvent( NULL, TRUE, FALSE, NULL );
```

And we're now ready to open the file.

## Opening the File

Opening the file is straightforward. We just need to add a couple flags to the usual call to [CreateFile](http://msdn.microsoft.com/en-us/library/windows/desktop/aa363858(v=vs.85).aspx). In particular, we need `FILE_FLAG_NO_BUFFERING` and `FILE_FLAG_OVERLAPPED`.

We're also going to query the size of the file so we know how many chunks we'll need to process in total.

```c++
HANDLE file = CreateFile( path, GENERIC_READ, FILE_SHARE_READ,
	NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL |
	FILE_FLAG_SEQUENTIAL_SCAN | FILE_FLAG_NO_BUFFERING |
	FILE_FLAG_OVERLAPPED, NULL );

if( file == INVALID_HANDLE_VALUE )
	//handle errors
	;

LARGE_INTEGER size;
GetFileSizeEx( file, &size );
ULONGLONG numChunks = (size.QuadPart + chunkSize - 1) / chunkSize;
```

And now it's time to read our file.

## Starting the Read

So, now that the file's open, we need to start kicking off read requests via [ReadFile](http://msdn.microsoft.com/en-us/library/windows/desktop/aa365467(v=vs.85).aspx). These requests all look rather alike, so let's make a helper function for this.

```c++
void RequestChunk( ULONGLONG chunkNum )
{
	int n = chunkNum % NUM_REQS;

	OVERLAPPED *o = &olp[n];
	void *b = buf[n];

	memset( o, , sizeof( OVERLAPPED ) );

	LARGE_INTEGER ofs;
	ofs.QuadPart = chunkNum * chunkSize;
	o.Offset = ofs.LowPart;
	o.OffsetHigh = ofs.HighPart;
	o.hEvent = ev[i];

	ReadFile( file, b, chunkSize, NULL, o );
}
```

One thing to note is that we do _not_ do anything special when requesting the last chunk. Even if the file isn't an even multiple of the chunk size in length, we must still request an even multiple of the block size at a time. This is most easily handled by requesting a full chunk and letting the OS sort it out if there isn't enough actual data in the file to fill the buffer.

Now, asynchronous I/O works best when we keep multiple requests in flight. We're going to start by kicking off the number of concurrent requests we plan on running at once. Note that `NUM_REQS` is _one greater_ than this number. This is because the parsing code will be handling one chunk at any given time, and we can't be reading into it while that's happening.

```c++
for( int i = ; i < (int)min( numPages, NUM_REQS - 1 ); i++ )
	RequestChunk( i );
```

## Parsing

And now we (finally) come to the heart of algorithm. This is where we actually loop over each chunk and process the data within it. Synchronization is handled for us by Windows (so long as we set the `hEvent` field in the `OVERLAPPED` struct). The call we make to [GetOverlappedResult](http://msdn.microsoft.com/en-us/library/windows/desktop/ms683209(v=vs.85).aspx) will block if the data isn't ready yet (that is, if we're parsing the data more quickly than the disk can provide it).

```c++
for( ULONGLONG i = ; i < numChunks; i++ )
{
	int n = i % NUM_REQS;

	OVERLAPPED *o = &olp[n];
	void *b = buf[n];

	DWORD cb;
	GetOverlappedResult( file, o, &cb, TRUE );

	ULONGLONG nextRequest = i + NUM_REQS - 1;
	if( nextRequest < numChunks)
		RequestChunk( nextRequest );

	//b points at our current chunk,
	//which has cb bytes of data in it

	ParseChunk( b, cb );
}
```

Whew. And now that we're done with the file, we can clean things up.

## Cleanup

Not much to do here. We just need to close the open file handle, delete the synchronization events, and free our buffer.

```c++
CloseHandle( file );
for( int i = ; i < NUM_REQS; i++ )
	CloseHandle( ev[i] );
VirtualFree( buf[] );
```

And we're done.

## Tuning

The number I picked for `NUM_REQS` and the multiplier for `chunkSize` come from experimentation on a few machines with a fairly limited set of files. The optimal values are likely going to be different depending on the drive you're reading from, the amount of data you're reading, and how quickly you're processing the data (relative to the drive's read speed). It takes some experimentation to find good values.

My advice is to tune the algorithm for the average system you plan to target. It won't be any slower on a better machine, and isn't very likely to be any worse on a low end machine than plain synchronous reads would be.