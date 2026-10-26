using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UglyToad.PdfPig;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Layout;
using WordNet.Model;
using Xunit;

namespace WordNet.Tests;

public sealed class P1ReviewRegressionTests
{
    private static readonly int[] TwoItems = { 1, 2 };
    private static readonly XNamespace W = TestDocuments.W;
    private const string Begin = "<w:r><w:fldChar w:fldCharType='begin'/></w:r>";
    private const string Separate = "<w:r><w:fldChar w:fldCharType='separate'/></w:r>";
    private const string End = "<w:r><w:fldChar w:fldCharType='end'/></w:r>";
    private static string Code(string text) => new XElement(W + "r", new XElement(W + "instrText", new XAttribute(XNamespace.Xml + "space", "preserve"), text)).ToString();
    private static string Nested => Begin + Code("IF ") + P1FieldTests.Field("DOCPROPERTY Status", "old") + Code(" = \"Final\" \"Yes\" \"No\"") + Separate + "<w:r><w:t>outer cache</w:t></w:r>" + End;
    private static WordDocument Load(string body, string? styles = null, ConversionOptions? options = null)
    {
        using var source = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(body), styles: styles));
        return WordDocument.Load(source, DocumentFormat.Docx, out _, options, TestContext.Current.CancellationToken);
    }
    private static byte[] Convert(byte[] bytes, DocumentFormat format, FidelityMode mode, out ConversionResult result)
    {
        using var source = new MemoryStream(bytes); using var output = new MemoryStream();
        result = new WordConverter().Convert(source, output, DocumentFormat.Docx, format, new ConversionOptions(mode), TestContext.Current.CancellationToken); return output.ToArray();
    }
    private static string Pdf(WordDocument document)
    { using var pdf = PdfDocument.Open(P1FieldTests.Save(document, DocumentFormat.Pdf)); return string.Concat(pdf.GetPages().Select(p => p.Text)); }

    [Theory]
    [InlineData(CompositionStyle.KeepSource)]
    [InlineData(CompositionStyle.UseDestination)]
    public void CompositionRetainsNestedFieldsAndTypedPropertyMetadata(CompositionStyle style)
    {
        WordDocument source = Load("<w:p>" + Nested + "</w:p>"); source.SetCustomProperty(new DocumentProperty("Status", "Final"), TestContext.Current.CancellationToken);
        WordDocument destination = WordDocument.Create(); destination.AddParagraph("Start", cancellationToken: TestContext.Current.CancellationToken);
        destination.Append(source, style, TestContext.Current.CancellationToken);
        Assert.Equal("StartYes", Pdf(destination)); Assert.Single(destination.CustomProperties);
        byte[] word = P1FieldTests.Save(destination, DocumentFormat.Docx); P1FieldTests.Validate(word);
        XDocument xml = P1FieldTests.Xml(word, "word/document.xml"); Assert.DoesNotContain("\ufffc", xml.ToString(), StringComparison.Ordinal);
        Assert.Contains("DOCPROPERTY Status", xml.ToString(), StringComparison.Ordinal);
        Assert.Equal("Yes", Pdf(source));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InlineControlsAndConditionCleaningRetainTheFieldTree(bool execute)
    {
        WordDocument document = Load("<w:p><w:sdt><w:sdtPr><w:tag w:val='if:Show'/></w:sdtPr><w:sdtContent>" + Nested + "</w:sdtContent></w:sdt></w:p>");
        document.SetCustomProperty(new DocumentProperty("Status", "Final"), TestContext.Current.CancellationToken);
        if (execute) document.Merge(new { Show = true }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("Yes", PdfWithTemplateLoss(document));
        byte[] word = P1FieldTests.Save(document, DocumentFormat.Docx); P1FieldTests.Validate(word);
        Assert.DoesNotContain("\ufffc", P1FieldTests.Xml(word, "word/document.xml").ToString(), StringComparison.Ordinal);
    }
    private static string PdfWithTemplateLoss(WordDocument document)
    {
        using var output = new MemoryStream(); document.Save(output, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        using var pdf = PdfDocument.Open(output.ToArray()); return pdf.GetPage(1).Text;
    }

    [Fact]
    public void RepeatedParagraphsRetainTheirNestedFieldInstructions()
    {
        WordDocument document = Load("<w:sdt><w:sdtPr><w:tag w:val='repeat:Items'/></w:sdtPr><w:sdtContent><w:p>" + Nested + "</w:p></w:sdtContent></w:sdt>");
        document.SetCustomProperty(new DocumentProperty("Status", "Final"), TestContext.Current.CancellationToken);
        document.Merge(new { Items = TwoItems }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("YesYes", Pdf(document));
        byte[] word = P1FieldTests.Save(document, DocumentFormat.Docx); P1FieldTests.Validate(word);
        Assert.DoesNotContain("\ufffc", P1FieldTests.Xml(word, "word/document.xml").ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void BinaryDocNestedFieldsRejectStrictAndUseIntactCachesInBestEffort()
    {
        byte[] input = TestDocuments.Docx(TestDocuments.Body("<w:p>" + Nested + "</w:p>"));
        using var source = new MemoryStream(input); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(source, output, DocumentFormat.Docx, DocumentFormat.Doc, new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
        byte[] doc = Convert(input, DocumentFormat.Doc, FidelityMode.BestEffort, out ConversionResult result);
        Assert.Contains(result.Diagnostics, d => d.Code == "Field.NestedOutput");
        Assert.DoesNotContain("\ufffc", Encoding.Unicode.GetString(doc), StringComparison.Ordinal);
        using var binary = new MemoryStream(doc); using var text = new MemoryStream();
        new WordConverter().Convert(binary, text, DocumentFormat.Doc, DocumentFormat.Text, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains("outer cache", Encoding.UTF8.GetString(text.ToArray()), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DocumentFormat.Doc)]
    [InlineData(DocumentFormat.Odt)]
    public void OmittedTableLayoutKeepsStrictExchangeCompatibility(DocumentFormat target)
    {
        byte[] input = TestDocuments.Docx(TestDocuments.Body("<w:tbl><w:tblGrid><w:gridCol w:w='3000'/></w:tblGrid><w:tr><w:tc><w:p><w:r><w:t>Cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>"));
        byte[] bytes = Convert(input, target, FidelityMode.Strict, out ConversionResult result);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code is "Table.RelativeWidthOutput" or "Odt.TableLoss");
        using var source = new MemoryStream(bytes); using var output = new MemoryStream();
        new WordConverter().Convert(source, output, target, DocumentFormat.Docx, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains("Cell", P1FieldTests.Xml(output.ToArray(), "word/document.xml").ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ParagraphMarkRevisionPrecedesInheritedFormattingAndPreservesColor()
    {
        string styles = "<w:styles xmlns:w='" + TestDocuments.W + "'><w:docDefaults><w:rPrDefault><w:rPr><w:sz w:val='22'/></w:rPr></w:rPrDefault></w:docDefaults></w:styles>";
        using var input = new MemoryStream(TestDocuments.Docx(TestDocuments.Body("<w:p><w:pPr><w:rPr><w:ins w:id='1' w:author='Reviewer'/><w:color w:val='FF0000'/></w:rPr></w:pPr><w:ins w:id='2' w:author='Reviewer'><w:r><w:t>Tracked</w:t></w:r></w:ins></w:p>"), styles: styles));
        WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, new ReviewLoadOptions(), out _, cancellationToken: TestContext.Current.CancellationToken);
        byte[] word = P1FieldTests.Save(document, DocumentFormat.Docx); P1FieldTests.Validate(word);
        XElement properties = P1FieldTests.Xml(word, "word/document.xml").Descendants(W + "pPr").Single().Element(W + "rPr")!;
        Assert.Equal(W + "ins", properties.Elements().First().Name); Assert.Equal("FF0000", (string?)properties.Element(W + "color")?.Attribute(W + "val"));
    }

    [Theory]
    [InlineData("Normal")]
    [InlineData("HeadingBase")]
    public void AppendedForeignStylesRetainTheirNamesWithoutInventedCodeFonts(string id)
    {
        string styles = "<w:styles xmlns:w='" + TestDocuments.W + "'><w:style w:type='paragraph' w:styleId='" + id + "'><w:name w:val='Foreign body'/></w:style></w:styles>";
        WordDocument incoming = Load("<w:p><w:pPr><w:pStyle w:val='" + id + "'/></w:pPr><w:r><w:t>Foreign</w:t></w:r></w:p>", styles);
        WordDocument destination = WordDocument.Create(); destination.Append(incoming, cancellationToken: TestContext.Current.CancellationToken);
        byte[] word = P1FieldTests.Save(destination, DocumentFormat.Docx); P1FieldTests.Validate(word);
        XDocument catalog = P1FieldTests.Xml(word, "word/styles.xml"); Assert.Contains("Foreign body", catalog.ToString(), StringComparison.Ordinal);
        Assert.Empty(catalog.Descendants(W + "rFonts")); Assert.Empty(catalog.Descendants(W + "outlineLvl"));
    }

    [Fact]
    public void AppendedRunningHeadersResolveRemappedSourceStyleNames()
    {
        string styles = "<w:styles xmlns:w='" + TestDocuments.W + "'><w:style w:type='paragraph' w:styleId='Topic'><w:name w:val='Destination topic'/></w:style></w:styles>";
        WordDocument destination = Load("<w:p><w:r><w:t>Start</w:t></w:r></w:p>", styles);
        byte[] input = P1StyleReferenceTests.Source("<w:p><w:pPr><w:pStyle w:val='Topic'/></w:pPr><w:r><w:t>Appended</w:t></w:r></w:p>", "STYLEREF \"Running Topic\"");
        using var source = new MemoryStream(input); WordDocument incoming = WordDocument.Load(source, DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken);
        destination.Append(incoming, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("Startdefault:AppendedAppended", Pdf(destination)); P1FieldTests.Validate(P1FieldTests.Save(destination, DocumentFormat.Docx));
    }

    [Fact]
    public void ConflictingCompositionPropertiesFailAtomically()
    {
        WordDocument source = Load("<w:p>" + Nested + "</w:p>"); source.SetCustomProperty(new DocumentProperty("Status", "Final"), TestContext.Current.CancellationToken);
        WordDocument destination = WordDocument.Create(); destination.SetCustomProperty(new DocumentProperty("Status", "Draft"), TestContext.Current.CancellationToken);
        Assert.Throws<NotSupportedException>(() => destination.Append(source, cancellationToken: TestContext.Current.CancellationToken)); Assert.Empty(destination.Paragraphs);
        Assert.Equal("Draft", Assert.Single(destination.CustomProperties).Value);
    }

    [Fact]
    public void CodeBlocksRetainTheirBuiltInMonospaceDefinitionAfterReload()
    {
        using var markdown = new MemoryStream(Encoding.UTF8.GetBytes("```\ncode\n```")); using var word = new MemoryStream();
        new WordConverter().Convert(markdown, word, DocumentFormat.Markdown, DocumentFormat.Docx, new ConversionOptions(), TestContext.Current.CancellationToken);
        word.Position = 0; WordDocument document = WordDocument.Load(word, DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = P1FieldTests.Save(document, DocumentFormat.Docx); P1FieldTests.Validate(saved);
        XElement fonts = P1FieldTests.Xml(saved, "word/styles.xml").Descendants(W + "style").Single(e => (string?)e.Attribute(W + "styleId") == "WordnetCodeBlock").Descendants(W + "rFonts").Single();
        Assert.Equal("Consolas", (string?)fonts.Attribute(W + "ascii"));
    }

    [Fact]
    public void LastDefaultParagraphStyleWinsWithAnInformationalDiagnostic()
    {
        string styles = "<w:styles xmlns:w='" + TestDocuments.W + "'><w:style w:type='paragraph' w:styleId='One' w:default='1'><w:name w:val='One'/><w:rPr><w:sz w:val='20'/></w:rPr></w:style><w:style w:type='paragraph' w:styleId='Two' w:default='1'><w:name w:val='Two'/><w:rPr><w:sz w:val='28'/></w:rPr></w:style></w:styles>";
        using var source = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(TestDocuments.TextParagraph("Last")), styles: styles)); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(source, output, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "Docx.MultipleDefaultStyles" && d.Severity == DiagnosticSeverity.Info);
        using var pdf = PdfDocument.Open(output.ToArray()); Assert.All(pdf.GetPage(1).Letters, letter => Assert.Equal(14, letter.FontSize, 3));
    }

    [Fact]
    public void RepeatedFieldSeparatorsAreIgnoredWithDiagnostics()
    {
        byte[] input = TestDocuments.Docx(TestDocuments.Body("<w:p>" + Begin + Code("IF 1 = 1 \"Yes\" \"No\"") + Separate + Separate + "<w:r><w:t>cache</w:t></w:r>" + End + "</w:p>"));
        byte[] bytes = Convert(input, DocumentFormat.Pdf, FidelityMode.Strict, out ConversionResult result);
        Assert.Contains(result.Diagnostics, d => d.Code == "Field.DuplicateSeparator"); using var pdf = PdfDocument.Open(bytes); Assert.Equal("Yes", pdf.GetPage(1).Text);
    }

    [Fact]
    public void SuccessfulStyleReferencesDoNotReportCachedFieldResults()
    {
        byte[] input = P1StyleReferenceTests.Source("<w:p><w:pPr><w:pStyle w:val='Topic'/></w:pPr><w:r><w:t>Heading</w:t></w:r></w:p>", "STYLEREF Topic");
        byte[] bytes = Convert(input, DocumentFormat.Pdf, FidelityMode.Strict, out ConversionResult result);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "Field.UnsupportedUsedCachedResult"); using var pdf = PdfDocument.Open(bytes); Assert.Equal("default:HeadingHeading", pdf.GetPage(1).Text);
    }

    [Fact]
    public void RepeatedRunningHeadersMeasureOnlyDistinctResultsInTheirOwnSection()
    {
        const string geometry = "<w:pgSz w:w='8000' w:h='6000'/><w:pgMar w:top='1000' w:bottom='600' w:left='600' w:right='600' w:header='200' w:footer='200'/>";
        var body = new StringBuilder();
        for (int section = 0; section < 8; section++)
            for (int page = 0; page < 10; page++)
            {
                string properties = (page == 0 ? "<w:pStyle w:val='Topic'/>" : "<w:pageBreakBefore/>");
                if (page == 9 && section < 7) properties += "<w:sectPr>" + geometry + "</w:sectPr>";
                body.Append("<w:p><w:pPr>" + properties + "</w:pPr><w:r><w:t>Topic</w:t></w:r></w:p>");
            }
        byte[] bytes = P1StyleReferenceTests.Source(body.ToString(), "STYLEREF Topic");
        // Earlier sections inherit the same story so every section exercises reservation.
        using var buffer = new MemoryStream(); buffer.Write(bytes, 0, bytes.Length);
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Update, true))
        {
            XDocument xml; using (Stream input = zip.GetEntry("word/document.xml")!.Open()) xml = XDocument.Load(input);
            foreach (XElement section in xml.Descendants(W + "sectPr").Where(e => e.Element(W + "headerReference") == null))
                section.AddFirst(new XElement(W + "headerReference", new XAttribute(W + "type", "default"), new XAttribute(XName.Get("id", TestDocuments.R), "default")));
            zip.GetEntry("word/document.xml")!.Delete(); using var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open()); xml.Save(writer);
        }
        using var source = new MemoryStream(buffer.ToArray()); using var output = new MemoryStream();
        // Parse under the ordinary XML budget, then independently constrain layout work.
        var parser = new ConversionContext(new ConversionOptions(), TestContext.Current.CancellationToken) { Destination = DocumentFormat.Pdf };
        DocumentModel model = DocxReader.Read(source, parser);
        var context = new ConversionContext(new ConversionOptions(maximumCharacters: 1000), TestContext.Current.CancellationToken) { Destination = DocumentFormat.Pdf };
        List<FixedPage> pages = FixedLayout.Create(model, context);
        PdfFormat.Write(model, output, context, pages);
        using var pdf = PdfDocument.Open(output.ToArray()); Assert.Equal(80, pdf.NumberOfPages);
        Assert.All(pdf.GetPages(), page => Assert.Equal("default:TopicTopic", page.Text));
    }

    [Theory]
    [InlineData("1234.56")]
    [InlineData("42")]
    public void LowercasePropertyGuidsAndWordNumericTypesAreAdmitted(string value)
    {
        string type = value == "42" ? "i4" : "r8";
        string props = "<Properties xmlns='http://schemas.openxmlformats.org/officeDocument/2006/custom-properties' xmlns:vt='http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes'><property fmtid='{d5cdd505-2e9c-101b-9397-08002b2cf9ae}' pid='2' name='Amount'><vt:" + type + ">" + value + "</vt:" + type + "></property></Properties>";
        byte[] input = TestDocuments.Docx(TestDocuments.Body("<w:p>" + P1FieldTests.Field("DOCPROPERTY Amount") + "</w:p>"), rootRelationships: "<Relationships xmlns='" + TestDocuments.PackageR + "'><Relationship Id='main' Type='" + TestDocuments.R + "/officeDocument' Target='word/document.xml'/><Relationship Id='custom' Type='" + TestDocuments.R + "/custom-properties' Target='docProps/custom.xml'/></Relationships>", extra: new KeyValuePair<string, string>("docProps/custom.xml", props));
        byte[] bytes = Convert(input, DocumentFormat.Pdf, FidelityMode.Strict, out _); using var pdf = PdfDocument.Open(bytes); Assert.Equal(value, pdf.GetPage(1).Text);
        using var source = new MemoryStream(input); WordDocument document = WordDocument.Load(source, DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken);
        byte[] word = P1FieldTests.Save(document, DocumentFormat.Docx); P1FieldTests.Validate(word);
        Assert.Equal(type, P1FieldTests.Xml(word, "docProps/custom.xml").Root!.Elements().Single().Elements().Single().Name.LocalName);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("INF")]
    [InlineData("1E300")]
    public void UnsupportedFloatingMetadataRetainsStrictConversionCaches(string value)
    {
        string properties = "<Properties xmlns='http://schemas.openxmlformats.org/officeDocument/2006/custom-properties' xmlns:vt='http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes'><property fmtid='{D5CDD505-2E9C-101B-9397-08002B2CF9AE}' pid='2' name='Amount'><vt:r8>" + value + "</vt:r8></property></Properties>";
        byte[] input = TestDocuments.Docx(TestDocuments.Body("<w:p>" + P1FieldTests.Field("DOCPROPERTY Amount", "safe cache") + "</w:p>"),
            rootRelationships: "<Relationships xmlns='" + TestDocuments.PackageR + "'><Relationship Id='main' Type='" + TestDocuments.R + "/officeDocument' Target='word/document.xml'/><Relationship Id='custom' Type='" + TestDocuments.R + "/custom-properties' Target='docProps/custom.xml'/></Relationships>", extra: new KeyValuePair<string, string>("docProps/custom.xml", properties));
        byte[] bytes = Convert(input, DocumentFormat.Pdf, FidelityMode.Strict, out ConversionResult result);
        Assert.Contains(result.Diagnostics, d => d.Code == "Docx.CustomPropertyType" && d.Severity == DiagnosticSeverity.Info);
        using var pdf = PdfDocument.Open(bytes); Assert.Equal("safe cache", pdf.GetPage(1).Text);
    }

    [Fact]
    public void DecimalPrecisionOutsideBinary64DisplayKeepsItsLosslessVariant()
    {
        WordDocument document = Load("<w:p>" + P1FieldTests.Field("DOCPROPERTY Amount") + "</w:p>");
        const decimal number = 0.1234567890123456789012345678m;
        document.SetCustomProperty(new DocumentProperty("Amount", number), TestContext.Current.CancellationToken);
        byte[] word = P1FieldTests.Save(document, DocumentFormat.Docx); P1FieldTests.Validate(word);
        XElement value = P1FieldTests.Xml(word, "docProps/custom.xml").Root!.Elements().Single().Elements().Single();
        Assert.Equal("decimal", value.Name.LocalName); Assert.Equal(number.ToString(CultureInfo.InvariantCulture), value.Value);
    }

    [Fact]
    public void ContextualNoteAndHeadingSizesRemainDistinctFromOrdinaryDefaults()
    {
        string body = "<w:p><w:pPr><w:outlineLvl w:val='0'/></w:pPr><w:r><w:t>Heading</w:t></w:r></w:p><w:p><w:r><w:t>Body</w:t><w:footnoteReference w:id='1'/></w:r></w:p>";
        string notes = "<w:footnotes xmlns:w='" + TestDocuments.W + "'><w:footnote w:id='1'><w:p><w:r><w:t>Note</w:t></w:r></w:p></w:footnote></w:footnotes>";
        byte[] input = TestDocuments.Docx(TestDocuments.Body(body), "<Relationship Id='notes' Type='" + TestDocuments.R + "/footnotes' Target='footnotes.xml'/>", extra: new KeyValuePair<string, string>("word/footnotes.xml", notes));
        using var pdf = PdfDocument.Open(Convert(input, DocumentFormat.Pdf, FidelityMode.Strict, out _));
        var words = pdf.GetPage(1).GetWords().ToArray(); Assert.Equal(24, words.Single(w => w.Text == "Heading").Letters[0].FontSize, 3); Assert.Equal(9, words.Single(w => w.Text == "Note").Letters[0].FontSize, 3);
    }

    [Fact]
    public void UnequalColumnContinuationKeepsTheOmittedSizePolicy()
    {
        string text = string.Join(" ", Enumerable.Repeat("ordinary", 220));
        string page = "<w:sectPr><w:pgSz w:w='10000' w:h='6000'/><w:pgMar w:top='600' w:bottom='600' w:left='600' w:right='600'/><w:cols w:num='2' w:equalWidth='0'><w:col w:w='3000' w:space='400'/><w:col w:w='5400'/></w:cols></w:sectPr>";
        using var pdf = PdfDocument.Open(Convert(TestDocuments.Docx(TestDocuments.Body(TestDocuments.TextParagraph(text) + page)), DocumentFormat.Pdf, FidelityMode.Strict, out _));
        Assert.All(pdf.GetPages().SelectMany(p => p.Letters), letter => Assert.Equal(11, letter.FontSize, 3));
        Assert.Equal(text.Replace(" ", ""), string.Concat(pdf.GetPages().Select(p => p.Text)).Replace(" ", ""));
    }
}
