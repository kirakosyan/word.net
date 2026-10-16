using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using OpenMcdf;
using Xunit;

namespace WordNet.Tests;

public sealed class DocumentInspectionTests
{
    [Theory]
    [InlineData("invoice-services")]
    [InlineData("invoice-nested")]
    [InlineData("invoice-relative")]
    [InlineData("contract-services")]
    [InlineData("contract-legal")]
    [InlineData("contract-revisions")]
    [InlineData("report-quarterly")]
    [InlineData("report-columns")]
    [InlineData("report-pages")]
    [InlineData("letterhead-offer")]
    [InlineData("letterhead-brand")]
    [InlineData("letterhead-textbox")]
    public void IndependentBusinessDocumentsHaveConsistentPreflightAndConversionFindings(string name)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "Comparison", name + ".docx"));
        var converter = new WordConverter(); var options = new ConversionOptions(FidelityMode.BestEffort);
        using var source = new MemoryStream(bytes);
        using (var schemaInput = new MemoryStream(bytes))
        using (WordprocessingDocument package = WordprocessingDocument.Open(schemaInput, false))
            Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
        DocumentInspection inspection = converter.Inspect(source, DocumentFormat.Docx, DocumentFormat.Pdf, options, true, TestContext.Current.CancellationToken);
        Assert.True(source.CanRead); Assert.Equal(bytes.Length, inspection.InputBytes);
        Assert.InRange(inspection.ArchiveEntryCount!.Value, 4, 5); Assert.True(inspection.DeclaredExpandedBytes > 0);
        Assert.True(inspection.Features.Paragraphs > 0); Assert.True(inspection.Characters > 0);
        Assert.True(inspection.LayoutPerformed); Assert.InRange(inspection.PageCount!.Value, 1, 2);
        source.Position = 0;
        using var output = new MemoryStream();
        ConversionResult conversion = converter.Convert(source, output, DocumentFormat.Docx, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken);
        Assert.Equal(conversion.Metrics.PageCount, inspection.PageCount);
        Assert.Equal(conversion.Diagnostics.Select(d => (d.Code, d.Count)), inspection.Diagnostics.Select(d => (d.Code, d.Count)));
        Assert.Equal(conversion.Diagnostics.SelectMany(d => d.Occurrences).Select(o => (o.Location.ToString(), o.Phase)),
            inspection.Diagnostics.SelectMany(d => d.Occurrences).Select(o => (o.Location.ToString(), o.Phase)));
        Assert.All(inspection.Diagnostics, d => Assert.NotEmpty(d.Occurrences));
        using var json = JsonReaderWriterFactory.CreateJsonReader(File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "Comparison", "manifest.json")), XmlDictionaryReaderQuotas.Max);
        XElement manifest = XDocument.Load(json).Root!;
        string[] expectedLosses = manifest.Element("documents")!.Elements("item").Single(d => d.Element("id")!.Value == name)
            .Element("knownLosses")!.Elements("item").Select(e => e.Value).OrderBy(code => code, StringComparer.Ordinal).ToArray();
        Assert.Equal(expectedLosses, conversion.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning).Select(d => d.Code).OrderBy(code => code, StringComparer.Ordinal));
    }

    [Fact]
    public void StrictInspectionCollectsActionableCellAndFontLossesWithoutWritingOutput()
    {
        byte[] bytes = TestDocuments.Docx(TestDocuments.Body("<w:tbl><w:tr><w:tc><w:p><w:r><w:rPr><w:rFonts w:ascii='Missing Private Font'/><w:rtl/></w:rPr><w:t>SECRET-CUSTOMER</w:t></w:r></w:p></w:tc></w:tr></w:tbl>"));
        using var source = new MemoryStream(bytes);
        var converter = new WordConverter();
        DocumentInspection result = converter.Inspect(source, DocumentFormat.Docx, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.WouldRejectStrict); Assert.False(result.LayoutPerformed); Assert.Null(result.PageCount);
        Assert.Equal(1, result.Features.Tables);
        ConversionDiagnostic font = Assert.Single(result.Diagnostics, d => d.Code == "Font.Substituted");
        Assert.Contains("/tbl[1]/tr[1]/tc[1]/p[1]", Assert.Single(font.Occurrences).Location.Path, StringComparison.Ordinal);
        Assert.Equal("word/document.xml", font.Occurrences[0].Location.Part);
        Assert.Equal(DiagnosticPhase.Layout, font.Occurrences[0].Phase);
        Assert.Contains(result.Fonts, f => f.RequestedFamily == "Missing Private Font" && f.ResolvedFamily == "Noto Sans");
        Assert.All(result.Diagnostics, d => { Assert.DoesNotContain("SECRET", d.Message, StringComparison.Ordinal); Assert.DoesNotContain("Private", d.Message, StringComparison.Ordinal); });
        source.Position = 0; using var destination = new MemoryStream(); destination.WriteByte(42);
        Assert.Throws<NotSupportedException>(() => converter.Convert(source, destination, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, destination.ToArray());
    }

    [Fact]
    public void GroupingRetainsAllCountsAndBoundsLocations()
    {
        string paragraph = "<w:p><w:r><w:rPr><w:rtl/></w:rPr><w:t>x</w:t></w:r></w:p>";
        using var source = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(string.Concat(Enumerable.Repeat(paragraph, 150)))));
        DocumentInspection result = new WordConverter().Inspect(source, DocumentFormat.Docx, DocumentFormat.Html, cancellationToken: TestContext.Current.CancellationToken);
        ConversionDiagnostic diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "Docx.RunFormatting");
        Assert.Equal(150, diagnostic.Count); Assert.Equal(128, diagnostic.Occurrences.Count); Assert.True(diagnostic.LocationsTruncated);
        Assert.Contains("/p[1]/r[1]", diagnostic.Occurrences[0].Location.Path, StringComparison.Ordinal);
        Assert.Contains("/p[128]/r[1]", diagnostic.Occurrences[127].Location.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AsyncInspectionReadsFromCurrentPositionAndLeavesNonseekableInputOpen()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("skip\nHello preflight");
        using var source = new NonseekableInput(bytes, 5);
        DocumentInspection result = await new WordConverter().InspectAsync(source, DocumentFormat.Text, DocumentFormat.Docx,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(bytes.Length - 5, result.InputBytes); Assert.Equal(1, result.Features.Paragraphs);
        Assert.Empty(result.Diagnostics); Assert.False(result.WouldRejectStrict); Assert.True(source.CanRead);
    }

    [Fact]
    public async Task InspectionHonorsCancellationBeforeReading()
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var converter = new WordConverter();
        Assert.Throws<OperationCanceledException>(() => converter.Inspect(source, DocumentFormat.Text, DocumentFormat.Docx, cancellationToken: cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => converter.InspectAsync(source, DocumentFormat.Text, DocumentFormat.Docx, cancellationToken: cancellation.Token));
        Assert.Equal(0, source.Position); Assert.True(source.CanRead);
    }

    [Theory]
    [InlineData("malformed-xml")]
    [InlineData("expanded-limit")]
    public void CorruptAndHostileDocumentsFailInspection(string name)
    {
        using var source = File.OpenRead(Path.Combine(TestDocuments.FixtureDirectory, "Enterprise", name + ".docx"));
        Assert.Throws<InvalidDataException>(() => new WordConverter().Inspect(source, DocumentFormat.Docx, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task InspectionChecksPairsPoliciesAndLimits()
    {
        var converter = new WordConverter(); using var source = new MemoryStream(Encoding.UTF8.GetBytes("too large"));
        Assert.Throws<NotSupportedException>(() => converter.Inspect(source, DocumentFormat.Html, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(() => converter.Inspect(source, DocumentFormat.Text, DocumentFormat.Docx, includeLayout: true, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Throws<InvalidDataException>(() => converter.Inspect(source, DocumentFormat.Text, DocumentFormat.Docx, new ConversionOptions(maximumInputBytes: 2), cancellationToken: TestContext.Current.CancellationToken));
        source.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => converter.InspectAsync(source, DocumentFormat.Text, DocumentFormat.Docx,
            new ConversionOptions(maximumInputBytes: 2), cancellationToken: TestContext.Current.CancellationToken));
        source.Position = 0;
        Assert.Throws<InvalidDataException>(() => converter.Inspect(source, DocumentFormat.Text, DocumentFormat.Docx, new ConversionOptions(maximumCharacters: 2), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void InspectionRejectsEncryptedBinaryWordBeforeParsingContent()
    {
        byte[] original = File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "plain.doc"));
        using var source = new MemoryStream(); source.Write(original, 0, original.Length); source.Position = 0;
        using (var root = RootStorage.Open(source, StorageModeFlags.LeaveOpen))
        using (CfbStream word = root.OpenStream("WordDocument"))
        {
            word.Position = 11; int flags = word.ReadByte(); word.Position = 11;
            word.WriteByte((byte)(flags | 1)); // MS-DOC FibBase.fEncrypted (bit 8 of flags at byte 10).
        }
        source.Position = 0;
        NotSupportedException error = Assert.Throws<NotSupportedException>(() => new WordConverter().Inspect(source, DocumentFormat.Doc, DocumentFormat.Docx,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.StartsWith("Doc.Encryption:", error.Message, StringComparison.Ordinal); Assert.True(source.CanRead);
    }

    [Fact]
    public void InspectionReportsProviderFindingsAndArchiveLayoutLimits()
    {
        var converter = new WordConverter();
        using var pdf = File.OpenRead(Path.Combine(TestDocuments.FixtureDirectory, "digital.pdf"));
        DocumentInspection result = converter.Inspect(pdf, DocumentFormat.Pdf, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics.SelectMany(d => d.Occurrences), o => o.Phase == DiagnosticPhase.Provider);
        Assert.Null(result.ArchiveEntryCount); Assert.Null(result.DeclaredExpandedBytes);
        using var source = File.OpenRead(Path.Combine(TestDocuments.FixtureDirectory, "Comparison", "report-pages.docx"));
        Assert.Throws<InvalidDataException>(() => converter.Inspect(source, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(maximumArchiveEntries: 1), cancellationToken: TestContext.Current.CancellationToken));
        source.Position = 0;
        Assert.Throws<InvalidDataException>(() => converter.Inspect(source, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(maximumExpandedBytes: 50), cancellationToken: TestContext.Current.CancellationToken));
        source.Position = 0;
        Assert.Throws<InvalidDataException>(() => converter.Inspect(source, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(maximumNestingDepth: 1), cancellationToken: TestContext.Current.CancellationToken));
        source.Position = 0;
        Assert.Throws<InvalidDataException>(() => converter.Inspect(source, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(maximumPages: 1), includeLayout: true, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void HeaderDiagnosticsIdentifyTheSourceStory()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "Comparison", "letterhead-offer.docx"));
        using var source = new MemoryStream(); source.Write(bytes, 0, bytes.Length);
        using (var zip = new ZipArchive(source, ZipArchiveMode.Update, true))
        {
            string xml;
            using (var reader = new StreamReader(zip.GetEntry("word/header.xml")!.Open())) xml = reader.ReadToEnd();
            zip.GetEntry("word/header.xml")!.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("word/header.xml").Open());
            writer.Write(xml.Replace("<w:rPr>", "<w:rPr><w:rtl/>"));
        }
        source.Position = 0;
        DocumentInspection result = new WordConverter().Inspect(source, DocumentFormat.Docx, DocumentFormat.Markdown,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "HeaderFooter.Output");
        ConversionDiagnostic finding = Assert.Single(result.Diagnostics, d => d.Code == "Docx.RunFormatting");
        DiagnosticOccurrence occurrence = Assert.Single(finding.Occurrences);
        Assert.Equal("word/header.xml", occurrence.Location.Part);
        Assert.Equal("/hdr[1]/p[1]/r[1]", occurrence.Location.Path);
        Assert.Equal(DiagnosticPhase.Parse, occurrence.Phase);
        source.Position = 0;
        DocumentInspection supported = new WordConverter().Inspect(source, DocumentFormat.Docx, DocumentFormat.Pdf,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(5, supported.Features.Paragraphs);
    }

    [Fact]
    public void WriterOnlyFindingsAreExplicitlyOutsidePreflight()
    {
        using var source = new MemoryStream(FidelityDocuments.Docx("color-grid.png"));
        var options = new ConversionOptions(FidelityMode.BestEffort); var converter = new WordConverter();
        DocumentInspection inspected = converter.Inspect(source, DocumentFormat.Docx, DocumentFormat.Pdf, options, true, TestContext.Current.CancellationToken);
        Assert.Equal(1, inspected.Features.Images); Assert.True(inspected.DecodedImagePixels > 0);
        Assert.DoesNotContain(inspected.Diagnostics, d => d.Code == "Pdf.ImageAlternativeText");
        source.Position = 0; using var output = new MemoryStream();
        ConversionResult converted = converter.Convert(source, output, DocumentFormat.Docx, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken);
        Assert.Equal(DiagnosticPhase.Write, Assert.Single(Assert.Single(converted.Diagnostics, d => d.Code == "Pdf.ImageAlternativeText").Occurrences).Phase);
    }

    private sealed class NonseekableInput : Stream
    {
        private readonly MemoryStream inner;
        internal NonseekableInput(byte[] data, int position) { inner = new MemoryStream(data); inner.Position = position; }
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
