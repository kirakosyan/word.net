using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.TrueType;
using WordNet.Internal;
using WordNet.Layout;
using WordNet.Model;

namespace WordNet.Formats;

internal static class SvgPreview
{
    internal static byte[] Write(FixedPage page, ConversionContext context)
    {
        XNamespace svg = "http://www.w3.org/2000/svg", xlink = "http://www.w3.org/1999/xlink";
        var root = new XElement(svg + "svg", new XAttribute("version", "1.1"), new XAttribute(XNamespace.Xmlns + "xlink", xlink), new XAttribute("width", N(page.Settings.Width) + "pt"), new XAttribute("height", N(page.Settings.Height) + "pt"),
            new XAttribute("viewBox", "0 0 " + N(page.Settings.Width) + " " + N(page.Settings.Height)), new XAttribute("role", "img"));
        root.Add(new XElement(svg + "rect", new XAttribute("width", "100%"), new XAttribute("height", "100%"), new XAttribute("fill", "#ffffff")));
        long length = 0; int imageId = 0, gradientId = 0;
        foreach (FixedShape shape in FixedShape.PaintOrder(page.Shapes.Where(s => !s.Foreground))) AddShape(shape);
        void AddShape(FixedShape shape)
        {
            if (shape.Geometry != null)
            {
                string data = string.Join(" ", VectorPath.Create(shape).Select(command => command.Kind + (command.Values.Length == 0 ? "" : " " + string.Join(" ", command.Values.Select(N)))));
                length += data.Length + 200; Check();
                string paint = "#" + shape.Color;
                if (shape.Gradient is LinearGradient gradient)
                {
                    string id = "gradient" + (gradientId++).ToString(CultureInfo.InvariantCulture);
                    var ends = gradient.Endpoints(shape.X, shape.Y, shape.Width, shape.Height);
                    root.Add(new XElement(svg + "defs", new XElement(svg + "linearGradient", new XAttribute("id", id), new XAttribute("gradientUnits", "userSpaceOnUse"),
                        new XAttribute("spreadMethod", "pad"), new XAttribute("color-interpolation", "sRGB"),
                        new XAttribute("x1", N(ends.X1)), new XAttribute("y1", N(ends.Y1)), new XAttribute("x2", N(ends.X2)), new XAttribute("y2", N(ends.Y2)),
                        gradient.Stops.Select(stop => new XElement(svg + "stop", new XAttribute("offset", N(stop.Position / 100000.0)), new XAttribute("stop-color", "#" + stop.Color))))));
                    paint = "url(#" + id + ")"; length += gradient.Stops.Length * 100 + 300; Check();
                }
                root.Add(new XElement(svg + "path", new XAttribute("d", data), new XAttribute("fill", shape.Stroke == 0 ? paint : "none"),
                    shape.Rotation == 0 ? null : new XAttribute("transform", "rotate(" + N(shape.Rotation) + " " + N(shape.RotationX) + " " + N(shape.RotationY) + ")"),
                    shape.Stroke == 0 ? null : new XAttribute("stroke", "#" + shape.Color), shape.Stroke == 0 ? null : new XAttribute("stroke-width", N(shape.Stroke))));
            }
            else if (shape.Stroke == 0) root.Add(new XElement(svg + "rect", new XAttribute("x", N(shape.X)), new XAttribute("y", N(shape.Y)), new XAttribute("width", N(shape.Width)), new XAttribute("height", N(shape.Height)), new XAttribute("fill", "#" + shape.Color)));
            else foreach (int side in shape.Style == "double" ? new[] { -1, 1 } : new[] { 0 })
            {
                double offset = side * shape.Stroke / 3;
                root.Add(new XElement(svg + "line", new XAttribute("x1", N(shape.X + (shape.Width == 0 ? offset : 0))), new XAttribute("y1", N(shape.Y + (shape.Width == 0 ? 0 : offset))),
                    new XAttribute("x2", N(shape.X + shape.Width + (shape.Width == 0 ? offset : 0))), new XAttribute("y2", N(shape.Y + shape.Height + (shape.Width == 0 ? 0 : offset))),
                    new XAttribute("stroke", "#" + shape.Color), new XAttribute("stroke-width", N(side == 0 ? shape.Stroke : shape.Stroke / 3)),
                    shape.Style is "dotted" or "dashed" ? new XAttribute("stroke-dasharray", N(shape.Stroke * (shape.Style == "dashed" ? 3 : 1)) + " " + N(shape.Stroke * (shape.Style == "dashed" ? 2 : 1))) : null));
            }
        }
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
                    new XAttribute("preserveAspectRatio", "none"), new XAttribute(xlink + "href", "data:" + image.MediaType + ";base64," + Convert.ToBase64String(image.Bytes)), new XElement(svg + "title", image.AlternativeText))));
                root.Add(item.Rotation == 0 ? group : new XElement(svg + "g", new XAttribute("transform", "rotate(" + N(item.Rotation) + " " + N(item.RotationX) + " " + N(item.RotationY) + ")"), group));
                length += (long)image.Bytes.Length * 4 / 3; Check(); continue;
            }
            if (item.Text.Length == 0 && item.NativeShape == null) continue;
            RenderFontCache.Face face = context.RenderFonts.Get(item.Font); TrueTypeFont font = face.Font;
            var text = new XElement(svg + "g", new XAttribute("fill", "#" + (item.Color ?? "000000")),
                item.Rotation == 0 ? null : new XAttribute("transform", "rotate(" + N(item.Rotation) + " " + N(item.RotationX) + " " + N(item.RotationY) + ")"), new XElement(svg + "title", item.Text));
            double x = item.X, scale = item.Size / font.GetUnitsPerEm();
            int glyphCount = item.NativeShape?.Glyphs.Length ?? item.Text.Length;
            for (int c = 0; c < glyphCount; c++)
            {
                ShapedGlyph? shaped = item.NativeShape?.Glyphs[c];
                int codepoint = shaped == null ? char.ConvertToUtf32(item.Text, c) : 32; if (codepoint > 0xffff) c++;
                int glyph = shaped?.Glyph ?? 0;
                if (glyph < 0) { x += shaped!.Advance * scale; continue; }
                if (shaped == null && font.TableRegister.CMapTable?.TryGetGlyphIndex(codepoint, out glyph) != true) throw new NotSupportedException("Preview.Glyph: A preview glyph is unavailable.");
                if (!face.SvgPaths.TryGetValue(glyph, out string? path))
                {
                    var data = new StringBuilder();
                    foreach (PdfSubpath outline in face.Paths(glyph))
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
                    path = data.ToString(); face.SvgPaths.Add(glyph, path);
                }
                text.Add(new XElement(svg + "path", new XAttribute("d", path), new XAttribute("transform", "translate(" + N(x + (shaped?.X ?? 0) * scale) + " " + N(item.Y - (shaped?.Y ?? 0) * scale) + ") scale(" + N(scale) + " " + N(-scale) + ")")));
                length += path.Length + 200; Check();
                if (shaped != null) x += shaped.Advance * scale;
                else if (font.TryGetAdvanceWidth(codepoint, _ => glyph, out double advance)) x += advance * scale;
            }
            root.Add(text);
        }
        foreach (FixedShape shape in FixedShape.PaintOrder(page.Shapes.Where(s => s.Foreground))) AddShape(shape);
        byte[] bytes = TextFormat.Utf8.GetBytes(root.ToString(SaveOptions.DisableFormatting)); if (bytes.Length > context.Options.MaximumOutputBytes) throw new InvalidDataException("Limit.OutputBytes: SVG exceeds output bounds."); return bytes;
        void Check() { if (length > context.Options.MaximumOutputBytes) throw new InvalidDataException("Limit.OutputBytes: SVG resources exceed output bounds."); }
        static string Point(PdfPoint p) => N(p.X) + " " + N(p.Y);
        static string N(double number) => number.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
