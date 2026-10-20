using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using WordNet.Internal;
using Xunit;

namespace WordNet.Tests;

public sealed class BidiConformanceTests
{
    [Fact]
    public void Unicode17AbstractCasesMatchAllSpecifiedDirectionalCombinations()
    {
        VerifyHash("BidiTest.txt", "888bdfc8090652272d1f859cdb00ae659e2dc6c26740be61ef1d03998a687620");
        var examples = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["L"] = 0x61,
            ["R"] = 0x5d0,
            ["AL"] = 0x627,
            ["EN"] = 0x31,
            ["ES"] = 0x2b,
            ["ET"] = 0x24,
            ["AN"] = 0x661,
            ["CS"] = 0x2c,
            ["NSM"] = 0x300,
            ["BN"] = 0xad,
            ["B"] = 0x2029,
            ["S"] = 9,
            ["WS"] = 32,
            ["ON"] = 0x21,
            ["LRE"] = 0x202a,
            ["RLE"] = 0x202b,
            ["PDF"] = 0x202c,
            ["LRO"] = 0x202d,
            ["RLO"] = 0x202e,
            ["LRI"] = 0x2066,
            ["RLI"] = 0x2067,
            ["FSI"] = 0x2068,
            ["PDI"] = 0x2069
        };
        int[] levels = Array.Empty<int>(), order = Array.Empty<int>(); int count = 0, failures = 0; var errors = new List<string>();
        var context = new ConversionContext(new ConversionOptions(), TestContext.Current.CancellationToken);
        foreach (string raw in File.ReadLines(Path.Combine(TestDocuments.FixtureDirectory, "Unicode17", "BidiTest.txt")))
        {
            string line = raw.Split('#')[0].Trim(); if (line.Length == 0) continue;
            if (line.StartsWith("@Levels:", StringComparison.Ordinal)) { levels = line.Substring(8).Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(s => s == "x" ? -1 : int.Parse(s, CultureInfo.InvariantCulture)).ToArray(); continue; }
            if (line.StartsWith("@Reorder:", StringComparison.Ordinal)) { order = line.Substring(9).Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray(); continue; }
            if (line[0] == '@') continue; string[] fields = line.Split(';'); int mask = int.Parse(fields[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            string text = string.Concat(fields[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(s => char.ConvertFromUtf32(examples[s])));
            for (int bit = 0; bit < 3; bit++) if ((mask & (1 << bit)) != 0)
            {
                UnicodeBidi.Result result = UnicodeBidi.Resolve(text, bit == 0 ? 2 : bit == 1 ? 0 : 1, context, false, 125); count++;
                if (!levels.SequenceEqual(result.Levels) || !order.SequenceEqual(result.Order))
                { failures++; if (errors.Count < 5) errors.Add(fields[0] + " bit=" + bit + " expected=" + string.Join(",", levels) + " actual=" + string.Join(",", result.Levels)); }
            }
        }
        Assert.Equal(770241, count); Assert.True(failures == 0, failures + " abstract bidi failures: " + string.Join(" | ", errors));
    }
    private static void VerifyHash(string name, string expected)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "Unicode17", name));
#if NET10_0_OR_GREATER
        string actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
#elif NET8_0_OR_GREATER
        string actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
#else
        using var hash = SHA256.Create();
        string actual = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
#endif
        Assert.Equal(expected, actual);
    }
    [Fact]
    public void Unicode17CharacterCasesMatchOfficialLevelsBaseAndReordering()
    {
        VerifyHash("BidiCharacterTest.txt", "a3e6e905ab5afbe318a96df5401d0372a04cd73ef139ab5e3cf0ae241c255488");
        var errors = new List<string>(); int count = 0, failures = 0;
        var context = new ConversionContext(new ConversionOptions(), TestContext.Current.CancellationToken);
        foreach (string raw in File.ReadLines(Path.Combine(TestDocuments.FixtureDirectory, "Unicode17", "BidiCharacterTest.txt")))
        {
            string line = raw.Split('#')[0].Trim(); if (line.Length == 0) continue; string[] fields = line.Split(';');
            string text = string.Concat(fields[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(s => char.ConvertFromUtf32(int.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture))));
            int direction = int.Parse(fields[1], CultureInfo.InvariantCulture), basis = int.Parse(fields[2], CultureInfo.InvariantCulture);
            int[] levels = fields[3].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(s => s == "x" ? -1 : int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            int[] order = fields[4].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            UnicodeBidi.Result result = UnicodeBidi.Resolve(text, direction, context, false, 125); count++;
            if (result.BaseLevel != basis || !levels.SequenceEqual(result.Levels) || !order.SequenceEqual(result.Order))
            {
                failures++; if (errors.Count < 5) errors.Add(fields[0] + " direction=" + direction + " expected=" + string.Join(",", levels) + " actual=" + string.Join(",", result.Levels));
            }
        }
        Assert.Equal(91707, count);
        Assert.True(failures == 0, failures + " official bidi failures: " + string.Join(" | ", errors));
    }
}
