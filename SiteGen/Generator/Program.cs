using System;
using System.Linq;

using Vec3.Site.Generator;

var proj = await Project.Load(Environment.CurrentDirectory);
var output = await proj.GenerateOutput();

foreach (var (path, source) in output.Items.OrderBy(it => it.Key))
	Console.WriteLine($"{path} << {source.GetType().Name} (from {source.Origin})");
