using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Layout;
using Xunit;

namespace WordNet.Tests;

public sealed class ExtendedTypographyTests
{
    private static string Fixture(string name) => Path.Combine(TestDocuments.FixtureDirectory, "P2", name);
    internal static ConversionOptions Options(bool devanagari = true, FidelityMode mode = FidelityMode.Strict)
    {
        var options = new ConversionOptions(mode); options.Typography.Enabled = true; options.Typography.ExtendedBidi = true; options.Typography.Devanagari = devanagari;
        options.Fonts.Sources.Add(FontSource.FromFile("Noto Sans Devanagari", Fixture("NotoSansDevanagari-Regular.ttf")));
        options.Fonts.Sources.Add(FontSource.FromFile("Noto Sans Arabic", Path.Combine(TestDocuments.FixtureDirectory, "Interoperability", "NotoSansArabic-Regular.ttf")));
        return options;
    }
    internal static byte[] Source(string text) => TestDocuments.Docx(TestDocuments.Body(TestDocuments.TextParagraph(text)));
    [Fact]
    public void HindiGlyphsConjunctClustersAdvancesAndMarkOffsetsMatchIndependentHarfBuzz()
    {
        using JsonDocument reference = JsonDocument.Parse(File.ReadAllBytes(Fixture("devanagari-reference.json")));
        var context = new ConversionContext(Options(), TestContext.Current.CancellationToken) { FontData = new[] { File.ReadAllBytes(Fixture("NotoSansDevanagari-Regular.ttf")) } };
        using TypographySession shaper = NativeTypography.Open(context)!;
        foreach (JsonElement test in reference.RootElement.GetProperty("cases").EnumerateArray())
        {
            ShapedWord shaped = shaper.Shape(test.GetProperty("text").GetString()!, 0, false); Assert.Equal(1000, shaped.Units);
            var expected = test.GetProperty("glyphs").EnumerateArray().ToArray(); Assert.Equal(expected.Length, shaped.Glyphs.Length);
            for (int i = 0; i < expected.Length; i++)
            { ShapedGlyph actual = shaped.Glyphs[i]; Assert.Equal(expected[i].EnumerateArray().Select(v => (double)v.GetInt32()), new[] { actual.Glyph, actual.Cluster, actual.Advance, actual.X, actual.Y }); }
        }
    }
    [Fact]
    public void IsolatesBracketsAndNumbersMatchIndependentFriBidiAndRenderWithoutControlOutlines()
    {
        using JsonDocument reference = JsonDocument.Parse(File.ReadAllBytes(Fixture("bidi-reference.json")));
        foreach (JsonElement test in reference.RootElement.GetProperty("cases").EnumerateArray())
        {
            string text = test.GetProperty("text").GetString()!; int basis = test.GetProperty("base").GetInt32(); var options = Options(false);
            options.Typography.Direction = basis == 1 ? TextDirection.RightToLeft : TextDirection.LeftToRight;
            var c = new ConversionContext(options, TestContext.Current.CancellationToken);
            UnicodeBidi.Result result = UnicodeBidi.Resolve(text, basis, c); int[] expected = test.GetProperty("levels").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            for (int i = 0; i < expected.Length; i++) if (result.Levels[i] >= 0) Assert.Equal(expected[i], result.Levels[i]);
            using var stream = new MemoryStream(Source(text)); var model = DocxReader.Read(stream, c); var page = Assert.Single(FixedLayout.Create(model, c));
            Assert.All(page.Text, t => Assert.NotNull(t.NativeShape)); Assert.Equal(text, string.Concat(page.Text.Select(t => t.Text)));
            Assert.All(page.Text.Where(t => t.Text.All(ch => UnicodeBidi.Format(ch))), t => Assert.Equal(0, t.NativeShape!.Width));
            int[] visual = test.GetProperty("order").EnumerateArray().Select(v => v.GetInt32()).ToArray(); var ranks = new int[text.Length];
            for (int i = 0; i < visual.Length; i++) ranks[visual[i]] = i;
            var tokens = new List<(int Logical, int Visual, double X)>(); int offset = 0;
            foreach (PositionedText item in page.Text)
            {
                if (item.NativeShape!.Width > 0) tokens.Add((offset, Enumerable.Range(offset, item.Text.Length).Min(i => ranks[i]), item.X));
                offset += item.Text.Length;
            }
            Assert.Equal(tokens.OrderBy(t => t.Visual).Select(t => t.Logical), tokens.OrderBy(t => t.X).Select(t => t.Logical));
            double previous = double.NegativeInfinity;
            foreach (PositionedText item in page.Text.Where(t => t.NativeShape!.Width > 0).OrderBy(t => t.X)) { Assert.True(item.X >= previous); previous = item.X + item.NativeShape!.Width * item.Size - 0.001; }
        }
    }
    [Fact]
    public void DirectionalDepthAndParagraphWorkLimitsRejectOnePastTheBound()
    {
        var options = Options(false); options.Typography.MaximumBidiDepth = 2;
        _ = new WordConverter().ConvertToPdf(Source("\u2066\u2066a\u2069\u2069"), options);
        var deep = Assert.Throws<NotSupportedException>(() => new WordConverter().ConvertToPdf(Source("\u2066\u2066\u2066a\u2069\u2069\u2069"), options));
        Assert.StartsWith("Typography.BidiDepth", deep.Message, StringComparison.Ordinal);
        var longParagraph = Assert.Throws<InvalidDataException>(() => new WordConverter().ConvertToPdf(Source(new string('a', 131073)), options));
        Assert.StartsWith("Typography.BidiBudget", longParagraph.Message, StringComparison.Ordinal);
    }
    [Fact]
    public void HindiParagraphsKeepLogicalTextAndConjunctsAcrossFallbackAndWrapping()
    {
        const string text = "Hindi हिन्दी कर्म क्षि र्कि क्‍ष क्‌ष ज़िंदा end";
        var options = Options(); byte[] source = Source(text); byte[] pdf = new WordConverter().ConvertToPdf(source, options);
        using var input = new MemoryStream(pdf);
        var imported = PdfImportBridge.Read(input, new ConversionContext(new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken));
        Assert.Contains(text, string.Join(" ", imported.BodyParagraphs.Select(p => p.Text)), StringComparison.Ordinal);
        using var document = new MemoryStream(Source(string.Join(" ", Enumerable.Repeat("हिन्दी क्षि", 40))));
        var c = new ConversionContext(options, TestContext.Current.CancellationToken); var pages = FixedLayout.Create(DocxReader.Read(document, c), c);
        Assert.Equal(40, pages.SelectMany(p => p.Text).Count(t => t.Text == "क्षि"));
        Assert.DoesNotContain(pages.SelectMany(p => p.Text), t => t.Text == "ि");
        string? folder = Environment.GetEnvironmentVariable("WORDNET_P2_EVIDENCE");
        if (folder != null) { Directory.CreateDirectory(folder); File.WriteAllBytes(Path.Combine(folder, "hindi.pdf"), pdf); File.WriteAllBytes(Path.Combine(folder, "hindi.docx"), source); }
    }
    [Theory]
    [InlineData("abc\u2067مرحبا", "Typography.BidiControls")]
    [InlineData("abc\u202c", "Typography.BidiControls")]
    [InlineData("বাংলা", "Typography.Script")]
    [InlineData("\u093f", "Typography.Cluster")]
    public void MalformedControlsAndUnsupportedScriptsLeaveOutputUntouched(string text, string diagnostic)
    {
        using var input = new MemoryStream(Source(text)); using var output = new MemoryStream(new byte[] { 7, 8, 9 }, true);
        var error = Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, Options(), TestContext.Current.CancellationToken));
        Assert.StartsWith(diagnostic, error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 7, 8, 9 }, output.ToArray());
    }
}
