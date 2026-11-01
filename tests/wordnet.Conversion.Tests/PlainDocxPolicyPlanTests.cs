using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using WordNet.Internal;
using Xunit;

namespace WordNet.Tests;

public sealed class PlainDocxPolicyPlanTests
{
    private static readonly DateTimeOffset Clock = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static byte[] Source(string text = "Alpha123") => TestDocuments.Docx(TestDocuments.Body(TestDocuments.TextParagraph(text)));
    private static string Policy(string digest, string semantics = "\"content.text\"", string losses = "") =>
        "{\"schemaVersion\":\"1.0\",\"planDigest\":\"" + digest + "\",\"requiredSemantics\":[" + semantics + "],\"allowedLosses\":[" + losses + "]}";

    [Fact]
    public void CompiledPlanAndStagedExecutionPreserveOwnershipAndReportUnperformedValidation()
    {
        byte[] document = Source(); byte[] prefixed = new byte[document.Length + 2]; Array.Copy(document, 0, prefixed, 2, document.Length);
        using var compileSource = new MemoryStream(prefixed) { Position = 2 };
        PlainDocxPolicyPlan plan = PlainDocxPolicyPlan.Compile(compileSource, Clock, TestContext.Current.CancellationToken);
        Assert.True(compileSource.CanRead); Assert.Equal(compileSource.Length, compileSource.Position);
        string json = plan.CanonicalJson(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("Alpha123", json, StringComparison.Ordinal); Assert.DoesNotContain("word/document.xml", json, StringComparison.Ordinal);
        using JsonDocument manifest = JsonDocument.Parse(json);
        Assert.Equal("performed", manifest.RootElement.GetProperty("stages").GetProperty("layout").GetString());
        Assert.Equal("not-performed", manifest.RootElement.GetProperty("stages").GetProperty("write").GetString());
        Assert.Equal("not-performed", manifest.RootElement.GetProperty("stages").GetProperty("validation").GetString());
        using var source = new MemoryStream(prefixed) { Position = 2 }; using var destination = new MemoryStream(); destination.WriteByte(42);
        PlainDocxPolicyOutcome outcome = plan.Execute(source, destination, Policy(plan.PlanSha256(TestContext.Current.CancellationToken)), TestContext.Current.CancellationToken);
        Assert.Equal("completed", outcome.State); Assert.Equal("Policy.Completed", outcome.Code);
        Assert.Equal(plan.PlanSha256(TestContext.Current.CancellationToken), outcome.PlanSha256);
        Assert.Equal(outcome.ExpectedInputSha256, outcome.ObservedInputSha256); Assert.Equal("not-performed", outcome.IndependentValidation);
        byte[] output = destination.ToArray(); Assert.Equal(42, output[0]); Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(output, 1, 5), StringComparison.Ordinal);
        Assert.Equal(Hash(output.Skip(1).ToArray()), outcome.OutputSha256); Assert.True(source.CanRead); Assert.True(destination.CanWrite);
    }

    [Fact]
    public void StalePolicyChangedSourceAndUnmatchedLossLeaveOutputUntouched()
    {
        byte[] document = Source(); using var compileSource = new MemoryStream(document);
        PlainDocxPolicyPlan plan = PlainDocxPolicyPlan.Compile(compileSource, Clock, TestContext.Current.CancellationToken);
        using var staleSource = new MemoryStream(document); using var staleOutput = new MemoryStream(); staleOutput.WriteByte(42);
        PlainDocxPolicyOutcome stale = plan.Execute(staleSource, staleOutput, Policy(new string('0', 64)), TestContext.Current.CancellationToken);
        Assert.Equal("policy-rejected", stale.State); Assert.Equal("Policy.StalePlan", stale.Code); Assert.Null(stale.ObservedInputSha256);
        Assert.Equal(0, staleSource.Position); Assert.Equal(42, Assert.Single(staleOutput.ToArray()));
        using var changedSource = new MemoryStream(Source("Changed")); using var changedOutput = new MemoryStream(); changedOutput.WriteByte(43);
        PlainDocxPolicyOutcome changed = plan.Execute(changedSource, changedOutput, Policy(plan.PlanSha256(TestContext.Current.CancellationToken)), TestContext.Current.CancellationToken);
        Assert.Equal("source-changed", changed.State); Assert.Equal("Plan.StaleInput", changed.Code); Assert.NotEqual(changed.ExpectedInputSha256, changed.ObservedInputSha256);
        Assert.Null(changed.OutputSha256); Assert.Equal(43, Assert.Single(changedOutput.ToArray()));
        string extraLoss = "{\"code\":\"Docx.ThemeFont\",\"location\":{\"partId\":\"part-0001\",\"childIndexes\":[]}}";
        using var deniedSource = new MemoryStream(document); using var deniedOutput = new MemoryStream(); deniedOutput.WriteByte(44);
        PlainDocxPolicyOutcome denied = plan.Execute(deniedSource, deniedOutput, Policy(plan.PlanSha256(TestContext.Current.CancellationToken), losses: extraLoss), TestContext.Current.CancellationToken);
        Assert.Equal("policy-rejected", denied.State); Assert.Equal("Policy.UnmatchedPermission", denied.Code);
        Assert.Equal(0, deniedSource.Position); Assert.Equal(44, Assert.Single(deniedOutput.ToArray()));
    }

    [Fact]
    public void UnsupportedSemanticsInputAndCancellationFailBeforeOutputMutation()
    {
        byte[] document = Source(); using var compileSource = new MemoryStream(document);
        PlainDocxPolicyPlan plan = PlainDocxPolicyPlan.Compile(compileSource, Clock, TestContext.Current.CancellationToken);
        using var source = new MemoryStream(document); using var destination = new MemoryStream(); destination.WriteByte(45);
        var error = Assert.Throws<InvalidDataException>(() => plan.Execute(source, destination, Policy(plan.PlanSha256(TestContext.Current.CancellationToken), "\"review.history\""), TestContext.Current.CancellationToken));
        Assert.StartsWith("Policy.UnknownSemantic:", error.Message, StringComparison.Ordinal); Assert.Equal(0, source.Position); Assert.Equal(45, Assert.Single(destination.ToArray()));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => plan.Execute(source, destination, Policy(plan.PlanSha256(TestContext.Current.CancellationToken)), cancelled.Token));
        Assert.Equal(0, source.Position); Assert.Equal(45, Assert.Single(destination.ToArray()));
        using var oversized = new MemoryStream(new byte[16 * 1024 * 1024 + 1]);
        Assert.Throws<InvalidDataException>(() => plan.Execute(oversized, destination, Policy(plan.PlanSha256(TestContext.Current.CancellationToken)), TestContext.Current.CancellationToken));
        Assert.Equal(45, Assert.Single(destination.ToArray())); Assert.True(source.CanRead); Assert.True(destination.CanWrite); Assert.True(oversized.CanRead);
        using var same = new MemoryStream(document);
        Assert.Throws<ArgumentException>(() => plan.Execute(same, same, Policy(plan.PlanSha256(TestContext.Current.CancellationToken)), TestContext.Current.CancellationToken));
        Assert.Equal(0, same.Position);
    }

    [Fact]
    public void FinalCopyIoFailureCanLeavePartialOutputAndDoesNotCloseStreams()
    {
        byte[] document = Source(); using var compileSource = new MemoryStream(document);
        PlainDocxPolicyPlan plan = PlainDocxPolicyPlan.Compile(compileSource, Clock, TestContext.Current.CancellationToken);
        using var source = new MemoryStream(document); using var destination = new PartialWriteStream();
        Assert.Throws<IOException>(() => plan.Execute(source, destination, Policy(plan.PlanSha256(TestContext.Current.CancellationToken)), TestContext.Current.CancellationToken));
        Assert.Equal(7, destination.Length); Assert.True(destination.CanWrite); Assert.True(source.CanRead);
    }

    [Fact]
    public void CancellationDuringInputReadCannotPublishOutput()
    {
        byte[] document = Source(); using var compileSource = new MemoryStream(document);
        PlainDocxPolicyPlan plan = PlainDocxPolicyPlan.Compile(compileSource, Clock, TestContext.Current.CancellationToken);
        using var executionCancellation = new CancellationTokenSource(); using var source = new CancelAfterReadStream(document, executionCancellation);
        using var destination = new MemoryStream(); destination.WriteByte(46);
        Assert.Throws<OperationCanceledException>(() => plan.Execute(source, destination, Policy(plan.PlanSha256(TestContext.Current.CancellationToken)), executionCancellation.Token));
        Assert.True(source.Position > 0); Assert.True(source.CanRead); Assert.Equal(46, Assert.Single(destination.ToArray()));
        using var compileCancellation = new CancellationTokenSource(); using var interrupted = new CancelAfterReadStream(document, compileCancellation);
        Assert.Throws<OperationCanceledException>(() => PlainDocxPolicyPlan.Compile(interrupted, Clock, compileCancellation.Token));
        Assert.True(interrupted.Position > 0); Assert.True(interrupted.CanRead);
    }

    [Fact]
    public void UnexpectedDiagnosticSnapshotPreservesCodeCountAndPhaseWithoutSourceLocations()
    {
        var diagnostic = new ConversionDiagnostic("Docx.ThemeFont", "private diagnostic text") { Count = 3 };
        diagnostic.Observe(new SourceLocation("private-part.xml", "/private[1]"), DiagnosticPhase.Write);
        var outcome = new PlainDocxPolicyOutcome("unexpected-finding", "Policy.UnexpectedFinding", "plan", "input", "input", "options", null,
            new[] { diagnostic });
        diagnostic.Count = 4; diagnostic.Observe(new SourceLocation("another-private-part.xml", "/other[1]"), DiagnosticPhase.Parse);
        PlainDocxPolicyFinding finding = Assert.Single(outcome.Findings);
        Assert.Equal("Docx.ThemeFont", finding.Code); Assert.Equal(DiagnosticSeverity.Warning, finding.Severity); Assert.Equal(3, finding.Count);
        Assert.Equal(DiagnosticPhase.Write, Assert.Single(finding.Phases)); Assert.False(finding.SourceLocationsAvailable); Assert.False(finding.LocationsTruncated);
        Assert.Throws<NotSupportedException>(() => ((IList<PlainDocxPolicyFinding>)outcome.Findings).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<DiagnosticPhase>)finding.Phases).Clear());
    }

    private sealed class PartialWriteStream : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            base.Write(buffer, offset, Math.Min(7, count));
            throw new IOException("Synthetic final-copy failure.");
        }
    }

    private sealed class CancelAfterReadStream : MemoryStream
    {
        private readonly CancellationTokenSource cancellation;
        internal CancelAfterReadStream(byte[] bytes, CancellationTokenSource cancellation) : base(bytes) => this.cancellation = cancellation;
        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = base.Read(buffer, offset, Math.Min(count, 7));
            if (read > 0) cancellation.Cancel();
            return read;
        }
    }

    private static string Hash(byte[] bytes)
    {
#if NET8_0_OR_GREATER
        byte[] hash = SHA256.HashData(bytes);
#else
        using var algorithm = SHA256.Create(); byte[] hash = algorithm.ComputeHash(bytes);
#endif
#if NET10_0_OR_GREATER
        return Convert.ToHexStringLower(hash);
#elif NET8_0_OR_GREATER
        return Convert.ToHexString(hash).ToLowerInvariant();
#else
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
#endif
    }
}
