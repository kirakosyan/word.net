using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using WordNet.Internal;
using Xunit;

namespace WordNet.Tests;

public sealed class CompatibilityPolicyDataTests
{
    private const string Digest = "48cf686f2c60bce1018b9bd12303e4116e3ffa67926614b167a6ae10b05a3544";
    private const string Sample = "{\"schemaVersion\":\"1.0\",\"planDigest\":\"" + Digest
        + "\",\"requiredSemantics\":[\"content.text\"],\"allowedLosses\":[{\"code\":\"Docx.ThemeFont\",\"location\":{\"partId\":\"part-0001\",\"childIndexes\":[1,2,1]}}]}";
    private const string Canonical = "{\"allowedLosses\":[{\"code\":\"Docx.ThemeFont\",\"location\":{\"childIndexes\":[1,2,1],\"partId\":\"part-0001\"}}],\"planDigest\":\""
        + Digest + "\",\"requiredSemantics\":[\"content.text\"],\"schemaVersion\":\"1.0\"}";
    private static readonly int[] SampleIndices = { 1, 2, 1 };
    private static readonly int[] PairIndices = { 1, 2 };
    private static readonly string[] SortedParts = { "part-0001", "part-0001", "part-0002" };

    [Fact]
    public void DesignExampleHasStableIndependentCanonicalBytesAndImmutableLocations()
    {
        CompatibilityPolicyData policy = CompatibilityPolicyData.Parse(Sample, TestContext.Current.CancellationToken);
        Assert.Equal(Digest, policy.PlanDigest); Assert.Equal("content.text", Assert.Single(policy.RequiredSemantics));
        CompatibilityPolicyLoss loss = Assert.Single(policy.AllowedLosses);
        Assert.Equal("Docx.ThemeFont", loss.Code); Assert.Equal("part-0001", loss.PartId); Assert.Equal(SampleIndices, loss.ChildIndexes);
        Assert.Equal(Canonical, policy.CanonicalJson(TestContext.Current.CancellationToken));
        Assert.Equal("c9e44c64611ed28b1f141ea18ba9b1d3e41fea04bd632b4a527d419efae25fa0", policy.CanonicalSha256(TestContext.Current.CancellationToken));
        Assert.Equal(Canonical, CompatibilityPolicyData.Parse(Canonical, TestContext.Current.CancellationToken).CanonicalJson(TestContext.Current.CancellationToken));
        Assert.Throws<NotSupportedException>(() => ((IList<int>)loss.ChildIndexes)[0] = 9);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)policy.RequiredSemantics)[0] = "changed");
        Assert.Throws<NotSupportedException>(() => ((IList<CompatibilityPolicyLoss>)policy.AllowedLosses).Clear());
    }

    [Fact]
    public void UnorderedLocationsNormalizeAndDuplicateLocationsReject()
    {
        string three = "{\"schemaVersion\":\"1.0\",\"planDigest\":\"" + Digest + "\",\"requiredSemantics\":[],\"allowedLosses\":["
            + Loss("part-0002", "1") + "," + Loss("part-0001", "1.0,2") + "," + Loss("part-0001", "") + "]}";
        CompatibilityPolicyData policy = CompatibilityPolicyData.Parse(three, TestContext.Current.CancellationToken);
        Assert.Equal(SortedParts, policy.AllowedLosses.Select(item => item.PartId));
        Assert.Empty(policy.AllowedLosses[0].ChildIndexes); Assert.Equal(PairIndices, policy.AllowedLosses[1].ChildIndexes);
        Assert.Contains("\"childIndexes\":[1,2]", policy.CanonicalJson(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        string duplicate = "{\"schemaVersion\":\"1.0\",\"planDigest\":\"" + Digest + "\",\"requiredSemantics\":[],\"allowedLosses\":["
            + Loss("part-0001", "1,2") + "," + Loss("part-0001", "1.0,2") + "]}";
        Assert.Throws<InvalidDataException>(() => CompatibilityPolicyData.Parse(duplicate, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void UnknownCodesMalformedShapesAndDuplicateKeysCannotBecomePermissions()
    {
        string[] invalid =
        {
            Sample.Replace("\"schemaVersion\":\"1.0\"", "\"schemaVersion\":\"2.0\""),
            Sample.Replace("\"schemaVersion\":\"1.0\"", "\"schemaVersion\":\"1.0\",\"schemaVersion\":\"1.0\""),
            Sample.Replace("\"schemaVersion\":\"1.0\"", "\"schemaVersion\":\"1.0\",\"extra\":true"),
            Sample.Replace(Digest, "invalid"),
            Sample.Replace("content.text", "review.history"),
            Sample.Replace("Docx.ThemeFont", "Docx.Other"),
            Sample.Replace("part-0001", "word/document.xml"),
            Sample.Replace("[1,2,1]", "[1,2.5,1]"),
            Sample.Replace("[1,2,1]", "[0]"),
            Sample.Replace("[1,2,1]", "[2147483648]"),
            Sample.Replace("[\"content.text\"]", "[\"content.text\",\"content.text\"]"),
        };
        Assert.All(invalid, json => Assert.Throws<InvalidDataException>(() => CompatibilityPolicyData.Parse(json, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public void BoundsAndCancellationFailWithoutPublishingAPartialPolicy()
    {
        var oversized = Assert.Throws<InvalidDataException>(() => CompatibilityPolicyData.Parse(new string(' ', 1024 * 1024 + 1), TestContext.Current.CancellationToken));
        Assert.StartsWith("Limit.CompatibilityPolicy:", oversized.Message, StringComparison.Ordinal);
        string manyIndices = Sample.Replace("1,2,1", string.Join(",", Enumerable.Repeat("1", 65)));
        Assert.Throws<InvalidDataException>(() => CompatibilityPolicyData.Parse(manyIndices, TestContext.Current.CancellationToken));
        string manySemantics = Sample.Replace("\"content.text\"", string.Join(",", Enumerable.Repeat("\"content.text\"", 257)));
        Assert.Throws<InvalidDataException>(() => CompatibilityPolicyData.Parse(manySemantics, TestContext.Current.CancellationToken));
        string manyLosses = Sample.Replace(Loss("part-0001", "1,2,1"), string.Join(",", Enumerable.Repeat(Loss("part-0001", "1,2,1"), 4097)));
        Assert.Throws<InvalidDataException>(() => CompatibilityPolicyData.Parse(manyLosses, TestContext.Current.CancellationToken));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => CompatibilityPolicyData.Parse(Sample, cancellation.Token));
        CompatibilityPolicyData policy = CompatibilityPolicyData.Parse(Sample, TestContext.Current.CancellationToken);
        Assert.Throws<OperationCanceledException>(() => policy.CanonicalJson(cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => policy.CanonicalSha256(cancellation.Token));
    }

    private static string Loss(string part, string indices) => "{\"code\":\"Docx.ThemeFont\",\"location\":{\"partId\":\""
        + part + "\",\"childIndexes\":[" + indices + "]}}";
}
