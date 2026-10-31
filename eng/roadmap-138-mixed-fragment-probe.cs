using System;
using System.IO;
using System.Linq;
using WordNet;

if (args.Length != 3) throw new ArgumentException("Expected a bound-control DOCX, chart/equation DOCX and new output DOCX path.");
string path = Path.GetFullPath(args[2]);
if (File.Exists(path)) throw new IOException("Choose a new output path.");
var options = new ConversionOptions(FidelityMode.Lenient);
using var boundInput = File.OpenRead(args[0]);
using var objectInput = File.OpenRead(args[1]);
WordDocument bound = WordDocument.LoadWithControls(boundInput, out _, options);
WordDocument objects = WordDocument.Load(objectInput, DocumentFormat.Docx, out _, options);
var mixed = WordDocument.Create(options);
mixed.AddParagraphs(new[] { "Control", "Objects" });
mixed.AddBookmark("control", 0, 0, 7);
mixed.AddBookmark("objects", 1, 0, 7);
mixed.InsertFragmentAtBookmark("control", bound);
mixed.InsertFragmentAtBookmark("objects", objects);
var target = WordDocument.Create(options);
target.AddParagraphs(new[] { "Before", "Insert", "After" });
target.AddBookmark("place", 1, 0, 6);
target.InsertFragmentAtBookmark("place", mixed);
target.ApplyControlBindings(target.ContentControls);
target.SetControlText(target.ContentControls[0], "Bea");
target.WriteBackControlBindings(target.ContentControls);
target.UpdateChart(target.Objects.Single(item => item.Chart != null),
    new ChartDefinition(ChartKind.ClusteredColumn, new[] { "Q1", "Q2" },
        new[] { new ChartSeries("Sales", "2468AC", new double?[] { 10, 20 }) }, "Quarterly"),
    description: "Quarterly sales");
target.UpdateEquation(target.Objects.Single(item => item.Equation != null),
    EquationExpression.Fraction(EquationExpression.Text("x"), EquationExpression.Text("4")),
    description: "x over four");
Directory.CreateDirectory(Path.GetDirectoryName(path)!);
using var output = File.Create(path);
target.Save(output, DocumentFormat.Docx);
Console.WriteLine("Created one mixed bound-control/chart/equation fragment with independent edits.");
