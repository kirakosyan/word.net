using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Layout;
using WordNet.Model;
using Xunit;

namespace WordNet.Tests;

public sealed class NativeScalarFieldTests
{
    private static readonly XNamespace W = TestDocuments.W;
    private static readonly int[] ConjunctGlyphs = { 610, 179 };
    private static byte[] Source(string content) => TestDocuments.Docx(TestDocuments.Body(content));
    private static WordDocument Load(string content, ConversionOptions options)
    {
        using var input = new MemoryStream(Source(content));
        return WordDocument.Load(input, DocumentFormat.Docx, out _, options, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("= 2 * (3 + 4)", "14")]
    [InlineData("IF 4 > 2 \"yes\" \"no\"", "yes")]
    public void StableScalarExpressionsAreEvaluatedBeforeNativeShaping(string instruction, string expected)
    {
        WordDocument document = Load("<w:p>" + P1FieldTests.Field(instruction) + "</w:p>", ExtendedTypographyTests.Options(mode: FidelityMode.BestEffort));
        using var output = new MemoryStream(); ConversionResult result = document.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, item => item.Code == "Typography.FieldLayoutApproximation");
        using var pdf = new MemoryStream(output.ToArray());
        var imported = PdfImportBridge.Read(pdf, new ConversionContext(new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken));
        Assert.Equal(expected, string.Join(" ", imported.BodyParagraphs.Select(p => p.Text)));
    }

    [Fact]
    public void AggregateFieldExpansionIsBoundedBeforeTheModelIsChanged()
    {
        var options = new ConversionOptions(FidelityMode.BestEffort, maximumCharacters: 100);
        options.Typography.Enabled = true; options.Typography.ExtendedBidi = true;
        var model = new DocumentModel(); model.CustomProperties.Add("Large", new DocumentProperty("Large", new string('a', 40)));
        var paragraph = new ParagraphBlock();
        for (int i = 0; i < 3; i++) paragraph.Runs.Add(new TextRun("x", fieldInstruction: "DOCPROPERTY Large"));
        model.Blocks.Add(paragraph); TextRun[] before = paragraph.Runs.ToArray();
        var context = new ConversionContext(options, TestContext.Current.CancellationToken) { Destination = DocumentFormat.Pdf };
        var error = Assert.Throws<InvalidDataException>(() => NativeTypography.Prepare(model, context));
        Assert.StartsWith("Limit.Characters:", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, paragraph.Runs); Assert.False(model.TypographyPrepared);
    }

    [Fact]
    public void ArabicScalarValuesRemainLogicalAndShapedInsideMixedDirectionText()
    {
        var options = ExtendedTypographyTests.Options(false, FidelityMode.BestEffort);
        WordDocument document = Load("<w:p><w:r><w:t xml:space='preserve'>Total </w:t></w:r>" + P1FieldTests.Field("DOCPROPERTY Word")
            + "<w:r><w:t xml:space='preserve'> 12</w:t></w:r></w:p>", options);
        document.SetCustomProperty(new DocumentProperty("Word", "مرحبا"), TestContext.Current.CancellationToken);
        byte[] saved = P1FieldTests.Save(document, DocumentFormat.Docx);
        using var input = new MemoryStream(saved); var context = new ConversionContext(options, TestContext.Current.CancellationToken) { Destination = DocumentFormat.Pdf };
        var model = DocxReader.Read(input, context); FidelityChecks.Destination(model, DocumentFormat.Pdf, context);
        var page = Assert.Single(FixedLayout.Create(model, context));
        Assert.Equal("Total مرحبا 12", string.Concat(page.Text.Select(item => item.Text)));
        PositionedText arabic = Assert.Single(page.Text, item => item.Text == "مرحبا");
        Assert.NotNull(arabic.NativeShape); Assert.True(arabic.NativeShape!.Width > 0);
        using var pdf = new MemoryStream();
        ConversionResult result = document.Save(pdf, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, item => item.Code == "Typography.FieldDirectionalOrder");
        Assert.Contains(result.Diagnostics, item => item.Code == "Typography.FieldLayoutApproximation");
        pdf.Position = 0;
        var imported = PdfImportBridge.Read(pdf, new ConversionContext(new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken));
        Assert.Contains("مرحبا", string.Join(" ", imported.BodyParagraphs.Select(p => p.Text)), StringComparison.Ordinal);
        using var strictOutput = new MemoryStream(); strictOutput.WriteByte(8);
        var error = Assert.Throws<NotSupportedException>(() => document.Save(strictOutput, DocumentFormat.Pdf,
            ExtendedTypographyTests.Options(false, FidelityMode.Strict), TestContext.Current.CancellationToken));
        Assert.StartsWith("Typography.FieldDirectionalOrder:", error.Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 8 }, strictOutput.ToArray());
    }

    [Fact]
    public void HindiScalarInAMergedCellRetainsTheExistingSpacingDiagnostic()
    {
        var options = ExtendedTypographyTests.Options(mode: FidelityMode.BestEffort);
        string body = "<w:tbl><w:tblPr><w:tblLayout w:type='fixed'/></w:tblPr><w:tblGrid><w:gridCol w:w='1800'/><w:gridCol w:w='1800'/></w:tblGrid>"
            + "<w:tr><w:tc><w:tcPr><w:gridSpan w:val='2'/></w:tcPr><w:p>" + P1FieldTests.Field("DOCPROPERTY Word") + "</w:p></w:tc></w:tr></w:tbl>";
        WordDocument document = Load(body, options);
        document.SetCustomProperty(new DocumentProperty("Word", "क्षि"), TestContext.Current.CancellationToken);
        using var output = new MemoryStream();
        ConversionResult result = document.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, item => item.Code == "Typography.MergedTableSpacing");
        Assert.Contains(result.Diagnostics, item => item.Code == "Typography.FieldLayoutApproximation");
        using var input = new MemoryStream(output.ToArray());
        var imported = PdfImportBridge.Read(input, new ConversionContext(new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken));
        Assert.Contains("क्षि", string.Join(" ", imported.BodyParagraphs.Select(p => p.Text)), StringComparison.Ordinal);
        var fields = P1FieldTests.Xml(P1FieldTests.Save(document, DocumentFormat.Docx), "word/document.xml").Descendants(W + "fldSimple");
        Assert.Single(fields);
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("tr-TR")]
    public void ScalarValuesAreRefreshedBeforeHindiShapingWithoutChangingTheEditableSource(string culture)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var options = ExtendedTypographyTests.Options(mode: FidelityMode.BestEffort);
            string contents = "<w:p><w:r><w:t xml:space='preserve'>Hindi </w:t></w:r>" + P1FieldTests.Field("DOCPROPERTY Word")
                + "<w:r><w:t xml:space='preserve'> Amount </w:t></w:r>" + P1FieldTests.Field("DOCPROPERTY Amount \\# \"0.00\"") + "</w:p>";
            WordDocument document = Load(contents, options);
            document.SetCustomProperty(new DocumentProperty("Word", "क्षि"), TestContext.Current.CancellationToken);
            document.SetCustomProperty(new DocumentProperty("Amount", 2.345m), TestContext.Current.CancellationToken);
            byte[] editable = P1FieldTests.Save(document, DocumentFormat.Docx);
            using var input = new MemoryStream(editable); var context = new ConversionContext(options, TestContext.Current.CancellationToken) { Destination = DocumentFormat.Pdf };
            var model = DocxReader.Read(input, context); FidelityChecks.Destination(model, DocumentFormat.Pdf, context);
            var page = Assert.Single(FixedLayout.Create(model, context));
            PositionedText conjunct = Assert.Single(page.Text, item => item.Text == "क्षि");
            Assert.Equal(ConjunctGlyphs, conjunct.NativeShape!.Glyphs.Select(glyph => glyph.Glyph));
            Assert.Contains(page.Text, item => item.Text == "2.35");
            using var pdf = new MemoryStream(); ConversionResult result = document.Save(pdf, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
            ConversionDiagnostic diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "Typography.FieldLayoutApproximation");
            Assert.NotEmpty(diagnostic.Occurrences); Assert.DoesNotContain("क्षि", diagnostic.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("Word", diagnostic.Message, StringComparison.Ordinal);
            using var pdfInput = new MemoryStream(pdf.ToArray()); var imported = PdfImportBridge.Read(pdfInput, new ConversionContext(new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken));
            Assert.Contains("क्षि", string.Join(" ", imported.BodyParagraphs.Select(p => p.Text)), StringComparison.Ordinal);
            using var strictOutput = new MemoryStream(); strictOutput.WriteByte(4);
            var strict = Assert.Throws<NotSupportedException>(() => document.Save(strictOutput, DocumentFormat.Pdf,
                ExtendedTypographyTests.Options(mode: FidelityMode.Strict), TestContext.Current.CancellationToken));
            Assert.StartsWith("Typography.FieldLayoutApproximation:", strict.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 4 }, strictOutput.ToArray());
            document.SetCustomProperty(new DocumentProperty("Word", "हिन्दी"), TestContext.Current.CancellationToken);
            byte[] updated = P1FieldTests.Save(document, DocumentFormat.Docx);
            var fields = P1FieldTests.Xml(updated, "word/document.xml").Descendants(W + "fldSimple").ToArray();
            Assert.Equal(2, fields.Length); Assert.Contains("DOCPROPERTY Word", (string?)fields[0].Attribute(W + "instr"), StringComparison.Ordinal);
            Assert.Equal("हिन्दी", fields[0].Value);
            using var updatedInput = new MemoryStream(updated); var updatedContext = new ConversionContext(options, TestContext.Current.CancellationToken) { Destination = DocumentFormat.Pdf };
            var updatedModel = DocxReader.Read(updatedInput, updatedContext); FidelityChecks.Destination(updatedModel, DocumentFormat.Pdf, updatedContext);
            Assert.Contains(FixedLayout.Create(updatedModel, updatedContext).SelectMany(p => p.Text), item => item.Text == "हिन्दी");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("PAGE")]
    [InlineData("REF Target")]
    public void PageAndReferenceFieldsRemainRejectedWithoutOutput(string instruction)
    {
        byte[] source = Source("<w:p>" + P1FieldTests.Field(instruction) + "</w:p>");
        using var input = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(7);
        Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, ExtendedTypographyTests.Options(), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 7 }, output.ToArray());
    }

    [Fact]
    public void AnUnsupportedMergeFieldKeepsItsCacheWithAnExplicitDiagnostic()
    {
        byte[] source = Source("<w:p>" + P1FieldTests.Field("MERGEFIELD Name", "cached") + "</w:p>");
        using var input = new MemoryStream(source); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            ExtendedTypographyTests.Options(), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, item => item.Code == "Field.UnsupportedUsedCachedResult");
        using var pdf = new MemoryStream(output.ToArray());
        var imported = PdfImportBridge.Read(pdf, new ConversionContext(new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken));
        Assert.Contains("cached", string.Join(" ", imported.BodyParagraphs.Select(p => p.Text)), StringComparison.Ordinal);
    }

    [Fact]
    public void AScalarFieldCannotSplitAConjunctOrBeCombinedWithTaggedOutput()
    {
        var options = ExtendedTypographyTests.Options(mode: FidelityMode.BestEffort);
        WordDocument document = Load("<w:p><w:r><w:t>क</w:t></w:r>" + P1FieldTests.Field("DOCPROPERTY Word") + "</w:p>", options);
        document.SetCustomProperty(new DocumentProperty("Word", "्षि"), TestContext.Current.CancellationToken);
        using var output = new MemoryStream(); output.WriteByte(7);
        var error = Assert.Throws<NotSupportedException>(() => document.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken));
        Assert.StartsWith("Typography.StyledCluster:", error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 7 }, output.ToArray());
        WordDocument safe = Load("<w:p>" + P1FieldTests.Field("= 1 + 1") + "</w:p>", options);
        var tagged = ExtendedTypographyTests.Options(); tagged.Pdf.Accessibility = PdfAccessibility.Tagged;
        using var taggedOutput = new MemoryStream(); taggedOutput.WriteByte(9);
        Assert.Throws<NotSupportedException>(() => safe.Save(taggedOutput, DocumentFormat.Pdf, tagged, TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 9 }, taggedOutput.ToArray());
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        using var cancelOutput = new MemoryStream(); cancelOutput.WriteByte(11);
        Assert.ThrowsAny<OperationCanceledException>(() => safe.Save(cancelOutput, DocumentFormat.Pdf, options, cancelled.Token));
        Assert.Equal(new byte[] { 11 }, cancelOutput.ToArray());
    }
}
