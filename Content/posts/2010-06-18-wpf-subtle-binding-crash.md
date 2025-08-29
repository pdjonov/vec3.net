---
id: 169
title: "WPF: Subtle Binding Crash"
date: 2010-06-18T14:56:06-07:00
author: phill
guid: http://vec3.ca/?p=169
permalink: /posts/wpf-subtle-binding-crash
categories:
  - code
  - WPF
tags:
  - bugs
  - code
---
Got a crash in, of all things, `System.Windows.Controls.Primitives.Popup.OnWindowResize(Object sender, AutoResizedEventArgs e)`. A `NullReferenceException`, to be precise. Ages later, it turns out that this error was actually caused by a binding operation on one of the controls in the popup failing because it was trying to instantiate itself as `TwoWay` with a read-only source property. Somehow, the binding error managed to turn into the `NullReferenceException` in the layout pass...

Just throwing that out there for anyone that might be seeing something similarly awful.