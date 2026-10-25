using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet;

public static partial class DocumentComparison
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static WordDocument CompareStructure(DocumentModel left, DocumentModel right, ComparisonOptions comparison,
        ConversionOptions options, ConversionContext context)
    {
        long structuralItems = (long)left.Blocks.Count + right.Blocks.Count
            + left.Tables.Sum(t => (long)t.Rows.Count) + right.Tables.Sum(t => (long)t.Rows.Count);
        if (structuralItems > Math.Min(10000, comparison.MaximumTokens)) throw new InvalidDataException("Comparison.WorkLimit: Structural alignment exceeds 10,000 combined blocks/rows or its token allowance.");
        foreach (DocumentModel document in new[] { left, right })
        {
            if (document.Blocks.Select(b => b.Section).Concat(new[] { document.FinalSection }).Where(s => s != null).Distinct().Count() > 1 || document.Notes.Count != 0 || document.ObjectCount != 0
                || document.BodyParagraphs.Any(p => p.Control != null || p.List != null || p.Runs.Any(r => r.Image != null || r.Box != null || r.Object != null
                    || r.Note != null || r.Bookmark != null || r.FieldInstruction != null || r.Control != null || r.Link != null)))
                throw new NotSupportedException("Comparison.Structure: Structural alignment supports plain single-section body paragraphs and simple tables.");
            foreach (TableBlock table in document.Blocks.OfType<TableBlock>())
                if (table.Control != null || table.RowControls.Count != 0 || table.RowHeights.Count != 0 || table.RowLefts.Count != 0 || table.NonSplittingRows.Count != 0
                    || table.Rows.Any(row => row.Any(c => c.ColumnSpan != 1 || c.Merge != VerticalMerge.None || c.Blocks.Any(b => b is not ParagraphBlock))))
                    throw new NotSupportedException("Comparison.Structure: Merged, nested, controlled and individually sized rows are outside structural alignment.");
        }
        if (Global(left) != Global(right)) throw new NotSupportedException("Comparison.Structure: Metadata, section geometry and protected stories must match.");
        TableBlock[] oldTables = left.Blocks.OfType<TableBlock>().ToArray(), newTables = right.Blocks.OfType<TableBlock>().ToArray();
        if (oldTables.Length != newTables.Length) throw new NotSupportedException("Comparison.Structure: Table insertion/deletion is outside the structural profile.");
        // Serialize each input once; signatures retain the writer's canonical XML.
        Dictionary<DocumentBlock, XElement> oldElements = Elements(left), newElements = Elements(right);
        int work = 0, items = 0;
        for (int t = 0; t < oldTables.Length; t++)
        {
            TableBlock a = oldTables[t], b = newTables[t];
            XElement ax = oldElements[a], bx = newElements[b];
            XElement[] ar = ax.Elements(W + "tr").ToArray(), br = bx.Elements(W + "tr").ToArray();
            ax.Elements(W + "tr").Remove(); bx.Elements(W + "tr").Remove();
            if (!XNode.DeepEquals(ax, bx) || a.HeaderRowCount != b.HeaderRowCount || !ar.Take(a.HeaderRowCount).Select(Key).SequenceEqual(br.Take(b.HeaderRowCount).Select(Key)))
                throw new NotSupportedException("Comparison.Formatting: Table properties and repeated headers must match.");
            var rows = new List<List<TableCell>>(); var changes = new Dictionary<int, string>();
            foreach (var pair in Align(ar.Select(Key).ToArray(), br.Select(Key).ToArray()))
            {
                if (pair.A >= 0 && pair.B >= 0) rows.Add(a.Rows[pair.A]);
                else
                {
                    if (pair.A >= 0 && pair.A < a.HeaderRowCount || pair.B >= 0 && pair.B < b.HeaderRowCount)
                        throw new NotSupportedException("Comparison.Structure: Repeated header revisions are unsupported.");
                    changes.Add(rows.Count, Record(pair.A >= 0, "row")); rows.Add(pair.A >= 0 ? a.Rows[pair.A] : b.Rows[pair.B]);
                }
            }
            a.Rows.Clear(); a.Rows.AddRange(rows); foreach (var pair in changes) a.RowRevisions.Add(pair.Key, pair.Value);
        }
        string[] aKeys = Keys(left, oldTables, oldElements), bKeys = Keys(right, newTables, newElements);
        var blocks = new List<DocumentBlock>();
        PageSettings? section = left.Blocks.FirstOrDefault()?.Section ?? left.FinalSection;
        foreach (var pair in Align(aKeys, bKeys))
        {
            if (pair.A >= 0 && pair.B >= 0) { blocks.Add(left.Blocks[pair.A]); continue; }
            DocumentBlock changed = pair.A >= 0 ? left.Blocks[pair.A] : right.Blocks[pair.B];
            if (changed is not ParagraphBlock p) throw new NotSupportedException("Comparison.Move: Table moves are unsupported.");
            bool deletion = pair.A >= 0; p.StructuralRevision = Record(deletion, "paragraph"); p.Section = section;
            if (p.Runs.Any(r => r.Text.Length != 0))
            {
                string revision = Record(deletion, ""); foreach (TextRun run in p.Runs) run.Revision = revision;
            }
            blocks.Add(p);
        }
        left.Blocks.Clear(); left.Blocks.AddRange(blocks);
        return WordDocument.ComparisonResult(left, options, context);

        string Record(bool deletion, string structure)
        {
            string id = "comparison-" + left.Review.Revisions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            left.Review.Revisions.Add(id, new ReviewRecord { Id = id, Author = comparison.Author, Date = comparison.Timestamp, Deletion = deletion, Structure = structure }); return id;
        }
        string[] Keys(DocumentModel document, TableBlock[] tables, Dictionary<DocumentBlock, XElement> elements) => document.Blocks.Select(b => b is TableBlock table
            ? "table:" + Array.IndexOf(tables, table).ToString(System.Globalization.CultureInfo.InvariantCulture) : Key(elements[b])).ToArray();
        List<(int A, int B)> Align(string[] a, string[] b)
        {
            items = checked(items + a.Length + b.Length);
            long cells = (a.Length + 1L) * (b.Length + 1L);
            if (items > comparison.MaximumTokens || cells > comparison.MaximumSteps - work || cells * 4 > options.MaximumExpandedBytes / 2)
                throw new InvalidDataException("Comparison.WorkLimit: Structural alignment exceeds its token/work/storage allowance.");
            var symbols = new Dictionary<string, int>(StringComparer.Ordinal);
            int Symbol(string value) { if (!symbols.TryGetValue(value, out int id)) symbols.Add(value, id = symbols.Count); return id; }
            int[] aSymbols = a.Select(Symbol).ToArray(), bSymbols = b.Select(Symbol).ToArray();
            int width = b.Length + 1; var lengths = new int[(int)cells];
            for (int i = a.Length - 1; i >= 0; i--) for (int j = b.Length - 1; j >= 0; j--)
            {
                context.CheckCancellation(); work++;
                lengths[i * width + j] = aSymbols[i] == bSymbols[j] ? 1 + lengths[(i + 1) * width + j + 1]
                    : Math.Max(lengths[(i + 1) * width + j], lengths[i * width + j + 1]);
            }
            var output = new List<(int A, int B)>(); int x = 0, y = 0;
            while (x < a.Length || y < b.Length)
            {
                context.CheckCancellation();
                if (x < a.Length && y < b.Length && aSymbols[x] == bSymbols[y]) output.Add((x++, y++));
                else if (x < a.Length && (y == b.Length || lengths[(x + 1) * width + y] >= lengths[x * width + y + 1])) output.Add((x++, -1));
                else output.Add((-1, y++));
            }
            // Matching content on both unmatched sides is an ambiguous move, not an insertion.
            var removed = new HashSet<string>(output.Where(p => p.B < 0).Select(p => a[p.A]), StringComparer.Ordinal);
            if (output.Any(p => p.A < 0 && removed.Contains(b[p.B]))) throw new NotSupportedException("Comparison.Move: Reordered content is outside structural comparison.");
            // Same text with different properties is a formatting revision, not a structural replacement.
            var oldText = output.Where(p => p.B < 0).Select(p => Text(a[p.A])).ToArray();
            if (output.Where(p => p.A < 0).Any(p => oldText.Contains(Text(b[p.B]), StringComparer.Ordinal)))
                throw new NotSupportedException("Comparison.Formatting: Formatting revisions are unsupported.");
            return output;
        }
        string Global(DocumentModel source)
        {
            DocumentModel copy = new DocumentCloner(context).Copy(source); copy.FinalSection ??= copy.Blocks.FirstOrDefault()?.Section; copy.Blocks.Clear();
            using var stream = new BoundedMemoryStream(options.MaximumOutputBytes); DocxWriter.Write(copy, stream, context); return Convert.ToBase64String(stream.ToArray());
        }
        Dictionary<DocumentBlock, XElement> Elements(DocumentModel document)
        {
            XElement[] elements = Body(document).Element(W + "body")!.Elements().Where(e => e.Name != W + "sectPr").ToArray();
            if (elements.Length != document.Blocks.Count) throw new NotSupportedException("Comparison.Structure: The body requires one canonical element per block.");
            return document.Blocks.Select((block, i) => new { block, element = elements[i] }).ToDictionary(p => p.block, p => p.element);
        }
        XElement Body(DocumentModel document)
        {
            using var stream = new BoundedMemoryStream(options.MaximumOutputBytes); DocxWriter.Write(document, stream, context); stream.Position = 0;
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, true); using Stream xml = zip.GetEntry("word/document.xml")!.Open();
            return XDocument.Load(xml).Root!;
        }
        static string Key(XElement element) => element.ToString(SaveOptions.DisableFormatting);
        static string Text(string key) => key.Length > 0 && key[0] == '<' ? string.Concat(XElement.Parse(key).Descendants(W + "t").Select(t => t.Value)) : key;
    }
}
