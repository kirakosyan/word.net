using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxReader
{
    private readonly Dictionary<XElement, TemplateControl> rowControls = new Dictionary<XElement, TemplateControl>();
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
        if (element.Element(W + "sdtPr")?.Elements().Any(p => p.Name != W + "tag" && p.Name != W + "id" && p.Name != W + "alias") == true)
            context.Loss("Template.ControlProperties", "Control-specific validation, locking, data bindings and appearance are outside the editable tagged-control profile.");
        if (tag.Length > 256) throw new InvalidDataException("Template.TagLength: A control tag exceeds the supported length.");
        return new TemplateControl(tag);
    }
    private bool ReadBlockControl(XElement element, List<DocumentBlock> blocks)
    {
        if (!context.Editing || element.Name != W + "sdt") return false;
        if (context.PreserveControls) return ReadTypedBlock(element, blocks);
        int start = blocks.Count;
        if (element.Element(W + "sdtContent") is XElement content) ReadBlocks(content, blocks);
        TemplateControl? control = ReadControl(element);
        if (control != null)
        {
            if (control.Tag.StartsWith("repeat:", StringComparison.Ordinal)
                && (blocks.Count == start || blocks.Skip(start).Any(b => b is not ParagraphBlock)
                || contentHasSection(element))) control.Executable = false;
            foreach (DocumentBlock block in blocks.Skip(start))
            {
                if (block.Control != null)
                {
                    if (control.Tag.StartsWith("repeat:", StringComparison.Ordinal) || block.Control.Tag.StartsWith("repeat:", StringComparison.Ordinal))
                    { block.Control.Executable = false; control.Executable = false; }
                    context.Loss("Template.NestedBlockControls", "Nested controls sharing a block are outside the editable template profile.");
                }
                else block.Control = control;
            }
        }
        return true;

        static bool contentHasSection(XElement wrapper) => wrapper.Descendants(W + "sectPr").Any();
    }
    private bool ReadInlineControl(XElement element, List<TextRun> runs, TextStyle inherited, string? link)
    {
        if (!context.Editing || element.Name != W + "sdt") return false;
        if (context.PreserveControls) return ReadTypedInline(element, runs, inherited, link);
        if (fields.Count > 0)
        {
            if (ReadControl(element) != null) context.Loss("Template.FieldStructure", "Controls inside field results retain cached content without template execution.");
            if (element.Element(W + "sdtContent") is XElement fieldContent) ReadInlines(fieldContent, runs, inherited, link);
            return true;
        }
        var cached = new List<TextRun>();
        if (element.Element(W + "sdtContent") is XElement content) ReadInlines(content, cached, inherited, link);
        TemplateControl? control = ReadControl(element);
        if (cached.Count == 0) cached.Add(new TextRun(""));
        foreach (TextRun run in cached)
        {
            if (run.Control != null && control != null) context.Loss("Template.NestedInlineControls", "Nested controls sharing inline text are outside the editable template profile.");
            Emit(runs, new TextRun(run.Text, run.Bold, run.Italic, run.Code, run.Link, run.FontSize, run.Color, run.Image, run.Appearance, run.FieldInstruction, run.Note, run.Box, run.Bookmark, run.Control ?? control, run.Object).RetainMetadata(run));
        }
        return true;
    }
}
