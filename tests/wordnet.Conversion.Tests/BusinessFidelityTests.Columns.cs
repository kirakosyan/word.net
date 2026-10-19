using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using UglyToad.PdfPig;
using WordNet.Formats;
using WordNet.Internal;
using Xunit;

namespace WordNet.Tests;

public sealed partial class BusinessFidelityTests
{
    private const string UnequalPage = "<w:sectPr><w:pgSz w:w='8000' w:h='6000'/><w:pgMar w:top='600' w:bottom='600' w:left='600' w:right='600' w:header='0' w:footer='0' w:gutter='0'/>"
        + "<w:cols w:num='2' w:equalWidth='0'><w:col w:w='4000' w:space='400'/><w:col w:w='2400'/></w:cols></w:sectPr>";

    [Theory]
    [InlineData(DocumentFormat.Pdf, 2)]
    [InlineData(DocumentFormat.Xps, 2)]
    [InlineData(DocumentFormat.Pdf, 3)]
    [InlineData(DocumentFormat.Xps, 3)]
    public void UnequalColumnsPreserveTwipsAndExplicitBreakOrigins(DocumentFormat format, int count)
    {
        string geometry = count == 2 ? UnequalPage : UnequalPage.Replace("w:num='2'", "w:num='3'")
            .Replace("<w:col w:w='4000' w:space='400'/><w:col w:w='2400'/>", "<w:col w:w='1200' w:space='400'/><w:col w:w='2400' w:space='200'/><w:col w:w='2600'/>");
        byte[] input = Docx("<w:p><w:r><w:t>First</w:t><w:br w:type='column'/><w:t>Second</w:t>"
            + (count == 3 ? "<w:br w:type='column'/><w:t>Third</w:t>" : "") + "</w:r></w:p>" + geometry);
        byte[] output = Export(input, format, out ConversionResult result);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "Docx.Columns");
        if (format == DocumentFormat.Pdf)
        {
            using PdfDocument pdf = PdfDocument.Open(output);
            Assert.Single(pdf.GetPages()); Assert.Equal(count == 2 ? "FirstSecond" : "FirstSecondThird", pdf.GetPage(1).Text);
            Assert.Equal(30, pdf.GetPage(1).Letters[0].StartBaseLine.X, 2);
            Assert.Equal(count == 2 ? 250 : 110, pdf.GetPage(1).Letters[5].StartBaseLine.X, 2);
            if (count == 3) Assert.Equal(240, pdf.GetPage(1).Letters[11].StartBaseLine.X, 2);
        }
        else
        {
            XNamespace xps = "http://schemas.microsoft.com/xps/2005/06";
            XElement[] glyphs = Part(output, "Documents/1/Pages/1.fpage").Descendants(xps + "Glyphs").ToArray();
            Assert.Equal(count == 2 ? "FirstSecond" : "FirstSecondThird", string.Concat(glyphs.Select(e => (string?)e.Attribute("UnicodeString"))));
            Assert.Equal(40, double.Parse(glyphs[0].Attribute("OriginX")!.Value, CultureInfo.InvariantCulture), 2);
            Assert.Equal((count == 2 ? 250 : 110) * 96.0 / 72, double.Parse(glyphs[1].Attribute("OriginX")!.Value, CultureInfo.InvariantCulture), 2);
            if (count == 3) Assert.Equal(320, double.Parse(glyphs[2].Attribute("OriginX")!.Value, CultureInfo.InvariantCulture), 2);
        }
        byte[] saved = Export(input, DocumentFormat.Docx); Schema(saved);
        XElement cols = Part(saved, "word/document.xml").Descendants(W + "cols").Single();
        Assert.Equal("false", cols.Attribute(W + "equalWidth")!.Value);
        Assert.Equal(count == 2 ? "4000" : "1200", cols.Elements(W + "col").First().Attribute(W + "w")!.Value);
        Assert.Equal(count == 2 ? "2400" : "2600", cols.Elements(W + "col").Last().Attribute(W + "w")!.Value);
        using var stream = new System.IO.MemoryStream(input);
        var context = new ConversionContext(new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        var model = DocxReader.Read(stream, context); var copy = new DocumentCloner(context).Copy(model);
        copy.FinalSection!.ExplicitColumns.Clear(); Assert.Equal(count, model.FinalSection!.ExplicitColumns.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LongParagraphsFlowIntoUnequalColumnsWithoutGutterLeak(bool narrowFirst)
    {
        string page = narrowFirst ? UnequalPage.Replace("w:w='4000'", "w:w='2400'").Replace("<w:col w:w='2400'/>", "<w:col w:w='4000'/>") : UnequalPage;
        string text = string.Concat(Enumerable.Range(0, 180).Select(i => "item" + i.ToString("D3", CultureInfo.InvariantCulture) + " "));
        using PdfDocument pdf = PdfDocument.Open(Export(Docx(P(text) + page), DocumentFormat.Pdf));
        Assert.Equal(text, string.Concat(pdf.GetPages().Select(p => p.Text)));
        double gutterStart = narrowFirst ? 150 : 230, rightStart = gutterStart + 20;
        Assert.All(pdf.GetPages().SelectMany(p => p.Letters), letter =>
            Assert.True(letter.BoundingBox.Right <= gutterStart + 0.1 || letter.BoundingBox.Left >= rightStart - 0.1));
        Assert.Contains(pdf.GetPages().SelectMany(p => p.Letters), letter => letter.StartBaseLine.X >= rightStart);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LongTablesRemeasureRowsAndSplitCellContentAtActiveColumnWidth(bool oneLongRow)
    {
        string text = string.Concat(Enumerable.Range(0, 120).Select(i => "cell" + i.ToString("D3", CultureInfo.InvariantCulture) + " "));
        string rows = oneLongRow ? "<w:tr><w:tc>" + P(text) + "</w:tc></w:tr>"
            : string.Concat(Enumerable.Range(0, 120).Select(i => "<w:tr><w:tc>" + P("cell" + i.ToString("D3", CultureInfo.InvariantCulture) + " ") + "</w:tc></w:tr>"));
        using PdfDocument pdf = PdfDocument.Open(Export(Docx("<w:tbl>" + rows + "</w:tbl>" + UnequalPage), DocumentFormat.Pdf));
        Assert.Equal(text, string.Concat(pdf.GetPages().Select(p => p.Text)));
        Assert.All(pdf.GetPages().SelectMany(p => p.Letters), letter => Assert.True(letter.BoundingBox.Right <= 230.1 || letter.BoundingBox.Left >= 249.9));
        Assert.Contains(pdf.GetPages().SelectMany(p => p.Letters), letter => letter.StartBaseLine.X >= 250);
    }
    [Fact]
    public void FootnotesUseTheReferencingUnequalColumnGeometry()
    {
        string content = "<w:p><w:r><w:t>First</w:t><w:br w:type='column'/><w:t>Second</w:t><w:footnoteReference w:id='7'/></w:r></w:p>";
        string notes = "<w:footnotes xmlns:w='" + W + "'><w:footnote w:id='7'>" + P("ColumnNote has bounded geometry in this column.") + "</w:footnote></w:footnotes>";
        byte[] input = TestDocuments.Docx(TestDocuments.Body(content + UnequalPage), "<Relationship Id='notes' Type='" + TestDocuments.R + "/footnotes' Target='footnotes.xml'/>",
            extra: new System.Collections.Generic.KeyValuePair<string, string>("word/footnotes.xml", notes));
        using PdfDocument pdf = PdfDocument.Open(Export(input, DocumentFormat.Pdf));
        Assert.InRange(pdf.GetPage(1).GetWords().Single(w => w.Text == "ColumnNote").BoundingBox.Left, 250, 270);
        Assert.All(pdf.GetPages().SelectMany(p => p.Letters), letter => Assert.True(letter.BoundingBox.Right <= 230.1 || letter.BoundingBox.Left >= 249.9));
    }

    [Theory]
    [InlineData("<w:col w:w='0'/>")]
    [InlineData("<w:col w:w='6803'/>")]
    [InlineData("")]
    [InlineData("<w:col w:w='4000'/>")]
    public void InvalidExplicitColumnsPreserveTheDestination(string columns)
    {
        string page = UnequalPage.Remove(UnequalPage.IndexOf("<w:cols", StringComparison.Ordinal)) + "<w:cols w:num='2' w:equalWidth='0'>" + columns + "</w:cols></w:sectPr>";
        Fails<System.IO.InvalidDataException>(Docx(P("Text") + page), DocumentFormat.Pdf);
    }
    [Theory]
    [InlineData("num")]
    [InlineData("four")]
    [InlineData("gutter")]
    public void ExplicitColumnAdmissionIgnoresNumAndDiagnosesUnsupportedShapes(string kind)
    {
        string page = kind == "num" ? UnequalPage.Replace("w:num='2'", "w:num='99'") : kind == "gutter"
            ? UnequalPage.Replace("w:gutter='0'", "w:gutter='400'").Replace("w:w='4000'", "w:w='3600'")
            : UnequalPage.Replace("<w:col w:w='4000' w:space='400'/><w:col w:w='2400'/>", string.Concat(Enumerable.Repeat("<w:col w:w='1700'/>", 4)));
        var context = new ConversionContext(new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        using var source = new System.IO.MemoryStream(Docx(P("Text") + page));
        var model = DocxReader.Read(source, context);
        Assert.Equal(kind == "four" ? 1 : 2, model.FinalSection!.Columns);
        if (kind == "four") Assert.Contains(context.Result(0).Diagnostics, d => d.Code == "Docx.Columns");
        if (kind == "gutter") { Assert.Equal(50, model.FinalSection.Left); Assert.Contains(context.Result(0).Diagnostics, d => d.Code == "Docx.Gutter"); }
        using var input = new System.IO.MemoryStream(Docx(P("Text") + page)); using var output = new System.IO.MemoryStream();
        new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        using PdfDocument result = PdfDocument.Open(output.ToArray()); Assert.Contains("Text", result.GetPage(1).Text, StringComparison.Ordinal);
    }
}
