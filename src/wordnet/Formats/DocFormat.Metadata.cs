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
    private const string SummaryStream = "\u0005SummaryInformation";
    private static readonly Guid SummaryFormat = new Guid("f29f85e0-4ff9-1068-ab91-08002b27b3d9");

    // Independently authored MS-OLEPS SummaryInformation, CodePageString and
    // PropertySetStream serialization. Unicode CodePageString lengths are bytes.
    // https://learn.microsoft.com/openspecs/windows_protocols/ms-oleps/f7933d28-2cc4-4b36-bc23-8861cbcd37c4
    private static void WriteMetadata(RootStorage root, DocumentModel model, ConversionContext context)
    {
        if (model.Title.Length == 0 && model.Author.Length == 0) return;
        context.CheckCharacters(checked(model.CharacterCount + model.Title.Length + model.Author.Length));
        var codepage = new byte[8]; W32(codepage, 0, 2); W16(codepage, 4, 1200);
        var values = new[] { codepage, Property(model.Title), Property(model.Author) };
        int sectionSize = checked(8 + values.Length * 8 + values.Sum(p => p.Length));
        var bytes = new byte[checked(48 + sectionSize)];
        W16(bytes, 0, 0xfffe); W32(bytes, 4, 0x00020006); W32(bytes, 24, 1);
        Buffer.BlockCopy(SummaryFormat.ToByteArray(), 0, bytes, 28, 16); W32(bytes, 44, 48);
        W32(bytes, 48, sectionSize); W32(bytes, 52, values.Length);
        int cursor = 8 + values.Length * 8;
        int[] ids = { 1, 2, 4 };
        for (int i = 0; i < values.Length; i++)
        {
            W32(bytes, 56 + i * 8, ids[i]); W32(bytes, 60 + i * 8, cursor);
            Buffer.BlockCopy(values[i], 0, bytes, 48 + cursor, values[i].Length); cursor += values[i].Length;
        }
        WriteStream(root, SummaryStream, bytes);

        static byte[] Property(string value)
        {
            byte[] text = Unicode.GetBytes(value + "\0");
            var data = new byte[checked(8 + ((text.Length + 3) & ~3))];
            W32(data, 0, 30); W32(data, 4, text.Length); Buffer.BlockCopy(text, 0, data, 8, text.Length);
            return data;
        }
    }

    private static void ReadMetadata(RootStorage root, DocumentModel model, ConversionContext context)
    {
        if (!root.TryOpenStream(SummaryStream, out CfbStream? probe)) return;
        probe!.Dispose();
        byte[] data = ReadStream(root, SummaryStream, context);
        if (data.Length < 28 || U16(data, 0) != 0xfffe || U16(data, 2) > 1) throw MetadataError();
        int sections = I32(data, 24);
        if (sections is < 1 or > 2) throw MetadataError();
        Bounds(data, 28, sections * 20);
        for (int section = 0; section < sections; section++)
        {
            int record = 28 + section * 20;
            if (new Guid(Slice(data, record, 16)) != SummaryFormat) continue;
            int start = I32(data, record + 16); Bounds(data, start, 8);
            int length = I32(data, start), count = I32(data, start + 4);
            if (length < 8 || count < 0 || count > (length - 8) / 8) throw MetadataError();
            Bounds(data, start, length);
            if (count > 4096) throw new InvalidDataException("Limit.MetadataProperties: DOC metadata has too many properties.");
            int limit = start + length, codepage = 1252;
            var properties = new Dictionary<int, int>();
            for (int p = 0; p < count; p++)
            {
                int id = I32(data, start + 8 + p * 8), offset = I32(data, start + 12 + p * 8);
                if (offset < 8 + count * 8 || offset > length - 4 || properties.ContainsKey(id)) throw MetadataError();
                properties.Add(id, start + offset);
            }
            if (properties.TryGetValue(1, out int cp))
            {
                if (U16(data, cp) != 2 || cp > limit - 8) throw MetadataError();
                codepage = U16(data, cp + 4);
            }
            if (properties.TryGetValue(2, out int title)) model.Title = ReadProperty(title);
            if (properties.TryGetValue(4, out int author)) model.Author = ReadProperty(author);
            context.CheckCharacters(checked(model.CharacterCount + model.Title.Length + model.Author.Length));

            string ReadProperty(int p)
            {
                int type = U16(data, p);
                if (type is 0 or 1) return "";
                if (type is not 30 and not 31 and not 8)
                { context.Loss("Doc.MetadataType", "An unsupported Title/Author property type is not retained."); return ""; }
                if (p > limit - 8) throw MetadataError();
                int size = I32(data, p + 4), bytes = size;
                if (size < 0) throw MetadataError();
                if (type == 31)
                {
                    if (size > context.Options.MaximumCharacters + 1L) throw new InvalidDataException("Limit.Characters: DOC metadata exceeds the character limit.");
                    bytes = checked(size * 2);
                }
                bool unicode = type == 31 || codepage == 1200;
                if (bytes > limit - p - 8 || unicode && (bytes & 1) != 0) throw MetadataError();
                if ((unicode ? bytes / 2 : bytes) > context.Options.MaximumCharacters + 1L) throw new InvalidDataException("Limit.Characters: DOC metadata exceeds the character limit.");
                if (bytes == 0) return "";
                string text;
                if (unicode) text = Unicode.GetString(data, p + 8, bytes);
                else if (codepage == 65001) text = new UTF8Encoding(false, true).GetString(data, p + 8, bytes);
                else if (codepage == 1252)
                {
                    var builder = new StringBuilder(bytes);
                    for (int i = 0; i < bytes; i++) { byte b = data[p + 8 + i]; builder.Append(b >= 0x80 && b <= 0x9f ? Windows1252[b - 0x80] : (char)b); }
                    text = builder.ToString();
                }
                else { context.Loss("Doc.MetadataCodePage", "An unsupported Title/Author metadata code page is not retained."); return ""; }
                if (text.Length == 0 || text[text.Length - 1] != '\0') throw MetadataError();
                return text.TrimEnd('\0');
            }
        }
    }

    private static InvalidDataException MetadataError() => new InvalidDataException("Doc.Metadata: Invalid SummaryInformation property set.");
}
