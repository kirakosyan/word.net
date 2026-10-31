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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedControlAndObjectFragmentsKeepSeparateEditableOwners(bool cell)
    {
        string control = "<w:p>" + ControlTestDocuments.Control(7, "<w:text/>", "Value", "<w:tag w:val='value'/>") + "</w:p>";
        string bookmark = "<w:p><w:bookmarkStart w:id='1' w:name='one'/><w:r><w:t>Insert</w:t></w:r><w:bookmarkEnd w:id='1'/></w:p>";
        WordDocument source = ControlTestDocuments.LoadBody(control + bookmark, MathOptions());
        WordDocument objects = LoadObjects(Input()); byte[] beforeObjects = SaveObjects(objects);
        source.InsertFragmentAtBookmark("one", objects, TestContext.Current.CancellationToken);
        byte[] beforeSource = SaveObjects(source); WordDocument target = ObjectFragmentTarget(cell);
        if (cell) { target.InsertFragmentAtTableCellBookmark("one", source, TestContext.Current.CancellationToken); target.InsertFragmentAtTableCellBookmark("two", source, TestContext.Current.CancellationToken); }
        else { target.InsertFragmentAtBookmark("one", source, TestContext.Current.CancellationToken); target.InsertFragmentAtBookmark("two", source, TestContext.Current.CancellationToken); }
        Assert.Equal(2, target.ContentControls.Count); Assert.Equal(4, target.Objects.Count);
        Assert.Equal(2, target.ContentControls.Select(item => item.Id).Distinct().Count());
        Assert.Equal(4, target.Objects.Select(item => item.Id).Distinct().Count());
        Assert.DoesNotContain(target.Objects, item => source.Objects.Any(original => original.Id == item.Id));
        target.SetControlText(target.ContentControls[0], "Edited", TestContext.Current.CancellationToken);
        Assert.Equal("Value", target.ContentControls[target.ContentControls.Count - 1].DisplayText);
        Guid firstChart = target.Objects.First(item => item.Chart != null).Id;
        target.UpdateChart(target.Objects.Single(item => item.Id == firstChart), Definition(ChartKind.Line), TestContext.Current.CancellationToken);
        Assert.Equal(ChartKind.ClusteredColumn, target.Objects.Last(item => item.Chart != null).Chart!.Kind);
        Guid firstEquation = target.Objects.First(item => item.Equation != null).Id;
        target.UpdateEquation(target.Objects.Single(item => item.Id == firstEquation), EquationExpression.Text("changed"), TestContext.Current.CancellationToken);
        Assert.NotEqual("changed", target.Objects.Last(item => item.Equation != null).Equation!.Value);
        byte[] saved = SaveObjects(target); ControlTestDocuments.Validate(saved);
        WordDocument reopened = ControlTestDocuments.Load(saved, MathOptions());
        Assert.Equal("Edited", reopened.ContentControls[0].DisplayText); Assert.Equal("Value", reopened.ContentControls[1].DisplayText);
        Assert.Equal(4, reopened.Objects.Count); Assert.Equal(beforeSource, SaveObjects(source)); Assert.Equal(beforeObjects, SaveObjects(objects));
        using var pdf = new MemoryStream(); target.Save(pdf, DocumentFormat.Pdf, MathOptions(), TestContext.Current.CancellationToken);
        using (PdfDocument parsed = PdfDocument.Open(pdf.ToArray()))
        { string text = string.Concat(parsed.GetPages().Select(page => page.Text)); Assert.Contains("Edited", text, StringComparison.Ordinal); Assert.Contains("Value", text, StringComparison.Ordinal); }
        using var xps = new MemoryStream(); target.Save(xps, DocumentFormat.Xps, MathOptions(), TestContext.Current.CancellationToken);
        Assert.Contains("Glyphs", Encoding.UTF8.GetString(PartBytes(xps.ToArray(), "Documents/1/Pages/1.fpage")), StringComparison.Ordinal);
    }

    [Fact]
    public void MixedObjectsCannotShareAnInlineControlParagraph()
    {
        byte[] input = Xml(Input(), "word/document.xml", xml =>
        {
            XNamespace w = TestDocuments.W;
            XElement control = new XElement(w + "sdt", new XElement(w + "sdtPr", new XElement(w + "id", new XAttribute(w + "val", 7)), new XElement(w + "text")),
                new XElement(w + "sdtContent", new XElement(w + "r", new XElement(w + "t", "Value"))));
            xml.Descendants(w + "drawing").Single().Ancestors(w + "p").First().Add(control);
        });
        using var stream = new MemoryStream(input);
        var error = Assert.Throws<NotSupportedException>(() => WordDocument.LoadWithControls(stream, out _, MathOptions(), TestContext.Current.CancellationToken));
        Assert.StartsWith("Object.Owner:", error.Message, StringComparison.Ordinal); Assert.True(stream.CanRead);
    }

    [Fact]
    public void TypedFragmentCanJoinAnExistingObjectDocumentAndKeepLocks()
    {
        string locked = "<w:p>" + ControlTestDocuments.Control(7, "<w:text/>", "Locked", "<w:lock w:val='contentLocked'/>") + "</w:p>";
        WordDocument source = ControlTestDocuments.LoadBody(locked, MathOptions()); WordDocument target = ObjectFragmentTarget(false);
        target.InsertFragmentAtBookmark("one", LoadObjects(Input()), TestContext.Current.CancellationToken);
        target.InsertFragmentAtBookmark("two", source, TestContext.Current.CancellationToken);
        byte[] before = SaveObjects(target);
        Assert.Throws<InvalidOperationException>(() => target.SetControlText(target.ContentControls[0], "Bypass", TestContext.Current.CancellationToken));
        Assert.Equal(before, SaveObjects(target));
        Guid chart = target.Objects.Single(item => item.Chart != null).Id;
        target.UpdateChart(target.Objects.Single(item => item.Id == chart), Definition(ChartKind.Line), TestContext.Current.CancellationToken);
        Assert.Equal("Locked", target.ContentControls[0].DisplayText);
        target.RemoveObject(target.Objects.Single(item => item.Id == chart), TestContext.Current.CancellationToken);
        Assert.Single(target.Objects); Assert.Equal("Locked", target.ContentControls[0].DisplayText);
        ControlTestDocuments.Validate(SaveObjects(target));
    }

    [Fact]
    public void MixedOwnersRejectUnsupportedProfilesAndCancelledEditsAtomically()
    {
        string control = "<w:p>" + ControlTestDocuments.Control(7, "<w:text/>", "Value") + "</w:p>";
        string bookmark = "<w:p><w:bookmarkStart w:id='1' w:name='one'/><w:r><w:t>Insert</w:t></w:r><w:bookmarkEnd w:id='1'/></w:p>";
        WordDocument document = ControlTestDocuments.LoadBody(control + bookmark, MathOptions());
        document.InsertFragmentAtBookmark("one", LoadObjects(Input()), TestContext.Current.CancellationToken);
        byte[] before = SaveObjects(document);
        foreach (var policy in new[] { new ConversionOptions { Typography = new TypographyOptions { Enabled = true } },
            new ConversionOptions { Pdf = new PdfOptions { Accessibility = PdfAccessibility.Tagged } },
            new ConversionOptions { Pdf = new PdfOptions { Conformance = PdfConformance.PdfA2b } } })
        {
            using var output = new MemoryStream(); output.WriteByte(3);
            Assert.Throws<NotSupportedException>(() => document.Save(output, DocumentFormat.Pdf, policy, TestContext.Current.CancellationToken));
            Assert.Equal(new byte[] { 3 }, output.ToArray());
        }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => document.SetControlText(document.ContentControls[0], "No", cancelled.Token));
        Assert.Throws<OperationCanceledException>(() => document.UpdateEquation(document.Objects.Single(item => item.Equation != null), EquationExpression.Text("No"), cancelled.Token));
        Assert.Equal(before, SaveObjects(document));
    }

    [Fact]
    public void ControlCreationCannotSplitAnObjectParagraph()
    {
        var document = WordDocument.Create(MathOptions()); document.AddChart(Definition(), cancellationToken: TestContext.Current.CancellationToken);
        byte[] before = SaveObjects(document);
        var error = Assert.Throws<NotSupportedException>(() => document.InsertContentControl(0, 0,
            new ContentControlDefinition(ContentControlKind.PlainText, initialText: "Value"), TestContext.Current.CancellationToken));
        Assert.StartsWith("Control.ProtectedOwner:", error.Message, StringComparison.Ordinal); Assert.Equal(before, SaveObjects(document));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ObjectsInsideStructuralControlOwnersAreRejected(bool row)
    {
        byte[] input = Xml(Input(), "word/document.xml", xml =>
        {
            XNamespace w = TestDocuments.W;
            XElement paragraph = xml.Descendants(w + "drawing").Single().Ancestors(w + "p").First(); paragraph.Remove();
            var cell = new XElement(w + "tc", new XElement(w + "tcPr", new XElement(w + "tcW", new XAttribute(w + "type", "dxa"), new XAttribute(w + "w", "4000"))), paragraph);
            XElement Wrap(XElement content) => new XElement(w + "sdt", new XElement(w + "sdtPr", new XElement(w + "id", new XAttribute(w + "val", 7)), new XElement(w + "richText")), new XElement(w + "sdtContent", content));
            var tr = new XElement(w + "tr", row ? cell : Wrap(cell));
            var table = new XElement(w + "tbl", new XElement(w + "tblPr", new XElement(w + "tblW", new XAttribute(w + "type", "dxa"), new XAttribute(w + "w", "4000"))),
                new XElement(w + "tblGrid", new XElement(w + "gridCol", new XAttribute(w + "w", "4000"))), row ? Wrap(tr) : tr);
            xml.Descendants(w + "sectPr").Single().AddBeforeSelf(table);
        });
        using var stream = new MemoryStream(input);
        var error = Assert.Throws<NotSupportedException>(() => WordDocument.LoadWithControls(stream, out _, MathOptions(), TestContext.Current.CancellationToken));
        Assert.True(error.Message.StartsWith("Control.ProtectedOwner:", StringComparison.Ordinal) || error.Message.StartsWith("Object.Owner:", StringComparison.Ordinal));
        Assert.True(stream.CanRead);
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
