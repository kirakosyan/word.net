using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed class MixedMetricsTests
{
    [Theory]
    [InlineData(DocumentFormat.Pdf)]
    [InlineData(DocumentFormat.Xps)]
    public void ExplicitMixedFacesUseIndependentlyReadOpenTypeLineMetrics(DocumentFormat format)
    {
        string root = Path.Combine(TestDocuments.FixtureDirectory, "Roadmap99");
        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "metrics.json")));
        double[] expected = reference.RootElement.GetProperty("lines").EnumerateArray().Select(e => e.GetProperty("expectedTopBaseline").GetDouble()).ToArray();
        using var input = File.OpenRead(Path.Combine(root, "metrics-explicit.docx")); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, format, new ConversionOptions(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "Font.Substituted");
        if (format == DocumentFormat.Pdf)
        {
            using var pdf = PdfDocument.Open(output.ToArray()); var page = pdf.GetPage(1);
            double[] actual = page.Letters.Select(l => page.Height - l.StartBaseLine.Y).Distinct().ToArray(); Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++) Assert.InRange(Math.Abs(expected[i] - actual[i]), 0, .03);
            Assert.Equal("FONT STUDY SHORTRegular faceBold faceItalic face", page.Text);
        }
        else
        {
            output.Position = 0; using var zip = new ZipArchive(output, ZipArchiveMode.Read, true);
            using Stream xml = zip.Entries.Single(e => e.FullName.EndsWith(".fpage", StringComparison.Ordinal)).Open();
            XDocument page = XDocument.Load(xml); double[] actual = page.Descendants().Where(e => e.Name.LocalName == "Glyphs")
                .Select(e => double.Parse(e.Attribute("OriginY")!.Value, System.Globalization.CultureInfo.InvariantCulture) * .75).Distinct().ToArray();
            Assert.Equal(expected.Length, actual.Length); for (int i = 0; i < expected.Length; i++) Assert.InRange(Math.Abs(expected[i] - actual[i]), 0, .03);
        }
    }
}
