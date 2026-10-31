using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed class DictionaryHyphenationTests
{
    private static readonly string[] Entries = { "ex-tra-or-di-nary" };
    private static readonly string[] InvalidEntries = { "in--valid" };
    private static readonly string[] DuplicateEntries = { "ex-tra", "EX-TRA" };
    private static readonly int[] ExpectedPoints = { 2, 5, 7, 9 };
    private static byte[] Input(string text = "A extraordinary example") => TestDocuments.Docx(TestDocuments.Body(
        TestDocuments.TextParagraph(text) + "<w:sectPr><w:pgSz w:w='2400' w:h='15840'/><w:pgMar w:top='720' w:bottom='720' w:left='600' w:right='600' w:header='0' w:footer='0' w:gutter='0'/></w:sectPr>"));
    private static ConversionOptions Options(FidelityMode mode = FidelityMode.BestEffort)
    {
        var options = new ConversionOptions(mode); options.Hyphenation.Dictionary = new HyphenationLexicon("en-US", Entries);
        options.Clock = () => new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero); return options;
    }

    [Fact]
    public void BrokenWordKeepsOneSearchableLogicalWordAndVisibleGlyphPaths()
    {
        byte[] input = Input(); TypographyTests.Save("dictionary-hyphenation-source.docx", input);
        using var source = new MemoryStream(input); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(source, output, DocumentFormat.Docx, DocumentFormat.Pdf, Options(), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, item => item.Code == "Hyphenation.SearchGeometry");
        using PdfDocument pdf = PdfDocument.Open(output.ToArray()); var page = Assert.Single(pdf.GetPages());
        Assert.Contains("extraordinary", page.Text, StringComparison.Ordinal); Assert.DoesNotContain("-", page.Text, StringComparison.Ordinal);
        Assert.NotEmpty(page.Paths); TypographyTests.Save("dictionary-hyphenation.pdf", output.ToArray());
        using var editable = new MemoryStream(input); WordDocument document = WordDocument.Load(editable, DocumentFormat.Docx, out _, Options(), TestContext.Current.CancellationToken);
        using var saved = new MemoryStream(); document.Save(saved, DocumentFormat.Docx, Options(), TestContext.Current.CancellationToken);
        TypographyTests.Save("dictionary-hyphenation-saved.docx", saved.ToArray());
    }

    [Fact]
    public void JustifiedParagraphRetainsSearchableTextAndMovesTheVisibleSplitWord()
    {
        string section = "<w:sectPr><w:pgSz w:w='3000' w:h='15840'/><w:pgMar w:top='720' w:bottom='720' w:left='600' w:right='600' w:header='0' w:footer='0' w:gutter='0'/></w:sectPr>";
        string content = "<w:r><w:t xml:space='preserve'>A red extraordinary example</w:t></w:r>";
        byte[] justified = TestDocuments.Docx(TestDocuments.Body("<w:p><w:pPr><w:jc w:val='both'/></w:pPr>" + content + "</w:p>" + section));
        byte[] left = TestDocuments.Docx(TestDocuments.Body("<w:p>" + content + "</w:p>" + section));
        TypographyTests.Save("dictionary-hyphenation-justified-source.docx", justified);
        using var input = new MemoryStream(justified); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, Options(), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, item => item.Code == "Hyphenation.SearchGeometry");
        Assert.Contains(result.Diagnostics, item => item.Code == "Hyphenation.JustificationApproximation");
        using PdfDocument pdf = PdfDocument.Open(output.ToArray()); Assert.Contains("extraordinary", pdf.GetPage(1).Text, StringComparison.Ordinal);
        Assert.NotEmpty(pdf.GetPage(1).Paths); TypographyTests.Save("dictionary-hyphenation-justified.pdf", output.ToArray());
        TypographyTests.Save("dictionary-hyphenation-left-control.pdf", new WordConverter().ConvertToPdf(left, Options()));
    }

    [Fact]
    public void StrictRejectsTheQualifiedSearchHighlightDifferenceWithoutChangingOutput()
    {
        using var input = new MemoryStream(Input()); using var output = new MemoryStream(); output.WriteByte(42);
        var error = Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, Options(FidelityMode.Strict), TestContext.Current.CancellationToken));
        Assert.StartsWith("Hyphenation.SearchGeometry:", error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void UnusedDictionaryLeavesDefaultPdfBytesUnchanged()
    {
        byte[] source = Input("A normal example"); var baseline = Options(); baseline.Hyphenation.Dictionary = null;
        byte[] first = new WordConverter().ConvertToPdf(source, baseline); byte[] second = new WordConverter().ConvertToPdf(source, Options());
        Assert.Equal(first, second);
    }

    [Fact]
    public void TrailingSpaceDoesNotHyphenateAWordThatFitsOnTheLine()
    {
        byte[] source = TestDocuments.Docx(TestDocuments.Body(TestDocuments.TextParagraph("extraordinary ")
            + "<w:sectPr><w:pgSz w:w='2620' w:h='15840'/><w:pgMar w:top='720' w:bottom='720' w:left='600' w:right='600' w:header='0' w:footer='0' w:gutter='0'/></w:sectPr>"));
        using var input = new MemoryStream(source); using var output = new MemoryStream();
        var options = Options(); options.Hyphenation.MaximumWork = 1;
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(result.Diagnostics, item => item.Code == "Hyphenation.SearchGeometry");
        using PdfDocument pdf = PdfDocument.Open(output.ToArray()); Assert.Contains("extraordinary", pdf.GetPage(1).Text, StringComparison.Ordinal);
        Assert.Empty(pdf.GetPage(1).Paths);
    }

    [Fact]
    public void SpaceWiderThanTheColumnFailsExplicitly()
    {
        byte[] source = TestDocuments.Docx(TestDocuments.Body(TestDocuments.TextParagraph(" ")
            + "<w:sectPr><w:pgSz w:w='1250' w:h='15840'/><w:pgMar w:top='720' w:bottom='720' w:left='600' w:right='600' w:header='0' w:footer='0' w:gutter='0'/></w:sectPr>"));
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42);
        var error = Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, Options(), TestContext.Current.CancellationToken));
        Assert.StartsWith("Layout.ColumnWidth:", error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void LocalDictionaryIsValidatedAndCopied()
    {
        string[] words = { "ex-tra-or-di-nary" }; var dictionary = new HyphenationLexicon("en-US", words); words[0] = "bad";
        Assert.True(dictionary.TryGetBreaks("EXTRAORDINARY", out int[] points)); Assert.Equal(ExpectedPoints, points);
        Assert.Throws<ArgumentException>(() => new HyphenationLexicon("fr", Entries));
        Assert.Throws<ArgumentException>(() => new HyphenationLexicon("en", InvalidEntries));
        Assert.Throws<ArgumentException>(() => new HyphenationLexicon("en", DuplicateEntries));
        var options = Options(); HyphenationOptions snapshot = options.Hyphenation.Snapshot(); options.Hyphenation.MinimumPrefix = 4;
        Assert.Equal(2, snapshot.MinimumPrefix); options.Hyphenation.MaximumConsecutiveLines = 0;
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Hyphenation.Snapshot());
    }

    [Fact]
    public void UnqualifiedProfilesAndCancellationLeaveOutputUntouched()
    {
        byte[] source = Input();
        foreach (int kind in Enumerable.Range(0, 3))
        {
            var options = Options(); if (kind == 0) options.Pdf.Accessibility = PdfAccessibility.PdfUa1;
            if (kind == 1) options.Pdf.Conformance = PdfConformance.PdfA2b;
            if (kind == 2) options.Pdf.Pages.Add(new PageRange(1, 1));
            using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(42);
            var error = Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
            Assert.StartsWith("Hyphenation.Profile:", error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, output.ToArray());
        }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); using var cancelledInput = new MemoryStream(source); using var cancelledOutput = new MemoryStream(); cancelledOutput.WriteByte(42);
        Assert.Throws<OperationCanceledException>(() => new WordConverter().Convert(cancelledInput, cancelledOutput, DocumentFormat.Docx, DocumentFormat.Pdf, Options(), cancelled.Token));
        Assert.Equal(new byte[] { 42 }, cancelledOutput.ToArray());
    }

    [Theory]
    [InlineData("soft")]
    [InlineData("manual")]
    [InlineData("url")]
    [InlineData("work")]
    [InlineData("consecutive")]
    [InlineData("pages")]
    public void UnsupportedTextAndExhaustedLimitsFailBeforeOutput(string failure)
    {
        string text = failure == "soft" ? "A extra\u00adordinary example"
            : failure == "manual" ? "A extraordinary\texample"
            : failure == "url" ? "A https://example.test/very/long/unbreakable/path"
            : failure == "consecutive" ? "A extraordinary extraordinary" : "A extraordinary example";
        var options = Options(); if (failure == "work") options.Hyphenation.MaximumWork = 1;
        if (failure == "consecutive") options.Hyphenation.MaximumConsecutiveLines = 1;
        byte[] bytes = failure == "pages" ? TestDocuments.Docx(TestDocuments.Body(string.Concat(Enumerable.Repeat(TestDocuments.TextParagraph(text), 80))
            + "<w:sectPr><w:pgSz w:w='2400' w:h='15840'/><w:pgMar w:top='720' w:bottom='720' w:left='600' w:right='600' w:header='0' w:footer='0' w:gutter='0'/></w:sectPr>")) : Input(text);
        using var input = new MemoryStream(bytes); using var output = new MemoryStream(); output.WriteByte(42);
        Exception error = Assert.ThrowsAny<Exception>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
        string code = failure == "soft" ? "Hyphenation.SoftHyphen:" : failure == "manual" ? "Hyphenation.Owner:" : failure == "work" ? "Limit.HyphenationWork:"
            : failure == "pages" ? "Hyphenation.Pagination:" : "Hyphenation.WordWidth:";
        Assert.StartsWith(code, error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void LocalDictionaryDoesNotChangeEditableSourceText()
    {
        using var input = new MemoryStream(Input()); WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, Options(), TestContext.Current.CancellationToken);
        using var output = new MemoryStream(); document.Save(output, DocumentFormat.Docx, Options(), TestContext.Current.CancellationToken);
        string text = string.Concat(P1FieldTests.Xml(output.ToArray(), "word/document.xml").Descendants(System.Xml.Linq.XName.Get("t", TestDocuments.W)).Select(e => e.Value));
        Assert.Equal("A extraordinary example", text);
    }

    [Fact]
    public void MultipleRunsRequireSeparateOwnershipQualification()
    {
        string paragraph = "<w:p><w:r><w:t>A extra</w:t></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>ordinary example</w:t></w:r></w:p>";
        using var input = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(paragraph))); using var output = new MemoryStream(); output.WriteByte(42);
        var error = Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, Options(), TestContext.Current.CancellationToken));
        Assert.StartsWith("Hyphenation.Owner:", error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void ImmutableLexiconCanBeUsedConcurrentlyWithoutSharedLayoutState()
    {
        byte[] input = Input(); var options = Options(); var results = new byte[4][];
        Parallel.For(0, results.Length, i => results[i] = new WordConverter().ConvertToPdf(input, options));
        Assert.All(results, bytes => Assert.Equal(results[0], bytes));
    }
}
