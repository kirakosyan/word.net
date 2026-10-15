using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using WordNet.Internal;
using WordNet.Model;
using Xunit;

namespace WordNet.Tests;

public sealed class PngAdmissionTests
{
    [Fact]
    public void SmallDeclaredImageCannotInflateArbitrarilyLargeScanlineData()
    {
        using var png = new MemoryStream();
        byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 }; png.Write(signature, 0, signature.Length);
        byte[] header = { 0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0 }; Chunk("IHDR", header);
        using var zlib = new MemoryStream(); zlib.WriteByte(0x78); zlib.WriteByte(0x9c);
        using (var deflate = new DeflateStream(zlib, CompressionLevel.Optimal, leaveOpen: true))
        { var payload = new byte[1024 * 1024]; deflate.Write(payload, 0, payload.Length); }
        zlib.Write(new byte[] { 0, 0xf0, 0, 1 }, 0, 4); // Adler32 of 1 MiB of zeros.
        Chunk("IDAT", zlib.ToArray()); Chunk("IEND", Array.Empty<byte>());
        var context = new ConversionContext(new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        Assert.Contains("exceeds its declared dimensions", Assert.Throws<InvalidDataException>(() => DocumentImage.Read(png.ToArray(), null, null, "", context)).Message, StringComparison.Ordinal);

        void Chunk(string name, byte[] data)
        {
            byte[] type = Encoding.ASCII.GetBytes(name); Big(data.Length); png.Write(type, 0, 4); png.Write(data, 0, data.Length);
            uint crc = 0xffffffff;
            foreach (byte value in type) Update(value);
            foreach (byte value in data) Update(value);
            Big(unchecked((int)~crc));
            void Update(byte value) { crc ^= value; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u); }
        }
        void Big(int value) { png.WriteByte((byte)(value >> 24)); png.WriteByte((byte)(value >> 16)); png.WriteByte((byte)(value >> 8)); png.WriteByte((byte)value); }
    }

    [Fact]
    public void ChunkLengthIsAdmittedBeforeProviderAllocation()
    {
        byte[] png = FidelityDocuments.Fixture("color-grid.png");
        // The first chunk after the fixed IHDR cannot claim an absent 16 MiB payload.
        png[33] = 1; png[34] = 0; png[35] = 0; png[36] = 0;
        var context = new ConversionContext(new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        Assert.StartsWith("Image.Decode:", Assert.Throws<InvalidDataException>(() => DocumentImage.Read(png, null, null, "", context)).Message, StringComparison.Ordinal);
    }
}
