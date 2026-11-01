// MIT synthetic baseline probe; compile independently against release source and public packages.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using WordNet;

string inputs = args[0], output = args[1];
if (Directory.Exists(output)) throw new IOException("Output already exists.");
Directory.CreateDirectory(output);
string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
WordDocument Load(string name)
{
    using var stream = File.OpenRead(Path.Combine(inputs, name + ".docx"));
    return WordDocument.Load(stream, DocumentFormat.Docx, out _);
}
byte[] Save(WordDocument document, DocumentFormat format)
{
    using var stream = new MemoryStream();
    document.Save(stream, format);
    return stream.ToArray();
}
string Record(string name, byte[] data)
{
    File.WriteAllBytes(Path.Combine(output, name), data);
    return Hash(data);
}
string Code(Exception error) => error.Message.Split(':')[0];

WordDocument host = Load("target"), field = Load("field-fragment");
byte[] hostBefore = Save(host, DocumentFormat.Docx), fieldBefore = Save(field, DocumentFormat.Docx);
string fragmentException = "none", fragmentCode = "none";
try { host.InsertFragmentAtBookmark("slot", field); }
catch (Exception error) { fragmentException = error.GetType().Name; fragmentCode = Code(error); }
byte[] hostAfter = Save(host, DocumentFormat.Docx), fieldAfter = Save(field, DocumentFormat.Docx);
WordDocument positiveHost = Load("target"), plain = Load("plain-fragment");
positiveHost.InsertFragmentAtBookmark("slot", plain);
string fragmentDocx = Record("fragment.docx", Save(positiveHost, DocumentFormat.Docx));
string fragmentPdf = Record("fragment.pdf", Save(positiveHost, DocumentFormat.Pdf));

WordDocument review;
using (var stream = File.OpenRead(Path.Combine(inputs, "review.docx")))
    review = WordDocument.Load(stream, DocumentFormat.Docx, new ReviewLoadOptions(), out _);
byte[] original = Save(review, DocumentFormat.Docx);
using var destination = new MemoryStream();
byte[] sentinel = { 1, 2, 3, 4, 5 };
destination.Write(sentinel); destination.Position = 0;
string reviewException = "none", reviewCode = "none";
try { review.Save(destination, DocumentFormat.Pdf); }
catch (Exception error) { reviewException = error.GetType().Name; reviewCode = Code(error); }
bool untouched = sentinel.SequenceEqual(destination.ToArray());
WordDocument accepted = review.CreateReviewView(ReviewDecision.Accept);
WordDocument rejected = review.CreateReviewView(ReviewDecision.Reject);
string acceptedDocx = Record("accepted.docx", Save(accepted, DocumentFormat.Docx));
string rejectedDocx = Record("rejected.docx", Save(rejected, DocumentFormat.Docx));
string acceptedPdf = Record("accepted.pdf", Save(accepted, DocumentFormat.Pdf));
string rejectedPdf = Record("rejected.pdf", Save(rejected, DocumentFormat.Pdf));
byte[] originalAfter = Save(review, DocumentFormat.Docx);
var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name is "wordnet" or "wordnet.Fonts")
    .OrderBy(a => a.GetName().Name, StringComparer.Ordinal)
    .ToDictionary(a => a.GetName().Name!, a => new { version = a.GetName().Version!.ToString(), sha256 = Hash(File.ReadAllBytes(a.Location)) });
var report = new
{
    inputHashes = Directory.GetFiles(inputs, "*.docx").OrderBy(x => x, StringComparer.Ordinal)
        .ToDictionary(x => Path.GetFileName(x)!, x => Hash(File.ReadAllBytes(x))),
    assemblies,
    fragment = new { fragmentException, fragmentCode, hostUnchanged = hostBefore.SequenceEqual(hostAfter),
        fragmentUnchanged = fieldBefore.SequenceEqual(fieldAfter), outputDocxSha256 = fragmentDocx, outputPdfSha256 = fragmentPdf },
    review = new { reviewException, reviewCode, outputUntouched = untouched, originalUnchanged = original.SequenceEqual(originalAfter),
        retainedRevisions = review.Revisions.Count, acceptedDocxSha256 = acceptedDocx, rejectedDocxSha256 = rejectedDocx,
        acceptedPdfSha256 = acceptedPdf, rejectedPdfSha256 = rejectedPdf },
};
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine("Synthetic workflow observations captured.");
