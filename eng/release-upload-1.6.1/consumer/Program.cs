using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WordNet;

const string text = "Public wordnet 1.6.1 verified.";
var converter = new WordConverter();
using var input = new MemoryStream(Encoding.UTF8.GetBytes(text));
using var docx = new MemoryStream();
converter.Convert(input, docx, DocumentFormat.Text, DocumentFormat.Docx, new ConversionOptions(), CancellationToken.None);
docx.Position = 0;
using var pdf = new MemoryStream();
converter.Convert(docx, pdf, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(), CancellationToken.None);
pdf.Position = 0;
using var imported = new MemoryStream();
converter.Convert(pdf, imported, DocumentFormat.Pdf, DocumentFormat.Docx, new ConversionOptions(FidelityMode.BestEffort), CancellationToken.None);
imported.Position = 0;
using var recovered = new MemoryStream();
converter.Convert(imported, recovered, DocumentFormat.Docx, DocumentFormat.Text, new ConversionOptions(FidelityMode.BestEffort), CancellationToken.None);
string actual = Encoding.UTF8.GetString(recovered.ToArray());
if (!actual.Contains(text, StringComparison.Ordinal)) throw new Exception("Public package round-trip text mismatch: " + actual);
var hashes = new Dictionary<string, string>();
foreach (string name in new[] { "wordnet", "wordnet.Fonts", "wordnet.Ocr" })
{
    string path = Path.Combine(AppContext.BaseDirectory, name + ".dll");
    hashes[name] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
Console.WriteLine(JsonSerializer.Serialize(new { runtime = Environment.Version.ToString(), passed = true, text = actual.Trim(), binaries = hashes }));
