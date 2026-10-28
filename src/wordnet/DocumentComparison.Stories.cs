using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WordNet.Internal;
using WordNet.Layout;
using WordNet.Model;

namespace WordNet;

public static partial class DocumentComparison
{
    private static bool PlainStory(List<DocumentBlock> story) => story.All(b => b is ParagraphBlock p && p.Control == null && p.NestedControl == null && p.TypedControlId == null && p.List == null
        && p.Runs.All(r => r.Text.Length != 0 && r.FieldInstruction == null && r.Control == null && r.TypedControlId == null && r.Bookmark == null
            && r.Box == null && r.Note == null && r.Image == null && r.Link == null && r.Object == null));

    private static List<PageSettings?> StorySections(DocumentModel document, ConversionContext context)
    {
        var sections = new List<PageSettings?>();
        foreach (PageSettings? section in document.Blocks.Select(b => b.Section).Concat(new[] { document.FinalSection }))
        {
            context.CheckCancellation();
            if (sections.Count == 0 || !ReferenceEquals(sections[sections.Count - 1], section))
            {
                if (sections.Count == 64) throw new InvalidDataException("Comparison.WorkLimit: At most 64 sections are supported.");
                sections.Add(section);
            }
        }
        return sections;
    }

    private static List<(ParagraphBlock Old, ParagraphBlock New)> MatchingStories(DocumentModel left, DocumentModel right, ConversionContext context)
    {
        var pairs = new List<(ParagraphBlock, ParagraphBlock)>();
        List<PageSettings?> a = StorySections(left, context), b = StorySections(right, context);
        if (a.Count != b.Count) return pairs;
        var mapping = new Dictionary<List<DocumentBlock>, List<DocumentBlock>>();
        var reverse = new Dictionary<List<DocumentBlock>, List<DocumentBlock>>();
        for (int i = 0; i < a.Count; i++)
        {
            context.CheckCancellation();
            if (a[i] == null || b[i] == null) { if (a[i] != b[i]) return new List<(ParagraphBlock, ParagraphBlock)>(); continue; }
            if (!a[i]!.Stories.Keys.OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(b[i]!.Stories.Keys.OrderBy(k => k, StringComparer.Ordinal))) return new List<(ParagraphBlock, ParagraphBlock)>();
            foreach (var story in a[i]!.Stories)
            {
                List<DocumentBlock> other = b[i]!.Stories[story.Key];
                if (mapping.TryGetValue(story.Value, out List<DocumentBlock>? mapped))
                {
                    if (!ReferenceEquals(mapped, other)) throw new NotSupportedException("Comparison.StoryTopology: Shared story identity must match.");
                    continue;
                }
                if (reverse.ContainsKey(other)) throw new NotSupportedException("Comparison.StoryTopology: Shared story identity must match.");
                mapping.Add(story.Value, other); reverse.Add(other, story.Value);
                if (!PlainStory(story.Value) || !PlainStory(other) || story.Value.Count != other.Count) continue;
                for (int j = 0; j < story.Value.Count; j++)
                {
                    context.CheckCancellation();
                    if (pairs.Count == 10000) throw new InvalidDataException("Comparison.WorkLimit: Too many story paragraphs.");
                    pairs.Add(((ParagraphBlock)story.Value[j], (ParagraphBlock)other[j]));
                }
            }
        }
        return pairs;
    }

    private static void NormalizePlainStories(DocumentModel document, ConversionContext context)
    {
        foreach (List<DocumentBlock> story in StorySections(document, context).Where(s => s != null).SelectMany(s => s!.Stories.Values).Distinct())
        {
            context.CheckCancellation(); if (!PlainStory(story)) continue;
            foreach (ParagraphBlock paragraph in story.Cast<ParagraphBlock>())
            {
                List<TextRun> combined = RunCoalescer.Combine(paragraph.Runs);
                paragraph.Runs.Clear(); paragraph.Runs.AddRange(combined.Select(r => r.WithText("")));
            }
        }
    }
}
