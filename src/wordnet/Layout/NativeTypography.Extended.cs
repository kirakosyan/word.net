using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UglyToad.PdfPig.Fonts.TrueType;
using UglyToad.PdfPig.Fonts.TrueType.Parser;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Layout;

internal static partial class NativeTypography
{
    internal static bool Devanagari(int c) => c is >= 0x0900 and <= 0x097f;
    internal static bool ExtendedCharacter(int c, bool devanagari) => c <= 0x024f || c is >= 0x0300 and <= 0x036f
        || c <= 0xffff && Arabic((char)c) || c is >= 0x2010 and <= 0x2027 or 0x2030 or 0x2039 or 0x203a or 0x200c or 0x200d
        || UnicodeBidi.Format(c) || devanagari && Devanagari(c);
    private static void PrepareExtended(DocumentModel model, ConversionContext context)
    {
        if (context.Destination == DocumentFormat.Xps) throw new NotSupportedException("Typography.Output: Native typography supports PDF and previews.");
        if (model.Tables.Any() || model.Notes.Count != 0 || model.Paragraphs.Count() != model.Blocks.OfType<ParagraphBlock>().Count()
            || model.Review.HasHistory || model.ObjectCount != 0 || model.Blocks.Select(b => b.Section).Concat(new[] { model.FinalSection }).Any(s => s?.Stories.Values.Any(v => v.Count > 0) == true))
            throw new NotSupportedException("Typography.Structure: Extended typography requires ordinary body paragraphs without tables, stories, objects or history.");
        FontSource[] sources = context.Options.Fonts.Sources.ToArray();
        if (sources.Length > 128 || sources.Any(s => s == null) || sources.Sum(s => (long)s.Bytes.Length) > context.Options.MaximumExpandedBytes)
            throw new InvalidDataException("Limit.TypographyFonts: Caller font count/bytes exceed the operation allowance.");
        var coverage = new Dictionary<byte[], Func<string, bool>>(); var bundled = Enumerable.Range(0, 4).Select(FixedLayout.FontBytes).ToArray();
        foreach (ParagraphBlock p in model.Blocks.Cast<ParagraphBlock>())
        {
            context.CheckCancellation();
            if (p.Alignment is ParagraphAlignment.Justified or ParagraphAlignment.Distributed)
                throw new NotSupportedException("Typography.Alignment: Native paragraphs require ordinary horizontal left, center or right alignment.");
            TextRun[] joined = RunCoalescer.Combine(p.Runs).ToArray();
            if (p.Control != null || p.List != null || joined.Any(r => r.Object != null || r.Control != null || r.Image != null || r.Box != null || r.Note != null || r.FieldInstruction != null || r.Bookmark != null || r.Text.Contains('\t')))
                throw new NotSupportedException("Typography.Structure: Fields, controls, drawings, notes, bookmarks and tabs require separate qualification.");
            string logical = string.Concat(joined.Select(r => r.Text));
            foreach (char c in logical) if (!ExtendedCharacter(c, context.Typography.Devanagari) || char.IsControl(c) && c is not '\n' and not '\f' and not '\v')
                throw new NotSupportedException("Typography.Script: This horizontal profile admits Arabic/Latin and optionally Hindi Devanagari, with balanced bidi controls.");
            int direction = context.Typography.Direction == TextDirection.RightToLeft || context.Typography.Direction == TextDirection.Auto && p.RightToLeft == true ? 1
                : context.Typography.Direction == TextDirection.LeftToRight || p.RightToLeft == false ? 0 : 2;
            UnicodeBidi.Result bidi = UnicodeBidi.Resolve(logical, direction, context, maximumDepth: context.Typography.MaximumBidiDepth);
            p.RightToLeft = bidi.BaseLevel == 1;
            int[] byOffset = new int[logical.Length];
            for (int i = 0; i < bidi.Offsets.Length; i++) byOffset[bidi.Offsets[i]] = bidi.Levels[i];
            List<int> boundaries = UnicodeBoundaries.Find(logical, false, context); var starts = new HashSet<int>(boundaries);
            var prepared = new List<TextRun>(); int offset = 0, boundary = 0;
            foreach (TextRun run in joined)
            {
                if (!starts.Contains(offset) || !starts.Contains(offset + run.Text.Length)) throw new NotSupportedException("Typography.StyledCluster: A grapheme/conjunct must have one appearance and font.");
                if (offset > 0 && offset < logical.Length && Arabic(logical[offset - 1]) && Arabic(logical[offset])) throw new NotSupportedException("Typography.StyledJoining: A joined Arabic word must have one appearance and font.");
                int end = offset + run.Text.Length; bool bold = run.Bold || p.HeadingLevel > 0;
                while (boundary + 1 < boundaries.Count && boundaries[boundary] < end)
                {
                    int at = boundaries[boundary], next = boundaries[++boundary]; string cluster = logical.Substring(at, next - at);
                    if (CharUnicodeInfo.GetUnicodeCategory(cluster, 0) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                        throw new NotSupportedException("Typography.Cluster: A combining cluster requires a supported base.");
                    string selected = "Noto Sans";
                    FontSource? face = sources.Where(s => s.Bold == bold && s.Italic == run.Italic && Has(s.Bytes, cluster))
                        .OrderByDescending(s => string.Equals(s.FamilyName, run.Appearance.FontFamily, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                    if (face != null && (run.Appearance.FontFamily != null || !Has(bundled[(bold ? 1 : 0) + (run.Italic ? 2 : 0)], cluster))) selected = face.FamilyName;
                    else if (!Has(bundled[(bold ? 1 : 0) + (run.Italic ? 2 : 0)], cluster)) throw new NotSupportedException("Typography.Font: Supply one caller face covering the entire grapheme/conjunct.");
                    int level = byOffset[at];
                    if (Enumerable.Range(at, next - at).Any(i => byOffset[i] != level && byOffset[i] >= 0)) throw new NotSupportedException("Typography.ClusterDirection: A grapheme cannot cross resolved directional levels.");
                    RunAppearance appearance = run.Appearance.Copy(); appearance.FontFamily = selected;
                    prepared.Add(new TextRun(cluster, run.Bold, run.Italic, run.Code, run.Link, run.FontSize, run.Color, appearance: appearance) { BidiLevel = level });
                    context.RecordFont(run.Appearance.FontFamily ?? "Noto Sans", selected);
                }
                offset = end;
            }
            p.Runs.Clear(); p.Runs.AddRange(RunCoalescer.Combine(prepared));
        }
        model.TypographyPrepared = true;
        bool Has(byte[] bytes, string cluster)
        {
            if (!coverage.TryGetValue(bytes, out Func<string, bool>? has))
            {
                TrueTypeOutlineGuard.Validate(bytes); var cmap = TrueTypeFontParser.Parse(new TrueTypeDataBytes(bytes)).TableRegister.CMapTable;
                has = value => value.All(c => UnicodeBidi.Format(c) || c is '\n' or '\f' or '\v' or '\u200c' or '\u200d' || cmap != null && cmap.TryGetGlyphIndex(c, out int glyph) && glyph != 0);
                coverage.Add(bytes, has);
            }
            return has(cluster);
        }
    }
    private static void ReorderExtended(List<PositionedText> items, double left, bool rtl, TypographySession session)
    {
        int[] levels = items.Select(i => i.SourceRun?.BidiLevel ?? throw new NotSupportedException("Typography.Level: A native token lacks its logical level.")).ToArray();
        for (int i = items.Count - 1; i >= 0 && items[i].Text.All(c => char.IsWhiteSpace(c) || UnicodeBidi.Format(c)); i--) levels[i] = rtl ? 1 : 0;
        int[] order = UnicodeBidi.Reorder(Enumerable.Range(0, items.Count).Where(i => levels[i] >= 0).ToArray(), levels);
        double x = left;
        foreach (int at in order)
        {
            PositionedText item = items[at];
            if (item.NativeShape == null) throw new NotSupportedException("Typography.Shape: A resolved token lacks native glyphs.");
            // Shaping already uses the resolved level, including mirrored brackets.
            items[at] = item.At(x, item.Y); x += item.NativeShape.Width * item.Size;
        }
        foreach (int i in Enumerable.Range(0, items.Count).Where(i => levels[i] < 0)) items[i] = items[i].At(left, items[i].Y);
    }
}
