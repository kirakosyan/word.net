using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Rendering.Skia;
using UglyToad.PdfPig.Writer;
using WordNet.Internal;
using WordNet.Layout;
using WordNet.Model;
using WordNet.Ocr;

namespace WordNet.Formats;

internal static class PdfFormat
{
    internal static void Write(DocumentModel model, Stream output, ConversionContext context)
    {
        var pages = FixedLayout.Create(model, context);
        using var builder = new PdfDocumentBuilder();
        var fonts = Enumerable.Range(0, 4).Select(i => builder.AddTrueTypeFont(FixedLayout.FontBytes(i))).ToArray();
        foreach (FixedPage page in pages)
        {
            context.CheckCancellation();
            PdfPageBuilder target = builder.AddPage(FixedLayout.Width, FixedLayout.Height);
            foreach (PositionedText text in page.Text)
            {
                if (text.Image is DocumentImage image)
                {
                    var rectangle = new PdfRectangle(text.X, FixedLayout.Height - text.Y, text.X + image.Width, FixedLayout.Height - text.Y + image.Height);
                    if (image.Extension == "png") target.AddPng(image.Bytes, rectangle); else target.AddJpeg(image.Bytes, rectangle);
                    if (image.AlternativeText.Length > 0) context.Loss("Pdf.ImageAlternativeText", "This PDF profile does not create accessibility tags for images.");
                }
                else if (text.Text.Length > 0)
                {
                    string color = text.Color ?? "000000";
                    target.SetTextAndFillColor(Convert.ToByte(color.Substring(0, 2), 16), Convert.ToByte(color.Substring(2, 2), 16), Convert.ToByte(color.Substring(4, 2), 16));
                    target.AddText(text.Text, text.Size, new PdfPoint(text.X, FixedLayout.Height - text.Y), fonts[text.Font]);
                }
            }
        }
        byte[] bytes = builder.Build(); output.Write(bytes, 0, bytes.Length);
    }

    internal static DocumentModel Read(Stream input, ConversionContext context)
    {
        using PdfDocument pdf = PdfDocument.Open(input, SkiaRenderingParsingOptions.Instance);
        if (pdf.IsEncrypted) throw new NotSupportedException("Pdf.Encryption: Encrypted PDFs are unsupported.");
        if (pdf.NumberOfPages > context.Options.MaximumPages) throw new InvalidDataException("Limit.Pages: PDF exceeds the page limit.");
        context.Loss("Pdf.Reconstruction", "PDF reading order is reconstructed into editable paragraphs; original page layout and vector graphics are not retained.");
        var result = new DocumentModel(); int characters = 0; bool rendererAdded = false;
        for (int p = 1; p <= pdf.NumberOfPages; p++)
        {
            context.CheckCancellation();
            var page = pdf.GetPage(p);
            string text = ContentOrderTextExtractor.GetText(page);
            bool images = page.NumberOfImages > 0;
            var embeddedImages = page.GetImages().ToArray();
            bool scannedPage = embeddedImages.Any(i => i.BoundingBox.Width * i.BoundingBox.Height >= page.Width * page.Height * 0.5);
            if (images && context.Options.EnglishOcr != null && (scannedPage || string.IsNullOrWhiteSpace(text)))
            {
                context.Loss("Pdf.OcrImageLoss", "OCR replaces the rendered page with editable text; embedded images are not retained.");
                if (!string.IsNullOrWhiteSpace(text))
                    context.Loss("Pdf.OcrTextReplacement", "A large image triggered whole-page OCR; existing digital text is replaced by recognition results.");
                EnglishOcrOptions options = context.Options.EnglishOcr;
                double scale = options.ResolutionDpi / 72.0;
                double pixels = Math.Ceiling(page.Width * scale) * Math.Ceiling(page.Height * scale);
                if (double.IsNaN(pixels) || double.IsInfinity(pixels) || pixels <= 0 || pixels > options.MaximumImagePixels) throw new InvalidDataException("Limit.ImagePixels: PDF raster exceeds the pixel limit.");
                long embeddedPixels = 0;
                foreach (var embedded in page.GetImages())
                {
                    embeddedPixels = checked(embeddedPixels + (long)embedded.WidthInSamples * embedded.HeightInSamples);
                    if (embeddedPixels > options.MaximumImagePixels) throw new InvalidDataException("Limit.ImagePixels: Embedded PDF images exceed the pixel limit.");
                }
                if (!rendererAdded) { pdf.AddSkiaPageFactory(); rendererAdded = true; }
                using SKBitmap bitmap = pdf.GetPageAsSKBitmap(p, (float)scale, SKColors.White);
                text = EnglishOcr.Recognize(bitmap, options, context);
            }
            else if (images)
            {
                if (scannedPage && string.IsNullOrWhiteSpace(text)) throw new NotSupportedException("Ocr.Required: Configure local English OCR to import scanned PDF pages.");
                var blocks = new List<(double Top, double Left, ParagraphBlock Paragraph)>();
                foreach (var line in page.Letters.GroupBy(l => Math.Round(l.StartBaseLine.Y, 1)).OrderByDescending(g => g.Key))
                {
                    var paragraph = new ParagraphBlock();
                    double? previous = null;
                    foreach (var letter in line.OrderBy(l => l.StartBaseLine.X))
                    {
                        if (previous.HasValue && letter.StartBaseLine.X - previous.Value > letter.PointSize * 0.25) paragraph.Runs.Add(new TextRun(" "));
                        paragraph.Runs.Add(new TextRun(letter.Value)); previous = letter.EndBaseLine.X;
                    }
                    blocks.Add((line.Key, line.Min(l => l.StartBaseLine.X), paragraph));
                }
                foreach (var embedded in embeddedImages)
                {
                    context.ReserveImagePixels(embedded.WidthInSamples, embedded.HeightInSamples);
                    if (embedded.IsImageMask || embedded.MaskImage != null)
                        context.Loss("Pdf.ImageMask", "Image stencil/soft masks are not retained by the extraction profile.");
                    byte[] bytes;
                    if (embedded.RawMemory.Length >= 3 && embedded.RawMemory.Span[0] == 255 && embedded.RawMemory.Span[1] == 216 && embedded.RawMemory.Span[2] == 255)
                        bytes = embedded.RawMemory.ToArray();
                    else if (!embedded.TryGetPng(out bytes!)) throw new NotSupportedException("Pdf.ImageEncoding: Embedded image cannot be decoded to PNG or JPEG.");
                    DocumentImage image = DocumentImage.Read(bytes, embedded.BoundingBox.Width, embedded.BoundingBox.Height, "", context, embedded.WidthInSamples, embedded.HeightInSamples);
                    var paragraph = new ParagraphBlock(); paragraph.Runs.Add(new TextRun("", image: image));
                    blocks.Add((embedded.BoundingBox.Top, embedded.BoundingBox.Left, paragraph));
                }
                context.Loss("Pdf.ImagePlacement", "Images retain their displayed dimensions in reading-order paragraphs; original anchoring, clipping and transforms are not retained.");
                result.Blocks.AddRange(blocks.OrderByDescending(b => b.Top).ThenBy(b => b.Left).Select(b => b.Paragraph));
                characters = checked(characters + text.Length); context.CheckCharacters(characters);
                continue;
            }
            characters = checked(characters + text.Length); context.CheckCharacters(characters);
            DocumentModel content = TextFormat.Read(text.Replace("\r\n", "\n").Replace('\r', '\n'), context);
            result.Blocks.AddRange(content.Blocks);
        }
        return result;
    }
}
