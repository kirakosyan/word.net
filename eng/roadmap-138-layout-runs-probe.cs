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
using var input = File.OpenRead(args[0]);
var preview = converter.RenderPages(input, DocumentFormat.Docx, new PreviewOptions { Layout = new LayoutSnapshotOptions { IncludeText = true } });
var snapshot = preview.Layout ?? throw new InvalidOperationException("Expected a layout snapshot.");
var payload = snapshot.Fragments.Select(item => new { page = item.Page, order = item.Order, text = item.Text,
    part = item.Source?.Part, path = item.Source?.Path, cellPart = item.CellSource?.Part, cellPath = item.CellSource?.Path,
    runPart = item.RunSource?.Part, runPath = item.RunSource?.Path, reason = item.UnavailableReason, outside = item.ExtendsOutsidePage,
    bounds = item.Bounds == null ? null : new { x = item.Bounds.X, y = item.Bounds.Y, width = item.Bounds.Width, height = item.Bounds.Height } });
File.WriteAllText(Path.Combine(directory, "snapshot.json"), JsonSerializer.Serialize(payload));
foreach (PagePreview page in preview.Pages) File.WriteAllBytes(Path.Combine(directory, "page-" + page.SourcePage + ".svg"), page.GetBytes());
input.Position = 0;
using var pdf = File.Create(Path.Combine(directory, "output.pdf"));
converter.Convert(input, pdf, DocumentFormat.Docx, DocumentFormat.Pdf);
Console.WriteLine("Produced the preview, source-run snapshot and matching PDF.");
