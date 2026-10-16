using System.Collections.Generic;
using System.Linq;
using WordNet.Model;

namespace WordNet.Internal;

internal static class FidelityChecks
{
    internal static void Destination(DocumentModel model, DocumentFormat format, ConversionContext context)
    {
        foreach (TableBlock table in model.Blocks.OfType<TableBlock>())
        {
            using var location = context.At(table.Location);
            TableValidation.Validate(table, context);
        }
        var tables = model.AllBlocks.OfType<TableBlock>().SelectMany(ExpandTables).Distinct().ToArray();
        var paragraphs = model.Paragraphs.Distinct().ToArray();
        if (format is not DocumentFormat.Html and not DocumentFormat.Docx)
        {
            LossAt(tables.Where(t => t.Rows.SelectMany(r => r).Any(c => c.Content.OfType<TableBlock>().Any())).Select(t => t.Location),
                "Table.NestedFlattened", "Nested tables retain their text in reading order as cell paragraphs in this output profile.");
            LossAt(tables.Where(t => t.Rows.SelectMany(r => r).Any(c => c.Background != null || c.Borders.Count > 0 || c.VerticalAlignment != "top")).Select(t => t.Location),
                "Table.Decoration", "This output profile omits cell shading, borders and vertical alignment while retaining cell content.");
            LossAt(paragraphs.Where(p => p.Runs.Any(r => r.Appearance.HasValues
                && (format is not DocumentFormat.Pdf and not DocumentFormat.Xps || r.Appearance.Underline || r.Appearance.Strike || r.Appearance.Background != null || r.Appearance.VerticalAlignment != null))).Select(p => p.Location),
                "Formatting.RunAppearance", "This output profile does not retain requested font families or additional text decoration.");
        }
        if (format == DocumentFormat.Doc)
            LossAt(paragraphs.Where(p => p.Layout.Tabs.Count > 0).Select(p => p.Location), "Layout.DocTabStops", "Binary DOC retains tab characters with default stops; custom tab definitions are not retained.");
        if (format is not DocumentFormat.Doc and not DocumentFormat.Docx and not DocumentFormat.Pdf and not DocumentFormat.Html and not DocumentFormat.Xps)
        {
            LossAt(model.AllBlocks.Except(model.Blocks).Select(b => b.Location), "HeaderFooter.Output", "This output profile does not preserve header/footer stories.");
            LossAt(paragraphs.Where(p => p.Runs.Any(r => r.FieldInstruction != null)).Select(p => p.Location), "Field.Output", "Fields use cached result text in this output profile.");
        }
        if (format is DocumentFormat.Text or DocumentFormat.Markdown or DocumentFormat.Mobi or DocumentFormat.Azw3)
            LossAt(tables.Where(t => t.Rows.SelectMany(r => r).Any(c => c.ColumnSpan != 1 || c.Merge != VerticalMerge.None)).Select(t => t.Location), "Table.Merges", "Merged-cell geometry is not preserved by this output profile.");
        if (format is not DocumentFormat.Doc and not DocumentFormat.Docx and not DocumentFormat.Pdf and not DocumentFormat.Xps)
        {
            if (model.FinalSection != null || model.Blocks.Any(b => b.Section != null))
            {
                if (format == DocumentFormat.Html) context.Report("Html.PageGeometryIgnored", "Page geometry and section breaks are ignored by the reflowable HTML policy.");
                else context.Loss("Layout.Sections", "Page geometry and section breaks are not preserved by this reflowable output profile.");
            }
            if (format != DocumentFormat.Html)
                LossAt(paragraphs.Where(p => p.Layout.HasValues).Select(p => p.Location), "Layout.Paragraph", "Paragraph spacing, indentation and pagination controls are not preserved by this output profile.");
            LossAt(tables.Where(t => t.NonSplittingRows.Count > 0).Select(t => t.Location), "Table.Pagination", "Non-splitting table-row controls are not preserved by this reflowable output profile.");
        }
        if (format is DocumentFormat.Text or DocumentFormat.Markdown)
        {
            LossAt(paragraphs.Where(p => p.Runs.Any(r => r.FontSize.HasValue || r.Color != null)).Select(p => p.Location), format == DocumentFormat.Text ? "Text.Formatting" : "Markdown.RunAppearance", "Font sizes and colors are not represented in this text format.");
            LossAt(tables.Where(t => t.LeftIndent != 0 || t.Width.HasValue || t.ColumnWidths.Any(w => w > 0) || t.Rows.SelectMany(r => r).Any(c => c.CellWidth.HasValue || c.Padding != null)).Select(t => t.Location), "Table.Geometry", "This text format cannot retain table widths and cell padding.");
            LossAt(paragraphs.Where(p => p.List != null).Select(p => p.Location), "List.Structure", "Editable list numbering becomes literal labels in this text profile.");
        }
        if (format is not DocumentFormat.Doc and not DocumentFormat.Docx and not DocumentFormat.Pdf and not DocumentFormat.Epub and not DocumentFormat.Html)
        {
            LossAt(paragraphs.Where(p => p.Runs.Any(r => r.Image != null)).Select(p => p.Location), "Image.Output", "This output profile cannot embed images; alternative text is retained where available.");
            foreach (ParagraphBlock paragraph in paragraphs)
                for (int i = 0; i < paragraph.Runs.Count; i++)
                {
                    TextRun run = paragraph.Runs[i];
                    if (run.Image != null) paragraph.Runs[i] = new TextRun(run.Image.AlternativeText, run.Bold, run.Italic, run.Code, run.Link, run.FontSize, run.Color);
                }
        }
        if (format is DocumentFormat.Mobi or DocumentFormat.Azw3)
            LossAt(paragraphs.Where(p => p.List != null).Select(p => p.Location), "Kindle.ListLayout", "Kindle list indentation depends on the reading application's reflow layout.");
        if (format is DocumentFormat.Pdf or DocumentFormat.Xps)
        {
            LossAt(paragraphs.Where(p => p.IsCodeBlock || p.Runs.Any(r => r.Code)).Select(p => p.Location), "Layout.CodeFont", "Code content uses the embedded proportional font.");
            LossAt(paragraphs.Where(p => p.Runs.Any(r => r.Text.Any(c => c >= '\u0590' && c <= '\u0dff' || char.IsSurrogate(c)))).Select(p => p.Location),
                "Layout.Shaping", "Unsupported complex-script shaping and supplementary characters use visible fallback characters.");
            if (format == DocumentFormat.Xps)
                LossAt(paragraphs.Where(p => p.Runs.Any(r => r.Link != null)).Select(p => p.Location), "Layout.Hyperlink", "This XPS profile retains hyperlink text without an interactive target.");
            LossAt(tables.SelectMany(t => t.Rows).SelectMany(r => r).Where(c => c.Merge != VerticalMerge.Continue).SelectMany(c => c.Paragraphs).Distinct()
                .Where(p => p.Layout.PageBreakBefore || p.Layout.KeepTogether || p.Layout.KeepWithNext).Select(p => p.Location),
                "Layout.CellPageControls", "Paragraph page controls inside table cells use the table row's pagination policy.");
            for (int i = 0; i + 1 < model.Blocks.Count; i++)
                if (model.Blocks[i] is ParagraphBlock paragraph && paragraph.Layout.KeepWithNext && model.Blocks[i + 1] is TableBlock)
                    LossAt(new[] { paragraph.Location }, "Layout.KeepWithTable", "Keeping a paragraph with the following table is not supported by this layout profile.");
        }

        void LossAt(IEnumerable<SourceLocation?> locations, string code, string message)
        {
            foreach (SourceLocation? source in locations)
            {
                using var location = context.At(source);
                context.Loss(code, message);
            }
        }
    }

    private static IEnumerable<TableBlock> ExpandTables(TableBlock table)
    {
        yield return table;
        foreach (TableBlock child in table.Rows.SelectMany(r => r).SelectMany(c => c.Content).OfType<TableBlock>().SelectMany(ExpandTables)) yield return child;
    }
}
