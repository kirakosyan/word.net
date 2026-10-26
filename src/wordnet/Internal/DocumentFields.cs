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
        if (value.Length > 0 && value[0] == '=') return "=";
        while (length < value.Length && !char.IsWhiteSpace(value[length])) length++;
        return value.Substring(0, length).ToUpperInvariant();
    }
    internal static bool Supported(string instruction) => Kind(instruction) is "PAGE" or "NUMPAGES" or "SECTIONPAGES" or "DATE" or "TIME" or "TITLE" or "AUTHOR" or "HYPERLINK" or "REF" or "PAGEREF" or "SEQ" or "TOC" or "STYLEREF" or "DOCPROPERTY" or "IF" or "=";
    internal static bool NeedsPages(DocumentModel model) => model.Paragraphs.Any(p => p.Runs.Any(r => r.FieldInstruction != null && Kind(r.FieldInstruction) is "PAGE" or "NUMPAGES" or "SECTIONPAGES"));

    internal static string Render(TextRun run, DocumentModel model, int page, int pages, int sectionPages, ConversionContext context)
    {
        if (model.FieldResults.TryGetValue(run, out string? calculated)) return calculated;
        if (run.Note != null) return run.Note.Label;
        if (run.FieldInstruction == null) return run.Text;
        if (ScalarFields.Candidate(run.FieldInstruction)) return ScalarFields.Render(run, model, context);
        switch (Kind(run.FieldInstruction))
        {
            case "STYLEREF":
                if (context.PhysicalPage > 0 && model.StyleResults.TryGetValue((run, context.PhysicalPage), out string? running)) { context.ReserveFieldWork(running.Length); return running; }
                return run.Text;
            case "PAGE": return ListCounter.Number(page, context.PageNumberFormat);
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
