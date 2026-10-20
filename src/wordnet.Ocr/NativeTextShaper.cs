using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using HarfBuzzSharp;
using WordNet.Internal;
using WordNet.Layout;

namespace WordNet.Ocr;

internal sealed class NativeTextShaper : TypographySession
{
    private readonly ConversionContext context;
    private readonly Dictionary<int, (Blob Blob, Face Face, Font Font)> fonts = new Dictionary<int, (Blob, Face, Font)>();
    private int work;
    private NativeTextShaper(ConversionContext context) { this.context = context; }
    internal static TypographySession Open(ConversionContext context) => new NativeTextShaper(context);
    internal override ShapedWord Shape(string text, int index, bool rtl)
    {
        context.CheckCancellation();
        if (text.Length > 16384) throw new InvalidDataException("Limit.TypographyToken: Shaping tokens are bounded to 16384 UTF-16 units.");
        if (!fonts.TryGetValue(index, out var entry))
        {
            byte[] data = context.FontData[index]; TrueTypeOutlineGuard.Validate(data);
            // FromStream retains an unpinned temporary managed array in this binding.
            // Duplicate while pinned so HarfBuzz owns stable native storage for the face.
            GCHandle pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
            Blob blob;
            try { blob = new Blob(pinned.AddrOfPinnedObject(), data.Length, MemoryMode.Duplicate); }
            finally { pinned.Free(); }
            Face? face = null; Font? font = null;
            try { face = new Face(blob, 0); font = new Font(face); font.SetFunctionsOpenType(); font.SetScale(face.UnitsPerEm, face.UnitsPerEm); entry = (blob, face, font); fonts.Add(index, entry); }
            catch { font?.Dispose(); face?.Dispose(); blob.Dispose(); throw; }
        }
        using var buffer = new HarfBuzzSharp.Buffer(); buffer.AddUtf16(text); buffer.GuessSegmentProperties(); buffer.Direction = rtl ? Direction.RightToLeft : Direction.LeftToRight;
        if (context.Typography.Devanagari && text.Any(c => NativeTypography.Devanagari(c))) { buffer.Script = Script.Devanagari; buffer.Language = new Language("hi"); }
        entry.Font.Shape(buffer);
        context.CheckCancellation(); var infos = buffer.GlyphInfos; var positions = buffer.GlyphPositions;
        work = checked(work + infos.Length); if (work > context.Typography.MaximumGlyphs) throw new InvalidDataException("Limit.TypographyGlyphs: Shaping exceeds the per-pass glyph work budget.");
        var glyphs = new ShapedGlyph[infos.Length];
        for (int i = 0; i < glyphs.Length; i++) { if (infos[i].Codepoint == 0) throw new NotSupportedException("Typography.Glyph: The selected face lacks a shaped glyph."); glyphs[i] = new ShapedGlyph((int)infos[i].Codepoint, (int)infos[i].Cluster, positions[i].XAdvance, positions[i].XOffset, positions[i].YOffset); }
        return new ShapedWord(glyphs, entry.Face.UnitsPerEm);
    }
    public override void Dispose() { foreach (var entry in fonts.Values) { entry.Font.Dispose(); entry.Face.Dispose(); entry.Blob.Dispose(); } fonts.Clear(); }
}
