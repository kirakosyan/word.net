using System;
using System.IO;
using System.IO.Compression;
using Xunit;

namespace WordNet.Tests;

public sealed class ParserFailureTests
{
    [Theory]
    [InlineData(63, 4, (byte)255)]
    [InlineData(189, 1, (byte)0)]
    public void CorruptPdfResourcesDoNotExposeProviderImplementationExceptions(int offset, int count, byte value)
    {
        byte[] bytes = ConversionAcceptanceTests.Fixture("digital.pdf");
        for (int i = 0; i < count; i++) bytes[offset + i] = value;
        using var input = new MemoryStream(bytes); using var output = new MemoryStream(); output.WriteByte(42);
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(input, output, DocumentFormat.Pdf, DocumentFormat.Docx,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken));
        Assert.StartsWith("Pdf.Resources:", error.Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Theory]
    [InlineData(DocumentFormat.Doc)]
    [InlineData(DocumentFormat.Docx)]
    public void InvalidEpubMediaTypeBytesHavePortableFailure(DocumentFormat target)
    {
        using var input = new MemoryStream();
        byte[] original = ConversionAcceptanceTests.Fixture("plain.epub"); input.Write(original, 0, original.Length); input.Position = 0;
        using (var zip = new ZipArchive(input, ZipArchiveMode.Update, true))
        {
            zip.GetEntry("mimetype")!.Delete();
            using Stream mime = zip.CreateEntry("mimetype", CompressionLevel.NoCompression).Open();
            mime.WriteByte(0xe9);
        }
        input.Position = 0;
        using var output = new MemoryStream(); output.WriteByte(42);
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(input, output, DocumentFormat.Epub, target));
        Assert.StartsWith("Epub.Mimetype:", error.Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Theory]
    [InlineData("plain.doc", DocumentFormat.Doc, false)]
    [InlineData("plain.doc", DocumentFormat.Doc, true)]
    [InlineData("digital.pdf", DocumentFormat.Pdf, false)]
    [InlineData("digital.pdf", DocumentFormat.Pdf, true)]
    public void InvalidContainerHasPortableErrorAndPreservesDestination(string fixture, DocumentFormat source, bool truncate)
    {
        byte[] bytes = ConversionAcceptanceTests.Fixture(fixture);
        if (truncate) Array.Resize(ref bytes, 32); else Array.Clear(bytes, 0, 64);
        using var input = new MemoryStream(bytes); using var output = new MemoryStream(); output.WriteByte(42);
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(input, output, source, DocumentFormat.Docx,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken));
        Assert.StartsWith(source == DocumentFormat.Doc ? "Doc.Container:" : "Pdf.Structure:", error.Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
        Assert.True(input.CanRead); Assert.True(output.CanWrite);
    }
}
