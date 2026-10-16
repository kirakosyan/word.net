using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Formats;

internal static partial class DocFormat
{
    private sealed class BinaryFields
    {
        private readonly ConversionContext context;
        private readonly Stack<BinaryField> stack = new Stack<BinaryField>();
        private sealed class BinaryField
        {
            internal StringBuilder Instruction { get; } = new StringBuilder();
            internal List<(ParagraphBlock Target, TextRun Run)> Runs { get; } = new List<(ParagraphBlock, TextRun)>();
            internal bool Separated { get; set; }
        }
        internal BinaryFields(ConversionContext context) => this.context = context;
        internal int Count => stack.Count;
        internal bool InInstruction => stack.Any(f => !f.Separated);

        internal void AddRun(ParagraphBlock paragraph, TextRun run)
        {
            if (stack.Count == 0) paragraph.Runs.Add(run);
            else if (!InInstruction) stack.Peek().Runs.Add((paragraph, run));
        }

        internal void Text(ParagraphBlock paragraph, string value, Properties properties)
        {
            var plain = new StringBuilder();
            foreach (char c in value)
            {
                if (c == '\u0013')
                {
                    Flush();
                    if (stack.Count >= context.Options.MaximumNestingDepth) throw new InvalidDataException("Doc.Fields: Excessive field nesting.");
                    stack.Push(new BinaryField());
                }
                else if (c == '\u0014')
                {
                    Flush(); if (stack.Count == 0) throw new InvalidDataException("Doc.Fields: Unexpected field separator.");
                    stack.Peek().Separated = true;
                }
                else if (c == '\u0015')
                {
                    Flush(); if (stack.Count == 0) throw new InvalidDataException("Doc.Fields: Unexpected field end.");
                    BinaryField field = stack.Pop(); string instruction = field.Instruction.ToString();
                    if (!DocumentFields.Supported(instruction))
                    {
                        context.Report("Field.UnsupportedUsedCachedResult", "An unsupported binary Word field uses its cached result.");
                        foreach (var cached in field.Runs) AddRun(cached.Target, cached.Run);
                        continue;
                    }
                    TextRun first = field.Runs.Select(r => r.Run).FirstOrDefault() ?? new TextRun("");
                    string result = string.Concat(field.Runs.Select(r => r.Run.Text));
                    if (DocumentFields.Kind(instruction) == "HYPERLINK")
                    {
                        Match match = Regex.Match(instruction, "^\\s*HYPERLINK\\s+(?:\"([^\"]*)\"|(\\S+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                        string? link = match.Success ? context.SafeLink(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value) : null;
                        foreach (var cached in field.Runs)
                        {
                            TextRun run = cached.Run;
                            AddRun(cached.Target, new TextRun(run.Text, run.Bold, run.Italic, run.Code, link,
                                run.FontSize, run.Color, run.Image, run.Appearance));
                        }
                    }
                    else if (field.Runs.Select(r => r.Target).Distinct().Skip(1).Any())
                    {
                        context.Report("Field.UnsupportedUsedCachedResult", "A binary Word field spanning paragraphs retains its cached result in place.");
                        foreach (var cached in field.Runs) AddRun(cached.Target, cached.Run);
                    }
                    else AddRun(field.Runs.Count == 0 ? paragraph : field.Runs[0].Target,
                        new TextRun(result, first.Bold, first.Italic, fontSize: first.FontSize, color: first.Color, fieldInstruction: instruction));
                }
                else if (InInstruction) stack.Peek().Instruction.Append(c);
                else if (!properties.Hidden)
                {
                    if (c is '\v' or '\f') { plain.Append('\n'); if (c == '\f') context.Loss("Doc.PageBreak", "Explicit page breaks become line breaks."); }
                    else if (!char.IsControl(c) || c is '\n' or '\t') plain.Append(c);
                    else context.Loss("Doc.SpecialContent", "Unsupported object and special-content markers are omitted.");
                }
            }
            Flush();
            void Flush()
            {
                if (plain.Length == 0) return;
                AddRun(paragraph, new TextRun(plain.ToString(), properties.Bold, properties.Italic, fontSize: properties.FontSize, color: properties.Color)); plain.Clear();
            }
        }
    }
}
