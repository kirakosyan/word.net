using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;
using WordNet.Internal;

namespace WordNet.Ocr;

// Installed before PdfDocument.Open: cross-reference, object, font, form and page
// streams are bounded before allocation. Image decode work is released per page;
// document content/resources keep an aggregate budget, including filter chains.
internal sealed class BoundedPdfFilters : IFilterProvider
{
    private readonly IFilterProvider inner;
    private readonly ConversionContext context;
    private long expanded, pageImages;
    private bool imageScope, exceeded;
    private long Used => imageScope ? pageImages : expanded;

    internal void BeginPage()
    {
        EnsureWithinBudget();
        pageImages = 0;
    }

    internal BoundedPdfFilters(IFilterProvider inner, ConversionContext context)
    { this.inner = inner; this.context = context; }

    public IReadOnlyList<IFilter> GetFilters(DictionaryToken dictionary) => Wrap(inner.GetFilters(dictionary));
    public IReadOnlyList<IFilter> GetNamedFilters(IReadOnlyList<NameToken> names) => Wrap(inner.GetNamedFilters(names));
    public IReadOnlyList<IFilter> GetAllFilters() => Wrap(inner.GetAllFilters());

    private IFilter[] Wrap(IReadOnlyList<IFilter> filters)
    {
        context.CheckCancellation();
        if (filters.Count > context.Options.MaximumNestingDepth)
            throw new InvalidDataException("Limit.NestingDepth: PDF filter chain exceeds the nesting limit.");
        return filters.Select(filter => (IFilter)new GuardedFilter(this, filter)).ToArray();
    }

    private void Charge(long count)
    {
        context.CheckCancellation();
        if (imageScope) pageImages = checked(pageImages + count);
        else expanded = checked(expanded + count);
        EnsureWithinBudget();
    }

    internal void EnsureWithinBudget()
    {
        context.CheckCancellation();
        exceeded |= expanded > context.Options.MaximumExpandedBytes || pageImages > context.Options.MaximumExpandedBytes;
        if (exceeded) throw new InvalidDataException("Limit.ExpandedBytes: Decoded PDF document streams or page images exceed MaximumExpandedBytes.");
    }

    private static int Number(DictionaryToken dictionary, NameToken key, int fallback)
        => dictionary.TryGet(key, out NumericToken value) ? value.Int : fallback;

    private void Predictor(DictionaryToken parameters)
    {
        if (Number(parameters, NameToken.Predictor, 1) <= 1) return;
        int colors = Number(parameters, NameToken.Colors, 1), bits = Number(parameters, NameToken.BitsPerComponent, 8), columns = Number(parameters, NameToken.Columns, 1);
        if (colors < 1 || colors > 32 || bits is not 1 and not 2 and not 4 and not 8 and not 16 || columns < 1)
            throw new InvalidDataException("Pdf.Predictor: Invalid predictor geometry.");
        // The provider allocates two complete scanlines before decoding any bytes.
        Charge(checked(((long)colors * bits * columns + 7) / 8 * 2));
    }

    private Memory<byte> Flate(Memory<byte> input, bool retain)
    {
        if (input.Length == 0) return Memory<byte>.Empty;
        if (input.Length < 2) throw new InvalidDataException("Pdf.Flate: Missing zlib header.");
        if (!MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)input, out ArraySegment<byte> bytes))
            bytes = new ArraySegment<byte>(input.ToArray());
        using var source = new MemoryStream(bytes.Array!, bytes.Offset, bytes.Count, writable: false);
        source.Position = 2; // RFC 1950 header; DeflateStream consumes RFC 1951 data.
        using var deflate = new DeflateStream(source, CompressionMode.Decompress);
        using var output = retain ? new MemoryStream() : null;
        var buffer = new byte[8192];
        int count;
        while ((count = deflate.Read(buffer, 0, buffer.Length)) != 0)
        {
            Charge(count);
            output?.Write(buffer, 0, count);
        }
        return output == null ? Memory<byte>.Empty : new Memory<byte>(output.GetBuffer(), 0, checked((int)output.Length));
    }

    private void RunLength(ReadOnlySpan<byte> input)
    {
        for (int offset = 0; offset < input.Length;)
        {
            int code = input[offset++];
            if (code == 128) return;
            int stored = code < 128 ? code + 1 : 1;
            if (stored > input.Length - offset) throw new InvalidDataException("Pdf.RunLength: Truncated run.");
            Charge(code < 128 ? stored : 257 - code);
            offset += stored;
        }
    }

    private void Lzw(ReadOnlySpan<byte> input, int earlyChange)
    {
        if (earlyChange is not 0 and not 1) throw new InvalidDataException("Pdf.Lzw: Invalid EarlyChange.");
        // Count decoded sequences without materialising them. The pinned provider
        // stores dictionary sequences too, so include that work in the admission.
        var lengths = new int[4096];
        for (int i = 0; i < 256; i++) lengths[i] = 1;
        int bit = 0, width = 9, next = 258, previousLength = 0;
        while ((long)bit + width <= (long)input.Length * 8)
        {
            int code = 0;
            for (int i = 0; i < width; i++, bit++) code = (code << 1) | ((input[bit / 8] >> (7 - bit % 8)) & 1);
            if (code == 257) return;
            if (code == 256) { width = 9; next = 258; previousLength = 0; continue; }
            if (code > next || code == next && previousLength == 0) throw new InvalidDataException("Pdf.Lzw: Invalid sequence code.");
            int length = code == next ? previousLength + 1 : lengths[code];
            Charge(length);
            if (previousLength != 0)
            {
                if (next == lengths.Length) throw new InvalidDataException("Limit.PdfLzwTable: LZW must clear its full dictionary before further additions.");
                lengths[next++] = previousLength + 1;
                Charge(previousLength + 1);
            }
            previousLength = length;
            if (width < 12 && next + earlyChange == 1 << width) width++;
        }
        throw new InvalidDataException("Pdf.Lzw: Missing end-of-data code.");
    }

    private sealed class GuardedFilter : IFilter
    {
        private readonly BoundedPdfFilters owner;
        private readonly IFilter filter;
        internal GuardedFilter(BoundedPdfFilters owner, IFilter filter) { this.owner = owner; this.filter = filter; }
        public bool IsSupported => filter.IsSupported;

        public Memory<byte> Decode(Memory<byte> input, DictionaryToken dictionary, IFilterProvider provider, int index)
        {
            owner.context.CheckCancellation();
            bool previousScope = owner.imageScope;
            owner.imageScope = dictionary.TryGet(NameToken.Subtype, out NameToken subtype) && subtype == NameToken.Image;
            try
            {
                long before = owner.Used;
                if (filter is FlateFilter or LzwFilter)
                {
                    DictionaryToken parameters = DecodeParameterResolver.GetFilterParameters(dictionary, index);
                    owner.Predictor(parameters);
                    if (filter is FlateFilter)
                    {
                        // No-predictor streams are inflated exactly once. The provider's
                        // predictor implementation remains guarded by a bounded preflight.
                        bool retain = Number(parameters, NameToken.Predictor, 1) <= 1;
                        Memory<byte> inflated = owner.Flate(input, retain);
                        if (retain) return inflated;
                    }
                    else owner.Lzw(input.Span, Number(parameters, NameToken.EarlyChange, 1));
                }
                else if (filter is RunLengthFilter) owner.RunLength(input.Span);
                else if (filter is Ascii85Filter) owner.Charge((long)input.Length * 4);
                else if (filter is AsciiHexDecodeFilter) owner.Charge(input.Length);
                else if (filter.IsSupported)
                {
                    int width = Number(dictionary, NameToken.Width, 0), height = Number(dictionary, NameToken.Height, 0);
                    if (width <= 0 || height <= 0) throw new InvalidDataException("Pdf.Image: Image filters require positive dimensions.");
                    // Admit a complete 32-bit decoder raster before entering native code.
                    // Charge the actual decoded bytes below, not this conservative bound.
                    long raster = checked((long)width * height * 4);
                    if (raster > owner.context.Options.MaximumExpandedBytes - owner.Used)
                    { owner.exceeded = true; owner.EnsureWithinBudget(); }
                }
                Memory<byte> decoded = filter.Decode(input, dictionary, provider, index);
                if (decoded.Length > owner.Used - before) owner.Charge(decoded.Length - (owner.Used - before));
                owner.context.CheckCancellation();
                return decoded;
            }
            finally { owner.imageScope = previousScope; }
        }
    }
}
