using System;
using System.Linq;
using Vec3.Site.Generator;

var engine = new TemplatingEngine(Environment.CurrentDirectory);

var home = await engine.GenerateContent("index.cshtml");
Console.WriteLine(home.Single().GetOutput());