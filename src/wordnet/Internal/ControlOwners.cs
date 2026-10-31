using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WordNet.Model;

namespace WordNet.Internal;

// One traversal per transaction. Owner locations never use public paragraph indexes as handles.
internal sealed class ControlOwner
{
    internal List<DocumentBlock> Blocks { get; set; } = null!;
    internal ParagraphBlock? Paragraph { get; set; }
    internal int Start { get; set; }
    internal int Count { get; set; }
    internal int? ParentId { get; set; }
    internal bool HasChildren => Paragraph == null && Content.Any(p => p.Runs.Any(r => r.TypedControlId.HasValue));
    internal void Refresh(int id, ConversionContext c)
    {
        if (Paragraph == null) return;
        int start = -1, end = -1;
        for (int i = 0; i < Paragraph.Runs.Count; i++)
        {
            c.ReserveControlWork(1);
            if (Paragraph.Runs[i].TypedControlId == id) { if (start < 0) start = i; end = i + 1; }
            else if (start >= 0) break;
        }
        if (start < 0) throw new InvalidDataException("Control.ProtectedOwner: Inline owner no longer exists.");
        Start = start; Count = end - start;
    }
    internal IEnumerable<ParagraphBlock> Content => Paragraph == null ? Blocks.Skip(Start).Take(Count).Cast<ParagraphBlock>() : new[] { Paragraph };
    internal IEnumerable<TextRun> Runs => Paragraph == null ? Content.SelectMany(p => p.Runs) : Paragraph.Runs.Skip(Start).Take(Count);
    internal string Text => Paragraph == null ? string.Join("\n", Content.Select(p => p.Text)) : string.Concat(Runs.Select(r => r.Text));
    internal void SetRuns(int id, IEnumerable<TextRun> runs)
    {
        var replacement = runs.ToList();
        if (replacement.Count == 0) replacement.Add(new TextRun(""));
        if (Paragraph != null)
        {
            foreach (TextRun r in replacement) r.TypedControlId = id;
            Paragraph.Runs.RemoveRange(Start, Count); Paragraph.Runs.InsertRange(Start, replacement); Count = replacement.Count;
        }
        else
        {
            ParagraphBlock paragraph = Content.First();
            paragraph.Runs.Clear(); paragraph.Runs.AddRange(replacement);
            Blocks.RemoveRange(Start + 1, Count - 1); Count = 1;
        }
    }
    internal void SetParagraphs(int id, IEnumerable<ParagraphBlock> paragraphs)
    {
        if (Paragraph != null) throw new NotSupportedException("Control.ProtectedOwner: Inline controls cannot contain paragraphs.");
        var replacement = paragraphs.ToList(); if (replacement.Count == 0) replacement.Add(new ParagraphBlock { FallbackFontSize = 11, FallbackAfter = 0 });
        PageSettings? section = Blocks[Start].Section; SourceLocation? location = Blocks[Start].Location;
        foreach (ParagraphBlock p in replacement) { p.TypedControlId = id; p.Section = section; p.Location = location; if (p.Runs.Count == 0) p.Runs.Add(new TextRun("")); }
        Blocks.RemoveRange(Start, Count); Blocks.InsertRange(Start, replacement); Count = replacement.Count;
        // Cell paragraph indexes are refreshed by Normalize below.
    }
}

internal static class ControlOwners
{
    internal static Dictionary<int, ControlOwner> Index(DocumentModel model, ConversionContext c)
    {
        var result = new Dictionary<int, ControlOwner>();
        Blocks(model.Blocks);
        if (result.Count != model.Controls.Items.Count || result.Keys.Any(id => !model.Controls.Items.ContainsKey(id)))
            throw new InvalidDataException("Control.ProtectedOwner: Control metadata and live owners disagree.");
        foreach (var item in result)
        {
            if (model.Controls.Items[item.Key].Inline != (item.Value.Paragraph != null))
                throw new InvalidDataException("Control.ProtectedOwner: Control ownership kind changed.");
            if (item.Value.ParentId is int parent && (!model.Controls.Items.TryGetValue(parent, out ControlData? outer)
                || outer.Inline || outer.Kind != ContentControlKind.RichText || outer.Placeholder || outer.Binding != null))
                throw new InvalidDataException("Control.ProtectedOwner: Nested controls require a non-placeholder, unbound block rich-text parent.");
        }
        return result;
        void Add(int id, ControlOwner owner)
        { if (result.ContainsKey(id)) throw new InvalidDataException("Control.ProtectedOwner: A control has disjoint owners."); result.Add(id, owner); }
        void Blocks(List<DocumentBlock> blocks)
        {
            for (int i = 0; i < blocks.Count; i++)
            {
                c.CheckCancellation(); c.ReserveControlWork(1);
                DocumentBlock block = blocks[i];
                if (block.TypedControlId is int id)
                {
                    int end = i + 1; while (end < blocks.Count && blocks[end].TypedControlId == id) { c.ReserveControlWork(1); end++; }
                    if (blocks.Skip(i).Take(end - i).Any(b => b is not ParagraphBlock || !ReferenceEquals(b.Section, block.Section)))
                        throw new InvalidDataException("Control.ProtectedOwner: Block controls require ordinary paragraphs in one section.");
                    Add(id, new ControlOwner { Blocks = blocks, Start = i, Count = end - i });
                    for (int nested = i; nested < end; nested++) Inlines((ParagraphBlock)blocks[nested], blocks, id);
                    i = end - 1; continue;
                }
                if (block is TableBlock table)
                { foreach (TableCell cell in table.Rows.SelectMany(r => r)) Blocks(cell.Content); continue; }
                var p = (ParagraphBlock)block;
                Inlines(p, blocks, null);
            }
        }
        void Inlines(ParagraphBlock p, List<DocumentBlock> blocks, int? parentId)
        {
            for (int j = 0; j < p.Runs.Count; j++)
            {
                c.ReserveControlWork(1);
                if (p.Runs[j].TypedControlId is not int inlineId) continue;
                int end = j + 1; while (end < p.Runs.Count && p.Runs[end].TypedControlId == inlineId) { c.ReserveControlWork(1); end++; }
                Add(inlineId, new ControlOwner { Blocks = blocks, Paragraph = p, Start = j, Count = end - j, ParentId = parentId }); j = end - 1;
            }
        }
    }
    internal static void Normalize(DocumentModel model)
    {
        foreach (TableBlock table in model.Tables)
            foreach (TableCell cell in table.Rows.SelectMany(r => r))
            { if (cell.Content.Count == 0) continue; cell.Paragraphs.Clear(); cell.Paragraphs.AddRange(DocumentModel.CellParagraphs(cell.Content)); }
    }
}
