using System.IO;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed class LayoutCorpusTests
{
    [Theory]
    [InlineData(DocumentFormat.Docx)]
    [InlineData(DocumentFormat.Doc)]
    public void RenderedSectionsAndMergedTablesMatchIndependentReference(DocumentFormat source)
    {
        byte[] input = ConversionAcceptanceTests.Fixture("layout-corpus.docx");
        if (source == DocumentFormat.Doc) input = Convert(input, DocumentFormat.Docx, source);
        using PdfDocument actual = PdfDocument.Open(Convert(input, source, DocumentFormat.Pdf));
        using PdfDocument expected = PdfDocument.Open(ConversionAcceptanceTests.Fixture("layout-reference.pdf"));
        Assert.Equal(expected.NumberOfPages, actual.NumberOfPages);
        for (int page = 1; page <= expected.NumberOfPages; page++)
        {
            var a = actual.GetPage(page); var e = expected.GetPage(page);
            Assert.Equal(e.Width, a.Width); Assert.Equal(e.Height, a.Height);
            Assert.Equal(e.Text, a.Text); Assert.Equal(e.Letters.Count, a.Letters.Count);
            for (int letter = 0; letter < e.Letters.Count; letter++)
            {
                Assert.Equal(e.Letters[letter].Value, a.Letters[letter].Value);
                Assert.InRange(a.Letters[letter].StartBaseLine.X - e.Letters[letter].StartBaseLine.X, -0.03, 0.03);
                Assert.InRange(a.Letters[letter].StartBaseLine.Y - e.Letters[letter].StartBaseLine.Y, -0.03, 0.03);
            }
        }
    }

    private static byte[] Convert(byte[] bytes, DocumentFormat from, DocumentFormat to)
    {
        using var input = new MemoryStream(bytes); using var output = new MemoryStream();
        new WordConverter().Convert(input, output, from, to, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        return output.ToArray();
    }
}
