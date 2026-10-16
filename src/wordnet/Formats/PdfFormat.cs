using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;
using WordNet.Internal;
using WordNet.Layout;
using WordNet.Model;

namespace WordNet.Formats;

internal static class PdfFormat
{
    internal static void Write(DocumentModel model, Stream output, ConversionContext context)
    {
        PdfOptions options = (context.Options.Pdf ?? throw new ArgumentException("PDF options must not be null.")).Snapshot();
        var pages = FixedLayout.Create(model, context);
        using var buffer = new MemoryStream();
        byte[]? metadata = options.Conformance == PdfConformance.PdfA2b
            ? System.Text.Encoding.UTF8.GetBytes(PdfExportMetadata.Xmp(model, context.FieldTime).ToString(System.Xml.Linq.SaveOptions.DisableFormatting)) : null;
        using var builder = new PdfDocumentBuilder(buffer, false, PdfWriterType.Default, 1.7, new PdfExportTokenWriter(options.Compress, metadata));
        builder.IncludeDocumentInformation = true;
        builder.DocumentInformation.Title = model.Title;
        builder.DocumentInformation.Author = model.Author;
        builder.DocumentInformation.Producer = PdfExportMetadata.Producer;
        builder.DocumentInformation.CreationDate = context.FieldTime.UtcDateTime.ToString("'D:'yyyyMMddHHmmss'Z'", CultureInfo.InvariantCulture);
        builder.DocumentInformation.ModifiedDate = builder.DocumentInformation.CreationDate;
        if (options.OutlineHeadings) builder.Bookmarks = PdfExportMetadata.Outlines(pages);
        if (options.Conformance == PdfConformance.PdfA2b)
        {
            builder.ArchiveStandard = PdfAStandard.A2B;
        }
        var fonts = context.FontData.Select(bytes => builder.AddTrueTypeFont(bytes)).ToArray();
        foreach (FixedPage page in pages)
        {
            context.CheckCancellation();
            PdfPageBuilder target = builder.AddPage(page.Settings.Width, page.Settings.Height);
            foreach (PositionedText text in page.Text)
            {
                if (text.Image is DocumentImage image)
                {
                    var rectangle = new PdfRectangle(text.X, page.Settings.Height - text.Y, text.X + image.Width, page.Settings.Height - text.Y + image.Height);
                    if (image.Extension == "png") target.AddPng(image.Bytes, rectangle); else target.AddJpeg(image.Bytes, rectangle);
                    if (text.Link != null) target.AddLink(text.Link, rectangle);
                    if (image.AlternativeText.Length > 0) context.Loss("Pdf.ImageAlternativeText", "This PDF profile does not create accessibility tags for images.");
                }
                else if (text.Text.Length > 0)
                {
                    string color = text.Color ?? "000000";
                    target.SetTextAndFillColor(Convert.ToByte(color.Substring(0, 2), 16), Convert.ToByte(color.Substring(2, 2), 16), Convert.ToByte(color.Substring(4, 2), 16));
                    target.AddText(text.Text, text.Size, new PdfPoint(text.X, page.Settings.Height - text.Y), fonts[text.Font]);
                    if (text.Link != null)
                    {
                        var measured = target.MeasureText(text.Text, text.Size, new PdfPoint(text.X, page.Settings.Height - text.Y), fonts[text.Font]);
                        if (measured.Count > 0) target.AddLink(text.Link, new PdfRectangle(text.X, page.Settings.Height - text.Y - text.Size * 0.25,
                            measured[measured.Count - 1].EndBaseLine.X, page.Settings.Height - text.Y + text.Size));
                    }
                }
            }
        }
        byte[] bytes = builder.Build(); PdfIdentity.Normalize(bytes); output.Write(bytes, 0, bytes.Length);
    }

}
