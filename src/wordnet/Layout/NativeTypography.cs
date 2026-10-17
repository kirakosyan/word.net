using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UglyToad.PdfPig.Fonts.TrueType;
using UglyToad.PdfPig.Fonts.TrueType.Parser;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Layout;

internal sealed class ShapedGlyph
{
    internal ShapedGlyph(int glyph, int cluster, double advance, double x, double y) { Glyph = glyph; Cluster = cluster; Advance = advance; X = x; Y = y; }
    internal int Glyph { get; }
    internal int Cluster { get; }
    internal double Advance { get; }
    internal double X { get; }
    internal double Y { get; }
}
internal sealed class ShapedWord
{
    internal ShapedWord(ShapedGlyph[] glyphs, int units) { Glyphs = glyphs; Units = units; }
    internal ShapedGlyph[] Glyphs { get; }
    internal int Units { get; }
    internal double Width => Glyphs.Sum(g => g.Advance) / Units;
}
internal abstract class TypographySession : IDisposable
{
    internal abstract ShapedWord Shape(string text, int font, bool rtl);
    public abstract void Dispose();
}
internal static class NativeTypography
{
    internal static bool Arabic(char c) => c is >= '\u0600' and <= '\u06ff' or >= '\u0750' and <= '\u077f' or >= '\u08a0' and <= '\u08ff';
    internal static bool Rtl(string value) => value.Any(c => Arabic(c) && char.IsLetter(c));
    internal static bool BaseRtl(ParagraphBlock paragraph, TypographyOptions options) => options.Direction == TextDirection.RightToLeft || options.Direction == TextDirection.Auto && (paragraph.RightToLeft ?? Rtl(paragraph.Text.FirstOrDefault(char.IsLetter).ToString()));
    internal static TypographySession? Open(ConversionContext context)
    {
        if (!context.Typography.Enabled) return null;
        Assembly provider;
        try { provider = Assembly.Load(new AssemblyName("wordnet.Ocr")); }
        catch (FileNotFoundException e) { throw new NotSupportedException("Typography.Provider: Reference matching wordnet.Ocr with native HarfBuzz assets.", e); }
        MethodInfo entry = provider.GetType("WordNet.Ocr.NativeTextShaper", true)!.GetMethod("Open", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new NotSupportedException("Typography.Provider: Incompatible provider.");
        return ((Func<ConversionContext, TypographySession>)Delegate.CreateDelegate(typeof(Func<ConversionContext, TypographySession>), entry))(context);
    }
    internal static void Prepare(DocumentModel model, ConversionContext context)
    {
        if (!context.Typography.Enabled || model.TypographyPrepared) return;
        if (context.Destination == DocumentFormat.Xps) throw new NotSupportedException("Typography.Output: Native typography supports PDF and page previews.");
        FontSource[] sources = context.Options.Fonts.Sources.ToArray();
        if (sources.Length > 128 || sources.Any(s => s == null)) throw new ArgumentException("Configure at most 128 non-null font faces.");
        if (sources.Sum(s => (long)s.Bytes.Length) > context.Options.MaximumExpandedBytes) throw new InvalidDataException("Limit.TypographyFonts: Caller fonts exceed expansion budget.");
        var coverage = new Dictionary<byte[], Func<string, bool>>();
        var bundled = Enumerable.Range(0, 4).Select(FixedLayout.FontBytes).ToArray();
        foreach (ParagraphBlock paragraph in model.Paragraphs)
        {
            context.CheckCancellation();
            if (paragraph.Alignment is ParagraphAlignment.Justified or ParagraphAlignment.Distributed) throw new NotSupportedException("Typography.Alignment: Native typography supports left, center and right alignment.");
            var prepared = new List<TextRun>();
            TextRun[] joined = RunCoalescer.Combine(paragraph.Runs).ToArray();
            for (int r = 0; r < joined.Length; r++)
            {
                TextRun run = joined[r];
                if (r > 0 && joined[r - 1].Text.Length > 0 && run.Text.Length > 0 && Arabic(joined[r - 1].Text[joined[r - 1].Text.Length - 1]) && Arabic(run.Text[0]))
                    throw new NotSupportedException("Typography.StyledJoining: A joined Arabic word must have one font and appearance.");
                if (run.Image != null || run.Box != null || run.Note != null || run.FieldInstruction != null || run.Bookmark != null || run.Text.Any(c => c == '\t')) throw new NotSupportedException("Typography.Structure: Native typography currently accepts ordinary text runs without fields, bookmarks, notes, tabs or inline drawings.");
                string? family = null; int? direction = null; var text = new System.Text.StringBuilder();
                TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(run.Text);
                while (elements.MoveNext())
                {
                    context.CheckCancellation();
                    string cluster = elements.GetTextElement();
                    if (CharUnicodeInfo.GetUnicodeCategory(cluster, 0) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark) throw new NotSupportedException("Typography.Cluster: A combining sequence requires a supported base character.");
                    foreach (char c in cluster)
                        if (!(c <= '\u024f' || Arabic(c) || c is '\u2013' or '\u2014' or '\u2018' or '\u2019' or '\u201c' or '\u201d' or '\u2026' || c is >= '\u0300' and <= '\u036f') || char.IsControl(c) && c is not '\n' and not '\t' and not '\f' and not '\v')
                            throw new NotSupportedException("Typography.Script: The optional profile accepts horizontal Arabic, Latin, digits, marks and common punctuation without directional controls.");
                    int rtl = Rtl(cluster) ? 1 : cluster.Any(char.IsDigit) ? 2 : cluster.Any(char.IsLetter) ? 0 : -1;
                    string selected = "Noto Sans";
                    bool bold = run.Bold || paragraph.HeadingLevel > 0;
                    FontSource? face = sources.Where(s => s.Bold == bold && s.Italic == run.Italic && Has(s.Bytes, cluster))
                        .OrderByDescending(s => string.Equals(s.FamilyName, run.Appearance.FontFamily, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                    if (face != null && (run.Appearance.FontFamily != null || !Has(bundled[(bold ? 1 : 0) + (run.Italic ? 2 : 0)], cluster))) selected = face.FamilyName;
                    else if (!Has(bundled[(bold ? 1 : 0) + (run.Italic ? 2 : 0)], cluster)) throw new NotSupportedException("Typography.Font: Supply a matching caller TrueType face covering each Arabic grapheme.");
                    // Whitespace stays with the preceding word so RTL shaping puts its advance on the correct side.
                    if (string.IsNullOrWhiteSpace(cluster) && text.Length > 0) { text.Append(cluster); continue; }
                    if (text.Length > 0 && (selected != family || rtl != direction)) Flush();
                    family = selected; direction = rtl; text.Append(cluster);
                }
                Flush();
                void Flush()
                {
                    if (text.Length == 0) return;
                    RunAppearance appearance = run.Appearance.Copy(); appearance.FontFamily = family;
                    context.RecordFont(run.Appearance.FontFamily ?? "Noto Sans", family ?? "Noto Sans");
                    prepared.Add(new TextRun(text.ToString(), run.Bold, run.Italic, run.Code, run.Link, run.FontSize, run.Color, appearance: appearance)); text.Clear();
                }
            }
            paragraph.Runs.Clear(); paragraph.Runs.AddRange(prepared);
        }
        model.TypographyPrepared = true;
        bool Has(byte[] bytes, string cluster)
        {
            if (!coverage.TryGetValue(bytes, out Func<string, bool>? has))
            {
                TrueTypeOutlineGuard.Validate(bytes);
                var cmap = TrueTypeFontParser.Parse(new TrueTypeDataBytes(bytes)).TableRegister.CMapTable;
                has = value => value.All(c => c is '\n' or '\t' or '\f' or '\v' || cmap != null && cmap.TryGetGlyphIndex(c, out int glyph) && glyph != 0); coverage.Add(bytes, has);
            }
            return has(cluster);
        }
    }
    internal static void Reorder(List<PositionedText> items, double left, bool rtl, TypographySession session)
    {
        if (items.Any(i => i.Image != null || i.Box != null || i.Text.Any(c => c == '\t'))) throw new NotSupportedException("Typography.InlineObject: Mixed-direction lines containing drawings or tabs are outside the bounded profile.");
        int Class(PositionedText item) => Rtl(item.Text) ? 1 : item.Text.Any(char.IsLetter) ? 0 : item.Text.Any(c => c is >= '\u0660' and <= '\u0669' or >= '\u06f0' and <= '\u06f9') ? 3 : item.Text.Any(char.IsDigit) ? 2 : -1;
        int[] classes = items.Select(Class).ToArray(); int[] levels = new int[classes.Length]; int[] directions = new int[classes.Length];
        int previous = rtl ? 1 : 0;
        for (int i = 0; i < classes.Length; i++)
        {
            int kind = classes[i];
            if (kind is 0 or 1) previous = kind;
            levels[i] = kind == 1 ? 1 : kind == 0 ? (rtl ? 2 : 0) : kind == 3 || kind == 2 && previous == 1 ? 2 : kind == 2 ? 0 : -1;
            directions[i] = kind < 0 ? -1 : kind is 2 or 3 ? (levels[i] == 0 ? 0 : 1) : kind;
        }
        var following = new int[levels.Length]; int next = rtl ? 1 : 0;
        for (int i = levels.Length - 1; i >= 0; i--) { following[i] = next; if (directions[i] >= 0) next = directions[i]; }
        previous = rtl ? 1 : 0;
        for (int i = 0; i < levels.Length; i++)
        {
            if (levels[i] < 0) { int direction = previous == following[i] ? previous : rtl ? 1 : 0; levels[i] = direction == 1 ? 1 : rtl ? 2 : 0; }
            else previous = directions[i];
        }
        for (int i = 0; i < items.Count; i++) if ((levels[i] & 1) != 0 && !Rtl(items[i].Text) && !items[i].Text.Any(char.IsLetterOrDigit))
        {
            PositionedText item = items[i]; ShapedWord shape = session.Shape(item.Text, item.Font, true);
            if (Math.Abs(shape.Width - item.NativeShape!.Width) > 0.000001) throw new NotSupportedException("Typography.Punctuation: Mirrored punctuation changes the measured word advance.");
            items[i] = item.Reshaped(shape);
        }
        int[] order = Enumerable.Range(0, items.Count).ToArray();
        for (int level = levels.DefaultIfEmpty(0).Max(); level >= 1; level--)
            for (int i = 0; i < order.Length; i++) if (levels[order[i]] >= level)
            {
                int end = i + 1; while (end < order.Length && levels[order[end]] >= level) end++;
                Array.Reverse(order, i, end - i); i = end - 1;
            }
        double x = left;
        foreach (int index in order) { PositionedText item = items[index]; items[index] = item.At(x, item.Y); x += item.NativeShape!.Width * item.Size; }
    }
}
