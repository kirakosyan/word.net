using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed class EnterprisePaginationTests
{
    [Fact]
    public void AuthoredBusinessCorpusMeetsRecordedWordPageCountTolerance()
    {
        string root = Path.Combine(TestDocuments.FixtureDirectory, "Enterprise");
        int cases = 0, exact = 0;
        var converter = new WordConverter();
        foreach (string row in File.ReadLines(Path.Combine(root, "word-pages.csv")).Skip(1).Where(s => s.Length > 0))
        {
            // Generated filenames/category/count have no commas. Font metadata can;
            // it is deliberately ignored and the final column is always the SHA-256.
            string[] cells = row.Split(',');
            string name = cells[0].Trim('"'), category = cells[1].Trim('"');
            if (category != "business") continue;
            byte[] input = File.ReadAllBytes(Path.Combine(root, name));
#if NET9_0_OR_GREATER
            string hash = Convert.ToHexStringLower(SHA256.HashData(input));
#elif NET8_0_OR_GREATER
            string hash = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
#else
            using var sha = SHA256.Create();
            string hash = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
#endif
            Assert.Equal(cells[cells.Length - 1].Trim('"'), hash);
            int expected = int.Parse(cells[2].Trim('"'), CultureInfo.InvariantCulture);
            using PdfDocument pdf = PdfDocument.Open(converter.ConvertToPdf(input, new ConversionOptions(FidelityMode.Lenient)
            { Clock = () => new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero) }));
            cases++; if (expected == pdf.NumberOfPages) exact++;
            Assert.True(Math.Abs(expected - pdf.NumberOfPages) <= 1, name + ": Word=" + expected + ", wordnet=" + pdf.NumberOfPages);
        }
        Assert.True(cases >= 30, "At least 30 independently paginated business inputs are required.");
        TestContext.Current.TestOutputHelper?.WriteLine("Exact Word page-count matches: " + exact + "/" + cases);
        Assert.True(exact >= Math.Ceiling(cases * 0.9), "Exact page-count matches: " + exact + "/" + cases);
    }
}
