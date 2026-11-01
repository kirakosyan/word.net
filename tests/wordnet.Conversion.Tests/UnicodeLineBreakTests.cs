using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using WordNet.Internal;
using Xunit;

namespace WordNet.Tests;

public sealed class UnicodeLineBreakTests
{
    private static readonly int[] SampleBreaks = { 2, 4, 5 };

    [Fact]
    public void Unicode17OfficialLineBreakConformance()
    {
        string path = Path.Combine(TestDocuments.FixtureDirectory, "Unicode17", "LineBreakTest.txt");
#if NET10_0_OR_GREATER
        string actualHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
#elif NET8_0_OR_GREATER
        string actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
#else
        using var hash = SHA256.Create();
        string actualHash = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
#endif
        Assert.Equal("e69884e0dde6a8724873f885d68c52dc14518abf9ae4ca9e2283b8773db3b752", actualHash);
        int cases = 0, failures = 0; var examples = new StringBuilder();
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Split('#')[0].Trim(); if (line.Length == 0) continue;
            var text = new StringBuilder(); var expected = new List<int>();
            foreach (string token in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                if (token == "÷") expected.Add(text.Length);
                else if (token != "×") text.Append(char.ConvertFromUtf32(int.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
            var context = new ConversionContext(new ConversionOptions(), TestContext.Current.CancellationToken);
            List<int> actual = UnicodeLineBreak.Find(text.ToString(), context);
            if (!expected.SequenceEqual(actual))
            {
                failures++;
                if (failures <= 20) examples.AppendLine(raw + " actual=" + string.Join(",", actual));
            }
            cases++;
        }
        Assert.Equal(19338, cases);
        Assert.True(failures == 0, "Failed " + failures + " Unicode 17 line-break cases:\n" + examples);
    }

    [Fact]
    public void LineBreakRejectsMalformedUtf16LimitsAndCancellation()
    {
        var context = new ConversionContext(new ConversionOptions(), TestContext.Current.CancellationToken);
        Assert.Throws<ArgumentException>(() => UnicodeLineBreak.Find("\ud800", context));
        Assert.Throws<InvalidDataException>(() => UnicodeLineBreak.Find("a b c", context, 2));
        Assert.Equal(SampleBreaks, UnicodeLineBreak.Find("a b c", context, 3));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => UnicodeLineBreak.Find("text", new ConversionContext(new ConversionOptions(), cancel.Token)));
    }
}
