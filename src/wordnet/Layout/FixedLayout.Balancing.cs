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
        var limits = new Dictionary<PageSettings, (int Page, double Bottom)>();
        var bounds = targets.ToDictionary(s => s, s =>
        {
            FixedPage[] sectionPages = pages.Where(p => p.Sections.Contains(s)).ToArray();
            FixedPage final = sectionPages[sectionPages.Length - 1];
            double minimum = sectionPages.Max(p => p.MaximumAscent) + sectionPages.Max(p => p.MaximumDescent);
            return (Low: Math.Min(final.Settings.Height - final.Settings.Bottom, final.Settings.Top + minimum), High: final.Settings.Height - final.Settings.Bottom);
        });
        // Probe all independent section frames together: at most sixteen whole-document
        // probes in total, instead of sixteen probes for each section. Section-relative
        // page indexes keep an overflowing probe from shifting another section's frame.
        for (int pass = 0; pass < 16 && bounds.Values.Any(b => b.High - b.Low > .01); pass++)
        {
            context.CheckCancellation();
            foreach (PageSettings section in targets)
            {
                var bound = bounds[section]; limits[section] = (totals[section], (bound.Low + bound.High) / 2);
            }
            List<FixedPage>? measured = null;
            try { measured = CreateCore(model, context, count, totals, limits); }
            catch (NotSupportedException ex) when (ex.Message.StartsWith("Layout.ItemHeight:", StringComparison.Ordinal)) { }
            catch (InvalidDataException ex) when (ex.Message.StartsWith("Limit.Pages:", StringComparison.Ordinal)) { }
            foreach (PageSettings section in targets)
            {
                var bound = bounds[section]; double candidate = limits[section].Bottom;
                bool feasible = measured != null && measured.Count(p => p.Sections.Contains(section)) == totals[section];
                bounds[section] = feasible ? (bound.Low, candidate) : (candidate, bound.High);
            }
        }
        foreach (PageSettings section in targets) limits[section] = (totals[section], bounds[section].High);
        List<FixedPage> balanced = CreateCore(model, context, count, totals, limits);
        // Keep the known feasible layout if a combination of section/parity policies
        // changes the document's physical allocation despite individual probe results.
        return balanced.Count == count && totals.All(t => balanced.Count(p => p.Sections.Contains(t.Key)) == t.Value) ? balanced : pages;
    }
}
