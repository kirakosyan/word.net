using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace WordNet.Tests;

public sealed class TemplateCompatibilityPublicTests
{
    private static readonly DateTimeOffset Clock = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static byte[] Source() => TestDocuments.Docx(TestDocuments.Body(TestDocuments.TextParagraph("Alpha123")));
    private static string Policy(string digest, string semantics = "\"content.text\"") =>
        "{\"schemaVersion\":\"1.0\",\"planDigest\":\"" + digest + "\",\"requiredSemantics\":[" + semantics + "],\"allowedLosses\":[]}";

    [Fact]
    public void PublicPlanExecutesTheBoundPlainProfileWithoutClaimingValidation()
    {
        byte[] bytes = Source(); using var source = new MemoryStream(bytes);
        TemplateCompatibilityPlan plan = new WordConverter().CompileTemplateCompatibilityPlan(source, new TemplateDataSchema(), DocumentFormat.Pdf, Clock, TestContext.Current.CancellationToken);
        Assert.True(source.CanRead); Assert.Equal(source.Length, source.Position);
        string json = plan.GetCanonicalJson(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("Alpha123", json, StringComparison.Ordinal); Assert.DoesNotContain("word/document.xml", json, StringComparison.Ordinal);
        using JsonDocument parsed = JsonDocument.Parse(json); Assert.Equal("pdf", parsed.RootElement.GetProperty("destinationFormat").GetString());
        using var current = new MemoryStream(bytes); using var destination = new MemoryStream(); destination.WriteByte(42);
        TemplatePolicyOutcome outcome = plan.Execute(current, destination, Policy(plan.GetIdentitySha256(TestContext.Current.CancellationToken)), TestContext.Current.CancellationToken);
        Assert.Equal("completed", outcome.State); Assert.Equal("Policy.Completed", outcome.Code); Assert.Equal("not-performed", outcome.IndependentValidation);
        Assert.Equal(plan.GetIdentitySha256(TestContext.Current.CancellationToken), outcome.PlanSha256); Assert.Equal(outcome.ExpectedInputSha256, outcome.ObservedInputSha256);
        Assert.NotNull(outcome.OutputSha256); Assert.Empty(outcome.Findings); Assert.True(current.CanRead); Assert.True(destination.CanWrite);
        string resultJson = JsonSerializer.Serialize(outcome); Assert.DoesNotContain("Alpha123", resultJson, StringComparison.Ordinal); Assert.DoesNotContain("word/document.xml", resultJson, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedDataAndDestinationRejectBeforeReadingSource()
    {
        var converter = new WordConverter(); using var source = new MemoryStream(Source());
        var schema = new TemplateDataSchema(new TemplateSchemaPath("Name", TemplatePathType.Scalar));
        NotSupportedException data = Assert.Throws<NotSupportedException>(() => converter.CompileTemplateCompatibilityPlan(source, schema, DocumentFormat.Pdf, Clock, TestContext.Current.CancellationToken));
        Assert.StartsWith("Plan.DataProfile:", data.Message, StringComparison.Ordinal); Assert.Equal(0, source.Position);
        NotSupportedException destination = Assert.Throws<NotSupportedException>(() => converter.CompileTemplateCompatibilityPlan(source, new TemplateDataSchema(), DocumentFormat.Docx, Clock, TestContext.Current.CancellationToken));
        Assert.StartsWith("Plan.DestinationProfile:", destination.Message, StringComparison.Ordinal); Assert.Equal(0, source.Position); Assert.True(source.CanRead);
    }

    [Fact]
    public void PublicPolicyRejectionAndChangedSourceDoNotTouchOutput()
    {
        byte[] bytes = Source(); var converter = new WordConverter(); using var compile = new MemoryStream(bytes);
        TemplateCompatibilityPlan plan = converter.CompileTemplateCompatibilityPlan(compile, new TemplateDataSchema(), DocumentFormat.Pdf, Clock, TestContext.Current.CancellationToken);
        using var original = new MemoryStream(bytes); using var target = new MemoryStream(); target.WriteByte(43);
        TemplatePolicyOutcome denied = plan.Execute(original, target, Policy(new string('0', 64)), TestContext.Current.CancellationToken);
        Assert.Equal("policy-rejected", denied.State); Assert.Equal("Policy.StalePlan", denied.Code); Assert.Equal(0, original.Position); Assert.Equal(43, Assert.Single(target.ToArray()));
        byte[] changed = (byte[])bytes.Clone(); changed[changed.Length - 1] ^= 1; using var changedSource = new MemoryStream(changed);
        TemplatePolicyOutcome stale = plan.Execute(changedSource, target, Policy(plan.GetIdentitySha256(TestContext.Current.CancellationToken)), TestContext.Current.CancellationToken);
        Assert.Equal("source-changed", stale.State); Assert.Equal("Plan.StaleInput", stale.Code); Assert.Null(stale.OutputSha256); Assert.Equal(43, Assert.Single(target.ToArray()));
    }

    [Fact]
    public async Task SharedPlanExecutesConcurrentlyWithSeparateStreams()
    {
        byte[] bytes = Source(); using var compile = new MemoryStream(bytes);
        TemplateCompatibilityPlan plan = new WordConverter().CompileTemplateCompatibilityPlan(compile, new TemplateDataSchema(), DocumentFormat.Pdf, Clock, TestContext.Current.CancellationToken);
        string policy = Policy(plan.GetIdentitySha256(TestContext.Current.CancellationToken));
        Task<TemplatePolicyOutcome>[] tasks = Enumerable.Range(0, 3).Select(_ => Task.Run(() =>
        {
            using var source = new MemoryStream(bytes); using var destination = new MemoryStream();
            TemplatePolicyOutcome outcome = plan.Execute(source, destination, policy, TestContext.Current.CancellationToken);
            Assert.True(destination.Length > 0); Assert.True(source.CanRead); Assert.True(destination.CanWrite); return outcome;
        }, TestContext.Current.CancellationToken)).ToArray();
        TemplatePolicyOutcome[] results = await Task.WhenAll(tasks);
        Assert.All(results, item => { Assert.Equal("completed", item.State); Assert.NotNull(item.OutputSha256); });
    }
}
