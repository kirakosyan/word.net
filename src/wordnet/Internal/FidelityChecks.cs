using System.Collections.Generic;
using System.Linq;
using WordNet.Model;

namespace WordNet.Internal;

internal static class FidelityChecks
{
    internal static void Destination(DocumentModel model, DocumentFormat format, ConversionContext context)
    {
        if (format is DocumentFormat.Docx or DocumentFormat.Pdf or DocumentFormat.Xps)
            foreach (ParagraphBlock paragraph in model.Paragraphs)
            {
                using var location = context.At(paragraph.Location);
                foreach (TextRun run in paragraph.Runs.Where(r => r.FieldInstruction != null && ScalarFields.Candidate(r.FieldInstruction)))
                    model.FieldResults[run] = ScalarFields.Render(run, model, context);
            }
        if (model.ObjectCount != 0 && (context.Typography.Enabled || !context.PreviewIntermediate && format == DocumentFormat.Pdf
            && context.Options.Pdf.Accessibility != PdfAccessibility.None && context.Options.Pdf.Conformance != PdfConformance.Pdf17))
            throw new System.NotSupportedException("Object.Combination: Native typography with objects and combined archival/accessibility profiles remain unsupported.");
        if (model.ObjectCount != 0 && !context.PreviewIntermediate && format == DocumentFormat.Pdf && context.Options.Pdf.Accessibility != PdfAccessibility.None)
            foreach (TextRun run in model.Paragraphs.SelectMany(p => p.Runs).Where(r => r.Object != null))
                _ = ObjectAlternatives.Describe(run.Object!, context);
        if (model.ObjectCount != 0 && format != DocumentFormat.Docx && !context.PreviewIntermediate)
        {
            context.Loss("Object.EditableOutputLoss", "This destination cannot retain editable chart or OMML semantics.");
            if (format is not DocumentFormat.Pdf and not DocumentFormat.Xps) context.Loss("Object.VisualOutputLoss", "This destination retains object descriptions instead of chart/equation graphics.");
        }
        // The ODT adapter diagnoses its own bounded mapping rather than inheriting ebook losses.
        if (format == DocumentFormat.Odt)
        {
            if (context.Inspecting) WordNet.Formats.OdtFormat.InspectDestination(model, context);
            return;
        }
        if (format != DocumentFormat.Docx && (model.Paragraphs.Any(p => p.Control != null || p.Runs.Any(r => r.Control != null || r.FieldInstruction != null && DocumentFields.Kind(r.FieldInstruction) == "MERGEFIELD"))
            || model.Tables.Any(t => t.Control != null || t.RowControls.Count > 0)))
            context.Loss("Template.UnmergedOutput", "Unexecuted template constructs use cached content without editable template semantics in this destination.");
        foreach (TableBlock table in model.Tables)
        {
            using var location = context.At(table.Location);
            TableValidation.Validate(table, context);
        }
        if (format is not DocumentFormat.Docx and not DocumentFormat.Pdf and not DocumentFormat.Xps and not DocumentFormat.Html && model.Notes.Count > 0)
        {
            foreach (DocumentNote note in model.Notes)
            {
                using var location = context.At(note.Blocks.FirstOrDefault()?.Location);
                context.Loss("Notes.PlacedAtEnd", "This destination retains notes as numbered end text without editable note semantics.");
                if (note.Blocks.FirstOrDefault() is ParagraphBlock first) first.Runs.Insert(0, new TextRun("[" + note.Label + "] "));
                foreach (DocumentBlock block in note.Blocks) block.Section = model.FinalSection ?? model.Blocks.LastOrDefault()?.Section;
                model.Blocks.AddRange(note.Blocks);
            }
        }
        var tables = model.Tables.ToArray();
        var paragraphs = model.Paragraphs.Distinct().ToArray();
        if (format is not DocumentFormat.Html and not DocumentFormat.Docx)
        {
            if (format is not DocumentFormat.Pdf and not DocumentFormat.Xps) LossAt(tables.Where(t => t.Rows.SelectMany(r => r).Any(c => c.Content.OfType<TableBlock>().Any())).Select(t => t.Location),
                "Table.NestedFlattened", "Nested tables retain their text in reading order as cell paragraphs in this output profile.");
            if (format is not DocumentFormat.Pdf and not DocumentFormat.Xps) LossAt(tables.Where(t => t.Rows.SelectMany(r => r).Any(c => c.Background != null || c.Borders.Count > 0 || c.VerticalAlignment != "top")).Select(t => t.Location),
                "Table.Decoration", "This output profile omits cell shading, borders and vertical alignment while retaining cell content.");
            if (format != DocumentFormat.Rtf) LossAt(paragraphs.Where(p => p.Runs.Any(r => r.Appearance.HasValues
                && (format is not DocumentFormat.Pdf and not DocumentFormat.Xps || r.Appearance.Underline || r.Appearance.Strike || r.Appearance.Background != null || r.Appearance.VerticalAlignment != null))).Select(p => p.Location),
                "Formatting.RunAppearance", "This output profile does not retain requested font families or additional text decoration.");
        }
        if (format is not DocumentFormat.Docx and not DocumentFormat.Pdf and not DocumentFormat.Xps and not DocumentFormat.Html)
        {
            LossAt(paragraphs.Where(p => p.Runs.Any(r => r.Image?.Transformed == true)).Select(p => p.Location), "Image.TransformOutput", "This destination retains original image pixels without crop, rotation or mirroring.");
            LossAt(paragraphs.Where(p => p.Runs.Any(r => r.Image?.Anchor != null)).Select(p => p.Location), "Image.AnchorApproximated", "This destination uses inline placement for anchored images.");
            foreach (ParagraphBlock paragraph in paragraphs)
                for (int i = 0; i < paragraph.Runs.Count; i++)
                    if (paragraph.Runs[i].Box is PositionedBox box)
                    {
                        context.Loss("Shape.TextBoxFlattened", "This destination retains text-box paragraphs as inline text.");
                        paragraph.Runs.RemoveAt(i);
                        List<TextRun> flattened = FlattenBox(box);
                        paragraph.Runs.InsertRange(i, flattened);
                        i += flattened.Count - 1;
                    }
        }
        if (format is not DocumentFormat.Docx and not DocumentFormat.Pdf and not DocumentFormat.Xps)
            foreach (ParagraphBlock paragraph in model.Paragraphs.Distinct())
                for (int i = 0; i < paragraph.Runs.Count; i++)
                {
                    TextRun run = paragraph.Runs[i];
                    if (run.Text.IndexOf('\f') < 0 && run.Text.IndexOf('\v') < 0) continue;
                    using var location = context.At(paragraph.Location);
                    context.Loss("Layout.ExplicitBreak", "Page and column breaks become line breaks in this output profile.");
                    paragraph.Runs[i] = run.WithText(run.Text.Replace('\f', '\n').Replace('\v', '\n'));
                }
        if (format is DocumentFormat.Doc or DocumentFormat.Epub or DocumentFormat.Mobi or DocumentFormat.Azw3)
            LossAt(paragraphs.Where(p => p.List?.Definition?.Any(l => l.Format is not "decimal" and not "bullet" || l.Legal || l.Pattern != "%" + (System.Array.IndexOf(p.List.Definition, l) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "." && l.Format != "bullet" || l.RestartAfter != System.Array.IndexOf(p.List.Definition, l) - 1 || l.Layout.HasValues) == true).Select(p => p.Location),
                "List.AdvancedOutput", "This destination uses decimal/round-bullet list definitions without the richer label profile.");
        if (format == DocumentFormat.Doc)
        {
            LossAt(paragraphs.Where(p => p.Runs.Any(r => r.Bookmark != null)).Select(p => p.Location), "Bookmark.Output", "Binary DOC output retains bookmark text without named bookmark ranges.");
            LossAt(tables.Where(t => t.WidthPercent.HasValue || t.AutoFit || t.Rows.SelectMany(r => r).Any(c => c.WidthPercent.HasValue)).Select(t => t.Location), "Table.RelativeWidthOutput", "Binary DOC retains the absolute table profile; percentage widths and autofit are not retained.");
            foreach (PageSettings section in model.Blocks.Select(b => b.Section).Concat(new[] { model.FinalSection }).Where(s => s != null).Distinct().Cast<PageSettings>())
                if (section.BreakType != "nextPage" || section.Columns > 1 || section.PageNumberStart.HasValue || section.PageNumberFormat != "decimal")
                    context.Loss("Layout.DocSections", "Binary DOC output retains next-page sections without columns or displayed-page-number restarts.");
        }
        if (format == DocumentFormat.Doc)
            LossAt(paragraphs.Where(p => p.Layout.Tabs.Count > 0).Select(p => p.Location), "Layout.DocTabStops", "Binary DOC retains tab characters with default stops; custom tab definitions are not retained.");
        if (format is not DocumentFormat.Doc and not DocumentFormat.Docx and not DocumentFormat.Pdf and not DocumentFormat.Html and not DocumentFormat.Xps)
        {
            LossAt(model.Blocks.Select(b => b.Section).Concat(new[] { model.FinalSection }).Where(s => s != null).Distinct().SelectMany(s => s!.Stories.Values).Distinct().SelectMany(b => b).Select(b => b.Location), "HeaderFooter.Output", "This output profile does not preserve header/footer stories.");
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
            if (format is not DocumentFormat.Html and not DocumentFormat.Rtf)
                LossAt(paragraphs.Where(p => p.Layout.HasValues).Select(p => p.Location), "Layout.Paragraph", "Paragraph spacing, indentation and pagination controls are not preserved by this output profile.");
            LossAt(tables.Where(t => t.NonSplittingRows.Count > 0).Select(t => t.Location), "Table.Pagination", "Non-splitting table-row controls are not preserved by this reflowable output profile.");
        }
        if (format is DocumentFormat.Text or DocumentFormat.Markdown)
        {
            LossAt(paragraphs.Where(p => p.Runs.Any(r => r.FontSize.HasValue || r.Color != null)).Select(p => p.Location), format == DocumentFormat.Text ? "Text.Formatting" : "Markdown.RunAppearance", "Font sizes and colors are not represented in this text format.");
            LossAt(tables.Where(t => t.LeftIndent != 0 || t.Width.HasValue || t.ColumnWidths.Any(w => w > 0) || t.Rows.SelectMany(r => r).Any(c => c.CellWidth.HasValue || c.Padding != null)).Select(t => t.Location), "Table.Geometry", "This text format cannot retain table widths and cell padding.");
            LossAt(paragraphs.Where(p => p.List != null).Select(p => p.Location), "List.Structure", "Editable list numbering becomes literal labels in this text profile.");
        }
        if (format is not DocumentFormat.Doc and not DocumentFormat.Docx and not DocumentFormat.Pdf and not DocumentFormat.Epub and not DocumentFormat.Html and not DocumentFormat.Xps and not DocumentFormat.Rtf)
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
            if (!context.Typography.Enabled) LossAt(paragraphs.Where(p => p.RightToLeft == true).Select(p => p.Location), "Layout.Direction", "Right-to-left paragraphs require the optional typography profile.");
            LossAt(paragraphs.Where(p => p.IsCodeBlock || p.Runs.Any(r => r.Code)).Select(p => p.Location), "Layout.CodeFont", "Code content uses the embedded proportional font.");
            if (!context.Typography.Enabled) LossAt(paragraphs.Where(p => p.Runs.Any(r => r.Text.Any(c => c >= '\u0590' && c <= '\u0dff' || char.IsSurrogate(c)))).Select(p => p.Location),
                "Layout.Shaping", "Unsupported complex-script shaping and supplementary characters use visible fallback characters.");
            if (format == DocumentFormat.Xps)
                LossAt(paragraphs.Where(p => p.Runs.Any(r => r.Link != null || r.FieldInstruction != null && r.FieldInstruction.Contains("\\h"))).Select(p => p.Location), "Layout.Hyperlink", "This XPS profile retains hyperlink text without an interactive target.");
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

    internal static List<TextRun> FlattenBox(PositionedBox box)
    {
        var result = new List<TextRun>();
        Append(box.Blocks);
        return result;

        void Append(IEnumerable<DocumentBlock> blocks)
        {
            foreach (DocumentBlock block in blocks)
            {
                if (block is TableBlock table) Append(table.Rows.SelectMany(r => r).SelectMany(c => c.Blocks));
                else if (block is ParagraphBlock paragraph)
                {
                    if (result.Count > 0) result.Add(new TextRun("\n"));
                    foreach (TextRun run in paragraph.Runs)
                        if (run.Box != null) Append(run.Box.Blocks);
                        else result.Add(run);
                }
            }
        }
    }

}
