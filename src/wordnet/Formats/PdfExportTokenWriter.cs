using System.IO;
using System.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;
using UglyToad.PdfPig.Writer;

namespace WordNet.Formats;

internal class PdfExportTokenWriter : TokenWriter
{
    private readonly bool compress;
    private readonly byte[]? metadata;
    private readonly string[]? labels;
    private readonly DictionaryToken? shading;
    internal PdfExportTokenWriter(bool compress, byte[]? metadata = null, string[]? labels = null, DictionaryToken? shading = null) { this.compress = compress; this.metadata = metadata; this.labels = labels; this.shading = shading; }

    protected override void WriteObject(ObjectToken objectToken, Stream outputStream)
    {
        IToken token = metadata == null ? objectToken.Data : ArchiveToken(objectToken.Data);
        if (shading != null && token is DictionaryToken page && page.TryGet(NameToken.Type, out NameToken pageType) && pageType.Data == "Page")
        {
            var resourceName = NameToken.Create("Resources");
            System.Collections.Generic.Dictionary<NameToken, IToken> resourceEntries;
            if (!page.TryGet(resourceName, out IToken rawResources)) resourceEntries = new System.Collections.Generic.Dictionary<NameToken, IToken>();
            else if (rawResources is DictionaryToken resources && !resources.Data.ContainsKey("Shading"))
                resourceEntries = resources.Data.ToDictionary(p => NameToken.Create(p.Key), p => p.Value);
            else throw new InvalidDataException("Shape.Gradient: Unexpected PDF page resource layout.");
            var pageEntries = page.Data.ToDictionary(p => NameToken.Create(p.Key), p => p.Value);
            resourceEntries[NameToken.Create("Shading")] = shading; pageEntries[resourceName] = new DictionaryToken(resourceEntries);
            token = new DictionaryToken(pageEntries);
        }
        if (labels != null && token is DictionaryToken dictionary && dictionary.TryGet(NameToken.Type, out NameToken type) && type.Data == "Catalog")
        {
            var entries = dictionary.Data.ToDictionary(p => NameToken.Create(p.Key), p => p.Value);
            entries[NameToken.Create("PageLabels")] = new DictionaryToken(new System.Collections.Generic.Dictionary<NameToken, IToken>
            { [NameToken.Create("Nums")] = new ArrayToken(labels.SelectMany((label, index) => new IToken[] { new NumericToken(index), new DictionaryToken(new System.Collections.Generic.Dictionary<NameToken, IToken> { [NameToken.Create("P")] = new StringToken(label) }) }).ToArray()) });
            token = new DictionaryToken(entries);
        }
        base.WriteObject(new ObjectToken(objectToken.Position, objectToken.Number, token), outputStream);
    }

    private static IToken ArchiveToken(IToken token)
    {
        if (token is ArrayToken array) return new ArrayToken(array.Data.Select(ArchiveToken).ToArray());
        if (token is DictionaryToken dictionary)
        {
            var entries = dictionary.Data.ToDictionary(p => NameToken.Create(p.Key), p => ArchiveToken(p.Value));
            // PDF/A-2 6.3.2: annotations need explicit flags. Links have no
            // appearance stream; Print is set and Invisible/Hidden/NoView are clear.
            if (dictionary.TryGet(NameToken.Type, out NameToken kind) && kind.Data == "Annot"
                && dictionary.TryGet(NameToken.Subtype, out NameToken subtype) && subtype.Data == "Link")
                entries[NameToken.Create("F")] = new NumericToken(4);
            return new DictionaryToken(entries);
        }
        return token;
    }

    protected override void WriteStream(StreamToken streamToken, Stream outputStream)
    {
        DictionaryToken dictionary = streamToken.StreamDictionary;
        if (metadata != null && dictionary.TryGet(NameToken.Type, out NameToken kind) && kind.Equals(NameToken.Metadata))
        {
            // The provider merges supplied XMP with generated properties, which can
            // duplicate RDF properties. Replace its completed packet atomically.
            var entries = dictionary.Data.ToDictionary(p => NameToken.Create(p.Key), p => p.Value);
            entries.Remove(NameToken.Filter); entries.Remove(NameToken.DecodeParms); entries.Remove(NameToken.Length1);
            entries[NameToken.Length] = new NumericToken(metadata.Length);
            base.WriteStream(new StreamToken(new DictionaryToken(entries), metadata), outputStream);
            return;
        }
        streamToken = PdfImagePrediction.Apply(streamToken);
        dictionary = streamToken.StreamDictionary;
        // The provider adds Length1 to both text and font-program streams.
        // Preserve image predictors and JPEG bytes regardless of this option.
        if (!compress && dictionary.Data.Keys.All(k => k == NameToken.Length.Data || k == NameToken.Length1.Data || k == NameToken.Filter.Data)
            && dictionary.TryGet(NameToken.Filter, out IToken filter) && Deflate(filter))
        {
            var decoded = streamToken.Decode(DefaultFilterProvider.Instance);
            var entries = dictionary.Data.ToDictionary(p => NameToken.Create(p.Key), p => p.Value);
            entries.Remove(NameToken.Filter); entries[NameToken.Length] = new NumericToken(decoded.Length);
            streamToken = new StreamToken(new DictionaryToken(entries), decoded);
        }
        base.WriteStream(streamToken, outputStream);
    }

    private static bool Deflate(IToken token) => token.Equals(NameToken.FlateDecode)
        || token is ArrayToken array && array.Data.Count == 1 && array.Data[0].Equals(NameToken.FlateDecode);
}
