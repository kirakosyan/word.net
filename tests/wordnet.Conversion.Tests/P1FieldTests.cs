using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Threading;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed class P1FieldTests
{
    private static readonly string[] PropertyNames = { "Count", "Empty", "Flag", "When" };
    private static readonly string[] PicturedValues = { "-1,234.57", "2026-10-26 12:30" };
    private static readonly XNamespace W = TestDocuments.W;
    internal static string Field(string instruction, string cache = "stale") => "<w:fldSimple w:instr='" + SecurityElement.Escape(instruction) + "'><w:r><w:t>" + SecurityElement.Escape(cache) + "</w:t></w:r></w:fldSimple>";
    private static WordDocument Load(string contents, ConversionOptions? options = null)
    {
        using var input = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(contents)));
        return WordDocument.Load(input, DocumentFormat.Docx, out _, options, TestContext.Current.CancellationToken);
    }
    internal static byte[] Save(WordDocument document, DocumentFormat format)
    {
        using var output = new MemoryStream(); document.Save(output, format, cancellationToken: TestContext.Current.CancellationToken); return output.ToArray();
    }
    internal static XDocument Xml(byte[] bytes, string part)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes)); using Stream input = zip.GetEntry(part)!.Open(); return XDocument.Load(input);
    }
    internal static void Validate(byte[] bytes)
    {
        using var input = new MemoryStream(bytes); using var package = WordprocessingDocument.Open(input, false);
        Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
    }
    private static string Pdf(WordDocument document)
    { using var pdf = PdfDocument.Open(Save(document, DocumentFormat.Pdf)); return string.Concat(pdf.GetPages().Select(p => p.Text)); }

    [Fact]
    public void TypedPropertiesAreImmutableAtomicAndSchemaValidated()
    {
        WordDocument document = Load("<w:p>" + Field("DOCPROPERTY Empty") + "</w:p>");
        document.SetCustomProperty(new DocumentProperty("Empty", ""), TestContext.Current.CancellationToken);
        var snapshot = document.CustomProperties;
        document.SetCustomProperty(new DocumentProperty("Count", 123.45m), TestContext.Current.CancellationToken);
        document.SetCustomProperty(new DocumentProperty("Flag", true), TestContext.Current.CancellationToken);
        document.SetCustomProperty(new DocumentProperty("When", new DateTimeOffset(2026, 10, 26, 14, 30, 0, TimeSpan.FromHours(2))), TestContext.Current.CancellationToken);
        Assert.Single(snapshot); Assert.Equal("", snapshot[0].Value);
        byte[] bytes = Save(document, DocumentFormat.Docx); Validate(bytes);
        XNamespace vt = "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";
        XElement[] properties = Xml(bytes, "docProps/custom.xml").Root!.Elements().ToArray();
        Assert.Equal(PropertyNames, properties.Select(p => (string?)p.Attribute("name")));
        Assert.Equal("123.45", properties[0].Element(vt + "r8")!.Value);
        Assert.Equal("true", properties[2].Element(vt + "bool")!.Value);
        Assert.Equal("2026-10-26T12:30:00.0000000Z", properties[3].Element(vt + "filetime")!.Value);
        using var input = new MemoryStream(bytes); WordDocument reloaded = WordDocument.Load(input, DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(4, reloaded.CustomProperties.Count); Assert.Equal(123.45m, reloaded.CustomProperties[0].Value);
        document.SetCustomProperty(new DocumentProperty("count", 2m), TestContext.Current.CancellationToken);
        Assert.Equal(4, document.CustomProperties.Count); Assert.True(document.RemoveCustomProperty("COUNT", TestContext.Current.CancellationToken));
        Assert.False(document.RemoveCustomProperty("absent", TestContext.Current.CancellationToken));
        Assert.Equal("", Pdf(document));
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("tr-TR")]
    public void PicturesAreInvariantAndDoNotConsultTheClock(string culture)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            WordDocument document = Load("<w:p>" + Field("DOCPROPERTY Amount \\# \"#,##0.00\"") + "</w:p><w:p>" + Field("DOCPROPERTY When \\@ \"yyyy-MM-dd HH:mm\"") + "</w:p>", new ConversionOptions { Clock = () => new DateTimeOffset(2001, 2, 3, 4, 5, 6, TimeSpan.Zero) });
            document.SetCustomProperty(new DocumentProperty("Amount", -1234.565m), TestContext.Current.CancellationToken);
            document.SetCustomProperty(new DocumentProperty("When", new DateTimeOffset(2026, 10, 26, 14, 30, 0, TimeSpan.FromHours(2))), TestContext.Current.CancellationToken);
            byte[] word = Save(document, DocumentFormat.Docx); Validate(word);
            Assert.Equal(PicturedValues, Xml(word, "word/document.xml").Descendants(W + "fldSimple").Select(e => e.Value));
            Assert.Equal("-1,234.572026-10-26 12:30", Pdf(document));
            XDocument xps = Xml(Save(document, DocumentFormat.Xps), "Documents/1/Pages/1.fpage");
            Assert.Equal("-1,234.572026-10-26 12:30", string.Concat(xps.Descendants().Where(e => e.Name.LocalName == "Glyphs").Select(e => (string?)e.Attribute("UnicodeString"))));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("= 2.345 \\# \"0.00\"", "2.35")]
    [InlineData("= -2.345 \\# \"0.00\"", "-2.35")]
    [InlineData("= 2.3 \\# \"0.###\"", "2.3")]
    [InlineData("= 2 \\# \"'USD '0.00\" \\* MERGEFORMAT", "USD 2.00")]
    public void NumericPicturesFollowTheSpecifiedRoundingAndLiteralRules(string instruction, string expected)
    { Assert.Equal(expected, Pdf(Load("<w:p>" + Field(instruction) + "</w:p>"))); }

    [Theory]
    [InlineData("= 1 \\# \"0.00E+00\"")]
    [InlineData("= 1 \\# \"0.00%;(0.00)\"")]
    [InlineData("= 1 \\# \"0.00000000000\"")]
    [InlineData("DOCPROPERTY Amount \\@ \"yyyy\"")]
    [InlineData("DOCPROPERTY When \\@ \"yyyy zzz\"")]
    [InlineData("DOCPROPERTY Empty \\@ \"yyyy\"")]
    public void UnsupportedPicturesAndTypesKeepTheCompleteCache(string instruction)
    {
        WordDocument document = Load("<w:p>" + Field(instruction) + "</w:p>");
        document.SetCustomProperty(new DocumentProperty("Amount", 4m), TestContext.Current.CancellationToken);
        document.SetCustomProperty(new DocumentProperty("When", DateTimeOffset.MinValue), TestContext.Current.CancellationToken);
        document.SetCustomProperty(new DocumentProperty("Empty", ""), TestContext.Current.CancellationToken);
        using var output = new MemoryStream(); ConversionResult result = document.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "Field.UnsupportedUsedCachedResult");
        using var pdf = PdfDocument.Open(output.ToArray()); Assert.Equal("stale", pdf.GetPage(1).Text);
    }

    [Fact]
    public void TypedPropertiesEvaluateInCellsAndHeaderStories()
    {
        string body = "<w:tbl><w:tr><w:tc><w:p>" + Field("DOCPROPERTY Amount") + "</w:p></w:tc></w:tr></w:tbl>"
            + "<w:sectPr><w:headerReference w:type='default' r:id='header'/></w:sectPr>";
        byte[] input = TestDocuments.Docx(TestDocuments.Body(body), "<Relationship Id='header' Type='" + TestDocuments.R + "/header' Target='header.xml'/>",
            extra: new KeyValuePair<string, string>("word/header.xml", "<w:hdr xmlns:w='" + TestDocuments.W + "'><w:p>" + Field("DOCPROPERTY Flag") + "</w:p></w:hdr>"));
        using var source = new MemoryStream(input); WordDocument document = WordDocument.Load(source, DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken);
        document.SetCustomProperty(new DocumentProperty("Amount", 12m), TestContext.Current.CancellationToken);
        document.SetCustomProperty(new DocumentProperty("Flag", true), TestContext.Current.CancellationToken);
        Assert.Equal("true12", Pdf(document)); Validate(Save(document, DocumentFormat.Docx));
    }

    [Fact]
    public void PropertyLimitsAndCancellationLeaveTheSessionAndOutputUnchanged()
    {
        WordDocument document = WordDocument.Create(new ConversionOptions(maximumCharacters: 32));
        document.SetCustomProperty(new DocumentProperty("Small", "value"), TestContext.Current.CancellationToken);
        Assert.Throws<InvalidDataException>(() => document.SetCustomProperty(new DocumentProperty("Large", new string('x', 33)), TestContext.Current.CancellationToken));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => document.SetCustomProperty(new DocumentProperty("Other", 3m), cancellation.Token));
        Assert.Single(document.CustomProperties);
        using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<OperationCanceledException>(() => document.Save(output, DocumentFormat.Docx, cancellationToken: cancellation.Token));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentPropertyInputRejectsDuplicateNamesAndInvalidNumbers(bool duplicate)
    {
        string property = "<property fmtid='{D5CDD505-2E9C-101B-9397-08002B2CF9AE}' pid='2' name='Code'><vt:decimal>" + (duplicate ? "4" : "NaN") + "</vt:decimal></property>";
        if (duplicate) property += "<property fmtid='{D5CDD505-2E9C-101B-9397-08002B2CF9AE}' pid='3' name='code'><vt:lpwstr>x</vt:lpwstr></property>";
        string xml = "<Properties xmlns='http://schemas.openxmlformats.org/officeDocument/2006/custom-properties' xmlns:vt='http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes'>" + property + "</Properties>";
        byte[] bytes = TestDocuments.Docx(TestDocuments.Body("<w:p/>"), rootRelationships: "<Relationships xmlns='" + TestDocuments.PackageR + "'><Relationship Id='main' Type='" + TestDocuments.R + "/officeDocument' Target='word/document.xml'/><Relationship Id='custom' Type='" + TestDocuments.R + "/custom-properties' Target='docProps/custom.xml'/></Relationships>", extra: new KeyValuePair<string, string>("docProps/custom.xml", xml));
        using var input = new MemoryStream(bytes); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    private const string Begin = "<w:r><w:fldChar w:fldCharType='begin'/></w:r>";
    private const string Separate = "<w:r><w:fldChar w:fldCharType='separate'/></w:r>";
    private const string End = "<w:r><w:fldChar w:fldCharType='end'/></w:r>";
    private static string Code(string text) => "<w:r><w:instrText xml:space='preserve'>" + SecurityElement.Escape(text) + "</w:instrText></w:r>";
    private static string Nested(string instruction) => Begin + Code(instruction) + Separate + "<w:r><w:t>outer cache</w:t></w:r>" + End;

    [Theory]
    [InlineData(2, "low")]
    [InlineData(12, "high")]
    public void NestedTypedIfPreservesExecutableBoundariesWithoutTextInjection(int number, string expected)
    {
        string contents = "<w:p>" + Nested("IF ").Replace(Separate, Field("DOCPROPERTY Amount") + Code(" >= 10 \"high\" \"low\" ") + Separate) + "</w:p>";
        WordDocument document = Load(contents); document.SetCustomProperty(new DocumentProperty("Amount", (decimal)number), TestContext.Current.CancellationToken);
        Assert.Equal(expected, Pdf(document));
        byte[] word = Save(document, DocumentFormat.Docx); Validate(word);
        XDocument xml = Xml(word, "word/document.xml");
        Assert.Equal(3, xml.Descendants(W + "fldChar").Count()); Assert.Single(xml.Descendants(W + "fldSimple"));
        Assert.DoesNotContain("\ufffc", xml.ToString(), StringComparison.Ordinal);
        using var source = new MemoryStream(word); WordDocument reloaded = WordDocument.Load(source, DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken);
        reloaded.SetCustomProperty(new DocumentProperty("Amount", number == 2 ? 12m : 2m), TestContext.Current.CancellationToken);
        Assert.Equal(number == 2 ? "high" : "low", Pdf(reloaded));
    }

    [Fact]
    public void NestedQuotedValuesCannotInjectInstructionsAndBranchesMayBeNested()
    {
        string contents = "<w:p>" + Begin + Code("IF \"") + Field("DOCPROPERTY Text") + Code("\" = \"safe\" ") + Field("IF 1 = 1 \"yes\" \"no\"") + Code(" \"fallback\"") + Separate + "<w:r><w:t>cache</w:t></w:r>" + End + "</w:p>";
        WordDocument document = Load(contents); document.SetCustomProperty(new DocumentProperty("Text", "\" = \"safe\" \"injected\" \"ignored"), TestContext.Current.CancellationToken);
        Assert.Equal("fallback", Pdf(document));
        document.SetCustomProperty(new DocumentProperty("Text", "safe"), TestContext.Current.CancellationToken); Assert.Equal("yes", Pdf(document));
    }

    [Theory]
    [InlineData("DOCPROPERTY Missing")]
    [InlineData("= 1 / 0")]
    [InlineData("INCLUDETEXT https://example.com/data")]
    public void UnsupportedNestedChildKeepsTheOuterCache(string child)
    {
        WordDocument document = Load("<w:p>" + Begin + Code("IF ") + Field(child) + Code(" = 1 \"yes\" \"no\"") + Separate + "<w:r><w:t>outer</w:t></w:r>" + End + "</w:p>");
        Assert.Equal("outer", Pdf(document));
    }

    [Fact]
    public void ExcessivelyDeepNestedFieldsRetainTheRootCache()
    {
        string field = Field("= 1");
        for (int i = 0; i < 17; i++) field = Begin + Code("IF 1 = 1 ") + field + Code(" \"no\"") + Separate + "<w:r><w:t>deep cache</w:t></w:r>" + End;
        WordDocument document = Load("<w:p>" + field + "</w:p>"); Assert.Equal("deep cache", Pdf(document));
        Validate(Save(document, DocumentFormat.Docx));
    }

    [Fact]
    public void MalformedFieldBoundariesFailBeforeOutput()
    {
        using var input = new MemoryStream(TestDocuments.Docx(TestDocuments.Body("<w:p>" + Begin + Code("IF 1 = 1 \"yes\" \"no\"") + Separate + End + End + "</w:p>")));
        using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }
}
