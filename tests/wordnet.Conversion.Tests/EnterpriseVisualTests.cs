using System;
using System.IO;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Rendering.Skia;
using Xunit;

namespace WordNet.Tests;

public sealed class EnterpriseVisualTests
{
    [Theory]
    [InlineData("letter-stories")]
    [InlineData("logo-png")]
    [InlineData("justified")]
    public void ManagedPdfMatchesReviewedRasterBaseline(string name)
    {
        string root = Path.Combine(TestDocuments.FixtureDirectory, "Enterprise");
        byte[] input = ExplicitLayoutFixture.LegacyMetrics(File.ReadAllBytes(Path.Combine(root, name + ".docx")));
        byte[] output = new WordConverter().ConvertToPdf(input, new ConversionOptions(FidelityMode.Lenient)
        { Clock = () => new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero) });
        using PdfDocument pdf = PdfDocument.Open(output, SkiaRenderingParsingOptions.Instance);
        pdf.AddSkiaPageFactory();
        using SKBitmap actual = pdf.GetPageAsSKBitmap(1, 1, SKColors.White);
        string? regenerate = Environment.GetEnvironmentVariable("WORDNET_REGENERATE_GOLDENS");
        if (!string.IsNullOrEmpty(regenerate))
        {
            Directory.CreateDirectory(regenerate);
            using SKData data = actual.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(regenerate, name + ".png"), data.ToArray());
        }
        string baseline = Path.Combine(root, "Goldens", name + ".png");
        if (!string.IsNullOrEmpty(regenerate)) baseline = Path.Combine(regenerate, name + ".png");
        using SKBitmap expected = SKBitmap.Decode(baseline) ?? throw new InvalidDataException("Visual baseline is missing: " + baseline);
        Assert.Equal(expected.Width, actual.Width); Assert.Equal(expected.Height, actual.Height);
        long changed = 0, difference = 0;
        for (int y = 0; y < actual.Height; y++) for (int x = 0; x < actual.Width; x++)
        {
            SKColor a = actual.GetPixel(x, y), b = expected.GetPixel(x, y);
            int delta = Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue);
            difference += delta; if (delta > 96) changed++;
        }
        double pixels = (double)actual.Width * actual.Height;
        Assert.InRange(changed / pixels, 0, 0.002);
        Assert.InRange(difference / (pixels * 3), 0, 0.25);
    }
}
