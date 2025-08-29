---
title: How to Make boost::function-like Templates
tags:
    - code
    - templates
notes:
    - type: warning
      date: 2025-07-19
      text: >
          This post was written before C++11 introduced variadic templates. The section about specializing for each arity should just read "use a parameter pack".
---
If you've dealt with Boost at all, you've certainly seen this at some point:

```c++
boost::function<int( int, float, const char* )> func = ... ;
int x = func( 4, 3.0F, "" );
```

So how is that done? How does the template "know" what the parameters are when instantiating the `operator()` method? You might have thought figuring this out would be a simple matter of poking around the boost header files, but you'd be wrong. They are, unfortunately, an incomprehensible mess of preprocessor madness. The reason for this will soon become apparent.

The thing to remember is that there is no magic at all to the above. Once you take the statement apart it becomes very easy to see what's going on. Starting inside the angly brackets:

```c++
int( int, float, const char* )
```

It doesn't look like one at first glance, but that's a type. To be precise, it is an unnamed function prototype with unnamed arguments, which returns an `int`. The original code could just as easily have been written:

```c++
typedef int madfunc( int x, float y, const char* );
boost::function<madfunc> func = ... ;
```

Note that `madfunc` is not a pointer to function. You could really never do anything with the type directly (my compiler lets me declare a variable of that type but I can't actually initialize it to anything - which makes sense), but it is something which can validly exist in C++ (and probably even C, though I'm too lazy to double-check right now). And since it is a type, you can use it in a template where a type is expected. And since you can do that, you can also use evil template specialization tricks like this one:

```c++
template< typename F >
class myfunc;
 
template< typename R >
class myfunc<R()>
{
    //...
};
 
template< typename R, typename P0 >
class myfunc<R( P0 )>
{
    //...
};
 
template< typename R, typename P0, typename P1 >
class myfunc<R( P0, P1 )>
{
    //...
};
 
//and so on
```

Yes. That actually works. The (reasonably modern) C++ compiler will actually match your template function to the correct instantiation, figure out all the types, etc, etc. (Kinda makes you feel sorry for the guy that has to write the compiler, no?)

Of course, you need a full specialization for each and every allowable arity (number of function parameters), so this would get tedious rather quickly. That's why the Boost headers are so incomprehensible. It really isn't a clever ploy to keep you from figuring out the magic, it's there to save them work. They have one header defining the entire range of `boost::function` values, which they include 50 times (or so) with a special macro set to values in the range 0-49, and the preprocessor diligently expands that out into the specialization for zero arguments, for one, for two, etc.