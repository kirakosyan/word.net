using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed class RoadmapLayoutTests
{
    [Theory]
    [InlineData(FidelityMode.Strict)]
    [InlineData(FidelityMode.BestEffort)]
    [InlineData(FidelityMode.Lenient)]
    public void BalancingNeverScalesTextToMakeAProbeFit(FidelityMode mode)
    {
        using var input = new MemoryStream(Input(P("First") + P("Second"), "<w:cols w:num='2'/>")); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(mode) { BalanceFinalColumns = true }, TestContext.Current.CancellationToken);
        using var pdf = PdfDocument.Open(output.ToArray()); Assert.Equal(1, pdf.NumberOfPages);
        Assert.All(pdf.GetPage(1).Letters, l => Assert.Equal(11, l.PointSize, 3));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "Layout.TextScaledToFit");
        Assert.Equal("FirstSecond", pdf.GetPage(1).Text);
    }

    [Fact]
    public void SixteenSectionsBalanceIndependentlyWithoutChangingPageCountOrSizes()
    {
        string section = Page.Replace("{0}", "<w:cols w:num='2' w:space='400'/>");
        var body = new System.Text.StringBuilder();
        for (int i = 0; i < 16; i++)
        {
            for (int line = 0; line < 6 + i % 3 * 2; line++)
            {
                string p = P("Section " + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + " line " + line.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (line == 5 + i % 3 * 2 && i < 15) p = p.Replace("</w:pPr>", section + "</w:pPr>");
                body.Append(p);
            }
        }
        byte[] input = TestDocuments.Docx(TestDocuments.Body(body + section));
        using var pdf = PdfDocument.Open(Export(input, true)); using var sequential = PdfDocument.Open(Export(input));
        Assert.Equal(16, pdf.NumberOfPages); Assert.Equal(sequential.NumberOfPages, pdf.NumberOfPages);
        for (int i = 1; i <= 16; i++)
        {
            var page = pdf.GetPage(i); Assert.Equal(sequential.GetPage(i).Text, page.Text);
            Assert.Equal(400, page.Width); Assert.Equal(300, page.Height); Assert.All(page.Letters, l => Assert.Equal(11, l.PointSize, 3));
            Assert.Equal(page.Letters.Where(l => l.StartBaseLine.X < 210).Min(l => l.StartBaseLine.Y), page.Letters.Where(l => l.StartBaseLine.X >= 210).Min(l => l.StartBaseLine.Y), 3);
        }
    }

    [Fact]
    public void HeaderNoteReservationDoesNotSubtractItsOwnHeightTwice()
    {
        string notes = "<w:footnotes xmlns:w='" + TestDocuments.W + "'><w:footnote w:id='1'>" + P(string.Join("</w:t><w:br/><w:t>", Enumerable.Range(0, 8).Select(i => "Note " + i.ToString(System.Globalization.CultureInfo.InvariantCulture)))) + "</w:footnote></w:footnotes>";
        string header = P("Header").Replace("</w:r>", "<w:footnoteReference w:id='1'/></w:r>");
        string table = "<w:tbl><w:tblGrid><w:gridCol w:w='6000'/></w:tblGrid><w:tr><w:trPr><w:tblHeader/><w:trHeight w:val='1800'/></w:trPr><w:tc>" + header
            + "</w:tc></w:tr><w:tr><w:tc>" + P("Body") + "</w:tc></w:tr></w:tbl>";
        byte[] input = TestDocuments.Docx(TestDocuments.Body(table + Page.Replace("{0}", "")), "<Relationship Id='notes' Type='" + TestDocuments.R + "/footnotes' Target='footnotes.xml'/>", extra: new KeyValuePair<string, string>("word/footnotes.xml", notes));
        using var pdf = PdfDocument.Open(Export(input)); string text = string.Concat(pdf.GetPages().Select(p => p.Text));
        Assert.Contains("Header", pdf.GetPage(1).Text, StringComparison.Ordinal); Assert.Contains("Body", pdf.GetPage(1).Text, StringComparison.Ordinal);
        for (int i = 0; i < 8; i++) Assert.Contains("Note " + i.ToString(System.Globalization.CultureInfo.InvariantCulture), text, StringComparison.Ordinal);
    }

    private const string Page = "<w:sectPr><w:pgSz w:w='8000' w:h='6000'/><w:pgMar w:top='600' w:bottom='600' w:left='600' w:right='600'/>{0}</w:sectPr>";
    private static string P(string text, string properties = "") => "<w:p><w:pPr><w:spacing w:after='0'/>" + properties + "</w:pPr><w:r><w:rPr><w:rFonts w:ascii='Noto Sans' w:hAnsi='Noto Sans'/><w:sz w:val='22'/></w:rPr><w:t>" + text + "</w:t></w:r></w:p>";
    private static byte[] Input(string body, string columns = "") => TestDocuments.Docx(TestDocuments.Body(body + Page.Replace("{0}", columns)));
    private static byte[] Export(byte[] input, bool balance = false)
    {
        using var source = new MemoryStream(input); using var output = new MemoryStream();
        new WordConverter().Convert(source, output, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions { BalanceFinalColumns = balance }, TestContext.Current.CancellationToken);
        return output.ToArray();
    }
    [Theory]
    [InlineData(false, 6)]
    [InlineData(true, 6)]
    [InlineData(false, 40)]
    [InlineData(true, 40)]
    public void FinalColumnsBalanceWithinOneLineWithoutChangingEarlierPages(bool unequal, int count)
    {
        string columns = unequal ? "<w:cols w:num='2' w:equalWidth='0'><w:col w:w='2800' w:space='400'/><w:col w:w='3600'/></w:cols>" : "<w:cols w:num='2' w:space='400'/>";
        byte[] input = Input(string.Concat(Enumerable.Range(0, count).Select(i => P("Line " + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)))), columns);
        byte[] bytes = Export(input, true); using var pdf = PdfDocument.Open(bytes); using var normal = PdfDocument.Open(Export(input));
        Assert.Equal(normal.NumberOfPages, pdf.NumberOfPages);
        for (int page = 1; page < pdf.NumberOfPages; page++) Assert.Equal(normal.GetPage(page).Text, pdf.GetPage(page).Text);
        var last = pdf.GetPage(pdf.NumberOfPages); double boundary = unequal ? 190 : 210;
        var left = last.Letters.Where(l => l.StartBaseLine.X < boundary).ToArray(); var right = last.Letters.Where(l => l.StartBaseLine.X >= boundary).ToArray();
        Assert.NotEmpty(left); Assert.NotEmpty(right);
        Assert.InRange(Math.Abs(left.Min(l => l.StartBaseLine.Y) - right.Min(l => l.StartBaseLine.Y)), 0, 11 * 1.362 + .01);
        Assert.Equal(string.Concat(Enumerable.Range(0, count).Select(i => "Line " + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture))), string.Concat(pdf.GetPages().Select(p => p.Text)));
        Evidence("columns-" + (unequal ? "unequal-" : "equal-") + count, input, bytes);
    }
    [Fact]
    public void BalancingRejectsUnqualifiedKeepsAndExplicitBreaksBeforeWriting()
    {
        foreach (string body in new[] { P("Keep", "<w:keepNext/>") + P("Next"), P("Break").Replace("</w:t>", "</w:t><w:br w:type='column'/>") })
        {
            using var source = new MemoryStream(Input(body, "<w:cols w:num='2'/>")); using var output = new MemoryStream(); output.WriteByte(42);
            Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(source, output, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions { BalanceFinalColumns = true }, TestContext.Current.CancellationToken));
            Assert.Equal(new byte[] { 42 }, output.ToArray());
        }
    }
    [Fact]
    public void BalancingRejectsTaggedContentAndFieldsAtomically()
    {
        foreach (bool tagged in new[] { false, true })
        {
            string body = tagged ? P("Tagged content") : "<w:p><w:fldSimple w:instr='PAGE'><w:r><w:t>1</w:t></w:r></w:fldSimple></w:p>";
            using var source = new MemoryStream(Input(body, "<w:cols w:num='2'/>")); using var output = new MemoryStream(); output.WriteByte(42);
            var options = new ConversionOptions { BalanceFinalColumns = true };
            if (tagged) options.Pdf.Accessibility = PdfAccessibility.Tagged;
            Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(source, output, DocumentFormat.Docx, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
            Assert.Equal(new byte[] { 42 }, output.ToArray());
        }
    }
    [Fact]
    public void AllNotesInAnUnsplitRowMoveWithTheirReferences()
    {
        string notes = "<w:footnotes xmlns:w='" + TestDocuments.W + "'><w:footnote w:id='1'>" + P("FIRST NOTE") + "</w:footnote><w:footnote w:id='2'>" + P("SECOND NOTE") + "</w:footnote></w:footnotes>";
        string first = P("First reference").Replace("</w:r>", "<w:footnoteReference w:id='1'/></w:r>"), second = P("Second reference").Replace("</w:r>", "<w:footnoteReference w:id='2'/></w:r>");
        string table = "<w:tbl><w:tblGrid><w:gridCol w:w='3000'/><w:gridCol w:w='3000'/></w:tblGrid><w:tr><w:trPr><w:cantSplit/></w:trPr><w:tc>" + first + "</w:tc><w:tc>" + second + "</w:tc></w:tr></w:tbl>";
        string body = string.Concat(Enumerable.Range(0, 13).Select(i => P("Filler"))) + table + Page.Replace("{0}", "");
        byte[] input = TestDocuments.Docx(TestDocuments.Body(body), "<Relationship Id='notes' Type='" + TestDocuments.R + "/footnotes' Target='footnotes.xml'/>", extra: new KeyValuePair<string, string>("word/footnotes.xml", notes));
        byte[] bytes = Export(input); using var pdf = PdfDocument.Open(bytes); Assert.Equal(2, pdf.NumberOfPages);
        Assert.DoesNotContain("NOTE", pdf.GetPage(1).Text, StringComparison.Ordinal);
        string page = pdf.GetPage(2).Text; Assert.Contains("First reference", page, StringComparison.Ordinal); Assert.Contains("SECOND NOTE", page, StringComparison.Ordinal);
        Assert.Equal(1, pdf.GetPages().Count(p => InteropText.Contains(p.Text, "FIRST NOTE")));
        Evidence("row-multiple-notes", input, bytes);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeptParagraphLogoRemeasuresFollowingTextAtItsDestinationPage(bool moveAnchor)
    {
        string logo = FidelityDocuments.ImageParagraph().Replace(" descr='Red and blue grid'", "")
            .Replace("<w:p>", "<w:p><w:pPr><w:keepNext/><w:spacing w:after='0'/></w:pPr>")
            .Replace("<wp:inline ", "<wp:anchor behindDoc='0' ")
            .Replace("<wp:extent", "<wp:positionH relativeFrom='margin'><wp:posOffset>0</wp:posOffset></wp:positionH><wp:positionV relativeFrom='paragraph'><wp:posOffset>0</wp:posOffset></wp:positionV><wp:wrapSquare wrapText='bothSides'/><wp:extent")
            .Replace("</wp:inline>", "</wp:anchor>");
        if (!moveAnchor) logo = logo.Replace("<w:keepNext/>", "");
        string follow = P("Follow A</w:t><w:br/><w:t>Follow B</w:t><w:br/><w:t>Follow C", "<w:keepLines/>");
        byte[] input = FidelityDocuments.Docx("color-grid.png", string.Concat(Enumerable.Range(0, 13).Select(i => P("Filler"))) + logo + follow + Page.Replace("{0}", ""));
        byte[] bytes = Export(input); using var pdf = PdfDocument.Open(bytes); Assert.Equal(2, pdf.NumberOfPages);
        var page = pdf.GetPage(2); var image = Assert.Single(pdf.GetPage(moveAnchor ? 2 : 1).GetImages());
        Assert.Equal(30, image.BoundingBox.Left, 3); Assert.Equal(moveAnchor ? 270 : 75.234, image.BoundingBox.Top, 3);
        Assert.All(page.GetWords().Where(w => w.Text == "Follow"), word => Assert.Equal(moveAnchor ? 150 : 30, word.Letters[0].StartBaseLine.X, 3));
        Evidence(moveAnchor ? "logo-kept-page" : "logo-clear-next-page", input, bytes);
    }
    [Fact]
    public void EmptyCompatibleContinuousFinalSectionDoesNotInventAPhysicalPage()
    {
        string section = Page.Replace("{0}", "");
        string first = P("Continuous body").Replace("</w:pPr>", section + "</w:pPr>");
        byte[] input = TestDocuments.Docx(TestDocuments.Body(first + section.Replace("<w:sectPr>", "<w:sectPr><w:type w:val='continuous'/>")));
        byte[] bytes = Export(input); using var pdf = PdfDocument.Open(bytes);
        Assert.Equal(1, pdf.NumberOfPages); Assert.Equal("Continuous body", pdf.GetPage(1).Text);
        Evidence("continuous-empty-final", input, bytes);
    }
    internal static void Evidence(string name, byte[] input, byte[] pdf)
    {
        string? root = Environment.GetEnvironmentVariable("WORDNET_ROADMAP_EVIDENCE");
        if (root != null) { Directory.CreateDirectory(root); File.WriteAllBytes(Path.Combine(root, name + ".docx"), input); File.WriteAllBytes(Path.Combine(root, name + ".pdf"), pdf); }
    }
}
