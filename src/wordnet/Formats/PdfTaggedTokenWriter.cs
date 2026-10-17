using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;
using UglyToad.PdfPig.Writer;
using WordNet.Internal;

namespace WordNet.Formats;

// Buffer only the opt-in path: real page/annotation references are available at xref time.
internal sealed class PdfTaggedTokenWriter : PdfExportTokenWriter, ITokenWriter
{
    private readonly PdfTags tags;
    private readonly ConversionContext context;
    private readonly byte[] xmp;
    private readonly Dictionary<IndirectReference, IToken> objects = new Dictionary<IndirectReference, IToken>();
    private long size;
    internal PdfTaggedTokenWriter(bool compress, string[]? labels, PdfTags tags, byte[] xmp, ConversionContext context) : base(compress, labels: labels) { this.tags = tags; this.xmp = xmp; this.context = context; }
    protected override void WriteObject(ObjectToken token, Stream output)
    {
        context.CheckCancellation();
        size = checked(size + Estimate(token.Data));
        if (objects.Count >= 100000 || size > context.Options.MaximumExpandedBytes) throw new InvalidDataException("Limit.PdfTags: Buffered PDF tokens exceed object or expanded-byte budget.");
        objects.Add(token.Number, token.Data);
    }
    private static long Estimate(IToken token) => token is StreamToken stream ? stream.Data.Length + Estimate(stream.StreamDictionary) : token is ArrayToken array ? 32 + array.Data.Sum(Estimate) : token is DictionaryToken dictionary ? 64 + dictionary.Data.Sum(p => 32 + p.Key.Length * 2L + Estimate(p.Value)) : token is StringToken text ? 32 + text.Data.Length * 2L : 32;
    private static NameToken N(string name) => NameToken.Create(name);
    private static DictionaryToken D(params (string Key, IToken Value)[] values) => new DictionaryToken(values.ToDictionary(p => N(p.Key), p => p.Value));
    private static IndirectReferenceToken R(IndirectReference reference) => new IndirectReferenceToken(reference);
    private static Dictionary<NameToken, IToken> Copy(DictionaryToken dictionary) => dictionary.Data.ToDictionary(p => N(p.Key), p => p.Value);
    void ITokenWriter.WriteCrossReferenceTable(IReadOnlyDictionary<IndirectReference, long> offsets, IndirectReference root, Stream output, IndirectReference? info, long? previousCrossReferenceTableOffset)
    {
        context.CheckCancellation();
        long next = objects.Keys.Max(r => r.ObjectNumber) + 1;
        IndirectReference New() => new IndirectReference(next++, 0);
        var catalog = (DictionaryToken)objects[root];
        var pages = new List<IndirectReference>();
        void Walk(IndirectReference reference, int depth)
        {
            if (depth > 32) throw new InvalidDataException("Pdf.Tags: Excessive page tree depth.");
            var dictionary = (DictionaryToken)objects[reference];
            if (dictionary.TryGet(N("Type"), out NameToken type) && type.Data == "Page") pages.Add(reference);
            else if (dictionary.TryGet(N("Kids"), out ArrayToken kids)) foreach (IndirectReferenceToken child in kids.Data) Walk(child.Data, depth + 1);
        }
        Walk(((IndirectReferenceToken)catalog.Data["Pages"]).Data, 0);
        if (pages.Count != tags.Pages.Count) throw new InvalidDataException("Pdf.Tags: Page mapping does not match exported layout.");
        IndirectReference structure = New(), parentTree = New(), metadata = New();
        var nodes = new Dictionary<PdfTags.Node, IndirectReference>();
        void Assign(PdfTags.Node node) { nodes.Add(node, New()); foreach (PdfTags.Node child in node.Children.Where(c => c.Used)) Assign(child); }
        Assign(tags.Root);
        var parents = new SortedDictionary<int, IToken>();
        int annotationKey = pages.Count;
        var annotations = new Dictionary<(int Page, int Index), IndirectReference>();
        for (int p = 0; p < pages.Count; p++)
        {
            var page = (DictionaryToken)objects[pages[p]]; var entries = Copy(page); entries[N("StructParents")] = new NumericToken(p); entries[N("Tabs")] = N("S"); objects[pages[p]] = new DictionaryToken(entries);
            parents.Add(p, new ArrayToken(tags.Pages[p].Select(n => (IToken)R(nodes[n])).ToArray()));
            var annots = page.TryGet(N("Annots"), out ArrayToken array) ? array : new ArrayToken(Array.Empty<IToken>());
            if (annots.Data.Count != tags.Annotations[p].Count) throw new InvalidDataException("Pdf.Tags: Link annotation mapping does not match exported content.");
            var annotationRefs = new List<IToken>();
            for (int i = 0; i < annots.Data.Count; i++)
            {
                IndirectReference reference;
                if (annots.Data[i] is IndirectReferenceToken existing) reference = existing.Data;
                else { reference = New(); objects.Add(reference, annots.Data[i]); }
                annotationRefs.Add(R(reference)); annotations.Add((p, i), reference);
                var annotation = Copy((DictionaryToken)objects[reference]); annotation[N("StructParent")] = new NumericToken(annotationKey); annotation[N("F")] = new NumericToken(4); annotation[N("Contents")] = PdfNativeText.Unicode(tags.Annotations[p][i].LinkDescription ?? ""); objects[reference] = new DictionaryToken(annotation);
                parents.Add(annotationKey++, R(nodes[tags.Annotations[p][i]]));
            }
            if (annotationRefs.Count > 0) { entries[N("Annots")] = new ArrayToken(annotationRefs); objects[pages[p]] = new DictionaryToken(entries); }
        }
        foreach (var pair in nodes)
        {
            PdfTags.Node node = pair.Key; var kids = node.Children.Where(c => c.Used).Select(c => (IToken)R(nodes[c])).ToList();
            kids.AddRange(node.Marks.Select(m => (IToken)D(("Type", N("MCR")), ("Pg", R(pages[m.Page])), ("MCID", new NumericToken(m.Mcid)))));
            kids.AddRange(node.Links.Select(l => (IToken)D(("Type", N("OBJR")), ("Pg", R(pages[l.Page])), ("Obj", R(annotations[l])))));
            var entry = new Dictionary<NameToken, IToken> { [N("Type")] = N("StructElem"), [N("S")] = N(node.Role), [N("P")] = R(node.Parent == null ? structure : nodes[node.Parent]), [N("K")] = new ArrayToken(kids) };
            if (node.Alt != null) entry[N("Alt")] = PdfNativeText.Unicode(node.Alt);
            if (node.Role is "TH" or "TD")
            {
                var attributes = new Dictionary<NameToken, IToken> { [N("O")] = N("Table") }; if (node.Role == "TH") attributes[N("Scope")] = N("Column"); if (node.Span > 1) attributes[N("ColSpan")] = new NumericToken(node.Span); entry[N("A")] = new DictionaryToken(attributes);
            }
            objects.Add(pair.Value, new DictionaryToken(entry));
        }
        objects.Add(parentTree, D(("Nums", new ArrayToken(parents.SelectMany(pair => new IToken[] { new NumericToken(pair.Key), pair.Value }).ToArray()))));
        objects.Add(structure, D(("Type", N("StructTreeRoot")), ("K", R(nodes[tags.Root])), ("ParentTree", R(parentTree)), ("ParentTreeNextKey", new NumericToken(annotationKey))));
        objects.Add(metadata, new StreamToken(D(("Type", N("Metadata")), ("Subtype", N("XML")), ("Length", new NumericToken(xmp.Length))), xmp));
        var catalogEntries = Copy(catalog); catalogEntries[N("StructTreeRoot")] = R(structure); catalogEntries[N("MarkInfo")] = D(("Marked", BooleanToken.True), ("Suspects", BooleanToken.False)); catalogEntries[N("ViewerPreferences")] = D(("DisplayDocTitle", BooleanToken.True)); catalogEntries[N("Metadata")] = R(metadata);
        if (tags.Options.Language != null) catalogEntries[N("Lang")] = new StringToken(tags.Options.Language);
        objects[root] = new DictionaryToken(catalogEntries);
        var actual = new Dictionary<IndirectReference, long>();
        foreach (var pair in objects.OrderBy(p => p.Key.ObjectNumber))
        {
            context.CheckCancellation(); actual.Add(pair.Key, output.Position); base.WriteObject(new ObjectToken(XrefLocation.File(output.Position), pair.Key, pair.Value), output);
        }
        base.WriteCrossReferenceTable(actual, root, output, info, previousCrossReferenceTableOffset);
    }
}
