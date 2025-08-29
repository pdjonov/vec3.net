---
id: 618
title: Windowed Fullscreen
date: 2012-04-29T14:52:57-07:00
author: phill
guid: http://vec3.ca/?p=618
permalink: /posts/windowed-fullscreen
categories:
  - code
  - graphics
  - Windows
tags:
  - 'C#'
  - code
  - graphics
  - Win32
  - Windows
---
Windowed (fake) fullscreen is probably my favorite graphics option ever when it comes to PC games. It lets me have my nice fullscreen game, but doesn't lock me out of using my other monitor, and any programs running behind the game are an _instant_ ALT+TAB away. Games that can go from fully windowed to fake-fullscreened in an instant are also super cool, and not all that difficult to write. So how does one implement such a thing?

## Creating the Window

Let's start off with a game running in a regular window. We start off by making an ordinary window and then setting up our 3D context.

One note, before we get to the code - we're going to be changing the window border styles as we go, and that means that the thickness of the borders will change. Since our content is what really matters, the window position will be specified in terms of the _client rectangle_ and later adjusted as necessary.

```c++
//pick out our window styles
 
DWORD style = WS_OVERLAPPED | WS_SYSMENU |
    WS_BORDER | WS_THICKFRAME | WS_CAPTION |
    WS_MAXIMIZEBOX | WS_MINIMIZEBOX;
DWORD ex_style = WS_EX_APPWINDOW;
 
//pick out our desired client rect coordinates
//these are relative to the desktop rectangle
 
int left = CW_USEDEFAULT;
int top = CW_USEDEFAULT;
int width = 800;
int height = 600;
 
//convert the client rectangle into a window
//rectangle for CreateWindow
 
RECT rc = { 0, 0, 200, 200 };
AdjustWindowRectEx( &rc, style, FALSE, ex_style );
 
if( left != CW_USEDEFAULT )
    left += rc.left;
if( top != CW_USEDEFAULT )
    top += rc.top;
 
if( width != CW_USEDEFAULT )
    width += (rc.right - rc.left) - 200;
if( height != CW_USEDEFAULT )
    height += (rc.bottom - rc.top) - 200;
 
//create the window
 
HWND hwnd = CreateWindowEx( ex_style, _T( "MyWindowClassName" ),
    _T( "Sample Window" ), style, left, top, width, height,
    NULL, NULL, GetModuleHandle( NULL ), NULL );
 
ShowWindow( hwhd, SW_SHOW );
```

## Transitioning to Fullscreen

The first thing we need to do before transitioning into fullscreen mode, is we need to save our window's position, so that we can restore to that position when we transition back out:

```c++
RECT saved_pos;
bool is_fullscreen = false;
 
//in our to-fullscreen function
 
if( is_fullscreen )
    //already fullscreen, nothing more to do
    return;
 
GetClientRect( hwnd, &saved_pos );
 
POINT pt = { 0, 0 };
ClientToScreen( hwnd, &pt );
 
saved_pos.left = pt.x;
saved_pos.top = pt.y;
```

Next up, we need to find a rectangle that covers the monitor we're going to go fullscreen on. In this case, I'm going to take the monitor that the window is on (or mostly on). You could just as easily ask DXGI to hand you the desktop rectangle associated with whatever adapter you'd like to render on, or use some other API.

```c++
HMONITOR target_monitor = MonitorFromWindow( hwnd,
    MONITOR_DEFAULTTONEAREST );
 
MONITORINFO info;
info.cpSize = sizeof( MONITORINFO );
GetMonitorInfo( monitor, &info );
 
RECT dest_pos = info.rcMonitor;
```

Once we have our target rectangle, we're ready to make the transition. We get rid of the window's borders and move it so that it covers the entire target rectangle:

```c++
DWORD style = WS_POPUP;
DWORD ex_style = WS_EX_APPWINDOW;
 
//future-proofing in case MS fiddles with the meaning
//of "no borders, please"
 
RECT rc = dest_pos;
AdjustWindowRect( &rc, style, FALSE, ex_style );
 
//update the styles
 
if( IsWindowVisible( hwnd ) )
    //important: odd bugs arise otherwise
    style |= WS_VISIBLE;
 
SetWindowLong( hwnd, GWL_STYLE, style );
SetWindowLong( hwnd, GWL_EXSTYLE, ex_style );
 
//move the window
 
SetWindowPos( hwnd, NULL, rc.left, rc.top, rc.right - rc.left,
    rc.bottom - rc.top, SWP_NOACTIVATE | SWP_NOZORDER |
    SWP_FRAMECHANGED );
 
//and note the new state
 
is_fullscreen = true;
```

And there we are! Our window is now fullscreen. One small problem - the start bar covers it, as do any desktop toolbar apps, and we don't want that. So let's change the last bit:

```c++
SetWindowPos( hwnd, HWND_TOPMOST, rc.left, rc.top,
    rc.right - rc.left, rc.bottom - rc.top,
    SWP_NOACTIVATE | SWP_FRAMECHANGED );
```

OK, better, except that now we can't ALT+TAB to other programs. Well, we can, and they become active, but we can't see them if they're underneath our window because they can't be brought up on top of it. We fix this by giving up our topmost status whenever our window loses focus and taking it back when focus returns. Somewhere in the window's message procedure:

```c++
case WM_ACTIVATE:
    if( is_fullscreen )
    {
        SetWindowPos( hwnd, LOWORD( wParam ) != WA_INACTIVE ?
            HWND_TOPMOST : HWND_NOTTOPMOST, 0, 0, 0, 0,
            SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE );
    }
    break;
```

## Transitioning Back

Transitioning back to windowed mode is also straightforward, we simply put our old window border back, move the window to its original location, give up our topmost status, and carry on as usual:

```c++
//in our to-windowed function
 
if( !is_fullscreen )
    //already windowed, nothing more to do
    return;
 
DWORD style = WS_OVERLAPPED | WS_SYSMENU |
    WS_BORDER | WS_THICKFRAME | WS_CAPTION |
    WS_MAXIMIZEBOX | WS_MINIMIZEBOX;
DWORD ex_style = WS_EX_APPWINDOW;
 
RECT rc = saved_pos;
AdjustWindowRect( &rc, style, FALSE, ex_style );
 
//update the styles
 
if( IsWindowVisible( hwnd ) )
    style |= WS_VISIBLE;
 
SetWindowLong( hwnd, GWL_STYLE, style );
SetWindowLong( hwnd, GWL_EXSTYLE, ex_style );
 
//move the window
 
SetWindowPos( hwnd, HWND_NOTTOPMOST, rc.left, rc.top,
    rc.right - rc.left, rc.bottom - rc.top,
    SWP_NOACTIVATE | SWP_FRAMECHANGED );
 
is_fullscreen = false;
```

And there we are. Or are we?

## Handling Maximized Windows

The above code will work wonderfully in all cases _except_ when the window is maximized before transitioning into fullscreen. Handling that case is a bit trickier, since we not only have to save the window's old position, but also its old pre-maximized position so that it restores properly after switching to fullscreen and back. Thankfully, it's not hard to get and set this info all at once. We need to modify the first bit of transitioning to fullscreen as follows:

```c++
union
{
    RECT rc;
    WINDOWPLACEMENT placement;
} saved_pos;
 
bool is_fullscreen = false;
bool saved_as_placement;
 
//in our to-fullscreen function
 
if( is_fullscreen )
    //already fullscreen, nothing more to do
    return;
 
saved_as_placement = IsZoomed( hwnd );
if( saved_as_placement )
{
    saved_pos.placement.length = sizeof( WINDOWPLACEMENT );
    GetWindowPlacement( hwnd, &saved_pos.placement );
}
else
{
    GetClientRect( hwnd, &saved_pos.rc );
 
    POINT pt = { 0, 0 };
    ClientToScreen( hwnd, &pt );
 
    saved_pos.rc.left = pt.x;
    saved_pos.rc.top = pt.y;
}
```

And restoring back to windowed mode becomes this:

```c++
//in our to-windowed function
 
if( !is_fullscreen )
    //already windowed, nothing more to do
    return;
 
DWORD style = WS_OVERLAPPED | WS_SYSMENU |
    WS_BORDER | WS_THICKFRAME | WS_CAPTION |
    WS_MAXIMIZEBOX | WS_MINIMIZEBOX;
DWORD ex_style = WS_EX_APPWINDOW;
 
//update the styles
 
if( IsWindowVisible( hwnd ) )
    style |= WS_VISIBLE;
 
SetWindowLong( hwnd, GWL_STYLE, style );
SetWindowLong( hwnd, GWL_EXSTYLE, ex_style );
 
if( saved_as_placement )
{
    SetWindowPlacement( hwnd, &saved_pos.placement );
 
    SetWindowPos( hwnd, HWND_NOTTOPMOST, 0, 0, 0, 0,
        SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE |
        SWP_FRAMECHANGED );
}
else
{
    RECT rc = saved_pos.rc;
    AdjustWindowRect( &rc, style, FALSE, ex_style );
 
    //move the window
 
    SetWindowPos( hwnd, HWND_NOTTOPMOST, rc.left, rc.top,
        rc.right - rc.left, rc.bottom - rc.top,
        SWP_NOACTIVATE | SWP_FRAMECHANGED );
}
 
is_fullscreen = false;
```

Why do we use the two different save modes? According to MSDN, `WINDOWPLACEMENT` is supposed to contain everything there is to know about the window's location on the desktop, we should be able to get away with always just saving that...right? Well, no. If you do that, then, for whatever reason, the window won't play well with Windows 7's `WIN+<ARROW>` shortcuts. Don't ask me why, I haven't got a clue.