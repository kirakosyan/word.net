// Probe independently authored DOCX workflows against one explicitly selected wordnet build.
// The caller supplies an isolated project referencing either the release source or public package.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using WordNet;

if (args.Length != 2) throw new ArgumentException("Expected fixture and output directories.");
string fixtures = Path.GetFullPath(args[0]), destination = Path.GetFullPath(args[1]);
Directory.CreateDirectory(destination);
var options = new ConversionOptions(FidelityMode.Lenient);
string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
WordDocument Load(string name, bool review = false)
{
    using var input = File.OpenRead(Path.Combine(fixtures, name + ".docx"));
    return review
        ? WordDocument.Load(input, DocumentFormat.Docx, new ReviewLoadOptions(), out _, options)
        : WordDocument.Load(input, DocumentFormat.Docx, out _, options);
}
byte[] Save(WordDocument doc, DocumentFormat format)
{
    using var output = new MemoryStream(); doc.Save(output, format); return output.ToArray();
}
object Fragment(string targetName, string sourceName)
{
    WordDocument target = Load(targetName), source = Load(sourceName);
    byte[] beforeTarget = Save(target, DocumentFormat.Docx), beforeSource = Save(source, DocumentFormat.Docx);
    string outcome, message;
    try { target.InsertFragmentAtBookmark("place", source); outcome = "accepted"; message = ""; }
    catch (Exception error) { outcome = error.GetType().Name; message = error.Message; }
    return new {
        target = targetName, source = sourceName, outcome, message,
        sourceDiagnostics = source.Diagnostics.Select(d => new { d.Code, severity = d.Severity.ToString() }).ToArray(),
        targetUnchanged = beforeTarget.SequenceEqual(Save(target, DocumentFormat.Docx)),
        sourceUnchanged = beforeSource.SequenceEqual(Save(source, DocumentFormat.Docx)),
    };
}
WordDocument reviewed = Load("review-source", review: true);
byte[] reviewBefore = Save(reviewed, DocumentFormat.Docx);
using var rejectedOutput = new MemoryStream(); rejectedOutput.WriteByte(42);
string reviewOutcome, reviewMessage;
try { reviewed.Save(rejectedOutput, DocumentFormat.Pdf); reviewOutcome = "accepted"; reviewMessage = ""; }
catch (Exception error) { reviewOutcome = error.GetType().Name; reviewMessage = error.Message; }
WordDocument accepted = reviewed.CreateReviewView(ReviewDecision.Accept);
WordDocument rejected = reviewed.CreateReviewView(ReviewDecision.Reject);
byte[] acceptedPdf = Save(accepted, DocumentFormat.Pdf), rejectedPdf = Save(rejected, DocumentFormat.Pdf);
File.WriteAllBytes(Path.Combine(destination, "review-accepted.pdf"), acceptedPdf);
File.WriteAllBytes(Path.Combine(destination, "review-rejected.pdf"), rejectedPdf);
var output = new {
    assembly = typeof(WordDocument).Assembly.FullName,
    assemblySha256 = Hash(File.ReadAllBytes(typeof(WordDocument).Assembly.Location)),
    inputs = Directory.GetFiles(fixtures, "*.docx").OrderBy(p => p, StringComparer.Ordinal)
        .ToDictionary(p => Path.GetFileName(p)!, p => Hash(File.ReadAllBytes(p))),
    fragments = new[] { Fragment("fragment-body-target", "fragment-field-source"), Fragment("fragment-cell-target", "fragment-plain-source") },
    review = new {
        revisions = reviewed.Revisions.Count, outcome = reviewOutcome, message = reviewMessage,
        outputUntouched = rejectedOutput.ToArray().SequenceEqual(new byte[] { 42 }),
        sourceUnchanged = reviewBefore.SequenceEqual(Save(reviewed, DocumentFormat.Docx)),
        acceptedText = accepted.Paragraphs.Select(p => p.Text).ToArray(), rejectedText = rejected.Paragraphs.Select(p => p.Text).ToArray(),
        acceptedPdfSha256 = Hash(acceptedPdf), rejectedPdfSha256 = Hash(rejectedPdf),
    },
};
Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
