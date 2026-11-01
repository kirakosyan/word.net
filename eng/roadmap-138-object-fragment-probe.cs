using System;
using System.IO;
using System.Linq;
using WordNet;

if (args.Length != 2) throw new ArgumentException("Expected source DOCX and a new output DOCX path.");
string path = Path.GetFullPath(args[1]);
if (File.Exists(path)) throw new IOException("Choose a new output path.");
var options = new ConversionOptions(FidelityMode.Lenient);
using var input = File.OpenRead(args[0]);
WordDocument source = WordDocument.Load(input, DocumentFormat.Docx, out _, options);
var target = WordDocument.Create(options);
target.AddParagraphs(new[] { "Before", "Insert", "After" });
target.AddBookmark("place", 1, 0, 6);
target.InsertFragmentAtBookmark("place", source);
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
Console.WriteLine("Created one edited synthetic chart/equation fragment DOCX.");
