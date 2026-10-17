using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
    }

    private void Emit(List<TextRun> runs, TextRun run)
    {
        if (fields.Count == 0) runs.Add(run);
        else if (fields.All(f => f.Separated)) fields.Peek().Result.Add((runs, run));
    }

    private void EmitField(List<TextRun> runs, string instruction, List<(List<TextRun> Target, TextRun Run)> result)
    {
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
                Emit(cached.Target, new TextRun(run.Text, run.Bold, run.Italic, run.Code, link, run.FontSize, run.Color, run.Image, run.Appearance));
            }
            return;
        }
        bool toc = DocumentFields.Kind(instruction) == "TOC" && DocumentReferences.SupportedToc(instruction)
            && context.Destination is DocumentFormat.Pdf or DocumentFormat.Xps
            && result.All(r => r.Run.Image == null && r.Run.Box == null && r.Run.Note == null
                && (r.Run.FieldInstruction == null || DocumentFields.Kind(r.Run.FieldInstruction) == "PAGEREF"));
        bool multipleParagraphs = result.Select(r => r.Target).Distinct().Skip(1).Any();
        TextRun first = result.Select(r => r.Run).FirstOrDefault() ?? new TextRun("");
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
        Emit(result.Count == 0 ? runs : result[0].Target, new TextRun(string.Concat(result.Select(r => r.Run.Text)), first.Bold, first.Italic, first.Code, first.Link,
            first.FontSize, first.Color, appearance: first.Appearance, fieldInstruction: instruction));
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

    private void FieldBoundary(string? kind, List<TextRun> runs)
    {
        if (kind == "begin")
        {
            if (fields.Count >= context.Options.MaximumNestingDepth) throw new InvalidDataException("Limit.NestingDepth: Fields exceed the nesting limit.");
            fields.Push(new FieldCapture());
        }
        else if (kind == "separate" && fields.Count > 0) fields.Peek().Separated = true;
        else if (kind == "end" && fields.Count > 0)
        {
            FieldCapture field = fields.Pop(); EmitField(runs, field.Instruction.ToString(), field.Result);
        }
        else throw new InvalidDataException("Docx.Field: Unbalanced field boundary.");
    }
}
