using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using WordNet;

string outputDirectory = Path.GetFullPath(args.Length == 0 ? "artifacts/automation-sample" : args[0]);
Directory.CreateDirectory(outputDirectory);
var options = new ConversionOptions(FidelityMode.Lenient) { Clock = () => new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero) };

WordDocument cover = WordDocument.Create(options);
cover.Title = "Invoice package"; cover.Author = "Example Company";
cover.AddParagraph("Cover letter", new TextFormatting { Bold = true }, headingLevel: 1);
cover.AddParagraph("Please review the enclosed invoice.");
Export(cover, "cover");

// Independently authored OOXML: one repeated table row, an optional section and logo.
using var template = new MemoryStream(Template());
WordDocument invoice = WordDocument.Load(template, DocumentFormat.Docx, out ConversionResult loaded, options);
Report(loaded);
invoice.Merge(new
{
    Customer = "Example customer",
    Items = new[] { new { Description = "Consulting", Amount = 120.50m }, new { Description = "Support", Amount = 25m } },
    ShowTerms = true,
    Logo = new TemplateImage(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "logo.png")), 80, 40, "Example logo")
}, new TemplateOptions { Culture = CultureInfo.InvariantCulture });
invoice.Replace("Invoice", "Tax invoice");
Export(invoice, "invoice");

WordDocument appendix = WordDocument.Create(options);
appendix.AddParagraph("Appendix", headingLevel: 1); appendix.AddParagraph("Payment is due within 30 days.");
cover.Append(invoice); cover.Append(appendix);
Export(cover, "assembled");
Console.WriteLine("Saved editable DOCX and PDF files to " + outputDirectory);

void Export(WordDocument document, string name)
{
    foreach (DocumentFormat format in new[] { DocumentFormat.Docx, DocumentFormat.Pdf })
    {
        using Stream output = File.Create(Path.Combine(outputDirectory, name + (format == DocumentFormat.Docx ? ".docx" : ".pdf")));
        Report(document.Save(output, format));
    }
}
static void Report(ConversionResult result)
{ foreach (ConversionDiagnostic diagnostic in result.Diagnostics) Console.WriteLine(diagnostic.Code + ": " + diagnostic.Message); }
static byte[] Template()
{
    const string w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    const string r = "http://schemas.openxmlformats.org/package/2006/relationships";
    string body = P("Invoice") + "<w:p>" + Field("Customer") + "</w:p>" + Control("image:Logo", P("Logo"))
        + "<w:tbl><w:tblGrid><w:gridCol w:w='5000'/><w:gridCol w:w='2000'/></w:tblGrid><w:tr>" + Cell(P("Description")) + Cell(P("Amount")) + "</w:tr>"
        + Control("repeat:Items", "<w:tr>" + Cell("<w:p>" + Field("Description") + "</w:p>") + Cell("<w:p>" + Field("Amount \\# &quot;F2&quot;") + "</w:p>") + "</w:tr>")
        + "</w:tbl>" + Control("if:ShowTerms", P("Terms and conditions apply."));
    using var memory = new MemoryStream();
    using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
    {
        Add("[Content_Types].xml", "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/><Default Extension='xml' ContentType='application/xml'/><Override PartName='/word/document.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/></Types>");
        Add("_rels/.rels", "<Relationships xmlns='" + r + "'><Relationship Id='document' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='word/document.xml'/></Relationships>");
        Add("word/document.xml", "<w:document xmlns:w='" + w + "'><w:body>" + body + "</w:body></w:document>");
        void Add(string name, string text) { using Stream part = zip.CreateEntry(name).Open(); byte[] bytes = Encoding.UTF8.GetBytes(text); part.Write(bytes, 0, bytes.Length); }
    }
    return memory.ToArray();
    static string P(string text) => "<w:p><w:r><w:t>" + text + "</w:t></w:r></w:p>";
    static string Field(string path) => "<w:fldSimple w:instr='MERGEFIELD " + path + "'><w:r><w:t>cached</w:t></w:r></w:fldSimple>";
    static string Cell(string content) => "<w:tc>" + content + "</w:tc>";
    static string Control(string tag, string content) => "<w:sdt><w:sdtPr><w:tag w:val='" + tag + "'/></w:sdtPr><w:sdtContent>" + content + "</w:sdtContent></w:sdt>";
}
