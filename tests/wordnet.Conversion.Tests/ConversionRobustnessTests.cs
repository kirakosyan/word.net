using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace WordNet.Tests;

public sealed class ConversionRobustnessTests
{
    private readonly WordConverter converter = new WordConverter();

    [Theory]
    [InlineData("input")]
    [InlineData("output")]
    [InlineData("characters")]
    public void TextResourceLimitsFailBeforeOutputIsWritten(string limit)
    {
        var options = new ConversionOptions(maximumInputBytes: limit == "input" ? 2 : 1024,
            maximumOutputBytes: limit == "output" ? 32 : 4096,
            maximumCharacters: limit == "characters" ? 2 : 1024);
        FailsWithoutWriting<InvalidDataException>(Encoding.UTF8.GetBytes("content"), DocumentFormat.Text, options);
    }

    [Theory]
    [InlineData("entries")]
    [InlineData("expanded")]
    [InlineData("nesting")]
    [InlineData("characters")]
    public void PackageResourceLimitsAreEnforced(string limit)
    {
        byte[] docx = TestDocuments.Docx(TestDocuments.Body(TestDocuments.TextParagraph("content")));
        var options = new ConversionOptions(maximumArchiveEntries: limit == "entries" ? 1 : 10,
            maximumExpandedBytes: limit == "expanded" ? 20 : 4096,
            maximumNestingDepth: limit == "nesting" ? 1 : 64,
            maximumCharacters: limit == "characters" ? 10 : 4096);
        FailsWithoutWriting<InvalidDataException>(docx, DocumentFormat.Docx, options);
    }

    [Fact]
    public void DuplicateMainPartIsRejected()
    {
        string document = TestDocuments.Body(TestDocuments.TextParagraph("one"));
        byte[] docx = TestDocuments.Docx(document, extra: new KeyValuePair<string, string>("word/document.xml", document));
        InvalidDataException error = FailsWithoutWriting<InvalidDataException>(docx, DocumentFormat.Docx);
        Assert.Contains("Docx.DuplicatePart", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../outside.xml")]
    [InlineData("word/../outside.xml")]
    [InlineData("word\\bad.xml")]
    [InlineData("word/%2e%2e/bad.xml")]
    public void UnsafePartNamesAreRejected(string name)
    {
        byte[] docx = TestDocuments.Docx(TestDocuments.Body(TestDocuments.TextParagraph("text")), extra: new KeyValuePair<string, string>(name, "payload"));
        FailsWithoutWriting<InvalidDataException>(docx, DocumentFormat.Docx);
    }

    [Fact]
    public void XmlDoctypeAndExternalEntityAreProhibited()
    {
        string xml = "<!DOCTYPE document [<!ENTITY secret SYSTEM 'file:///not-to-be-read'>]>"
            + TestDocuments.Body("<w:p><w:r><w:t>&secret;</w:t></w:r></w:p>");
        InvalidDataException error = FailsWithoutWriting<InvalidDataException>(TestDocuments.Docx(xml), DocumentFormat.Docx);
        Assert.Contains("Docx.Xml", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExternalMainDocumentIsNotFetched()
    {
        string relationships = "<Relationships xmlns=\"" + TestDocuments.PackageR + "\"><Relationship Id=\"main\" Type=\""
            + TestDocuments.R + "/officeDocument\" Target=\"https://example.com/document.xml\" TargetMode=\"External\"/></Relationships>";
        byte[] docx = TestDocuments.Docx(TestDocuments.Body(TestDocuments.TextParagraph("text")), rootRelationships: relationships);
        InvalidDataException error = FailsWithoutWriting<InvalidDataException>(docx, DocumentFormat.Docx);
        Assert.Contains("Docx.ExternalPart", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<w:basedOn w:val=\"Loop\"/>")]
    [InlineData("<w:rPr><w:rStyle w:val=\"Loop\"/></w:rPr>")]
    public void StyleCyclesFailWithinTheParserBudget(string cyclicProperty)
    {
        string styles = "<w:styles xmlns:w=\"" + TestDocuments.W + "\"><w:style w:styleId=\"Loop\" w:type=\"paragraph\">"
            + cyclicProperty + "</w:style></w:styles>";
        byte[] docx = TestDocuments.Docx(TestDocuments.Body("<w:p><w:pPr><w:pStyle w:val=\"Loop\"/></w:pPr><w:r><w:t>text</w:t></w:r></w:p>"), styles: styles);
        InvalidDataException error = FailsWithoutWriting<InvalidDataException>(docx, DocumentFormat.Docx);
        Assert.Contains("Docx.StyleCycle", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CharacterStyleCanToggleInheritedBoldOff()
    {
        string styles = "<w:styles xmlns:w=\"" + TestDocuments.W + "\">"
            + "<w:style w:styleId=\"Parent\" w:type=\"paragraph\"><w:rPr><w:b/></w:rPr></w:style>"
            + "<w:style w:styleId=\"Plain\" w:type=\"character\"><w:rPr><w:b/></w:rPr></w:style></w:styles>";
        string paragraph = "<w:p><w:pPr><w:pStyle w:val=\"Parent\"/></w:pPr><w:r><w:rPr><w:rStyle w:val=\"Plain\"/></w:rPr><w:t>plain</w:t></w:r></w:p>";
        using var input = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(paragraph), styles: styles));
        using var output = new MemoryStream();
        converter.Convert(input, output, DocumentFormat.Docx, DocumentFormat.Text);
        Assert.Equal("plain", Encoding.UTF8.GetString(output.ToArray()));
    }

    [Fact]
    public void DirectFormattingCanDisableInheritedBold()
    {
        string styles = "<w:styles xmlns:w=\"" + TestDocuments.W + "\"><w:style w:styleId=\"Parent\" w:type=\"paragraph\"><w:rPr><w:b/></w:rPr></w:style></w:styles>";
        string paragraph = "<w:p><w:pPr><w:pStyle w:val=\"Parent\"/></w:pPr><w:r><w:rPr><w:b w:val=\"0\"/></w:rPr><w:t>plain</w:t></w:r></w:p>";
        using var input = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(paragraph), styles: styles));
        using var output = new MemoryStream();
        converter.Convert(input, output, DocumentFormat.Docx, DocumentFormat.Text);
        Assert.Equal("plain", Encoding.UTF8.GetString(output.ToArray()));
    }

    [Theory]
    [InlineData("<w:rPr><w:b w:val=\"maybe\"/></w:rPr>")]
    [InlineData("<w:pPr><w:outlineLvl w:val=\"-1\"/></w:pPr>")]
    public void InvalidPropertyValuesAreRejected(string property)
    {
        string paragraph = property.StartsWith("<w:pPr>", StringComparison.Ordinal)
            ? "<w:p>" + property + "<w:r><w:t>text</w:t></w:r></w:p>"
            : "<w:p><w:r>" + property + "<w:t>text</w:t></w:r></w:p>";
        FailsWithoutWriting<InvalidDataException>(TestDocuments.Docx(TestDocuments.Body(paragraph)), DocumentFormat.Docx);
    }

    [Theory]
    [InlineData("<w:p><w:pPr><w:spacing w:after=\"240\"/></w:pPr><w:r><w:t>text</w:t></w:r></w:p>", "Layout.Paragraph")]
    [InlineData("<w:p><w:r><w:rPr><w:color w:val=\"ff0000\"/></w:rPr><w:t>text</w:t></w:r></w:p>", "Text.Formatting")]
    [InlineData("<w:p><w:ins><w:r><w:t>inserted</w:t></w:r></w:ins></w:p>", "Docx.Inline")]
    public void UnsupportedWordFeaturesAreExplicit(string paragraph, string code)
    {
        NotSupportedException error = FailsWithoutWriting<NotSupportedException>(TestDocuments.Docx(TestDocuments.Body(paragraph)), DocumentFormat.Docx);
        Assert.Contains(code, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidUtf8FailsWithoutReplacementCharacters()
    {
        FailsWithoutWriting<InvalidDataException>(new byte[] { 0xc3, 0x28 }, DocumentFormat.Text);
    }

    [Fact]
    public void XmlInvalidControlCharactersFailWithoutCorruptOutput()
    {
        InvalidDataException error = FailsWithoutWriting<InvalidDataException>(Encoding.UTF8.GetBytes("before\0after"), DocumentFormat.Text);
        Assert.Contains("Text.XmlCharacter", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RenamingPlainTextToDocxDoesNotMakeItAWordDocument()
    {
        FailsWithoutWriting<InvalidDataException>(Encoding.UTF8.GetBytes("not a zip archive"), DocumentFormat.Docx);
    }

    [Fact]
    public void TruncatedPackageFailsCleanly()
    {
        byte[] package = TestDocuments.Docx(TestDocuments.Body(TestDocuments.TextParagraph("text")));
        Array.Resize(ref package, package.Length / 2);
        FailsWithoutWriting<InvalidDataException>(package, DocumentFormat.Docx);
    }

    [Fact]
    public void InvalidArgumentsAndLimitsFailClearly()
    {
        using var stream = new MemoryStream();
        Assert.Throws<ArgumentNullException>(() => converter.Convert(null!, stream, DocumentFormat.Text, DocumentFormat.Docx));
        Assert.Throws<ArgumentNullException>(() => converter.Convert(stream, null!, DocumentFormat.Text, DocumentFormat.Docx));
        using var target = new MemoryStream();
        Assert.Throws<ArgumentNullException>(() => converter.Convert(stream, target, DocumentFormat.Text, DocumentFormat.Docx, null!, TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConversionOptions(maximumInputBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConversionOptions((FidelityMode)999));
        using var readOnly = new MemoryStream(new byte[] { 1 }, writable: false);
        Assert.Throws<ArgumentException>(() => converter.Convert(stream, readOnly, DocumentFormat.Text, DocumentFormat.Docx));
    }

    private TException FailsWithoutWriting<TException>(byte[] input, DocumentFormat format, ConversionOptions? options = null)
        where TException : Exception
    {
        using var source = new MemoryStream(input);
        using var target = new MemoryStream();
        target.WriteByte(42);
        TException result = Assert.Throws<TException>(() => converter.Convert(source, target, format,
            format == DocumentFormat.Docx ? DocumentFormat.Text : DocumentFormat.Docx, options ?? new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, target.ToArray());
        Assert.True(source.CanRead);
        Assert.True(target.CanWrite);
        return result;
    }
}
