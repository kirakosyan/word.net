using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using WordNet.Internal;
using WordNet.Layout;
using Xunit;

namespace WordNet.Tests;

public sealed class TypographyTests
{
    private const string Arabic = "مَرْحَبًا";
    private static ConversionOptions Options()
    {
        var options = new ConversionOptions(); options.Typography.Enabled = true; options.Pdf.Compress = false;
        options.Fonts.Sources.Add(FontSource.FromFile("Noto Sans Arabic", Path.Combine(TestDocuments.FixtureDirectory, "Interoperability", "NotoSansArabic-Regular.ttf"))); return options;
    }
    private static byte[] Source(string text, bool rtl = false) => TestDocuments.Docx(TestDocuments.Body("<w:p><w:pPr>" + (rtl ? "<w:bidi/>" : "") + "</w:pPr><w:r><w:rPr><w:sz w:val='60'/></w:rPr><w:t xml:space='preserve'>" + text + "</w:t></w:r></w:p>"));
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualArabicOutlinesAndLogicalWordMapping(bool rtl)
    {
        byte[] source = Source("Hello " + Arabic + " 123 world.", rtl); var options = Options();
        byte[] pdf = new WordConverter().ConvertToPdf(source, options); Save(rtl ? "arabic-rtl.pdf" : "arabic-ltr.pdf", pdf);
        string raw = Encoding.ASCII.GetString(pdf); Assert.Contains("/ActualText", raw, StringComparison.Ordinal); Assert.DoesNotContain("/Subtype /Image", raw, StringComparison.Ordinal);
        using var input = new MemoryStream(source); PagePreview page = Assert.Single(new WordConverter().RenderPages(input, DocumentFormat.Docx, options: options, cancellationToken: TestContext.Current.CancellationToken).Pages);
        XElement svg = XElement.Parse(Encoding.UTF8.GetString(page.GetBytes())); XNamespace ns = "http://www.w3.org/2000/svg";
        Assert.Contains(svg.Descendants(ns + "title"), t => t.Value.Trim() == Arabic); Assert.True(svg.Descendants(ns + "path").Count() > 20); Save(rtl ? "arabic-rtl.svg" : "arabic-ltr.svg", page.GetBytes());
    }
    [Theory]
    [InlineData("क", "Typography.Script")]
    [InlineData("\u0301", "Typography.Cluster")]
    [InlineData("😀", "Typography.Script")]
    [InlineData("abc\u202Edef", "Typography.Script")]
    public void UnsupportedScriptAndDirectionalControlsFailAtomically(string text, string code)
    {
        using var input = new MemoryStream(Source(text)); using var output = new MemoryStream(); output.WriteByte(42);
        var error = Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, Options(), TestContext.Current.CancellationToken)); Assert.StartsWith(code, error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, output.ToArray());
    }
    [Fact]
    public void MissingFontAndGlyphWorkBoundsAreExplicit()
    {
        var missing = new ConversionOptions(); missing.Typography.Enabled = true;
        Assert.Contains("Typography.Font", Assert.Throws<NotSupportedException>(() => new WordConverter().ConvertToPdf(Source(Arabic), missing)).Message, StringComparison.Ordinal);
        var archival = Options(); archival.Pdf.Conformance = PdfConformance.PdfA2b;
        Assert.Contains("Typography.Conformance", Assert.Throws<NotSupportedException>(() => new WordConverter().ConvertToPdf(Source(Arabic), archival)).Message, StringComparison.Ordinal);
        var bounded = Options(); bounded.Typography.MaximumGlyphs = 2;
        Assert.Contains("Limit.TypographyGlyphs", Assert.Throws<InvalidDataException>(() => new WordConverter().ConvertToPdf(Source(Arabic), bounded)).Message, StringComparison.Ordinal);
    }
    [Fact]
    public async Task ConcurrentShapingOwnsNativeStateAndCancellationIsObserved()
    {
        var options = Options(); byte[] source = Source(Arabic); byte[][] values = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => new WordConverter().ConvertToPdf(source, options), TestContext.Current.CancellationToken)));
        Assert.All(values, value => Assert.True(value.Length > 10000));
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); using var input = new MemoryStream(source); using var output = new MemoryStream(); Assert.Throws<OperationCanceledException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, options, cancel.Token)); Assert.Equal(0, output.Length);
    }
    [Fact]
    public void GlyphsClustersAdvancesAndMarkOffsetsMatchIndependentHarfBuzzGolden()
    {
        var context = new ConversionContext(Options(), TestContext.Current.CancellationToken) { FontData = new[] { File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "Interoperability", "NotoSansArabic-Regular.ttf")) } };
        using TypographySession shaper = NativeTypography.Open(context)!; AssertArabicGolden(shaper.Shape(Arabic, 0, true));
    }
    [Fact]
    public void NativeFontStorageSurvivesCompactingCollectionsAndAllocationPressure()
    {
        var context = new ConversionContext(Options(), TestContext.Current.CancellationToken) { FontData = new[] { File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "Interoperability", "NotoSansArabic-Regular.ttf")) } };
        using TypographySession shaper = NativeTypography.Open(context)!;
        // Register the native face, then shape previously unused Arabic glyphs after
        // collections/reuse of temporary large arrays. Cached first-word glyphs alone
        // would hide a dangling font-table pointer.
        _ = shaper.Shape("ب", 0, true);
        for (int iteration = 0; iteration < 4; iteration++)
        {
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            var pressure = new byte[16][];
            for (int i = 0; i < pressure.Length; i++) { pressure[i] = new byte[context.FontData[0].Length]; for (int j = 0; j < pressure[i].Length; j++) pressure[i][j] = 0xA5; }
            AssertArabicGolden(shaper.Shape(Arabic, 0, true));
            GC.KeepAlive(pressure);
        }
    }
    private static void AssertArabicGolden(ShapedWord shaped)
    {
        // uharfbuzz 0.53.0 / HarfBuzz 12.1.0; font SHA256 in fixture provenance.
        int[][] golden = { new[] { 47, 8, 291, 0, 0 }, new[] { 299, 6, 0, -7, -174 }, new[] { 104, 6, 343, 0, 0 }, new[] { 291, 4, 0, 4, -106 }, new[] { 417, 4, 549, 0, 0 }, new[] { 1217, 2, 0, 35, -107 }, new[] { 979, 2, 396, 0, 0 }, new[] { 291, 0, 0, 142, -100 }, new[] { 771, 0, 525, 0, 0 } };
        Assert.Equal(1000, shaped.Units); Assert.Equal(golden.Length, shaped.Glyphs.Length);
        for (int i = 0; i < golden.Length; i++) { ShapedGlyph g = shaped.Glyphs[i]; Assert.Equal(golden[i][0], g.Glyph); Assert.Equal(golden[i][1], g.Cluster); Assert.Equal(golden[i][2], g.Advance); Assert.Equal(golden[i][3], g.X); Assert.Equal(golden[i][4], g.Y); }
        Assert.Equal(2.104, shaped.Width, 6);
    }
    [Fact]
    public void ArabicDotRetainsNegativeSignedDeltasAndImpliedHalfPoints()
    {
        var context = new ConversionContext(Options(), TestContext.Current.CancellationToken);
        var decoder = new TrueTypeOutlines(File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "Interoperability", "NotoSansArabic-Regular.ttf")), context);
        var paths = decoder.Paths(104); Assert.Equal(2, paths.Count);
        Assert.Equal(-191, Assert.Single(paths[1].Commands.OfType<UglyToad.PdfPig.Core.PdfSubpath.Move>()).Location.Y);
        Assert.Contains(paths[1].Commands.OfType<UglyToad.PdfPig.Core.PdfSubpath.QuadraticBezierCurve>(), q => q.EndPoint.Y == -176.5);
    }
    [Fact]
    public void CyclicCompositeFontIsRejectedBeforePathExpansion()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "Interoperability", "NotoSansArabic-Regular.ttf"));
        int U16(int p) => bytes[p] * 256 + bytes[p + 1]; int U32(int p) => (bytes[p] << 24) | (bytes[p + 1] << 16) | (bytes[p + 2] << 8) | bytes[p + 3];
        int Table(string name) { for (int i = 0; i < U16(4); i++) { int p = 12 + i * 16; if (Encoding.ASCII.GetString(bytes, p, 4) == name) return U32(p + 8); } throw new InvalidDataException(); }
        int loca = Table("loca"), glyf = Table("glyf"), count = U16(Table("maxp") + 4); bool wide = U16(Table("head") + 50) == 1; bool mutated = false;
        for (int i = 0; i < count; i++) { int offset = wide ? U32(loca + i * 4) : U16(loca + i * 2) * 2; int end = wide ? U32(loca + (i + 1) * 4) : U16(loca + (i + 1) * 2) * 2; int p = glyf + offset; if (end > offset && U16(p) == 65535) { bytes[p + 12] = (byte)(i >> 8); bytes[p + 13] = (byte)i; mutated = true; break; } }
        Assert.True(mutated); Assert.Contains("Font.Outline", Assert.Throws<InvalidDataException>(() => TrueTypeOutlineGuard.Validate(bytes)).Message, StringComparison.Ordinal);
    }
    [Fact]
    public void NativeWrapKeepsWholeWordsAndStyledJoinsAreExplicit()
    {
        byte[] source = TestDocuments.Docx(TestDocuments.Body(TestDocuments.TextParagraph(string.Join(" ", Enumerable.Repeat(Arabic, 30)))));
        using var input = new MemoryStream(source); var preview = new WordConverter().RenderPages(input, DocumentFormat.Docx, options: Options(), cancellationToken: TestContext.Current.CancellationToken);
        XElement svg = XElement.Parse(Encoding.UTF8.GetString(preview.Pages[0].GetBytes())); XNamespace ns = "http://www.w3.org/2000/svg";
        Assert.Equal(30, svg.Descendants(ns + "title").Count(t => t.Value == Arabic));
        byte[] styled = TestDocuments.Docx(TestDocuments.Body("<w:p><w:r><w:t>مر</w:t></w:r><w:r><w:rPr><w:i/></w:rPr><w:t>حبا</w:t></w:r></w:p>"));
        Assert.Contains("Typography.StyledJoining", Assert.Throws<NotSupportedException>(() => new WordConverter().ConvertToPdf(styled, Options())).Message, StringComparison.Ordinal);
    }
    internal static void Save(string name, byte[] bytes) { string? directory = Environment.GetEnvironmentVariable("WORDNET_INTEROP_ARTIFACTS"); if (directory != null) { Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, name), bytes); } }
}
