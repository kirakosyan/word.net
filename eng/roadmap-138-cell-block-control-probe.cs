// MIT probe for independently authored simple-cell block-control fixtures.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using WordNet;

string fixtures = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
if (Directory.Exists(output)) throw new IOException("Choose a new output directory.");
Directory.CreateDirectory(output);
string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
WordDocument Load(string path)
{
    using var source = File.OpenRead(path);
    return WordDocument.LoadWithControls(source, new ContentControlLoadOptions(), out _, new ConversionOptions(FidelityMode.Lenient));
}
byte[] Save(WordDocument document, DocumentFormat format, out string[] diagnostics)
{
    using var stream = new MemoryStream();
    ConversionResult result = document.Save(stream, format);
    diagnostics = result.Diagnostics.Select(item => item.Code + ":" + item.Severity).ToArray();
    return stream.ToArray();
}
byte[] Record(WordDocument document, DocumentFormat format, string name, out string[] diagnostics)
{
    byte[] bytes = Save(document, format, out diagnostics); File.WriteAllBytes(Path.Combine(output, name), bytes); return bytes;
}
var items = new[] { new ContentControlListItem("Alpha", "a"), new ContentControlListItem("Beta", "b") };
var definitions = new[] {
    new ContentControlDefinition(ContentControlKind.PlainText, initialText: "Ada"),
    new ContentControlDefinition(ContentControlKind.Checkbox, isChecked: true),
    new ContentControlDefinition(ContentControlKind.DropDownList, items: items, selectedValue: "b"),
    new ContentControlDefinition(ContentControlKind.ComboBox, items: items, initialText: "Manual"),
    new ContentControlDefinition(ContentControlKind.Date, dateValue: ContentControlDateValue.FromInstant(new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero))),
    new ContentControlDefinition(ContentControlKind.RichText, initialText: "Approved")
};
WordDocument document = Load(Path.Combine(fixtures, "plain.docx"));
for (int i = 0; i < definitions.Length; i++) _ = document.InsertBlockContentControlInTableCell(i + 1, definitions[i]);
byte[] created = Record(document, DocumentFormat.Docx, "created.docx", out string[] createdDiagnostics);
byte[] createdPdf = Record(document, DocumentFormat.Pdf, "created.pdf", out string[] createdPdfDiagnostics);
WordDocument edited = Load(Path.Combine(output, "created.docx"));
DocumentContentControl ById(int id) => edited.ContentControls.Single(item => item.Id == id);
edited.SetControlText(ById(1), "Grace"); edited.SetControlChecked(ById(2), false); edited.SetControlSelection(ById(3), "a");
edited.SetControlComboText(ById(4), "New");
edited.SetControlDate(ById(5), ContentControlDateValue.FromInstant(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero)));
edited.RemoveControl(ById(6));
byte[] changed = Record(edited, DocumentFormat.Docx, "edited.docx", out string[] editedDiagnostics);
byte[] editedPdf = Record(edited, DocumentFormat.Pdf, "edited.pdf", out string[] editedPdfDiagnostics);
var rejected = new Dictionary<string, object>();
foreach (string name in new[] { "styled", "merged", "nested" })
{
    WordDocument negative = Load(Path.Combine(fixtures, name + ".docx"));
    byte[] before = Save(negative, DocumentFormat.Docx, out _); string exception = "none", code = "none";
    try { _ = negative.InsertBlockContentControlInTableCell(0, definitions[0]); }
    catch (Exception error) { exception = error.GetType().Name; code = error.Message.Split(':')[0]; }
    byte[] after = Save(negative, DocumentFormat.Docx, out _);
    rejected.Add(name, new { exception, code, unchanged = before.SequenceEqual(after) });
}
var report = new
{
    inputHashes = Directory.GetFiles(fixtures, "*.docx").OrderBy(x => x, StringComparer.Ordinal)
        .ToDictionary(x => Path.GetFileName(x)!, x => Hash(File.ReadAllBytes(x))),
    assemblySha256 = Hash(File.ReadAllBytes(typeof(WordDocument).Assembly.Location)),
    outputs = new { createdDocx = Hash(created), createdPdf = Hash(createdPdf), editedDocx = Hash(changed), editedPdf = Hash(editedPdf) },
    diagnostics = new { created = createdDiagnostics, createdPdf = createdPdfDiagnostics, edited = editedDiagnostics, editedPdf = editedPdfDiagnostics },
    retainedControlCount = edited.ContentControls.Count, rejected
};
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine("Captured six created cell controls, editable output and protected-owner rejections.");
