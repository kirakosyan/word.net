using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using WordNet.Layout;
using WordNet.Model;

namespace WordNet.Internal;

internal static class StyleReferences
{
    internal static bool HasFields(DocumentModel model) => model.Paragraphs.SelectMany(p => p.Runs).Any(r => r.FieldInstruction != null && DocumentFields.Kind(r.FieldInstruction) == "STYLEREF");

    internal static bool Refresh(DocumentModel model, List<FixedPage> pages, ConversionContext context)
    {
        var previous = new Dictionary<(TextRun, int), string>(model.StyleResults);
        model.StyleResults.Clear(); model.UnresolvedStyleReferences.Clear(); model.StylePages.Clear();
        if (context.Typography.Enabled) return false;
        var body = new HashSet<ParagraphBlock>(model.Blocks.OfType<ParagraphBlock>());
        var starts = new Dictionary<ParagraphBlock, int>();
        for (int i = 0; i < pages.Count; i++)
            foreach (ParagraphBlock paragraph in pages[i].Text.Where(t => t.SourceParagraph != null).Select(t => t.SourceParagraph!).Distinct())
                if (body.Contains(paragraph) && !starts.ContainsKey(paragraph)) starts.Add(paragraph, i);
        var positions = new Dictionary<ParagraphBlock, int>(); int index = 0;
        foreach (ParagraphBlock paragraph in model.Blocks.OfType<ParagraphBlock>()) positions.Add(paragraph, index++);
        var groups = starts.Where(p => p.Key.StyleId != null && p.Key.Runs.All(r => r.FieldInstruction == null && r.Image == null && r.Box == null && r.Note == null && r.Object == null))
            .GroupBy(p => (p.Key.Section, p.Key.StyleId!)).ToDictionary(g => g.Key, g => g.OrderBy(p => positions[p.Key]).ToArray());
        var lookup = new Dictionary<(PageSettings, string, int, bool), string>();
        long work = 0, expanded = 0;
        var paragraphText = new Dictionary<ParagraphBlock, string>();
        for (int i = 0; i < pages.Count; i++)
        {
            FixedPage page = pages[i]; PageSettings section = page.SourceSettings;
            if (!model.StylePages.TryGetValue(section, out List<int>? sectionPages)) { sectionPages = new List<int>(); model.StylePages.Add(section, sectionPages); }
            sectionPages.Add(i + 1);
            foreach (TextRun run in section.Stories.Values.SelectMany(DocumentModel.Expand).OfType<ParagraphBlock>().SelectMany(p => p.Runs).Where(r => r.FieldInstruction != null && DocumentFields.Kind(r.FieldInstruction) == "STYLEREF").Distinct())
            {
                context.CheckCancellation();
                string instruction = run.FieldInstruction!;
                if (instruction.Length > 4096) { Cached(); continue; }
                Match match = Regex.Match(instruction, "^\\s*STYLEREF\\s+(?:\"([^\"]+)\"|([^\\s\\\\]+))(?:\\s+(\\\\l))?(?:\\s+\\\\\\*\\s+MERGEFORMAT)?\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                if (!match.Success) { Cached(); continue; }
                string name = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                context.ReserveFieldWork(model.Styles.Count);
                string[] ids = model.Styles.ContainsKey(name) ? new[] { name } : model.Styles.Where(p => string.Equals(p.Value, name, StringComparison.OrdinalIgnoreCase)).Select(p => p.Key).ToArray();
                if (ids.Length != 1 || !groups.TryGetValue((section, ids[0]), out var candidates)) { Cached(); continue; }
                bool last = match.Groups[3].Success;
                var key = (section, ids[0], i, last);
                if (!lookup.TryGetValue(key, out string? result))
                {
                    work = checked(work + candidates.Length);
                    if (work > Math.Max(4096L, context.Options.MaximumCharacters * 16L)) throw new System.IO.InvalidDataException("Limit.FieldWork: Style lookup exceeds the aggregate work budget.");
                    var onPage = candidates.Where(p => p.Value == i).ToArray();
                    ParagraphBlock? chosen = onPage.Length > 0 ? (last ? onPage[onPage.Length - 1].Key : onPage[0].Key)
                        : candidates.LastOrDefault(p => p.Value < i).Key ?? candidates.FirstOrDefault(p => p.Value > i).Key;
                    if (chosen == null) result = run.Text;
                    else if (!paragraphText.TryGetValue(chosen, out result)) { result = chosen.Text; paragraphText.Add(chosen, result); }
                    context.CheckCharacters(result.Length); lookup.Add(key, result);
                }
                expanded = checked(expanded + result.Length);
                if (expanded > context.Options.MaximumCharacters || expanded * 2 + (model.StyleResults.Count + 1) * 64L > context.Options.MaximumExpandedBytes)
                    throw new System.IO.InvalidDataException("Limit.FieldResults: Running-header results exceed aggregate limits.");
                model.StyleResults[(run, i + 1)] = result;
                void Cached() => model.UnresolvedStyleReferences.Add(run);
            }
        }
        return previous.Count != model.StyleResults.Count || model.StyleResults.Any(p => !previous.TryGetValue(p.Key, out string? value) || value != p.Value);
    }
    internal static void ReportUnresolved(DocumentModel model, ConversionContext context)
    {
        var resolved = new HashSet<TextRun>(model.StyleResults.Keys.Select(k => k.Run));
        foreach (ParagraphBlock paragraph in model.Paragraphs)
            foreach (TextRun run in paragraph.Runs.Where(r => r.FieldInstruction != null && DocumentFields.Kind(r.FieldInstruction) == "STYLEREF"))
            {
                context.CheckCancellation();
                if (!resolved.Contains(run) || model.UnresolvedStyleReferences.Contains(run))
                {
                    using var location = context.At(paragraph.Location);
                    context.Report("Field.UnsupportedUsedCachedResult", "An unresolved or unsupported style reference retains its cache in the final layout.");
                }
            }
    }

}
