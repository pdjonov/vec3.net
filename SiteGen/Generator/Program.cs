using System;

using Vec3.Site.Generator;

var proj = await Project.Load(Environment.CurrentDirectory);
var output = await proj.GenerateOutput();

var verbose = false;

foreach (var e in output.Entries)
{
	if (!verbose && e.Action == OutputLayout.Action.Unchanged)
		continue;

	var (color, prefix) = e.Action switch
	{
		OutputLayout.Action.Added => (ConsoleColor.Green, 'A'),
		OutputLayout.Action.Updated => (ConsoleColor.Blue, 'U'),
		OutputLayout.Action.Deleted => (ConsoleColor.Red, 'R'),
		_ => (ConsoleColor.Gray, '-'),
	};

	Console.ForegroundColor = color;

	if (e.Source != null)
		Console.WriteLine($"{prefix} {e.Path} << {e.Source.Origin}");
	else
		Console.WriteLine($"{prefix} {e.Path}");
}

Console.ResetColor();
Console.WriteLine($"Done! {output.Entries.Count} files and directories touched");