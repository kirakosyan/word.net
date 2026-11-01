// MIT probe for composition of created cell controls into an independently authored host.
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using WordNet;

string fixtures = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
if (Directory.Exists(output)) throw new IOException("Choose a new output directory.");
Directory.CreateDirectory(output);
string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
WordDocument Load(string name)
{
    using var source = File.OpenRead(Path.Combine(fixtures, name + ".docx"));
    return WordDocument.LoadWithControls(source, new ContentControlLoadOptions(), out _, new ConversionOptions(FidelityMode.Lenient));
}
byte[] Save(WordDocument document, DocumentFormat format, out string[] diagnostics)
{
    using var stream = new MemoryStream(); ConversionResult result = document.Save(stream, format);
    diagnostics = result.Diagnostics.Select(item => item.Code + ":" + item.Severity).ToArray(); return stream.ToArray();
}
WordDocument source = Load("source");
_ = source.InsertBlockContentControlInTableCell(1, new ContentControlDefinition(ContentControlKind.PlainText, initialText: "Alpha", tag: "plain-cell"));
_ = source.InsertBlockContentControlInTableCell(2, new ContentControlDefinition(ContentControlKind.RichText, initialText: "Bravo", tag: "rich-cell"));
byte[] sourceBefore = Save(source, DocumentFormat.Docx, out _);
WordDocument target = Load("body-target"); target.InsertFragmentAtBookmark("slot", source);
var beforeEdit = target.ContentControls.Select(item => new { item.Id, item.Tag, item.Owner, item.DisplayText }).OrderBy(item => item.Id).ToArray();
target.SetControlText(target.ContentControls.Single(item => item.Tag == "plain-cell"), "Edited");
byte[] result = Save(target, DocumentFormat.Docx, out string[] docxDiagnostics);
byte[] pdf = Save(target, DocumentFormat.Pdf, out string[] pdfDiagnostics);
File.WriteAllBytes(Path.Combine(output, "composed.docx"), result); File.WriteAllBytes(Path.Combine(output, "composed.pdf"), pdf);
File.WriteAllBytes(Path.Combine(output, "source.docx"), sourceBefore);
byte[] sourceAfter = Save(source, DocumentFormat.Docx, out _);
WordDocument unsupported = Load("cell-target"); byte[] before = Save(unsupported, DocumentFormat.Docx, out _);
string exception = "none", code = "none";
try { unsupported.InsertFragmentAtTableCellBookmark("slot", source); }
catch (Exception error) { exception = error.GetType().Name; code = error.Message.Split(':')[0]; }
byte[] after = Save(unsupported, DocumentFormat.Docx, out _);
var report = new
{
    inputHashes = Directory.GetFiles(fixtures, "*.docx").OrderBy(x => x, StringComparer.Ordinal)
        .ToDictionary(x => Path.GetFileName(x)!, x => Hash(File.ReadAllBytes(x))),
    assemblySha256 = Hash(File.ReadAllBytes(typeof(WordDocument).Assembly.Location)), beforeEdit,
    sourceUnchanged = sourceBefore.SequenceEqual(sourceAfter), exception, code, rejectedTargetUnchanged = before.SequenceEqual(after),
    outputs = new { source = Hash(sourceBefore), docx = Hash(result), pdf = Hash(pdf) }, docxDiagnostics, pdfDiagnostics
};
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine("Composed independently created cell controls and checked protected cell destination.");
