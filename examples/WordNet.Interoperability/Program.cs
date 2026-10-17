using System;
using System.IO;
using System.Text;
using WordNet;

string directory = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("artifacts/interoperability-sample");
Directory.CreateDirectory(directory);
var converter = new WordConverter();
using var source = new MemoryStream(Encoding.UTF8.GetBytes("<!doctype html><html><head><title>Invoice</title></head><body><h1>Invoice</h1><p>Amount due: <b>42.50</b></p><table><tr><th>Item</th><th>Amount</th></tr><tr><td>Service</td><td>42.50</td></tr></table></body></html>"));
WordDocument document = WordDocument.Load(source, DocumentFormat.Html, out _);
using (var docx = File.Create(Path.Combine(directory, "invoice.docx"))) document.Save(docx, DocumentFormat.Docx);
using (var rtf = File.Create(Path.Combine(directory, "invoice.rtf"))) document.Save(rtf, DocumentFormat.Rtf);
var options = new ConversionOptions(); options.Pdf.Accessibility = PdfAccessibility.PdfUa1; options.Pdf.Language = "en-US";
using (var pdf = File.Create(Path.Combine(directory, "invoice.pdf"))) document.Save(pdf, DocumentFormat.Pdf, options);
using var previewSource = File.OpenRead(Path.Combine(directory, "invoice.docx"));
DocumentPreview preview = converter.RenderPages(previewSource, DocumentFormat.Docx);
foreach (PagePreview page in preview.Pages) File.WriteAllBytes(Path.Combine(directory, "page-" + page.SourcePage + ".svg"), page.GetBytes());
Console.WriteLine("Editable HTML import, RTF, PDF/UA and managed outlined previews: " + directory);
