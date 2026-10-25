using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Layout;

internal static partial class FixedLayout
{
    private static List<FixedPage> BalanceColumns(DocumentModel model, ConversionContext context, List<FixedPage> pages)
    {
        var targets = pages.SelectMany(p => p.Sections).Distinct().Where(s => s.Columns > 1).ToArray();
        if (targets.Length == 0) return pages;
        if (targets.Length > 16 || model.Blocks.Count > 1000 || model.CharacterCount > 200000)
            throw new InvalidDataException("Layout.BalanceLimit: Balancing permits 16 column sections, 1,000 paragraphs and 200,000 characters.");
        if (context.Typography.Enabled || context.Options.Pdf.Accessibility != PdfAccessibility.None || model.Notes.Count != 0 || model.Blocks.Any(b => b is not ParagraphBlock)
            || model.BodyParagraphs.Any(p => p.Control != null || p.List != null || p.Layout.KeepTogether || p.Layout.KeepWithNext || p.Layout.PageBreakBefore
                || p.Runs.Any(r => r.Text.Any(c => c is '\f' or '\v') || r.Image != null || r.Box != null || r.Object != null || r.Note != null || r.FieldInstruction != null || r.Bookmark != null || r.Control != null || r.Link != null))
            || pages.Any(p => p.Sections.Count > 1 && p.Sections.Any(s => s.Columns > 1)))
            throw new NotSupportedException("Layout.BalanceProfile: Balancing requires ordinary managed paragraphs without tags, fields, links, lists, controls, keeps, explicit breaks, objects, notes, tables or shared continuous-section pages.");
        int count = pages.Count;
        var totals = pages.SelectMany(p => p.Sections).Distinct().ToDictionary(s => s, s => pages.Count(p => p.Sections.Contains(s)));
        var limits = new Dictionary<int, double>();
        foreach (PageSettings section in targets)
        {
            int index = pages.FindLastIndex(p => p.Sections.Contains(section));
            FixedPage final = pages[index];
            double low = final.Settings.Top, high = final.Settings.Height - final.Settings.Bottom;
            // Monotonic feasibility search: keep all previous pages intact, and minimize
            // the final column frame without increasing the physical page count.
            for (int pass = 0; pass < 16 && high - low > 0.01; pass++)
            {
                context.CheckCancellation(); double candidate = (low + high) / 2; limits[index + 1] = candidate;
                List<FixedPage>? measured = null;
                try { measured = CreateCore(model, context, count, totals, limits); }
                catch (NotSupportedException ex) when (ex.Message.StartsWith("Layout.ItemHeight:", StringComparison.Ordinal)) { }
                catch (InvalidDataException ex) when (ex.Message.StartsWith("Limit.Pages:", StringComparison.Ordinal)) { }
                if (measured != null && measured.Count == count && measured.SelectMany(p => p.Sections).All(totals.ContainsKey)
                    && totals.All(t => measured.Count(p => p.Sections.Contains(t.Key)) == t.Value))
                { high = candidate; pages = measured; }
                else low = candidate;
            }
            limits[index + 1] = high;
        }
        return CreateCore(model, context, count, totals, limits);
    }
}
