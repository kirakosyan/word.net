using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UglyToad.PdfPig.Fonts.TrueType;
using UglyToad.PdfPig.Fonts.TrueType.Parser;
using WordNet.Fonts;
using WordNet.Layout;
using WordNet.Model;

namespace WordNet.Internal;

internal static class ControlDisplay
{
    internal static void Destination(DocumentModel model, DocumentFormat format, ConversionContext c)
    {
        bool fixedOutput = format is DocumentFormat.Pdf or DocumentFormat.Xps;
        if (!model.Controls.HasControls)
        {
            if (fixedOutput && model.HasControlDisplay) PrepareFixed(model, c);
            return;
        }
        if (format == DocumentFormat.Docx)
        {
            if (model.Controls.Items.Values.Any(d => d.BindingState == ContentControlBindingState.Dirty)) throw new InvalidOperationException("Control.BindingDirty: Explicitly apply, write back or detach edited bindings before DOCX save.");
            if (model.Controls.Items.Values.Any(d => d.BindingState == ContentControlBindingState.Unevaluated)) c.Report("Control.BindingNotApplied", "Preserved local bindings were not applied by this session; another consumer can refresh them on open.");
            return;
        }
        if (fixedOutput)
        {
            if (c.Typography.Enabled || c.Options.Pdf.Accessibility != PdfAccessibility.None || c.Options.Pdf.Conformance != PdfConformance.Pdf17)
                throw new NotSupportedException("Control.OutputProfile: Typed controls currently qualify ordinary managed output only.");
            c.Report("Control.StaticDisplay", "Typed controls are displayed as selectable static content, not interactive form fields.");
        }
        else c.Loss("Control.OutputLoss", "This destination keeps explicit control display without editable controls or local binding metadata.");
        if (fixedOutput) { MarkDisplay(model, c); PrepareFixed(model, c); }
        else Flatten(model, c);
    }
    private static void MarkDisplay(DocumentModel model, ConversionContext c)
    {
        var owners = ControlOwners.Index(model, c);
        foreach (ControlData data in model.Controls.Items.Values)
        {
            ControlGlyph? hint = null;
            if (data.Kind == ContentControlKind.Checkbox && !data.Placeholder)
            { var glyph = ControlValues.Glyph(data); hint = new ControlGlyph(glyph.Text, glyph.Font, data.Checked); }
            foreach (TextRun run in owners[data.Id].Runs)
            { c.ReserveControlWork(1); run.ControlDisplay = true; run.ControlGlyph = hint; }
        }
        model.HasControlDisplay = true;
    }
    private static void PrepareFixed(DocumentModel model, ConversionContext c)
    {
        FontOptions options = c.Options.Fonts ?? throw new ArgumentException("Font options must not be null.");
        FontSource[] sources = options.Sources.ToArray();
        if (sources.Length > 128 || sources.Any(f => f == null)) throw new ArgumentException("Configure at most 128 non-null caller font faces.");
        long faceBytes = 0;
        var faces = new Dictionary<(string Family, bool Bold, bool Italic), (TrueTypeFont? Font, string Family)>();
        char[] characters = model.Paragraphs.SelectMany(p => p.Runs).Where(r => r.ControlGlyph != null).Select(r => r.ControlGlyph!.Text[0]).Distinct().ToArray();
        foreach (ParagraphBlock paragraph in model.Paragraphs)
            for (int runIndex = 0; runIndex < paragraph.Runs.Count; runIndex++)
            {
                c.ReserveControlWork(1);
                TextRun run = paragraph.Runs[runIndex];
                if (!run.ControlDisplay) continue;
                if (!c.Typography.Enabled && run.Appearance.Underline && (run.Text.Contains('\t') || paragraph.Alignment is ParagraphAlignment.Justified or ParagraphAlignment.Distributed))
                    throw new NotSupportedException("Control.UnderlineProfile: Managed control underlines currently require ordinary alignment without tab runs.");
                // Ordinary view edits can replace the original glyph; never restore it over caller text.
                if (run.ControlGlyph is not ControlGlyph glyph || run.Text != glyph.Text) continue;
                using var location = c.At(paragraph.Location);
                bool bold = run.Bold, italic = run.Italic;
                var key = (glyph.Font.ToUpperInvariant(), bold, italic);
                if (!faces.TryGetValue(key, out var face))
                {
                    string family = glyph.Font;
                    FontSource? supplied = Find(family);
                    if (supplied == null && options.Substitutions.TryGetValue(family, out string? replacement)) { family = replacement; supplied = Find(family); }
                    byte[]? bytes = supplied?.Bytes;
                    if (bytes == null && family.Equals("Noto Sans", StringComparison.OrdinalIgnoreCase)) bytes = FixedLayout.FontBytes((bold ? 1 : 0) + (italic ? 2 : 0));
                    if (bytes == null)
                    {
                        var bundled = BundledFonts.Read(family, bold, italic);
                        if (bundled.HasValue) { bytes = bundled.Value.Bytes; family = bundled.Value.Family; }
                    }
                    TrueTypeFont? font = null;
                    if (bytes != null)
                    {
                        if (bytes.Length * 8L > c.Options.MaximumExpandedBytes) throw new InvalidDataException("Limit.Controls: Checkbox font parsing exceeds the expanded resource bound.");
                        c.ReserveControlWork(bytes.Length / 64 + 1);
                        byte[] prepared = CffFontConverter.Prepare(bytes, characters, c);
                        faceBytes = checked(faceBytes + prepared.Length * 8L);
                        if (faceBytes > c.Options.MaximumExpandedBytes) throw new InvalidDataException("Limit.Controls: Aggregate checkbox font parsing exceeds the expanded resource bound.");
                        font = TrueTypeFontParser.Parse(new TrueTypeDataBytes(prepared));
                    }
                    face = (font, supplied?.FamilyName ?? family); faces.Add(key, face);
                    FontSource? Find(string name) => sources.Where(f => f.FamilyName.Equals(name, StringComparison.OrdinalIgnoreCase) && f.Bold == bold && f.Italic == italic).OrderBy(f => Math.Abs(f.Weight - (bold ? 700 : 400))).FirstOrDefault();
                }
                int index = 0; bool available = face.Font?.TableRegister.CMapTable?.TryGetGlyphIndex(glyph.Text[0], out index) == true && index != 0;
                string text, fontFamily;
                if (available)
                {
                    text = glyph.Text; fontFamily = face.Family;
                    if (!face.Family.Equals(glyph.Font, StringComparison.OrdinalIgnoreCase)) c.Report("Control.GlyphFallback", "A declared checkbox face uses an admitted display replacement; editable DOCX metadata is unchanged.");
                }
                else { text = glyph.Checked ? "[x]" : "[ ]"; fontFamily = "Noto Sans"; c.Report("Control.GlyphFallback", "An unavailable declared checkbox glyph/font uses selectable ASCII display; editable DOCX metadata is unchanged."); }
                ControlValues.CheckText(text, c);
                RunAppearance appearance = run.Appearance.Copy(); appearance.FontFamily = fontFamily;
                var replacementRun = new TextRun(text, run.Bold, run.Italic, run.Code, run.Link, run.FontSize, run.Color, appearance: appearance).RetainMetadata(run);
                replacementRun.ControlGlyph = null; paragraph.Runs[runIndex] = replacementRun;
            }
    }
    internal static void Flatten(DocumentModel model, ConversionContext c)
    {
        MarkDisplay(model, c);
        foreach (DocumentBlock block in DocumentModel.Expand(model.Blocks))
        {
            block.TypedControlId = null;
            if (block is ParagraphBlock p) foreach (TextRun run in p.Runs) run.TypedControlId = null;
        }
        model.Controls = new ContentControlStore();
    }
    internal static void CheckArchive(Stream output, ConversionContext c)
    {
        output.Position = 0;
        using (var archive = new ZipArchive(output, ZipArchiveMode.Read, true))
        {
            if (archive.Entries.Count > c.Options.MaximumArchiveEntries) throw new InvalidDataException("Limit.ArchiveEntries: Staged control DOCX has too many entries.");
            long expanded = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            { c.CheckCancellation(); expanded = checked(expanded + entry.Length); if (expanded > c.Options.MaximumExpandedBytes) throw new InvalidDataException("Limit.ExpandedBytes: Staged control DOCX exceeds aggregate expansion limits."); }
        }
        output.Position = 0;
    }
}
