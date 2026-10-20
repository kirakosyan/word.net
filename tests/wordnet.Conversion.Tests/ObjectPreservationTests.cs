using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using WordNet.Formats;
using Xunit;

namespace WordNet.Tests;

public sealed partial class ObjectFoundationTests
{
    private static WordDocument LoadObjects(byte[] bytes)
    { using var input = new MemoryStream(bytes); return WordDocument.Load(input, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken); }
    private static byte[] SaveObjects(WordDocument doc)
    { using var output = new MemoryStream(); doc.Save(output, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken); return output.ToArray(); }
    private static byte[] PartBytes(byte[] bytes, string name)
    { using var stream = new MemoryStream(bytes); using var package = new ZipArchive(stream); using Stream input = package.GetEntry(name)!.Open(); using var data = new MemoryStream(); input.CopyTo(data); return data.ToArray(); }
    [Theory]
    [InlineData("column")]
    [InlineData("line")]
    public void ChartAndOmmlSaveRetainRealEditableObjectsAndOriginalWorkbookBytes(string kind)
    {
        byte[] bytes = Input(kind); WordDocument doc = LoadObjects(bytes); byte[] saved = SaveObjects(doc);
        Assert.Equal(PartBytes(bytes, "word/embeddings/data.xlsx"), PartBytes(saved, "word/embeddings/chart1.xlsx"));
        using var stream = new MemoryStream(saved); using var package = WordprocessingDocument.Open(stream, false);
        Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
        XNamespace m = MathObjectFormat.M;
        XDocument xml = XDocument.Parse(Encoding.UTF8.GetString(PartBytes(saved, "word/document.xml")));
        Assert.Equal("x", xml.Descendants(m + "num").Single().Value); Assert.Equal("2", xml.Descendants(m + "den").Single().Value);
        Assert.Single(xml.Descendants(C + "chart")); Assert.Equal(saved, SaveObjects(doc));
        using var read = new MemoryStream(saved); var model = DocxReader.Read(read, Context()); Assert.Equal(2, model.ObjectCount);
        Assert.Equal(North, model.BodyParagraphs.SelectMany(p => p.Runs).Single(r => r.Object?.Chart != null).Object!.Chart!.Series[0].Values);
    }
    [Fact]
    public void CompositionRemapsPlacementsDrawingIdsAndGraphOwnersWithoutChangingSource()
    {
        WordDocument source = LoadObjects(Input()); byte[] before = SaveObjects(source);
        WordDocument destination = WordDocument.Create(new ConversionOptions(FidelityMode.Lenient));
        destination.Append(source, cancellationToken: TestContext.Current.CancellationToken);
        destination.Append(source, cancellationToken: TestContext.Current.CancellationToken);
        byte[] saved = SaveObjects(destination); Assert.Equal(before, SaveObjects(source));
        using var stream = new MemoryStream(saved); using (var package = WordprocessingDocument.Open(stream, false)) Assert.Empty(new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken));
        using var read = new MemoryStream(saved); var model = DocxReader.Read(read, Context()); Assert.Equal(4, model.ObjectCount);
        Assert.Equal(4, model.BodyParagraphs.SelectMany(p => p.Runs).Where(r => r.Object != null).Select(r => r.Object!.Id).Distinct().Count());
        Assert.Equal(PartBytes(before, "word/embeddings/chart1.xlsx"), PartBytes(saved, "word/embeddings/chart2.xlsx"));
    }
    [Fact]
    public void UnsupportedOwnerEditsAndTemplateExecutionAreAtomic()
    {
        WordDocument doc = LoadObjects(Input()); byte[] before = SaveObjects(doc);
        Assert.Throws<NotSupportedException>(() => doc.ReplaceParagraph(0, new[] { new DocumentText("Changed") }, TestContext.Current.CancellationToken));
        Assert.Throws<NotSupportedException>(() => doc.Merge(new { Rows = Array.Empty<object>() }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Throws<NotSupportedException>(() => doc.Append(doc, CompositionStyle.UseDestination, TestContext.Current.CancellationToken));
        Assert.Equal(before, SaveObjects(doc));
    }
    [Theory]
    [InlineData("control")]
    [InlineData("revision")]
    public void StructuredChartOwnersRejectInsteadOfFlatteningObjects(string kind)
    {
        byte[] bytes = Xml(Input(), "word/document.xml", xml =>
        {
            XNamespace w = TestDocuments.W; XElement paragraph = xml.Descendants(w + "p").First();
            if (kind == "control") paragraph.ReplaceWith(new XElement(w + "sdt", new XElement(w + "sdtPr", new XElement(w + "tag", new XAttribute(w + "val", "repeat:Rows"))), new XElement(w + "sdtContent", new XElement(paragraph))));
            else { XElement run = paragraph.Element(w + "r")!; run.ReplaceWith(new XElement(w + "ins", new XAttribute(w + "id", 1), new XAttribute(w + "author", "Author"), new XElement(run))); }
        });
        NotSupportedException error = Assert.Throws<NotSupportedException>(() => LoadObjects(bytes)); Assert.Contains("Object.Owner", error.Message, StringComparison.Ordinal);
    }
    [Fact]
    public void NestedExpansionUsesTheOuterOperationAllowance()
    {
        byte[] input = Input(); var original = Read(input); long outer;
        using (var stream = new MemoryStream(input)) using (var package = new ZipArchive(stream)) outer = package.Entries.Sum(e => e.Length);
        var options = new ConversionOptions(maximumExpandedBytes: checked((int)(outer + original.Graph.NestedExpansion - 1)));
        Assert.Throws<InvalidDataException>(() => Read(input, Context(options)));
    }
    [Fact]
    public void ObjectSemanticLossAndConformanceCombinationsLeaveOutputUntouched()
    {
        var converter = new WordConverter(); using var input = new MemoryStream(Input()); using var output = new MemoryStream(new byte[] { 7, 8, 9 }, true);
        Assert.Throws<NotSupportedException>(() => converter.Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 7, 8, 9 }, output.ToArray());
    }
}
