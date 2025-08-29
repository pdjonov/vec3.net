---
id: 650
title: Working With Win32 Resources in .NET
date: 2012-08-09T00:32:45-07:00
author: phill
guid: http://vec3.ca/?p=650
permalink: /posts/working-with-win32-resources-in-dot-net
categories:
  - code
  - Windows
tags:
  - 'C#'
  - code
  - programming
  - Windows
---
Most native applications make extensive use of Win32 resources. While the .NET Framework provides a far more useful resource API, it's sometimes necessary to access the old style Win32 resources. Fortunately, this isn't very difficult.

## The Win32 Resource API

The first thing we'll need is some P/Invoke to get at the resources. Let's start with that.

### The HINSTANCE

Windows refers to each loaded module (EXE or DLL) through a handle called an `HINSTANCE`. There are two ways to get one of these. If the target module is part of a loaded .NET assembly, then we can use [Marshal.GetHINSTANCE](http://msdn.microsoft.com/en-us/library/system.runtime.interopservices.marshal.gethinstance(v=vs.90)). Otherwise, we need to use [GetModuleHandle](http://msdn.microsoft.com/en-us/library/windows/desktop/ms683199(v=vs.85).aspx). It has the following P/Invoke signature:

```csharp
partial static class Native
{
    [DllImport( "kernel32.dll", CharSet = CharSet.Auto )]
    public static extern IntPtr GetModuleHandle( string modName );
}
```

### Opening the Resource

Win32 resources are identified by their type and ID. These parameters can be specified either as a string or as an integer. They are located using [FindResource](http://msdn.microsoft.com/en-us/library/windows/desktop/ms648042(v=vs.85).aspx), which we'll import several times since there's no other way to do pointer-casting tricks with P/Invoke.

```csharp
partial static class Native
{
    [DllImport( "kernel32.dll", CharSet = CharSet.Auto )]
    public static extern IntPtr FindResource( IntPtr hModule,
        string name, string type );
    [DllImport( "kernel32.dll", CharSet = CharSet.Auto )]
    public static extern IntPtr FindResource( IntPtr hModule,
        string name, IntPtr type );
    [DllImport( "kernel32.dll", CharSet = CharSet.Auto )]
    public static extern IntPtr FindResource( IntPtr hModule,
        IntPtr name, IntPtr type );
}
```

Yes, it's also legal to use an integer name and string type, but that's a less likely usage so we won't provide an overload for it. If necessary, it's always possible to pass an integer ID as a string by prepending its string representation with a hash sign (#).

### Getting the Actual Data

We need three functions to get the actual data. [SizeofResource](http://msdn.microsoft.com/en-us/library/windows/desktop/ms648048(v=vs.85).aspx) will tell us the size of our resource. [LoadResource](http://msdn.microsoft.com/en-us/library/windows/desktop/ms648046(v=vs.85).aspx) and [LockResource](http://msdn.microsoft.com/en-us/library/windows/desktop/ms648047(v=vs.85).aspx) are used together to get the actual data pointer:

```csharp
partial static class Native
{
    [DllImport( "kernel32.dll", ExactSpelling = true )]
    public static extern uint SizeofResource( IntPtr hModule,
        IntPtr hResInfo );
    [DllImport( "kernel32.dll", ExactSpelling = true )]
    public static extern IntPtr LoadResource( IntPtr hModule,
        IntPtr hRes );
    [DllImport( "kernel32.dll", ExactSpelling = true )]
    public static extern IntPtr LockResource( IntPtr hRes );
}
```

## Wrapping it All Up

These can wrapped up in a simple class, which I'll call `Win32ResourceStream`. We'll derive from [UnmanagedMemoryStream](http://msdn.microsoft.com/en-us/library/13e02eft(v=vs.90)) in order to make things simple:

```csharp
public class Win32ResourceStream : UnmanagedMemoryStream
{
    public Win32ResourceStream( Module managedModule,
        string resName, string resType );
    public Win32ResourceStream( string moduleName,
        string resName, string resType );
 
    private IntPtr GetModuleHandle( string name );
    private IntPtr GetModuleHandle( Module module );
 
    protected void Initialize( IntPtr hModule,
        string resName, string resType );
    protected unsafe void Initialize(
        IntPtr hModule, IntPtr hResource );
}
```

The implementation is pretty simple. We'll start with the constructors, which get the necessary `HINSTANCE` value and pass it along to `Initialize`, which will do the rest of the work. There's also a bit of validation going on in the `GetModuleHandle` implementations.

```csharp
public Win32ResourceStream( Module managedModule,
    string resName, string resType )
{
    Initialize( GetModuleHandle( managedModule ),
        resName, resType );
}
 
public Win32ResourceStream( string moduleName,
    string resName, string resType )
{
    Initialize( GetModuleHandle( moduleName ),
        resName, resType );
}
 
private IntPtr GetModuleHandle( string name )
{
    if( name == null )
        throw new ArgumentNullException( name );
 
    var hModule = Native.GetModuleHandle( name );
    if( hModule == IntPtr.Zero )
        throw new FileNotFoundException();
 
    return hModule;
}
 
private IntPtr GetModuleHandle( Module module )
{
    if( module == null )
        throw new ArgumentNullException( "module" );
 
    var hModule = Marshal.GetHINSTANCE( module );
    if( hModule == (IntPtr)(-1) )
        throw new ArgumentException( "Module has no HINSTANCE." );
 
    return hModule;
}
```

And that just leaves us with `Initialize`. This one isn't hard. We just validate our parameters and call the necessary functions in sequence, checking for errors as we go:

```csharp
protected void Initialize( IntPtr hModule,
    string resName, string resType )
{
    if( hModule == IntPtr.Zero )
        throw new ArgumentNullException( "hModule" );
 
    if( resName == null )
        throw new ArgumentNullException( "resName" );
    if( resType == null )
        throw new ArgumentNullException( "resType" );
 
    var hRes = Native.FindResource( hModule, resName, resType );
    Initialize( hModule, hRes );
}
 
protected unsafe void Initialize( IntPtr hModule, IntPtr hRes )
{
    if( hModule == IntPtr.Zero || hRes == IntPtr.Zero )
        throw new FileNotFoundException();
 
    var size = Native.SizeofResource( hModule, hRes );
    var hResData = Native.LoadResource( hModule, hRes );
    var pResData = Native.LockResource( hResData );
 
    Initialize( (byte*)pResData, size, size, FileAccess.Read );
}
```

### Cleanup

There isn't any! Windows cleans everything up when the module is unloaded. Just take care that you don't unload the module before you're finished with the stream.

### Standard Type IDs and a Convenient Overload

The built-in resource types have predefined IDs. These IDs are 16-bit integers which are passed in place of a type name by casting them to a string pointer (or `IntPtr`, in our case). We can stick these in an enumeration to make things more convenient when working with the built-in types:

```csharp
public enum Win32ResourceType : ushort
{
    Accelerator = 9,
    AnimatedCursor = 21,
    AnimatedIcon = 22,
    Bitmap = 2,
    Cursor = 1,
    Dialog = 5,
    Font = 8,
    FontDir = 7,
    GroupCursor = 12,
    GroupIcon = 14,
    Icon = 3,
    Html = 23,
    Menu = 4,
    Manifest = 24,
    MessageTable = 11,
    UserData = 10,
    String = 6,
    Version = 16,
    PlugAndPlay = 19,
}
```

And then we can add some overloads to make it easy to use the enumeration:

```csharp
public Win32ResourceStream( Module managedModule,
    string resName, Win32ResourceType resType )
{
    Initialize( GetModuleHandle( managedModule ),
        resName, (ushort)resType );
}
 
public Win32ResourceStream( string moduleName,
    string resName, ushort resType )
{
    Initialize( GetModuleHandle( moduleName ),
        resName, resType );
}
 
protected void Initialize( IntPtr hModule,
    string resName, ushort resType )
{
    if( hModule == IntPtr.Zero )
        throw new ArgumentNullException( "hModule" );
 
    if( resName == null )
        throw new ArgumentNullException( "resName" );
 
    var hRes = Native.FindResource( hModule,
        resName, (IntPtr)resType );
    Initialize( hModule, hRes );
}
```

### Opening Resources by Integer ID

We'll add one more overload to make it easy to open resources by integer ID.

```csharp
public Win32ResourceStream( Module managedModule,
    ushort resId, Win32ResourceType resType )
{
    Initialize( GetModuleHandle( managedModule ),
        resId, (ushort)resType );
}
 
public Win32ResourceStream( string moduleName,
    ushort resId, ushort resType )
{
    Initialize( GetModuleHandle( moduleName ),
        resId, resType );
}
 
protected void Initialize( IntPtr hModule,
    ushort resId, ushort resType )
{
    if( hModule == IntPtr.Zero )
        throw new ArgumentNullException( "hModule" );
 
    var hRes = Native.FindResource( hModule,
        (IntPtr)resId, (IntPtr)resType );
    Initialize( hModule, hRes );
}
```

And that's that!