using System;
using System.IO;
using System.Linq;
using System.Text;
using AngleSharp.Html.Parser;
using DocumentFormat.OpenXml.Packaging;
using OpenMcdf;
using UglyToad.PdfPig;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Model;
using Xunit;

namespace WordNet.Tests;

public sealed class EnterpriseBinaryMetadataTests
{
    [Fact]
    public void BinaryWordPreservesUnicodePropertiesFieldsAndClickableLinks()
    {
        const string title = "Offer å ø æ €", author = "Émilie França";
        string body = "<w:p><w:fldSimple w:instr='TITLE'><w:r><w:t>old title</w:t></w:r></w:fldSimple>"
            + "<w:r><w:t> / </w:t></w:r><w:fldSimple w:instr='AUTHOR'><w:r><w:t>old author</w:t></w:r></w:fldSimple>"
            + "<w:fldSimple w:instr='HYPERLINK &quot;https://example.com/offer&quot;'><w:r><w:rPr><w:b/></w:rPr><w:t>Open offer</w:t></w:r></w:fldSimple></w:p>";
        using var docx = new MemoryStream(); byte[] initial = TestDocuments.Docx(TestDocuments.Body(body)); docx.Write(initial, 0, initial.Length);
        using (WordprocessingDocument package = WordprocessingDocument.Open(docx, true))
        { package.PackageProperties.Title = title; package.PackageProperties.Creator = author; }
        byte[] binary = Convert(docx.ToArray(), DocumentFormat.Docx, DocumentFormat.Doc);
        // Inspect the property set independently of our DOC reader: property IDs,
        // code page, type and byte-sized Unicode CodePageStrings (MS-OLEPS).
        using (RootStorage root = RootStorage.Open(new MemoryStream(binary)))
        {
            using CfbStream stream = root.OpenStream("\u0005SummaryInformation"); using var buffer = new MemoryStream(); stream.CopyTo(buffer);
            byte[] data = buffer.ToArray(); int section = BitConverter.ToInt32(data, 44);
            Assert.Equal(1200, BitConverter.ToUInt16(data, section + BitConverter.ToInt32(data, section + 12) + 4));
            Assert.Equal(title, Property(2)); Assert.Equal(author, Property(4));
            string Property(int id)
            {
                int count = BitConverter.ToInt32(data, section + 4);
                int record = Enumerable.Range(0, count).Select(i => section + 8 + i * 8).Single(p => BitConverter.ToInt32(data, p) == id);
                int start = section + BitConverter.ToInt32(data, record + 4);
                Assert.Equal(30, BitConverter.ToInt32(data, start));
                int length = BitConverter.ToInt32(data, start + 4);
                Assert.Equal(0, length % 2); return Encoding.Unicode.GetString(data, start + 8, length - 2);
            }
        }
        using PdfDocument pdf = PdfDocument.Open(Convert(binary, DocumentFormat.Doc, DocumentFormat.Pdf));
        Assert.Equal(title, pdf.Information.Title); Assert.Equal(author, pdf.Information.Author);
        Assert.Contains(title + " / " + author, pdf.GetPage(1).Text, StringComparison.Ordinal);
        Assert.Contains(pdf.GetPage(1).GetHyperlinks(), h => h.Uri == "https://example.com/offer");
        using var html = new HtmlParser().ParseDocument(Encoding.UTF8.GetString(Convert(binary, DocumentFormat.Doc, DocumentFormat.Html)));
        Assert.Equal("Open offer", Assert.Single(html.QuerySelectorAll("a")).TextContent);
        Assert.Equal("https://example.com/offer", html.QuerySelector("a")!.GetAttribute("href"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentUnicodePropertySetIsReadAndMalformedLengthIsRejected(bool malformed)
    {
        var model = new DocumentModel(); var paragraph = new ParagraphBlock(); paragraph.Runs.Add(new TextRun("Body")); model.Blocks.Add(paragraph);
        using var binary = new MemoryStream(); DocFormat.Write(model, binary, Context());
        using (RootStorage root = RootStorage.Open(binary, StorageModeFlags.LeaveOpen))
        {
            // One independent VT_LPWSTR title with no code-page property.
            byte[] text = Encoding.Unicode.GetBytes("Independent é\0"), bytes = new byte[72 + text.Length];
            Put(0, 0xfffe); Put(24, 1); Buffer.BlockCopy(new Guid("f29f85e0-4ff9-1068-ab91-08002b27b3d9").ToByteArray(), 0, bytes, 28, 16);
            Put(44, 48); Put(48, bytes.Length - 48); Put(52, 1); Put(56, 2); Put(60, 16); Put(64, 31); Put(68, malformed ? int.MaxValue : text.Length / 2);
            Buffer.BlockCopy(text, 0, bytes, 72, text.Length);
            using CfbStream stream = root.CreateStream("\u0005SummaryInformation"); stream.Write(bytes, 0, bytes.Length); root.Flush();
            void Put(int p, int value) => Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, p, 4);
        }
        binary.Position = 0;
        if (malformed) Assert.Throws<InvalidDataException>(() => DocFormat.Read(binary, Context()));
        else Assert.Equal("Independent é", DocFormat.Read(binary, Context()).Title);
    }

    private static ConversionContext Context() => new ConversionContext(new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
    private static byte[] Convert(byte[] bytes, DocumentFormat source, DocumentFormat destination)
    {
        using var input = new MemoryStream(bytes); using var output = new MemoryStream();
        new WordConverter().Convert(input, output, source, destination, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        return output.ToArray();
    }
}
