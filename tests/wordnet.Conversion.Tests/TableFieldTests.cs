using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Xml.Linq;
using UglyToad.PdfPig;
using WordNet.Internal;
using WordNet.Model;
using Xunit;

namespace WordNet.Tests;

public sealed class TableFieldTests
{
    private static readonly XNamespace W = TestDocuments.W;
    private static readonly string[] ExpectedTotals = { "3.25", "1.5", "4.75", "4.75", "12.75" };
    private static readonly string[] ExpectedUnrounded = { "1.2", "2.468" };
    private static readonly string[] RetainedGraph = { "old-a", "old-b", "old-c" };
    private static readonly string[] RetainedTable = { "old-total", "old-other" };
    private static readonly string[] IndependentResults = { "4.75", "4.75", "9.5", "15.00%", "15.00%", "0.15%" };
    private static readonly FidelityMode[] Modes = { FidelityMode.Strict, FidelityMode.BestEffort, FidelityMode.Lenient };
    private static string Text(string text) => "<w:r><w:t>" + SecurityElement.Escape(text) + "</w:t></w:r>";
    private static string Cell(string contents, string properties = "") => "<w:tc>" + properties + "<w:p>" + contents + "</w:p></w:tc>";
    private static string Row(params string[] cells) => "<w:tr>" + string.Concat(cells) + "</w:tr>";
    private static string Formula(string instruction, string cache) => Cell(P1FieldTests.Field(instruction, cache));
    private static byte[] Input(string table, string extra = "") => TestDocuments.Docx(TestDocuments.Body("<w:tbl>" + table + "</w:tbl>" + extra));
    private static WordDocument Load(byte[] input)
    {
        using var stream = new MemoryStream(input);
        return WordDocument.Load(stream, DocumentFormat.Docx, out _, cancellationToken: TestContext.Current.CancellationToken);
    }
    private static string[] Caches(byte[] bytes) => P1FieldTests.Xml(bytes, "word/document.xml").Descendants(W + "fldSimple")
        .Where(e => ((string?)e.Attribute(W + "instr"))?.TrimStart() is string value && value.Length > 0 && value[0] == '=').Select(e => e.Value).ToArray();

    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    [InlineData("tr-TR")]
    public void BoundedDependenciesRefreshInOrderAndSurviveReferenceRefresh(string culture)
    {
        string rows = Row(Cell(Text("1.25")), Cell(Text("2")), Formula("= A1 + B1", "old-c1"))
            + Row(Cell(Text("3.5")), Cell(Text("-2")), Formula("= SUM(LEFT)", "old-c2"))
            + Row(Formula("= SUM(ABOVE)", "old-a3"), Formula("= SUM(A1:B2)", "old-b3"), Formula("= A3 + B3 + C1", "old-c3"));
        string bookmark = "<w:p><w:bookmarkStart w:id='1' w:name='proof'/><w:r><w:t>marker</w:t></w:r><w:bookmarkEnd w:id='1'/>"
            + P1FieldTests.Field("REF proof", "old-ref") + "</w:p>";
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            WordDocument document = Load(Input(rows, bookmark));
            byte[] word = P1FieldTests.Save(document, DocumentFormat.Docx);
            P1FieldTests.Validate(word);
            Assert.Equal(ExpectedTotals, Caches(word));
            using var pdf = PdfDocument.Open(P1FieldTests.Save(document, DocumentFormat.Pdf));
            string rendered = string.Concat(pdf.GetPages().Select(page => page.Text));
            Assert.Contains("12.75", rendered); Assert.DoesNotContain("old-", rendered);
            XDocument xps = P1FieldTests.Xml(P1FieldTests.Save(document, DocumentFormat.Xps), "Documents/1/Pages/1.fpage");
            string fixedText = string.Concat(xps.Descendants().Where(e => e.Name.LocalName == "Glyphs").Select(e => (string?)e.Attribute("UnicodeString")));
            Assert.Contains("12.75", fixedText); Assert.DoesNotContain("old-", fixedText);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void FormulaReferencesUseUnroundedValuesRatherThanDisplayCaches()
    {
        string rows = Row(Cell(Text("1")), Formula("= 1.234 \\# \"0.0\"", "old-b1"), Formula("= B1 * 2", "old-c1"));
        byte[] word = P1FieldTests.Save(Load(Input(rows)), DocumentFormat.Docx);
        P1FieldTests.Validate(word); Assert.Equal(ExpectedUnrounded, Caches(word));
    }

    [Theory]
    [InlineData("= A1")]
    [InlineData("= SUM(A1:B1)")]
    [InlineData("= 1 / 0")]
    [InlineData("= 79228162514264337593543950335 + 1")]
    [InlineData("= INDIRECT(\"A1\")")]
    [InlineData("= AA1")]
    [InlineData("= UNSUPPORTED()")]
    [InlineData("= Other!A1")]
    [InlineData("= SUM(A1:Z999)")]
    [InlineData("= SUM(B1:A1)")]
    public void InvalidDependenciesPreserveEveryCacheInTheTable(string invalid)
    {
        string rows = Row(Formula("= B1", "old-a"), Formula(invalid, "old-b"))
            + Row(Formula("= 1 + 2", "old-c"), Cell(Text("5")));
        foreach (FidelityMode mode in Modes)
        {
            WordDocument document = Load(Input(rows)); using var output = new MemoryStream();
            ConversionResult result = document.Save(output, DocumentFormat.Docx, new ConversionOptions(mode), TestContext.Current.CancellationToken);
            Assert.Contains(result.Diagnostics, d => d.Code == "Field.UnsupportedUsedCachedResult");
            Assert.Equal(RetainedGraph, Caches(output.ToArray()));
        }
    }

    [Fact]
    public void UnknownFunctionWithoutAValidReferenceDoesNotPartiallyRefreshTheTable()
    {
        string rows = Row(Formula("= 1 + 2", "old-a"), Formula("= UNSUPPORTED()", "old-b"));
        using var output = new MemoryStream();
        ConversionResult result = Load(Input(rows)).Save(output, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "Field.UnsupportedUsedCachedResult");
        Assert.Equal("old-a", Caches(output.ToArray())[0]); Assert.Equal("old-b", Caches(output.ToArray())[1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Header")]
    [InlineData("1,25")]
    public void BlankTextAndCultureDependentCellsRetainTheTableCaches(string value)
    {
        string rows = Row(Cell(Text(value)), Cell(Text("9")))
            + Row(Formula("= SUM(ABOVE)", "old-total"), Formula("= 2", "old-other"));
        WordDocument document = Load(Input(rows)); using var output = new MemoryStream();
        ConversionResult result = document.Save(output, DocumentFormat.Docx, new ConversionOptions(), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "Field.UnsupportedUsedCachedResult");
        Assert.Equal(RetainedTable, Caches(output.ToArray()));
    }

    [Theory]
    [InlineData("1.5", "1.5")]
    [InlineData("Header", "old-total")]
    public void HeaderCellsUseTheSameNumericAdmissionRules(string header, string expected)
    {
        string rows = "<w:tr><w:trPr><w:tblHeader/></w:trPr>" + Cell(Text(header)) + "</w:tr>"
            + Row(Formula("= SUM(ABOVE)", "old-total"));
        byte[] word = P1FieldTests.Save(Load(Input(rows)), DocumentFormat.Docx);
        Assert.Equal(expected, Assert.Single(Caches(word)));
    }

    [Theory]
    [InlineData("= SUM(ABOVE)")]
    [InlineData("= SUM(LEFT)")]
    public void EmptyPositionalRangesProduceZero(string instruction)
    {
        byte[] word = P1FieldTests.Save(Load(Input(Row(Formula(instruction, "old")))), DocumentFormat.Docx);
        Assert.Equal("0", Assert.Single(Caches(word)));
    }

    [Fact]
    public void MergedCellsAndOverdeepGraphsRetainCaches()
    {
        string merged = Row(Cell(Text("1"), "<w:tcPr><w:gridSpan w:val='2'/></w:tcPr>"), Cell(Text("2")))
            + Row(Formula("= SUM(ABOVE)", "old-total"), Formula("= 3", "old-other"), Cell(Text("0")));
        Assert.Equal(RetainedTable, Caches(P1FieldTests.Save(Load(Input(merged)), DocumentFormat.Docx)));
        string deep = string.Concat(Enumerable.Range(1, 65).Select(i => Row(Formula(i == 65 ? "= 1" : "= A" + (i + 1).ToString(CultureInfo.InvariantCulture), "old-" + i.ToString(CultureInfo.InvariantCulture)))));
        Assert.Equal(65, Caches(P1FieldTests.Save(Load(Input(deep)), DocumentFormat.Docx)).Count(cache => cache.StartsWith("old-", StringComparison.Ordinal)));
    }

    [Fact]
    public void CancellationDoesNotChangeTheDestination()
    {
        WordDocument document = Load(Input(Row(Cell(Text("1")), Formula("= A1", "old"))));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<OperationCanceledException>(() => document.Save(output, DocumentFormat.Docx, cancellationToken: cancellation.Token));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }

    [Fact]
    public void ExpandedRowsAndLaterEditsRecomputeTheTotal()
    {
        string repeated = "<w:sdt><w:sdtPr><w:tag w:val='repeat:Items'/></w:sdtPr><w:sdtContent>"
            + Row(Cell(P1FieldTests.Field("MERGEFIELD Amount", "old-item"))) + "</w:sdtContent></w:sdt>";
        string body = "<w:tbl><w:tblGrid><w:gridCol w:w='4000'/></w:tblGrid>" + repeated + Row(Formula("= SUM(ABOVE)", "old-total")) + "</w:tbl>";
        WordDocument document = Load(TestDocuments.Docx(TestDocuments.Body(body)));
        document.Merge(new { Items = new[] { new { Amount = 1.25m }, new { Amount = 3.5m } } }, new TemplateOptions { Culture = CultureInfo.InvariantCulture }, TestContext.Current.CancellationToken);
        byte[] first = P1FieldTests.Save(document, DocumentFormat.Docx); P1FieldTests.Validate(first);
        Assert.Equal("4.75", Assert.Single(Caches(first)));
        document.Replace("1.25", "2.25", cancellationToken: TestContext.Current.CancellationToken);
        byte[] updated = P1FieldTests.Save(document, DocumentFormat.Docx); P1FieldTests.Validate(updated);
        Assert.Equal("5.75", Assert.Single(Caches(updated)));
    }

    [Fact]
    public void ReferenceWorkLimitDoesNotCommitEarlierFormulaResults()
    {
        var model = new DocumentModel(); var table = new TableBlock(); model.Blocks.Add(table);
        var formulas = new System.Collections.Generic.List<TextRun>();
        for (int row = 0; row < 37; row++)
        {
            var cells = new System.Collections.Generic.List<TableCell>();
            for (int column = 0; column < 26; column++)
            {
                var paragraph = new ParagraphBlock();
                var run = row >= 20 && column == 0 ? new TextRun("old", fieldInstruction: "= SUM(A1:Z20)") : new TextRun("1");
                if (row >= 20 && column == 0) formulas.Add(run);
                paragraph.Runs.Add(run); cells.Add(new TableCell(paragraph));
            }
            table.Rows.Add(cells);
        }
        TableFields.Refresh(model, new ConversionContext(new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.Equal(17, formulas.Count); Assert.All(formulas, run => Assert.Equal("old", model.FieldResults[run]));
    }

    [Fact]
    public void IndependentLibreOfficeFixtureAgreesOnNumericDependencies()
    {
        string directory = Path.Combine(TestDocuments.FixtureDirectory, "P2");
        using (PdfDocument reference = PdfDocument.Open(Path.Combine(directory, "table-formulas-libreoffice.pdf")))
        {
            string text = reference.GetPage(1).Text;
            Assert.Contains("4.75", text); Assert.Contains("9.5", text);
        }
        WordDocument document = Load(File.ReadAllBytes(Path.Combine(directory, "table-formulas.docx")));
        byte[] word = P1FieldTests.Save(document, DocumentFormat.Docx); P1FieldTests.Validate(word);
        Assert.Equal(IndependentResults, Caches(word));
    }
}
