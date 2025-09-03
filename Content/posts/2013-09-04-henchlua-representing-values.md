---
title: 'HenchLua: Representing Values'
time: 15:00
series: HenchLua
tags:
  - 'C#'
  - code
  - HenchLua
  - Lua
  - programming
---
## Values

Lua supports the following standard types:

  * Nil
  * Booleans
  * Numbers (doubles)
  * Strings (that is, _byte_-strings)
  * Tables
  * UserData (arbitrary object references)
  * Functions (both Lua functions and callable user code)
  * Threads (coroutine state)

Similarly to .NET types, that list can be split into reference and value types. Nil, booleans, and numbers are true value types, while strings behave like value types due to their immutability. The rest are reference types. Now, since any variable may be of any type, and since that type may change dynamically, the backing storage for values needs to accept all of the above types.

### Values in Standard Lua

In standard Lua this is done using a tagged union, which looks something like this (not a direct cut from the Lua source, I've evaluated some of the macros and typedefs and reformatted for clarity - this may also come out differently on other architectures):

```C
struct lua_TValue
{
    union
    {
        struct
        {
            union Value
            {
                GCObject *gc;    /* collectable objects */
                void *p;         /* light userdata */
                int b;           /* booleans */
                lua_CFunction f; /* light C functions */
            } v__;
            int tt__;
        } i;
        double d__;
    } u;
};
```

Alright, so let's make sense of this. A Lua value is a union of a double (`d__`, for storing number values) and a struct (`i`) which is a combination of fields for storing all the other possible types of values (`v__`) and a field (`tt__`) which keeps track of the actual value.

The `tt__` field's position in the `i` struct and its values are all carefully chosen such that the useful number values and non-number types are distinguishable (if you try to read a non-number as a number you'll see some kind of NaN, and the Lua VM asserts on arithmetic operations that produce NaNs).

This makes `lua_TValue` eight bytes long, which is a wonderfully efficient state of affairs.

### Values in HenchLua

Unfortunately, there's no way to match the 8-byte value type in C# without boxing (which is counter to one of the primary design goals - being nice to the GC). So what can be done? A naive approach would be to create a struct with a field for each of the value types and a field of type `object` for the reference types, along with yet another field to act as the equivalent of `tt__`.

```csharp
public enum ValueType
{
    // ...
}
 
public struct Value
{
    private object asRefType;
    private double asNumber;
    private bool asBool;
    private ValueType type;
}
```

Unfortunately, that `Value` type would be somewhere around twenty bytes long (alignment, padding - let's not even bring up x64). That's unacceptably large.

My first attempt to reduce `Value`'s size was the [FieldOffset attribute](http://msdn.microsoft.com/en-us/library/system.runtime.interopservices.fieldoffsetattribute.aspx), which is the obvious way to implement unions in C#. I didn't have much success with that approach. For one thing, the `object` field _cannot_ be overlaid over the other fields (just imagine the havoc it would play with the GC), so all I have to play with are `asNumber`, `asBool`, and `type`. While that does indeed bring our struct size down to twelve bytes (which happens to be optimal), it's brittle since I can't actually put the `type` field where I want it on all platforms - there's no way to dynamically compute the offset values to account for differences in endianness and alignment between architectures, and there goes the goal of working on all sorts of .NET runtimes.

So I took a step back and looked at the fields one at a time. The first thing that struck me was that `asBool` could easily be replaced by reinterpreting `asNumber` - zero is false, one is true. The annoying thing about that was that I'd constantly be loading and testing an 8-byte register when there's really only one bit's worth of data around.

But these tests would always come after testing `type`. So the first change I made was to split `ValueType.Bool` into `ValueType.True` and `ValueType.False` (naturally this could be hidden behind a public interface that only exposes a `Bool` enumerant, to keep things simple for external code).

After that change, all that was left to eliminate was the `type` field. I already knew that overlaying it over `asNumber` wouldn't work, so all that left was somehow overlaying it over `asRefType`. Sentinel object instances to the rescue:

```csharp
public struct Value
{
    internal object RefVal;
    internal double NumVal;
 
    internal static readonly object NumTypeTag = new object();
}
 
internal sealed class BoolBox
{
    public readonly bool Value;
    private BoolBox( bool value ) { Value = value; }
 
    public static readonly BoolBox True = new BoolBox( true );
    public static readonly BoolBox False = new BoolBox( false );
}
```

The final semantics are fairly straight-forward. `RefVal` always carries the type information. For reference types, the already existing .NET type info is sufficient to identify the actual value. For true value types, I either use a preallocated and immutable boxed value (for booleans), or I use the sentinel value `Value.NumTypeTag` (which tells us that the actual value is in the `NumVal` field). And `null` obviously means `nil`.

## Strings

Lua strings are byte arrays. That's unfortunate, because it means the standard `System.String` type can't be used directly. So, first step in writing a custom type is gathering requirements. Lua strings are:

  * Byte arrays - they can readily contain embedded zeroes
  * Immutable
  * Very often used as keys to a hashtable
  * Sometimes used to hold large blocks of data
  * Reference types

So our implementation needs to be compact and quick to compare. The naive approach follows:

```csharp
class LString
{
    private byte[] data;
 
    //cache the hash code to keep things snappy
    private int hashCode;
}
```

Unfortunately, due to how ubiquitous strings are in Lua, this type violates some of our fundamental requirements. First, it's actually _two_ objects, and that wastes memory since each object has some overhead. Second, it adds a level of indirection: when we need the string data (say, to compare values) we first need to load the `LString` object and _only then_ can we read the `data` object. That's up to two cache misses where there should only be one, which is relevant in a tight loop like the one at the heart of the GC's marking phase.

Fortunately, while strings are reference types, their immutability makes them behave like value types, which allows us to expose the public interface through a struct, with no loss of clarity, while internally handling the data as a byte array:

```csharp
struct LString
{
    //the first four bytes contain the hash of the remaining data
    internal byte[] InternalData;
 
    public bool IsNil { get { return InternalData == null; } }
    public int Length
    {
        get
        {
            return InternalData != null ? InternalData.Length - 4 : 0;
        }
    }
 
    // ...
}
```

When constructing a `Value` which contains a string, we don't box the `LString` struct, we just grab the `InternalData` field directly. A `RefVal` of type `byte[]` is understood to mean string.

But what if the user gives us a `byte[]` as user data? This is probably a rare case (relative to how ubiquitous strings are in Lua), so we handle it by allocating a small proxy object around the user data. This is hidden from the user, so the library interface stays simple.

A small aside: I had originally named the type `String`, which was fine and worked well inside the `HenchLua` namespace. However, outside that namespace, it was constantly conflicting with `System.String`, and after the fourth or fifth time I wrote `using LString = HenchLua.String;` I decided to just rename the type for my sanity's sake.

## Callables

Callable Lua objects are represented using any of the following types:

```csharp
public abstract class Function;
internal class Proto : Function;
internal class Closure : Function;
public abstract class UserFunction : Function;
 
public delegate int UserCallback( Thread thread );
```

That last one, the delegate, complicates things for us. In all the other cases, it would be enough to treat types derived from `Function` specially. However, delegates are too useful to ignore (particularly since they can be cleanly constructed around lambdas, and anonymous and static methods).

I use a trick similar to the `LString` struct to keep things clear. The `Callable` struct wraps either a `Function` or a `UserCallback` in the public interface, while the raw object values are passed around internally without any boxed or proxy objects being in the way.

## Table

And, finally, this brings me to Lua's core structured type: the table. I'm not going to go into too much detail on the underlying algorithms here, as `Table` is a fairly direct port of Lua's implementation. If you're curious, either the `Table` code or Lua's `luaH_*` functions will tell you everything you need to know, though `Table.cs` might be easier for someone new to Lua to understand since nothing is buried in macros.

Table mainly differs from the standard Lua implementation in its interface. Since tables are ordinary .NET objects, with no reliance on any sort of global state, there's no need to hide them behind Lua's cumbersome stack interface. Raw access is directly exposed as an indexer. (Non-raw access requires an execution context, in case metamethods need to be called, and must therefore be done through a `Thread` object.)

The only interesting implementation detail is the way table members are stored. The thing with tables is that their storage is often rather sparse (especially in the hashtable part), and using full `Value` types would be wasteful. Tables instead use the internal `CompactValue` type, which works like `Value` except that they don't have a separate `NumVal` field. Instead, numbers are boxed.

One thing to note, however, is that they aren't boxed using the standard .NET boxing mechanism. The reason for this is that I wanted to reuse the boxes when possible, to keep allocation pressure low, and the standard boxes are impossible to efficiently mutate in C#.

This is the main exception to the "don't allocate where standard Lua wouldn't" rule. Lua can allocate memory when setting nil fields to non-nil values. HenchLua can, in addition, allocate when setting non-number fields to number values. However updating a number value won't allocate additional boxes. That compromise was made to save memory and to make the GC run a little faster when scanning a table's fields, and I think it's a net win since, in my experience, the type of a table element doesn't often change dynamically in common Lua code.