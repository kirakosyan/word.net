using System.Linq;
using WordNet.Model;

namespace WordNet.Internal;

internal static class FidelityChecks
{
    internal static void Destination(DocumentModel model, DocumentFormat format, ConversionContext context)
    {
        foreach (TableBlock table in model.Blocks.OfType<TableBlock>()) TableValidation.Validate(table, context);
        if (format is not DocumentFormat.Html and not DocumentFormat.Docx && model.AllBlocks.OfType<TableBlock>().Any(t => t.Rows.SelectMany(r => r).Any(c => c.Content.OfType<TableBlock>().Any())))
            context.Loss("Table.NestedFlattened", "Nested tables retain their text in reading order as cell paragraphs in this output profile.");
        var paragraphs = model.Paragraphs.ToArray();
        if (format == DocumentFormat.Doc && paragraphs.Any(p => p.Layout.Tabs.Count > 0))
            context.Loss("Layout.DocTabStops", "Binary DOC retains tab characters with default stops; custom tab definitions are not retained.");
        if (format is not DocumentFormat.Html and not DocumentFormat.Docx && model.AllBlocks.OfType<TableBlock>().SelectMany(t => t.Rows).SelectMany(r => r)
            .Any(c => c.Background != null || c.Borders.Count > 0 || c.VerticalAlignment != "top"))
            context.Loss("Table.Decoration", "This output profile omits cell shading, borders and vertical alignment while retaining cell content.");
        if (format is not DocumentFormat.Doc and not DocumentFormat.Docx and not DocumentFormat.Pdf and not DocumentFormat.Html)
        {
            if (model.AllBlocks.Count() != model.Blocks.Count) context.Loss("HeaderFooter.Output", "This output profile does not preserve header/footer stories.");
            if (paragraphs.Any(p => p.Runs.Any(r => r.FieldInstruction != null))) context.Loss("Field.Output", "Fields use cached result text in this output profile.");
        }
        if (format is not DocumentFormat.Html and not DocumentFormat.Docx && paragraphs.Any(p => p.Runs.Any(r => r.Appearance.HasValues
            && (format != DocumentFormat.Pdf || r.Appearance.Underline || r.Appearance.Strike || r.Appearance.Background != null || r.Appearance.VerticalAlignment != null))))
            context.Loss("Formatting.RunAppearance", "This output profile does not retain requested font families or additional text decoration.");
        if (format is DocumentFormat.Text or DocumentFormat.Markdown or DocumentFormat.Mobi or DocumentFormat.Azw3)
        {
            if (model.Blocks.OfType<TableBlock>().SelectMany(t => t.Rows).SelectMany(r => r).Any(c => c.ColumnSpan != 1 || c.Merge != VerticalMerge.None))
                context.Loss("Table.Merges", "Merged-cell geometry is not preserved by this output profile.");
        }
        if (format is not DocumentFormat.Doc and not DocumentFormat.Docx and not DocumentFormat.Pdf and not DocumentFormat.Xps)
        {
            if (model.FinalSection != null || model.Blocks.Any(b => b.Section != null))
            {
                if (format == DocumentFormat.Html) context.Report("Html.PageGeometryIgnored", "Page geometry and section breaks are ignored by the reflowable HTML policy.");
                else context.Loss("Layout.Sections", "Page geometry and section breaks are not preserved by this reflowable output profile.");
            }
            if (format != DocumentFormat.Html && paragraphs.Any(p => p.Layout.HasValues))
                context.Loss("Layout.Paragraph", "Paragraph spacing, indentation and pagination controls are not preserved by this output profile.");
            if (model.Blocks.OfType<TableBlock>().Any(t => t.NonSplittingRows.Count > 0))
                context.Loss("Table.Pagination", "Non-splitting table-row controls are not preserved by this reflowable output profile.");
        }
        if (format is DocumentFormat.Text or DocumentFormat.Markdown)
        {
            if (paragraphs.Any(p => p.Runs.Any(r => r.FontSize.HasValue || r.Color != null)))
                context.Loss(format == DocumentFormat.Text ? "Text.Formatting" : "Markdown.RunAppearance", "Font sizes and colors are not represented in this text format.");
            if (model.Blocks.OfType<TableBlock>().Any(t => t.LeftIndent != 0 || t.Width.HasValue || t.ColumnWidths.Any(w => w > 0) || t.Rows.SelectMany(r => r).Any(p => p.CellWidth.HasValue || p.Padding != null)))
                context.Loss("Table.Geometry", "This text format cannot retain table widths and cell padding.");
            if (paragraphs.Any(p => p.List != null)) context.Loss("List.Structure", "Editable list numbering becomes literal labels in this text profile.");
        }
        if (format is not DocumentFormat.Doc and not DocumentFormat.Docx and not DocumentFormat.Pdf and not DocumentFormat.Epub and not DocumentFormat.Html
            && paragraphs.Any(p => p.Runs.Any(r => r.Image != null)))
        {
            context.Loss("Image.Output", "This output profile cannot embed images; alternative text is retained where available.");
            foreach (ParagraphBlock paragraph in paragraphs)
                for (int i = 0; i < paragraph.Runs.Count; i++)
                {
                    TextRun run = paragraph.Runs[i];
                    if (run.Image != null) paragraph.Runs[i] = new TextRun(run.Image.AlternativeText, run.Bold, run.Italic, run.Code, run.Link, run.FontSize, run.Color);
                }
        }
        if (format is DocumentFormat.Mobi or DocumentFormat.Azw3 && paragraphs.Any(p => p.List != null))
            context.Loss("Kindle.ListLayout", "Kindle list indentation depends on the reading application's reflow layout.");
    }
}
