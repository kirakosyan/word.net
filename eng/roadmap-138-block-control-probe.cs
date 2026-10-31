using System;
using System.IO;
using WordNet;

if (args.Length != 1) throw new ArgumentException("Expected a new output DOCX path.");
string path = Path.GetFullPath(args[0]);
if (File.Exists(path)) throw new IOException("Choose a new output path.");
var document = WordDocument.Create();
document.AddParagraphs(new[] { "", "", "", "", "", "" });
var items = new[] { new ContentControlListItem("Alpha", "a"), new ContentControlListItem("Beta", "b") };
var definitions = new[] {
    new ContentControlDefinition(ContentControlKind.PlainText, initialText: "Ada"),
    new ContentControlDefinition(ContentControlKind.Checkbox, isChecked: true),
    new ContentControlDefinition(ContentControlKind.DropDownList, items: items, selectedValue: "b"),
    new ContentControlDefinition(ContentControlKind.ComboBox, items: items, initialText: "Manual"),
    new ContentControlDefinition(ContentControlKind.Date, dateValue: ContentControlDateValue.FromInstant(new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero))),
    new ContentControlDefinition(ContentControlKind.RichText, initialText: "Approved")
};
for (int i = 0; i < definitions.Length; i++) _ = document.InsertBlockContentControl(i, definitions[i]);
Directory.CreateDirectory(Path.GetDirectoryName(path)!);
using var output = File.Create(path);
document.Save(output, DocumentFormat.Docx);
Console.WriteLine("Created six typed block controls in ordinary body paragraphs.");
