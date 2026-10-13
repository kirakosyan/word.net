using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using WordNet.Internal;
using WordNet.Model;
using MarkdownCell = Markdig.Extensions.Tables.TableCell;
using MarkdownRow = Markdig.Extensions.Tables.TableRow;
using MarkdownTable = Markdig.Extensions.Tables.Table;
using ModelParagraph = WordNet.Model.ParagraphBlock;
using TableColumnAlign = Markdig.Extensions.Tables.TableColumnAlign;

namespace WordNet.Formats;

internal static class MarkdownFormat
{
    internal static DocumentModel Read(string text, ConversionContext context)
    {
        var builder = new MarkdownPipelineBuilder { MaximumNestingDepth = context.Options.MaximumNestingDepth };
        MarkdownPipeline pipeline = builder.UsePipeTables().Build();
        MarkdownDocument syntax = Markdown.Parse(text, pipeline);
        context.CheckCancellation();
        var document = new DocumentModel();
        ReadBlocks(syntax, document.Blocks, context);
        return document;
    }

    private static void ReadBlocks(ContainerBlock source, List<DocumentBlock> blocks, ConversionContext context, int listDepth = 0)
    {
        foreach (Block block in source)
        {
            context.CheckCancellation();
            switch (block)
            {
                case HeadingBlock heading:
                    blocks.Add(ReadParagraph(heading, context, heading.Level));
                    break;
                case Markdig.Syntax.ParagraphBlock paragraph:
                    blocks.Add(ReadParagraph(paragraph, context));
                    break;
                case CodeBlock code:
                    var codeParagraph = new ModelParagraph { IsCodeBlock = true };
                    codeParagraph.Runs.Add(new TextRun(code.Lines.ToString()));
                    blocks.Add(codeParagraph);
                    if (code is FencedCodeBlock fenced && !string.IsNullOrEmpty(fenced.Info))
                    {
                        context.Loss("Markdown.CodeLanguage", "Code block language labels are not retained in DOCX.");
                    }

                    break;
                case MarkdownTable table:
                    blocks.Add(ReadTable(table, context));
                    break;
                case ListBlock list:
                    if (listDepth > 8) throw new NotSupportedException("Markdown.ListDepth: Lists support up to nine levels.");
                    int number = int.TryParse(list.OrderedStart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int start) ? start : 1;
                    if (number < 1) throw new NotSupportedException("Markdown.ListStart: List numbering must start at one or greater.");
                    string id = "markdown-" + context.NextListId();
                    foreach (ListItemBlock item in list.Cast<ListItemBlock>())
                    {
                        var itemBlocks = new List<DocumentBlock>();
                        ReadBlocks(item, itemBlocks, context, listDepth + 1);
                        if (itemBlocks.FirstOrDefault() is ModelParagraph first)
                        {
                            first.List = new ListItem { Id = id, Level = listDepth, Ordered = list.IsOrdered, Start = number };
                        }
                        else context.Loss("Markdown.ListItem", "A list item without an initial paragraph cannot retain its list marker.");

                        blocks.AddRange(itemBlocks);
                    }

                    break;
                case ContainerBlock container:
                    context.Loss("Markdown.BlockStructure", "An unsupported container, such as a block quote, is flattened into its content.");
                    ReadBlocks(container, blocks, context, listDepth);
                    break;
                case LeafBlock leaf:
                    context.Loss("Markdown.UnsupportedBlock", "Unsupported Markdown block syntax is retained as literal text.");
                    var literal = new ModelParagraph();
                    literal.Runs.Add(new TextRun(leaf.Lines.ToString()));
                    blocks.Add(literal);
                    break;
                default:
                    context.Loss("Markdown.UnsupportedBlock", "Unsupported Markdown block structure could not be preserved.");
                    break;
            }
        }
    }

    private static ModelParagraph ReadParagraph(LeafBlock source, ConversionContext context, int heading = 0)
    {
        var paragraph = new ModelParagraph { HeadingLevel = heading };
        if (source.Inline != null)
        {
            ReadInlines(source.Inline, paragraph.Runs, context);
        }

        return paragraph;
    }

    private static void ReadInlines(ContainerInline source, List<TextRun> runs, ConversionContext context,
        bool bold = false, bool italic = false, string? link = null)
    {
        foreach (Inline inline in source)
        {
            context.CheckCancellation();
            switch (inline)
            {
                case LiteralInline literal:
                    runs.Add(new TextRun(literal.Content.ToString(), bold, italic, link: link));
                    break;
                case LineBreakInline line:
                    runs.Add(new TextRun(line.IsHard ? "\n" : " ", bold, italic, link: link));
                    break;
                case EmphasisInline emphasis:
                    ReadInlines(emphasis, runs, context, bold || emphasis.DelimiterCount >= 2,
                        italic || emphasis.DelimiterCount == 1 || emphasis.DelimiterCount == 3, link);
                    break;
                case CodeInline code:
                    runs.Add(new TextRun(code.Content, bold, italic, code: true, link: link));
                    break;
                case LinkInline hyperlink:
                    if (hyperlink.IsImage)
                    {
                        context.Loss("Image.Unsupported", "Images are not imported yet; their alternative text is retained without fetching resources.");
                        ReadInlines(hyperlink, runs, context, bold, italic, link);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(hyperlink.Title))
                        {
                            context.Loss("Markdown.LinkTitle", "Hyperlink titles are not retained.");
                        }

                        ReadInlines(hyperlink, runs, context, bold, italic, context.SafeLink(hyperlink.Url));
                    }

                    break;
                case AutolinkInline auto:
                    runs.Add(new TextRun(auto.Url, bold, italic, link: context.SafeLink(auto.IsEmail ? "mailto:" + auto.Url : auto.Url)));
                    break;
                case HtmlInline html:
                    context.Loss("Markdown.Html", "Inline HTML is retained as literal text, without rendering or executing it.");
                    runs.Add(new TextRun(html.Tag, bold, italic));
                    break;
                default:
                    context.Loss("Markdown.UnsupportedInline", "Unsupported inline formatting is flattened where text is available.");
                    if (inline is ContainerInline container)
                    {
                        ReadInlines(container, runs, context, bold, italic, link);
                    }

                    break;
            }
        }
    }

    private static TableBlock ReadTable(MarkdownTable source, ConversionContext context)
    {
        var table = new TableBlock();
        foreach (MarkdownRow row in source.Cast<MarkdownRow>())
        {
            table.HasHeaderRow |= row.IsHeader;
            var cells = new List<ModelParagraph>();
            foreach (MarkdownCell cell in row.Cast<MarkdownCell>())
            {
                if (cell.Count != 1 || !(cell[0] is LeafBlock leaf))
                {
                    context.Loss("Markdown.TableCell", "Complex Markdown table cells are not supported.");
                    cells.Add(new ModelParagraph());
                    continue;
                }

                ModelParagraph paragraph = ReadParagraph(leaf, context);
                if (cells.Count < source.ColumnDefinitions.Count)
                    paragraph.Alignment = source.ColumnDefinitions[cells.Count].Alignment switch
                    {
                        TableColumnAlign.Center => ParagraphAlignment.Center,
                        TableColumnAlign.Right => ParagraphAlignment.Right,
                        _ => ParagraphAlignment.Left,
                    };
                cells.Add(paragraph);
            }

            table.Rows.Add(cells);
        }

        return table;
    }

    internal static string Write(DocumentModel document, ConversionContext context)
    {
        var blocks = new List<string>();
        var counter = new ListCounter();
        foreach (DocumentBlock block in document.Blocks)
        {
            context.CheckCancellation();
            if (block is ModelParagraph paragraph)
            {
                if (paragraph.Alignment != ParagraphAlignment.Left)
                    context.Loss("Markdown.Alignment", "Markdown prose cannot preserve paragraph alignment.");
                if (paragraph.IsCodeBlock)
                {
                    string fence = new string('`', Math.Max(3, LongestBacktickRun(paragraph.Text) + 1));
                    blocks.Add(fence + "\n" + paragraph.Text + "\n" + fence);
                }
                else
                {
                    if (paragraph.Runs.Count == 0 || paragraph.Text.Length == 0)
                    {
                        context.Loss("Markdown.EmptyParagraph", "Markdown does not preserve explicit empty Word paragraphs.");
                    }

                    if (paragraph.Text.Length > 0 && (char.IsWhiteSpace(paragraph.Text[0]) || char.IsWhiteSpace(paragraph.Text[paragraph.Text.Length - 1])))
                    {
                        context.Loss("Markdown.ParagraphWhitespace", "Leading or trailing paragraph whitespace may be normalized by Markdown readers.");
                    }

                    string label = paragraph.List == null ? "" : new string(' ', paragraph.List.Level * 4) + (paragraph.List.Ordered ? counter.Label(paragraph.List) : "-") + " ";
                    blocks.Add(label + (paragraph.HeadingLevel == 0 ? "" : new string('#', paragraph.HeadingLevel) + " ") + WriteRuns(paragraph, context));
                }
            }
            else if (block is TableBlock table)
            {
                blocks.Add(WriteTable(table, context, counter));
            }
        }

        return string.Join("\n\n", blocks) + (blocks.Count == 0 ? "" : "\n");
    }

    private static string WriteRuns(ModelParagraph paragraph, ConversionContext context)
    {
        var output = new StringBuilder();
        foreach (TextRun run in MergeRuns(paragraph.Runs))
        {
            context.CheckCancellation();
            string content;
            if (run.Code)
            {
                if (run.Text.Contains('\n'))
                {
                    context.Loss("Markdown.CodeLineBreak", "Inline code line breaks become spaces in Markdown.");
                }

                string text = run.Text.Replace('\n', ' ');
                string fence = new string('`', LongestBacktickRun(text) + 1);
                bool padding = text.Length > 0 && !text.All(character => character == ' ')
                    && (text[0] == '`' || text[text.Length - 1] == '`' || (text[0] == ' ' && text[text.Length - 1] == ' '));
                content = fence + (padding ? " " : "") + text + (padding ? " " : "") + fence;
            }
            else
            {
                content = Escape(run.Text, context);
            }

            if (run.Bold || run.Italic)
            {
                string trimmed = content.Trim();
                if (trimmed.Length > 0)
                {
                    int leading = content.Length - content.TrimStart().Length;
                    string marker = run.Bold ? (run.Italic ? "***" : "**") : "*";
                    content = content.Substring(0, leading) + marker + trimmed + marker + content.Substring(leading + trimmed.Length);
                }
            }

            if (run.Link != null)
            {
                string target = run.Link.Replace(" ", "%20").Replace("<", "%3C").Replace(">", "%3E").Replace("\\", "%5C");
                content = "[" + content + "](<" + target + ">)";
            }

            output.Append(content);
        }

        return output.ToString();
    }

    private static IEnumerable<TextRun> MergeRuns(List<TextRun> runs)
    {
        TextRun? previous = null;
        var text = new StringBuilder();
        foreach (TextRun run in runs)
        {
            if (previous != null && (run.Bold != previous.Bold || run.Italic != previous.Italic
                || run.Code != previous.Code || !string.Equals(run.Link, previous.Link, StringComparison.Ordinal)))
            {
                yield return new TextRun(text.ToString(), previous.Bold, previous.Italic, previous.Code, previous.Link);
                text.Clear();
            }

            text.Append(run.Text);
            previous = run;
        }

        if (previous != null)
        {
            yield return new TextRun(text.ToString(), previous.Bold, previous.Italic, previous.Code, previous.Link);
        }
    }

    private static string WriteTable(TableBlock table, ConversionContext context, ListCounter counter)
    {
        if (table.Rows.Count == 0)
        {
            return "";
        }

        int columns = table.Rows.Max(row => row.Count);
        var rows = new List<string>();
        foreach (List<ModelParagraph> row in table.Rows)
        {
            var cells = new List<string>();
            foreach (ModelParagraph cell in row)
            {
                if (cell.Text.Contains('\n'))
                {
                    context.Loss("Markdown.TableLineBreak", "Line breaks in table cells become spaces.");
                }

                string label = cell.List == null ? "" : counter.Label(cell.List) + " ";
                cells.Add(EscapeTablePipes(label + WriteRuns(cell, context).Replace("  \n", " ").Replace('\n', ' ')));
            }

            while (cells.Count < columns)
            {
                cells.Add("");
            }

            rows.Add("| " + string.Join(" | ", cells) + " |");
        }

        if (!table.HasHeaderRow)
        {
            context.Loss("Markdown.TableHeader", "An empty header row is added because pipe tables require a header.");
            rows.Insert(0, "| " + string.Join(" | ", Enumerable.Repeat("", columns)) + " |");
        }

        var separators = new List<string>();
        for (int column = 0; column < columns; column++)
        {
            ParagraphAlignment[] alignments = table.Rows.Where(row => column < row.Count).Select(row => row[column].Alignment).Distinct().ToArray();
            if (alignments.Length > 1)
                context.Loss("Markdown.TableAlignment", "Different alignments within a table column become the first cell's alignment.");
            separators.Add(alignments[0] switch { ParagraphAlignment.Center => ":---:", ParagraphAlignment.Right => "---:", _ => "---" });
        }
        rows.Insert(1, "| " + string.Join(" | ", separators) + " |");
        return string.Join("\n", rows);
    }

    private static string EscapeTablePipes(string content)
    {
        var output = new StringBuilder();
        int backslashes = 0;
        foreach (char character in content)
        {
            if (character == '|' && backslashes % 2 == 0)
            {
                output.Append('\\');
            }

            output.Append(character);
            backslashes = character == '\\' ? backslashes + 1 : 0;
        }

        return output.ToString();
    }

    private static string Escape(string text, ConversionContext context)
    {
        var escaped = new StringBuilder();
        foreach (char character in text)
        {
            if (character == '\n')
            {
                escaped.Append("  \n");
            }
            else if (character == '\t')
            {
                context.Loss("Markdown.Tab", "Tabs become four spaces in Markdown prose.");
                escaped.Append("    ");
            }
            else
            {
                if ((character >= '!' && character <= '/') || (character >= ':' && character <= '@')
                    || (character >= '[' && character <= '`') || (character >= '{' && character <= '~'))
                {
                    escaped.Append('\\');
                }

                escaped.Append(character);
            }
        }

        return escaped.ToString();
    }

    private static int LongestBacktickRun(string text)
    {
        int longest = 0;
        int current = 0;
        foreach (char character in text)
        {
            current = character == '`' ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }

        return longest;
    }
}
