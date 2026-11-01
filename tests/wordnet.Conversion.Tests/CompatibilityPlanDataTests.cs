using System;
using System.IO;
using System.Threading;
using WordNet.Internal;
using Xunit;

namespace WordNet.Tests;

public sealed class CompatibilityPlanDataTests
{
    private const string Digest = "48cf686f2c60bce1018b9bd12303e4116e3ffa67926614b167a6ae10b05a3544";
    private const string Requirement = "{\"pathId\":\"path-0001\",\"type\":\"string\",\"required\":true,\"owner\":\"body\",\"support\":\"supported\"}";
    private const string Semantic = "{\"code\":\"content.text\",\"assessment\":\"supported\"}";
    private const string Finding = "{\"code\":\"Docx.ThemeFont\",\"phase\":\"parse\",\"evidence\":\"observed\",\"severity\":\"warning\",\"count\":1,\"locationsTruncated\":false,\"locations\":[{\"partId\":\"part-0001\",\"childIndexes\":[1,2,1]}],\"remediation\":\"Remediation.SetExplicitFont\"}";
    private static readonly string Plan = "{\"schemaVersion\":\"1.0\",\"identities\":{\"input\":\"" + new string('1', 64)
        + "\",\"options\":\"" + new string('2', 64) + "\",\"dataSchema\":\"" + new string('3', 64)
        + "\",\"fonts\":\"" + new string('4', 64) + "\",\"providers\":\"" + new string('5', 64)
        + "\",\"engine\":\"" + new string('6', 64) + "\"},\"sourceFormat\":\"docx\",\"destinationFormat\":\"pdf\","
        + "\"stages\":{\"parse\":\"performed\",\"destination\":\"performed\",\"layout\":\"not-performed\",\"write\":\"not-performed\",\"validation\":\"not-performed\"},"
        + "\"requirements\":[" + Requirement + "],\"semantics\":[" + Semantic + "],\"findings\":[" + Finding + "],\"observedInputBytes\":1024,\"estimatedPages\":null}";
    private const string Policy = "{\"schemaVersion\":\"1.0\",\"planDigest\":\"" + Digest
        + "\",\"requiredSemantics\":[\"content.text\"],\"allowedLosses\":[{\"code\":\"Docx.ThemeFont\",\"location\":{\"partId\":\"part-0001\",\"childIndexes\":[1,2,1]}}]}";

    [Fact]
    public void IndependentDesignExampleIsAdmittedOnlyByTheExactNonStrictPolicy()
    {
        CompatibilityPlanData plan = CompatibilityPlanData.Parse(Plan, TestContext.Current.CancellationToken);
        CompatibilityPolicyData policy = CompatibilityPolicyData.Parse(Policy, TestContext.Current.CancellationToken);
        Assert.Equal(Digest, plan.CanonicalSha256(TestContext.Current.CancellationToken));
        Assert.Equal("Policy.Admitted", plan.CheckAdmission(policy, false, TestContext.Current.CancellationToken));
        Assert.Equal("Policy.StrictLoss", plan.CheckAdmission(policy, true, TestContext.Current.CancellationToken));
        CompatibilityPlanData changed = CompatibilityPlanData.Parse(Plan.Replace(new string('1', 64), new string('a', 64)), TestContext.Current.CancellationToken);
        Assert.Equal("Policy.StalePlan", changed.CheckAdmission(policy, false, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void UnknownRequirementsSemanticsAndFindingsCannotBeAdmitted()
    {
        Check(Plan.Replace("\"type\":\"string\"", "\"type\":\"unknown\""), "Policy.PathUnknown");
        Check(Plan.Replace("\"required\":true,\"owner\":\"body\"", "\"required\":false,\"owner\":\"unknown\""), "Policy.PathUnknown");
        Check(Plan.Replace("\"assessment\":\"supported\"", "\"assessment\":\"unknown\""), "Policy.SemanticUnknown");
        Check(Plan.Replace("\"semantics\":[" + Semantic + "]", "\"semantics\":[]"), "Policy.RequiredSemanticUnknown");
        Check(Plan.Replace("\"evidence\":\"observed\"", "\"evidence\":\"unknown\""), "Policy.UnknownFinding");
        Check(Plan.Replace("\"severity\":\"warning\"", "\"severity\":\"info\""), "Policy.UnknownFinding");
        string unknown = "{\"code\":\"Plan.UninspectedCoverage\",\"phase\":\"parse\",\"evidence\":\"unknown\",\"severity\":\"info\",\"count\":1,\"locationsTruncated\":false,\"locations\":[],\"remediation\":\"Remediation.ReviewConversion\"}";
        Check(Plan.Replace("\"findings\":[" + Finding + "]", "\"findings\":[" + Finding + "," + unknown + "]"), "Policy.UnknownFinding");
    }

    [Fact]
    public void TruncatedUnlocatedOrUnmatchedLossesAndPermissionsReject()
    {
        Check(Plan.Replace("\"locationsTruncated\":false", "\"locationsTruncated\":true"), "Policy.LocationUnknown");
        Check(Plan.Replace("\"locations\":[{\"partId\":\"part-0001\",\"childIndexes\":[1,2,1]}]", "\"locations\":[]"), "Policy.LocationUnknown");
        Check(Plan.Replace("[1,2,1]", "[9]"), "Policy.LossNotAllowed");
        CompatibilityPlanData plan = CompatibilityPlanData.Parse(Plan, TestContext.Current.CancellationToken);
        string extra = Policy.Replace("\"allowedLosses\":[", "\"allowedLosses\":[{\"code\":\"Docx.ThemeFont\",\"location\":{\"partId\":\"part-0002\",\"childIndexes\":[1]}},");
        Assert.Equal("Policy.UnmatchedPermission", plan.CheckAdmission(CompatibilityPolicyData.Parse(extra, TestContext.Current.CancellationToken), false, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void InvalidStagesDuplicateKeysAndMalformedLocationsFailClosed()
    {
        string[] invalid =
        {
            Plan.Replace("\"sourceFormat\":\"docx\"", "\"sourceFormat\":\"pdf\""),
            Plan.Replace("\"validation\":\"not-performed\"", "\"validation\":\"passed\""),
            Plan.Replace("\"phase\":\"parse\"", "\"phase\":\"layout\""),
            Plan.Replace("\"schemaVersion\":\"1.0\"", "\"schemaVersion\":\"1.0\",\"schemaVersion\":\"1.0\""),
            Plan.Replace("\"requirements\":[" + Requirement + "]", "\"requirements\":[" + Requirement + "," + Requirement + "]"),
            Plan.Replace("\"partId\":\"part-0001\"", "\"partId\":\"word/document.xml\""),
            Plan.Replace("[1,2,1]", "[1,0,1]"),
        };
        Assert.All(invalid, json => Assert.Throws<InvalidDataException>(() => CompatibilityPlanData.Parse(json, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public void CanonicalizationSortsRequirementsAndHonorsBoundsAndCancellation()
    {
        string second = Requirement.Replace("path-0001", "path-0002");
        string forward = Plan.Replace("\"requirements\":[" + Requirement + "]", "\"requirements\":[" + Requirement + "," + second + "]");
        string reverse = Plan.Replace("\"requirements\":[" + Requirement + "]", "\"requirements\":[" + second + "," + Requirement + "]");
        CompatibilityPlanData a = CompatibilityPlanData.Parse(forward, TestContext.Current.CancellationToken);
        CompatibilityPlanData b = CompatibilityPlanData.Parse(reverse, TestContext.Current.CancellationToken);
        Assert.Equal(a.CanonicalJson(TestContext.Current.CancellationToken), b.CanonicalJson(TestContext.Current.CancellationToken));
        Assert.Equal(a.CanonicalSha256(TestContext.Current.CancellationToken), b.CanonicalSha256(TestContext.Current.CancellationToken));
        Assert.Throws<InvalidDataException>(() => CompatibilityPlanData.Parse(new string(' ', 4 * 1024 * 1024 + 1), TestContext.Current.CancellationToken));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => CompatibilityPlanData.Parse(Plan, cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => a.CanonicalJson(cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => a.CheckAdmission(CompatibilityPolicyData.Parse(Policy, TestContext.Current.CancellationToken), false, cancellation.Token));
    }

    private static void Check(string json, string expected)
    {
        CompatibilityPlanData plan = CompatibilityPlanData.Parse(json, TestContext.Current.CancellationToken);
        CompatibilityPolicyData policy = CompatibilityPolicyData.Parse(Policy.Replace(Digest, plan.CanonicalSha256(TestContext.Current.CancellationToken)), TestContext.Current.CancellationToken);
        Assert.Equal(expected, plan.CheckAdmission(policy, false, TestContext.Current.CancellationToken));
    }
}
