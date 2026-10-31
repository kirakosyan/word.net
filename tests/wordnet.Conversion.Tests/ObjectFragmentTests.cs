using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using UglyToad.PdfPig;
using WordNet.Formats;
using Xunit;

namespace WordNet.Tests;

public sealed partial class ObjectFoundationTests
{
    private static WordDocument ObjectFragmentTarget(bool cell, ConversionOptions? options = null)
    {
        string one = "<w:p><w:bookmarkStart w:id='1' w:name='one'/><w:r><w:t>Insert</w:t></w:r><w:bookmarkEnd w:id='1'/></w:p>";
        string two = "<w:p><w:bookmarkStart w:id='2' w:name='two'/><w:r><w:t>Insert</w:t></w:r><w:bookmarkEnd w:id='2'/></w:p>";
        string body = cell ? "<w:tbl><w:tblPr><w:tblW w:type='dxa' w:w='7200'/></w:tblPr><w:tblGrid><w:gridCol w:w='7200'/></w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:type='dxa' w:w='7200'/></w:tcPr>" + one + two + "</w:tc></w:tr></w:tbl>" : one + two;
        using var stream = new MemoryStream(ControlTestDocuments.Package(ControlTestDocuments.Document(body)));
        return WordDocument.Load(stream, DocumentFormat.Docx, out _, options ?? MathOptions(), TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObjectFragmentsRemainEditableAndIndependentAtBodyAndCellBookmarks(bool cell)
    {
        WordDocument source = LoadObjects(Input()); byte[] sourceBefore = SaveObjects(source);
        WordDocument target = ObjectFragmentTarget(cell);
        if (cell) target.InsertFragmentAtTableCellBookmark("one", source, TestContext.Current.CancellationToken);
        else target.InsertFragmentAtBookmark("one", source, TestContext.Current.CancellationToken);
        DocumentObject[] first = target.Objects.ToArray(); Guid[] firstIds = first.Select(item => item.Id).ToArray();
        if (cell) target.InsertFragmentAtTableCellBookmark("two", source, TestContext.Current.CancellationToken);
        else target.InsertFragmentAtBookmark("two", source, TestContext.Current.CancellationToken);
        Assert.Equal(sourceBefore, SaveObjects(source)); Assert.Equal(4, target.Objects.Count);
        Assert.Equal(4, target.Objects.Select(item => item.Id).Distinct().Count());
        Assert.DoesNotContain(target.Objects, item => source.Objects.Any(original => original.Id == item.Id));
        Assert.Equal(2, target.Objects.Count(item => firstIds.Contains(item.Id)));
        Assert.Equal(source.Objects.Select(item => item.Description), target.Objects.Take(2).Select(item => item.Description));

        byte[] beforeStaleEdit = SaveObjects(target);
        Assert.Throws<ArgumentException>(() => target.UpdateChart(first.Single(item => item.Chart != null), Definition(), TestContext.Current.CancellationToken));
        Assert.Equal(beforeStaleEdit, SaveObjects(target));

        byte[] saved = SaveObjects(target);
        using (var stream = new MemoryStream(saved)) using (var package = WordprocessingDocument.Open(stream, false))
            Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
        Assert.Equal(PartBytes(sourceBefore, "word/embeddings/chart1.xlsx"), PartBytes(saved, "word/embeddings/chart1.xlsx"));
        Assert.Equal(PartBytes(sourceBefore, "word/embeddings/chart1.xlsx"), PartBytes(saved, "word/embeddings/chart2.xlsx"));
        XDocument xml = XDocument.Parse(Encoding.UTF8.GetString(PartBytes(saved, "word/document.xml")));
        Assert.Equal(2, xml.Descendants(MathObjectFormat.M + "oMath").Count());
        if (cell) Assert.Single(xml.Descendants((XNamespace)TestDocuments.W + "tc"));

        DocumentObject firstChart = target.Objects.First(item => firstIds.Contains(item.Id) && item.Chart != null);
        Guid chartId = firstChart.Id;
        var replacement = new ChartDefinition(ChartKind.Line, Categories, new[] { new ChartSeries("Changed", "2468AC", North) });
        target.UpdateChart(firstChart, replacement, TestContext.Current.CancellationToken);
        Assert.Equal(ChartKind.Line, target.Objects.Single(item => item.Id == chartId).Chart!.Kind);
        Assert.Equal(ChartKind.ClusteredColumn, target.Objects.Single(item => !firstIds.Contains(item.Id) && item.Chart != null).Chart!.Kind);
        DocumentObject firstEquation = target.Objects.First(item => firstIds.Contains(item.Id) && item.Equation != null);
        Guid equationId = firstEquation.Id;
        target.UpdateEquation(firstEquation, EquationExpression.Text("changed"), TestContext.Current.CancellationToken);
        Assert.Equal("changed", target.Objects.Single(item => item.Id == equationId).Equation!.Value);
        Assert.NotEqual("changed", target.Objects.Single(item => !firstIds.Contains(item.Id) && item.Equation != null).Equation!.Value);
        Assert.Equal(sourceBefore, SaveObjects(source));
        byte[] changed = SaveObjects(target); WordDocument reopened = LoadObjects(changed);
        Assert.Equal(2, reopened.Objects.Count(item => item.Chart != null)); Assert.Equal(2, reopened.Objects.Count(item => item.Equation != null));
        using var pdf = new MemoryStream(); target.Save(pdf, DocumentFormat.Pdf, MathOptions(), TestContext.Current.CancellationToken);
        using (PdfDocument parsed = PdfDocument.Open(pdf.ToArray()))
        {
            string text = string.Concat(parsed.GetPages().Select(page => page.Text));
            Assert.Contains("First", text, StringComparison.Ordinal);
            Assert.Contains("Changed", text, StringComparison.Ordinal);
        }
        using var xps = new MemoryStream(); target.Save(xps, DocumentFormat.Xps, MathOptions(), TestContext.Current.CancellationToken);
        Assert.Contains("Glyphs", Encoding.UTF8.GetString(PartBytes(xps.ToArray(), "Documents/1/Pages/1.fpage")), StringComparison.Ordinal);
    }

    [Fact]
    public void ObjectFragmentRestylingAndCancellationLeaveBothSessionsUntouched()
    {
        WordDocument source = LoadObjects(Input()); WordDocument target = ObjectFragmentTarget(false);
        byte[] beforeSource = SaveObjects(source), beforeTarget = SaveObjects(target);
        var options = new FragmentInsertionOptions { Style = CompositionStyle.UseDestination };
        var error = Assert.Throws<NotSupportedException>(() => target.InsertFragmentAtBookmarkWithOptions("one", source, options, TestContext.Current.CancellationToken));
        Assert.StartsWith("Object.Restyle:", error.Message, StringComparison.Ordinal);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => target.InsertFragmentAtBookmark("one", source, cancelled.Token));
        Assert.Equal(beforeSource, SaveObjects(source)); Assert.Equal(beforeTarget, SaveObjects(target));
    }

    [Fact]
    public void ObjectFragmentsWithSeparateReportControlsRejectBeforeInsertion()
    {
        byte[] input = Xml(Input(), "word/document.xml", xml =>
        {
            XNamespace w = TestDocuments.W;
            XElement control = new XElement(w + "sdt", new XElement(w + "sdtPr", new XElement(w + "tag", new XAttribute(w + "val", "repeat:Items"))),
                new XElement(w + "sdtContent", new XElement(w + "p", new XElement(w + "r", new XElement(w + "t", "Item")))));
            xml.Descendants(w + "sectPr").Single().AddBeforeSelf(control);
        });
        WordDocument source = LoadObjects(input); WordDocument target = ObjectFragmentTarget(false);
        byte[] beforeSource = SaveObjects(source), beforeTarget = SaveObjects(target);
        var error = Assert.Throws<NotSupportedException>(() => target.InsertFragmentAtBookmark("one", source, TestContext.Current.CancellationToken));
        Assert.StartsWith("Fragment.SourceStructure:", error.Message, StringComparison.Ordinal);
        Assert.Equal(beforeSource, SaveObjects(source)); Assert.Equal(beforeTarget, SaveObjects(target));
    }

    [Fact]
    public void ObjectFragmentsRejectTypedSourceAndDestinationAtomically()
    {
        string control = "<w:p>" + ControlTestDocuments.Control(7, "<w:text/>", "Value") + "</w:p>";
        string bookmark = "<w:p><w:bookmarkStart w:id='1' w:name='one'/><w:r><w:t>Insert</w:t></w:r><w:bookmarkEnd w:id='1'/></w:p>";
        WordDocument typed = ControlTestDocuments.LoadBody(control + bookmark, new ConversionOptions(FidelityMode.Lenient));
        WordDocument target = ObjectFragmentTarget(false); WordDocument objects = LoadObjects(Input());
        target.InsertFragmentAtBookmark("one", objects, TestContext.Current.CancellationToken);
        byte[] beforeTyped = SaveObjects(typed), beforeTarget = SaveObjects(target), beforeObjects = SaveObjects(objects);
        var sourceError = Assert.Throws<NotSupportedException>(() => target.InsertFragmentAtBookmark("two", typed, TestContext.Current.CancellationToken));
        Assert.StartsWith("Fragment.SourceStructure:", sourceError.Message, StringComparison.Ordinal);
        var targetError = Assert.Throws<NotSupportedException>(() => typed.InsertFragmentAtBookmark("one", objects, TestContext.Current.CancellationToken));
        Assert.StartsWith("Fragment.SourceStructure:", targetError.Message, StringComparison.Ordinal);
        Assert.Equal(beforeTyped, SaveObjects(typed)); Assert.Equal(beforeTarget, SaveObjects(target)); Assert.Equal(beforeObjects, SaveObjects(objects));
    }

    [Fact]
    public void ObjectFragmentRespectsTheDestinationResourceBudget()
    {
        WordDocument source = LoadObjects(Input());
        WordDocument target = WordDocument.Create(new ConversionOptions(FidelityMode.Lenient, maximumCharacters: 128));
        target.AddParagraph("Insert", cancellationToken: TestContext.Current.CancellationToken);
        target.AddBookmark("one", 0, 0, 6, TestContext.Current.CancellationToken);
        byte[] beforeSource = SaveObjects(source), beforeTarget = SaveObjects(target);
        Assert.Throws<InvalidDataException>(() => target.InsertFragmentAtBookmark("one", source, TestContext.Current.CancellationToken));
        Assert.Equal(beforeSource, SaveObjects(source)); Assert.Equal(beforeTarget, SaveObjects(target));
    }
}
