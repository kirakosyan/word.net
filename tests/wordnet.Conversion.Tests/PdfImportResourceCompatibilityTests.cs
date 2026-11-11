using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;
using Xunit;

namespace WordNet.Tests;

public sealed class PdfImportResourceCompatibilityTests
{
    private static readonly string[] ExpectedText = { "Ordinary scanned page" };
    [Theory]
    [Trait("Category", "Ocr")]
    [InlineData(2550, 3300, 3, true)]
    [InlineData(1700, 2200, 3, true)]
    [InlineData(1700, 2200, 6, false)]
    public void OrdinaryMultipageScansRetainDefaultAdmission(int width, int height, int pages, bool jpeg)
    {
        using var fontStream = typeof(WordConverter).Assembly.GetManifestResourceStream("WordNet.Resources.Fonts.NotoSans-Regular.ttf");
        Assert.NotNull(fontStream);
        using var fontData = SKData.Create(fontStream);
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        using (var typeface = SKTypeface.FromData(fontData))
        using (var font = new SKFont(typeface, width / 28f))
        using (var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true })
        {
            canvas.Clear(SKColors.White);
            canvas.DrawText("Ordinary scanned page", width / 12f, height / 6f, SKTextAlign.Left, font, paint);
        }
        byte[] image;
        if (jpeg)
        {
            using var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, 90); image = encoded.ToArray();
        }
        else
        {
            byte[] pixels = new byte[bitmap.ByteCount]; Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
            byte[] rgb = new byte[width * height * 3];
            for (int i = 0; i < width * height; i++)
            { rgb[i * 3] = pixels[i * 4 + 2]; rgb[i * 3 + 1] = pixels[i * 4 + 1]; rgb[i * 3 + 2] = pixels[i * 4]; }
            image = Deflate(rgb);
        }
        byte[] source = ScannedPdf(image, width, height, pages, jpeg ? "/DCTDecode" : "/FlateDecode");
        string? data = Environment.GetEnvironmentVariable("WORDNET_TESSDATA"), native = Environment.GetEnvironmentVariable("WORDNET_TESSERACT_LIBRARY");
        Assert.False(string.IsNullOrEmpty(data) || string.IsNullOrEmpty(native), "Provision English OCR before testing.");
        var options = new ConversionOptions(FidelityMode.BestEffort, englishOcr: new EnglishOcrOptions(data!, native!));
        using var input = new MemoryStream(source); using var output = new MemoryStream();
        new WordConverter().Convert(input, output, DocumentFormat.Pdf, DocumentFormat.Docx, options, TestContext.Current.CancellationToken);
        string text = ConversionAcceptanceTests.ReadOutput(output.ToArray(), DocumentFormat.Docx);
        Assert.Equal(pages, text.Split(ExpectedText, StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void EmptyFlateBeforeOrdinaryContentRemainsReadable()
    {
        byte[] pdf = BuildPdf(new[]
        {
            Plain("<< /Type /Catalog /Pages 2 0 R >>"),
            Plain("<< /Type /Pages /Count 1 /Kids [3 0 R] >>"),
            Plain("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 6 0 R >> >> /Contents [4 0 R 5 0 R] >>"),
            StreamObject(Array.Empty<byte>(), "/Filter /FlateDecode"),
            StreamObject(Plain("BT /F1 12 Tf 72 720 Td (Hello empty flate) Tj ET"), ""),
            Plain("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"),
        });
        using var input = new MemoryStream(pdf); using var output = new MemoryStream();
        new WordConverter().Convert(input, output, DocumentFormat.Pdf, DocumentFormat.Docx, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains("Hello empty flate", ConversionAcceptanceTests.ReadOutput(output.ToArray(), DocumentFormat.Docx), StringComparison.Ordinal);
    }

    private static byte[] ScannedPdf(byte[] image, int width, int height, int pages, string filter)
    {
        var objects = new List<byte[]>
        {
            Plain("<< /Type /Catalog /Pages 2 0 R >>"),
            Plain("<< /Type /Pages /Count " + pages + " /Kids [" + string.Join(" ", Enumerable.Range(0, pages).Select(i => (3 + i * 3) + " 0 R")) + "] >>"),
        };
        for (int i = 0; i < pages; i++)
        {
            int id = 3 + i * 3;
            objects.Add(Plain("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /XObject << /Im " + (id + 2) + " 0 R >> >> /Contents " + (id + 1) + " 0 R >>"));
            objects.Add(StreamObject(Plain("q 612 0 0 792 0 0 cm /Im Do Q"), ""));
            objects.Add(StreamObject(image, "/Type /XObject /Subtype /Image /Width " + width + " /Height " + height + " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter " + filter));
        }
        return BuildPdf(objects);
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream(); output.WriteByte(0x78); output.WriteByte(0x9c);
        using (var compressed = new DeflateStream(output, CompressionLevel.Optimal, true)) compressed.Write(data, 0, data.Length);
        uint a = 1, b = 0;
        foreach (byte value in data) { a = (a + value) % 65521; b = (b + a) % 65521; }
        uint checksum = (b << 16) | a;
        for (int shift = 24; shift >= 0; shift -= 8) output.WriteByte((byte)(checksum >> shift));
        return output.ToArray();
    }

    private static byte[] Plain(string text) => Encoding.ASCII.GetBytes(text);
    private static byte[] StreamObject(byte[] bytes, string properties)
    {
        using var output = new MemoryStream();
        byte[] header = Plain("<< /Length " + bytes.Length + " " + properties + " >>\nstream\n"); output.Write(header, 0, header.Length);
        output.Write(bytes, 0, bytes.Length); byte[] end = Plain("\nendstream"); output.Write(end, 0, end.Length); return output.ToArray();
    }

    private static byte[] BuildPdf(IEnumerable<byte[]> objects)
    {
        using var output = new MemoryStream(); var offsets = new List<long> { 0 };
        void Write(string text) { byte[] bytes = Plain(text); output.Write(bytes, 0, bytes.Length); }
        Write("%PDF-1.4\n");
        foreach (byte[] body in objects)
        { int id = offsets.Count; offsets.Add(output.Position); Write(id + " 0 obj\n"); output.Write(body, 0, body.Length); Write("\nendobj\n"); }
        long xref = output.Position; Write("xref\n0 " + offsets.Count + "\n0000000000 65535 f \n");
        foreach (long offset in offsets.Skip(1)) Write(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        Write("trailer\n<< /Size " + offsets.Count + " /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF\n"); return output.ToArray();
    }
}
