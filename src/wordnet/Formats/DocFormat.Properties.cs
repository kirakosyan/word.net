using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Formats;

internal static partial class DocFormat
{
    private sealed class PropertyRun
    {
        internal PropertyRun(int start, int end, byte[] data, int style = 0) { Start = start; End = end; Data = data; Style = style; }
        internal int Start { get; }
        internal int End { get; }
        internal byte[] Data { get; }
        internal int Style { get; }
    }

    private sealed class DocStyle
    {
        internal int Parent { get; set; }
        internal int Kind { get; set; }
        internal int Heading { get; set; }
        internal byte[] Paragraph { get; set; } = Array.Empty<byte>();
        internal byte[] Character { get; set; } = Array.Empty<byte>();
    }

    private sealed class Properties
    {
        internal bool Bold { get; set; }
        internal bool Italic { get; set; }
        internal bool Hidden { get; set; }
        internal int Heading { get; set; }
        internal bool InTable { get; set; }
        internal ParagraphAlignment Alignment { get; set; }
        internal ParagraphLayout Layout { get; set; } = new ParagraphLayout();
        internal bool RowEnd { get; set; }
        internal int Depth { get; set; }
        internal bool Header { get; set; }
        internal bool CantSplit { get; set; }
        internal int? Columns { get; set; }
        internal double? FontSize { get; set; }
        internal string? Color { get; set; }
        internal double? TableWidth { get; set; }
        internal double TableLeft { get; set; }
        internal int ListId { get; set; }
        internal int ListLevel { get; set; }
        internal int? PictureOffset { get; set; }
        internal bool Special { get; set; }
        internal bool BinaryData { get; set; }
        internal double[]? Widths { get; set; }
        internal CellPadding[]? Padding { get; set; }
        internal int[]? CellFlags { get; set; }
        internal Properties Clone()
        {
            var copy = (Properties)MemberwiseClone();
            copy.Padding = Padding?.Select(p => p.Copy()).ToArray();
            copy.Layout = Layout.Copy();
            return copy;
        }
    }

    private sealed class Prl
    {
        internal Prl(int code, byte[] operand) { Code = code; Operand = operand; }
        internal int Code { get; }
        internal byte[] Operand { get; }
    }

    private static List<Prl> ParseProperties(byte[] bytes)
    {
        var result = new List<Prl>();
        int position = 0;
        while (position < bytes.Length)
        {
            if (position == bytes.Length - 1 && bytes[position] == 0) break; // Word-aligned UPX padding.
            int code = U16(bytes, position); position += 2;
            int length;
            int type = code >> 13;
            if (type == 6)
            {
                Bounds(bytes, position, 1);
                if (code is 0xd608 or 0xd606) { length = U16(bytes, position) - 1; position += 2; }
                else
                {
                    length = bytes[position++];
                    // The extended tabs encoding cannot be skipped as an ordinary 255-byte operand.
                    if (code == 0xc615 && length == 255) throw new NotSupportedException("Doc.TabChanges: Extended tab changes are not supported.");
                }
            }
            else length = type switch { 0 or 1 => 1, 2 or 4 or 5 => 2, 3 => 4, 7 => 3, _ => throw new InvalidDataException("Doc.Sprm: Invalid operand type.") };
            result.Add(new Prl(code, Slice(bytes, position, length)));
            position = checked(position + length);
        }
        return result;
    }

    private static List<PropertyRun> ReadFkps(byte[] word, byte[] table, int index, bool paragraph, ConversionContext context)
    {
        int offset = I32(word, 154 + index * 8), length = I32(word, 158 + index * 8);
        if (length == 0) return new List<PropertyRun>();
        Bounds(table, offset, length);
        if (length < 12 || (length - 4) % 8 != 0) throw new InvalidDataException("Doc.Fkp: Invalid formatting page table.");
        int count = (length - 4) / 8;
        var result = new List<PropertyRun>();
        var pages = new HashSet<int>();
        int previous = -1;
        for (int i = 0; i < count; i++)
        {
            context.CheckCancellation();
            int start = I32(table, offset + i * 4), end = I32(table, offset + (i + 1) * 4);
            int pageNumber = I32(table, offset + (count + 1) * 4 + i * 4);
            if (start < 0 || end <= start || pageNumber < 0 || pageNumber > (word.Length - 512) / 512 || !pages.Add(pageNumber))
                throw new InvalidDataException("Doc.Fkp: Invalid formatting range or page number.");
            int page = pageNumber * 512;
            Bounds(word, page, 512);
            int runs = word[page + 511], entrySize = paragraph ? 13 : 1;
            if (runs < 1 || runs > (paragraph ? 29 : 101)) throw new InvalidDataException("Doc.Fkp: Invalid run count.");
            int propertiesStart = (runs + 1) * 4 + runs * entrySize;
            for (int r = 0; r < runs; r++)
            {
                int from = I32(word, page + r * 4), to = I32(word, page + (r + 1) * 4);
                if (from < start || to > end || to <= from || from < previous) throw new InvalidDataException("Doc.Fkp: Invalid or overlapping run offsets.");
                previous = to;
                int pointer = word[page + (runs + 1) * 4 + r * entrySize] * 2;
                byte[] data = Array.Empty<byte>(); int style = 0;
                if (pointer != 0)
                {
                    if (pointer < propertiesStart || pointer >= 511) throw new InvalidDataException("Doc.Fkp: Property data overlaps the run index.");
                    int size = word[page + pointer++];
                    if (paragraph)
                    {
                        if (size == 0) { if (pointer >= 511) throw new InvalidDataException("Doc.Fkp: Missing paragraph property length."); size = 2 * word[page + pointer++]; }
                        else size = 2 * size - 1;
                        if (size < 2) throw new InvalidDataException("Doc.Fkp: Missing paragraph style.");
                    }
                    if (pointer + size > 511) throw new InvalidDataException("Doc.Fkp: Property data exceeds its page.");
                    if (paragraph) { style = U16(word, page + pointer); pointer += 2; size -= 2; }
                    data = Slice(word, page + pointer, size);
                }
                result.Add(new PropertyRun(from, to, data, style));
            }
        }
        return result;
    }

    private static PropertyRun? FindRun(List<PropertyRun> runs, int offset)
    {
        int low = 0, high = runs.Count - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            PropertyRun run = runs[middle];
            if (offset < run.Start) high = middle - 1;
            else if (offset >= run.End) low = middle + 1;
            else return run;
        }
        return null;
    }

    private static Dictionary<int, DocStyle> ReadStyles(byte[] word, byte[] table)
    {
        int offset = I32(word, 162), length = I32(word, 166);
        byte[] bytes = Slice(table, offset, length);
        int header = U16(bytes, 0), count = U16(bytes, 2), baseSize = U16(bytes, 4);
        if (header < 18 || count > 4094 || baseSize is not 10 and not 18) throw new InvalidDataException("Doc.Styles: Invalid stylesheet header.");
        int position = checked(header + 2);
        Bounds(bytes, 2, header);
        var styles = new Dictionary<int, DocStyle>();
        for (int i = 0; i < count; i++)
        {
            int size = U16(bytes, position); position += 2;
            if (size == 0) continue;
            byte[] style = Slice(bytes, position, size); position += size;
            Bounds(style, 0, baseSize);
            int kind = U16(style, 2) & 15, parent = U16(style, 2) >> 4, sets = U16(style, 4) & 15;
            int nameLength = U16(style, baseSize);
            int cursor = checked(baseSize + 2 + (nameLength + 1) * 2);
            Bounds(style, baseSize, cursor - baseSize);
            var entry = new DocStyle { Kind = kind, Parent = parent, Heading = i is >= 1 and <= 6 ? i : 0 };
            for (int set = 0; set < sets; set++)
            {
                int upxSize = U16(style, cursor); cursor += 2;
                byte[] upx = Slice(style, cursor, upxSize); cursor = checked(cursor + upxSize + (upxSize & 1));
                if (kind == 1 && set == 0) { if (upx.Length < 2) throw new InvalidDataException("Doc.Styles: Missing paragraph style index."); entry.Paragraph = Slice(upx, 2, upx.Length - 2); }
                else if ((kind == 1 && set == 1) || (kind == 2 && set == 0)) entry.Character = upx;
            }
            styles.Add(i, entry);
        }
        return styles;
    }

    private sealed class PropertyReader
    {
        private readonly Dictionary<int, DocStyle> styles;
        private readonly ConversionContext context;
        private readonly Func<byte[]> loadData;
        private readonly Dictionary<int, Properties> resolved = new Dictionary<int, Properties>();
        private readonly Dictionary<byte[], List<Prl>> parsed = new Dictionary<byte[], List<Prl>>();
        private byte[]? data;

        internal PropertyReader(Dictionary<int, DocStyle> styles, ConversionContext context, Func<byte[]> loadData)
        { this.styles = styles; this.context = context; this.loadData = loadData; }

        internal Properties Style(int index) => Resolve(index, new HashSet<int>());

        private Properties Resolve(int index, HashSet<int> chain)
        {
            if (index == 4095) return new Properties();
            if (resolved.TryGetValue(index, out Properties? value)) return value.Clone();
            if (!styles.TryGetValue(index, out DocStyle? style))
            {
                if (index is 0 or 10) return new Properties();
                throw new InvalidDataException("Doc.Styles: Referenced style is missing.");
            }
            if (!chain.Add(index) || chain.Count > context.Options.MaximumNestingDepth) throw new InvalidDataException("Doc.Styles: Cyclic or excessively deep style inheritance.");
            Properties state = Resolve(style.Parent, chain); chain.Remove(index);
            if (style.Kind is not 1 and not 2) context.Loss("Doc.StyleKind", "Table and numbering style formatting is not retained.");
            if (style.Heading != 0) state.Heading = style.Heading;
            Apply(state, style.Paragraph, state.Clone());
            Apply(state, style.Character, state.Clone());
            resolved.Add(index, state.Clone());
            return state;
        }

        internal Properties Character(Properties paragraphStyle, byte[] direct, byte[] piece)
        {
            Properties baseline = paragraphStyle.Clone();
            foreach (Prl property in Get(direct).Concat(Get(piece)))
                if (property.Code == 0x4a30)
                {
                    Properties characterStyle = Style(U16(property.Operand, 0));
                    // Character styles supply their supported properties on top of the paragraph style.
                    if (styles.TryGetValue(U16(property.Operand, 0), out DocStyle? definition))
                        ApplyCharacterStyle(baseline, definition, new HashSet<DocStyle>());
                    else { baseline.Bold = characterStyle.Bold; baseline.Italic = characterStyle.Italic; }
                }
            Properties state = baseline.Clone();
            Apply(state, direct, baseline);
            Apply(state, piece, baseline);
            return state;
        }

        private void ApplyCharacterStyle(Properties state, DocStyle style, HashSet<DocStyle> chain)
        {
            if (!chain.Add(style) || chain.Count > context.Options.MaximumNestingDepth) throw new InvalidDataException("Doc.Styles: Invalid character style inheritance.");
            if (styles.TryGetValue(style.Parent, out DocStyle? parent)) ApplyCharacterStyle(state, parent, chain);
            Apply(state, style.Character, state.Clone());
            chain.Remove(style);
        }

        internal void Apply(Properties state, byte[] bytes, Properties baseline) => Apply(state, bytes, baseline, new HashSet<int>());

        private List<Prl> Get(byte[] bytes)
        {
            if (!parsed.TryGetValue(bytes, out List<Prl>? properties)) { properties = ParseProperties(bytes); parsed.Add(bytes, properties); }
            return properties;
        }

        private void Apply(Properties state, byte[] bytes, Properties baseline, HashSet<int> pointers)
        {
            foreach (Prl property in Get(bytes))
            {
                context.CheckCancellation();
                int code = property.Code; byte[] operand = property.Operand;
                switch (code)
                {
                    case 0x0835: state.Bold = Toggle(operand[0], baseline.Bold); break;
                    case 0x0836: state.Italic = Toggle(operand[0], baseline.Italic); break;
                    case 0x083c: state.Hidden = Toggle(operand[0], baseline.Hidden); if (state.Hidden) context.Loss("Doc.HiddenText", "Hidden text is omitted."); break;
                    case 0x2a33: state.Bold = baseline.Bold; state.Italic = baseline.Italic; state.FontSize = baseline.FontSize; state.Color = baseline.Color; break;
                    case 0x4a43:
                        int halfPoints = U16(operand, 0);
                        if (halfPoints < 2 || halfPoints > 3276) throw new InvalidDataException("Doc.FontSize: Invalid half-point size.");
                        state.FontSize = halfPoints / 2.0; break;
                    case 0x6870:
                        if (operand[3] != 0 && operand[3] != 255) throw new InvalidDataException("Doc.Color: Invalid COLORREF.");
                        state.Color = operand[3] == 255 ? null : string.Concat(operand.Take(3).Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture))); break;
                    case 0x2a42:
                        string[] palette = { "", "000000", "0000FF", "00FFFF", "00FF00", "FF00FF", "FF0000", "FFFF00", "FFFFFF", "000080", "008080", "008000", "800080", "800000", "808000", "808080", "C0C0C0" };
                        if (operand[0] >= palette.Length) throw new InvalidDataException("Doc.Color: Invalid indexed color.");
                        state.Color = operand[0] == 0 ? null : palette[operand[0]]; break;
                    case 0x460b: state.ListId = U16(operand, 0); break;
                    case 0x260a:
                        if (operand[0] > 8) throw new InvalidDataException("Doc.List: Invalid level.");
                        state.ListLevel = operand[0]; break;
                    case 0x6a03: state.PictureOffset = I32(operand, 0); break;
                    case 0x0855: state.Special = operand[0] != 0; break;
                    case 0x0806: state.BinaryData = operand[0] != 0; break;
                    case 0xd632: case 0xd634: ReadPadding(state, operand, code == 0xd634); break;
                    case 0xf614:
                        int preferredWidth = U16(operand, 1);
                        if (operand[0] > 3 || preferredWidth > (operand[0] == 2 ? 30000 : 31680) || operand[0] < 2 && preferredWidth != 0)
                            throw new InvalidDataException("Doc.TableWidth: Invalid preferred table width.");
                        if (operand[0] == 2) context.Loss("Doc.TableWidth", "Percentage table widths become automatic widths.");
                        state.TableWidth = operand[0] == 3 && preferredWidth > 0 ? preferredWidth / 20.0 : null; break;
                    case 0x4a30: break; // Character style selected before direct formatting.
                    case 0x2403: // Physical alignment (Word 97).
                        if (operand[0] > 5) throw new InvalidDataException("Doc.Alignment: Invalid paragraph alignment.");
                        state.Alignment = operand[0] <= 2 ? (ParagraphAlignment)operand[0] : ParagraphAlignment.Left;
                        if (operand[0] > 2) context.Loss("Doc.Alignment", "Justified and distributed paragraphs become left aligned.");
                        break;
                    case 0x2405: state.Layout.KeepTogether = operand[0] != 0; break;
                    case 0x2406: state.Layout.KeepWithNext = operand[0] != 0; break;
                    case 0x2407: state.Layout.PageBreakBefore = operand[0] != 0; break;
                    case 0xa413: state.Layout.Before = U16(operand, 0) / 20.0; break;
                    case 0xa414: state.Layout.After = U16(operand, 0) / 20.0; break;
                    case 0x840f: case 0x845e: state.Layout.Left = unchecked((short)U16(operand, 0)) / 20.0; break;
                    case 0x840e: case 0x845d: state.Layout.Right = unchecked((short)U16(operand, 0)) / 20.0; break;
                    case 0x8411: case 0x8460: state.Layout.FirstLine = unchecked((short)U16(operand, 0)) / 20.0; break;
                    case 0x6412:
                        int spacing = unchecked((short)U16(operand, 0));
                        int multiple = U16(operand, 2);
                        if (multiple > 1 || multiple == 1 && spacing <= 0) throw new InvalidDataException("Doc.LineSpacing: Invalid line spacing.");
                        state.Layout.MultipleLineSpacing = multiple == 1;
                        state.Layout.ExactLineSpacing = multiple == 0 && spacing < 0;
                        state.Layout.LineSpacing = spacing == 0 ? null : Math.Abs(spacing) / (multiple == 1 ? 240.0 : 20.0); break;
                    case 0x2640:
                        if (operand[0] > 9) throw new InvalidDataException("Doc.Heading: Invalid outline level.");
                        state.Heading = operand[0] < 6 ? operand[0] + 1 : 0;
                        if (operand[0] is >= 6 and <= 8) context.Loss("Doc.Heading", "Outline levels beyond six are flattened.");
                        break;
                    case 0x2416: state.InTable = operand[0] != 0; break;
                    case 0x2417: state.RowEnd = operand[0] != 0; break;
                    case 0x6649: state.Depth = I32(operand, 0); break;
                    case 0x664a: state.Depth = checked(state.Depth + I32(operand, 0)); break;
                    case 0x244b:
                    case 0x244c:
                        if (operand[0] != 0) throw new NotSupportedException("Doc.NestedTable: Nested tables are not supported.");
                        break;
                    case 0x3404: state.Header = operand[0] != 0; break;
                    case 0x3403: state.CantSplit = operand[0] != 0; break;
                    case 0xd608: ReadTableDefinition(state, operand, context); break;
                    case 0x7621: // TInsert: cell range followed by width.
                        if (operand[0] > 63 || operand[1] > 63 || operand[0] + operand[1] > 63) throw new InvalidDataException("Doc.Table: Invalid inserted cell range.");
                        state.Columns = Math.Max(state.Columns ?? 0, operand[0] + operand[1]); break;
                    case 0x6646:
                    case 0x6645:
                    case 0x646b:
                        int pointer = I32(operand, 0);
                        if (!pointers.Add(pointer) || pointers.Count > context.Options.MaximumNestingDepth) throw new InvalidDataException("Doc.Properties: Cyclic or excessive indirect properties.");
                        data ??= loadData();
                        int length = U16(data, pointer);
                        Apply(state, Slice(data, checked(pointer + 2), length), baseline, pointers);
                        pointers.Remove(pointer); break;
                    case 0x0800:
                    case 0x0801:
                    case 0x2a83:
                        if (operand[0] != 0) context.Loss("Doc.Revisions", "Tracked changes are not preserved.");
                        break;
                    // Bookkeeping/language flags do not change the retained semantic content.
                    case 0x486d:
                    case 0x486e:
                    case 0x486f:
                    case 0x4a41:
                    case 0x4a42:
                    case 0x6815:
                    case 0x6816:
                    case 0x6817:
                    case 0x6864:
                    case 0x6865:
                    case 0x6866:
                    case 0x6467:
                    case 0x6468:
                    case 0x6469:
                    case 0x646a:
                        break;
                    default:
                        context.Loss("Doc.UnsupportedProperty", "Some document appearance, metadata or advanced formatting properties are not retained.");
                        break;
                }
            }
            if (state.Depth < 0 || state.Depth > 1) throw new NotSupportedException("Doc.NestedTable: Only top-level tables are supported.");
            if (state.Depth == 1) state.InTable = true;
        }

        internal DocumentImage Image(int offset) => ReadPicture(data ??= loadData(), offset, context);
    }

    private static void ReadPadding(Properties state, byte[] operand, bool defaults)
    {
        if (operand.Length != 6 || operand[0] >= operand[1] || operand[1] > 63 || (operand[2] & ~15) != 0
            || operand[3] is not 0 and not 3 || U16(operand, 4) > 31680 || operand[3] == 0 && U16(operand, 4) != 0)
            throw new InvalidDataException("Doc.CellPadding: Invalid cell margin operand.");
        state.Padding ??= Enumerable.Range(0, 63).Select(_ => new CellPadding { Left = 5.4, Right = 5.4 }).ToArray();
        for (int c = defaults ? 0 : operand[0]; c < (defaults ? 63 : operand[1]); c++)
        {
            double value = U16(operand, 4) / 20.0; CellPadding p = state.Padding[c];
            if ((operand[2] & 1) != 0) p.Top = value;
            if ((operand[2] & 2) != 0) p.Left = value;
            if ((operand[2] & 4) != 0) p.Bottom = value;
            if ((operand[2] & 8) != 0) p.Right = value;
        }
    }

    private static bool Toggle(byte value, bool baseline) => value switch
    {
        0 => false,
        1 => true,
        0x80 => baseline,
        0x81 => !baseline,
        _ => throw new InvalidDataException("Doc.Toggle: Invalid Boolean formatting operand."),
    };

    private static void ReadTableDefinition(Properties state, byte[] bytes, ConversionContext context)
    {
        Bounds(bytes, 0, 1); int columns = bytes[0];
        if (columns > 63) throw new InvalidDataException("Doc.Table: Invalid column count.");
        int definitions = checked(1 + (columns + 1) * 2);
        Bounds(bytes, 0, definitions);
        int previous = short.MinValue;
        state.Widths = new double[columns];
        state.CellFlags = new int[columns];
        for (int i = 0; i <= columns; i++)
        {
            int edge = unchecked((short)U16(bytes, 1 + i * 2));
            if (i == 0) state.TableLeft = edge / 20.0;
            if (edge < previous) throw new InvalidDataException("Doc.Table: Cell boundaries are not ordered.");
            if (i > 0) state.Widths[i - 1] = (edge - previous) / 20.0;
            previous = edge;
        }
        if ((bytes.Length - definitions) % 20 != 0) throw new InvalidDataException("Doc.Table: Truncated cell definition.");
        for (int i = 0; i < Math.Min(columns, (bytes.Length - definitions) / 20); i++)
        {
            int flags = U16(bytes, definitions + i * 20);
            state.CellFlags[i] = flags;
            if ((flags & ~0x63) != 0 || Slice(bytes, definitions + i * 20 + 4, 16).Any(b => b != 0))
                context.Loss("Doc.TableAppearance", "Borders, shading and advanced table appearance are not retained.");
        }
        state.Columns = columns;
    }
}
