using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using WordNet.Model;

namespace WordNet.Internal;

internal static class DocumentReferences
{
    internal static bool HasFields(DocumentModel model) => model.Paragraphs.SelectMany(p => p.Runs).Any(r => r.FieldInstruction != null && DocumentFields.Kind(r.FieldInstruction) is "REF" or "PAGEREF" or "SEQ" or "TOC");

    internal static bool Refresh(DocumentModel model, ConversionContext context)
    {
        if (!model.ReferencesPrepared)
        {
            var names = new HashSet<string>(model.Paragraphs.SelectMany(p => p.Runs).Where(r => r.Bookmark != null).Select(r => r.Bookmark!), StringComparer.Ordinal);
            int id = 0;
            foreach (ParagraphBlock heading in model.BodyParagraphs.Where(p => p.HeadingLevel > 0).ToArray())
            {
                string name;
                do { name = "_wordnet_heading_" + (++id).ToString(CultureInfo.InvariantCulture); } while (!names.Add(name));
                heading.Runs.Insert(0, new TextRun("", bookmark: name)); heading.Runs.Add(new TextRun("", bookmark: "/" + name));
            }
            model.ReferencesPrepared = true;
        }
        ParagraphBlock[] paragraphs = model.Paragraphs.ToArray();
        var ranges = new Dictionary<string, List<TextRun>>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        var counter = new ListCounter();
        foreach (ParagraphBlock paragraph in paragraphs)
        {
            context.CheckCancellation();
            string label = paragraph.List == null ? "" : counter.Label(paragraph.List);
            foreach (TextRun run in paragraph.Runs)
            {
                if (run.Bookmark != null)
                {
                    if ((run.Bookmark.Length > 0 && run.Bookmark[0] == '/')) active.Remove(run.Bookmark.Substring(1));
                    else { ranges[run.Bookmark] = new List<TextRun>(); active.Add(run.Bookmark); labels[run.Bookmark] = label; }
                }
                else foreach (string name in active) ranges[name].Add(run);
            }
        }
        var previous = new Dictionary<TextRun, string>(model.FieldResults);
        model.FieldResults.Clear(); model.TocTargets.Clear();
        var sequences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (TextRun run in paragraphs.SelectMany(p => p.Runs).Where(r => r.FieldInstruction != null && DocumentFields.Kind(r.FieldInstruction) == "SEQ"))
        {
            string[] args = Arguments(run.FieldInstruction!);
            if (args.Length < 2 || !ValidSwitches(args, "\\r", "\\c", "\\n", "\\*")) { Cached(run); continue; }
            string name = args[1], format = Switch(args, "\\*") ?? "decimal";
            if (format.Equals("MERGEFORMAT", StringComparison.OrdinalIgnoreCase)) format = "decimal";
            format = Format(format);
            if (format.Length == 0) { Cached(run); continue; }
            int value = sequences.TryGetValue(name, out int last) ? last : 0;
            string? restart = Switch(args, "\\r");
            if (restart != null)
            {
                if (!int.TryParse(restart, NumberStyles.None, CultureInfo.InvariantCulture, out value) || value < 0 || value == int.MaxValue) { Cached(run); continue; }
            }
            else if (!args.Contains("\\c")) value = checked(value + 1);
            sequences[name] = value; model.FieldResults[run] = ListCounter.Number(value, format);
        }
        foreach (TextRun run in paragraphs.SelectMany(p => p.Runs).Where(r => r.FieldInstruction != null)) Evaluate(run, new HashSet<TextRun>());
        context.CheckCharacters(checked(model.CharacterCount + model.FieldResults.Values.Sum(v => v.Length)));
        return previous.Count != model.FieldResults.Count || model.FieldResults.Any(p => !previous.TryGetValue(p.Key, out string? value) || p.Value != value);

        string Evaluate(TextRun run, HashSet<TextRun> visiting)
        {
            context.CheckCancellation();
            if (model.FieldResults.TryGetValue(run, out string? existing)) return existing;
            if (run.FieldInstruction == null) return run.Text;
            string kind = DocumentFields.Kind(run.FieldInstruction);
            if (kind is not "REF" and not "PAGEREF" and not "TOC") return run.Text;
            if (!visiting.Add(run) || visiting.Count > context.Options.MaximumNestingDepth) return Cached(run);
            string[] args = Arguments(run.FieldInstruction);
            string result;
            if (kind == "TOC")
            {
                if (!ValidSwitches(args, "\\o", "\\h", "\\u")) return Cached(run);
                string[] levels = (Switch(args, "\\o") ?? "1-3").Split('-');
                if (levels.Length != 2 || !int.TryParse(levels[0], out int first) || !int.TryParse(levels[1], out int last) || first < 1 || last > 6 || first > last) return Cached(run);
                var entries = new List<string>(); var targets = new List<string>();
                foreach (ParagraphBlock heading in model.BodyParagraphs.Where(p => p.HeadingLevel >= first && p.HeadingLevel <= last))
                {
                    string name = heading.Runs.First(r => r.Bookmark?.StartsWith("_wordnet_heading_", StringComparison.Ordinal) == true).Bookmark!;
                    string title = string.Concat(heading.Runs.Where(r => r.Bookmark == null).Select(r => Evaluate(r, visiting)));
                    string page = model.BookmarkPages.TryGetValue(name, out var destination) ? destination.Label : "1";
                    entries.Add((labels[name].Length == 0 ? "" : labels[name] + " ") + title + "\t" + page); targets.Add(name);
                }
                result = string.Join("\n", entries); if (args.Contains("\\h")) model.TocTargets[run] = targets;
            }
            else
            {
                if (args.Length < 2 || !ValidSwitches(args, "\\h", "\\n") || !ranges.TryGetValue(args[1], out List<TextRun>? contents)) return Cached(run);
                if (kind == "PAGEREF") result = model.BookmarkPages.TryGetValue(args[1], out var destination) ? destination.Label : "1";
                else if (args.Contains("\\n")) result = labels[args[1]];
                else if (contents.Any(r => r.Image != null || r.Box != null)) return Cached(run);
                else result = string.Concat(contents.Select(r => Evaluate(r, visiting)));
            }
            visiting.Remove(run); model.FieldResults[run] = result; return result;
        }
        string Cached(TextRun run)
        { context.Report("Field.UnsupportedUsedCachedResult", "An unsupported, cyclic or unresolved reference retains its cached result."); model.FieldResults[run] = run.Text; return run.Text; }
    }

    private static string[] Arguments(string instruction)
    {
        if (instruction.Length > 4096) return Array.Empty<string>();
        return Regex.Matches(instruction, "\"([^\"]*)\"|(\\S+)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            .Cast<Match>().Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToArray();
    }
    private static string? Switch(string[] args, string name)
    { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
    private static bool ValidSwitches(string[] args, params string[] supported)
    {
        if (args.Length == 0) return false;
        for (int i = args[0].Equals("TOC", StringComparison.OrdinalIgnoreCase) ? 1 : 2; i < args.Length; i++)
        {
            if (!supported.Contains(args[i])) return false;
            if (args[i] is "\\o" or "\\r" or "\\*")
            {
                if (++i >= args.Length || args[i].Length == 0 || args[i][0] == '\\') return false;
            }
        }
        return true;
    }
    private static string Format(string format) => format switch
    { "decimal" or "ARABIC" => "decimal", "lowerRoman" or "roman" => "lowerRoman", "upperRoman" or "ROMAN" => "upperRoman", "lowerLetter" or "alphabetic" => "lowerLetter", "upperLetter" or "ALPHABETIC" => "upperLetter", _ => "" };
}
