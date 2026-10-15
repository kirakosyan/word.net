using System;
using System.IO;
using System.Linq;
using System.Text;
using AngleSharp.Html.Parser;
using SkiaSharp;
using UglyToad.PdfPig;
using WordNet.Internal;
using WordNet.Layout;
using Xunit;

namespace WordNet.Tests;

public sealed class EnterpriseFontTests
{
    private const string Latin = "å ä ö ø æ é è ê à ç ñ ã õ ì ò ù « » € – — ’ “ ”";

    [Fact]
    public void MergeEngineRunBoundariesDoNotDisableKerning()
    {
        using var font = File.OpenRead(Path.Combine(TestDocuments.FixtureDirectory, "Enterprise", "gpos-pair.ttf"));
        FontSource face = Assert.Single(FontSource.FromStream(font));
        var options = new ConversionOptions(); options.Fonts.Sources.Add(face);
        string run(string text) => "<w:r><w:rPr><w:rFonts w:ascii='" + face.FamilyName + "'/></w:rPr><w:t>" + text + "</w:t></w:r>";
        using PdfDocument split = PdfDocument.Open(new WordConverter().ConvertToPdf(TestDocuments.Docx(TestDocuments.Body("<w:p>" + run("A") + run("V") + "</w:p>")), options));
        using PdfDocument combined = PdfDocument.Open(new WordConverter().ConvertToPdf(Text(face.FamilyName, "AV"), options));
        Assert.Equal(combined.GetPage(1).Text, split.GetPage(1).Text);
        Assert.Equal(combined.GetPage(1).Letters[1].StartBaseLine.X, split.GetPage(1).Letters[1].StartBaseLine.X, 5);
    }

    [Theory]
    [InlineData(FidelityMode.BestEffort)]
    [InlineData(FidelityMode.Lenient)]
    public void CallerFaceWithoutReplacementGlyphUsesBundledCoverage(FidelityMode mode)
    {
        using var stream = File.OpenRead(Path.Combine(TestDocuments.FixtureDirectory, "Enterprise", "caller-cff.otf"));
        FontSource face = Assert.Single(FontSource.FromStream(stream));
        var options = new ConversionOptions(mode); options.Fonts.Sources.Add(face);
        using var input = new MemoryStream(Text(face.FamilyName, "AVAé")); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken);
        using PdfDocument pdf = PdfDocument.Open(output.ToArray());
        Assert.Equal("AVAé", pdf.GetPage(1).Text);
        Assert.Contains(result.Diagnostics, d => d.Code == "Font.MissingGlyph");
        FontUsage usage = Assert.Single(result.Metrics.FontsUsed);
        Assert.Equal(face.FamilyName, usage.RequestedFamily); Assert.Equal("Noto Sans", usage.ResolvedFamily);
        Assert.Equal(1, result.Metrics.SubstitutionCount);
    }

    [Fact]
    public void IndependentlyRasterizedCffAndConvertedOutlinesAgree()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "Enterprise", "caller-cff.otf"));
        byte[] converted = CffFontConverter.Prepare(input, "AVATAR".ToCharArray(),
            new ConversionContext(new ConversionOptions(), TestContext.Current.CancellationToken));
        using SKBitmap expected = Raster(input), actual = Raster(converted);
        long difference = 0; int ink = 0;
        for (int y = 0; y < expected.Height; y++) for (int x = 0; x < expected.Width; x++)
        {
            SKColor a = expected.GetPixel(x, y), b = actual.GetPixel(x, y);
            difference += Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue);
            if (a.Red < 240) ink++;
        }
        Assert.True(ink > 1000);
        // Skia is an independent CFF/TrueType reader. Normalize by ink rather than
        // blank page area so a missing/wrong glyph cannot pass a low pixel ratio.
        Assert.InRange(difference / (ink * 765.0), 0, 0.02);

        static SKBitmap Raster(byte[] fontBytes)
        {
            using SKData data = SKData.CreateCopy(fontBytes);
            using SKTypeface? typeface = SKTypeface.FromData(data);
            Assert.NotNull(typeface);
            using var font = new SKFont(typeface, 96) { Hinting = SKFontHinting.None };
            using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            var bitmap = new SKBitmap(500, 128);
            using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.White);
            canvas.DrawText("AVATAR", 10, 100, SKTextAlign.Left, font, paint);
            return bitmap;
        }
    }

    [Fact]
    public void CffOutlineFontRetainsTextMetricsAndEmbeddedGlyphs()
    {
        using var stream = File.OpenRead(Path.Combine(TestDocuments.FixtureDirectory, "Enterprise", "caller-cff.otf"));
        FontSource face = Assert.Single(FontSource.FromStream(stream));
        var options = new ConversionOptions(); options.Fonts.Sources.Add(face);
        using PdfDocument pdf = PdfDocument.Open(new WordConverter().ConvertToPdf(Text(face.FamilyName, "AVATAR"), options));
        Assert.Equal("AVATAR", pdf.GetPage(1).Text);
        var letters = pdf.GetPage(1).Letters;
        Assert.InRange(Math.Abs(letters[1].StartBaseLine.X - letters[0].StartBaseLine.X - 7.06640625), 0, 0.0001);
        Assert.All(letters, letter => Assert.True(letter.BoundingBox.Height > 0));
    }

    [Theory]
    [InlineData("pair", 15.0703125)]
    [InlineData("class", 15.5390625)]
    public void GposOnlyFontsApplyPairAndClassKerning(string kind, double expected)
    {
        using var stream = File.OpenRead(Path.Combine(TestDocuments.FixtureDirectory, "Enterprise", "gpos-" + kind + ".ttf"));
        FontSource face = Assert.Single(FontSource.FromStream(stream));
        var options = new ConversionOptions(); options.Fonts.Sources.Add(face);
        using PdfDocument pdf = PdfDocument.Open(new WordConverter().ConvertToPdf(Text(face.FamilyName, "AV"), options));
        var letters = pdf.GetPage(1).Letters;
        Assert.Equal("AV", pdf.GetPage(1).Text);
        // Independently authored fontTools features: hmtx 1366+1366, UPEM 2048,
        // pair adjustment -160 or class adjustment -80, at 12 points.
        Assert.InRange(Math.Abs(expected - (letters[1].EndBaseLine.X - letters[0].StartBaseLine.X)), 0, 0.0001);
    }

    [Fact]
    public void GposExtensionPreservesPlacementAndBothGlyphAdvances()
    {
        using var stream = File.OpenRead(Path.Combine(TestDocuments.FixtureDirectory, "Enterprise", "gpos-extension.ttf"));
        FontSource face = Assert.Single(FontSource.FromStream(stream));
        var options = new ConversionOptions(); options.Fonts.Sources.Add(face);
        using PdfDocument pdf = PdfDocument.Open(new WordConverter().ConvertToPdf(Text(face.FamilyName, "AVA"), options));
        var letters = pdf.GetPage(1).Letters;
        Assert.Equal("AVA", pdf.GetPage(1).Text);
        Assert.Equal(48.05859375, letters[0].StartBaseLine.X, 4);
        Assert.Equal(55.154296875, letters[1].StartBaseLine.X, 4);
        Assert.Equal(62.89453125, letters[2].StartBaseLine.X, 4);
        Assert.Equal(-0.146484375, letters[1].StartBaseLine.Y - letters[0].StartBaseLine.Y, 4);
    }

    [Fact]
    public void CollectionCatalogReadsFamiliesStylesAndAppliesKerning()
    {
        using var collection = File.OpenRead(Path.Combine(TestDocuments.FixtureDirectory, "Enterprise", "caller-fonts.ttc"));
        var faces = FontSource.FromStream(collection);
        Assert.True(collection.CanRead); Assert.Equal(2, faces.Count);
        Assert.All(faces, face => Assert.Equal("Wordnet Fixture Sans", face.FamilyName));
        Assert.False(faces[0].Bold); Assert.True(faces[1].Bold);
        var options = new ConversionOptions(); foreach (FontSource face in faces) options.Fonts.Sources.Add(face);
        using PdfDocument pdf = PdfDocument.Open(new WordConverter().ConvertToPdf(Text("Wordnet Fixture Sans", "AV"), options));
        Assert.Equal("AV", pdf.GetPage(1).Text);
        var letters = pdf.GetPage(1).Letters;
        Assert.Equal(15.1171875, letters[letters.Count - 1].EndBaseLine.X - letters[0].StartBaseLine.X, 4);
    }

    [Fact]
    public void CollectionOffsetsAreValidatedBeforeReading()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "Enterprise", "caller-fonts.ttc"));
        bytes[12] = 0x7f; bytes[13] = 0xff; bytes[14] = 0xff; bytes[15] = 0xff;
        using var input = new MemoryStream(bytes);
        Assert.Throws<InvalidDataException>(() => FontSource.FromStream(input));
        Assert.True(input.CanRead);
    }

    [Theory]
    [InlineData("Arial", "Liberation Sans")]
    [InlineData("Times New Roman", "Liberation Serif")]
    [InlineData("Courier New", "Liberation Mono")]
    [InlineData("Calibri", "Carlito")]
    [InlineData("Cambria", "Caladea")]
    public void BundledFallbacksPreserveLatinTextAndReportFamilyMapping(string requested, string resolved)
    {
        byte[] input = Text(requested, Latin);
        using var source = new MemoryStream(input); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(source, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Metrics.FontsUsed, f => f.RequestedFamily == requested && f.ResolvedFamily == resolved);
        Assert.Equal(1, result.Metrics.SubstitutionCount);
        Assert.Single(result.Diagnostics, d => d.Code == "Font.Substituted");
        using PdfDocument pdf = PdfDocument.Open(output.ToArray());
        Assert.Equal(Latin, pdf.GetPage(1).Text);
        Assert.All(pdf.GetPage(1).Letters, letter => Assert.Contains(resolved.Replace(" ", ""), letter.FontName!.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CallerStreamFaceWinsWithoutClosingTheStream()
    {
        using Stream font = typeof(WordConverter).Assembly.GetManifestResourceStream("WordNet.Resources.Fonts.NotoSans-Regular.ttf")!;
        var source = new FontSource("Enterprise Brand", font);
        Assert.True(font.CanRead);
        var options = new ConversionOptions(); options.Fonts.Sources.Add(source);
        using var input = new MemoryStream(Text("Enterprise Brand", Latin)); using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken);
        Assert.Equal(0, result.Metrics.SubstitutionCount);
        Assert.Contains(result.Metrics.FontsUsed, f => f.RequestedFamily == "Enterprise Brand" && f.ResolvedFamily == "Enterprise Brand");
        using PdfDocument pdf = PdfDocument.Open(output.ToArray()); Assert.Equal(Latin, pdf.GetPage(1).Text);
    }

    [Theory]
    [InlineData("color-grid.png")]
    [InlineData("color-grid.jpg")]
    public void HtmlImagesAreInlineDataWithPhysicalDimensions(string image)
    {
        byte[] input = FidelityDocuments.Docx(image, FidelityDocuments.ImageParagraph());
        using var html = new HtmlParser().ParseDocument(Encoding.UTF8.GetString(new WordConverter().ConvertToHtml(input, new ConversionOptions(FidelityMode.BestEffort))));
        var picture = Assert.Single(html.QuerySelectorAll("img"));
        Assert.StartsWith("data:image/", picture.GetAttribute("src"), StringComparison.Ordinal);
        Assert.Equal("160", picture.GetAttribute("width")); Assert.Equal("80", picture.GetAttribute("height"));
        byte[] encoded = Convert.FromBase64String(picture.GetAttribute("src")!.Split(',')[1]);
        Assert.Equal(File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, image)), encoded);
    }

    private static byte[] Text(string family, string text) => TestDocuments.Docx(TestDocuments.Body("<w:p><w:r><w:rPr><w:rFonts w:ascii='" + family
        + "'/></w:rPr><w:t xml:space='preserve'>" + text + "</w:t></w:r></w:p>"));
}
