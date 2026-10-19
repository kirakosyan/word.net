using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using WordNet.Internal;
using WordNet.Layout;
using WordNet.Model;

namespace WordNet.Formats;

internal static class XpsFormat
{
    internal static void Write(DocumentModel document, Stream output, ConversionContext context)
    {
        var pages = FixedLayout.Create(document, context);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, true);
        XNamespace xps = "http://schemas.microsoft.com/xps/2005/06";
        XNamespace rel = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
        Xml("[Content_Types].xml", new XElement(ct + "Types", new[] {
            new XElement(ct + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(ct + "Default", new XAttribute("Extension", "png"), new XAttribute("ContentType", "image/png")),
            new XElement(ct + "Default", new XAttribute("Extension", "jpg"), new XAttribute("ContentType", "image/jpeg")),
            new XElement(ct + "Default", new XAttribute("Extension", "ttf"), new XAttribute("ContentType", "application/vnd.ms-opentype")),
            new XElement(ct + "Default", new XAttribute("Extension", "fdseq"), new XAttribute("ContentType", "application/vnd.ms-package.xps-fixeddocumentsequence+xml")),
            new XElement(ct + "Default", new XAttribute("Extension", "fdoc"), new XAttribute("ContentType", "application/vnd.ms-package.xps-fixeddocument+xml")),
            new XElement(ct + "Default", new XAttribute("Extension", "fpage"), new XAttribute("ContentType", "application/vnd.ms-package.xps-fixedpage+xml")) }));
        Xml("_rels/.rels", new XElement(rel + "Relationships", new XElement(rel + "Relationship", new XAttribute("Id", "r1"), new XAttribute("Type", "http://schemas.microsoft.com/xps/2005/06/fixedrepresentation"), new XAttribute("Target", "FixedDocumentSequence.fdseq"))));
        Xml("FixedDocumentSequence.fdseq", new XElement(xps + "FixedDocumentSequence", new XElement(xps + "DocumentReference", new XAttribute("Source", "Documents/1/FixedDocument.fdoc"))));
        Xml("Documents/1/FixedDocument.fdoc", new XElement(xps + "FixedDocument", Enumerable.Range(1, pages.Count).Select(n => new XElement(xps + "PageContent", new XAttribute("Source", "Pages/" + n.ToString(CultureInfo.InvariantCulture) + ".fpage")))));
        for (int i = 0; i < context.FontData.Length; i++) PackageArchive.Write(archive, "Resources/Fonts/" + FontName(i), context.FontData[i]);
        var imageNames = new System.Collections.Generic.Dictionary<DocumentImage, string>();
        var imageDensities = new System.Collections.Generic.Dictionary<DocumentImage, (double X, double Y)>();
        foreach (DocumentImage image in pages.SelectMany(p => p.Text).Where(t => t.Image != null).Select(t => t.Image!).Distinct())
        {
            string imageName = "/Resources/Images/" + imageNames.Count.ToString(CultureInfo.InvariantCulture) + "." + image.Extension;
            imageNames.Add(image, imageName); PackageArchive.Write(archive, imageName.Substring(1), image.Bytes);
            imageDensities.Add(image, ManagedRaster.Density(image.Bytes, image.Extension));
        }
        for (int p = 0; p < pages.Count; p++)
        {
            context.CheckCancellation();
            string name = (p + 1).ToString(CultureInfo.InvariantCulture);
            Xml("Documents/1/Pages/" + name + ".fpage", new XElement(xps + "FixedPage", new XAttribute("Width", Number(pages[p].Settings.Width)), new XAttribute("Height", Number(pages[p].Settings.Height)), new XAttribute(XNamespace.Xml + "lang", "en-US"),
                FixedShape.PaintOrder(pages[p].Shapes).SelectMany(Shape),
                pages[p].Text.OrderBy(t => t.Image?.Anchor == null ? 0 : t.Image.Anchor.Behind ? -1 : 1).Where(t => t.Image != null || t.Text.Length != 0 || t.NativeShape != null)
                    .Select(t => t.Image != null ? Image(t, imageNames[t.Image]) : Text(t))));
            Xml("Documents/1/Pages/_rels/" + name + ".fpage.rels", new XElement(rel + "Relationships", Enumerable.Range(0, context.FontData.Length).Select(f => new XElement(rel + "Relationship", new XAttribute("Id", "f" + f.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("Type", "http://schemas.microsoft.com/xps/2005/06/required-resource"), new XAttribute("Target", "/Resources/Fonts/" + FontName(f))))
                .Concat(imageNames.Select((image, i) => new XElement(rel + "Relationship", new XAttribute("Id", "i" + i.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("Type", "http://schemas.microsoft.com/xps/2005/06/required-resource"), new XAttribute("Target", image.Value))))));
        }
        XElement Text(PositionedText t)
        {
            var element = new XElement(xps + "Glyphs", new XAttribute("FontUri", "/Resources/Fonts/" + FontName(t.Font)),
                new XAttribute("FontRenderingEmSize", Number(t.Size)), new XAttribute("OriginX", Number(t.X)), new XAttribute("OriginY", Number(t.Y)),
                new XAttribute("Fill", "#FF" + (t.Color ?? "000000")));
            if (t.NativeShape != null)
            {
                if (t.NativeShape.Glyphs.All(g => g.Glyph < 0))
                {
                    // Separate, invisible logical equation description; no invented visible glyphs.
                    element.Add(new XAttribute("Opacity", "0"), new XAttribute("UnicodeString", t.Text.Length > 0 && t.Text[0] == '{' ? "{}" + t.Text : t.Text));
                    return element;
                }
                string Metric(double n) => (n * 100 / t.NativeShape.Units).ToString("0.######", CultureInfo.InvariantCulture);
                element.Add(new XAttribute("Indices", string.Join(";", t.NativeShape.Glyphs.Select(g => g.Glyph.ToString(CultureInfo.InvariantCulture) + "," + Metric(g.Advance) + "," + Metric(g.X) + "," + Metric(g.Y)))));
            }
            if (t.Text.Length > 0) element.Add(new XAttribute("UnicodeString", t.Text[0] == '{' ? "{}" + t.Text : t.Text));
            return element;
        }
        System.Collections.Generic.IEnumerable<XElement> Shape(FixedShape shape)
        {
            if (shape.Stroke == 0)
            {
                yield return new XElement(xps + "Path", new XAttribute("Fill", "#FF" + shape.Color), new XAttribute("Data", "M " + Number(shape.X) + "," + Number(shape.Y) + " l " + Number(shape.Width) + ",0 0," + Number(shape.Height) + " " + Number(-shape.Width) + ",0 Z"));
                yield break;
            }
            foreach (int side in shape.Style == "double" ? new[] { -1, 1 } : new[] { 0 })
            {
                double displacement = side * shape.Stroke / 3;
                yield return new XElement(xps + "Path", new XAttribute("Stroke", "#FF" + shape.Color), new XAttribute("StrokeThickness", Number(side == 0 ? shape.Stroke : shape.Stroke / 3)),
                    shape.Style is "dotted" or "dashed" ? new XAttribute("StrokeDashArray", shape.Style == "dashed" ? "3 2" : "1 1") : null,
                    new XAttribute("Data", "M " + Number(shape.X + (shape.Width == 0 ? displacement : 0)) + "," + Number(shape.Y + (shape.Width == 0 ? 0 : displacement)) + " l " + Number(shape.Width) + "," + Number(shape.Height)));
            }
        }
        XElement Image(PositionedText text, string path)
        {
            DocumentImage image = text.Image!;
            var density = imageDensities[image];
            double naturalWidth = image.PixelsWide * 96 / density.X, naturalHeight = image.PixelsHigh * 96 / density.Y;
            double angle = image.Rotation * System.Math.PI / 180, c = System.Math.Round(System.Math.Cos(angle)), s = System.Math.Round(System.Math.Sin(angle));
            double a = c * (image.FlipHorizontal ? -1 : 1), b = s * (image.FlipHorizontal ? -1 : 1), d = c * (image.FlipVertical ? -1 : 1), k = -s * (image.FlipVertical ? -1 : 1);
            string Matrix(double v) => v.ToString("0.########", CultureInfo.InvariantCulture);
            string transform = Matrix(a) + "," + Matrix(b) + "," + Matrix(k) + "," + Matrix(d) + ","
                + Number(text.X + image.Width / 2 - a * image.FrameWidth / 2 - k * image.FrameHeight / 2) + ","
                + Number(text.Y - image.Height / 2 - b * image.FrameWidth / 2 - d * image.FrameHeight / 2);
            return new XElement(xps + "Canvas", new XAttribute("RenderTransform", transform),
                new XElement(xps + "Path", new XAttribute("Data", "M 0,0 l " + Number(image.FrameWidth) + ",0 0," + Number(image.FrameHeight) + " " + Number(-image.FrameWidth) + ",0 Z"),
                    new XElement(xps + "Path.Fill", new XElement(xps + "ImageBrush", new XAttribute("ImageSource", path), new XAttribute("ViewboxUnits", "Absolute"), new XAttribute("ViewportUnits", "Absolute"),
                        new XAttribute("Viewbox", Matrix(image.CropLeft * naturalWidth) + "," + Matrix(image.CropTop * naturalHeight) + "," + Matrix((1 - image.CropLeft - image.CropRight) * naturalWidth) + "," + Matrix((1 - image.CropTop - image.CropBottom) * naturalHeight)),
                        new XAttribute("Viewport", "0,0," + Number(image.FrameWidth) + "," + Number(image.FrameHeight))))));
        }
        void Xml(string name, XElement root) => PackageArchive.Write(archive, name, TextFormat.Utf8.GetBytes(root.ToString(SaveOptions.DisableFormatting)));
    }

    private static string Number(double points) => (points * 96 / 72).ToString("0.####", CultureInfo.InvariantCulture);
    private static string FontName(int index) => "Face-" + index.ToString(CultureInfo.InvariantCulture) + ".ttf";
}
