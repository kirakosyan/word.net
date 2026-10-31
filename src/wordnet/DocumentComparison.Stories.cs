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
    private static bool PlainStory(List<DocumentBlock> story) => story.All(b => b is ParagraphBlock p
        && ContentProfiles.OrdinaryParagraph(p, textHistory: true, formatHistory: true, comments: true, emptyRuns: false, structuralHistory: true));

    private static List<PageSettings?> Sections(DocumentModel document, ConversionContext context)
    {
        var result = new List<PageSettings?>();
        foreach (DocumentBlock block in document.Blocks)
        {
            context.CheckCancellation();
            if (result.Count == 0 || !ReferenceEquals(result[result.Count - 1], block.Section))
            {
                if (result.Count == 64) throw new InvalidDataException("Comparison.WorkLimit: At most 64 sections are supported.");
                if (result.Any(section => ReferenceEquals(section, block.Section))) throw new NotSupportedException("Comparison.Structure: Noncontiguous section ownership is unsupported.");
                result.Add(block.Section);
            }
        }
        if (result.Count == 0 || !ReferenceEquals(result[result.Count - 1], document.FinalSection))
        {
            if (result.Count == 64) throw new InvalidDataException("Comparison.WorkLimit: At most 64 sections are supported.");
            if (result.Any(section => ReferenceEquals(section, document.FinalSection))) throw new NotSupportedException("Comparison.Structure: Noncontiguous final section ownership is unsupported.");
            result.Add(document.FinalSection);
        }
        return result;
    }

    private static List<(ParagraphBlock Old, ParagraphBlock New)> MatchingStories(DocumentModel left, DocumentModel right, ConversionContext context)
    {
        var pairs = new List<(ParagraphBlock, ParagraphBlock)>();
        if (!HasStories(left) && !HasStories(right)) return pairs;
        List<PageSettings?> a = Sections(left, context), b = Sections(right, context);
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

    private static void NormalizePlainStories(DocumentModel document, ConversionContext context, bool ignoreRunFormatting, System.Action<int> reserveAdditionalWork)
    {
        if (!HasStories(document)) return;
        foreach (List<DocumentBlock> story in Sections(document, context).Where(s => s != null).SelectMany(s => s!.Stories.Values).Distinct())
        {
            context.CheckCancellation(); if (!PlainStory(story)) continue;
            foreach (ParagraphBlock paragraph in story.Cast<ParagraphBlock>())
            {
                if (ignoreRunFormatting) reserveAdditionalWork(checked(paragraph.Runs.Count + 1));
                List<TextRun> combined = RunCoalescer.Combine(paragraph.Runs);
                paragraph.Runs.Clear(); paragraph.Runs.AddRange(combined.Select(r => (ignoreRunFormatting ? Neutral(r) : r).WithText("")));
            }
        }
    }

    private static bool HasStories(DocumentModel document) => document.Blocks.Select(b => b.Section)
        .Concat(new[] { document.FinalSection }).Any(section => section != null && section.Stories.Count != 0);
}
