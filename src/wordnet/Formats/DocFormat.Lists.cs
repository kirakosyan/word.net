using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Formats;

internal static partial class DocFormat
{
    // MS-DOC 2.9.147/149/150, PlfLst and PlfLfo (FIB slots 73 and 74).
    private static Dictionary<int, ListItem[]> ReadLists(byte[] word, byte[] table, ConversionContext context)
    {
        var result = new Dictionary<int, ListItem[]>();
        if (U16(word, 152) < 75 || I32(word, 158 + 73 * 8) == 0) return result;
        int offset = I32(word, 154 + 73 * 8), length = I32(word, 158 + 73 * 8);
        Bounds(table, offset, length); int count = U16(table, offset);
        if (length != 2 + count * 28) throw new InvalidDataException("Doc.List: Invalid list table length.");
        var definitions = new Dictionary<int, ListItem[]>(); int cursor = offset + length;
        for (int i = 0; i < count; i++)
        {
            context.CheckCancellation(); int record = offset + 2 + i * 28, id = I32(table, record);
            if (definitions.ContainsKey(id)) throw new InvalidDataException("Doc.List: Duplicate list identifier.");
            int levels = (table[record + 26] & 1) != 0 ? 1 : 9;
            var items = new ListItem[levels];
            for (int l = 0; l < levels; l++) items[l] = ReadLevel(table, ref cursor, l, context);
            definitions.Add(id, items);
        }
        int lfo = I32(word, 154 + 74 * 8), lfoLength = I32(word, 158 + 74 * 8);
        Bounds(table, lfo, lfoLength); int overrides = I32(table, lfo);
        if (overrides < 0 || overrides > (lfoLength - 4) / 20) throw new InvalidDataException("Doc.List: Invalid override count.");
        cursor = lfo + 4 + overrides * 16;
        for (int i = 0; i < overrides; i++)
        {
            int record = lfo + 4 + i * 16;
            if (!definitions.TryGetValue(I32(table, record), out ListItem[]? original)) throw new InvalidDataException("Doc.List: Missing list definition.");
            ListItem[] levels = original.Select(item => item.Copy()).ToArray();
            foreach (ListItem item in levels) item.Id = "doc-" + (i + 1).ToString(CultureInfo.InvariantCulture);
            Bounds(table, cursor, 4); cursor += 4;
            for (int j = 0; j < table[record + 12]; j++)
            {
                Bounds(table, cursor, 8); int start = I32(table, cursor), flags = table[cursor + 4], level = flags & 15; cursor += 8;
                if (level >= levels.Length) throw new InvalidDataException("Doc.List: Invalid override level.");
                if ((flags & 32) != 0)
                {
                    ListItem value = ReadLevel(table, ref cursor, level, context); value.Id = levels[level].Id; levels[level] = value;
                }
                else if ((flags & 16) != 0)
                {
                    if (start < 1 || start > 32767) throw new NotSupportedException("Doc.ListStart: Numbering must start between 1 and 32767.");
                    levels[level].Start = start;
                }
            }
            if (cursor > lfo + lfoLength) throw new InvalidDataException("Doc.List: Truncated overrides.");
            result.Add(i + 1, levels);
        }
        return result;
    }

    private static ListItem ReadLevel(byte[] table, ref int cursor, int level, ConversionContext context)
    {
        Bounds(table, cursor, 28); int record = cursor, start = I32(table, record), format = table[record + 4];
        int chpx = table[record + 24], papx = table[record + 25];
        cursor = checked(cursor + 28 + chpx + papx); int characters = U16(table, cursor); cursor += 2;
        string label = Unicode.GetString(Slice(table, cursor, checked(characters * 2))); cursor += characters * 2;
        if (format is not 0 and not 23) context.Loss("Doc.ListFormat", "Unsupported list formats become decimal numbering.");
        if (format != 23 && label != ((char)level).ToString() + "." || format == 23 && label is not "•" and not "\uf0b7")
            context.Loss("Doc.ListLabel", "Custom list labels become decimal numbers or round bullets.");
        if ((table[record + 5] & 15) != 0 || table[record + 15] != 0 || chpx != 0 || papx != 0)
            context.Loss("Doc.ListAppearance", "List styling, suffixes and custom restart behavior use the standard list profile.");
        if (format != 23 && (start < 1 || start > 32767)) throw new NotSupportedException("Doc.ListStart: Numbering must start between 1 and 32767.");
        return new ListItem { Level = level, Start = format == 23 ? 1 : start, Ordered = format != 23 };
    }

    private static byte[] WriteLists(List<IGrouping<string, ListItem>> lists)
    {
        byte[] header = new byte[2 + lists.Count * 28]; W16(header, 0, lists.Count);
        for (int i = 0; i < lists.Count; i++)
        {
            int record = 2 + i * 28; W32(header, record, i + 1);
            for (int level = 0; level < 9; level++) W16(header, record + 8 + level * 2, 4095);
        }
        using var stream = new MemoryStream(); stream.Write(header, 0, header.Length);
        foreach (var list in lists)
            for (int level = 0; level < 9; level++)
            {
                ListItem item = list.FirstOrDefault(l => l.Level == level) ?? new ListItem { Level = level, Ordered = true };
                if (item.Start < 1 || item.Start > 32767) throw new NotSupportedException("Doc.ListStart: Numbering must start between 1 and 32767.");
                byte[] value = new byte[28]; W32(value, 0, item.Start); value[4] = item.Ordered ? (byte)0 : (byte)23;
                if (item.Ordered) value[6 + level] = 1;
                stream.Write(value, 0, value.Length);
                string text = item.Ordered ? ((char)level).ToString() + "." : "•";
                stream.WriteByte((byte)text.Length); stream.WriteByte(0); byte[] encoded = Unicode.GetBytes(text); stream.Write(encoded, 0, encoded.Length);
            }
        return stream.ToArray();
    }

    private static byte[] WriteListOverrides(int count)
    {
        byte[] bytes = new byte[4 + count * 20]; W32(bytes, 0, count);
        for (int i = 0; i < count; i++) { W32(bytes, 4 + i * 16, i + 1); W32(bytes, 4 + count * 16 + i * 4, -1); }
        return bytes;
    }
}
