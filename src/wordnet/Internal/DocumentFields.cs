using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using WordNet.Model;

namespace WordNet.Internal;

internal static class DocumentFields
{
    internal static string Kind(string instruction)
    {
        string value = instruction.Trim(); int length = 0;
        while (length < value.Length && !char.IsWhiteSpace(value[length])) length++;
        return value.Substring(0, length).ToUpperInvariant();
    }
    internal static bool Supported(string instruction) => Kind(instruction) is "PAGE" or "NUMPAGES" or "SECTIONPAGES" or "DATE" or "TIME" or "TITLE" or "AUTHOR" or "HYPERLINK";
    internal static bool NeedsPages(DocumentModel model) => model.Paragraphs.Any(p => p.Runs.Any(r => r.FieldInstruction != null && Kind(r.FieldInstruction) is "PAGE" or "NUMPAGES" or "SECTIONPAGES"));

    internal static string Render(TextRun run, DocumentModel model, int page, int pages, int sectionPages, ConversionContext context)
    {
        if (run.FieldInstruction == null) return run.Text;
        switch (Kind(run.FieldInstruction))
        {
            case "PAGE": return page.ToString(CultureInfo.InvariantCulture);
            case "NUMPAGES": return pages.ToString(CultureInfo.InvariantCulture);
            case "SECTIONPAGES": return sectionPages.ToString(CultureInfo.InvariantCulture);
            case "TITLE": return model.Title;
            case "AUTHOR": return model.Author;
            case "DATE":
            case "TIME":
                string format = Kind(run.FieldInstruction) == "DATE" ? "yyyy-MM-dd" : "HH:mm:ss";
                Match match = Regex.Match(run.FieldInstruction, "\\\\@\\s+\"([^\"]{1,128})\"", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                if (match.Success) format = match.Groups[1].Value.Replace("AM/PM", "tt");
                try { return context.FieldTime.ToString(format, CultureInfo.InvariantCulture); }
                catch (FormatException) { context.Report("Field.UnsupportedUsedCachedResult", "An unsupported date/time format uses the cached result."); return run.Text; }
            default: return run.Text;
        }
    }
}
