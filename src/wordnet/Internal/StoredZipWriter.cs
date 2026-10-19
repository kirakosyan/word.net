using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace WordNet.Internal;

// Write package-required stored entries explicitly: Framework's NoCompression
// still emits ZIP method 8. Other entries use the platform's raw DEFLATE stream.
internal sealed class StoredZipWriter : IDisposable
{
    private readonly BinaryWriter writer;
    private readonly ConversionContext context;
    private readonly List<(byte[] Name, ushort Method, uint Crc, uint Size, uint Compressed, uint Offset)> entries = new List<(byte[], ushort, uint, uint, uint, uint)>();
    private static readonly uint[] CrcTable = CreateCrcTable();
    private long expanded;
    internal StoredZipWriter(Stream output, ConversionContext context)
    { writer = new BinaryWriter(output, Encoding.UTF8, true); this.context = context; }
    internal void Write(string name, byte[] bytes, bool stored = false)
    {
        context.CheckCancellation(); expanded = checked(expanded + bytes.Length);
        byte[] path = Encoding.UTF8.GetBytes(name);
        if (path.Length > ushort.MaxValue || entries.Count >= Math.Min(ushort.MaxValue - 1, context.Options.MaximumArchiveEntries)
            || expanded > context.Options.MaximumExpandedBytes) throw new InvalidDataException("Package.WriteLimit: Output package exceeds its bounds.");
        uint crc = uint.MaxValue;
        for (int i = 0; i < bytes.Length; i++)
        {
            if ((i & 8191) == 0) context.CheckCancellation();
            crc = CrcTable[(crc ^ bytes[i]) & 255] ^ (crc >> 8);
        }
        crc = ~crc;
        byte[] payload = bytes;
        if (!stored)
        {
            using var compressed = new BoundedMemoryStream(context.Options.MaximumExpandedBytes);
            using (var deflate = new DeflateStream(compressed, CompressionLevel.Optimal, true))
                for (int offset = 0; offset < bytes.Length; offset += 8192)
                { context.CheckCancellation(); deflate.Write(bytes, offset, Math.Min(8192, bytes.Length - offset)); }
            payload = compressed.ToArray();
        }
        uint position = Position(); ushort method = stored ? (ushort)0 : (ushort)8;
        writer.Write(0x04034b50u); writer.Write((ushort)20); writer.Write((ushort)0x800); writer.Write(method);
        writer.Write((ushort)0); writer.Write((ushort)0x21); writer.Write(crc); writer.Write((uint)payload.Length); writer.Write((uint)bytes.Length);
        writer.Write((ushort)path.Length); writer.Write((ushort)0); writer.Write(path); writer.Write(payload);
        entries.Add((path, method, crc, (uint)bytes.Length, (uint)payload.Length, position));
    }
    internal void Complete()
    {
        uint start = Position();
        foreach (var entry in entries)
        {
            context.CheckCancellation();
            writer.Write(0x02014b50u); writer.Write((ushort)20); writer.Write((ushort)20); writer.Write((ushort)0x800); writer.Write(entry.Method);
            writer.Write((ushort)0); writer.Write((ushort)0x21); writer.Write(entry.Crc); writer.Write(entry.Compressed); writer.Write(entry.Size);
            writer.Write((ushort)entry.Name.Length); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)0);
            writer.Write(0u); writer.Write(entry.Offset); writer.Write(entry.Name);
        }
        uint size = Position() - start;
        writer.Write(0x06054b50u); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)entries.Count); writer.Write((ushort)entries.Count);
        writer.Write(size); writer.Write(start); writer.Write((ushort)0); writer.Flush();
    }
    private uint Position()
    {
        long position = writer.BaseStream.Position;
        if (position < 0 || position > uint.MaxValue) throw new InvalidDataException("Package.WriteLimit: ZIP64 output is outside the package profile.");
        return (uint)position;
    }
    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++) value = (value & 1) == 0 ? value >> 1 : 0xedb88320u ^ (value >> 1);
            table[i] = value;
        }
        return table;
    }
    public void Dispose() => writer.Dispose();
}
