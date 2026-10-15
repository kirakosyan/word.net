using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenMcdf;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Formats;

internal static partial class DocFormat
{
    private sealed class TextPiece
    {
        internal TextPiece(int start, int end, int offset, int width, byte[] properties)
        { Start = start; End = end; Offset = offset; Width = width; Properties = properties; }
        internal int Start { get; }
        internal int End { get; }
        internal int Offset { get; }
        internal int Width { get; }
        internal byte[] Properties { get; }
        internal int FileOffset(int cp) => checked(Offset + (cp - Start) * Width);
    }

    internal static DocumentModel Read(Stream input, ConversionContext context)
    {
        try { return ReadCore(input, context); }
        catch (Exception error) when (error is FileFormatException or EndOfStreamException or FileNotFoundException)
        { throw new InvalidDataException("Doc.Container: Invalid compound document or missing required stream.", error); }
    }

    private static DocumentModel ReadCore(Stream input, ConversionContext context)
    {
        using var root = RootStorage.Open(input, StorageModeFlags.LeaveOpen | StorageModeFlags.StrictValidation);
        byte[] word = ReadStream(root, "WordDocument", context);
        if (word.Length < 898 || U16(word, 0) != 0xa5ec || U16(word, 2) < 0xc1) throw new InvalidDataException("Doc.Header: Unsupported or invalid Word binary header.");
        int flags = U16(word, 10);
        if ((flags & 0x8100) != 0) throw new NotSupportedException("Doc.Encryption: Password-protected DOC files are unsupported.");
        if (U16(word, 32) != 14 || U16(word, 62) != 22 || U16(word, 152) < 34) throw new InvalidDataException("Doc.Header: Invalid FIB lengths.");
        byte[] table = ReadStream(root, (flags & 0x200) != 0 ? "1Table" : "0Table", context);
        string allText = ReadText(word, table, context, out List<TextPiece> pieces, out int terminalParagraph);
        string text = allText.Substring(0, I32(word, 76));
        List<PropertyRun> paragraphRuns = ReadFkps(word, table, 13, true, context);
        List<PropertyRun> characterRuns = ReadFkps(word, table, 12, false, context);
        var properties = new PropertyReader(ReadStyles(word, table), context, () => ReadStream(root, "Data", context));
        Dictionary<int, ListItem[]> lists = ReadLists(word, table, context);
        List<(int End, PageSettings Settings)> sections = ReadSections(word, table, text, context);
        context.Loss("Doc.Formatting", "DOC import retains the documented text, section and table profile; unsupported Word formatting is not retained.");
        DocumentModel model = ReadContent(text, 0, pieces, paragraphRuns, characterRuns, properties, lists, sections, context, terminalParagraph);
        ReadHeaders(allText, word, table, pieces, paragraphRuns, characterRuns, properties, lists, sections, context);
        ReadMetadata(root, model, context);
        if (Enumerable.Range(1, 7).Any(i => i != 2 && I32(word, 76 + i * 4) != 0))
            context.Loss("Doc.OtherStories", "Notes, comments and textboxes outside the header/footer profile are not imported.");
        return model;
    }

    private static DocumentModel ReadContent(string text, int cpBase, List<TextPiece> pieces,
        List<PropertyRun> paragraphRuns, List<PropertyRun> characterRuns, PropertyReader properties,
        Dictionary<int, ListItem[]> lists, List<(int End, PageSettings Settings)> sections, ConversionContext context, int? terminalParagraph = null)
    {
        int sectionIndex = 0;
        var model = new DocumentModel();
        TableBlock? currentTable = null;
        var row = new List<TableCell>();
        TableCell? pendingCell = null;
        var fields = new BinaryFields(context);
        int position = 0;
        while (position < text.Length)
        {
            context.CheckCancellation();
            while (sectionIndex < sections.Count - 1 && position >= sections[sectionIndex].End) sectionIndex++;
            PageSettings? section = sections.Count == 0 ? null : sections[sectionIndex].Settings;
            int end = position;
            while (end < text.Length && text[end] is not '\r' and not '\u0007'
                && !(text[end] == '\f' && sectionIndex < sections.Count - 1 && end + 1 == sections[sectionIndex].End)) end++;
            int mark = Math.Min(end, text.Length - 1);
            int formattingCp = terminalParagraph.HasValue && mark == text.Length - 1 ? terminalParagraph.Value : mark + cpBase;
            TextPiece markPiece = FindPiece(pieces, formattingCp);
            PropertyRun? papx = FindRun(paragraphRuns, markPiece.FileOffset(formattingCp));
            if (papx == null && paragraphRuns.Count > 0) throw new InvalidDataException("Doc.Fkp: Paragraph mark has no formatting range.");
            Properties style = properties.Style(papx?.Style ?? 0);
            Properties paragraphProperties = style.Clone();
            properties.Apply(paragraphProperties, papx?.Data ?? Array.Empty<byte>(), style);
            properties.Apply(paragraphProperties, markPiece.Properties, style);
            var paragraph = new ParagraphBlock { HeadingLevel = paragraphProperties.Heading, Alignment = paragraphProperties.Alignment, Layout = paragraphProperties.Layout.Copy(), Section = section };
            if (paragraphProperties.ListId != 0)
            {
                if (!lists.TryGetValue(paragraphProperties.ListId, out ListItem[]? levels) || paragraphProperties.ListLevel >= levels.Length)
                    throw new NotSupportedException("Doc.List: Unresolved or legacy numbering reference.");
                paragraph.List = levels[paragraphProperties.ListLevel].Copy();
            }
            AddRuns(paragraph, position, end, style);
            char terminator = end < text.Length ? text[end] : '\r';
            if (paragraphProperties.InTable)
            {
                if (currentTable == null) { currentTable = new TableBlock { Section = section }; model.Blocks.Add(currentTable); }
                if (paragraphProperties.RowEnd)
                {
                    if (terminator != '\u0007' || paragraph.Text.Length != 0 || pendingCell != null || row.Count == 0)
                        throw new InvalidDataException("Doc.Table: Invalid table row terminator.");
                    if (paragraphProperties.Columns.HasValue && paragraphProperties.Columns != row.Count)
                        throw new InvalidDataException("Doc.Table: Cell definitions do not match the row content.");
                    if (paragraphProperties.Header)
                    {
                        if (currentTable.HeaderRowCount == currentTable.Rows.Count) currentTable.HeaderRowCount++;
                        else context.Loss("Doc.TableHeaders", "Noncontiguous header rows are not retained.");
                    }
                    if (paragraphProperties.CantSplit) currentTable.NonSplittingRows.Add(currentTable.Rows.Count);
                    for (int c = 0; c < row.Count; c++)
                    {
                        if (paragraphProperties.Widths != null && paragraphProperties.Widths[c] > 0) row[c].CellWidth = paragraphProperties.Widths[c];
                        if (paragraphProperties.Padding != null) row[c].Padding = paragraphProperties.Padding[c].Copy();
                        int cellFlags = paragraphProperties.CellFlags?[c] ?? 0;
                        if ((cellFlags & 0x60) == 0x40) throw new InvalidDataException("Doc.TableMerge: Invalid vertical merge flag.");
                        row[c].Merge = (cellFlags & 0x60) == 0x60 ? VerticalMerge.Start : (cellFlags & 0x20) != 0 ? VerticalMerge.Continue : VerticalMerge.None;
                    }
                    if (currentTable.Rows.Count == 0 && paragraphProperties.Widths != null)
                    {
                        currentTable.ColumnWidths.AddRange(paragraphProperties.Widths);
                        currentTable.Width = paragraphProperties.TableWidth ?? paragraphProperties.Widths.Sum();
                    }
                    var merged = new List<TableCell>();
                    bool horizontalStart = false;
                    for (int c = 0; c < row.Count; c++)
                    {
                        int horizontal = (paragraphProperties.CellFlags?[c] ?? 0) & 3;
                        if (horizontal == 1)
                        {
                            if (!horizontalStart || merged.Count == 0) throw new InvalidDataException("Doc.TableMerge: Horizontal continuation has no preceding start.");
                            if (row[c].Paragraphs.Any(p => p.Runs.Any(run => run.Image != null || run.Text.Length != 0)))
                                throw new NotSupportedException("Doc.TableMergeContent: Content in a horizontal continuation cell cannot be discarded.");
                            TableCell first = merged[merged.Count - 1];
                            first.CellWidth = (first.CellWidth ?? 0) + (row[c].CellWidth ?? 0);
                        }
                        else { merged.Add(row[c]); horizontalStart = horizontal >= 2; }
                    }
                    currentTable.RowLefts.Add(currentTable.Rows.Count, paragraphProperties.TableLeft);
                    currentTable.Rows.Add(merged); row = new List<TableCell>();
                }
                else
                {
                    pendingCell ??= new TableCell();
                    pendingCell.Paragraphs.Add(paragraph);
                    if (terminator == '\u0007')
                    {
                        row.Add(pendingCell); pendingCell = null;
                        if (row.Count > 63) throw new InvalidDataException("Doc.Table: Too many cells in a row.");
                    }
                }
            }
            else
            {
                if (pendingCell != null || row.Count != 0) throw new InvalidDataException("Doc.Table: Incomplete table row.");
                if (terminator == '\u0007') throw new InvalidDataException("Doc.Table: Cell mark outside a table.");
                currentTable = null; model.Blocks.Add(paragraph);
            }
            position = end + 1;
        }
        if (fields.Count > 0) throw new InvalidDataException("Doc.Fields: Unterminated field.");
        if (pendingCell != null || row.Count != 0) throw new InvalidDataException("Doc.Table: Incomplete table row.");
        model.FinalSection = sections.LastOrDefault().Settings;
        foreach (TableBlock tableBlock in model.Blocks.OfType<TableBlock>()) InferTableGrid(tableBlock, context);
        return model;

        void AddRuns(ParagraphBlock paragraph, int start, int end, Properties paragraphStyle)
        {
            int cp = start;
            while (cp < end)
            {
                context.CheckCancellation();
                TextPiece piece = FindPiece(pieces, cp + cpBase); int offset = piece.FileOffset(cp + cpBase);
                PropertyRun? chpx = FindRun(characterRuns, offset);
                if (chpx == null && characterRuns.Count > 0) throw new InvalidDataException("Doc.Fkp: Character has no formatting range.");
                int next = Math.Min(end, piece.End - cpBase);
                if (chpx != null)
                {
                    int characters = (chpx.End - offset) / piece.Width;
                    if (characters < 1) throw new InvalidDataException("Doc.Fkp: Formatting splits a Unicode character.");
                    next = Math.Min(next, cp + characters);
                }
                Properties character = properties.Character(paragraphStyle, chpx?.Data ?? Array.Empty<byte>(), piece.Properties);
                string raw = text.Substring(cp, next - cp);
                if (raw.Contains('\u0001') && character.Special && character.PictureOffset.HasValue && !character.BinaryData && !character.Hidden && !fields.InInstruction)
                {
                    foreach (string part in raw.Split('\u0001').Select((s, i) => (i == 0 ? "" : "\u0001") + s))
                    {
                        string value = part;
                        if (value.Length > 0 && value[0] == '\u0001') { fields.AddRun(paragraph, new TextRun("", image: properties.Image(character.PictureOffset.Value))); value = value.Substring(1); }
                        fields.Text(paragraph, value, character);
                    }
                }
                else
                {
                    fields.Text(paragraph, raw, character);
                }
                cp = next;
            }
        }
    }

    private static string ReadText(byte[] word, byte[] table, ConversionContext context, out List<TextPiece> pieces, out int terminalParagraph)
    {
        long total = 0;
        for (int part = 0; part < 8; part++)
        {
            int partLength = I32(word, 76 + part * 4);
            if (partLength < 0) throw new InvalidDataException("Doc.Characters: Invalid story length.");
            total += partLength;
        }
        if (total > int.MaxValue - 1) throw new InvalidDataException("Doc.Characters: Invalid aggregate character count.");
        int characters = (int)total;
        bool otherParts = characters > I32(word, 76);
        context.CheckCharacters(characters);
        int clx = I32(word, 418), length = I32(word, 422); Bounds(table, clx, length);
        int limit = checked(clx + length); var modifiers = new List<byte[]>();
        while (clx < limit && table[clx] == 1)
        {
            int size = U16(table, clx + 1);
            if (size > limit - clx - 3) throw new InvalidDataException("Doc.PieceProperties: Truncated property record.");
            modifiers.Add(Slice(table, clx + 3, size)); clx = checked(clx + 3 + size);
        }
        if (clx > limit - 5 || table[clx] != 2) throw new InvalidDataException("Doc.PieceTable: Missing text piece table.");
        int plcLength = I32(table, clx + 1);
        if (plcLength < 4 || (plcLength - 4) % 12 != 0 || plcLength > limit - clx - 5) throw new InvalidDataException("Doc.PieceTable: Invalid piece table length.");
        int count = (plcLength - 4) / 12, start = clx + 5;
        var text = new StringBuilder(); pieces = new List<TextPiece>(); int previous = 0;
        for (int i = 0; i < count; i++)
        {
            context.CheckCancellation();
            int cp = I32(table, start + i * 4), next = I32(table, start + (i + 1) * 4);
            if (cp != previous || next <= cp) throw new InvalidDataException("Doc.PieceTable: Invalid character positions."); previous = next;
            if (next > characters + (otherParts ? 1 : 0)) throw new InvalidDataException("Doc.PieceTable: Text pieces exceed the declared document parts.");
            int size = Math.Max(0, Math.Min(next, characters) - cp);
            int pcd = start + 4 * (count + 1) + 8 * i;
            uint encoded = unchecked((uint)I32(table, pcd + 2));
            bool compressed = (encoded & 0x40000000) != 0;
            int offset = checked((int)(encoded & 0x3fffffff)); if (compressed) offset /= 2;
            Bounds(word, offset, checked((next - cp) * (compressed ? 1 : 2)));
            if (compressed) for (int c = 0; c < size; c++) { byte b = word[offset + c]; text.Append(b >= 0x80 && b <= 0x9f ? Windows1252[b - 0x80] : (char)b); }
            else text.Append(Unicode.GetString(word, offset, size * 2));
            int prm = U16(table, pcd + 6); byte[] properties;
            if ((prm & 1) != 0)
            {
                if ((prm >> 1) >= modifiers.Count) throw new InvalidDataException("Doc.PieceProperties: Invalid property reference.");
                properties = modifiers[prm >> 1];
            }
            else properties = SimpleModifier(prm, context);
            pieces.Add(new TextPiece(cp, next, offset, compressed ? 1 : 2, properties));
        }
        if (text.Length != characters) throw new InvalidDataException("Doc.PieceTable: Main story is incomplete.");
        if (previous != characters + (otherParts ? 1 : 0)) throw new InvalidDataException("Doc.PieceTable: The terminal paragraph is missing.");
        terminalParagraph = previous - 1;
        return text.ToString();
    }

    private static byte[] SimpleModifier(int prm, ConversionContext context)
    {
        if (prm == 0) return Array.Empty<byte>();
        int code = ((prm >> 1) & 127) switch
        {
            0x18 => 0x2416,
            0x19 => 0x2417,
            0x55 => 0x0835,
            0x56 => 0x0836,
            0x5c => 0x083c,
            0x75 => 0x0855,
            0x78 => 0x2640,
            0x41 => 0x0800,
            0x42 => 0x0801,
            _ => 0,
        };
        if (code == 0) { context.Loss("Doc.PieceProperties", "Some piece-level formatting properties are not retained."); return Array.Empty<byte>(); }
        return Sprm(code, prm >> 8);
    }

    private static TextPiece FindPiece(List<TextPiece> pieces, int cp)
    {
        int low = 0, high = pieces.Count - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2; TextPiece piece = pieces[middle];
            if (cp < piece.Start) high = middle - 1;
            else if (cp >= piece.End) low = middle + 1;
            else return piece;
        }
        throw new InvalidDataException("Doc.PieceTable: Missing text piece.");
    }
}
