using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Formats;

// Independently authored Palm database / MOBI6 / standalone KF8 adapter.
// Binary field definitions and PalmDOC coding are documented by MobileRead's MOBI reference.
internal static class KindleFormat
{
    internal static void Write(DocumentModel model, Stream output, bool kf8, ConversionContext context)
    {
        byte[] html = EbookContent.Write(model, context);
        var records = new List<byte[]> { Array.Empty<byte>() };
        for (int offset = 0; offset < html.Length; offset += 4096) records.Add(html.Skip(offset).Take(Math.Min(4096, html.Length - offset)).ToArray());
        int textRecords = records.Count - 1;
        int skeleton = -1, fdst = -1;
        if (kf8)
        {
            skeleton = records.Count;
            records.AddRange(SkeletonIndex(html.Length));
            fdst = records.Count;
            byte[] flows = new byte[20]; Magic(flows, 0, "FDST"); Put32(flows, 4, 12); Put32(flows, 8, 1); Put32(flows, 16, html.Length); records.Add(flows);
        }
        int flis = records.Count;
        byte[] flisData = new byte[36]; Magic(flisData, 0, "FLIS"); Put32(flisData, 4, 8); Put16(flisData, 8, 65); Put32(flisData, 16, -1); Put16(flisData, 20, 1); Put16(flisData, 22, 3); Put32(flisData, 24, 3); Put32(flisData, 28, 1); Put32(flisData, 32, -1); records.Add(flisData);
        int fcis = records.Count;
        byte[] fcisData = new byte[44]; Magic(fcisData, 0, "FCIS"); Put32(fcisData, 4, 20); Put32(fcisData, 8, 16); Put32(fcisData, 12, 1); Put32(fcisData, 20, html.Length); Put32(fcisData, 28, 32); Put32(fcisData, 32, 8); Put16(fcisData, 36, 1); Put16(fcisData, 38, 1); records.Add(fcisData);
        records.Add(new byte[] { 0xe9, 0x8e, 0x0d, 0x0a });
        int mobiLength = kf8 ? 264 : 232;
        byte[] header = new byte[16 + mobiLength + 12];
        Put16(header, 0, 1); Put32(header, 4, html.Length); Put16(header, 8, textRecords); Put16(header, 10, 4096);
        Magic(header, 16, "MOBI"); Put32(header, 20, mobiLength); Put32(header, 24, 2); Put32(header, 28, 65001); Put32(header, 32, 1); Put32(header, 36, kf8 ? 8 : 6);
        for (int i = 40; i <= 76; i += 4) Put32(header, i, -1);
        Put32(header, 80, textRecords + 1); Put32(header, 84, 16 + mobiLength); Put32(header, 88, 8); Put32(header, 92, 0x409); Put32(header, 104, kf8 ? 8 : 6); Put32(header, 108, -1);
        Put32(header, 164, -1); Put32(header, 168, -1);
        if (kf8) Put32(header, 192, fdst); else { Put16(header, 192, 1); Put16(header, 194, textRecords); }
        Put32(header, 196, 1); Put32(header, 200, fcis); Put32(header, 204, 1); Put32(header, 208, flis); Put32(header, 212, 1);
        Put32(header, 224, -1); Put32(header, 232, -1); Put32(header, 236, -1); Put32(header, 244, -1);
        if (kf8) { Put32(header, 248, -1); Put32(header, 252, skeleton); Put32(header, 256, -1); Put32(header, 260, -1); }
        Magic(header, 16 + mobiLength, "Document"); records[0] = header;
        if (records.Count > ushort.MaxValue || records.Count > context.Options.MaximumArchiveEntries) throw new InvalidDataException("Limit.ArchiveEntries: Ebook record count exceeds the limit.");
        byte[] pdb = new byte[78 + records.Count * 8 + 2];
        Magic(pdb, 0, "Document"); Put16(pdb, 34, 0); Magic(pdb, 60, "BOOKMOBI"); Put32(pdb, 68, records.Count + 1); Put16(pdb, 76, records.Count);
        int position = pdb.Length;
        for (int i = 0; i < records.Count; i++) { Put32(pdb, 78 + i * 8, position); Put32(pdb, 82 + i * 8, i * 2); position = checked(position + records[i].Length); }
        output.Write(pdb, 0, pdb.Length);
        foreach (byte[] record in records) { context.CheckCancellation(); output.Write(record, 0, record.Length); }
    }

    internal static DocumentModel Read(MemoryStream input, bool kf8, ConversionContext context)
    {
        byte[] data = input.ToArray(); Bounds(data, 0, 78);
        if (Ascii(data, 60, 8) != "BOOKMOBI") throw new InvalidDataException("Kindle.Header: Not a MOBI Palm database.");
        int count = Get16(data, 76);
        if (count < 2 || count > context.Options.MaximumArchiveEntries) throw new InvalidDataException("Limit.ArchiveEntries: Invalid ebook record count.");
        Bounds(data, 78, count * 8);
        var records = new List<byte[]>();
        int minimum = 78 + count * 8;
        for (int i = 0; i < count; i++)
        {
            int start = Get32(data, 78 + i * 8), end = i + 1 < count ? Get32(data, 86 + i * 8) : data.Length;
            if (start < minimum || end < start) throw new InvalidDataException("Kindle.Records: Invalid record offsets.");
            Bounds(data, start, end - start); byte[] record = new byte[end - start]; Array.Copy(data, start, record, 0, record.Length); records.Add(record); minimum = end;
        }
        byte[] header = records[0]; Bounds(header, 0, 248);
        if (Ascii(header, 16, 4) != "MOBI") throw new InvalidDataException("Kindle.Header: MOBI header is missing.");
        int version = Get32(header, 36);
        if (version != (kf8 ? 8 : 6)) throw new NotSupportedException("Kindle.Version: Only MOBI6 and standalone KF8 match these input formats; hybrid books are unsupported.");
        if (Get16(header, 12) != 0 || Get32(header, 172) > 0) throw new NotSupportedException("Kindle.Encryption: DRM-protected books are unsupported.");
        if (Get32(header, 28) != 65001) throw new NotSupportedException("Kindle.Encoding: Only UTF-8 ebooks are currently supported.");
        int compression = Get16(header, 0), textCount = Get16(header, 8), textLength = Get32(header, 4);
        if (Get16(header, 10) != 4096) throw new NotSupportedException("Kindle.RecordSize: Only 4096-byte text records are supported.");
        if (compression != 1 && compression != 2) throw new NotSupportedException("Kindle.Compression: HUFF/CDIC compression is not implemented.");
        if (textCount < 1 || textCount >= records.Count || textLength < 0 || textLength > context.Options.MaximumExpandedBytes) throw new InvalidDataException("Kindle.Text: Invalid or oversized text.");
        using var text = new BoundedMemoryStream(context.Options.MaximumExpandedBytes);
        int extra = Get16(header, 242);
        for (int r = 1; r <= textCount; r++)
        {
            context.CheckCancellation(); byte[] record = records[r]; int usable = StripTrailing(record, extra);
            byte[] decoded = compression == 1 ? record.Take(usable).ToArray() : PalmDoc(record, usable);
            if (decoded.Length > 4096) throw new InvalidDataException("Limit.ExpandedBytes: Text record exceeds 4096 bytes.");
            text.Write(decoded, 0, decoded.Length);
        }
        if (text.Length != textLength) throw new InvalidDataException("Kindle.Text: Uncompressed length does not match the header.");
        byte[] content = text.ToArray();
        if (kf8)
        {
            var model = new DocumentModel();
            foreach (byte[] chapter in Reassemble(records, header, content, context)) model.Blocks.AddRange(EbookContent.Read(chapter, context).Blocks);
            return model;
        }
        return EbookContent.Read(content, context);
    }

    private static List<byte[]> Reassemble(List<byte[]> records, byte[] header, byte[] raw, ConversionContext context)
    {
        Bounds(header, 248, 16);
        int skeleton = Get32(header, 252), fragment = Get32(header, 248);
        if (skeleton < 0) throw new InvalidDataException("Kindle.Skeleton: KF8 requires a skeleton index.");
        var skeletons = ReadIndex(records, skeleton, context);
        var fragments = fragment < 0 ? new List<IndexEntry>() : ReadIndex(records, fragment, context);
        var output = new List<byte[]>();
        long outputLength = 0;
        int frag = 0;
        foreach (IndexEntry item in skeletons)
        {
            int fragmentCount = Value(item, 1, 0), start = Value(item, 6, 0), length = Value(item, 6, 1);
            Bounds(raw, start, length);
            var assembled = raw.Skip(start).Take(length).ToList();
            int fragmentStart = checked(start + length);
            for (int i = 0; i < fragmentCount; i++)
            {
                context.CheckCancellation();
                if (frag >= fragments.Count) throw new InvalidDataException("Kindle.Fragment: Missing KF8 fragment.");
                IndexEntry piece = fragments[frag++];
                if (!int.TryParse(piece.Label, NumberStyles.None, CultureInfo.InvariantCulture, out int insertion)) throw new InvalidDataException("Kindle.Fragment: Invalid insertion point.");
                insertion -= start;
                int pieceLength = Value(piece, 6, 1);
                Bounds(raw, fragmentStart, pieceLength);
                if (insertion < 0 || insertion > assembled.Count || (long)assembled.Count + pieceLength > context.Options.MaximumExpandedBytes) throw new InvalidDataException("Kindle.Fragment: Invalid fragment bounds.");
                assembled.InsertRange(insertion, raw.Skip(fragmentStart).Take(pieceLength)); fragmentStart += pieceLength;
            }
            byte[] chapter = assembled.ToArray(); outputLength += chapter.Length;
            if (outputLength > context.Options.MaximumExpandedBytes) throw new InvalidDataException("Limit.ExpandedBytes: Reassembled KF8 exceeds the byte limit.");
            output.Add(chapter);
        }
        if (frag != fragments.Count) throw new InvalidDataException("Kindle.Fragment: Unreferenced fragments.");
        if (Get32(header, 196) > 1) context.Loss("Kindle.Flows", "Additional CSS, SVG and resource flows are not retained.");
        return output;
    }

    private static List<IndexEntry> ReadIndex(List<byte[]> records, int recordNumber, ConversionContext context)
    {
        byte[] meta = Record(records, recordNumber); Bounds(meta, 0, 192);
        if (Ascii(meta, 0, 4) != "INDX") throw new InvalidDataException("Kindle.Index: Missing INDX record.");
        int tagStart = Get32(meta, 4), following = Get32(meta, 24); Bounds(meta, tagStart, 12);
        if (Ascii(meta, tagStart, 4) != "TAGX" || following < 1 || following > records.Count - recordNumber - 1) throw new InvalidDataException("Kindle.Index: Invalid TAGX or record count.");
        int tagLength = Get32(meta, tagStart + 4), controls = Get32(meta, tagStart + 8); Bounds(meta, tagStart, tagLength);
        if (tagLength < 12 || (tagLength - 12) % 4 != 0 || controls < 1 || controls > 16) throw new InvalidDataException("Kindle.Index: Invalid tag table.");
        var result = new List<IndexEntry>();
        for (int r = 1; r <= following; r++)
        {
            byte[] record = Record(records, recordNumber + r); Bounds(record, 0, 192);
            if (Ascii(record, 0, 4) != "INDX") throw new InvalidDataException("Kindle.Index: Missing index data.");
            int table = Get32(record, 20), entries = Get32(record, 24); Bounds(record, table, checked(4 + entries * 2));
            if (entries < 0 || entries > context.Options.MaximumCharacters || Ascii(record, table, 4) != "IDXT") throw new InvalidDataException("Kindle.Index: Invalid IDXT.");
            for (int e = 0; e < entries; e++)
            {
                context.CheckCancellation();
                int offset = Get16(record, table + 4 + e * 2), end = e + 1 < entries ? Get16(record, table + 6 + e * 2) : table;
                Bounds(record, offset, end - offset); if (offset >= end) throw new InvalidDataException("Kindle.Index: Empty index entry.");
                int labelLength = record[offset++]; Bounds(record, offset, labelLength + controls);
                string label = TextFormat.Utf8.GetString(record, offset, labelLength); offset += labelLength;
                int control = offset, cursor = offset + controls;
                var entry = new IndexEntry(label);
                var descriptors = new List<(int Tag, int Per, int Count, int Bytes)>();
                for (int t = tagStart + 12; t < tagStart + tagLength; t += 4)
                {
                    int tag = meta[t], per = meta[t + 1], mask = meta[t + 2];
                    if (meta[t + 3] == 1) { control++; continue; }
                    if (control >= offset + controls) throw new InvalidDataException("Kindle.Index: Invalid control byte.");
                    int selected = record[control] & mask;
                    if (selected == 0) continue;
                    int repeat = 1, byteLength = -1;
                    if (selected == mask && (mask & (mask - 1)) != 0) byteLength = Variable(record, ref cursor, end);
                    else { while ((mask & 1) == 0) { mask >>= 1; selected >>= 1; } repeat = selected; }
                    if (per == 0) throw new InvalidDataException("Kindle.Index: Empty tag value definition.");
                    descriptors.Add((tag, per, repeat, byteLength));
                }
                foreach (var descriptor in descriptors)
                {
                    var values = new List<int>();
                    if (descriptor.Bytes >= 0)
                    {
                        if (descriptor.Bytes > end - cursor) throw new InvalidDataException("Kindle.Index: Tag values exceed entry bounds.");
                        int stop = cursor + descriptor.Bytes;
                        while (cursor < stop) values.Add(Variable(record, ref cursor, stop));
                        if (values.Count % descriptor.Per != 0) throw new InvalidDataException("Kindle.Index: Incomplete tag values.");
                    }
                    else for (int v = 0; v < descriptor.Per * descriptor.Count; v++) values.Add(Variable(record, ref cursor, end));
                    if (entry.Values.ContainsKey(descriptor.Tag)) throw new InvalidDataException("Kindle.Index: Duplicate tag.");
                    entry.Values.Add(descriptor.Tag, values);
                }
                result.Add(entry);
            }
        }
        return result;
    }

    private static byte[][] SkeletonIndex(int length)
    {
        byte[] tag = { 84, 65, 71, 88, 0, 0, 0, 24, 0, 0, 0, 1, 1, 1, 1, 0, 6, 2, 2, 0, 0, 0, 0, 1 };
        byte[] meta = new byte[228]; Magic(meta, 0, "INDX"); Put32(meta, 4, 192); Put32(meta, 12, 2); Put32(meta, 20, 220); Put32(meta, 24, 1); Put32(meta, 28, 65001); Put32(meta, 32, -1); Put32(meta, 36, 1);
        Array.Copy(tag, 0, meta, 192, tag.Length); meta[216] = 1; meta[217] = (byte)'0'; Put16(meta, 218, 1); Magic(meta, 220, "IDXT"); Put16(meta, 224, 216);
        var entry = new List<byte> { 1, (byte)'0', 3, 0x80, 0x80 }; entry.AddRange(EncodeVariable(length));
        int idxt = (192 + entry.Count + 3) & ~3;
        byte[] body = new byte[idxt + 8]; Magic(body, 0, "INDX"); Put32(body, 4, 192); Put32(body, 20, idxt); Put32(body, 24, 1); Put32(body, 28, 65001); Put32(body, 32, -1);
        entry.CopyTo(body, 192); Magic(body, idxt, "IDXT"); Put16(body, idxt + 4, 192);
        return new[] { meta, body };
    }

    private static byte[] PalmDoc(byte[] input, int length)
    {
        var result = new List<byte>();
        for (int i = 0; i < length; i++)
        {
            int value = input[i];
            if (value is >= 1 and <= 8) { if (i + value >= length) throw new InvalidDataException("Kindle.Compression: Truncated literal run."); for (int k = 0; k < value; k++) result.Add(input[++i]); }
            else if (value < 128) result.Add((byte)value);
            else if (value >= 192) { result.Add(32); result.Add((byte)(value ^ 0x80)); }
            else
            {
                if (++i >= length) throw new InvalidDataException("Kindle.Compression: Truncated back reference.");
                int pair = ((value & 63) << 8) | input[i], distance = pair >> 3, count = (pair & 7) + 3;
                if (distance <= 0 || distance > result.Count) throw new InvalidDataException("Kindle.Compression: Invalid back reference.");
                for (int k = 0; k < count; k++) result.Add(result[result.Count - distance]);
            }
            if (result.Count > 4096) throw new InvalidDataException("Limit.ExpandedBytes: PalmDOC record exceeds 4096 bytes.");
        }
        return result.ToArray();
    }

    private static int StripTrailing(byte[] bytes, int flags)
    {
        int end = bytes.Length;
        for (int bits = flags >> 1; bits != 0; bits >>= 1)
        {
            if ((bits & 1) == 0) continue;
            int length = 0, shift = 0, cursor = end;
            while (true) { if (cursor == 0 || shift > 21) throw new InvalidDataException("Kindle.Trailer: Invalid trailer size."); byte b = bytes[--cursor]; length |= (b & 127) << shift; if ((b & 128) != 0) break; shift += 7; }
            if (length < end - cursor || length > end) throw new InvalidDataException("Kindle.Trailer: Invalid trailer bounds."); end -= length;
        }
        if ((flags & 1) != 0) { if (end == 0) throw new InvalidDataException("Kindle.Trailer: Missing overlap marker."); end -= (bytes[end - 1] & 3) + 1; }
        if (end < 0) throw new InvalidDataException("Kindle.Trailer: Invalid overlap."); return end;
    }

    private static int Variable(byte[] bytes, ref int cursor, int end)
    {
        int result = 0;
        for (int count = 0; count < 5 && cursor < end; count++) { byte b = bytes[cursor++]; result = checked(result * 128 + (b & 127)); if ((b & 128) != 0) return result; }
        throw new InvalidDataException("Kindle.Index: Invalid variable integer.");
    }

    private static byte[] EncodeVariable(int number) { var bytes = new List<byte> { (byte)((number & 127) | 128) }; while ((number >>= 7) > 0) bytes.Insert(0, (byte)(number & 127)); return bytes.ToArray(); }
    private static int Value(IndexEntry entry, int tag, int index) => entry.Values.TryGetValue(tag, out List<int>? values) && index < values.Count ? values[index] : throw new InvalidDataException("Kindle.Index: Required tag is missing.");
    private static byte[] Record(List<byte[]> records, int number) => number >= 0 && number < records.Count ? records[number] : throw new InvalidDataException("Kindle.Record: Invalid record reference.");
    private static string Ascii(byte[] bytes, int offset, int length) { Bounds(bytes, offset, length); return Encoding.ASCII.GetString(bytes, offset, length); }
    private static int Get16(byte[] bytes, int offset) { Bounds(bytes, offset, 2); return (bytes[offset] << 8) | bytes[offset + 1]; }
    private static int Get32(byte[] bytes, int offset) { Bounds(bytes, offset, 4); return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3]; }
    private static void Put16(byte[] bytes, int offset, int value) { bytes[offset] = (byte)(value >> 8); bytes[offset + 1] = (byte)value; }
    private static void Put32(byte[] bytes, int offset, int value) { for (int i = 0; i < 4; i++) bytes[offset + i] = (byte)(value >> (24 - 8 * i)); }
    private static void Magic(byte[] bytes, int offset, string text) => Encoding.ASCII.GetBytes(text).CopyTo(bytes, offset);
    private static void Bounds(byte[] bytes, int offset, int count) { if (offset < 0 || count < 0 || offset > bytes.Length - count) throw new InvalidDataException("Kindle.Bounds: Invalid offset or length."); }
    private sealed class IndexEntry
    {
        internal IndexEntry(string label) { Label = label; }
        internal string Label { get; }
        internal Dictionary<int, List<int>> Values { get; } = new Dictionary<int, List<int>>();
    }
}
