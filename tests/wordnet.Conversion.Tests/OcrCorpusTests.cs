using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;

namespace WordNet.Tests;

public sealed class OcrCorpusTests
{
    [Theory]
    [Trait("Category", "Ocr")]
    [InlineData("clean", DocumentFormat.Docx)]
    [InlineData("rotated90", DocumentFormat.Docx)]
    [InlineData("rotated180", DocumentFormat.Docx)]
    [InlineData("skewed", DocumentFormat.Docx)]
    [InlineData("noisy", DocumentFormat.Docx)]
    [InlineData("low-resolution", DocumentFormat.Docx)]
    [InlineData("columns", DocumentFormat.Docx)]
    [InlineData("phototest", DocumentFormat.Docx)]
    [InlineData("clean", DocumentFormat.Doc)]
    [InlineData("rotated90", DocumentFormat.Doc)]
    [InlineData("rotated180", DocumentFormat.Doc)]
    [InlineData("skewed", DocumentFormat.Doc)]
    [InlineData("noisy", DocumentFormat.Doc)]
    [InlineData("low-resolution", DocumentFormat.Doc)]
    [InlineData("columns", DocumentFormat.Doc)]
    [InlineData("phototest", DocumentFormat.Doc)]
    public void DegradedEnglishScansMeetDeclaredAccuracyAndReadingOrder(string scenario, DocumentFormat target)
    {
        var options = new ConversionOptions(FidelityMode.BestEffort, englishOcr: Options(correctOrientation: scenario != "columns", readTextColumns: scenario == "columns", dpi: scenario == "phototest" ? 300 : 200));
        using var input = new MemoryStream(ConversionAcceptanceTests.Fixture("ocr-" + scenario + ".pdf"));
        using var output = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, output, DocumentFormat.Pdf, target, options, TestContext.Current.CancellationToken);
        string actual = ConversionAcceptanceTests.Normalize(ConversionAcceptanceTests.ReadOutput(output.ToArray(), target));
        string expected = ConversionAcceptanceTests.Normalize(File.ReadAllText(Path.Combine(TestDocuments.FixtureDirectory, "ocr-" + scenario + ".txt")));
        double characters = (double)Distance(expected.Select(c => c.ToString()).ToArray(), actual.Select(c => c.ToString()).ToArray()) / expected.Length;
        string[] expectedWords = expected.Split(' '), actualWords = actual.Split(' ');
        double words = (double)Distance(expectedWords, actualWords) / expectedWords.Length;
        TestContext.Current.TestOutputHelper?.WriteLine(string.Format(CultureInfo.InvariantCulture, "OCR_METRIC scenario={0} target={1} CER={2:0.00000} WER={3:0.00000}", scenario, target, characters, words));
        Assert.True(characters <= 0.03 && words <= 0.08, string.Format(CultureInfo.InvariantCulture, "{0}: CER {1:P2}, WER {2:P2}. Text: {3}", scenario, characters, words, actual));
        Assert.Contains(result.Diagnostics, d => d.Code == "Ocr.English");
        if (scenario.StartsWith("rotated", StringComparison.Ordinal)) Assert.Contains(result.Diagnostics, d => d.Code == "Ocr.Orientation");
        if (scenario == "columns")
        {
            Assert.Contains(result.Diagnostics, d => d.Code == "Ocr.Columns");
            Assert.True(actual.IndexOf("Left column final", StringComparison.Ordinal) < actual.IndexOf("Right column first", StringComparison.Ordinal));
        }
    }

    private static EnglishOcrOptions Options(bool correctOrientation, bool readTextColumns, int dpi)
    {
        string? data = Environment.GetEnvironmentVariable("WORDNET_TESSDATA"), native = Environment.GetEnvironmentVariable("WORDNET_TESSERACT_LIBRARY");
        Assert.False(string.IsNullOrEmpty(data) || string.IsNullOrEmpty(native), "Run eng/prepare-ocr-tests.ps1 in the process that launches the tests.");
        return new EnglishOcrOptions(data!, native!, correctOrientation, readTextColumns, resolutionDpi: dpi);
    }

    private static int Distance(string[] expected, string[] actual)
    {
        int[] previous = Enumerable.Range(0, actual.Length + 1).ToArray(), current = new int[actual.Length + 1];
        for (int i = 1; i <= expected.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= actual.Length; j++)
                current[j] = Math.Min(previous[j] + 1, Math.Min(current[j - 1] + 1, previous[j - 1] + (expected[i - 1] == actual[j - 1] ? 0 : 1)));
            int[] swap = previous; previous = current; current = swap;
        }
        return previous[actual.Length];
    }
}
