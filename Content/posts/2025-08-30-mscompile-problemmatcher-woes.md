---
title: "$msCompile problemMatcher Woes"
tags:
  - vscode
---
I don't know _why_ no one bothers to keep `"problemMatcher": "$msCompile"` working as tools like `dotnet` and `MSBuild` evolve, but here we are. Every now and then an SDK update breaks the problem matcher, making it harder to get .NET work done in VS Code.

(A friend tells me, "no one's using `dotnet` on any platform but Windows + Visual Studio". That makes me sad if it's true.)

_Anyway_, I finally got annoyed enough waiting for _someone else_ to fix vscode that I figured out how to work around the issue on my end. I'm sharing the result here so I can easily find it in the future, and in case it's vaguely useful to anybody else.

```json
"problemMatcher": {
	"base": "$msCompile",
	"pattern":[
		{
			"regexp": "^\\s*(?:\\s*\\d+>)?(\\S.*)\\((\\d+|\\d+,\\d+|\\d+,\\d+,\\d+,\\d+)\\)\\s*:\\s+((?:fatal +)?error|warning|info)\\s+(\\w+\\d+)\\s*:\\s*(.*)$",
			"kind": "location",
			"file": 1,
			"location": 2,
			"severity": 3,
			"code": 4,
			"message": 5
		}
	]
},
```

The `regexp` is the one from the current (as of writing) [vscode source](https://github.com/microsoft/vscode/blob/614287f64de39de2b2b2b9b185867187a150d0bb/src/vs/workbench/contrib/tasks/common/problemMatcher.ts#L1500) with just a `\s*` jammed in at the front (because `dotnet` build errors are now indented when they didn't used to be, I guess).

I opened a ticket to let them know about the leading spaces. Who knows if anything will come of it, but now at least I don't have to care if they don't. (Well, until _the next_ SDK update which breaks _that_ regex...)

Now if only the vscode workspace JSON allowed me to define that matcher _once_ and reference it in multiple tasks instead of having to copy-paste it throughout the file. Oh well.