using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed class EastAsianLayoutTests
{
    private const string Japanese = "日本語の文章です。段落（句読点）を正しく折り返す、次の行は継続します。";
    private const string Simplified = "这是一个中文段落，包含标点（以及）数字123.45。测试换行规则！";
    private const string Traditional = "繁體中文測試，標點與文字換行規則。";
    private static readonly string[] Dictionary = { "ex-tra-or-di-nary" };

    private static string Family(string suffix) => "Noto Sans CJK " + suffix;
    private static ConversionOptions Options(EastAsianLineBreakLanguage language, string suffix, FidelityMode mode = FidelityMode.BestEffort)
    {
        var options = new ConversionOptions(mode) { Clock = () => new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero) };
        options.Typography.EastAsianLineBreak = language;
        options.Fonts.Sources.Add(FontSource.FromFile(Family(suffix), Path.Combine(TestDocuments.FixtureDirectory, "EastAsian", "RoadmapCJK-" + suffix + ".ttf")));
        return options;
    }
    private static byte[] Input(string text, string suffix = "JP", int pageWidth = 2400)
    {
        XNamespace w = TestDocuments.W;
        var paragraph = new XElement(w + "p", new XElement(w + "r", new XElement(w + "rPr", new XElement(w + "rFonts",
            new XAttribute(w + "ascii", Family(suffix)), new XAttribute(w + "hAnsi", Family(suffix)), new XAttribute(w + "eastAsia", Family(suffix)))),
            new XElement(w + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text)));
        string section = "<w:sectPr><w:pgSz w:w='" + pageWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "' w:h='15840'/><w:pgMar w:top='720' w:bottom='720' w:left='600' w:right='600' w:header='0' w:footer='0' w:gutter='0'/></w:sectPr>";
        return TestDocuments.Docx(TestDocuments.Body(paragraph + section));
    }

    [Theory]
    [InlineData(EastAsianLineBreakLanguage.Japanese, "JP", Japanese)]
    [InlineData(EastAsianLineBreakLanguage.SimplifiedChinese, "SC", Simplified)]
    [InlineData(EastAsianLineBreakLanguage.TraditionalChinese, "TC", Traditional)]
    public void ManagedPdfKeepsLogicalTextAndDoesNotStartOrEndLinesWithProhibitedPunctuation(EastAsianLineBreakLanguage language, string suffix, string text)
    {
        byte[] source = Input(text, suffix); using var input = new MemoryStream(source); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, Options(language, suffix), TestContext.Current.CancellationToken);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "Font.MissingGlyph");
        Assert.Equal(suffix == "SC", result.Diagnostics.Any(diagnostic => diagnostic.Code == "Layout.EastAsianMixedReference"));
        Assert.Equal(suffix == "JP", result.Diagnostics.Any(diagnostic => diagnostic.Code == "Layout.EastAsianPunctuationReference"));
        Assert.All(result.Diagnostics.Where(diagnostic => diagnostic.Code is "Layout.EastAsianMixedReference" or "Layout.EastAsianPunctuationReference"), diagnostic =>
        {
            Assert.NotEmpty(diagnostic.Occurrences);
            Assert.All(diagnostic.Occurrences, occurrence => Assert.Equal(DiagnosticPhase.Layout, occurrence.Phase));
        });
        using PdfDocument pdf = PdfDocument.Open(output.ToArray()); var page = Assert.Single(pdf.GetPages());
        Assert.Equal(text, string.Concat(page.Letters.Select(letter => letter.Value)));
        string[] lines = page.Letters.GroupBy(letter => Math.Round(letter.StartBaseLine.Y, 1)).Select(line => string.Concat(line.Select(letter => letter.Value))).ToArray();
        Assert.True(lines.Length > 1);
        Assert.All(lines, line =>
        {
            Assert.DoesNotContain(line[0].ToString(), "、。，．））」』！？", StringComparison.Ordinal);
            Assert.DoesNotContain(line[line.Length - 1].ToString(), "（「『", StringComparison.Ordinal);
        });
    }

    [Fact]
    public void MissingGlyphAndUnqualifiedGraphemeOrOutputFailWithoutPublishing()
    {
        foreach (string text in new[] { "日本語😀", "日本語e\u0301", "日本語\u2060文字" })
        {
            using var input = new MemoryStream(Input(text)); using var output = new MemoryStream(); output.WriteByte(42);
            var error = Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf,
                Options(EastAsianLineBreakLanguage.Japanese, "JP"), TestContext.Current.CancellationToken));
            Assert.StartsWith("Layout.EastAsianOwner:", error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, output.ToArray());
        }
        using var missing = new MemoryStream(Input("日本語龍")); using var missingOutput = new MemoryStream(); missingOutput.WriteByte(42);
        var glyph = Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(missing, missingOutput, DocumentFormat.Docx, DocumentFormat.Pdf,
            Options(EastAsianLineBreakLanguage.Japanese, "JP"), TestContext.Current.CancellationToken));
        Assert.StartsWith("Layout.EastAsianGlyph:", glyph.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, missingOutput.ToArray());
        using var source = new MemoryStream(Input(Japanese)); using var destination = new MemoryStream(); destination.WriteByte(42);
        var profile = Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(source, destination, DocumentFormat.Docx, DocumentFormat.Xps,
            Options(EastAsianLineBreakLanguage.Japanese, "JP"), TestContext.Current.CancellationToken));
        Assert.StartsWith("Layout.EastAsianProfile:", profile.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, destination.ToArray());
    }

    [Fact]
    public void ManagedBreaksCanUseTheBoundedEnglishHyphenationDictionary()
    {
        var options = Options(EastAsianLineBreakLanguage.Japanese, "JP"); options.Hyphenation.Dictionary = new HyphenationLexicon("en-US", Dictionary);
        using var source = new MemoryStream(Input("日本語 extraordinary 文章", pageWidth: 3000)); using var destination = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(source, destination, DocumentFormat.Docx, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Layout.EastAsianMixedReference");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Hyphenation.SearchGeometry");
        using PdfDocument pdf = PdfDocument.Open(destination.ToArray()); Assert.Contains("extraordinary", pdf.GetPage(1).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void MixedReferenceDifferenceRejectsStrictOutputBeforePublishing()
    {
        using var input = new MemoryStream(Input(Simplified, "SC")); using var output = new MemoryStream(); output.WriteByte(42);
        var error = Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            Options(EastAsianLineBreakLanguage.SimplifiedChinese, "SC", FidelityMode.Strict), TestContext.Current.CancellationToken));
        Assert.StartsWith("Layout.EastAsianMixedReference:", error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void JapanesePunctuationReferenceDifferenceRejectsStrictOutputBeforePublishing()
    {
        using var input = new MemoryStream(Input(Japanese)); using var output = new MemoryStream(); output.WriteByte(42);
        var error = Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            Options(EastAsianLineBreakLanguage.Japanese, "JP", FidelityMode.Strict), TestContext.Current.CancellationToken));
        Assert.StartsWith("Layout.EastAsianPunctuationReference:", error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void PlainJapaneseCanUseStrictManagedLayout()
    {
        const string text = "日本語の文章です日本語の文章です";
        using var input = new MemoryStream(Input(text)); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            Options(EastAsianLineBreakLanguage.Japanese, "JP", FidelityMode.Strict), TestContext.Current.CancellationToken);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.StartsWith("Layout.EastAsian", StringComparison.Ordinal));
        using PdfDocument pdf = PdfDocument.Open(output.ToArray()); Assert.Equal(text, string.Concat(pdf.GetPage(1).Letters.Select(letter => letter.Value)));
    }

    [Fact]
    public void NonbreakingSpaceKeepsAdjacentCjkCharactersOnTheSameLine()
    {
        const string text = "日本語\u00a0文章です";
        using var source = new MemoryStream(Input(text, pageWidth: 1860)); using var destination = new MemoryStream();
        new WordConverter().Convert(source, destination, DocumentFormat.Docx, DocumentFormat.Pdf,
            Options(EastAsianLineBreakLanguage.Japanese, "JP", FidelityMode.Strict), TestContext.Current.CancellationToken);
        using PdfDocument pdf = PdfDocument.Open(destination.ToArray()); var letters = pdf.GetPage(1).Letters.ToArray();
        Assert.Equal(text, string.Concat(letters.Select(letter => letter.Value)));
        double before = letters.Single(letter => letter.Value == "語").StartBaseLine.Y;
        double after = letters.Single(letter => letter.Value == "文").StartBaseLine.Y;
        Assert.InRange(Math.Abs(before - after), 0, .01);
    }

    [Fact]
    public void OrdinarySpacesRemainInLogicalTextAcrossWrapping()
    {
        const string text = "日本語 文章 です";
        using var source = new MemoryStream(Input(text, pageWidth: 1860)); using var destination = new MemoryStream();
        new WordConverter().Convert(source, destination, DocumentFormat.Docx, DocumentFormat.Pdf,
            Options(EastAsianLineBreakLanguage.Japanese, "JP", FidelityMode.Strict), TestContext.Current.CancellationToken);
        using PdfDocument pdf = PdfDocument.Open(destination.ToArray());
        Assert.Equal(text, string.Concat(pdf.GetPage(1).Letters.Select(letter => letter.Value)));
    }

    [Fact]
    public void LanguageOptionsAndCancellationAreExplicit()
    {
        var options = Options(EastAsianLineBreakLanguage.Japanese, "JP"); options.Typography.EastAsianLineBreak = (EastAsianLineBreakLanguage)100;
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Typography.Snapshot());
        options.Typography.EastAsianLineBreak = EastAsianLineBreakLanguage.Japanese; options.Typography.Enabled = true;
        Assert.Throws<ArgumentException>(() => options.Typography.Snapshot());
        options.Typography.Enabled = false; using var cancel = new CancellationTokenSource(); cancel.Cancel();
        using var input = new MemoryStream(Input(Japanese)); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.ThrowsAny<OperationCanceledException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, options, cancel.Token));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void MultipleRunsAndAnUnbreakableWideSpanFailBeforeOutput()
    {
        const string family = "<w:rPr><w:rFonts w:ascii='Noto Sans CJK JP' w:hAnsi='Noto Sans CJK JP'/></w:rPr>";
        byte[] split = TestDocuments.Docx(TestDocuments.Body("<w:p><w:r>" + family + "<w:t>日本</w:t></w:r><w:r>" + family + "<w:t>語</w:t></w:r></w:p>"));
        using var splitInput = new MemoryStream(split); using var splitOutput = new MemoryStream(); splitOutput.WriteByte(42);
        var owner = Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(splitInput, splitOutput, DocumentFormat.Docx, DocumentFormat.Pdf,
            Options(EastAsianLineBreakLanguage.Japanese, "JP"), TestContext.Current.CancellationToken));
        Assert.StartsWith("Layout.EastAsianOwner:", owner.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, splitOutput.ToArray());
        using var longInput = new MemoryStream(Input("日本語" + new string('A', 100))); using var longOutput = new MemoryStream(); longOutput.WriteByte(42);
        var width = Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(longInput, longOutput, DocumentFormat.Docx, DocumentFormat.Pdf,
            Options(EastAsianLineBreakLanguage.Japanese, "JP"), TestContext.Current.CancellationToken));
        Assert.StartsWith("Layout.LineBreakWidth:", width.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, longOutput.ToArray());
    }

    [Fact]
    public void HyphenationDoesNotMoveARequiredTrailingSpaceToTheNextLine()
    {
        var options = Options(EastAsianLineBreakLanguage.Japanese, "JP"); options.Hyphenation.Dictionary = new HyphenationLexicon("en-US", Dictionary);
        using var input = new MemoryStream(Input("日 ", pageWidth: 1430)); using var output = new MemoryStream(); output.WriteByte(42);
        var error = Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
        Assert.StartsWith("Layout.LineBreakWidth:", error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, output.ToArray());
    }
}
