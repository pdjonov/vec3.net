using System;
using System.Linq;

using Vec3.Site.Generator.Content;
using Vec3.Site.Generator.Templates;


var contentDirectory = Environment.CurrentDirectory;

var input = await InputItems.Load(contentDirectory);
var output = await Output.Generate(contentDirectory, input);

foreach (var (path, source) in output.Items.OrderBy(it => it.Key))
	Console.WriteLine($"{path} << {source} (from {source.GetType().Name} at {source.Origin})");