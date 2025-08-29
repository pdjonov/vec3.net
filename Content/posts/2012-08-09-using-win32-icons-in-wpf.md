---
id: 644
title: Using Win32 Icons in WPF
date: 2012-08-09T01:13:14-07:00
author: phill
guid: http://vec3.ca/?p=644
permalink: /posts/using-win32-icons-in-wpf
categories:
  - code
  - Windows
  - WPF
tags:
  - 'C#'
  - code
  - programming
  - Windows
  - WPF
---
Using custom icons can be a little tricky in WPF. It's simple enough if you want to use your application's main icon or an icon file that you can refer to using a [pack URI](http://msdn.microsoft.com/en-us/library/aa970069.aspx) - so long as you do that, everything just works.

However, if your icon data is anywhere else, then things can get a little tricky.

## The Icon Property

It seems enough to just set a window's [Icon](http://msdn.microsoft.com/en-us/library/system.windows.window.icon.aspx) property to any old [ImageSource](http://msdn.microsoft.com/en-us/library/system.windows.media.imagesource.aspx) should be enough, and indeed that generally works.

However there's a snag. An `ImageSource` typically refers to just one image, whereas Windows requires two separate images. These images have different sizes, according to the current [system metrics](http://msdn.microsoft.com/en-us/library/windows/desktop/ms724385(v=vs.85).aspx). The larger one needs to be `SM_CXICON` by `SM_CYICON` pixels, and is used in the task-switcher dialog and on the Windows 7 task bar. The smaller one is `SM_CXSMICON` by `SM_CYSMICON`, and is used in the window's caption and on the task bar (in the preview thumbnails that pop up on Windows 7).

If you set the window's icon to a simple bitmap image, then WPF will simply scale it to the two sizes and pass those images to Windows. Unfortunately, images which work well at one size (usually 32 by 32) tend to look bad at the other (16 by 16). That's why Windows icon files have individually authored images for each size - the two images will be different, each created specifically with its size in mind. We can't do that by just throwing any old `ImageSource` at the `Icon` property.

And yet everything works fine if we set the property to a URI that refers to a windows icon file - the system will happily find the correct image in the icon data. So what does WPF do with that URI and how do we replicate it if we haven't got our image data in a URI-friendly location?

## The BitmapFrame Class

The trick is that when WPF decodes an icon, it returns a [BitmapFrame](http://msdn.microsoft.com/en-us/library/system.windows.media.imaging.bitmapframe.aspx) object. That object keeps [a reference](http://msdn.microsoft.com/en-us/library/system.windows.media.imaging.bitmapframe.decoder.aspx) back to the [decoder](http://msdn.microsoft.com/en-us/library/system.windows.media.imaging.bitmapdecoder.aspx) which parsed the icon file. When you set the `Icon` property to a `BitmapFrame`, WPF will go and look at the frame's decoder's [output](http://msdn.microsoft.com/en-us/library/system.windows.media.imaging.bitmapdecoder.frames.aspx) and see if that decoder found more than a single image in the source file. If it did, WPF will choose the two images from that set which best match the required resolution and color depth, scale those if they're not exact matches, and then pass those images to Windows.

So all we need to do is decode a multi-image file and pass one of the resulting images to the `Icon` property, and WPF will do the rest.

## Loading a Windows Icon From a Stream

The typical multi-image file format that's used for Windows icons is, unsurprisingly, the Windows Icon (.ico) format. Loading one of these is trivial. All you need to do is get your data into a `Stream`, and you can pass it to [IconBitmapDecoder](http://msdn.microsoft.com/en-us/library/system.windows.media.imaging.iconbitmapdecoder.aspx)'s constructor. Once the decoder is constructed, simply set the target window's `Icon` property to any one of the frames that the decoder loaded from the file:

```csharp
Stream icoData = //load the data from wherever it is

var ico = new IconBitmapDecoder( icoData, BitmapCreateOptions.None,
    BitmapCacheOption.Default );

window.Icon = ico.Frames[0];
```

## Loading From a Windows Resource

One of the more common places to find icon resources is embedded into [PE (executable)](http://en.wikipedia.org/wiki/Portable_Executable) files. Loading icon resources from these is a little tricky, since the icon's parts are split up into multiple resource entries, and `IconBitmapDecoder` can't handle that directly.

Fortunately, [we know to fix that](/posts/extracting-icons-from-pe-files). We simply load the icon resource into a `MemoryStream` using that code and pass the stream to `IconBitmapDecoder`.