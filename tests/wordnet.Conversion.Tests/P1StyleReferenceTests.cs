using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed class P1StyleReferenceTests
{
    private static readonly XNamespace W = TestDocuments.W;
    private static string P(string text, bool styled = false, bool pageBreak = false) => "<w:p><w:pPr>" + (styled ? "<w:pStyle w:val='Topic'/>" : "") + (pageBreak ? "<w:pageBreakBefore/>" : "") + "</w:pPr><w:r><w:rPr><w:rFonts w:ascii='Noto Sans' w:hAnsi='Noto Sans'/></w:rPr><w:t>" + text + "</w:t></w:r></w:p>";
    private static string Section(string references, string properties = "") => "<w:sectPr>" + references + "<w:pgSz w:w='8000' w:h='6000'/><w:pgMar w:top='1000' w:bottom='600' w:left='600' w:right='600' w:header='200' w:footer='200'/>" + properties + "</w:sectPr>";
    private static byte[] Source(string body, string instruction, bool variants = false, string? extraStyles = null)
    {
        string styles = "<w:styles xmlns:w='" + TestDocuments.W + "'><w:style w:type='paragraph' w:styleId='Topic'><w:name w:val='Running Topic'/><w:rPr><w:sz w:val='22'/></w:rPr></w:style>" + extraStyles + "</w:styles>";
        string relations = "", references = ""; var parts = new Dictionary<string, string>();
        foreach (string variant in variants ? new[] { "first", "even", "default" } : new[] { "default" })
        {
            relations += "<Relationship Id='" + variant + "' Type='" + TestDocuments.R + "/header' Target='" + variant + ".xml'/>";
            references += "<w:headerReference w:type='" + variant + "' r:id='" + variant + "'/>";
            parts.Add("word/" + variant + ".xml", "<w:hdr xmlns:w='" + TestDocuments.W + "'><w:p><w:r><w:t>" + variant + ":</w:t></w:r>" + P1FieldTests.Field(instruction, "cache") + "</w:p></w:hdr>");
        }
        if (variants)
        {
            relations += "<Relationship Id='settings' Type='" + TestDocuments.R + "/settings' Target='settings.xml'/>";
            parts.Add("word/settings.xml", "<w:settings xmlns:w='" + TestDocuments.W + "'><w:evenAndOddHeaders/></w:settings>");
        }
        byte[] input = TestDocuments.Docx(TestDocuments.Body(body + Section(references, variants ? "<w:titlePg/>" : "")), relationships: relations, styles: styles);
        using var output = new MemoryStream(); output.Write(input, 0, input.Length);
        using (var zip = new ZipArchive(output, ZipArchiveMode.Update, leaveOpen: true))
            foreach (var part in parts)
            { using var writer = new StreamWriter(zip.CreateEntry(part.Key).Open(), new UTF8Encoding(false)); writer.Write(part.Value); }
        return output.ToArray();
    }
    private static byte[] Export(byte[] input, DocumentFormat format = DocumentFormat.Pdf)
    {
        using var source = new MemoryStream(input); using var output = new MemoryStream();
        if (format == DocumentFormat.Docx) { WordDocument document = WordDocument.Load(source, DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken); return P1FieldTests.Save(document, format); }
        new WordConverter().Convert(source, output, DocumentFormat.Docx, format, new ConversionOptions { Clock = () => new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero) }, TestContext.Current.CancellationToken); return output.ToArray();
    }

    [Theory]
    [InlineData("Topic", false, "Beta")]
    [InlineData("Running Topic", false, "Beta")]
    [InlineData("running topic", true, "Gamma")]
    public void RunningHeadersUsePageHeadingsThenThePrecedingSectionHeading(string style, bool last, string third)
    {
        string instruction = "STYLEREF \"" + style + "\"" + (last ? " \\l" : "");
        byte[] input = Source(P("Alpha", true) + P("one") + P("two", pageBreak: true) + P("Beta", true, true) + P("Gamma", true), instruction);
        byte[] bytes = Export(input); using var pdf = PdfDocument.Open(bytes);
        Assert.Equal(3, pdf.NumberOfPages);
        Assert.Equal("default:AlphaAlphaone", pdf.GetPage(1).Text);
        Assert.Equal("default:Alphatwo", pdf.GetPage(2).Text);
        Assert.Equal("default:" + third + "BetaGamma", pdf.GetPage(3).Text);
        Assert.Equal(bytes, Export(input));
        byte[] word = Export(input, DocumentFormat.Docx); P1FieldTests.Validate(word);
        Assert.Contains("Topic", P1FieldTests.Xml(word, "word/styles.xml").ToString(), StringComparison.Ordinal);
        using var zip = new ZipArchive(new MemoryStream(word));
        var header = zip.Entries.Single(e => e.FullName.StartsWith("word/header", StringComparison.Ordinal) && e.FullName.EndsWith(".xml", StringComparison.Ordinal));
        using Stream stream = header.Open(); XElement field = XDocument.Load(stream).Descendants(W + "fldSimple").Single();
        Assert.Equal(instruction, (string?)field.Attribute(W + "instr")); Assert.Equal("cache", field.Value);
        RoadmapLayoutTests.Evidence("p1-styleref-" + (last ? "last" : style == "Topic" ? "id" : "name"), input, bytes);
    }

    [Fact]
    public void FirstEvenAndDefaultVariantsUsePhysicalPagesWithPrecedingFallback()
    {
        byte[] input = Source(P("Alpha", true) + P("two", pageBreak: true) + P("Beta", true, true), "STYLEREF Topic", variants: true);
        using var pdf = PdfDocument.Open(Export(input));
        Assert.StartsWith("first:Alpha", pdf.GetPage(1).Text, StringComparison.Ordinal);
        Assert.StartsWith("even:Alpha", pdf.GetPage(2).Text, StringComparison.Ordinal);
        Assert.StartsWith("default:Beta", pdf.GetPage(3).Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("STYLEREF Missing")]
    [InlineData("STYLEREF Topic \\n")]
    [InlineData("STYLEREF Topic \\l junk")]
    public void MissingStylesAndUnsupportedSwitchesRetainTheirCache(string instruction)
    {
        using var pdf = PdfDocument.Open(Export(Source(P("Alpha", true), instruction)));
        Assert.Equal("default:cacheAlpha", pdf.GetPage(1).Text);
    }

    [Fact]
    public void AmbiguousDisplayNamesDoNotChooseAnArbitraryStyle()
    {
        string extra = "<w:style w:type='paragraph' w:styleId='Other'><w:name w:val='Running Topic'/></w:style>";
        using var pdf = PdfDocument.Open(Export(Source(P("Alpha", true), "STYLEREF \"Running Topic\"", extraStyles: extra)));
        Assert.Equal("default:cacheAlpha", pdf.GetPage(1).Text);
    }

    [Fact]
    public void SectionBoundariesDoNotReuseThePreviousSectionsHeading()
    {
        string previousSection = Section("<w:headerReference w:type='default' r:id='default'/>");
        byte[] input = Source(P("Old", true).Replace("</w:pPr>", previousSection + "</w:pPr>") + P("New"), "STYLEREF Topic");
        using var pdf = PdfDocument.Open(Export(input)); Assert.Equal(2, pdf.NumberOfPages);
        Assert.Equal("default:OldOld", pdf.GetPage(1).Text); Assert.Equal("default:cacheNew", pdf.GetPage(2).Text);
    }

    [Fact]
    public void UnusableCalculatedStoryReservationFailsBeforeWriting()
    {
        byte[] input = Source(P(new string('W', 600), true), "STYLEREF Topic");
        using var source = new MemoryStream(input); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(source, output, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void BodyUsesAreCachedAndDoNotSubstituteDocumentTitles()
    {
        byte[] input = Source(P("Alpha", true) + "<w:p>" + P1FieldTests.Field("STYLEREF Topic", "body cache") + "</w:p>", "STYLEREF Topic");
        using var source = new MemoryStream(input); WordDocument document = WordDocument.Load(source, DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken); document.Title = "unrelated";
        using var pdf = PdfDocument.Open(P1FieldTests.Save(document, DocumentFormat.Pdf)); Assert.Equal("default:AlphaAlphabody cache", pdf.GetPage(1).Text);
    }
}
