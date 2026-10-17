using System.Collections.Generic;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.TrueType;
using UglyToad.PdfPig.Fonts.TrueType.Parser;
using WordNet.Internal;

namespace WordNet.Layout;

// Scoped to one conversion, including all pages of a preview. Font byte arrays use
// reference identity, so a later layout font registration cannot reuse stale entries.
internal sealed class RenderFontCache
{
    internal sealed class Face
    {
        private readonly byte[] bytes;
        private readonly TrueTypeOutlines decoder;
        private TrueTypeFont? font;
        private readonly Dictionary<int, IReadOnlyList<PdfSubpath>> paths = new Dictionary<int, IReadOnlyList<PdfSubpath>>();
        internal Dictionary<int, string> SvgPaths { get; } = new Dictionary<int, string>();
        internal Face(byte[] bytes, ConversionContext context) { this.bytes = bytes; decoder = new TrueTypeOutlines(bytes, context); }
        // The decoder already validates the font's outline admission before parsing.
        internal TrueTypeFont Font => font ??= TrueTypeFontParser.Parse(new TrueTypeDataBytes(bytes));
        internal IReadOnlyList<PdfSubpath> Paths(int glyph)
        { if (!paths.TryGetValue(glyph, out IReadOnlyList<PdfSubpath>? value)) paths.Add(glyph, value = decoder.Paths(glyph)); return value; }
    }
    private readonly Dictionary<byte[], Face> faces = new Dictionary<byte[], Face>();
    private readonly ConversionContext context;
    internal RenderFontCache(ConversionContext context) { this.context = context; }
    internal Face Get(int index)
    {
        byte[] bytes = context.FontData[index];
        if (!faces.TryGetValue(bytes, out Face? face)) faces.Add(bytes, face = new Face(bytes, context));
        return face;
    }
}
