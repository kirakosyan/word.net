using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Xunit;

namespace WordNet.Tests;

public sealed class RtfTests
{
    private static readonly XNamespace W = TestDocuments.W;
    private static readonly string[][] WriterRows = { new[] { "Item", "Amount" }, new[] { "Service", "42.50" } };

    [Fact]
    public void LibreOfficeProducerRetainsUnicodeEditableListsTablesImagesAndEmphasis()
    {
        byte[] rtf = File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "Interoperability", "editor.rtf"));
        byte[] word = Import(rtf, new ConversionOptions(FidelityMode.BestEffort)); Schema(word); XElement root = Part(word).Root!;
        string text = string.Concat(root.Descendants(W + "t").Select(t => t.Value));
        Assert.Contains("Unicode café Ελληνικά Привет 😀.", text, StringComparison.Ordinal);
        Assert.Contains("Consulting", text, StringComparison.Ordinal); Assert.Contains("42.50", text, StringComparison.Ordinal);
        Assert.Equal(2, root.Descendants(W + "numPr").Count()); Assert.Single(root.Descendants(W + "tbl")); Assert.Equal(4, root.Descendants(W + "tc").Count()); Assert.Single(root.Descendants(W + "drawing"));
        Assert.NotNull(root.Descendants(W + "r").Single(r => InteropText.Contains(r.Value, "Bold reference")).Element(W + "rPr")?.Element(W + "b"));
        Assert.NotNull(root.Descendants(W + "r").Single(r => InteropText.Contains(r.Value, "italic reference")).Element(W + "rPr")?.Element(W + "i"));
        XNamespace wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        Assert.Equal("762000", (string?)Assert.Single(root.Descendants(wp + "extent")).Attribute("cx"));
    }

    [Theory]
    [InlineData(DocumentFormat.Doc)]
    [InlineData(DocumentFormat.Docx)]
    public void NormalConverterExchangesRtfWithBothWordFormats(DocumentFormat wordFormat)
    {
        const string value = "Unicode Ω café Ж.";
        byte[] source = Encoding.ASCII.GetBytes(@"{\rtf1\ansi Unicode \u937? caf\'e9 \u1046?.\par}");
        var options = new ConversionOptions(FidelityMode.BestEffort); var converter = new WordConverter();
        using var rtf = new MemoryStream(source); using var word = new MemoryStream(); converter.Convert(rtf, word, DocumentFormat.Rtf, wordFormat, options, TestContext.Current.CancellationToken);
        if (wordFormat == DocumentFormat.Docx) Schema(word.ToArray());
        else Assert.Equal(new byte[] { 208, 207, 17, 224, 161, 177, 26, 225 }, word.ToArray().Take(8).ToArray());
        word.Position = 0; WordDocument editable = WordDocument.Load(word, wordFormat, out _, options, TestContext.Current.CancellationToken); Assert.Equal(value, Assert.Single(editable.Paragraphs).Text);
        word.Position = 0; using var written = new MemoryStream(); converter.Convert(word, written, wordFormat, DocumentFormat.Rtf, options, TestContext.Current.CancellationToken);
        Assert.StartsWith(@"{\rtf1", Encoding.ASCII.GetString(written.ToArray()), StringComparison.Ordinal); written.Position = 0;
        WordDocument imported = WordDocument.Load(written, DocumentFormat.Rtf, out _, options, TestContext.Current.CancellationToken); Assert.Equal(value, Assert.Single(imported.Paragraphs).Text);
    }

    [Theory]
    [InlineData(@"{\rtf1\ansi escaped \{braces\} and \\ slash}", "escaped {braces} and \\ slash")]
    [InlineData(@"{\rtf1\ansi\ansicpg1252 caf\'e9 \u937? \uc2\u1046xy!}", "café Ω Ж!")]
    [InlineData(@"{\rtf1\ansi\ansicpg1251 \'cf\'f0\'e8\'e2\'e5\'f2}", "Привет")]
    [InlineData(@"{\rtf1\ansi\uc0\u-10179\u-8704}", "😀")]
    [InlineData(@"{\rtf1\ansi\uc1\u937\'3f X}", "Ω X")]
    public void SpecificationEscapesUnicodeFallbackAndCodePagesHaveExactText(string rtf, string expected)
    {
        byte[] word = Import(Encoding.ASCII.GetBytes(rtf)); Schema(word);
        Assert.Equal(expected, string.Concat(Part(word).Descendants(W + "t").Select(t => t.Value)));
    }

    [Fact]
    public void GroupFormattingScopesAndMultipleCellParagraphsRemainEditable()
    {
        const string rtf = @"{\rtf1\ansi{\colortbl;\red51\green102\blue153;}\rtlpar\qc\fs36\cf1 A{\b B{\i C}D}E\par\trowd\cellx2000\cellx4000\pard\intbl One\par Two\cell Other\cell\row}";
        byte[] word = Import(Encoding.ASCII.GetBytes(rtf)); Schema(word); XElement root = Part(word).Root!;
        XElement[] runs = root.Descendants(W + "p").First().Elements(W + "r").ToArray();
        Assert.Equal("ABCDE", string.Concat(runs.Select(r => r.Value))); Assert.Null(runs[0].Element(W + "rPr")?.Element(W + "b"));
        Assert.NotNull(runs.Single(r => r.Value == "C").Element(W + "rPr")?.Element(W + "i")); Assert.Null(runs.Last().Element(W + "rPr")?.Element(W + "b"));
        Assert.Equal(2, root.Descendants(W + "tc").First().Elements(W + "p").Count());
        Assert.Equal("1", (string?)root.Descendants(W + "p").First().Element(W + "pPr")?.Element(W + "bidi")?.Attribute(W + "val"));
        Assert.Empty(root.Descendants(W + "tc").Descendants(W + "bidi"));
        Assert.Equal("center", (string?)root.Descendants(W + "jc").First().Attribute(W + "val"));
    }

    [Fact]
    public void BinaryPictureBytesAreBoundedAndDoNotBecomeRtfSyntax()
    {
        byte[] image = FidelityDocuments.Fixture("color-grid.png"); using var input = new MemoryStream();
        byte[] header = Encoding.ASCII.GetBytes(@"{\rtf1\ansi Before{\pict\pngblip\picwgoal1200\pichgoal600\bin" + image.Length + " ");
        input.Write(header, 0, header.Length); input.Write(image, 0, image.Length); byte[] tail = Encoding.ASCII.GetBytes("}After}"); input.Write(tail, 0, tail.Length);
        byte[] word = Import(input.ToArray()); Schema(word); Assert.Single(Part(word).Descendants(W + "drawing")); Assert.Equal("BeforeAfter", Part(word).Root!.Value);
    }

    [Fact]
    public void WriterProducesIndependentReaderInputAndPreservesRealValues()
    {
        var document = WordDocument.Create(new ConversionOptions(FidelityMode.BestEffort)); document.Title = "RTF qualification";
        document.AddParagraph("Unicode Ω Ж 😀 {braces} \\ path", new TextFormatting { Bold = true, Italic = true, Color = "336699", FontSize = 18 }, cancellationToken: TestContext.Current.CancellationToken);
        document.AddTable(WriterRows, true, cancellationToken: TestContext.Current.CancellationToken);
        document.AddImage(new TemplateImage(FidelityDocuments.Fixture("color-grid.png"), 60, 30), cancellationToken: TestContext.Current.CancellationToken);
        using var output = new MemoryStream(); document.Save(output, DocumentFormat.Rtf, cancellationToken: TestContext.Current.CancellationToken);
        byte[] rtf = output.ToArray(); Assert.StartsWith(@"{\rtf1\ansi", Encoding.ASCII.GetString(rtf), StringComparison.Ordinal);
        Assert.Contains(@"\u-10179?\u-8704?", Encoding.ASCII.GetString(rtf), StringComparison.Ordinal); Assert.True(rtf.All(b => b < 128));
        byte[] word = Import(rtf); Schema(word); Assert.Contains("Unicode Ω Ж 😀 {braces} \\ path", Part(word).Root!.Value, StringComparison.Ordinal); Assert.Equal(4, Part(word).Descendants(W + "tc").Count()); Assert.Single(Part(word).Descendants(W + "drawing"));
        string? artifacts = Environment.GetEnvironmentVariable("WORDNET_INTEROP_ARTIFACTS");
        if (!string.IsNullOrEmpty(artifacts)) { Directory.CreateDirectory(artifacts); File.WriteAllBytes(Path.Combine(artifacts, "writer.rtf"), rtf); }
    }

    [Theory]
    [InlineData(@"{\rtf1 Missing close")]
    [InlineData(@"{\rtf1}\bin2 xx")]
    [InlineData(@"{\rtf1\bin-1 x}")]
    [InlineData(@"{\rtf1\bin999 x}")]
    [InlineData(@"{\rtf1\u999999?}")]
    [InlineData(@"{\rtf1\fs9999999999999 text}")]
    [InlineData(@"{\rtf1\trowd\cellx2000 A\cell\cellx1000 B\cell\row}")]
    [InlineData(@"{\rtf1{\pict\pngblip 012}}")]
    public void MalformedControlsLengthsGroupsAndPicturesFailAtomically(string rtf)
    {
        using var input = new MemoryStream(Encoding.ASCII.GetBytes(rtf)); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.ThrowsAny<InvalidDataException>(() => new WordConverter().Convert(input, output, DocumentFormat.Rtf, DocumentFormat.Docx, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken)); Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void ObjectsUnknownDestinationsAndControlsAreDiagnosedWithoutExecution()
    {
        const string rtf = @"{\rtf1 Safe{\*\unknown SECRET}{\object\objdata 00ff}text}";
        Assert.Throws<NotSupportedException>(() => Import(Encoding.ASCII.GetBytes(rtf)));
        byte[] word = Import(Encoding.ASCII.GetBytes(rtf), new ConversionOptions(FidelityMode.BestEffort)); Assert.Equal("Safetext", Part(word).Root!.Value);
    }

    [Fact]
    public void RtfDepthAndCharacterAdmissionAreFinite()
    {
        Assert.Throws<InvalidDataException>(() => Import(Encoding.ASCII.GetBytes(@"{\rtf1{{{{ text }}}}}"), new ConversionOptions(maximumNestingDepth: 3)));
        Assert.Throws<InvalidDataException>(() => Import(Encoding.ASCII.GetBytes(@"{\rtf1\uc0\u937\u937\u937}"), new ConversionOptions(maximumCharacters: 2)));
    }
    private static byte[] Import(byte[] bytes, ConversionOptions? options = null)
    { using var input = new MemoryStream(bytes); using var output = new MemoryStream(); new WordConverter().Convert(input, output, DocumentFormat.Rtf, DocumentFormat.Docx, options ?? new ConversionOptions(), TestContext.Current.CancellationToken); return output.ToArray(); }
    private static XDocument Part(byte[] bytes) { using var zip = new ZipArchive(new MemoryStream(bytes)); using var stream = zip.GetEntry("word/document.xml")!.Open(); return XDocument.Load(stream); }
    private static void Schema(byte[] bytes) { using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false); Assert.Empty(new OpenXmlValidator().Validate(doc)); }
}

internal static class InteropText
{
    internal static bool Contains(string value, string fragment)
    {
#if NET8_0_OR_GREATER
        return value.Contains(fragment, StringComparison.Ordinal);
#else
        return value.IndexOf(fragment, StringComparison.Ordinal) >= 0;
#endif
    }
}
