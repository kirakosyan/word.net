using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UglyToad.PdfPig.Fonts.TrueType;
using UglyToad.PdfPig.Fonts.TrueType.Parser;
using WordNet.Fonts;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Layout;

internal sealed class FontResolver
{
    private readonly Dictionary<(string Family, bool Bold, bool Italic), Choice> choices = new Dictionary<(string Family, bool Bold, bool Italic), Choice>();
    private readonly ConversionContext context;
    private readonly DocumentModel model;
    private readonly FontSource[] sources;
    private readonly Dictionary<string, string> substitutions;
    private readonly string fallback;
    private readonly Dictionary<int, int> defaultFaces = new Dictionary<int, int>();
    private readonly Dictionary<byte[], int> sourceIndices = new Dictionary<byte[], int>();
    private readonly Dictionary<byte[], Func<char, bool>> coverage = new Dictionary<byte[], Func<char, bool>>();
    private readonly char[] characters;
    private bool recording;
    internal List<byte[]> Data { get; } = new List<byte[]>();
    internal int MathIndex { get; private set; } = -1;
    internal MathFont? Math { get; private set; }

    internal FontResolver(DocumentModel model, ConversionContext context, bool recordUsage = false)
    {
        this.context = context; this.model = model; recording = recordUsage;
        characters = Enumerable.Range(32, 95).Select(i => (char)i)
            .Concat(model.Title + model.Author)
            .Concat(model.Paragraphs.Where(p => p.List?.Definition != null).SelectMany(p => p.List!.Definition!).SelectMany(l => l.Pattern))
            .Concat(model.Paragraphs.SelectMany(p => p.Runs).SelectMany(r => r.Text + r.FieldInstruction)).Distinct().ToArray();
        FontOptions options = context.Options.Fonts ?? throw new ArgumentException("Font options must not be null.");
        sources = options.Sources.ToArray();
        substitutions = new Dictionary<string, string>(options.Substitutions, StringComparer.OrdinalIgnoreCase);
        fallback = options.DefaultFallback;
        if (string.IsNullOrWhiteSpace(fallback)) throw new ArgumentException("Fallback font family must not be empty.");
        if (sources.Length > 128 || sources.Any(s => s == null)) throw new ArgumentException("Configure at most 128 non-null caller font faces.");
        foreach (ParagraphBlock paragraph in model.Paragraphs)
        {
            context.CheckCancellation();
            using var location = context.At(paragraph.Location);
            if (paragraph.List != null)
            {
                _ = Resolve(new TextRun(""), paragraph.HeadingLevel > 0);
                if (paragraph.List.Definition != null) foreach (ListLevel level in paragraph.List.Definition) _ = Resolve(level.Appearance.WithText(level.Pattern), paragraph.HeadingLevel > 0);
            }
            foreach (TextRun run in RunCoalescer.Combine(paragraph.Runs)) _ = Resolve(run, paragraph.HeadingLevel > 0);
        }
        if (model.ObjectCount != 0 && model.Paragraphs.SelectMany(p => p.Runs).Any(r => r.Object?.Equation != null))
        {
            FontSource? source = sources.Where(s => !s.Bold && !s.Italic && string.Equals(s.FamilyName, options.MathFamily, StringComparison.OrdinalIgnoreCase)).OrderBy(s => System.Math.Abs(s.Weight - 400)).FirstOrDefault();
            if (source == null || string.IsNullOrWhiteSpace(options.MathFamily)) throw new NotSupportedException("Equation.Font: Configure Fonts.MathFamily and an exact caller-supplied static MATH face.");
            if (source.Bytes.Length * 8L > context.Options.MaximumExpandedBytes) throw new InvalidDataException("Equation.FontBudget: Math metrics and outlines exceed their operation allowance.");
            Math = new MathFont(source.Bytes, context);
            if (!sourceIndices.TryGetValue(source.Bytes, out int index)) { index = Data.Count; Data.Add(source.Bytes); sourceIndices.Add(source.Bytes, index); }
            MathIndex = index;
        }
        recording = true;
    }

    internal int Resolve(TextRun run, bool heading)
    {
        bool bold = run.Bold || heading, italic = run.Italic;
        string family = run.Appearance.FontFamily ?? "Noto Sans";
        var key = (family.ToUpperInvariant(), bold, italic);
        if (!choices.TryGetValue(key, out Choice? choice))
        {
            FontSource? selected = Find(family); string actual = family;
            if (selected == null && !family.Equals("Noto Sans", StringComparison.OrdinalIgnoreCase))
            {
                if (substitutions.TryGetValue(family, out string? replacement))
                { selected = Find(replacement) ?? Bundled(replacement); actual = selected?.FamilyName ?? replacement; }
                if (selected == null) { selected = Bundled(family); if (selected != null) actual = selected.FamilyName; }
                if (selected == null) { selected = Find(fallback) ?? Bundled(fallback); actual = selected?.FamilyName ?? "Noto Sans"; }
            }
            choice = new Choice(selected, actual); choices.Add(key, choice);
        }
        FontSource? source = choice.Source; string resolved = choice.Family;
        if (source != null && context.EffectiveMode != FidelityMode.Strict)
        {
            Func<char, bool> hasGlyph = Coverage(source.Bytes);
            if (!hasGlyph('?'))
            {
                string text = run.FieldInstruction == null ? run.Text : DocumentFields.Render(run, model, 1, 1, 1, context);
                if (run.FieldInstruction != null && DocumentFields.Kind(run.FieldInstruction) is "PAGE" or "NUMPAGES" or "SECTIONPAGES") text += "0123456789";
                if (text.Any(c => c is not '\n' and not '\t' && (!hasGlyph(c) || char.IsSurrogate(c))))
                {
                    source = null; resolved = "Noto Sans";
                    context.ReportStrictRejection("Font.MissingGlyph", "A face without a usable replacement glyph uses the bundled glyph font for the affected run.");
                }
            }
        }
        if (!family.Equals(resolved, StringComparison.OrdinalIgnoreCase) && context.RecordSubstitution(family))
            context.Loss("Font.Substituted", "A requested font family uses a fallback; inspect font resolution metrics for family names.");
        int index;
        if (source == null)
        {
            int face = (bold ? 1 : 0) + (italic ? 2 : 0);
            if (!defaultFaces.TryGetValue(face, out index))
            { index = Data.Count; Data.Add(FixedLayout.FontBytes(face)); defaultFaces.Add(face, index); }
        }
        else if (!sourceIndices.TryGetValue(source.Bytes, out index))
        { index = Data.Count; Data.Add(CffFontConverter.Prepare(source.Bytes, characters, context)); sourceIndices.Add(source.Bytes, index); }
        if (recording) context.RecordFont(family, resolved);
        return index;

        FontSource? Find(string name) => sources.Where(s => s.FamilyName.Equals(name, StringComparison.OrdinalIgnoreCase) && s.Bold == bold && s.Italic == italic)
            .OrderBy(s => System.Math.Abs(s.Weight - (bold ? 700 : 400))).FirstOrDefault();
        FontSource? Bundled(string name)
        {
            var bundled = BundledFonts.Read(name, bold, italic);
            if (!bundled.HasValue) return null;
            using var stream = new MemoryStream(bundled.Value.Bytes, writable: false);
            return new FontSource(bundled.Value.Family, stream, bold, italic);
        }
    }

    private Func<char, bool> Coverage(byte[] bytes)
    {
        if (coverage.TryGetValue(bytes, out Func<char, bool>? result)) return result;
        var cmap = TrueTypeFontParser.Parse(new TrueTypeDataBytes(bytes)).TableRegister.CMapTable;
        result = c => cmap != null && cmap.TryGetGlyphIndex(c, out int glyph) && glyph != 0;
        coverage.Add(bytes, result); return result;
    }

    private sealed class Choice
    {
        internal Choice(FontSource? source, string family) { Source = source; Family = family; }
        internal FontSource? Source { get; }
        internal string Family { get; }
    }
}
