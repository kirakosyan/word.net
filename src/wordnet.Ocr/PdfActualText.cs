using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UglyToad.PdfPig.Content;
using WordNet.Internal;

namespace WordNet.Ocr;

// Admit a wholly covered logical carrier page. Mixed uncovered content keeps
// the existing import path rather than dropping visible text while replacing spans.
internal static class PdfActualText
{
    internal static string? Read(Page page, ConversionContext context)
    {
        IReadOnlyList<MarkedContentElement> marked = page.GetMarkedContents();
        if (marked.Count == 0) return null;
        if (page.Letters.Count * 64L > context.Options.MaximumExpandedBytes) throw new InvalidDataException("Pdf.ActualTextLimit: Logical carrier coverage exceeds its operation allowance.");
        var spans = new List<(string Text, IReadOnlyList<Letter> Letters)>(); var covered = new HashSet<Letter>(); int nodes = 0; long characters = 0;
        void Visit(MarkedContentElement element, int depth)
        {
            context.CheckCancellation();
            if (++nodes > 100000 || nodes * 128L > context.Options.MaximumExpandedBytes || depth > context.Options.MaximumNestingDepth)
                throw new InvalidDataException("Pdf.ActualTextLimit: Logical marked content exceeds its operation allowance.");
            string? replacement = element.ActualText;
            if (replacement != null && replacement.Length > 0 && element.Letters.Count > 0)
            {
                characters += replacement.Length; if (characters > context.Options.MaximumCharacters) throw new InvalidDataException("Pdf.ActualTextLimit: Logical text exceeds its character allowance.");
                spans.Add((replacement, element.Letters)); foreach (Letter letter in element.Letters) if (!covered.Add(letter)) throw new InvalidDataException("Pdf.ActualTextOverlap: Replacement spans overlap.");
            }
            else foreach (MarkedContentElement child in element.Children) Visit(child, depth + 1);
        }
        foreach (MarkedContentElement element in marked) Visit(element, 0);
        if (spans.Count == 0 || page.Letters.Any(l => !covered.Contains(l))) return null;
        var text = new StringBuilder(); double? previousY = null;
        foreach (var span in spans.OrderBy(s => s.Letters.Min(l => l.TextSequence)))
        {
            double y = span.Letters[0].StartBaseLine.Y;
            if (previousY.HasValue && Math.Abs(y - previousY.Value) > Math.Max(1, span.Letters[0].PointSize / 2)) text.Append('\n');
            text.Append(span.Text); previousY = y;
        }
        return text.ToString();
    }
}
