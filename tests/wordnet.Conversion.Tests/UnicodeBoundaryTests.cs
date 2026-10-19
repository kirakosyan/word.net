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

public sealed class UnicodeBoundaryTests
{
    [Theory]
    [InlineData(false, "GraphemeBreakTest.txt")]
    [InlineData(true, "WordBreakTest.txt")]
    public void Unicode17OfficialConformance(bool words, string filename)
    {
        int cases = 0;
        string path = Path.Combine(TestDocuments.FixtureDirectory, "Unicode17", filename);
        {
#if NET10_0_OR_GREATER
            string actual = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
#elif NET8_0_OR_GREATER
            string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
#else
            using var hash = SHA256.Create();
            string actual = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
#endif
            Assert.Equal(words ? "1de23a75f37904abc7d206239ee8d34f8fdf0fb4ab32a7174dfbabbde25419b2" : "e2d134d2c52919bace503ebb6a551c1855fe1a1faec18478c78fff254a1793ec", actual);
        }
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Split('#')[0].Trim(); if (line.Length == 0) continue;
            var text = new StringBuilder(); var expected = new List<int>();
            foreach (string token in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                if (token == "÷") expected.Add(text.Length);
                else if (token != "×") text.Append(char.ConvertFromUtf32(int.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
            var context = new ConversionContext(new ConversionOptions(), TestContext.Current.CancellationToken);
            List<int> actual = UnicodeBoundaries.Find(text.ToString(), words, context);
            Assert.True(expected.SequenceEqual(actual), filename + ": " + raw + " actual=" + string.Join(",", actual)); cases++;
        }
        Assert.Equal(words ? 1944 : 766, cases);
    }
    [Fact]
    public void MalformedTextTokenLimitsAndCancellationFailExplicitly()
    {
        var context = new ConversionContext(new ConversionOptions(), TestContext.Current.CancellationToken);
        Assert.Throws<ArgumentException>(() => UnicodeBoundaries.Find("\ud800", false, context));
        Assert.Throws<InvalidDataException>(() => UnicodeBoundaries.Find("abc", false, context, 2));
        Assert.Equal(4, UnicodeBoundaries.Find("abc", false, context, 3).Count);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => UnicodeBoundaries.Find("text", true, new ConversionContext(new ConversionOptions(), cancellation.Token)));
    }
}
