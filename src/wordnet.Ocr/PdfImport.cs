using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Rendering.Skia;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Ocr;

internal static class PdfImport
{
    internal static DocumentModel Read(Stream input, ConversionContext context)
    {
        try { return ReadCore(input, context); }
        catch (PdfDocumentFormatException error)
        { throw new InvalidDataException("Pdf.Structure: Invalid PDF structure.", error); }
    }

    private static DocumentModel ReadCore(Stream input, ConversionContext context)
    {
        using PdfDocument pdf = PdfDocument.Open(input, SkiaRenderingParsingOptions.Instance);
        if (pdf.IsEncrypted) throw new NotSupportedException("Pdf.Encryption: Encrypted PDFs are unsupported.");
        if (pdf.NumberOfPages > context.Options.MaximumPages) throw new InvalidDataException("Limit.Pages: PDF exceeds the page limit.");
        context.Loss("Pdf.Reconstruction", "PDF reading order is reconstructed into editable paragraphs; original page layout and vector graphics are not retained.");
        var result = new DocumentModel(); int characters = 0, canonicalCharacters = 0, paragraphCount = 0; bool rendererAdded = false;
        PdfImportOptions reconstruction = (context.Options.PdfImport ?? throw new ArgumentException("PDF input options must not be null.")).Snapshot();
        if (reconstruction.ReconstructLayout && pdf.TryGetForm(out _))
        { context.Report("Pdf.ReconstructionAmbiguity", "PDF form fields use the existing content-order fallback; editable form semantics are not inferred.", DiagnosticSeverity.Warning); reconstruction.ReconstructLayout = false; }
        for (int p = 1; p <= pdf.NumberOfPages; p++)
        {
            context.CheckCancellation();
            var page = ReadPage(pdf, p);
            OcrRecognition? recognition = null;
            string? logicalText = PdfActualText.Read(page, context);
            string text = logicalText ?? ContentOrderTextExtractor.GetText(page);
            bool images = page.NumberOfImages > 0;
            var embeddedImages = page.GetImages().ToArray();
            bool scannedPage = embeddedImages.Any(i => i.BoundingBox.Width * i.BoundingBox.Height >= page.Width * page.Height * 0.5);
            if (images && scannedPage && string.IsNullOrWhiteSpace(text) && context.Options.EnglishOcr == null)
                throw new NotSupportedException("Ocr.Required: Configure local English OCR to import scanned PDF pages.");
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
                if (context.OcrMetadata.Enabled) { recognition = EnglishOcr.RecognizeWithMetadata(bitmap, options, context); text = recognition.Text; }
                else text = EnglishOcr.Recognize(bitmap, options, context);
            }
            else if (logicalText == null && reconstruction.ReconstructLayout && PdfReconstruction.Read(page, reconstruction, context) is IReadOnlyList<DocumentBlock> recovered)
            {
                Append(recovered); characters = checked(characters + text.Length); context.CheckCharacters(characters); continue;
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
                Append(blocks.OrderByDescending(b => b.Top).ThenBy(b => b.Left).Select(b => b.Paragraph));
                characters = checked(characters + text.Length); context.CheckCharacters(characters);
                continue;
            }
            characters = checked(characters + text.Length); context.CheckCharacters(characters);
            string canonical = text.Replace("\r\n", "\n").Replace('\r', '\n');
            DocumentModel content = TextFormat.Read(canonical, context);
            if (recognition != null)
                context.OcrPages.Add(OcrGeometry.Build(recognition.Tsv!, canonical, p, checked(canonicalCharacters + (paragraphCount > 0 ? 1 : 0)), page.Width, page.Height,
                    recognition.OriginalWidth, recognition.OriginalHeight, recognition.Width, recognition.Height, recognition.Transform, recognition.Confidence, recognition.Sequence, context));
            Append(content.Blocks);
        }
        return result;

        void Append(IEnumerable<DocumentBlock> blocks)
        {
            foreach (DocumentBlock block in blocks)
            {
                foreach (ParagraphBlock paragraph in DocumentModel.Expand(new[] { block }).OfType<ParagraphBlock>())
                { canonicalCharacters = checked(canonicalCharacters + paragraph.Text.Length + (paragraphCount > 0 ? 1 : 0)); paragraphCount++; }
                result.Blocks.Add(block);
            }
        }
    }

    private static UglyToad.PdfPig.Content.Page ReadPage(PdfDocument pdf, int page)
    {
        try { return pdf.GetPage(page); }
        // Malformed indirect font dictionaries/stream delimiters can currently make
        // PdfPig throw these implementation exceptions. Keep this guard at the provider
        // call so defects in our own mapping code are not mistaken for invalid input.
        catch (Exception error) when (error is NullReferenceException or IndexOutOfRangeException)
        { throw new InvalidDataException("Pdf.Resources: Invalid page resource or stream structure.", error); }
    }
}
