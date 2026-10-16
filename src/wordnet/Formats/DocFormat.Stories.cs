using System;
using System.Collections.Generic;
using System.IO;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Formats;

internal static partial class DocFormat
{
    private static readonly string[] HeaderKinds = { "header-even", "header-default", "footer-even", "footer-default", "header-first", "footer-first" };

    private static void ReadHeaders(string allText, byte[] word, byte[] table, List<TextPiece> pieces,
        List<PropertyRun> paragraphRuns, List<PropertyRun> characterRuns, PropertyReader properties,
        Dictionary<int, ListItem[]> lists, List<(int End, PageSettings Settings)> sections, ConversionContext context)
    {
        int count = I32(word, 84), offset = I32(word, 154 + 11 * 8), length = I32(word, 158 + 11 * 8);
        if (count == 0) return;
        Bounds(table, offset, length);
        int required = checked((6 + sections.Count * 6 + 2) * 4);
        if (length < required || length % 4 != 0) throw new InvalidDataException("Doc.Headers: Invalid header story table.");
        int baseCp = checked(I32(word, 76) + I32(word, 80));
        if (baseCp > allText.Length - count) throw new InvalidDataException("Doc.Headers: Incomplete header text.");
        int dopOffset = I32(word, 154 + 31 * 8), dopLength = I32(word, 158 + 31 * 8);
        bool evenAndOdd = false;
        if (dopLength > 0) { Bounds(table, dopOffset, dopLength); evenAndOdd = (table[dopOffset] & 1) != 0; }
        for (int section = 0; section < sections.Count; section++)
        {
            PageSettings settings = sections[section].Settings; settings.EvenAndOdd = evenAndOdd;
            for (int kind = 0; kind < 6; kind++)
            {
                int index = 6 + section * 6 + kind;
                int start = I32(table, offset + index * 4), end = I32(table, offset + (index + 1) * 4);
                if (start < 0 || end < start || end >= count) throw new InvalidDataException("Doc.Headers: Invalid story character range.");
                if (start == end)
                {
                    if (section > 0 && sections[section - 1].Settings.Stories.TryGetValue(HeaderKinds[kind], out List<DocumentBlock>? previous))
                        settings.Stories.Add(HeaderKinds[kind], previous);
                    continue;
                }
                if (end - start < 2 || allText[baseCp + end - 1] != '\r' || allText[baseCp + end - 2] != '\r')
                    throw new InvalidDataException("Doc.Headers: Header/footer story must end with a content and guard paragraph.");
                string story = allText.Substring(baseCp + start, end - start - 1);
                var storySections = new List<(int End, PageSettings Settings)> { (story.Length, new PageSettings()) };
                DocumentModel content = ReadContent(story, baseCp + start, pieces, paragraphRuns, characterRuns, properties, lists, storySections, context);
                settings.Stories.Add(HeaderKinds[kind], content.Blocks);
            }
        }
    }
}
