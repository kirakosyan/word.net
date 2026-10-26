using System;
using System.Collections.Generic;
using System.Text;
using WordNet.Model;

namespace WordNet.Layout;

internal static class RunCoalescer
{
    // Merge engines often split an otherwise identical word into many runs.
    // Coalesce linearly before shaping so run boundaries do not disable kerning.
    internal static List<TextRun> Combine(List<TextRun> runs)
    {
        var result = new List<TextRun>();
        for (int i = 0; i < runs.Count; i++)
        {
            TextRun first = runs[i];
            if (first.Object != null || first.Image != null || first.Note != null || first.Box != null || first.Bookmark != null || first.FieldInstruction != null || first.Control != null
                || first.Revision != null || first.Comment != null || i + 1 == runs.Count || !Same(first, runs[i + 1]))
            { result.Add(first); continue; }
            var text = new StringBuilder(first.Text);
            while (i + 1 < runs.Count && Same(first, runs[i + 1])) text.Append(runs[++i].Text);
            result.Add(first.WithText(text.ToString()));
        }
        return result;
    }

    internal static bool Same(TextRun a, TextRun b) => b.Object == null && b.Image == null && b.Note == null && b.Box == null && b.Bookmark == null && b.FieldInstruction == null && b.Control == null
        && b.Revision == null && b.Comment == null
        && a.ControlDisplay == b.ControlDisplay && a.TypedControlId == b.TypedControlId
        && a.BidiLevel == b.BidiLevel
        && a.Bold == b.Bold && a.Italic == b.Italic && a.Code == b.Code && a.Link == b.Link && a.FontSize == b.FontSize && a.Color == b.Color
        && string.Equals(a.Appearance.FontFamily, b.Appearance.FontFamily, StringComparison.OrdinalIgnoreCase)
        && a.Appearance.Background == b.Appearance.Background && a.Appearance.Underline == b.Appearance.Underline
        && a.Appearance.Strike == b.Appearance.Strike && a.Appearance.VerticalAlignment == b.Appearance.VerticalAlignment;
}
