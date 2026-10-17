using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics.Operations.ClippingPaths;
using UglyToad.PdfPig.Graphics.Operations.General;
using UglyToad.PdfPig.Graphics.Operations.PathConstruction;
using UglyToad.PdfPig.Graphics.Operations.PathPainting;
using UglyToad.PdfPig.Graphics.Operations.SpecialGraphicsState;
using UglyToad.PdfPig.Outline.Destinations;
using UglyToad.PdfPig.Writer;
using WordNet.Internal;
using WordNet.Layout;
using WordNet.Model;

namespace WordNet.Formats;

internal static class PdfFormat
{
    internal static void Write(DocumentModel model, Stream output, ConversionContext context, IReadOnlyList<FixedPage>? prepared = null, IReadOnlyList<PageRange>? selection = null)
    {
        PdfOptions options = (context.Options.Pdf ?? throw new ArgumentException("PDF options must not be null.")).Snapshot();
        var allPages = prepared ?? FixedLayout.Create(model, context);
        int[] indices = PageSelection.Resolve(selection ?? options.Pages.ToArray(), allPages.Count);
        var pages = indices.Select(p => allPages[p - 1]).ToList();
        var pageMap = indices.Select((p, i) => (p, i)).ToDictionary(pair => pair.p, pair => pair.i + 1);
        context.PageCount = pages.Count;
        using var buffer = new MemoryStream();
        byte[]? metadata = options.Conformance == PdfConformance.PdfA2b
            ? System.Text.Encoding.UTF8.GetBytes(PdfExportMetadata.Xmp(model, context.FieldTime).ToString(System.Xml.Linq.SaveOptions.DisableFormatting)) : null;
        using var builder = new PdfDocumentBuilder(buffer, false, PdfWriterType.Default, 1.7, new PdfExportTokenWriter(options.Compress, metadata,
            indices.Where((p, i) => p != i + 1).Any() || pages.Count != allPages.Count ? pages.Select(p => ListCounter.Number(p.PageNumber, p.SourceSettings.PageNumberFormat)).ToArray() : null));
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
            foreach (FixedShape shape in FixedShape.PaintOrder(page.Shapes))
            {
                byte r = Convert.ToByte(shape.Color.Substring(0, 2), 16), g = Convert.ToByte(shape.Color.Substring(2, 2), 16), b = Convert.ToByte(shape.Color.Substring(4, 2), 16);
                if (shape.Stroke == 0)
                {
                    target.SetTextAndFillColor(r, g, b);
                    target.CurrentStream.Operations.Add(new AppendRectangle(shape.X, page.Settings.Height - shape.Y - shape.Height, shape.Width, shape.Height));
                    target.CurrentStream.Operations.Add(FillPathNonZeroWinding.Value);
                }
                else
                {
                    target.SetStrokeColor(r, g, b);
                    target.CurrentStream.Operations.Add(new SetLineDashPattern(shape.Style == "dashed" ? new[] { shape.Stroke * 3, shape.Stroke * 2 }
                        : shape.Style == "dotted" ? new[] { shape.Stroke, shape.Stroke } : Array.Empty<double>(), 0));
                    bool vertical = shape.Width == 0;
                    double offset = shape.Style == "double" ? shape.Stroke / 3 : 0;
                    foreach (double displacement in offset == 0 ? new[] { 0.0 } : new[] { -offset, offset })
                        target.DrawLine(new PdfPoint(shape.X + (vertical ? displacement : 0), page.Settings.Height - shape.Y + (vertical ? 0 : displacement)),
                            new PdfPoint(shape.X + shape.Width + (vertical ? displacement : 0), page.Settings.Height - shape.Y - shape.Height + (vertical ? 0 : displacement)),
                            offset == 0 ? shape.Stroke : shape.Stroke / 3);
                    target.CurrentStream.Operations.Add(new SetLineDashPattern(Array.Empty<double>(), 0));
                }
            }
            foreach (PositionedText text in page.Text.OrderBy(t => t.Image?.Anchor == null ? 0 : t.Image.Anchor.Behind ? -1 : 1))
            {
                if (text.Image is DocumentImage image)
                {
                    var rectangle = new PdfRectangle(text.X, page.Settings.Height - text.Y, text.X + image.Width, page.Settings.Height - text.Y + image.Height);
                    var operations = target.CurrentStream.Operations;
                    operations.Add(Push.Value);
                    double angle = image.Rotation * Math.PI / 180, cosine = Math.Round(Math.Cos(angle)), sine = Math.Round(Math.Sin(angle));
                    operations.Add(new ModifyCurrentTransformationMatrix(new[] { cosine, -sine, sine, cosine, text.X + image.Width / 2, page.Settings.Height - text.Y + image.Height / 2 }));
                    operations.Add(new ModifyCurrentTransformationMatrix(new[] { image.FlipHorizontal ? -1.0 : 1, 0, 0, image.FlipVertical ? -1.0 : 1, 0, 0 }));
                    operations.Add(new AppendRectangle(-image.FrameWidth / 2, -image.FrameHeight / 2, image.FrameWidth, image.FrameHeight));
                    operations.Add(ModifyClippingByNonZeroWindingIntersect.Value); operations.Add(EndPath.Value);
                    double fullWidth = image.FrameWidth / (1 - image.CropLeft - image.CropRight), fullHeight = image.FrameHeight / (1 - image.CropTop - image.CropBottom);
                    double left = -image.FrameWidth / 2 - fullWidth * image.CropLeft, bottom = -image.FrameHeight / 2 - fullHeight * image.CropBottom;
                    var source = new PdfRectangle(left, bottom, left + fullWidth, bottom + fullHeight);
                    if (image.Extension == "png") target.AddPng(image.Bytes, source); else target.AddJpeg(image.Bytes, source);
                    operations.Add(Pop.Value);
                    if (text.Link != null) target.AddLink(text.Link, rectangle);
                    if (image.AlternativeText.Length > 0 && !context.PreviewIntermediate) context.Loss("Pdf.ImageAlternativeText", "This PDF profile does not create accessibility tags for images.");
                }
                else if (text.Text.Length > 0)
                {
                    string color = text.Color ?? "000000";
                    target.SetTextAndFillColor(Convert.ToByte(color.Substring(0, 2), 16), Convert.ToByte(color.Substring(2, 2), 16), Convert.ToByte(color.Substring(4, 2), 16));
                    target.AddText(text.Text, text.Size, new PdfPoint(text.X, page.Settings.Height - text.Y), fonts[text.Font]);
                    if (text.Link != null)
                    {
                        var measured = target.MeasureText(text.Text, text.Size, new PdfPoint(text.X, page.Settings.Height - text.Y), fonts[text.Font]);
                        if (measured.Count > 0)
                        {
                            var area = new PdfRectangle(text.X, page.Settings.Height - text.Y - text.Size * 0.25, measured[measured.Count - 1].EndBaseLine.X, page.Settings.Height - text.Y + text.Size);
                            if ((text.Link.Length > 0 && text.Link[0] == '#'))
                            {
                                if (model.BookmarkPages.TryGetValue(text.Link.Substring(1), out var destination))
                                {
                                    if (pageMap.TryGetValue(destination.Page, out int mapped)) target.AddLink(new ExplicitDestination(mapped, ExplicitDestinationType.XyzCoordinates, new ExplicitDestinationCoordinates(destination.X, destination.Y)), area);
                                    else context.Loss("Pdf.OmittedDestination", "A hyperlink to an excluded physical page retains visible text without a destination.");
                                }
                            }
                            else target.AddLink(text.Link, area);
                        }
                    }
                }
            }
        }
        byte[] bytes = builder.Build(); PdfIdentity.Normalize(bytes); output.Write(bytes, 0, bytes.Length);
    }

}
