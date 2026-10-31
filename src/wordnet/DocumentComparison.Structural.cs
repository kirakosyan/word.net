using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Layout;
using WordNet.Model;

namespace WordNet;

public static partial class DocumentComparison
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static void CompareStructure(DocumentModel left, DocumentModel right, ComparisonOptions comparison,
        ConversionOptions options, ConversionContext context, bool ignoreRunFormatting, bool compareParagraphFormatting, int priorWork, out int usedWork, out int usedTokens)
    {
        long structuralItems = (long)left.Blocks.Count + right.Blocks.Count
            + left.Tables.Sum(t => (long)t.Rows.Count) + right.Tables.Sum(t => (long)t.Rows.Count);
        if (structuralItems > Math.Min(10000, comparison.MaximumTokens)) throw new InvalidDataException("Comparison.WorkLimit: Structural alignment exceeds 10,000 combined blocks/rows or its token allowance.");
        foreach (DocumentModel document in new[] { left, right })
        {
            if (document.Notes.Count != 0 || document.ObjectCount != 0
                || document.BodyParagraphs.Any(p => p.Control != null || p.List != null || p.Runs.Any(r => r.Image != null || r.Box != null || r.Object != null
                    || r.Note != null || r.Bookmark != null || r.FieldInstruction != null || r.Control != null || r.Link != null)))
                throw new NotSupportedException("Comparison.Structure: Structural alignment supports plain body paragraphs and simple tables.");
            foreach (TableBlock table in document.Blocks.OfType<TableBlock>())
                if (table.Control != null || table.RowControls.Count != 0 || table.RowHeights.Count != 0 || table.RowLefts.Count != 0 || table.NonSplittingRows.Count != 0
                    || table.Rows.Any(row => row.Any(c => c.ColumnSpan != 1 || c.Merge != VerticalMerge.None || c.Blocks.Any(b => b is not ParagraphBlock))))
                    throw new NotSupportedException("Comparison.Structure: Merged, nested, controlled and individually sized rows are outside structural alignment.");
        }
        int work = priorWork, items = 0; long metadataBytes = 0;
        List<PageSettings?> leftSections = Sections(left, context), rightSections = Sections(right, context);
        if (leftSections.Count > 64 || rightSections.Count > 64) throw new InvalidDataException("Comparison.WorkLimit: At most 64 sections are supported.");
        if (leftSections.Count != rightSections.Count || StoryTopology(leftSections) != StoryTopology(rightSections)
            || !Global(left).SequenceEqual(Global(right)))
            throw new NotSupportedException("Comparison.Structure: Section topology, geometry, metadata and protected stories must match.");
        TableBlock[] oldTables = left.Blocks.OfType<TableBlock>().ToArray(), newTables = right.Blocks.OfType<TableBlock>().ToArray();
        if (oldTables.Length != newTables.Length) throw new NotSupportedException("Comparison.Structure: Table insertion/deletion is outside the structural profile.");
        // Serialize each input once; signatures retain the writer's canonical XML.
        Dictionary<DocumentBlock, XElement> oldElements = Elements(left), newElements = Elements(right);
        for (int t = 0; t < oldTables.Length; t++)
        {
            TableBlock a = oldTables[t], b = newTables[t];
            if (SectionIndex(leftSections, a.Section) != SectionIndex(rightSections, b.Section))
                throw new NotSupportedException("Comparison.Structure: Tables cannot move across sections.");
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
        var moveMarks = new Dictionary<string, string>(StringComparer.Ordinal);
        var alignedBlocks = new List<(int A, int B, int Section)>();
        for (int sectionIndex = 0; sectionIndex < leftSections.Count; sectionIndex++)
        {
            context.CheckCancellation();
            long scan = (long)left.Blocks.Count + right.Blocks.Count;
            if (scan > comparison.MaximumSteps - work) throw new InvalidDataException("Comparison.WorkLimit: Section alignment exceeds its work allowance.");
            work += (int)scan;
            int[] oldIndices = Enumerable.Range(0, left.Blocks.Count).Where(i => ReferenceEquals(left.Blocks[i].Section, leftSections[sectionIndex])).ToArray();
            int[] newIndices = Enumerable.Range(0, right.Blocks.Count).Where(i => ReferenceEquals(right.Blocks[i].Section, rightSections[sectionIndex])).ToArray();
            foreach (var pair in Align(oldIndices.Select(i => aKeys[i]).ToArray(), newIndices.Select(i => bKeys[i]).ToArray(), true))
                alignedBlocks.Add((pair.A < 0 ? -1 : oldIndices[pair.A], pair.B < 0 ? -1 : newIndices[pair.B], sectionIndex));
        }
        var deletedSections = alignedBlocks.Where(p => p.B < 0).GroupBy(p => aKeys[p.A], StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => new HashSet<int>(g.Select(p => p.Section)), StringComparer.Ordinal);
        if (alignedBlocks.Any(p => p.A < 0 && deletedSections.TryGetValue(bKeys[p.B], out HashSet<int>? sections) && !sections.Contains(p.Section)))
            throw new NotSupportedException("Comparison.Move: Cross-section moves are unsupported.");
        var movedKeys = new HashSet<string>(alignedBlocks.Where(p => p.B < 0).Select(p => aKeys[p.A]), StringComparer.Ordinal);
        movedKeys.IntersectWith(alignedBlocks.Where(p => p.A < 0).Select(p => bKeys[p.B]));
        movedKeys.RemoveWhere(key => Text(key).Length == 0);
        if (movedKeys.Count != 0)
        {
            var oldCounts = aKeys.GroupBy(k => k, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            var newCounts = bKeys.GroupBy(k => k, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            if (movedKeys.Any(k => oldCounts[k] != 1 || newCounts[k] != 1))
                throw new NotSupportedException("Comparison.Move: Duplicate paragraph candidates are ambiguous across sections.");
            ReserveAdditionalWork(checked(aKeys.Length + bKeys.Length));
            var oldIndices = aKeys.Select((key, index) => (key, index)).Where(item => movedKeys.Contains(item.key)).ToDictionary(item => item.key, item => item.index, StringComparer.Ordinal);
            var newIndices = bKeys.Select((key, index) => (key, index)).Where(item => movedKeys.Contains(item.key)).ToDictionary(item => item.key, item => item.index, StringComparer.Ordinal);
            foreach (string key in movedKeys)
            {
                context.CheckCancellation();
                if (left.Blocks[oldIndices[key]] is ParagraphBlock oldParagraph && right.Blocks[newIndices[key]] is ParagraphBlock newParagraph
                    && !ParagraphFormatSnapshot.Same(oldParagraph, newParagraph))
                    throw new NotSupportedException("Comparison.Move: Paragraph formatting changes on moved paragraphs are unsupported.");
            }
        }
        foreach (var pair in alignedBlocks)
        {
            if (pair.A >= 0 && pair.B >= 0)
            {
                if (left.Blocks[pair.A] is ParagraphBlock oldParagraph && right.Blocks[pair.B] is ParagraphBlock newParagraph
                    && !ParagraphFormatSnapshot.Same(oldParagraph, newParagraph))
                {
                    ReserveAdditionalWork(1);
                    if (!ReviewStore.OrdinaryParagraphFormat(oldParagraph) || !ReviewStore.OrdinaryParagraphFormat(newParagraph))
                        throw new NotSupportedException("Comparison.ParagraphFormat: Paragraph-property changes require ordinary body paragraphs.");
                    if (compareParagraphFormatting)
                    {
                        string id = Guid.NewGuid().ToString("N"); var previous = new ParagraphFormatSnapshot(oldParagraph);
                        new ParagraphFormatSnapshot(newParagraph).Apply(oldParagraph);
                        oldParagraph.ParagraphFormatRevision = id; oldParagraph.PreviousParagraphFormat = previous;
                        left.Review.Revisions.Add(id, new ReviewRecord { Id = id, Author = comparison.Author, Date = comparison.Timestamp, Structure = "paragraph-format" });
                    }
                    else oldParagraph.ExplicitParagraphFormat = true;
                }
                blocks.Add(left.Blocks[pair.A]); continue;
            }
            DocumentBlock changed = pair.A >= 0 ? left.Blocks[pair.A] : right.Blocks[pair.B];
            if (changed is not ParagraphBlock p) throw new NotSupportedException("Comparison.Move: Table moves are unsupported.");
            bool deletion = pair.A >= 0; string key = deletion ? aKeys[pair.A] : bKeys[pair.B];
            p.StructuralRevision = Record(deletion, "paragraph"); p.Section = leftSections[pair.Section];
            if (movedKeys.Contains(key))
            {
                ReviewRecord mark = left.Review.Revisions[p.StructuralRevision]; mark.MoveSide = deletion ? "from" : "to";
                if (moveMarks.TryGetValue(key, out string? partner))
                { mark.MovePartner = partner; left.Review.Revisions[partner].MovePartner = mark.Id; }
                else moveMarks.Add(key, mark.Id);
            }
            if (p.Runs.Any(r => r.Text.Length != 0))
            {
                string revision = Record(deletion, ""); foreach (TextRun run in p.Runs) run.Revision = revision;
                if (movedKeys.Contains(key)) left.Review.Revisions[revision].MoveSide = deletion ? "from" : "to";
            }
            blocks.Add(p);
        }
        left.Blocks.Clear(); left.Blocks.AddRange(blocks);
        usedWork = work; usedTokens = items;

        string Record(bool deletion, string structure)
        {
            string id = "comparison-" + left.Review.Revisions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            left.Review.Revisions.Add(id, new ReviewRecord { Id = id, Author = comparison.Author, Date = comparison.Timestamp, Deletion = deletion, Structure = structure }); return id;
        }
        string[] Keys(DocumentModel document, TableBlock[] tables, Dictionary<DocumentBlock, XElement> elements) => document.Blocks.Select(b => b is TableBlock table
            ? "table:" + Array.IndexOf(tables, table).ToString(System.Globalization.CultureInfo.InvariantCulture) : Key(elements[b])).ToArray();
        List<(int A, int B)> Align(string[] a, string[] b, bool allowMoves = false)
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
            var output = new List<(int A, int B)>(); var ties = new List<(string Old, string New)>(); int x = 0, y = 0;
            while (x < a.Length || y < b.Length)
            {
                context.CheckCancellation();
                if (x < a.Length && y < b.Length && aSymbols[x] == bSymbols[y]) output.Add((x++, y++));
                else if (x < a.Length && (y == b.Length || lengths[(x + 1) * width + y] >= lengths[x * width + y + 1]))
                { if (y < b.Length && lengths[(x + 1) * width + y] == lengths[x * width + y + 1]) ties.Add((a[x], b[y])); output.Add((x++, -1)); }
                else output.Add((-1, y++));
            }
            // Matching content on both unmatched sides is an ambiguous move, not an insertion.
            var removed = new HashSet<string>(output.Where(p => p.B < 0).Select(p => a[p.A]), StringComparer.Ordinal);
            var moved = new HashSet<string>(output.Where(p => p.A < 0 && removed.Contains(b[p.B])).Select(p => b[p.B]), StringComparer.Ordinal);
            var empty = new HashSet<string>(moved.Where(key => Text(key).Length == 0), StringComparer.Ordinal);
            moved.ExceptWith(empty);
            var oldCounts = a.GroupBy(value => value, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            var newCounts = b.GroupBy(value => value, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            if (moved.Count != 0 && (!allowMoves || ties.Any(tie => moved.Contains(tie.Old) || moved.Contains(tie.New)) || moved.Any(key => key.StartsWith("table:", StringComparison.Ordinal)
                || oldCounts[key] != 1 || newCounts[key] != 1)))
                throw new NotSupportedException("Comparison.Move: Reordered content has ambiguous or unsupported ownership.");
            // Same text with different properties is a formatting revision, not a structural replacement.
            var oldText = new HashSet<string>(output.Where(p => p.B < 0 && !moved.Contains(a[p.A]) && !empty.Contains(a[p.A])).Select(p => Text(a[p.A])), StringComparer.Ordinal);
            if (output.Where(p => p.A < 0 && !moved.Contains(b[p.B]) && !empty.Contains(b[p.B])).Any(p => oldText.Contains(Text(b[p.B]))))
                throw new NotSupportedException("Comparison.Formatting: Formatting revisions are unsupported.");
            return output;
        }
        int SectionIndex(List<PageSettings?> sections, PageSettings? section)
        {
            for (int i = 0; i < sections.Count; i++) if (ReferenceEquals(sections[i], section)) return i;
            throw new InvalidDataException("Comparison.Structure: A section has no owner.");
        }
        string StoryTopology(List<PageSettings?> sections)
        {
            var identities = new Dictionary<List<DocumentBlock>, int>(); var signature = new System.Text.StringBuilder();
            foreach (PageSettings? section in sections)
            {
                context.CheckCancellation(); signature.Append('|');
                if (section == null) continue;
                foreach (var story in section.Stories.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    if (!identities.TryGetValue(story.Value, out int id)) identities.Add(story.Value, id = identities.Count);
                    signature.Append(story.Key).Append(':').Append(id).Append(';');
                }
            }
            return signature.ToString();
        }
        IEnumerable<string> Global(DocumentModel source)
        {
            DocumentModel copy = new DocumentCloner(context).Copy(source); List<PageSettings?> sections = Sections(copy, context); copy.Blocks.Clear();
            foreach (PageSettings? section in sections)
            {
                context.CheckCancellation(); copy.FinalSection = section;
                DocumentModel normalized = new DocumentCloner(context).Copy(copy); NormalizePlainStories(normalized, context, ignoreRunFormatting, ReserveAdditionalWork);
                using var stream = new BoundedMemoryStream(options.MaximumOutputBytes); DocxWriter.Write(normalized, stream, context);
                metadataBytes += stream.Length;
                if (metadataBytes > options.MaximumExpandedBytes) throw new InvalidDataException("Comparison.ResourceLimit: Section metadata comparison exceeds its byte allowance.");
                yield return Convert.ToBase64String(stream.ToArray());
            }
        }
        Dictionary<DocumentBlock, XElement> Elements(DocumentModel document)
        {
            DocumentModel comparisonDocument = new DocumentCloner(context).Copy(document);
            foreach (ParagraphBlock paragraph in comparisonDocument.Blocks.OfType<ParagraphBlock>())
            {
                if (!ReviewStore.OrdinaryParagraphFormat(paragraph)) continue;
                ReserveAdditionalWork(1); NeutralParagraphFormat(paragraph);
            }
            if (ignoreRunFormatting)
            {
                foreach (ParagraphBlock paragraph in comparisonDocument.BodyParagraphs)
                {
                    ReserveAdditionalWork(checked(paragraph.Runs.Count + 1));
                    List<TextRun> normalized = RunCoalescer.Combine(paragraph.Runs.Select(Neutral).ToList());
                    paragraph.Runs.Clear(); paragraph.Runs.AddRange(normalized);
                }
            }
            XElement[] elements = Body(comparisonDocument).Element(W + "body")!.Elements().Where(e => e.Name != W + "sectPr").ToArray();
            var result = new Dictionary<DocumentBlock, XElement>(); int index = 0;
            if (document.Blocks.Count == 0 && elements.Length == 1 && elements[0].Name == W + "p" && !elements[0].HasElements) return result;
            for (int i = 0; i < document.Blocks.Count; i++)
            {
                context.CheckCancellation();
                if (i > 0 && document.Blocks[i - 1] is TableBlock && !ReferenceEquals(document.Blocks[i - 1].Section, document.Blocks[i].Section))
                {
                    if (index >= elements.Length || !SyntheticBreak(elements[index])) throw new NotSupportedException("Comparison.Structure: Invalid table section boundary.");
                    index++;
                }
                if (index >= elements.Length || elements[index].Name != W + (document.Blocks[i] is TableBlock ? "tbl" : "p"))
                    throw new NotSupportedException("Comparison.Structure: The body requires one canonical element per block.");
                XElement element = elements[index++]; XElement? properties = element.Element(W + "pPr");
                properties?.Element(W + "sectPr")?.Remove(); if (properties != null && !properties.HasElements && !properties.HasAttributes) properties.Remove();
                result.Add(document.Blocks[i], element);
            }
            if (index < elements.Length && (index + 1 != elements.Length || document.Blocks.Count == 0 || document.Blocks[document.Blocks.Count - 1] is not TableBlock || !SyntheticBreak(elements[index])))
                throw new NotSupportedException("Comparison.Structure: Unexpected synthetic body content.");
            return result;
        }
        void ReserveAdditionalWork(int count)
        {
            context.CheckCancellation();
            if (count > comparison.MaximumSteps - work) throw new InvalidDataException("Comparison.WorkLimit: Formatting normalization exceeds its work allowance.");
            work += count;
        }
        static bool SyntheticBreak(XElement element) => element.Name == W + "p" && element.Elements().Count() == 1
            && element.Element(W + "pPr")?.Elements().Count() == 1 && element.Element(W + "pPr")?.Element(W + "sectPr") != null;
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
