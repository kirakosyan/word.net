using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.TrueType;
using UglyToad.PdfPig.Fonts.TrueType.Parser;
using WordNet.Internal;
using WordNet.Layout;
using WordNet.Model;

namespace WordNet.Formats;

internal static class SvgPreview
{
    internal static byte[] Write(FixedPage page, ConversionContext context)
    {
        XNamespace svg = "http://www.w3.org/2000/svg";
        var root = new XElement(svg + "svg", new XAttribute("version", "1.1"), new XAttribute("width", N(page.Settings.Width) + "pt"), new XAttribute("height", N(page.Settings.Height) + "pt"),
            new XAttribute("viewBox", "0 0 " + N(page.Settings.Width) + " " + N(page.Settings.Height)), new XAttribute("role", "img"));
        root.Add(new XElement(svg + "rect", new XAttribute("width", "100%"), new XAttribute("height", "100%"), new XAttribute("fill", "#ffffff")));
        foreach (FixedShape shape in FixedShape.PaintOrder(page.Shapes))
        {
            if (shape.Stroke == 0) root.Add(new XElement(svg + "rect", new XAttribute("x", N(shape.X)), new XAttribute("y", N(shape.Y)), new XAttribute("width", N(shape.Width)), new XAttribute("height", N(shape.Height)), new XAttribute("fill", "#" + shape.Color)));
            else foreach (int side in shape.Style == "double" ? new[] { -1, 1 } : new[] { 0 })
            {
                double offset = side * shape.Stroke / 3;
                root.Add(new XElement(svg + "line", new XAttribute("x1", N(shape.X + (shape.Width == 0 ? offset : 0))), new XAttribute("y1", N(shape.Y + (shape.Width == 0 ? 0 : offset))),
                    new XAttribute("x2", N(shape.X + shape.Width + (shape.Width == 0 ? offset : 0))), new XAttribute("y2", N(shape.Y + shape.Height + (shape.Width == 0 ? 0 : offset))),
                    new XAttribute("stroke", "#" + shape.Color), new XAttribute("stroke-width", N(side == 0 ? shape.Stroke : shape.Stroke / 3)),
                    shape.Style is "dotted" or "dashed" ? new XAttribute("stroke-dasharray", N(shape.Stroke * (shape.Style == "dashed" ? 3 : 1)) + " " + N(shape.Stroke * (shape.Style == "dashed" ? 2 : 1))) : null));
            }
        }
        var fonts = new Dictionary<int, TrueTypeFont>();
        var paths = new Dictionary<(int Font, int Glyph), string>(); long length = 0; int imageId = 0;
        foreach (PositionedText item in page.Text.OrderBy(t => t.Image?.Anchor == null ? 0 : t.Image.Anchor.Behind ? -1 : 1))
        {
            context.CheckCancellation();
            if (item.Image is DocumentImage image)
            {
                string id = "clip" + (imageId++).ToString(CultureInfo.InvariantCulture);
                var group = new XElement(svg + "g", new XAttribute("transform", "translate(" + N(item.X + image.Width / 2) + " " + N(item.Y - image.Height / 2) + ") rotate(" + image.Rotation.ToString(CultureInfo.InvariantCulture) + ") scale(" + (image.FlipHorizontal ? "-1" : "1") + " " + (image.FlipVertical ? "-1" : "1") + ")"));
                group.Add(new XElement(svg + "clipPath", new XAttribute("id", id), new XElement(svg + "rect", new XAttribute("x", N(-image.FrameWidth / 2)), new XAttribute("y", N(-image.FrameHeight / 2)), new XAttribute("width", N(image.FrameWidth)), new XAttribute("height", N(image.FrameHeight)))));
                double width = image.FrameWidth / (1 - image.CropLeft - image.CropRight), height = image.FrameHeight / (1 - image.CropTop - image.CropBottom);
                group.Add(new XElement(svg + "g", new XAttribute("clip-path", "url(#" + id + ")"), new XElement(svg + "image", new XAttribute("x", N(-image.FrameWidth / 2 - width * image.CropLeft)), new XAttribute("y", N(-image.FrameHeight / 2 - height * image.CropTop)), new XAttribute("width", N(width)), new XAttribute("height", N(height)),
                    new XAttribute("preserveAspectRatio", "none"), new XAttribute("href", "data:" + image.MediaType + ";base64," + Convert.ToBase64String(image.Bytes)), new XElement(svg + "title", image.AlternativeText))));
                root.Add(group); length += (long)image.Bytes.Length * 4 / 3; Check(); continue;
            }
            if (item.Text.Length == 0) continue;
            if (!fonts.TryGetValue(item.Font, out TrueTypeFont? font)) fonts.Add(item.Font, font = TrueTypeFontParser.Parse(new TrueTypeDataBytes(context.FontData[item.Font])));
            var text = new XElement(svg + "g", new XAttribute("fill", "#" + (item.Color ?? "000000")), new XElement(svg + "title", item.Text));
            double x = item.X, scale = item.Size / font.GetUnitsPerEm();
            for (int c = 0; c < item.Text.Length; c++)
            {
                int codepoint = char.ConvertToUtf32(item.Text, c); if (codepoint > 0xffff) c++;
                if (font.TableRegister.CMapTable?.TryGetGlyphIndex(codepoint, out int glyph) != true) throw new NotSupportedException("Preview.Glyph: A preview glyph is unavailable.");
                if (!paths.TryGetValue((item.Font, glyph), out string? path))
                {
                    var data = new StringBuilder();
                    if (!font.TryGetPath(codepoint, _ => glyph, out IReadOnlyList<PdfSubpath> outlines)) { if (codepoint <= char.MaxValue && char.IsWhiteSpace((char)codepoint)) outlines = Array.Empty<PdfSubpath>(); else throw new InvalidDataException("Preview.Font: A glyph outline is unreadable."); }
                    foreach (PdfSubpath outline in outlines)
                        foreach (var command in outline.Commands)
                        {
                            context.CheckCancellation();
                            if (command is PdfSubpath.Move move) data.Append('M').Append(Point(move.Location));
                            else if (command is PdfSubpath.Line line) data.Append('L').Append(Point(line.To));
                            else if (command is PdfSubpath.QuadraticBezierCurve quad) data.Append('Q').Append(Point(quad.ControlPoint)).Append(' ').Append(Point(quad.EndPoint));
                            else if (command is PdfSubpath.CubicBezierCurve cubic) data.Append('C').Append(Point(cubic.FirstControlPoint)).Append(' ').Append(Point(cubic.SecondControlPoint)).Append(' ').Append(Point(cubic.EndPoint));
                            else if (command is PdfSubpath.Close) data.Append('Z');
                            else throw new InvalidDataException("Preview.Font: Unsupported outline command.");
                            if (data.Length > context.Options.MaximumOutputBytes) throw new InvalidDataException("Limit.PreviewPaths: Glyph outline exceeds output bounds.");
                        }
                    path = data.ToString(); paths.Add((item.Font, glyph), path);
                }
                text.Add(new XElement(svg + "path", new XAttribute("d", path), new XAttribute("transform", "translate(" + N(x) + " " + N(item.Y) + ") scale(" + N(scale) + " " + N(-scale) + ")")));
                length += path.Length + 200; Check();
                if (font.TryGetAdvanceWidth(codepoint, _ => glyph, out double advance)) x += advance * scale;
            }
            root.Add(text);
        }
        byte[] bytes = TextFormat.Utf8.GetBytes(root.ToString(SaveOptions.DisableFormatting)); if (bytes.Length > context.Options.MaximumOutputBytes) throw new InvalidDataException("Limit.OutputBytes: SVG exceeds output bounds."); return bytes;
        void Check() { if (length > context.Options.MaximumOutputBytes) throw new InvalidDataException("Limit.OutputBytes: SVG resources exceed output bounds."); }
        static string Point(PdfPoint p) => N(p.X) + " " + N(p.Y);
        static string N(double number) => number.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
