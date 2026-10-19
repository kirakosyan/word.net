using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Model;
using Xunit;

namespace WordNet.Tests;

public sealed partial class OdtTests
{
    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "Odt", name + ".odt"));
    private static ConversionContext Context() => new ConversionContext(new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
    [Fact]
    public void PublicOdtGateAddsExactlyTheDocxPairAndKeepsSessionEditingExplicit()
    {
        var converter = new WordConverter();
        for (int value = 0; value <= 12; value++)
        {
            DocumentFormat format = (DocumentFormat)value;
            Assert.Equal(format == DocumentFormat.Docx, converter.CanConvert(format, DocumentFormat.Odt));
            Assert.Equal(format == DocumentFormat.Docx, converter.CanConvert(DocumentFormat.Odt, format));
        }
        Assert.False(converter.CanConvert((DocumentFormat)999, DocumentFormat.Docx));
        using var source = new MemoryStream(Fixture("text"));
        Assert.Throws<NotSupportedException>(() => WordDocument.Load(source, DocumentFormat.Odt, out _, cancellationToken: TestContext.Current.CancellationToken));
        WordDocument document = WordDocument.Create(); using var output = new MemoryStream();
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Save(output, DocumentFormat.Odt, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(0, output.Length);
    }
    [Fact]
    public void StrictPlainTextExchangeUsesBothPublicDirectionsWithoutInventedLosses()
    {
        byte[] original = TestDocuments.Docx(TestDocuments.Body(TestDocuments.TextParagraph("Plain Café")));
        var converter = new WordConverter(); using var word = new MemoryStream(original); using var odt = new MemoryStream();
        ConversionResult written = converter.Convert(word, odt, DocumentFormat.Docx, DocumentFormat.Odt, new ConversionOptions(), TestContext.Current.CancellationToken);
        Assert.DoesNotContain(written.Diagnostics, d => d.Severity == DiagnosticSeverity.Warning);
        odt.Position = 0; using var restored = new MemoryStream();
        converter.Convert(odt, restored, DocumentFormat.Odt, DocumentFormat.Docx, new ConversionOptions(), TestContext.Current.CancellationToken);
        restored.Position = 0; DocumentModel model = DocxReader.Read(restored, Context());
        Assert.Equal("Plain Café", Assert.Single(model.BodyParagraphs).Text); Assert.True(word.CanRead); Assert.True(odt.CanRead);
    }
    [Fact]
    public void OdtPreflightReportsTheSameUnsupportedAppearanceAsConversion()
    {
        byte[] bytes = TestDocuments.Docx(TestDocuments.Body("<w:p><w:r><w:rPr><w:strike/></w:rPr><w:t>Text</w:t></w:r></w:p>"));
        var converter = new WordConverter(); using var input = new MemoryStream(bytes);
        DocumentInspection inspection = converter.Inspect(input, DocumentFormat.Docx, DocumentFormat.Odt, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(inspection.Diagnostics, d => d.Code == "Odt.StyleLoss");
        input.Position = 0; using var output = new MemoryStream(new byte[] { 1, 2, 3 }, true);
        Assert.Throws<NotSupportedException>(() => converter.Convert(input, output, DocumentFormat.Docx, DocumentFormat.Odt, new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.Equal(0, output.Position);
    }
    [Theory]
    [InlineData("text")]
    [InlineData("list")]
    [InlineData("table-image")]
    [InlineData("nine-levels")]
    [InlineData("masters")]
    public void InternalExchangeProducesIndependentReaderArtifacts(string name)
    {
        using var source = new MemoryStream(Fixture(name)); DocumentModel model = OdtFormat.Read(source, Context());
        using var word = new MemoryStream(); DocxWriter.Write(model, word, Context());
        word.Position = 0; DocumentModel fromWord = DocxReader.Read(word, Context());
        using var odt = new MemoryStream(); OdtFormat.Write(fromWord, odt, Context());
        odt.Position = 0; DocumentModel reloaded = OdtFormat.Read(odt, Context());
        Assert.Equal(model.Paragraphs.Select(p => p.Text), reloaded.Paragraphs.Select(p => p.Text));
        string? directory = Environment.GetEnvironmentVariable("WORDNET_ODT_EVIDENCE");
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, name + ".docx"), word.ToArray());
            File.WriteAllBytes(Path.Combine(directory, name + ".odt"), odt.ToArray());
        }
    }
    [Fact]
    public void NineLevelListsAndTwoMasterStoriesRetainTheirActualStructure()
    {
        using var source = new MemoryStream(Fixture("nine-levels")); DocumentModel model = OdtFormat.Read(source, Context());
        Assert.Equal(Enumerable.Range(0, 9), model.BodyParagraphs.Select(p => p.List!.Level));
        Assert.Equal(0, model.BodyParagraphs.First().List!.Start); Assert.Single(model.BodyParagraphs.Select(p => p.List!.Id).Distinct());
        using var masters = new MemoryStream(Fixture("masters")); model = OdtFormat.Read(masters, Context());
        Assert.Equal(400, model.Blocks[0].Section!.Width); Assert.Equal(500, model.Blocks[1].Section!.Width);
        Assert.Equal(30, model.Blocks[0].Section!.Left); Assert.Equal(40, model.Blocks[1].Section!.Left);
        Assert.Equal("First header", ((ParagraphBlock)model.Blocks[0].Section!.Stories["header-default"][0]).Text);
        Assert.Equal("Second footer", ((ParagraphBlock)model.Blocks[1].Section!.Stories["footer-default"][0]).Text);
    }
    [Fact]
    public void IndependentStylesWhitespaceMetadataAndMasterAreReadExactly()
    {
        using var source = new MemoryStream(Fixture("text")); DocumentModel model = OdtFormat.Read(source, Context());
        Assert.True(source.CanRead); ParagraphBlock paragraph = Assert.Single(model.BodyParagraphs);
        Assert.Equal("Café  Ω\tend\nnext", paragraph.Text); Assert.Equal(ParagraphAlignment.Center, paragraph.Alignment);
        Assert.All(paragraph.Runs, r => { Assert.True(r.Bold); Assert.Equal(11, r.FontSize); Assert.Equal("123456", r.Color); });
        Assert.Equal("Independent ODT", model.Title); Assert.Equal("Fixture Author", model.Author);
        Assert.Equal(400, model.FinalSection!.Width); Assert.Equal(300, model.FinalSection.Height); Assert.Equal(30, model.FinalSection.Left);
    }
    [Fact]
    public void IndependentlyOfficeAuthoredOdf13RetainsExpectedTextAndResolvedAppearance()
    {
        using var source = new MemoryStream(Fixture("office-text")); DocumentModel model = OdtFormat.Read(source, Context());
        ParagraphBlock paragraph = Assert.Single(model.BodyParagraphs);
        Assert.Equal("Café  Ω\tend\nnext", paragraph.Text);
        Assert.Equal(ParagraphAlignment.Center, paragraph.Alignment); Assert.All(paragraph.Runs, run => Assert.True(run.Bold));
    }
    [Fact]
    public void IndependentListStartIsEditableAndTableImageHashesAndFramesSurvive()
    {
        using var list = new MemoryStream(Fixture("list")); DocumentModel numbered = OdtFormat.Read(list, Context());
        var counter = new ListCounter();
        Assert.Equal("3.", counter.Label(numbered.BodyParagraphs.First().List!)); Assert.Equal("4.", counter.Label(numbered.BodyParagraphs.Last().List!));
        using var source = new MemoryStream(Fixture("table-image")); DocumentModel model = OdtFormat.Read(source, Context());
        TableBlock table = Assert.Single(model.Tables); Assert.Equal(2, table.Rows[0][0].ColumnSpan); Assert.Single(table.Rows[0]);
        Assert.Equal(120, table.ColumnWidths[0]); Assert.Equal("Left", table.Rows[1][0].Text); Assert.Equal("Right", table.Rows[1][1].Text);
        DocumentImage image = Assert.Single(model.Paragraphs.SelectMany(p => p.Runs), r => r.Image != null).Image!;
        Assert.Equal(60, image.Width); Assert.Equal(30, image.Height); Assert.Equal("Fixture grid", image.AlternativeText);
        Assert.Equal(FidelityDocuments.Fixture("color-grid.png"), image.Bytes);
        using var output = new MemoryStream(); OdtFormat.Write(model, output, Context()); output.Position = 0;
        byte[] package = output.ToArray();
        Assert.Equal(0, BitConverter.ToUInt16(package, 8)); // ZIP stored method, including on Framework.
        Assert.Equal(0, BitConverter.ToUInt16(package, 28)); // ODF forbids extra fields on mimetype.
        Assert.Equal((uint)OdfPackage.MediaType.Length, BitConverter.ToUInt32(package, 18));
        using var zip = new ZipArchive(output, ZipArchiveMode.Read, true);
        Assert.Equal("mimetype", zip.Entries[0].FullName);
        using var imageStream = zip.Entries.Single(e => e.FullName.StartsWith("Pictures/", StringComparison.Ordinal)).Open(); using var bytes = new MemoryStream(); imageStream.CopyTo(bytes);
        Assert.Equal(image.Bytes, bytes.ToArray());
    }
    [Theory]
    [InlineData("../content.xml")]
    [InlineData("Pictures/%2fsecret")]
    [InlineData("/absolute")]
    [InlineData("https://example.invalid/picture")]
    [InlineData("Pictures/../secret")]
    public void PackageTargetsNeverResolveOutsideThePackage(string path) => Assert.Throws<InvalidDataException>(() => OdfPackage.Resolve(path));
    [Theory]
    [InlineData("mime")]
    [InlineData("manifest")]
    [InlineData("script")]
    [InlineData("dtd")]
    public void HostilePackagesFailBeforeAnyDestinationWrite(string mutation)
    {
        byte[] bytes = Fixture("text");
        using var source = new MemoryStream(); source.Write(bytes, 0, bytes.Length);
        using (var archive = new ZipArchive(source, ZipArchiveMode.Update, true))
        {
            string path = mutation is "mime" ? "mimetype" : mutation is "manifest" ? "META-INF/manifest.xml" : "content.xml";
            ZipArchiveEntry entry = archive.GetEntry(path)!; string text;
            using (var reader = new StreamReader(entry.Open())) text = reader.ReadToEnd(); entry.Delete();
            text = mutation switch { "mime" => "wrong", "manifest" => text.Replace("text/xml", "wrong"), "script" => text.Replace("<office:text>", "<office:text><office:scripts><office:script>disabled fixture</office:script></office:scripts>"), _ => "<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///must-not-open'>]>" + text };
            using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false)); writer.Write(text);
        }
        source.Position = 0; using var sentinel = new MemoryStream(new byte[] { 1, 2, 3 }, true);
        Assert.Throws<InvalidDataException>(() => { DocumentModel model = OdtFormat.Read(source, Context()); OdtFormat.Write(model, sentinel, Context()); });
        Assert.Equal(3, sentinel.Length); Assert.Equal(0, sentinel.Position); Assert.True(source.CanRead);
    }
    [Fact]
    public void CancellationAndExpansionLimitsLeaveCallerOwnershipIntact()
    {
        using var source = new MemoryStream(Fixture("text")); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => OdtFormat.Read(source, new ConversionContext(new ConversionOptions(), cancellation.Token)));
        Assert.True(source.CanRead);
        source.Position = 0;
        Assert.Throws<InvalidDataException>(() => OdtFormat.Read(source, new ConversionContext(new ConversionOptions(maximumExpandedBytes: 1024), TestContext.Current.CancellationToken)));
    }
}
