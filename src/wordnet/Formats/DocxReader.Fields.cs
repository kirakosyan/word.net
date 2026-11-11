using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxReader
{
    private readonly Stack<FieldCapture> fields = new Stack<FieldCapture>();
    private readonly HashSet<List<TextRun>> replacedTocParagraphs = new HashSet<List<TextRun>>();
    private sealed class FieldCapture
    {
        internal StringBuilder Instruction { get; } = new StringBuilder();
        internal List<(List<TextRun> Target, TextRun Run)> Result { get; } = new List<(List<TextRun>, TextRun)>();
        internal bool Separated { get; set; }
        internal bool NestedInstruction { get; set; }
        internal bool RetainTree { get; set; } = true;
        internal Dictionary<int, TextRun> Children { get; } = new Dictionary<int, TextRun>();
        internal XElement? InspectionBegin { get; set; }
        internal List<TextRun>? InspectionTarget { get; set; }
        internal bool InspectionNested { get; set; }
    }

    private void Emit(List<TextRun> runs, TextRun run)
    {
        if (fields.Count == 0) runs.Add(run);
        else if (fields.Peek().Separated) fields.Peek().Result.Add((runs, run));
    }

    private void EmitField(List<TextRun> runs, string instruction, List<(List<TextRun> Target, TextRun Run)> result, bool nestedInstruction = false, Dictionary<int, TextRun>? children = null)
    {
        if (nestedInstruction && (!ScalarFields.Candidate(instruction) || children == null || children.Count == 0))
        {
            context.Report("Field.UnsupportedUsedCachedResult", "A nested field instruction retains its cached runs without creating a live field.");
            foreach (var cached in result) Emit(cached.Target, cached.Run);
            return;
        }
        if (context.Editing && DocumentFields.Kind(instruction) == "MERGEFIELD")
        {
            if (result.Any(r => r.Run.Image != null || r.Run.Box != null || r.Run.Bookmark != null || r.Run.Note != null || r.Run.FieldInstruction != null || r.Run.Control != null)
                || result.Select(r => r.Target).Distinct().Skip(1).Any())
            {
                context.Loss("Template.FieldStructure", "A merge field with embedded structure or multiple result paragraphs retains its cached content.");
                foreach (var cached in result) Emit(cached.Target, cached.Run);
            }
            else
            {
                TextRun prototype = result.Select(r => r.Run).FirstOrDefault() ?? new TextRun(context, "");
                Emit(result.Count == 0 ? runs : result[0].Target, new TextRun(context, string.Concat(result.Select(r => r.Run.Text)), prototype.Bold, prototype.Italic,
                    prototype.Code, prototype.Link, prototype.FontSize, prototype.Color, appearance: prototype.Appearance, fieldInstruction: instruction)
                { FieldCache = result.Select(r => r.Run).ToList() });
            }
            return;
        }
        if (!DocumentFields.Supported(instruction))
        {
            context.Report("Field.UnsupportedUsedCachedResult", "An unsupported field uses its cached result text.");
            // Retain every cached run, including pictures and run formatting. A single
            // flattened field would silently discard pictures in cached TOC/IF results.
            foreach (var cached in result) Emit(cached.Target, cached.Run);
            return;
        }
        if (DocumentFields.Kind(instruction) == "HYPERLINK")
        {
            Match match = Regex.Match(instruction, "^\\s*HYPERLINK\\s+(?:\"([^\"]*)\"|(\\S+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            string? link = match.Success ? context.SafeLink(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value) : null;
            foreach (var cached in result)
            {
                TextRun run = cached.Run;
                Emit(cached.Target, new TextRun(context, run.Text, run.Bold, run.Italic, run.Code, link, run.FontSize, run.Color, run.Image, run.Appearance));
            }
            return;
        }
        bool toc = DocumentFields.Kind(instruction) == "TOC" && DocumentReferences.SupportedToc(instruction)
            && context.Destination is DocumentFormat.Pdf or DocumentFormat.Xps
            && result.All(r => r.Run.Image == null && r.Run.Box == null && r.Run.Note == null
                && (r.Run.FieldInstruction == null || DocumentFields.Kind(r.Run.FieldInstruction) == "PAGEREF"));
        bool multipleParagraphs = result.Select(r => r.Target).Distinct().Skip(1).Any();
        TextRun first = result.Select(r => r.Run).FirstOrDefault() ?? new TextRun(context, "");
        bool plain = result.All(r => r.Run.Image == null && r.Run.Box == null && r.Run.Note == null && r.Run.Bookmark == null && r.Run.FieldInstruction == null)
            && WordNet.Layout.RunCoalescer.Combine(result.Select(r => r.Run).ToList()).Count <= 1;
        if (!toc && (multipleParagraphs || !plain))
        {
            context.Report("Field.UnsupportedUsedCachedResult", "A field with structured or formatted cached content retains its original runs and paragraph structure.");
            foreach (var cached in result) Emit(cached.Target, cached.Run);
            return;
        }
        if (toc && multipleParagraphs)
            foreach (List<TextRun> target in result.Select(r => r.Target).Distinct().Skip(1)) replacedTocParagraphs.Add(target);
        Emit(result.Count == 0 ? runs : result[0].Target, new TextRun(context, string.Concat(result.Select(r => r.Run.Text)), first.Bold, first.Italic, first.Code, first.Link,
            first.FontSize, first.Color, appearance: first.Appearance, fieldInstruction: instruction)
        { FieldChildren = children?.Count > 0 ? children : null });
    }

    private void PruneTocParagraphs(List<DocumentBlock> blocks)
    {
        blocks.RemoveAll(b => b is ParagraphBlock p && p.Runs.Count == 0 && replacedTocParagraphs.Contains(p.Runs));
        foreach (DocumentBlock block in blocks)
            if (block is TableBlock table)
                foreach (TableCell cell in table.Rows.SelectMany(r => r))
                {
                    PruneTocParagraphs(cell.Content);
                    cell.Paragraphs.RemoveAll(p => p.Runs.Count == 0 && replacedTocParagraphs.Contains(p.Runs));
                }
            else if (block is ParagraphBlock paragraph)
                foreach (PositionedBox box in paragraph.Runs.Where(r => r.Box != null).Select(r => r.Box!)) PruneTocParagraphs(box.Blocks);
    }

    private void CaptureChild(string instruction, FieldCapture field)
    {
        FieldCapture parent = fields.Peek(); parent.NestedInstruction = true;
        bool plain = field.Result.All(r => r.Run.Image == null && r.Run.Box == null && r.Run.Note == null && r.Run.FieldInstruction == null
                && r.Run.Bookmark == null && r.Run.Object == null && r.Run.Control == null && r.Run.Revision == null && r.Run.Comment == null)
            && field.Result.Select(r => r.Target).Distinct().Count() <= 1
            && WordNet.Layout.RunCoalescer.Combine(field.Result.Select(r => r.Run).ToList()).Count <= 1;
        if (!plain || !field.RetainTree || !ScalarFields.Candidate(instruction)) { parent.RetainTree = false; return; }
        int offset = parent.Instruction.Length;
        parent.Instruction.Append('\ufffc');
        TextRun first = field.Result.Select(r => r.Run).FirstOrDefault() ?? new TextRun(context, "");
        parent.Children.Add(offset, new TextRun(context, string.Concat(field.Result.Select(r => r.Run.Text)), first.Bold, first.Italic, first.Code, first.Link,
            first.FontSize, first.Color, appearance: first.Appearance.Copy(), fieldInstruction: instruction)
        { FieldChildren = field.Children });
    }

    private void FieldBoundary(XElement marker, List<TextRun> runs)
    {
        string? kind = (string?)marker.Attribute(W + "fldCharType");
        if (kind == "begin")
        {
            if (fields.Count >= context.Options.MaximumNestingDepth) throw new InvalidDataException("Limit.NestingDepth: Fields exceed the nesting limit.");
            if (context.CaptureTemplateSources && fields.Count != 0) fields.Peek().InspectionNested = true;
            var capture = new FieldCapture();
            if (context.CaptureTemplateSources && fields.Count == 0 && marker.Attribute(W + "dirty") != null && DirectMarker(marker, allowDirty: true))
            { capture.InspectionBegin = marker; capture.InspectionTarget = runs; }
            fields.Push(capture);
        }
        else if (kind == "separate" && fields.Count > 0)
        {
            if (fields.Peek().Separated) context.Report("Field.DuplicateSeparator", "A repeated field separator is ignored without changing the result destination.");
            fields.Peek().Separated = true;
        }
        else if (kind == "end" && fields.Count > 0)
        {
            FieldCapture field = fields.Pop();
            if (fields.Count > 0 && !fields.Peek().Separated)
            {
                CaptureChild(field.Instruction.ToString(), field);
            }
            else
            {
                int first = runs.Count;
                EmitField(runs, field.Instruction.ToString(), field.Result, field.NestedInstruction, field.RetainTree ? field.Children : null);
                if (field.InspectionBegin != null && fields.Count == 0 && !field.InspectionNested && !field.NestedInstruction && field.Children.Count == 0
                    && ReferenceEquals(field.InspectionTarget, runs) && ReferenceEquals(field.InspectionBegin.Parent?.Parent, marker.Parent?.Parent)
                    && DirectMarker(marker, allowDirty: false) && runs.Count == first + 1 && runs[first].FieldInstruction != null)
                    runs[first].TemplateDirtyFieldElement = field.InspectionBegin;
            }
        }
        else throw new InvalidDataException("Docx.Field: Unbalanced field boundary.");

        static bool DirectMarker(XElement element, bool allowDirty)
        {
            XElement? run = element.Parent;
            if (element.HasElements || run?.Name != W + "r" || run.Parent?.Name != W + "p") return false;
            XElement[] contents = run.Elements().Take(3).ToArray();
            if (contents.Length is < 1 or > 2 || contents.Count(item => item.Name == W + "fldChar") != 1
                || contents.Any(item => item.Name != W + "fldChar" && item.Name != W + "rPr")) return false;
            XAttribute[] attributes = element.Attributes().Take(4).ToArray();
            return attributes.Length <= 3 && attributes.All(attribute => attribute.IsNamespaceDeclaration
                || attribute.Name == W + "fldCharType" || allowDirty && attribute.Name == W + "dirty");
        }
    }
}
