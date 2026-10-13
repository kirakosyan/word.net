using System;
using System.IO;
using System.Text;
using OpenMcdf;
using WordNet.Internal;

namespace WordNet.Formats;

// Independently authored MS-DOC FIB/CLX/FKP/STSH adapter. OpenMcdf supplies CFB storage.
internal static partial class DocFormat
{
    private static readonly UnicodeEncoding Unicode = new UnicodeEncoding(false, false, true);
    private const string Windows1252 = "€\u0081‚ƒ„…†‡ˆ‰Š‹Œ\u008dŽ\u008f\u0090‘’“”•–—˜™š›œ\u009džŸ";
    private const int TextOffset = 2048;

    private static byte[] ReadStream(RootStorage root, string name, ConversionContext context)
    {
        using CfbStream stream = root.OpenStream(name);
        if (stream.Length > context.Options.MaximumExpandedBytes) throw new InvalidDataException("Limit.ExpandedBytes: DOC stream exceeds the limit.");
        byte[] bytes = new byte[checked((int)stream.Length)];
        int offset = 0;
        while (offset < bytes.Length)
        {
            context.CheckCancellation();
            int count = stream.Read(bytes, offset, Math.Min(8192, bytes.Length - offset));
            if (count == 0) throw new InvalidDataException("Doc.Truncated: Incomplete CFB stream.");
            offset += count;
        }
        return bytes;
    }

    private static void WriteStream(RootStorage root, string name, byte[] bytes)
    {
        using CfbStream stream = root.CreateStream(name);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static byte[] Slice(byte[] bytes, int offset, int length)
    {
        Bounds(bytes, offset, length);
        byte[] result = new byte[length];
        Array.Copy(bytes, offset, result, 0, length);
        return result;
    }

    private static int U16(byte[] bytes, int offset) { Bounds(bytes, offset, 2); return bytes[offset] | (bytes[offset + 1] << 8); }
    private static int I32(byte[] bytes, int offset) { Bounds(bytes, offset, 4); return bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24); }
    private static void W16(byte[] bytes, int offset, int value) { bytes[offset] = (byte)value; bytes[offset + 1] = (byte)(value >> 8); }
    private static void W32(byte[] bytes, int offset, int value) { for (int i = 0; i < 4; i++) bytes[offset + i] = (byte)(value >> (8 * i)); }
    private static void Bounds(byte[] bytes, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length - length) throw new InvalidDataException("Doc.Bounds: Invalid document offset or length.");
    }
}
