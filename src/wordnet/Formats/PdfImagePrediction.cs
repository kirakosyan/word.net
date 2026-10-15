using System.IO;
using System.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace WordNet.Formats;

internal static class PdfImagePrediction
{
    internal static StreamToken Apply(StreamToken stream)
    {
        DictionaryToken dictionary = stream.StreamDictionary;
        if (!dictionary.TryGet(NameToken.Subtype, out NameToken subtype) || !subtype.Equals(NameToken.Image)
            || dictionary.Data.ContainsKey(NameToken.DecodeParms.Data)
            || !dictionary.TryGet(NameToken.Filter, out IToken filter) || !Deflate(filter)
            || !dictionary.TryGet(NameToken.Width, out NumericToken width) || !dictionary.TryGet(NameToken.Height, out NumericToken height)
            || !dictionary.TryGet(NameToken.BitsPerComponent, out NumericToken bits) || bits.Int != 8
            || !dictionary.TryGet(NameToken.ColorSpace, out NameToken color)) return stream;
        int colors = color.Data == "DeviceRGB" ? 3 : color.Data == "DeviceGray" ? 1 : 0;
        if (colors == 0 || width.Int <= 0 || height.Int <= 0 || (long)width.Int * colors * height.Int > int.MaxValue - height.Int) return stream;
        int stride = width.Int * colors;
        byte[] raw = stream.Decode(DefaultFilterProvider.Instance).ToArray();
        if (raw.Length != (long)stride * height.Int) return stream;
        var filtered = new byte[raw.Length + height.Int];
        for (int row = 0; row < height.Int; row++)
        {
            int source = row * stride, target = row * (stride + 1);
            filtered[target] = 1; // PNG Sub filter, with one filter byte per row.
            for (int column = 0; column < stride; column++)
                filtered[target + 1 + column] = unchecked((byte)(raw[source + column] - (column < colors ? 0 : raw[source + column - colors])));
        }
        using var input = new MemoryStream(filtered, writable: false);
        byte[] encoded = new FlateFilter().Encode(input, dictionary);
        var values = dictionary.Data.ToDictionary(p => NameToken.Create(p.Key), p => p.Value);
        values[NameToken.Length] = new NumericToken(encoded.Length);
        values[NameToken.DecodeParms] = new DictionaryToken(new System.Collections.Generic.Dictionary<NameToken, IToken>
        {
            [NameToken.Predictor] = new NumericToken(15),
            [NameToken.Colors] = new NumericToken(colors),
            [NameToken.Columns] = new NumericToken(width.Int),
            [NameToken.BitsPerComponent] = new NumericToken(8),
        });
        return new StreamToken(new DictionaryToken(values), encoded);
    }

    private static bool Deflate(IToken token) => token.Equals(NameToken.FlateDecode)
        || token is ArrayToken array && array.Data.Count == 1 && array.Data[0].Equals(NameToken.FlateDecode);
}
