using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UglyToad.PdfPig.Fonts.TrueType;
using UglyToad.PdfPig.Fonts.TrueType.Parser;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Layout;

internal sealed class MathLayout
{
    private readonly MathFont font;
    private readonly int index;
    private readonly ConversionContext context;
    private readonly Func<int, int> glyph;
    private int work;
    private sealed class Box
    {
        internal ObjectVisual Visual { get; } = new ObjectVisual();
        internal int First { get; set; } = -1;
        internal int Last { get; set; } = -1;
    }
    internal MathLayout(MathFont font, int index, byte[] bytes, ConversionContext context)
    {
        this.font = font; this.index = index; this.context = context;
        glyph = font.Character;
    }
    internal ObjectVisual Create(DocumentObjectData item)
    {
        if (!item.Renderable) throw new NotSupportedException("Equation.Appearance: The retained OMML appearance is outside the math layout profile.");
        Box box = Build(item.Equation!, item.FontSize, 0, item.Display);
        string description = item.Description;
        if (description.Length == 0 && item.Equation!.NodeCount <= 256 && item.Equation.Characters <= 2048) description = MathObjectFormat.Description(item.Equation);
        if (description.Length > 0)
        {
            context.Objects.Render(description.Length, 1);
            // Exactly one logical carrier for the expression; visible math glyphs are artifacts.
            var carrier = new ShapedWord(new[] { new ShapedGlyph(-1, 0, box.Visual.Width / item.FontSize * font.Units, 0, 0) }, font.Units);
            box.Visual.Text.Add(new PositionedText(description, 0, 0, item.FontSize, index, nativeShape: carrier));
        }
        return box.Visual;
    }
    private void Charge()
    {
        context.CheckCancellation();
        context.Objects.Render(0, 1);
        if (++work > 200000 || work * 128L > context.Options.MaximumExpandedBytes) throw new InvalidDataException("Equation.LayoutBudget: Math construction work exceeds its operation allowance.");
    }
    private double Constant(int n, double size) => font.Constants[n] * size / font.Units;
    private double Script(double size, int level) => size * (level == 0 ? font.ScriptPercent / 100.0 : (double)font.ScriptScriptPercent / font.ScriptPercent);
    private Box Build(EquationExpression node, double size, int level, bool display)
    {
        Charge();
        if (node.Kind == EquationKind.Text)
        {
            var result = new Box(); double x = 0;
            for (int i = 0; i < node.Value.Length; i++)
            {
                int c = char.ConvertToUtf32(node.Value, i); if (c > 0xffff) i++;
                if (node.Italic && c > 0x024f && c <= 0xffff && char.IsLetter((char)c)) throw new NotSupportedException("Equation.ItalicScript: Automatic math italic selection is qualified for Latin variables only.");
                if (node.Italic) c = c is >= 'A' and <= 'Z' ? 0x1d434 + c - 'A' : c == 'h' ? 0x210e : c is >= 'a' and <= 'z' ? 0x1d44e + c - 'a' : c;
                // Complex scripts are handled by native typography in ordinary text, not by this math-run subset.
                if (c is >= 0x0590 and <= 0x0dff) throw new NotSupportedException("Equation.Script: Complex-script math text requires a separately qualified shaping contract.");
                Box letter = Glyph(glyph(c), size); Put(result, letter, x, 0); x += letter.Visual.Width;
            }
            result.Visual.Width = x; return result;
        }
        if (node.Kind == EquationKind.Row)
        {
            var result = new Box(); double x = 0;
            foreach (EquationExpression child in node.Children) { Box b = Build(child, size, level, display); Put(result, b, x, 0); x += b.Visual.Width; }
            result.Visual.Width = x; return result;
        }
        if (node.Kind == EquationKind.Fraction)
        {
            double childSize = display ? size : Script(size, level);
            Box num = Build(node.Children[0], childSize, display ? level : level + 1, false), den = Build(node.Children[1], childSize, display ? level : level + 1, false);
            double axis = Constant(1, size), rule = Constant(34, size), gapN = Constant(display ? 33 : 32, size), gapD = Constant(display ? 36 : 35, size);
            double up = Math.Max(Constant(display ? 29 : 28, size), num.Visual.Descent + axis + rule / 2 + gapN);
            double down = Math.Max(Constant(display ? 31 : 30, size), den.Visual.Ascent - axis + rule / 2 + gapD);
            double padding = size * 0.1, width = Math.Max(num.Visual.Width, den.Visual.Width) + padding * 2;
            var result = new Box(); Put(result, num, (width - num.Visual.Width) / 2, -up); Put(result, den, (width - den.Visual.Width) / 2, down);
            result.Visual.Shapes.Add(new FixedShape(0, -axis - rule / 2, width, rule, "000000")); result.Visual.Width = width; return result;
        }
        if (node.Kind is EquationKind.Subscript or EquationKind.Superscript or EquationKind.SubSuperscript)
        {
            Box basis = Build(node.Children[0], size, level, display); double scriptSize = Script(size, level);
            Box? sub = node.Kind == EquationKind.Superscript ? null : Build(node.Children[1], scriptSize, level + 1, false);
            Box? sup = node.Kind == EquationKind.Subscript ? null : Build(node.Children[node.Kind == EquationKind.Superscript ? 1 : 2], scriptSize, level + 1, false);
            double down = sub == null ? 0 : Math.Max(Constant(4, size), Math.Max(sub.Visual.Ascent - Constant(5, size), basis.Visual.Descent + Constant(6, size)));
            double up = sup == null ? 0 : Math.Max(Constant(7, size), Math.Max(sup.Visual.Descent + Constant(9, size), basis.Visual.Ascent - Constant(10, size)));
            if (sub != null && sup != null)
            {
                double missing = Constant(11, size) - (down + up - sub.Visual.Ascent - sup.Visual.Descent);
                if (missing > 0) { double raise = Math.Min(missing, Math.Max(0, Constant(12, size) - (up - sup.Visual.Descent))); up += raise; down += missing - raise; }
            }
            var result = new Box(); Put(result, basis, 0, 0); double end = basis.Visual.Width;
            if (sub != null)
            {
                double kern = ScriptKern(basis, sub, size, scriptSize, -down, false); double x = Math.Max(0, basis.Visual.Width + kern);
                Put(result, sub, x, down); end = Math.Max(end, x + sub.Visual.Width);
            }
            if (sup != null)
            {
                double italic = basis.Last < 0 ? 0 : font.Italic(basis.Last) * size / font.Units;
                double x = Math.Max(0, basis.Visual.Width + italic + ScriptKern(basis, sup, size, scriptSize, up, true));
                Put(result, sup, x, -up); end = Math.Max(end, x + sup.Visual.Width);
            }
            result.Visual.Width = end + Constant(13, size); return result;
        }
        if (node.Kind == EquationKind.Delimiter)
        {
            Box body = Build(node.Children[0], size, level, display); double axis = Constant(1, size);
            double target = Math.Max(body.Visual.Ascent - axis, body.Visual.Descent + axis) * 2 + size * 0.1;
            Box opening = Stretch(glyph(char.ConvertToUtf32(node.Opening, 0)), size, target, axis), closing = Stretch(glyph(char.ConvertToUtf32(node.Closing, 0)), size, target, axis);
            var result = new Box(); Put(result, opening, 0, 0); Put(result, body, opening.Visual.Width, 0); Put(result, closing, opening.Visual.Width + body.Visual.Width, 0);
            result.Visual.Width = opening.Visual.Width + body.Visual.Width + closing.Visual.Width; return result;
        }
        if (node.Kind == EquationKind.Radical)
        {
            Box body = Build(node.Children[0], size, level, false); double rule = Constant(47, size), gap = Constant(display ? 46 : 45, size);
            double height = body.Visual.Ascent + body.Visual.Descent + gap + rule;
            Box root = Stretch(glyph(0x221a), size, height, 0);
            double rootY = body.Visual.Descent - root.Visual.Descent, barY = -body.Visual.Ascent - gap - rule;
            double before = 0; Box? degree = null;
            if (node.Children.Count == 2)
            { degree = Build(node.Children[1], size * font.ScriptScriptPercent / 100, level + 2, false); before = Math.Max(0, degree.Visual.Width + Constant(49, size) + Constant(50, size)); }
            var result = new Box(); Put(result, root, before, rootY); Put(result, body, before + root.Visual.Width, 0);
            result.Visual.Shapes.Add(new FixedShape(before + root.Visual.Width - rule, barY, body.Visual.Width + rule, rule, "000000"));
            result.Visual.Ascent = Math.Max(result.Visual.Ascent, -barY + Constant(48, size));
            if (degree != null) Put(result, degree, Constant(49, size), rootY + root.Visual.Descent - (root.Visual.Ascent + root.Visual.Descent) * font.DegreePercent / 100 - degree.Visual.Descent);
            result.Visual.Width = before + root.Visual.Width + body.Visual.Width; return result;
        }
        throw new NotSupportedException("Equation.Layout: Unsupported construction.");
    }
    private double ScriptKern(Box basis, Box script, double size, double scriptSize, double shiftUp, bool superscript)
    {
        if (basis.Last < 0 || script.First < 0) return 0;
        int a = superscript ? 0 : 2, b = superscript ? 3 : 1;
        double h1 = superscript ? shiftUp - script.Visual.Descent : shiftUp + script.Visual.Ascent;
        double h2 = superscript ? basis.Visual.Ascent : -basis.Visual.Descent;
        double At(double h) => font.Kern(basis.Last, a, h * font.Units / size) * size / font.Units
            + font.Kern(script.First, b, (h - shiftUp) * font.Units / scriptSize) * scriptSize / font.Units;
        return Math.Min(At(h1), At(h2));
    }
    private Box Glyph(int glyphIndex, double size)
    {
        Charge(); var bounds = font.Bounds(glyphIndex); int advance = font.Advance(glyphIndex);
        var result = new Box { First = glyphIndex, Last = glyphIndex };
        result.Visual.Width = advance * size / font.Units; result.Visual.Ascent = Math.Max(0, bounds.Top * size / font.Units); result.Visual.Descent = Math.Max(0, -bounds.Bottom * size / font.Units);
        result.Visual.Text.Add(new PositionedText("", 0, 0, size, index, nativeShape: new ShapedWord(new[] { new ShapedGlyph(glyphIndex, 0, advance, 0, 0) }, font.Units)));
        return result;
    }
    private Box Stretch(int glyphIndex, double size, double height, double axis)
    {
        Box basis = Glyph(glyphIndex, size); double target = height * font.Units / size;
        if (basis.Visual.Ascent + basis.Visual.Descent >= height) return Center(basis);
        if (!font.Vertical.TryGetValue(glyphIndex, out MathFont.Construction? construction)) throw new NotSupportedException("Equation.Stretch: The exact font has no sufficient vertical construction.");
        foreach (var variant in construction.Variants) if (variant.Advance >= target) return Center(Glyph(variant.Glyph, size));
        if (!construction.Parts.Any(p => p.Extender)) throw new NotSupportedException("Equation.Stretch: The font has no sufficient variant or extender assembly.");
        for (int repeats = 1; repeats <= 256; repeats++)
        {
            Charge(); var parts = construction.Parts.SelectMany(p => p.Extender ? Enumerable.Repeat(p, repeats) : new[] { p }).ToArray();
            if (parts.Length > 1024) break;
            double[] overlaps = new double[parts.Length - 1]; double full = parts.Sum(p => (double)p.Advance);
            for (int i = 0; i < overlaps.Length; i++) { overlaps[i] = Math.Min(Math.Min(parts[i].End, parts[i + 1].Start), Math.Min(parts[i].Advance, parts[i + 1].Advance)); if (overlaps[i] < font.MinOverlap) throw new NotSupportedException("Equation.Connector: Font assembly connectors cannot meet the minimum overlap."); }
            double maximum = full - overlaps.Length * font.MinOverlap;
            if (maximum < target) continue;
            double desiredOverlap = Math.Min(overlaps.Sum(), Math.Max(overlaps.Length * font.MinOverlap, full - target));
            double allowance = overlaps.Sum() - overlaps.Length * font.MinOverlap;
            for (int i = 0; i < overlaps.Length; i++) overlaps[i] = font.MinOverlap + (allowance == 0 ? 0 : (overlaps[i] - font.MinOverlap) * (desiredOverlap - overlaps.Length * font.MinOverlap) / allowance);
            double actual = full - overlaps.Sum(), at = 0; var result = new Box();
            for (int i = 0; i < parts.Length; i++)
            {
                Box part = Glyph(parts[i].Glyph, size); Put(result, part, 0, -at * size / font.Units);
                result.Visual.Width = Math.Max(result.Visual.Width, part.Visual.Width); at += parts[i].Advance - (i < overlaps.Length ? overlaps[i] : 0);
            }
            result.Visual.Ascent = actual * size / font.Units; result.Visual.Descent = 0; return Center(result);
        }
        throw new NotSupportedException("Equation.StretchLimit: Math extender work exceeds the qualified bound.");
        Box Center(Box box)
        {
            double shift = (box.Visual.Ascent - box.Visual.Descent) / 2 - axis;
            var centered = new Box(); Put(centered, box, 0, shift); centered.Visual.Width = box.Visual.Width; return centered;
        }
    }
    private static void Put(Box destination, Box source, double x, double y)
    {
        destination.Visual.Text.AddRange(source.Visual.Text.Select(t => t.At(t.X + x, t.Y + y)));
        destination.Visual.Shapes.AddRange(source.Visual.Shapes.Select(s => s.At(s.X + x, s.Y + y)));
        destination.Visual.Ascent = Math.Max(destination.Visual.Ascent, source.Visual.Ascent - y);
        destination.Visual.Descent = Math.Max(destination.Visual.Descent, source.Visual.Descent + y);
        if (destination.First < 0) destination.First = source.First; destination.Last = source.Last;
    }
}
