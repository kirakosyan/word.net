using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using OpenMcdf;

namespace WordNet.Tests;

public sealed partial class ImportResourceTests
{
    // Authored from MS-DOC FIB/CLX/FKP/PICF, ISO 32000 streams and the KF8 index
    // layouts. These adversarial inputs do not use the production document writers.
    private static byte[] Doc(string text, byte[] properties, byte[] data, bool picture = false)
    {
        int page = (2048 + text.Length + 511) / 512;
        byte[] word = new byte[(page + (picture ? 2 : 1)) * 512], table = new byte[65];
        Le16(word, 0, 0xa5ec); Le16(word, 2, 0xc1); Le16(word, 10, 0x200);
        Le16(word, 32, 14); Le16(word, 62, 22); Le16(word, 152, 93); Le32(word, 76, text.Length);
        Encoding.ASCII.GetBytes(text).CopyTo(word, 2048);
        Le32(word, 162, 0); Le32(word, 166, 20); Le16(table, 0, 18); Le16(table, 4, 10);
        Le32(word, 418, 20); Le32(word, 422, 21);
        table[20] = 2; Le32(table, 21, 16); Le32(table, 29, text.Length); Le32(table, 35, 0x40001000);
        Le32(word, 258, 41); Le32(word, 262, 12);
        Le32(table, 41, 2048); Le32(table, 45, 2048 + text.Length); Le32(table, 49, page);
        int fkp = page * 512;
        Le32(word, fkp, 2048); Le32(word, fkp + 4, 2048 + text.Length); word[fkp + 8] = 32; word[fkp + 511] = 1;
        word[fkp + 64] = 0; word[fkp + 65] = (byte)((properties.Length + 2) / 2);
        properties.CopyTo(word, fkp + 68);
        if (picture)
        {
            Le32(word, 250, 53); Le32(word, 254, 12);
            Le32(table, 53, 2048); Le32(table, 57, 2048 + text.Length); Le32(table, 61, page + 1);
            fkp += 512; Le32(word, fkp, 2048); Le32(word, fkp + 4, 2048 + text.Length);
            word[fkp + 8] = 32; word[fkp + 511] = 1; word[fkp + 64] = 9;
            Le16(word, fkp + 65, 0x0855); word[fkp + 67] = 1; Le16(word, fkp + 68, 0x6a03);
        }
        using var memory = new MemoryStream();
        using (var root = RootStorage.Create(memory, OpenMcdf.Version.V3, StorageModeFlags.LeaveOpen))
        {
            foreach (var entry in new[] { ("WordDocument", word), ("1Table", table), ("Data", data) })
            { using CfbStream stream = root.CreateStream(entry.Item1); stream.Write(entry.Item2, 0, entry.Item2.Length); }
        }
        return memory.ToArray();
    }

    private static byte[] Pointer(int code, int offset)
    { byte[] bytes = new byte[6]; Le16(bytes, 0, code); Le32(bytes, 2, offset); return bytes; }

    private static byte[] Picture(byte[] png)
    {
        byte[] data = new byte[68 + 8 + 17 + png.Length];
        Le32(data, 0, data.Length); Le16(data, 4, 68); Le16(data, 6, 100);
        Le16(data, 28, 1440); Le16(data, 30, 1440); Le16(data, 32, 1000); Le16(data, 34, 1000);
        Le16(data, 68, 0x6e00); Le16(data, 70, 0xf01e); Le32(data, 72, 17 + png.Length); data[92] = 255; png.CopyTo(data, 93);
        return data;
    }

    private static byte[] PaddedPng(int padding)
    {
        byte[] png = FidelityDocuments.Fixture("color-grid.png");
        byte[] chunk = new byte[padding + 12]; Be32(chunk, 0, padding); Encoding.ASCII.GetBytes("tEXt").CopyTo(chunk, 4);
        uint crc = uint.MaxValue;
        for (int i = 4; i < chunk.Length - 4; i++)
        {
            crc ^= chunk[i];
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
        }
        Be32(chunk, chunk.Length - 4, unchecked((int)~crc));
        using var output = new MemoryStream(); output.Write(png, 0, 33); output.Write(chunk, 0, chunk.Length); output.Write(png, 33, png.Length - 33);
        return output.ToArray();
    }

    private static byte[] WithParts(byte[] source, params (string Path, byte[] Bytes)[] parts)
    {
        using var output = new MemoryStream(); output.Write(source, 0, source.Length);
        using (var zip = new ZipArchive(output, ZipArchiveMode.Update, true))
            foreach (var part in parts) FidelityDocuments.Add(zip, part.Path, part.Bytes);
        return output.ToArray();
    }

    private static byte[] Epub(byte[] png)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            void Add(string path, string text) => FidelityDocuments.Add(zip, path, Encoding.UTF8.GetBytes(text));
            Add("mimetype", "application/epub+zip");
            Add("META-INF/container.xml", "<container xmlns='urn:oasis:names:tc:opendocument:xmlns:container'><rootfiles><rootfile full-path='book.opf' media-type='application/oebps-package+xml'/></rootfiles></container>");
            Add("book.opf", "<package xmlns='http://www.idpf.org/2007/opf'><manifest><item id='a' href='a.xhtml' media-type='application/xhtml+xml'/><item id='b' href='b.xhtml' media-type='application/xhtml+xml'/><item id='image' href='i.png' media-type='image/png'/></manifest><spine><itemref idref='a'/><itemref idref='b'/></spine></package>");
            foreach (string name in new[] { "a", "b" })
                Add(name + ".xhtml", "<html xmlns='http://www.w3.org/1999/xhtml'><body>" + string.Concat(Enumerable.Repeat("<p><img src='i.png' width='" + (name == "a" ? "10" : "20") + "' alt='sample'/></p>", 40)) + "</body></html>");
            FidelityDocuments.Add(zip, "i.png", png);
        }
        return output.ToArray();
    }

    private static byte[] Pdf(int expandedBytes, int pages)
    {
        // Zlib framing around a raw Deflate stream; Adler-32 covers ASCII spaces.
        byte[] spaces = Enumerable.Repeat((byte)32, 8192).ToArray();
        using var compressed = new MemoryStream(); compressed.WriteByte(0x78); compressed.WriteByte(0x9c);
        using (var deflate = new DeflateStream(compressed, CompressionLevel.Optimal, true))
            for (int offset = 0; offset < expandedBytes; offset += spaces.Length)
                deflate.Write(spaces, 0, Math.Min(spaces.Length, expandedBytes - offset));
        long count = expandedBytes % 65521L;
        long a = (1 + 32 * count) % 65521, b = (count + 16 * count * (count + 1)) % 65521;
        byte[] checksum = new byte[4]; Be32(checksum, 0, unchecked((int)((b << 16) | a))); compressed.Write(checksum, 0, 4);
        return PdfEncoded(compressed.ToArray(), "/FlateDecode", pages);
    }

    private static byte[] PdfEncoded(byte[] stream, string filter, int pages = 1, string parameters = "")
    {
        using var output = new MemoryStream(); var offsets = new List<long> { 0 };
        void Write(string value) { byte[] bytes = Encoding.ASCII.GetBytes(value); output.Write(bytes, 0, bytes.Length); }
        void Object(int id, string body) { offsets.Add(output.Position); Write(id + " 0 obj\n" + body + "\nendobj\n"); }
        Write("%PDF-1.4\n"); Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Object(2, "<< /Type /Pages /Count " + pages + " /Kids [" + string.Join(" ", Enumerable.Range(0, pages).Select(i => (3 + i * 2) + " 0 R")) + "] >>");
        for (int i = 0; i < pages; i++)
        {
            int page = 3 + i * 2;
            Object(page, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Contents " + (page + 1) + " 0 R >>");
            offsets.Add(output.Position); Write((page + 1) + " 0 obj\n<< /Length " + stream.Length + " /Filter " + filter + " " + parameters + " >>\nstream\n");
            output.Write(stream, 0, stream.Length); Write("\nendstream\nendobj\n");
        }
        long xref = output.Position; Write("xref\n0 " + offsets.Count + "\n0000000000 65535 f \n");
        foreach (long offset in offsets.Skip(1)) Write(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        Write("trailer\n<< /Size " + offsets.Count + " /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
        return output.ToArray();
    }

    private static byte[] Kindle(int skeletons, bool overlap, bool empty = false)
    {
        const int textLength = 800 * 1024;
        byte[] header = new byte[280]; Be16(header, 0, 1); Be32(header, 4, textLength); Be16(header, 8, textLength / 4096); Be16(header, 10, 4096);
        Encoding.ASCII.GetBytes("MOBI").CopyTo(header, 16); Be32(header, 20, 264); Be32(header, 28, 65001); Be32(header, 36, 8); Be32(header, 248, -1);
        var records = new List<byte[]> { header };
        for (int i = 0; i < textLength / 4096; i++) records.Add(Enumerable.Repeat((byte)'x', 4096).ToArray());
        Be32(header, 252, records.Count);
        int following = (skeletons + 999) / 1000;
        byte[] meta = new byte[216]; Encoding.ASCII.GetBytes("INDX").CopyTo(meta, 0); Be32(meta, 4, 192); Be32(meta, 24, following);
        Encoding.ASCII.GetBytes("TAGX").CopyTo(meta, 192); Be32(meta, 196, 24); Be32(meta, 200, 1);
        new byte[] { 1, 1, 1, 0, 6, 2, 2, 0, 0, 0, 0, 1 }.CopyTo(meta, 204); records.Add(meta);
        for (int first = 0; first < skeletons; first += 1000)
        {
            var bytes = new List<byte>(new byte[192]); var offsets = new List<int>();
            for (int i = first; i < Math.Min(first + 1000, skeletons); i++)
            {
                offsets.Add(bytes.Count); bytes.AddRange(new byte[] { 1, (byte)'0', 3, 0x80 });
                Variable(bytes, overlap ? textLength - 1 : textLength - skeletons + i); Variable(bytes, empty ? 0 : 1);
            }
            int idxt = bytes.Count; bytes.AddRange(Encoding.ASCII.GetBytes("IDXT"));
            foreach (int offset in offsets) { bytes.Add((byte)(offset >> 8)); bytes.Add((byte)offset); }
            byte[] record = bytes.ToArray(); Encoding.ASCII.GetBytes("INDX").CopyTo(record, 0); Be32(record, 4, 192); Be32(record, 20, idxt); Be32(record, 24, offsets.Count); records.Add(record);
        }
        byte[] pdb = new byte[78 + records.Count * 8]; Encoding.ASCII.GetBytes("BOOKMOBI").CopyTo(pdb, 60); Be16(pdb, 76, records.Count);
        int position = pdb.Length;
        for (int i = 0; i < records.Count; i++) { Be32(pdb, 78 + i * 8, position); position += records[i].Length; }
        using var result = new MemoryStream(); result.Write(pdb, 0, pdb.Length);
        foreach (byte[] record in records) result.Write(record, 0, record.Length);
        return result.ToArray();
    }

    private static void Variable(List<byte> output, int number)
    {
        var bytes = new List<byte> { (byte)((number & 127) | 128) };
        while ((number >>= 7) != 0) bytes.Insert(0, (byte)(number & 127));
        output.AddRange(bytes);
    }
    private static void Le16(byte[] bytes, int offset, int value) { bytes[offset] = (byte)value; bytes[offset + 1] = (byte)(value >> 8); }
    private static void Le32(byte[] bytes, int offset, int value) { for (int i = 0; i < 4; i++) bytes[offset + i] = (byte)(value >> (8 * i)); }
    private static void Be16(byte[] bytes, int offset, int value) { bytes[offset] = (byte)(value >> 8); bytes[offset + 1] = (byte)value; }
    private static void Be32(byte[] bytes, int offset, int value) { for (int i = 0; i < 4; i++) bytes[offset + i] = (byte)(value >> (24 - 8 * i)); }
}
