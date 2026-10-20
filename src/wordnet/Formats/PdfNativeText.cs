using System;
using System.Collections.Generic;
using System.Text;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.TrueType;
using UglyToad.PdfPig.Fonts.TrueType.Parser;
using UglyToad.PdfPig.Graphics.Operations.MarkedContent;
using UglyToad.PdfPig.Graphics.Operations.PathConstruction;
using UglyToad.PdfPig.Graphics.Operations.PathPainting;
using UglyToad.PdfPig.Graphics.Operations.SpecialGraphicsState;
using UglyToad.PdfPig.Graphics.Operations.TextState;
using UglyToad.PdfPig.Tokens;
using UglyToad.PdfPig.Writer;
using WordNet.Internal;
using WordNet.Layout;

namespace WordNet.Formats;

internal sealed class PdfNativeText
{
    private readonly ConversionContext context;
    private long operations;
    internal PdfNativeText(ConversionContext context) { this.context = context; }
    internal void Draw(PdfPageBuilder target, PositionedText item, double pageHeight, PdfDocumentBuilder.AddedFont carrier)
    {
        DrawOutlines(target, item, pageHeight);
        DrawCarrier(target, item, pageHeight, carrier);
    }
    internal void DrawOutlines(PdfPageBuilder target, PositionedText item, double pageHeight)
    {
        ShapedWord word = item.NativeShape!;
        RenderFontCache.Face font = context.RenderFonts.Get(item.Font);
        var ops = target.CurrentStream.Operations; double pen = 0, scale = item.Size / word.Units;
        ops.Add(new BeginMarkedContent(NameToken.Create("Artifact")));
        foreach (ShapedGlyph glyph in word.Glyphs)
        {
            context.CheckCancellation();
            if (glyph.Glyph < 0) { pen += glyph.Advance; continue; }
            IReadOnlyList<PdfSubpath> paths = font.Paths(glyph.Glyph);
            if (paths.Count > 0)
            {
                double X(PdfPoint p) => item.X + (pen + glyph.X + p.X) * scale;
                double Y(PdfPoint p) => pageHeight - item.Y + (glyph.Y + p.Y) * scale;
                foreach (PdfSubpath path in paths) foreach (var command in path.Commands)
                {
                    if (++operations > context.Options.MaximumExpandedBytes / 128) throw new System.IO.InvalidDataException("Limit.TypographyPaths: Glyph drawing operations exceed the expanded-byte budget.");
                    if (command is PdfSubpath.Move move) ops.Add(new BeginNewSubpath(X(move.Location), Y(move.Location)));
                    else if (command is PdfSubpath.Line line) ops.Add(new AppendStraightLineSegment(X(line.To), Y(line.To)));
                    else if (command is PdfSubpath.CubicBezierCurve cubic) ops.Add(new AppendDualControlPointBezierCurve(X(cubic.FirstControlPoint), Y(cubic.FirstControlPoint), X(cubic.SecondControlPoint), Y(cubic.SecondControlPoint), X(cubic.EndPoint), Y(cubic.EndPoint)));
                    else if (command is PdfSubpath.QuadraticBezierCurve quad)
                    {
                        var a = new PdfPoint(quad.StartPoint.X + (quad.ControlPoint.X - quad.StartPoint.X) * 2 / 3, quad.StartPoint.Y + (quad.ControlPoint.Y - quad.StartPoint.Y) * 2 / 3);
                        var b = new PdfPoint(quad.EndPoint.X + (quad.ControlPoint.X - quad.EndPoint.X) * 2 / 3, quad.EndPoint.Y + (quad.ControlPoint.Y - quad.EndPoint.Y) * 2 / 3);
                        ops.Add(new AppendDualControlPointBezierCurve(X(a), Y(a), X(b), Y(b), X(quad.EndPoint), Y(quad.EndPoint)));
                    }
                    else if (command is PdfSubpath.Close) ops.Add(CloseSubpath.Value);
                }
                ops.Add(FillPathNonZeroWinding.Value);
            }
            pen += glyph.Advance;
        }
        ops.Add(EndMarkedContent.Value);
    }
    internal static void DrawCarrier(PdfPageBuilder target, PositionedText item, double pageHeight, PdfDocumentBuilder.AddedFont carrier)
    {
        if (item.Text.Length == 0) return;
        ShapedWord word = item.NativeShape!;
        var ops = target.CurrentStream.Operations;
        ops.Add(Push.Value);
        ops.Add(new BeginMarkedContentWithProperties(NameToken.Create("Span"), new DictionaryToken(new Dictionary<NameToken, IToken> { [NameToken.Create("ActualText")] = Unicode(item.Text) })));
        var measured = target.MeasureText("?", item.Size, new PdfPoint(0, 0), carrier);
        double width = measured[measured.Count - 1].EndBaseLine.X;
        ops.Add(new SetHorizontalScaling(Math.Max(0.001, word.Width * item.Size) / width * 100)); target.SetTextRenderingMode(TextRenderingMode.Neither);
        target.AddText("?", item.Size, new PdfPoint(item.X, pageHeight - item.Y), carrier);
        ops.Add(EndMarkedContent.Value); ops.Add(Pop.Value);
    }
    internal static HexToken Unicode(string value)
    {
        byte[] bytes = Encoding.BigEndianUnicode.GetBytes(value); var hex = new StringBuilder("FEFF");
        foreach (byte b in bytes) hex.Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        return new HexToken(hex.ToString().AsSpan());
    }
}
