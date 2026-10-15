using System;
using System.IO;
using System.Linq;
using System.Text;
using AngleSharp.Html.Parser;
using Xunit;

namespace WordNet.Tests;

public sealed class HtmlConversionTests
{
    [Fact]
    public void EmailOutputPreservesStructureAndInlineAppearance()
    {
        byte[] input = TestDocuments.Docx(TestDocuments.Body("<w:p><w:pPr><w:outlineLvl w:val='1'/><w:jc w:val='center'/><w:spacing w:before='120' w:after='180'/></w:pPr>"
            + "<w:r><w:rPr><w:b/><w:i/><w:u/><w:strike/><w:sz w:val='30'/><w:color w:val='336699'/><w:highlight w:val='yellow'/><w:rFonts w:ascii='Brand Font'/></w:rPr><w:t>&lt;Heading&gt; &amp; æøå éñ €</w:t><w:tab/><w:t>Tab</w:t><w:br/><w:t>Next</w:t></w:r></w:p>"
            + "<w:tbl><w:tblPr><w:tblW w:w='4000' w:type='dxa'/></w:tblPr><w:tblGrid><w:gridCol w:w='2000'/><w:gridCol w:w='2000'/></w:tblGrid>"
            + "<w:tr><w:tc><w:tcPr><w:gridSpan w:val='2'/><w:tcMar><w:left w:w='120' w:type='dxa'/></w:tcMar></w:tcPr><w:p><w:r><w:t>Merged</w:t></w:r></w:p></w:tc></w:tr></w:tbl>"));
        byte[] output = new WordConverter().ConvertToHtml(input);
        using var html = new HtmlParser().ParseDocument(Encoding.UTF8.GetString(output));
        Assert.Equal("html", html.Doctype!.Name);
        Assert.Equal("utf-8", html.QuerySelector("meta[charset]")!.GetAttribute("charset"));
        var heading = html.QuerySelector("h2")!;
        Assert.Contains("<Heading> & æøå éñ €", heading.TextContent);
        Assert.Contains("text-align:center", heading.GetAttribute("style"));
        Assert.Contains("font-family:'Brand Font'", heading.QuerySelector("span")!.GetAttribute("style"));
        Assert.Contains("text-decoration:underline line-through", heading.QuerySelector("span")!.GetAttribute("style"));
        Assert.Contains("background-color:#FFFF00", heading.QuerySelector("span")!.GetAttribute("style"));
        Assert.Single(heading.QuerySelectorAll("br"));
        Assert.Equal("2", html.QuerySelector("td")!.GetAttribute("colspan"));
        Assert.Contains("padding:0pt 0pt 0pt 6pt", html.QuerySelector("td")!.GetAttribute("style"));
        Assert.Empty(html.QuerySelectorAll("style,link,script,iframe,object"));
        Assert.All(html.All.SelectMany(e => e.Attributes), attribute => Assert.False(attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase)));
    }

    [Theory]
    [InlineData("https://example.org/?a=1&amp;b=2", true)]
    [InlineData("mailto:person@example.org", true)]
    [InlineData("javascript:alert(1)", false)]
    public void LinksAreEscapedAndFiltered(string target, bool allowed)
    {
        string relationship = TestDocuments.LinkRelationship(target.Replace("&amp;", "&"));
        byte[] input = TestDocuments.Docx(TestDocuments.Body("<w:p><w:hyperlink r:id='link'><w:r><w:t>Visible</w:t></w:r></w:hyperlink></w:p>"), relationship);
        byte[] output = new WordConverter().ConvertToHtml(input, new ConversionOptions(FidelityMode.BestEffort));
        using var html = new HtmlParser().ParseDocument(Encoding.UTF8.GetString(output));
        Assert.Equal(allowed ? 1 : 0, html.QuerySelectorAll("a").Length);
        Assert.Contains("Visible", html.Body!.TextContent);
    }

    [Fact]
    public void HtmlOptionsValidateBeforeDestinationCopy()
    {
        byte[] input = TestDocuments.Docx(TestDocuments.Body(TestDocuments.TextParagraph("Text")));
        var options = new ConversionOptions { Html = new HtmlOptions { MaxContentWidthPx = 0 } };
        using var source = new MemoryStream(input);
        using var output = new MemoryStream(new byte[] { 42 }, writable: true);
        Assert.Throws<ArgumentOutOfRangeException>(() => new WordConverter().Convert(source, output, DocumentFormat.Docx, DocumentFormat.Html, options, TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
        Assert.True(source.CanRead);
        Assert.True(output.CanWrite);
    }
}
