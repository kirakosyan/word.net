using System;
using System.IO;
using System.Linq;
using System.Text;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Formats;

internal static class TextFormat
{
    internal static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    internal static string Decode(byte[] bytes, ConversionContext context)
    {
        int offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
        try
        {
            string text = Utf8.GetString(bytes, offset, bytes.Length - offset);
            context.CheckCharacters(text.Length);
            context.CheckCancellation();
            return text.Replace("\r\n", "\n").Replace('\r', '\n');
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Text.Encoding: Input must be valid UTF-8, with an optional UTF-8 BOM.", exception);
        }
    }

    internal static DocumentModel Read(string text, ConversionContext context)
    {
        var document = new DocumentModel();
        foreach (string line in text.Split('\n'))
        {
            context.CheckCancellation();
            var paragraph = new ParagraphBlock();
            paragraph.Runs.Add(new TextRun(line));
            document.Blocks.Add(paragraph);
        }

        return document;
    }

    internal static string Write(DocumentModel document, ConversionContext context)
    {
        var output = new StringBuilder();
        var counter = new ListCounter();
        bool firstBlock = true;
        foreach (DocumentBlock block in document.Blocks)
        {
            context.CheckCancellation();
            if (!firstBlock)
            {
                output.Append('\n');
            }

            firstBlock = false;

            if (block is ParagraphBlock paragraph)
            {
                CheckFormatting(paragraph, context);
                output.Append(Label(paragraph) + paragraph.Text);
            }
            else if (block is TableBlock table)
            {
                context.Loss("Text.TableStructure", "Table structure becomes tab-separated rows in plain text.");
                for (int row = 0; row < table.Rows.Count; row++)
                {
                    if (row > 0)
                    {
                        output.Append('\n');
                    }

                    output.Append(string.Join("\t", table.Rows[row].Select(cell => Label(cell) + cell.Text)));
                }
            }
        }

        return output.ToString();
        string Label(ParagraphBlock p) => p.List == null ? "" : new string(' ', p.List.Level * 2) + counter.Label(p.List) + " ";
    }

    private static void CheckFormatting(ParagraphBlock paragraph, ConversionContext context)
    {
        if (paragraph.HeadingLevel != 0 || paragraph.IsCodeBlock || paragraph.Alignment != ParagraphAlignment.Left
            || paragraph.Runs.Any(run => run.Bold || run.Italic || run.Code || run.Link != null))
        {
            context.Loss("Text.Formatting", "Plain text cannot preserve headings, alignment, emphasis, code styling or hyperlink targets.");
        }
    }
}
