using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Layout;
using Xunit;

namespace WordNet.Tests;

public sealed class NativeTableTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NativeTablesWrapSplitAndRepeatHeadersWithLogicalUaOwnership(bool hindi, bool footer)
    {
        string story = footer ? "footer" : "header", root = footer ? "ftr" : "hdr";
        string text = hindi ? "हिन्दी क्षि" : "مرحبا عربي";
        string paragraph = TestDocuments.TextParagraph(text);
        string body = "<w:tbl><w:tblPr><w:tblW w:w='6000' w:type='dxa'/></w:tblPr><w:tblGrid><w:gridCol w:w='3000'/><w:gridCol w:w='3000'/></w:tblGrid>"
            + "<w:tr><w:trPr><w:tblHeader/></w:trPr><w:tc>" + TestDocuments.TextParagraph("Item") + "</w:tc><w:tc>" + TestDocuments.TextParagraph("Value") + "</w:tc></w:tr>"
            + string.Concat(Enumerable.Range(0, 12).Select(i => "<w:tr><w:tc>" + TestDocuments.TextParagraph("Row " + i.ToString(System.Globalization.CultureInfo.InvariantCulture))
                + "</w:tc><w:tc>" + TestDocuments.TextParagraph(string.Join(" ", Enumerable.Repeat(text, 6))) + "</w:tc></w:tr>"))
            + "</w:tbl><w:sectPr><w:" + story + "Reference w:type='default' r:id='story'/><w:pgSz w:w='8000' w:h='6000'/><w:pgMar w:left='600' w:right='600' w:top='900' w:bottom='900' w:header='300' w:footer='300'/></w:sectPr>";
        byte[] source = TestDocuments.Docx(TestDocuments.Body(body), "<Relationship Id='story' Type='" + TestDocuments.R + "/" + story + "' Target='story.xml'/>",
            extra: new KeyValuePair<string, string>("word/story.xml", "<w:" + root + " xmlns:w='" + TestDocuments.W + "'>" + paragraph + "</w:" + root + ">"));
        ConversionOptions options = ExtendedTypographyTests.Options(hindi); options.Pdf.Language = hindi ? "hi-IN" : "ar"; options.Pdf.Accessibility = PdfAccessibility.PdfUa1; options.Pdf.Compress = false;
        using var input = new MemoryStream(source); WordDocument document = WordDocument.Load(input, DocumentFormat.Docx, out _, options, TestContext.Current.CancellationToken); document.Title = "Multilingual table";
        using var output = new MemoryStream(); document.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        string raw = Encoding.ASCII.GetString(output.ToArray()); Assert.Contains("/Table", raw, StringComparison.Ordinal); Assert.Contains("/TH", raw, StringComparison.Ordinal); Assert.Contains("/TD", raw, StringComparison.Ordinal);
        input.Position = 0; var context = new ConversionContext(options, TestContext.Current.CancellationToken);
        var pages = FixedLayout.Create(DocxReader.Read(input, context), context); Assert.True(pages.Count > 1);
        Assert.All(pages, p => { Assert.Contains(p.Text, t => t.Text == "Item"); Assert.Contains(p.Text, t => t.Text == (hindi ? "हिन्दी" : "مرحبا")); });
        Assert.All(pages.SelectMany(p => p.Text), t => Assert.NotNull(t.NativeShape));
        Assert.Equal(12, pages.SelectMany(p => p.Text).Count(t => t.Text == "Row"));
        if (hindi) Assert.DoesNotContain(pages.SelectMany(p => p.Text), t => t.Text == "ि");
        string? folder = Environment.GetEnvironmentVariable("WORDNET_ROADMAP_EVIDENCE");
        if (folder != null)
        {
            Directory.CreateDirectory(folder); string name = (hindi ? "native-hindi-table" : "native-arabic-table") + (footer ? "-footer" : "");
            File.WriteAllBytes(Path.Combine(folder, name + ".docx"), source); File.WriteAllBytes(Path.Combine(folder, name + ".pdf"), output.ToArray());
        }
    }
}
