using System.Linq;
using WordNet.Model;

namespace WordNet.Internal;

internal static class FidelityChecks
{
    internal static void Destination(DocumentModel model, DocumentFormat format, ConversionContext context)
    {
        foreach (TableBlock table in model.Blocks.OfType<TableBlock>()) TableValidation.Validate(table, context);
        var paragraphs = model.Paragraphs.ToArray();
        if (format is DocumentFormat.Text or DocumentFormat.Markdown or DocumentFormat.Mobi or DocumentFormat.Azw3)
        {
            if (model.Blocks.OfType<TableBlock>().SelectMany(t => t.Rows).SelectMany(r => r).Any(c => c.ColumnSpan != 1 || c.Merge != VerticalMerge.None))
                context.Loss("Table.Merges", "Merged-cell geometry is not preserved by this output profile.");
        }
        if (format is not DocumentFormat.Doc and not DocumentFormat.Docx and not DocumentFormat.Pdf and not DocumentFormat.Xps)
        {
            if (model.FinalSection != null || model.Blocks.Any(b => b.Section != null))
                context.Loss("Layout.Sections", "Page geometry and section breaks are not preserved by this reflowable output profile.");
            if (paragraphs.Any(p => p.Layout.HasValues))
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
        if (format is not DocumentFormat.Doc and not DocumentFormat.Docx and not DocumentFormat.Pdf and not DocumentFormat.Epub
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
