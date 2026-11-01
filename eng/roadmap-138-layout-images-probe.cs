using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using WordNet;

if (args.Length != 2) throw new ArgumentException("Expected a source DOCX and a new output directory.");
string directory = Path.GetFullPath(args[1]);
if (Directory.Exists(directory)) throw new IOException("Choose a new output directory.");
Directory.CreateDirectory(directory);
var converter = new WordConverter();
var options = new ConversionOptions(FidelityMode.BestEffort);
using var input = File.OpenRead(args[0]);
var preview = converter.RenderPages(input, DocumentFormat.Docx, new PreviewOptions { Layout = new LayoutSnapshotOptions() }, options);
var snapshot = preview.Layout ?? throw new InvalidOperationException("Expected a layout snapshot.");
var payload = snapshot.Fragments.Select(item => new { page = item.Page, text = item.Text,
    part = item.Source?.Part, path = item.Source?.Path, objectPart = item.ObjectSource?.Part, objectPath = item.ObjectSource?.Path,
    cellPath = item.CellSource?.Path, reason = item.UnavailableReason, outside = item.ExtendsOutsidePage,
    bounds = item.Bounds == null ? null : new { x = item.Bounds.X, y = item.Bounds.Y, width = item.Bounds.Width, height = item.Bounds.Height } });
File.WriteAllText(Path.Combine(directory, "snapshot.json"), JsonSerializer.Serialize(payload));
File.WriteAllBytes(Path.Combine(directory, "preview.svg"), preview.Pages.Single().GetBytes());
input.Position = 0;
using var pdf = File.Create(Path.Combine(directory, "output.pdf"));
converter.Convert(input, pdf, DocumentFormat.Docx, DocumentFormat.Pdf, options);
Console.WriteLine("Produced the inline-image preview, its snapshot and a matching PDF.");
