using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet;

/// <summary>Explicit treatment of supported fields in an inserted fragment.</summary>
public enum FragmentFieldHandling
{
    /// <summary>Reject fragments containing fields, matching the original insertion API.</summary>
    Reject,
    /// <summary>Retain supported field instructions while explicitly accepting that some cached results can be stale.</summary>
    AcceptCachedResults,
}

/// <summary>Explicit field and appearance policies for one fragment insertion.</summary>
public sealed class FragmentInsertionOptions
{
    /// <summary>Gets or sets how fragment fields are treated. Defaults to rejection.</summary>
    public FragmentFieldHandling Fields { get; set; } = FragmentFieldHandling.Reject;
    /// <summary>Gets or sets whether source or placeholder appearance is used. Defaults to source appearance.</summary>
    public CompositionStyle Style { get; set; } = CompositionStyle.KeepSource;
}

public sealed partial class WordDocument
{
    /// <summary>Replaces a dedicated body bookmark paragraph with a private copy of an admitted document fragment.</summary>
    /// <remarks>Load caller-supplied DOCX or safe HTML into a separate session first. The placeholder must
    /// occupy one whole body paragraph. Source appearance is retained; section geometry and stories are
    /// not imported. Admitted chart and equation placements receive independent identities. Both sessions
    /// remain unchanged if validation, cancellation or admission fails.</remarks>
    public DocumentEditResult InsertFragmentAtBookmark(string name, WordDocument fragment, CancellationToken cancellationToken = default)
        => InsertFragment(name, fragment, cellDestination: false, FragmentFieldHandling.Reject, CompositionStyle.KeepSource, cancellationToken);

    /// <summary>Replaces a body bookmark paragraph under explicit field and appearance policies.</summary>
    /// <remarks>Retained field results may be stale; supported references can be recalculated in the destination.</remarks>
    public DocumentEditResult InsertFragmentAtBookmarkWithOptions(string name, WordDocument fragment, FragmentInsertionOptions fragmentOptions, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(fragmentOptions, nameof(fragmentOptions));
        return InsertFragment(name, fragment, cellDestination: false, fragmentOptions.Fields, fragmentOptions.Style, cancellationToken);
    }

    /// <summary>Replaces a dedicated bookmark paragraph in a simple body-table cell with a private fragment copy.</summary>
    /// <remarks>The host must be a single unmerged cell in a top-level body table. The bookmark must own
    /// the whole paragraph. Source sections/stories, nested tables, fields, typed controls, review history
    /// and positioned text boxes remain unsupported. Admitted charts and equations retain their source
    /// appearance and receive independent placement identities. Both sessions remain unchanged on failure.</remarks>
    public DocumentEditResult InsertFragmentAtTableCellBookmark(string name, WordDocument fragment, CancellationToken cancellationToken = default)
        => InsertFragment(name, fragment, cellDestination: true, FragmentFieldHandling.Reject, CompositionStyle.KeepSource, cancellationToken);

    /// <summary>Replaces a simple body-table-cell bookmark under explicit field and appearance policies.</summary>
    /// <remarks>Retained field results may be stale; supported references can be recalculated in the destination.</remarks>
    public DocumentEditResult InsertFragmentAtTableCellBookmarkWithOptions(string name, WordDocument fragment, FragmentInsertionOptions fragmentOptions, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(fragmentOptions, nameof(fragmentOptions));
        return InsertFragment(name, fragment, cellDestination: true, fragmentOptions.Fields, fragmentOptions.Style, cancellationToken);
    }

    private DocumentEditResult InsertFragment(string name, WordDocument fragment, bool cellDestination, FragmentFieldHandling fields, CompositionStyle style, CancellationToken cancellationToken)
    {
        ValidateBookmarkName(name); Guard.NotNull(fragment, nameof(fragment));
        if (fields is not FragmentFieldHandling.Reject and not FragmentFieldHandling.AcceptCachedResults) throw new ArgumentOutOfRangeException(nameof(fields));
        if (style is not CompositionStyle.KeepSource and not CompositionStyle.UseDestination) throw new ArgumentOutOfRangeException(nameof(style));
        if (ReferenceEquals(fragment, this)) throw new ArgumentException("Fragment.Source: A fragment must be a separate document session.", nameof(fragment));
        DocumentModel incoming;
        lock (fragment.gate)
        {
            if (fragment.diagnostics.Any(d => d.Severity == DiagnosticSeverity.Warning
                || d.Code is "Field.UnsupportedUsedCachedResult" or "Field.OrphanInstruction" or "Field.DuplicateSeparator"))
                throw new NotSupportedException("Fragment.SourceLoss: Resolve source load losses before inserting a fragment.");
            if (fragment.model.Controls.HasControls || fragment.model.Review.HasHistory)
                throw new NotSupportedException("Fragment.SourceStructure: Typed controls and retained history are unsupported in fragments.");
            var context = new ConversionContext(options, cancellationToken);
            Admit(fragment.model, context); incoming = new DocumentCloner(context).Copy(fragment.model);
        }
        return Edit((m, c) =>
        {
            if (incoming.Blocks.Count == 0) throw new InvalidDataException("Fragment.Empty: A fragment must contain at least one body block.");
            PageSettings[] sections = incoming.Blocks.Select(b => b.Section).Concat(new[] { incoming.FinalSection }).Where(s => s != null).Select(s => s!).Distinct().ToArray();
            if (sections.Length > 1 || sections.Any(s => s.Stories.Values.Any(story => story.Count != 0)))
                throw new NotSupportedException("Fragment.Section: Fragment section breaks and stories are unsupported.");
            if (incoming.Tables.Any(table => table.Rows.SelectMany(row => row).SelectMany(cell => cell.Blocks).Any(block => block is TableBlock)))
                throw new NotSupportedException("Fragment.Table: Nested fragment tables are unsupported.");
            if (incoming.ObjectCount != 0)
                foreach (DocumentBlock block in DocumentModel.Expand(incoming.AllBlocks).Distinct())
                {
                    c.ReserveControlWork(1);
                    if (block.Control != null || block.NestedControl != null || block is TableBlock table && table.RowControls.Count != 0)
                        throw new NotSupportedException("Fragment.SourceStructure: Object-bearing fragments cannot contain report controls.");
                    if (block is ParagraphBlock paragraph)
                        foreach (TextRun run in paragraph.Runs)
                        {
                            c.ReserveControlWork(1);
                            if (run.Control != null) throw new NotSupportedException("Fragment.SourceStructure: Object-bearing fragments cannot contain report controls.");
                        }
                }
            TextRun[] sourceRuns = incoming.Paragraphs.SelectMany(p => p.Runs).ToArray();
            if (sourceRuns.Any(r => r.Box != null))
                throw new NotSupportedException("Fragment.Structure: Positioned text boxes are unsupported in fragments.");
            ValidateRanges(incoming);
            TextRun[] fieldRuns = sourceRuns.Where(r => r.FieldInstruction != null).ToArray();
            var openBookmarks = new Stack<string>();
            foreach (ParagraphBlock paragraph in incoming.Paragraphs)
            {
                foreach (TextRun run in paragraph.Runs)
                {
                    c.CheckCancellation();
                    if (run.Bookmark is not string marker) continue;
                    if (!Starts(marker, '/'))
                    {
                        if (openBookmarks.Count >= c.Options.MaximumNestingDepth) throw new InvalidDataException("Limit.NestingDepth: Fragment bookmark nesting exceeds the destination bound.");
                        openBookmarks.Push(marker);
                    }
                    else if (openBookmarks.Count == 0 || openBookmarks.Pop() != marker.Substring(1))
                        throw new NotSupportedException("Fragment.Reference: Crossing fragment bookmark ranges are unsupported.");
                }
                if (fieldRuns.Length != 0 && openBookmarks.Count != 0)
                    throw new NotSupportedException("Fragment.Reference: Field-bearing fragments require paragraph-contained bookmark ranges.");
            }
            if (openBookmarks.Count != 0) throw new InvalidDataException("Fragment.Reference: Incomplete fragment bookmark range.");
            var names = new HashSet<string>(sourceRuns.Where(r => r.Bookmark != null && !Starts(r.Bookmark, '/')).Select(r => r.Bookmark!), StringComparer.Ordinal);
            if (sourceRuns.Any(r => r.Link != null && Starts(r.Link, '#') && !names.Contains(r.Link.Substring(1))))
                throw new NotSupportedException("Fragment.Reference: Fragment links must resolve within the fragment.");
            if (fieldRuns.Length != 0 && fields == FragmentFieldHandling.Reject)
                throw new NotSupportedException("Fragment.Structure: Field-bearing fragments require an explicit cached-result policy.");
            foreach (TextRun field in fieldRuns)
            {
                c.CheckCancellation(); string instruction = field.FieldInstruction!;
                string kind = DocumentFields.Kind(instruction);
                if (kind is not "MERGEFIELD" and not "REF" and not "PAGEREF" and not "SEQ" and not "PAGE" and not "NUMPAGES" and not "SECTIONPAGES" and not "DATE" and not "TIME"
                    || field.FieldChildren != null || field.FieldCache?.Any(r => r.Link != null || r.FieldInstruction != null || r.Bookmark != null || r.Image != null || r.Note != null || r.Box != null || r.Object != null || r.Control != null) == true)
                    throw new NotSupportedException("Fragment.Structure: The fragment contains an unsupported field or structured field cache.");
                if (kind == "MERGEFIELD")
                {
                    try { _ = MergeField(instruction); }
                    catch (InvalidDataException error) { throw new NotSupportedException("Fragment.Structure: Unsupported merge-field syntax or switches.", error); }
                }
                else if (kind is "REF" or "PAGEREF" or "SEQ")
                { if (!DocumentReferences.SupportedFragmentReference(instruction)) throw new NotSupportedException("Fragment.Structure: Unsupported fragment field syntax or switches."); }
                else if (!instruction.Trim().Equals(kind, StringComparison.OrdinalIgnoreCase))
                    throw new NotSupportedException("Fragment.Structure: Fragment page/date/time fields admit no switches.");
                if (kind is "REF" or "PAGEREF")
                {
                    Match reference = Regex.Match(instruction, "^\\s*(?:REF|PAGEREF)\\s+(?:\"([^\"]+)\"|(\\S+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                    if (!reference.Success || !names.Contains(reference.Groups[1].Success ? reference.Groups[1].Value : reference.Groups[2].Value))
                        throw new NotSupportedException("Fragment.Reference: Fragment field targets must resolve within the fragment.");
                }
            }
            if (fieldRuns.Length != 0)
                c.Report("Fragment.CachedFields", "The caller explicitly accepted retained field caches; supported references can refresh in the destination and other cached results can be stale.");
            // Reject ambiguous names anywhere in the model before the selected bookmark is removed.
            ValidateRanges(m);
            var matches = new List<(List<DocumentBlock> Blocks, int Index, ParagraphBlock Paragraph, bool Protected, TableCell? Cell)>();
            if (cellDestination)
            {
                foreach (TableBlock table in m.Blocks.OfType<TableBlock>())
                    for (int row = 0; row < table.Rows.Count; row++)
                        foreach (TableCell cell in table.Rows[row])
                            for (int index = 0; index < cell.Content.Count; index++)
                            {
                                c.CheckCancellation();
                                if (cell.Content[index] is ParagraphBlock p && p.Runs.Any(r => r.Bookmark == name))
                                    matches.Add((cell.Content, index, p, table.Control != null || table.NestedControl != null
                                        || table.RowControls.ContainsKey(row) || cell.ColumnSpan != 1 || cell.Merge != VerticalMerge.None, cell));
                            }
            }
            else
                for (int index = 0; index < m.Blocks.Count; index++)
                    if (m.Blocks[index] is ParagraphBlock p && p.Runs.Any(r => r.Bookmark == name)) matches.Add((m.Blocks, index, p, false, null));
            if (matches.Count != 1 || matches[0].Protected || matches[0].Paragraph.Control != null || matches[0].Paragraph.NestedControl != null)
                throw new InvalidDataException(cellDestination
                    ? "Fragment.Placeholder: A unique unwrapped bookmark paragraph in one simple top-level body-table cell is required."
                    : "Fragment.Placeholder: A unique unwrapped body bookmark paragraph is required.");
            ParagraphBlock placeholder = matches[0].Paragraph;
            int start = placeholder.Runs.FindIndex(r => r.Bookmark == name), end = placeholder.Runs.FindIndex(r => r.Bookmark == "/" + name);
            if (start != 0 || end != placeholder.Runs.Count - 1 || end <= start
                || placeholder.Runs.Skip(1).Take(end - 1).Any(r => r.Bookmark != null || r.Control != null || r.Note != null || r.Image != null || r.FieldInstruction != null || r.Link != null || r.Box != null || r.Object != null))
                throw new NotSupportedException("Fragment.Placeholder: The bookmark must own the complete plain-text placeholder paragraph.");
            var pending = new Stack<TextRun>(m.Paragraphs.SelectMany(p => p.Runs)); long work = 0;
            var reservedReferences = new HashSet<string>(StringComparer.Ordinal);
            while (pending.Count != 0)
            {
                c.CheckCancellation(); if (++work > Math.Max(4096L, c.Options.MaximumCharacters * 64L)) throw new InvalidDataException("Limit.FragmentWork: Reference traversal exceeds the work bound.");
                TextRun run = pending.Pop();
                if (run.Link != null && Starts(run.Link, '#'))
                {
                    string target = run.Link.Substring(1);
                    if (string.Equals(target, name, StringComparison.OrdinalIgnoreCase))
                        throw new NotSupportedException("Fragment.Reference: Resolve links to the placeholder before insertion.");
                    reservedReferences.Add(target);
                }
                if (run.FieldInstruction != null)
                {
                    Match reference = Regex.Match(run.FieldInstruction, "^\\s*(REF|PAGEREF|SEQ)\\s+(?:\"([^\"]+)\"|(\\S+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                    if (reference.Success)
                    {
                        string target = reference.Groups[2].Success ? reference.Groups[2].Value : reference.Groups[3].Value;
                        if (!reference.Groups[1].Value.Equals("SEQ", StringComparison.OrdinalIgnoreCase) && string.Equals(target, name, StringComparison.OrdinalIgnoreCase))
                            throw new NotSupportedException("Fragment.Reference: Resolve fields referring to the placeholder before insertion.");
                        reservedReferences.Add(target);
                    }
                }
                if (run.FieldChildren != null) foreach (TextRun child in run.FieldChildren.Values) pending.Push(child);
                if (run.FieldCache != null) foreach (TextRun child in run.FieldCache) pending.Push(child);
            }
            string prefix = UniquePrefix(m, c, reservedReferences); int nextId = 1; RemapTemplateControlIds(incoming, TemplateControlIds(m), ref nextId, c);
            incoming.CustomProperties.Clear(); MergeMetadata(m, incoming, prefix, c);
            Remap(incoming, prefix, m.Notes.Select(n => n.PartId).DefaultIfEmpty().Max());
            if (style == CompositionStyle.UseDestination)
            {
                if (incoming.ObjectCount != 0)
                    throw new NotSupportedException("Object.Restyle: Fragment objects retain source appearance only.");
                if (incoming.Tables.Any(table => table.Style != null))
                    throw new NotSupportedException("Table.StyleRestyle: Resolve table styles before applying destination appearance.");
                if (incoming.HasControlDisplay) throw new NotSupportedException("Fragment.Structure: Detached typed-control display cannot be restyled during fragment insertion.");
                if (placeholder.IsCodeBlock || incoming.BodyParagraphs.Any(p => p.IsCodeBlock
                    || p.Runs.Any(r => r.Code || r.FieldCache?.Any(cached => cached.Code) == true)))
                    throw new NotSupportedException("Fragment.Structure: Code-styled fragment content or placeholders cannot use destination restyling.");
                TextRun? appearance = placeholder.Runs.FirstOrDefault(r => r.Bookmark == null);
                foreach (ParagraphBlock paragraph in incoming.BodyParagraphs)
                {
                    c.CheckCancellation();
                    paragraph.StyleId = placeholder.StyleId; paragraph.Mark = placeholder.Mark;
                    paragraph.FallbackFontSize = placeholder.FallbackFontSize; paragraph.FallbackAfter = placeholder.FallbackAfter;
                    paragraph.Layout = placeholder.Layout.Copy(); paragraph.Alignment = placeholder.Alignment;
                    paragraph.RightToLeft = placeholder.RightToLeft;
                    if (appearance != null)
                        for (int index = 0; index < paragraph.Runs.Count; index++) paragraph.Runs[index] = Restyle(paragraph.Runs[index], appearance);
                }
            }
            foreach (DocumentBlock block in incoming.Blocks) block.Section = placeholder.Section;
            m.HasControlDisplay |= incoming.HasControlDisplay;
            List<DocumentBlock> destination = matches[0].Blocks;
            destination.RemoveAt(matches[0].Index); destination.InsertRange(matches[0].Index, incoming.Blocks);
            // WordprocessingML cells must end in a paragraph, even when the fragment ends in a table.
            if (cellDestination && destination[destination.Count - 1] is not ParagraphBlock)
                destination.Add(new ParagraphBlock { Section = placeholder.Section });
            if (matches[0].Cell is TableCell destinationCell)
            {
                destinationCell.Paragraphs.Clear(); destinationCell.Paragraphs.AddRange(DocumentModel.CellParagraphs(destination));
            }
            m.Notes.AddRange(incoming.Notes);
            ValidateRanges(m); return incoming.Blocks.Count;
        }, cancellationToken, objectAware: true);
    }
}
