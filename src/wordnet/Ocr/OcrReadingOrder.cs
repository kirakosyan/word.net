using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using WordNet.Internal;

namespace WordNet.Ocr;

internal static class OcrReadingOrder
{
    private static readonly char[] Tab = { '\t' };
    // Tesseract's TSV word rectangles expose a gutter even when its line segmentation
    // joins two columns. Only split a wide, uninterrupted gutter with text on both sides.
    internal static string? Columns(string tsv, int pageWidth, int pageHeight, ConversionContext context)
    {
        var words = new List<Word>();
        using var reader = new StringReader(tsv);
        string? row;
        while ((row = reader.ReadLine()) != null)
        {
            context.CheckCancellation();
            string[] fields = row.Split(Tab, 12);
            if (fields.Length != 12 || fields[0] != "5" || string.IsNullOrWhiteSpace(fields[11])) continue;
            if (!Number(fields[6], out int x) || !Number(fields[7], out int y)
                || !Number(fields[8], out int width) || !Number(fields[9], out int height)
                || width <= 0 || height <= 0 || (long)x + width > pageWidth || (long)y + height > pageHeight)
                return null;
            words.Add(new Word(fields[1] + ":" + fields[2] + ":" + fields[3] + ":" + fields[4], fields[11], x, y, width, height));
            if (words.Count > context.Options.MaximumCharacters) throw new InvalidDataException("Limit.Characters: OCR word count exceeds the character limit.");
        }
        if (words.Count < 12) return null;
        int medianHeight = words.Select(w => w.Height).OrderBy(h => h).ElementAt(words.Count / 2);
        int minimumGap = Math.Max(pageWidth / 20, medianHeight * 4);
        int edge = 0, bestGap = minimumGap, split = -1;
        foreach (Word word in words.OrderBy(w => w.X))
        {
            int middle = (edge + word.X) / 2;
            if (edge > 0 && word.X - edge > bestGap && middle > pageWidth / 5 && middle < pageWidth * 4 / 5)
            { split = middle; bestGap = word.X - edge; }
            edge = Math.Max(edge, word.X + word.Width);
        }
        if (split < 0) return null;
        Word[] left = words.Where(w => w.X < split).ToArray(), right = words.Where(w => w.X >= split).ToArray();
        if (left.Select(w => w.Line).Distinct().Count() < 3 || right.Select(w => w.Line).Distinct().Count() < 3) return null;
        int overlap = Math.Min(left.Max(w => w.Y + w.Height), right.Max(w => w.Y + w.Height)) - Math.Max(left.Min(w => w.Y), right.Min(w => w.Y));
        if (overlap < Math.Max(left.Max(w => w.Y + w.Height) - left.Min(w => w.Y), right.Max(w => w.Y + w.Height) - right.Min(w => w.Y)) / 2) return null;
        string text = Read(left) + "\n\n" + Read(right);
        context.CheckCharacters(text.Length);
        return text;

        static bool Number(string value, out int number) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number);
        static string Read(IEnumerable<Word> column) => string.Join("\n", column.GroupBy(w => w.Line).OrderBy(line => line.Min(w => w.Y))
            .Select(line => string.Join(" ", line.OrderBy(w => w.X).Select(w => w.Text))));
    }

    private sealed class Word
    {
        internal Word(string line, string text, int x, int y, int width, int height)
        { Line = line; Text = text; X = x; Y = y; Width = width; Height = height; }
        internal string Line { get; }
        internal string Text { get; }
        internal int X { get; }
        internal int Y { get; }
        internal int Width { get; }
        internal int Height { get; }
    }
}
