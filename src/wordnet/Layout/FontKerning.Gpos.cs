using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WordNet.Layout;

internal sealed partial class FontKerning
{
    // Independently implemented from OpenType GPOS, pair formats 1/2 and extension 9:
    // https://learn.microsoft.com/typography/opentype/spec/gpos
    private sealed class Gpos
    {
        private readonly TableReader table;
        private readonly TableReader? definitions;
        private readonly List<Lookup> lookups = new List<Lookup>();
        internal bool HasKerning => lookups.Count > 0;

        internal Gpos(byte[] font, int start, int length, int gdef, int gdefLength)
        {
            table = new TableReader(font, start, length);
            if (gdefLength >= 12) definitions = new TableReader(font, gdef, gdefLength);
            if (table.U16(start) != 1) throw Invalid();
            int scripts = table.At(start, table.U16(start + 4), 2);
            int features = table.At(start, table.U16(start + 6), 2);
            int lookupList = table.At(start, table.U16(start + 8), 2);
            int featureCount = table.U16(features), lookupCount = table.U16(lookupList);
            table.Check(features + 2, featureCount * 6); table.Check(lookupList + 2, lookupCount * 2);
            var chosen = new SortedSet<int>();
            foreach (int index in LatinFeatures(scripts))
            {
                if (index >= featureCount) throw Invalid();
                int record = features + 2 + index * 6;
                if (table.U32(record) != 0x6b65726e) continue; // kern, not mark/cursive/context shaping.
                int feature = table.At(features, table.U16(record + 4), 4), count = table.U16(feature + 2);
                table.Check(feature + 4, count * 2);
                for (int i = 0; i < count; i++) chosen.Add(table.U16(feature + 4 + i * 2));
            }
            foreach (int index in chosen)
            {
                if (index >= lookupCount) throw Invalid();
                int lookup = table.At(lookupList, table.U16(lookupList + 2 + index * 2), 6);
                int type = table.U16(lookup), flags = table.U16(lookup + 2), count = table.U16(lookup + 4);
                table.Check(lookup + 6, count * 2 + ((flags & 16) != 0 ? 2 : 0));
                var subtables = new List<PairTable>();
                for (int i = 0; i < count; i++)
                {
                    int subtable = table.At(lookup, table.U16(lookup + 6 + i * 2), 2), actualType = type;
                    if (type == 9)
                    {
                        if (table.U16(subtable) != 1) throw Invalid();
                        actualType = table.U16(subtable + 2);
                        subtable = table.At(subtable, table.U32(subtable + 4), 2);
                    }
                    if (actualType == 2) subtables.Add(new PairTable(table, subtable));
                }
                if (subtables.Count > 0) lookups.Add(new Lookup(flags, subtables));
            }
        }

        private IEnumerable<int> LatinFeatures(int scripts)
        {
            int count = table.U16(scripts); table.Check(scripts + 2, count * 6);
            int selected = 0, defaultScript = 0;
            for (int i = 0; i < count; i++)
            {
                int p = scripts + 2 + i * 6, tag = table.U32(p);
                if (tag == 0x6c61746e) selected = table.At(scripts, table.U16(p + 4), 4);
                if (tag == 0x44464c54) defaultScript = table.At(scripts, table.U16(p + 4), 4);
            }
            if (selected == 0) selected = defaultScript;
            if (selected == 0 || table.U16(selected) == 0) yield break;
            int language = table.At(selected, table.U16(selected), 6);
            int required = table.U16(language + 2), entries = table.U16(language + 4);
            table.Check(language + 6, entries * 2);
            if (required != 0xffff) yield return required;
            for (int i = 0; i < entries; i++) yield return table.U16(language + 6 + i * 2);
        }

        internal void Apply(int[] glyphs, GlyphAdjustment[] result, double scale)
        {
            foreach (Lookup lookup in lookups)
            {
                for (int left = 0; left < glyphs.Length; left++)
                {
                    if (Ignored(glyphs[left], lookup.Flags)) continue;
                    int right = left + 1;
                    while (right < glyphs.Length && Ignored(glyphs[right], lookup.Flags)) right++;
                    if (right == glyphs.Length) break;
                    foreach (PairTable subtable in lookup.Subtables)
                    {
                        if (!subtable.Apply(glyphs[left], glyphs[right], ref result[left], ref result[right], scale)) continue;
                        // ValueFormat2=0 permits the second glyph to start the next pair.
                        if (subtable.ValueFormat2 != 0) left = right;
                        break;
                    }
                }
            }
        }

        private bool Ignored(int glyph, int flags)
        {
            if (definitions == null) return false;
            int offset = definitions.U16(definitions.Start + 4);
            int kind = offset == 0 ? 0 : Class(definitions, definitions.At(definitions.Start, offset, 4), glyph);
            if (kind == 1 && (flags & 2) != 0 || kind == 2 && (flags & 4) != 0 || kind == 3 && (flags & 8) != 0) return true;
            int attachment = flags >> 8;
            if (kind != 3 || attachment == 0) return false;
            int classes = definitions.U16(definitions.Start + 10);
            return classes != 0 && Class(definitions, definitions.At(definitions.Start, classes, 4), glyph) != attachment;
        }

        private sealed class Lookup
        {
            internal Lookup(int flags, List<PairTable> subtables) { Flags = flags; Subtables = subtables; }
            internal int Flags { get; }
            internal List<PairTable> Subtables { get; }
        }

        private sealed class PairTable
        {
            private readonly TableReader data;
            private readonly int start, format, coverage, valueFormat1, recordSize, class1, class2, classCount2;
            internal int ValueFormat2 { get; }

            internal PairTable(TableReader data, int start)
            {
                this.data = data; this.start = start; format = data.U16(start);
                coverage = data.At(start, data.U16(start + 2), 4);
                valueFormat1 = data.U16(start + 4); ValueFormat2 = data.U16(start + 6);
                recordSize = ValueSize(valueFormat1) + ValueSize(ValueFormat2);
                ValidateCoverage(data, coverage);
                if (format == 1)
                {
                    int count = data.U16(start + 8); data.Check(start + 10, count * 2);
                    for (int i = 0; i < count; i++)
                    {
                        int set = data.At(start, data.U16(start + 10 + i * 2), 2), pairs = data.U16(set);
                        data.Check(set + 2, pairs * (2 + recordSize));
                    }
                }
                else if (format == 2)
                {
                    class1 = data.At(start, data.U16(start + 8), 4); class2 = data.At(start, data.U16(start + 10), 4);
                    ValidateClass(data, class1); ValidateClass(data, class2);
                    int classCount1 = data.U16(start + 12); classCount2 = data.U16(start + 14);
                    data.Check(start + 16, (long)classCount1 * classCount2 * recordSize);
                }
                else throw Invalid();
            }

            internal bool Apply(int left, int right, ref GlyphAdjustment first, ref GlyphAdjustment second, double scale)
            {
                int index = Coverage(data, coverage, left), record;
                if (index < 0) return false;
                if (format == 1)
                {
                    if (index >= data.U16(start + 8)) throw Invalid();
                    int set = data.At(start, data.U16(start + 10 + index * 2), 2);
                    int low = 0, high = data.U16(set) - 1; record = 0;
                    while (low <= high)
                    {
                        int middle = low + (high - low) / 2, p = set + 2 + middle * (2 + recordSize), glyph = data.U16(p);
                        if (glyph < right) low = middle + 1;
                        else if (glyph > right) high = middle - 1;
                        else { record = p + 2; break; }
                    }
                    if (record == 0) return false;
                }
                else
                {
                    int a = Class(data, class1, left), b = Class(data, class2, right);
                    if (a >= data.U16(start + 12) || b >= classCount2) throw Invalid();
                    record = start + 16 + (a * classCount2 + b) * recordSize;
                }
                ReadValue(record, valueFormat1, ref first, scale);
                ReadValue(record + ValueSize(valueFormat1), ValueFormat2, ref second, scale);
                return true;
            }

            private void ReadValue(int p, int flags, ref GlyphAdjustment value, double scale)
            {
                for (int bit = 0; bit < 8; bit++)
                {
                    if ((flags & (1 << bit)) == 0) continue;
                    double amount = unchecked((short)data.U16(p)) * scale; p += 2;
                    if (bit == 0) value.OffsetX += amount;
                    else if (bit == 1) value.OffsetY += amount;
                    else if (bit == 2) value.AdvanceX += amount;
                    else if (bit == 3) value.AdvanceY += amount;
                    // Device/variation offsets do not change unhinted static-font PDF coordinates.
                }
            }
        }

        private static int ValueSize(int format)
        {
            if ((format & ~255) != 0) throw Invalid();
            int count = 0;
            for (int bit = 0; bit < 8; bit++) if ((format & (1 << bit)) != 0) count += 2;
            return count;
        }

        private static void ValidateCoverage(TableReader data, int p)
        {
            int format = data.U16(p), count = data.U16(p + 2);
            if (format is not 1 and not 2) throw Invalid();
            data.Check(p + 4, count * (format == 1 ? 2 : 6));
        }
        private static int Coverage(TableReader data, int p, int glyph)
        {
            int format = data.U16(p), low = 0, high = data.U16(p + 2) - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2, record = p + 4 + middle * (format == 1 ? 2 : 6);
                int first = data.U16(record), last = format == 1 ? first : data.U16(record + 2);
                if (glyph < first) high = middle - 1;
                else if (glyph > last) low = middle + 1;
                else return format == 1 ? middle : data.U16(record + 4) + glyph - first;
            }
            return -1;
        }
        private static void ValidateClass(TableReader data, int p)
        {
            int format = data.U16(p);
            if (format == 1) data.Check(p + 6, data.U16(p + 4) * 2);
            else if (format == 2) data.Check(p + 4, data.U16(p + 2) * 6);
            else throw Invalid();
        }
        private static int Class(TableReader data, int p, int glyph)
        {
            int format = data.U16(p);
            if (format == 1)
            {
                int index = glyph - data.U16(p + 2);
                return index >= 0 && index < data.U16(p + 4) ? data.U16(p + 6 + index * 2) : 0;
            }
            if (format != 2) throw Invalid();
            int low = 0, high = data.U16(p + 2) - 1;
            data.Check(p + 4, (high + 1) * 6);
            while (low <= high)
            {
                int middle = low + (high - low) / 2, record = p + 4 + middle * 6;
                if (glyph < data.U16(record)) high = middle - 1;
                else if (glyph > data.U16(record + 2)) low = middle + 1;
                else return data.U16(record + 4);
            }
            return 0;
        }

        private sealed class TableReader
        {
            private readonly byte[] bytes;
            private readonly int end;
            internal int Start { get; }
            internal TableReader(byte[] bytes, int start, int length) { this.bytes = bytes; Start = start; end = checked(start + length); Check(start, length); }
            internal void Check(int p, long length) { if (p < Start || length < 0 || length > end - (long)p || p > bytes.Length - length) throw Invalid(); }
            internal int At(int origin, int offset, int size) { long p = (long)origin + offset; if (offset == 0 || p > int.MaxValue) throw Invalid(); Check((int)p, size); return (int)p; }
            internal int U16(int p) { Check(p, 2); return (bytes[p] << 8) | bytes[p + 1]; }
            internal int U32(int p) { Check(p, 4); return FontKerning.U32(bytes, p); }
        }

        private static InvalidDataException Invalid() => new InvalidDataException("Font.Invalid: Invalid OpenType positioning table.");
    }
}
