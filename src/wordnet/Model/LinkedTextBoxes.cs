using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WordNet.Internal;

namespace WordNet.Model;

internal static class LinkedTextBoxes
{
    internal static Dictionary<PositionedBox, List<PositionedBox>> Validate(DocumentModel document, ConversionContext context)
    {
        if (!document.HasLinkedTextBoxes) return new Dictionary<PositionedBox, List<PositionedBox>>();
        var body = new HashSet<ParagraphBlock>(document.Blocks.OfType<ParagraphBlock>());
        var owners = new List<(PositionedBox Box, ParagraphBlock Paragraph)>();
        foreach (ParagraphBlock paragraph in document.Paragraphs)
            foreach (TextRun run in paragraph.Runs)
            {
                context.CheckCancellation();
                if (run.Box is not PositionedBox box || box.StoryId == null) continue;
                owners.Add((box, paragraph));
            }
        var result = new Dictionary<PositionedBox, List<PositionedBox>>();
        foreach (var group in owners.GroupBy(pair => pair.Box.StoryId))
        {
            context.CheckCancellation(); var entries = group.ToArray();
            // An explicit head identifies zero without capturing unrelated implicit heads.
            if (entries.Any(e => e.Box.StorySequence == 0 && !e.Box.ImplicitStoryId))
                entries = entries.Where(e => e.Box.StorySequence > 0 || !e.Box.ImplicitStoryId).ToArray();
            if (!entries.Any(e => e.Box.StorySequence > 0)) continue;
            context.ReserveBoxFitWork(entries.Length);
            if (entries.Length is < 2 or > 64 || entries[0].Box.StorySequence != 0
                || entries.Where((e, i) => e.Box.StorySequence != i).Any())
                throw new InvalidDataException("Shape.TextBoxChain: Linked boxes need one head and contiguous, unique ordered sequences.");
            ParagraphBlock firstOwner = entries[0].Paragraph;
            foreach (var entry in entries)
            {
                PositionedBox box = entry.Box;
                if (!body.Contains(entry.Paragraph) || !ReferenceEquals(entry.Paragraph.Section, firstOwner.Section) || box.Rotation != 0 || box.ShrinkToFit || box.Anchor.UnqualifiedLayer
                    || box.Width <= box.Padding.Left + box.Padding.Right || box.Height <= box.Padding.Top + box.Padding.Bottom
                    || entry.Paragraph.Control != null || entry.Paragraph.NestedControl != null || entry.Paragraph.TypedControlId != null || entry.Paragraph.List != null
                    || box.StorySequence > 0 && box.Blocks.Count != 0)
                    throw new NotSupportedException("Shape.TextBoxChain: Chains require fixed, unrotated same-section boxes with content only in the head.");
                if (box.Blocks.Any(b => b is not ParagraphBlock p || !ContentProfiles.OrdinaryParagraph(p, decorations: false, structuralHistory: true)))
                    throw new NotSupportedException("Shape.TextBoxChain: Chain content supports ordinary paragraphs only.");
            }
            List<PositionedBox> chain = entries.Select(e => e.Box).ToList();
            foreach (PositionedBox box in chain) result.Add(box, chain);
        }
        return result;
    }
}
