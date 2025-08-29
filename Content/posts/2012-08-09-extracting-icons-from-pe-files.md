---
title: Extracting Icons from PE Files
tags:
  - 'C#'
  - code
  - programming
  - Windows
---
There are times when you need an icon file, but all you have is an icon resource embedded in a [PE (executable)](http://en.wikipedia.org/wiki/Portable_Executable) file. Getting at these is a little tricky, since icon files aren't stored as a simple blob in the PE file. In fact, they're split up into a number of different entries. Fortunately, it isn't very hard to combine these entries into an ICO-format data blob which you can then save to file or pass to an API that expects it.

I'll be writing the sample code for this post in C#, and as such I'll be using the `Win32ResourceStream` class from my [last post](/posts/working-with-win32-resources-in-dot-net). For this particular example, I'll be loading the current assembly's main icon.

```csharp
public static Stream ExtractAssemblyIcon( Assembly asm )
{
    var module = asm.ManifestModule;
    var resId = (ushort)32512;
 
    //extract the icon here...
}
```

(If you're wondering where the weird 32512 comes from, it's the value of [IDI_APPLICATION](http://msdn.microsoft.com/en-us/library/windows/desktop/ms648072(v=vs.85).aspx), and is the resource ID assigned to the assembly icon by the C# compiler.)

# The Icon Header

The first thing we'll need to load is the icon's header. It's stored in its own resource and consists of an array of image descriptions. I'm not going to get into any detail about many of the fields since we'll generally just be writing them to our output stream.

We'll need to store the entries in an array. Here's the struct defining each entry:

```csharp
struct MemIconEntry
{
    public byte Width;
    public byte Height;
    public byte ColorCount;
    public byte Reserved;
    public ushort Planes;
    public ushort BitCount;
    public uint BytesInRes;
    public ushort Id;
}
```

and here's how we'll load them:

```csharp
MemIconEntry[] entries;
 
using( var resStream = new Win32ResourceStream( module,
    resId, Win32ResourceType.GroupIcon ) )
{
    var reader = new BinaryReader( resStream );
 
    if( reader.ReadUInt16() != 0 )
        throw new InvalidDataException();
    if( reader.ReadUInt16() != 1 )
        throw new InvalidDataException();
 
    var numEntries = reader.ReadUInt16();
 
    entries = new MemIconEntry[numEntries];
    for( int i = 0; i < entries.Length; i++ )
    {
        entries[i].Width = reader.ReadByte();
        entries[i].Height = reader.ReadByte();
 
        entries[i].ColorCount = reader.ReadByte();
 
        entries[i].Reserved = reader.ReadByte();
 
        entries[i].Planes = reader.ReadUInt16();
        entries[i].BitCount = reader.ReadUInt16();
 
        entries[i].BytesInRes = reader.ReadUInt32();
        entries[i].Id = reader.ReadUInt16();
    }
}
```

Now that we have those, we're ready to start writing our icon data. We'll be writing it to a `MemoryStream` here, though it could just as easily be written to file:

```csharp
var ret = new MemoryStream();
var writer = new BinaryWriter( ret );

writer.Write( (ushort)0 );
writer.Write( (ushort)1 );
writer.Write( (ushort)entries.Length );

//each entry has an offset to the start of that
//icon's image data, we start that offset at the
//byte immediately following the header data
uint offset = 6U + 16U * (uint)entries.Length;

foreach( var e in entries )
{
    writer.Write( e.Width );
    writer.Write( e.Height );

    writer.Write( e.ColorCount );

    writer.Write( e.Reserved );

    writer.Write( e.Planes );
    writer.Write( e.BitCount );

    writer.Write( e.BytesInRes );
    writer.Write( offset );

    offset += e.BytesInRes;
}
 
writer.Flush();
```

And finally we load each individual image's data and append it to the output.

```csharp
foreach( var e in entries )
{
    using( var imgData = new Win32ResourceStream( module,
        e.Id, Win32ResourceType.Icon ) )
    {
        if( imgData.Length != e.BytesInRes )
            throw new InvalidDataException();

        imgData.CopyTo( ret );
    }
}
```

And that's it. We just rewind our output stream and return it to finish.

```csharp
ret.Position = 0;

return ret;
```