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
    private sealed class FieldCapture
    {
        internal StringBuilder Instruction { get; } = new StringBuilder();
        internal List<TextRun> Result { get; } = new List<TextRun>();
        internal bool Separated { get; set; }
    }

    private void Emit(List<TextRun> runs, TextRun run)
    {
        if (fields.Count == 0) runs.Add(run);
        else if (fields.Peek().Separated) fields.Peek().Result.Add(run);
    }

    private void EmitField(List<TextRun> runs, string instruction, List<TextRun> result)
    {
        if (!DocumentFields.Supported(instruction))
        {
            context.Report("Field.UnsupportedUsedCachedResult", "An unsupported field uses its cached result text.");
            // Retain every cached run, including pictures and run formatting. A single
            // flattened field would silently discard pictures in cached TOC/IF results.
            foreach (TextRun run in result) Emit(runs, run);
            return;
        }
        if (DocumentFields.Kind(instruction) == "HYPERLINK")
        {
            Match match = Regex.Match(instruction, "^\\s*HYPERLINK\\s+(?:\"([^\"]*)\"|(\\S+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            string? link = match.Success ? context.SafeLink(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value) : null;
            foreach (TextRun run in result)
                Emit(runs, new TextRun(run.Text, run.Bold, run.Italic, run.Code, link, run.FontSize, run.Color, run.Image, run.Appearance));
            return;
        }
        TextRun first = result.FirstOrDefault() ?? new TextRun("");
        Emit(runs, new TextRun(string.Concat(result.Select(r => r.Text)), first.Bold, first.Italic, first.Code, first.Link,
            first.FontSize, first.Color, appearance: first.Appearance, fieldInstruction: instruction));
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
