using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxReader
{
    private readonly Dictionary<XElement, TemplateControl> rowControls = new Dictionary<XElement, TemplateControl>();
    private readonly Dictionary<XElement, int> typedRowControls = new Dictionary<XElement, int>();
    private readonly Dictionary<XElement, int> typedCellControls = new Dictionary<XElement, int>();
    private void RegisterRowControl(XElement element, TemplateControl control)
    {
#if NET8_0_OR_GREATER
        if (rowControls.TryAdd(element, control)) return;
#else
        if (!rowControls.ContainsKey(element)) { rowControls.Add(element, control); return; }
#endif
        context.Loss("Template.NestedRowControls", "Nested controls sharing one table row are outside the editable template profile.");
    }
    private TemplateControl? ReadControl(XElement element)
    {
        string? tag = (string?)element.Element(W + "sdtPr")?.Element(W + "tag")?.Attribute(W + "val");
        if (tag == null || !(tag.StartsWith("merge:", StringComparison.Ordinal) || tag.StartsWith("image:", StringComparison.Ordinal)
            || tag.StartsWith("if:", StringComparison.Ordinal) || tag.StartsWith("repeat:", StringComparison.Ordinal))) return null;
        if (element.Element(W + "sdtPr")!.Elements().Where(p => p.Name == W + "tag" || p.Name == W + "id" || p.Name == W + "alias").GroupBy(p => p.Name).Any(g => g.Count() > 1))
            throw new InvalidDataException("Template.ControlId: Duplicate control identity or tag metadata is unsupported.");
        if (element.Element(W + "sdtPr")?.Elements().Any(p => p.Name != W + "tag" && p.Name != W + "id" && p.Name != W + "alias") == true)
            context.Loss("Template.ControlProperties", "Control-specific validation, locking, data bindings and appearance are outside the editable tagged-control profile.");
        if (tag.Length > 256) throw new InvalidDataException("Template.TagLength: A control tag exceeds the supported length.");
        var control = new TemplateControl(tag);
        if (element.Element(W + "sdtPr")?.Element(W + "id") is XElement id)
        {
            string value = DocxPackage.RequiredAttribute(id, W + "val");
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) throw new InvalidDataException("Template.ControlId: A control ID must be a signed 32-bit integer.");
            control.Id = parsed;
        }
        control.Alias = (string?)element.Element(W + "sdtPr")?.Element(W + "alias")?.Attribute(W + "val");
        if (control.Alias?.Length > 256) throw new InvalidDataException("Template.TagLength: A control alias exceeds the supported length.");
        return control;
    }
    private bool ReadBlockControl(XElement element, List<DocumentBlock> blocks)
    {
        if (!context.Editing || element.Name != W + "sdt") return false;
        if (context.PreserveControls) return ReadTypedBlock(element, blocks);
        int start = blocks.Count;
        if (element.Element(W + "sdtContent") is XElement content) ReadBlocks(content, blocks);
        TemplateControl? control = ReadControl(element);
        if (control == null && context.TemplateExchange)
            context.Loss("Template.UnsupportedControl", "Unadmitted content-control metadata is not preserved by this template exchange.");
        if (control != null)
        {
            bool hasTable = false;
            for (int index = start; index < blocks.Count; index++) { context.CheckCancellation(); if (blocks[index] is TableBlock) { hasTable = true; break; } }
            if (control.Tag.StartsWith("repeat:", StringComparison.Ordinal) && hasTable
                && element.Descendants(W + "sdt").Concat(new[] { element }).Any(n => !AdmittedMixedControl(n)))
            {
                control.Executable = false;
                context.Report("Template.InactiveControl", "Mixed regions with unknown or behavior-bearing controls cannot execute.");
            }
            if (control.Tag.StartsWith("repeat:", StringComparison.Ordinal)
                && (blocks.Count == start || blocks.Skip(start).OfType<TableBlock>().Any(t => t.Rows.SelectMany(r => r).SelectMany(c => c.Blocks).Any(b => b is TableBlock))
                || contentHasSection(element))) control.Executable = false;
            foreach (DocumentBlock block in blocks.Skip(start))
            {
                if (block.Control != null)
                {
                    if (control.Tag.StartsWith("repeat:", StringComparison.Ordinal) && block.Control.Tag.StartsWith("repeat:", StringComparison.Ordinal)
                        && control.Tag != block.Control.Tag && block.NestedControl == null && hasTable)
                    { block.NestedControl = block.Control; block.Control = control; continue; }
                    if (control.Tag.StartsWith("repeat:", StringComparison.Ordinal) || block.Control.Tag.StartsWith("repeat:", StringComparison.Ordinal))
                    { block.Control.Executable = false; control.Executable = false; if (block.NestedControl != null) block.NestedControl.Executable = false; }
                    context.Loss("Template.NestedBlockControls", "Nested controls sharing a block are outside the editable template profile.");
                }
                else block.Control = control;
            }
        }
        return true;

        static bool contentHasSection(XElement wrapper) => wrapper.Descendants(W + "sectPr").Any();
        static bool AdmittedMixedControl(XElement wrapper)
        {
            XElement? properties = wrapper.Element(W + "sdtPr");
            string? nestedTag = (string?)properties?.Element(W + "tag")?.Attribute(W + "val");
            return nestedTag != null && (nestedTag.StartsWith("merge:", StringComparison.Ordinal) || nestedTag.StartsWith("image:", StringComparison.Ordinal)
                || nestedTag.StartsWith("if:", StringComparison.Ordinal) || nestedTag.StartsWith("repeat:", StringComparison.Ordinal))
                && properties!.Elements().All(p => p.Name == W + "tag" || p.Name == W + "id" || p.Name == W + "alias");
        }
    }
    private bool ReadInlineControl(XElement element, List<TextRun> runs, TextStyle inherited, string? link)
    {
        if (!context.Editing || element.Name != W + "sdt") return false;
        if (context.PreserveControls) return ReadTypedInline(element, runs, inherited, link);
        if (fields.Count > 0)
        {
            if (ReadControl(element) != null) context.Loss("Template.FieldStructure", "Controls inside field results retain cached content without template execution.");
            else if (context.TemplateExchange) context.Loss("Template.UnsupportedControl", "Unadmitted content-control metadata is not preserved by this template exchange.");
            if (element.Element(W + "sdtContent") is XElement fieldContent) ReadInlines(fieldContent, runs, inherited, link);
            return true;
        }
        var cached = new List<TextRun>();
        if (element.Element(W + "sdtContent") is XElement content) ReadInlines(content, cached, inherited, link);
        TemplateControl? control = ReadControl(element);
        if (control == null && context.TemplateExchange)
            context.Loss("Template.UnsupportedControl", "Unadmitted content-control metadata is not preserved by this template exchange.");
        if (cached.Count == 0) cached.Add(new TextRun(""));
        foreach (TextRun run in cached)
        {
            if (run.Control != null && control != null) context.Loss("Template.NestedInlineControls", "Nested controls sharing inline text are outside the editable template profile.");
            Emit(runs, new TextRun(run.Text, run.Bold, run.Italic, run.Code, run.Link, run.FontSize, run.Color, run.Image, run.Appearance, run.FieldInstruction, run.Note, run.Box, run.Bookmark, run.Control ?? control, run.Object).RetainMetadata(run));
        }
        return true;
    }
}
