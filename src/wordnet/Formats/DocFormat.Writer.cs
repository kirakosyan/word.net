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
    private sealed class WriteRun
    {
        internal WriteRun(int start, int end, byte[] properties) { Start = start; End = end; Properties = properties; }
        internal int Start { get; }
        internal int End { get; }
        internal byte[] Properties { get; }
    }

    internal static void Write(DocumentModel model, Stream output, ConversionContext context)
    {
        var text = new StringBuilder();
        var characters = new List<WriteRun>();
        var paragraphs = new List<WriteRun>();
        var sections = new List<(int Start, PageSettings Settings)>();
        var fieldMarks = new List<(int Position, byte Mark, byte Type)>();
        PageSettings? previousSection = null;
        using var data = new BoundedMemoryStream(context.Options.MaximumOutputBytes);
        var lists = model.Paragraphs
            .Where(p => p.List != null).Select(p => p.List!).GroupBy(l => l.Id).ToList();
        if (lists.Count > 2046) throw new NotSupportedException("Doc.ListCount: Too many numbering instances.");
        TableBlock? activeTable = null;
        List<TableCell>? activeRow = null;
        double[] activeEdges = Array.Empty<double>();
        int activeColumns = 0;
        bool cantSplit = false;
        int imageId = 1024;
        bool previousTable = false;
        foreach (DocumentBlock block in model.Blocks)
        {
            context.CheckCancellation();
            if (sections.Count == 0 || !ReferenceEquals(previousSection, block.Section))
            {
                if (text.Length > 0)
                {
                    if (previousTable) AddParagraph(new ParagraphBlock(), '\f', false, false, 0, false);
                    else text[text.Length - 1] = '\f';
                }
                sections.Add((text.Length, block.Section ?? new PageSettings()));
                previousSection = block.Section;
                previousTable = false;
            }
            if (block is ParagraphBlock paragraph)
            {
                AddParagraph(paragraph, '\r', false, false, 0, false);
                previousTable = false;
            }
            else if (block is TableBlock table)
            {
                activeTable = table;
                activeEdges = table.ColumnEdges();
                if (previousTable)
                {
                    context.Loss("Doc.AdjacentTables", "An empty paragraph separates adjacent tables in binary Word output.");
                    AddParagraph(new ParagraphBlock(), '\r', false, false, 0, false);
                }
                for (int r = 0; r < table.Rows.Count; r++)
                {
                    List<TableCell> row = table.Rows[r];
                    activeRow = row;
                    activeColumns = row.Sum(cell => cell.ColumnSpan);
                    cantSplit = table.NonSplittingRows.Contains(r);
                    if (row.Count is < 1 or > 63) throw new NotSupportedException("Doc.TableColumns: DOC rows require between one and 63 cells.");
                    bool header = r < table.HeaderRowCount;
                    foreach (TableCell cell in row)
                        for (int p = 0; p < cell.Paragraphs.Count; p++)
                            AddParagraph(cell.Paragraphs[p], p == cell.Paragraphs.Count - 1 ? '\u0007' : '\r', true, false, 0, header);
                    AddParagraph(new ParagraphBlock(), '\u0007', true, true, row.Count, header);
                }
                previousTable = true;
            }
        }
        // MS-DOC requires the main story to end in a paragraph, outside any table.
        if (text.Length == 0 || previousTable) AddParagraph(new ParagraphBlock(), '\r', false, false, 0, false);
        if (sections.Count == 0) sections.Add((0, model.FinalSection ?? new PageSettings()));
        else if (model.FinalSection != null && !ReferenceEquals(previousSection, model.FinalSection))
        {
            text[text.Length - 1] = '\f';
            sections.Add((text.Length, model.FinalSection));
            AddParagraph(new ParagraphBlock(), '\r', false, false, 0, false);
        }
        int mainLength = text.Length;
        byte[] finalMainParagraph = paragraphs[paragraphs.Count - 1].Properties;
        var headerPositions = new List<int>();
        bool hasStories = sections.Any(s => s.Settings.Stories.Count > 0);
        if (hasStories)
        {
            for (int i = 0; i < 6; i++) headerPositions.Add(0);
            foreach (var section in sections)
                foreach (string kind in HeaderKinds)
                {
                    headerPositions.Add(text.Length - mainLength);
                    if (!section.Settings.Stories.TryGetValue(kind, out List<DocumentBlock>? story)) continue;
                    foreach (DocumentBlock block in story)
                    {
                        if (block is ParagraphBlock paragraph) AddParagraph(paragraph, '\r', false, false, 0, false);
                        else if (block is TableBlock table)
                        {
                            activeTable = table; activeEdges = table.ColumnEdges();
                            for (int r = 0; r < table.Rows.Count; r++)
                            {
                                activeRow = table.Rows[r]; activeColumns = activeRow.Sum(c => c.ColumnSpan); cantSplit = table.NonSplittingRows.Contains(r);
                                if (activeRow.Count is < 1 or > 63) throw new NotSupportedException("Doc.TableColumns: DOC rows require between one and 63 cells.");
                                foreach (TableCell cell in activeRow)
                                    for (int p = 0; p < cell.Paragraphs.Count; p++) AddParagraph(cell.Paragraphs[p], p == cell.Paragraphs.Count - 1 ? '\u0007' : '\r', true, false, 0, r < table.HeaderRowCount);
                                AddParagraph(new ParagraphBlock(), '\u0007', true, true, activeRow.Count, r < table.HeaderRowCount);
                            }
                            AddParagraph(new ParagraphBlock(), '\r', false, false, 0, false);
                        }
                    }
                    if (story.Count == 0) AddParagraph(new ParagraphBlock(), '\r', false, false, 0, false);
                    AddParagraph(new ParagraphBlock(), '\r', false, false, 0, false);
                }
            headerPositions.Add(text.Length - mainLength);
            AddParagraph(new ParagraphBlock(), '\r', false, false, 0, false);
            // The terminal CP has no story meaning, but keep the PLC monotonically ordered.
            headerPositions.Add(text.Length - mainLength + 2);
        }
        int headerLength = text.Length - mainLength;
        // MS-DOC 2.3 requires a final, non-editable paragraph outside all document
        // parts whenever a non-main part exists. It must not be counted in ccpHdd.
        if (hasStories)
        {
            int finalStart = text.Length;
            AddParagraph(new ParagraphBlock(), '\r', false, false, 0, false);
            // The special main-story terminal paragraph is formatted via this
            // document-wide guard when other document parts exist (MS-DOC 2.4.2).
            paragraphs[paragraphs.Count - 1] = new WriteRun(finalStart, text.Length, finalMainParagraph);
        }
        context.CheckCharacters(text.Length);
        byte[] encoded = Unicode.GetBytes(text.ToString());
        int textEnd = checked(TextOffset + encoded.Length);
        int firstPage = checked((textEnd + 511) / 512);
        List<byte[]> chpx = BuildPages(characters, false, context);
        List<byte[]> papx = BuildPages(paragraphs, true, context);
        int sectionOffset = checked((firstPage + chpx.Count + papx.Count) * 512);
        byte[][] sectionProperties = sections.Select(s => SectionProperties(s.Settings)).ToArray();
        byte[] word = new byte[checked(sectionOffset + sectionProperties.Sum(p => p.Length + 2))];
        W16(word, 0, 0xa5ec); W16(word, 2, 0xc1); W16(word, 6, 0x409); W16(word, 10, 0x1200); W16(word, 12, 0xbf);
        W32(word, 24, TextOffset); W32(word, 28, textEnd); W16(word, 32, 14); W16(word, 62, 22);
        W32(word, 64, word.Length); W32(word, 76, mainLength); W32(word, 84, headerLength); W16(word, 152, 93);
        Array.Copy(encoded, 0, word, TextOffset, encoded.Length);
        for (int p = 0; p < chpx.Count; p++) Array.Copy(chpx[p], 0, word, (firstPage + p) * 512, 512);
        for (int p = 0; p < papx.Count; p++) Array.Copy(papx[p], 0, word, (firstPage + chpx.Count + p) * 512, 512);

        using var tableStream = new MemoryStream();
        Add(1, WriteStyles());
        byte[] sed = new byte[4 + sections.Count * 16];
        for (int i = 0; i < sections.Count; i++)
        {
            W32(sed, i * 4, sections[i].Start);
            int entry = (sections.Count + 1) * 4 + i * 12;
            W32(sed, entry + 2, sectionOffset); W32(sed, entry + 8, -1);
            W16(word, sectionOffset, sectionProperties[i].Length);
            Array.Copy(sectionProperties[i], 0, word, sectionOffset + 2, sectionProperties[i].Length);
            sectionOffset += sectionProperties[i].Length + 2;
        }
        W32(sed, sections.Count * 4, mainLength); Add(6, sed);
        if (hasStories)
        {
            byte[] positions = new byte[headerPositions.Count * 4];
            for (int i = 0; i < headerPositions.Count; i++) W32(positions, i * 4, headerPositions[i]);
            Add(11, positions);
        }
        Add(12, WritePageIndex(chpx, firstPage));
        Add(13, WritePageIndex(papx, firstPage + chpx.Count));
        byte[] fontName = Unicode.GetBytes("Noto Sans\0"); byte[] fonts = new byte[44 + fontName.Length];
        W16(fonts, 0, 1); fonts[4] = (byte)(39 + fontName.Length); fonts[5] = 0x26; W16(fonts, 6, 400);
        Array.Copy(fontName, 0, fonts, 44, fontName.Length); Add(15, fonts);
        byte[] dop = new byte[500]; if (sections.Any(s => s.Settings.EvenAndOdd)) dop[0] = 1; Add(31, dop);
        WriteFields(16, fieldMarks.Where(f => f.Position < mainLength).ToArray(), 0, mainLength);
        WriteFields(17, fieldMarks.Where(f => f.Position >= mainLength).ToArray(), mainLength, headerLength);
        byte[] clx = new byte[21]; clx[0] = 2; W32(clx, 1, 16); W32(clx, 9, text.Length); W32(clx, 15, TextOffset); Add(33, clx);
        if (lists.Count > 0)
        {
            Add(73, WriteLists(lists)); W32(word, 158 + 73 * 8, 2 + lists.Count * 28);
            Add(74, WriteListOverrides(lists.Count));
        }
        using var root = RootStorage.Create(output, OpenMcdf.Version.V3, StorageModeFlags.LeaveOpen);
        WriteStream(root, "WordDocument", word); WriteStream(root, "1Table", tableStream.ToArray());
        if (data.Length > 0) WriteStream(root, "Data", data.ToArray());
        WriteMetadata(root, model, context);
        root.Flush();

        void Add(int index, byte[] bytes)
        {
            W32(word, 154 + index * 8, checked((int)tableStream.Position)); W32(word, 158 + index * 8, bytes.Length);
            tableStream.Write(bytes, 0, bytes.Length);
        }

        void WriteFields(int index, (int Position, byte Mark, byte Type)[] marks, int cpBase, int length)
        {
            if (marks.Length == 0) return;
            byte[] plc = new byte[(marks.Length + 1) * 4 + marks.Length * 2];
            for (int i = 0; i < marks.Length; i++)
            {
                W32(plc, i * 4, marks[i].Position - cpBase);
                plc[(marks.Length + 1) * 4 + i * 2] = marks[i].Mark; plc[(marks.Length + 1) * 4 + i * 2 + 1] = marks[i].Type;
            }
            W32(plc, marks.Length * 4, length); Add(index, plc);
        }

        void FieldMark(char mark, byte type)
        {
            int start = text.Length; fieldMarks.Add((start, (byte)mark, type)); text.Append(mark);
            characters.Add(new WriteRun(start, text.Length, Sprm(0x0855, 1)));
        }

        void AddParagraph(ParagraphBlock paragraph, char terminator, bool inTable, bool rowEnd, int columns, bool header)
        {
            int start = text.Length;
            if (paragraph.IsCodeBlock) context.Loss("Doc.Code", "Code block semantics are not retained in binary Word output.");
            foreach (TextRun run in paragraph.Runs)
            {
                if (run.Code) context.Loss("Doc.Code", "Inline code semantics are not retained in binary Word output.");
                if (run.Text.Any(c => char.IsControl(c) && c is not '\n' and not '\t')) throw new InvalidDataException("Doc.Text: Text contains reserved DOC control characters.");
                string? instruction = run.FieldInstruction;
                if (run.Link != null && context.SafeLink(run.Link) is string link)
                    instruction = "HYPERLINK \"" + new Uri(link, UriKind.Absolute).AbsoluteUri + "\"";
                if (instruction != null)
                {
                    byte type = DocumentFields.Kind(instruction) switch
                    { "PAGE" => 33, "NUMPAGES" => 26, "SECTIONPAGES" => 66, "DATE" => 31, "TIME" => 32, "TITLE" => 15, "AUTHOR" => 17, "HYPERLINK" => 88, _ => 0 };
                    FieldMark('\u0013', type);
                    int instructionStart = text.Length; text.Append(instruction);
                    if (text.Length > instructionStart) characters.Add(new WriteRun(instructionStart, text.Length, Array.Empty<byte>()));
                    FieldMark('\u0014', 0);
                }
                int runStart = text.Length;
                if (run.Image != null)
                {
                    byte[] picture = WritePicture(run.Image, imageId++, context); int offset = checked((int)data.Position); data.Write(picture, 0, picture.Length);
                    text.Append('\u0001'); characters.Add(new WriteRun(runStart, text.Length, Join(Sprm(0x0855, 1), SprmValue(0x6a03, offset, 4))));
                    if (instruction != null) FieldMark('\u0015', 0x80);
                    continue;
                }
                text.Append(run.Text.Replace('\n', '\v'));
                if (text.Length != runStart) characters.Add(new WriteRun(runStart, text.Length,
                    Join(Sprm(0x0835, run.Bold ? 1 : 0), Sprm(0x0836, run.Italic ? 1 : 0),
                        run.FontSize.HasValue ? SprmValue(0x4a43, checked((int)Math.Round(run.FontSize.Value * 2)), 2) : Array.Empty<byte>(),
                        run.Color == null ? Array.Empty<byte>() : Join(new byte[] { 0x70, 0x68 }, Enumerable.Range(0, 3).Select(i => Convert.ToByte(run.Color.Substring(i * 2, 2), 16)).ToArray(), new byte[1]))));
                if (instruction != null) FieldMark('\u0015', 0x80); // grffldEnd.fHasSep
            }
            int mark = text.Length; text.Append(terminator);
            characters.Add(new WriteRun(mark, text.Length, Array.Empty<byte>()));
            var properties = new List<byte>();
            properties.Add((byte)paragraph.HeadingLevel); properties.Add(0);
            properties.AddRange(Sprm(0x2403, (int)paragraph.Alignment));
            properties.AddRange(ParagraphProperties(paragraph.Layout));
            if (paragraph.List != null)
            {
                properties.AddRange(Sprm(0x260a, paragraph.List.Level));
                properties.AddRange(SprmValue(0x460b, lists.FindIndex(l => l.Key == paragraph.List.Id) + 1, 2));
            }
            if (inTable) properties.AddRange(Sprm(0x2416, 1));
            if (rowEnd)
            {
                properties.AddRange(Sprm(0x2417, 1));
                if (header) properties.AddRange(Sprm(0x3404, 1));
                if (cantSplit) properties.AddRange(Sprm(0x3403, 1));
                if (activeTable!.Width is double preferredWidth)
                {
                    byte[] preferred = new byte[5]; W16(preferred, 0, 0xf614); preferred[2] = 3;
                    W16(preferred, 3, checked((int)Math.Round(preferredWidth * 20))); properties.AddRange(preferred);
                }
                // TC80 is optional. Boundaries preserve absolute widths where available.
                bool merged = activeRow!.Any(c => c.Merge != VerticalMerge.None);
                int definitions = 1 + (columns + 1) * 2;
                byte[] operand = new byte[definitions + (merged ? columns * 20 : 0)]; operand[0] = (byte)columns;
                double edge = activeTable.LeftIndent * 20;
                if (edge < -31680 || edge > 31680) throw new NotSupportedException("Doc.TableWidth: Table boundaries exceed binary Word limits.");
                W16(operand, 1, checked((int)Math.Round(edge)));
                int gridColumn = 0;
                for (int c = 0; c < columns; c++)
                {
                    TableCell cell = activeRow![c];
                    double width = cell.CellWidth ?? (gridColumn + cell.ColumnSpan <= activeTable!.ColumnWidths.Count
                        ? activeEdges[gridColumn + cell.ColumnSpan] - activeEdges[gridColumn] : (activeTable.Width ?? 450) * cell.ColumnSpan / activeColumns);
                    gridColumn += cell.ColumnSpan;
                    edge += width * 20;
                    if (width <= 0 || edge > 31680) throw new NotSupportedException("Doc.TableWidth: Table boundaries exceed binary Word limits.");
                    W16(operand, 1 + (c + 1) * 2, checked((int)Math.Round(edge)));
                    if (merged) W16(operand, definitions + c * 20, cell.Merge == VerticalMerge.Start ? 0x60 : cell.Merge == VerticalMerge.Continue ? 0x20 : 0);
                }
                properties.Add(0x08); properties.Add(0xd6);
                properties.Add((byte)(operand.Length + 1)); properties.Add((byte)((operand.Length + 1) >> 8)); properties.AddRange(operand);
                for (int c = 0; c < columns; c++)
                    if (activeRow![c].Padding is CellPadding padding)
                    {
                        Pad(1, padding.Top); Pad(2, padding.Left); Pad(4, padding.Bottom); Pad(8, padding.Right);
                        void Pad(int side, double value)
                        {
                            int twips = checked((int)Math.Round(value * 20));
                            properties.AddRange(new byte[] { 0x32, 0xd6, 6, (byte)c, (byte)(c + 1), (byte)side, 3, (byte)twips, (byte)(twips >> 8) });
                        }
                    }
            }
            if (properties.Count > 480)
            {
                int offset = checked((int)data.Position), size = properties.Count - 2;
                data.WriteByte((byte)size); data.WriteByte((byte)(size >> 8)); byte[] bytes = properties.Skip(2).ToArray(); data.Write(bytes, 0, bytes.Length);
                properties.RemoveRange(2, properties.Count - 2); properties.AddRange(SprmValue(0x6646, offset, 4));
            }
            paragraphs.Add(new WriteRun(start, text.Length, properties.ToArray()));
        }
    }

    private static List<byte[]> BuildPages(List<WriteRun> runs, bool paragraph, ConversionContext context)
    {
        var result = new List<byte[]>(); int index = 0;
        while (index < runs.Count)
        {
            context.CheckCancellation();
            var records = new List<byte[]>(); int available = 510, count = 0;
            while (index + count < runs.Count && count < (paragraph ? 29 : 101))
            {
                byte[] data = runs[index + count].Properties;
                byte[] record;
                if (paragraph)
                {
                    // Use the even-length variant: cb=0, cb'=word count, istd+grpprl.
                    int size = data.Length + (data.Length & 1);
                    if (size > 488) throw new NotSupportedException("Doc.Properties: Paragraph properties exceed a formatting page.");
                    record = new byte[size + 2]; record[1] = (byte)(size / 2); Array.Copy(data, 0, record, 2, data.Length);
                }
                else
                {
                    if (data.Length > 255) throw new NotSupportedException("Doc.Properties: Character properties exceed a formatting record.");
                    record = data.Length == 0 ? Array.Empty<byte>() : new byte[data.Length + 1];
                    if (record.Length > 0) { record[0] = (byte)data.Length; Array.Copy(data, 0, record, 1, data.Length); }
                }
                int next = record.Length == 0 ? available : (available - record.Length) & ~1;
                int indexBytes = (count + 2) * 4 + (count + 1) * (paragraph ? 13 : 1);
                if (next < indexBytes) break;
                available = next; records.Add(record); count++;
            }
            if (count == 0) throw new NotSupportedException("Doc.Properties: A formatting record cannot fit in a page.");
            byte[] page = new byte[512]; int cursor = 510;
            for (int i = 0; i < count; i++)
            {
                W32(page, i * 4, checked(TextOffset + runs[index + i].Start * 2));
                if (records[i].Length > 0)
                {
                    cursor = (cursor - records[i].Length) & ~1;
                    Array.Copy(records[i], 0, page, cursor, records[i].Length);
                    page[(count + 1) * 4 + i * (paragraph ? 13 : 1)] = (byte)(cursor / 2);
                }
            }
            W32(page, count * 4, checked(TextOffset + runs[index + count - 1].End * 2)); page[511] = (byte)count;
            result.Add(page); index += count;
        }
        return result;
    }

    private static byte[] WritePageIndex(List<byte[]> pages, int firstPage)
    {
        byte[] index = new byte[4 + pages.Count * 8];
        for (int p = 0; p < pages.Count; p++)
        {
            W32(index, p * 4, I32(pages[p], 0));
            W32(index, (pages.Count + 1) * 4 + p * 4, firstPage + p);
        }
        byte[] last = pages[pages.Count - 1]; W32(index, pages.Count * 4, I32(last, last[511] * 4));
        return index;
    }

    private static byte[] WriteStyles()
    {
        using var stream = new MemoryStream();
        byte[] header = new byte[20]; W16(header, 0, 18); W16(header, 2, 15); W16(header, 4, 10);
        W16(header, 6, 1); W16(header, 8, 91); W16(header, 10, 15); stream.Write(header, 0, header.Length);
        for (int index = 0; index < 15; index++)
        {
            if (index > 6 && index != 10) { stream.WriteByte(0); stream.WriteByte(0); continue; }
            bool character = index == 10;
            string name = character ? "Default Paragraph Font" : index == 0 ? "Normal" : "heading " + index;
            byte[] nameBytes = Unicode.GetBytes(name + "\0");
            byte[] papx = index == 0 ? new byte[2] : Join(new byte[] { (byte)index, 0 }, Sprm(0x2640, index - 1));
            byte[] chpx = index is >= 1 and <= 6 ? Join(Sprm(0x0835, 1), new byte[] { 0x43, 0x4a, (byte)(52 - index * 4), 0 }) : Array.Empty<byte>();
            using var content = new MemoryStream();
            byte[] basic = new byte[12]; W16(basic, 0, character ? 65 : index);
            W16(basic, 2, (index == 0 || character ? 0xfff0 : 0) | (character ? 2 : 1));
            W16(basic, 4, character ? (10 << 4) | 1 : 2); W16(basic, 10, name.Length);
            content.Write(basic, 0, basic.Length); content.Write(nameBytes, 0, nameBytes.Length);
            if (!character) UpX(papx);
            UpX(chpx);
            byte[] bytes = content.ToArray(); W16(bytes, 6, bytes.Length);
            stream.WriteByte((byte)bytes.Length); stream.WriteByte((byte)(bytes.Length >> 8)); stream.Write(bytes, 0, bytes.Length);

            void UpX(byte[] bytes)
            {
                content.WriteByte((byte)bytes.Length); content.WriteByte((byte)(bytes.Length >> 8)); content.Write(bytes, 0, bytes.Length);
                if ((bytes.Length & 1) != 0) content.WriteByte(0);
            }
        }
        return stream.ToArray();
    }

    private static byte[] Sprm(int code, int value) => new byte[] { (byte)code, (byte)(code >> 8), (byte)value };
    private static byte[] SprmValue(int code, int value, int bytes)
    {
        byte[] result = new byte[2 + bytes]; W16(result, 0, code);
        for (int i = 0; i < bytes; i++) result[i + 2] = (byte)(value >> (8 * i));
        return result;
    }
    private static byte[] Join(params byte[][] arrays) => arrays.SelectMany(a => a).ToArray();
}
