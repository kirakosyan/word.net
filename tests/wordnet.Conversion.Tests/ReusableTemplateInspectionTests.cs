using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using static WordNet.Tests.ControlTestDocuments;

namespace WordNet.Tests;

public sealed class ReusableTemplateInspectionTests
{
    private static readonly DateTimeOffset FixedTime = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static byte[] Template() => Package(Document("<w:p><w:fldSimple w:instr='MERGEFIELD Name'><w:r><w:t>private-cache</w:t></w:r></w:fldSimple></w:p>"));
    private static TemplateDataSchema Schema() => new TemplateDataSchema(new TemplateSchemaPath("Name", TemplatePathType.Scalar));

    [Fact]
    public void ReuseVerifiesCurrentBytesAndReturnsTheOriginalBoundedInspection()
    {
        byte[] document = Template(); var converter = new WordConverter(); var schema = Schema();
        using var source = new MemoryStream(document);
        ReusableTemplateInspection plan = converter.CompileReusableTemplateInspection(source, schema, FixedTime, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(source.CanRead); Assert.Equal(source.Length, source.Position);
        byte[] prefixed = new byte[document.Length + 3]; Array.Copy(document, 0, prefixed, 3, document.Length);
        using var current = new MemoryStream(prefixed) { Position = 3 };
        TemplateInspection first = plan.Reuse(current, schema, TestContext.Current.CancellationToken);
        Assert.Same(plan.Inspection, first);
        Assert.True(current.CanRead); Assert.Equal(current.Length, current.Position);
        Assert.Equal(plan.InputSha256, first.InputSha256); Assert.Equal(schema.IdentitySha256, plan.DataSchemaSha256);
        Assert.False(first.IsReusable); Assert.Contains(first.Findings, item => item.Code == "Plan.UninspectedCoverage" && item.Unknown);
        using var repeated = new MemoryStream(document);
        Assert.Same(first, plan.Reuse(repeated, schema, TestContext.Current.CancellationToken));
        string json = JsonSerializer.Serialize(plan);
        Assert.DoesNotContain("Name", json, StringComparison.Ordinal); Assert.DoesNotContain("private-cache", json, StringComparison.Ordinal);
        Assert.DoesNotContain("word/document.xml", json, StringComparison.Ordinal);
        Assert.All(new[] { plan.InputSha256, plan.DataSchemaSha256, plan.OptionsSha256, plan.FontsSha256, plan.ProvidersSha256, plan.EngineSha256 },
            digest => Assert.Matches("^[0-9a-f]{64}$", digest));
    }

    [Fact]
    public void ChangedSchemaOrBytesCannotReuseTheInspection()
    {
        byte[] document = Template(); var converter = new WordConverter(); var schema = Schema();
        using var source = new MemoryStream(document);
        ReusableTemplateInspection plan = converter.CompileReusableTemplateInspection(source, schema, FixedTime, cancellationToken: TestContext.Current.CancellationToken);
        var changedSchema = new TemplateDataSchema(new TemplateSchemaPath("Name", TemplatePathType.Scalar, required: false));
        using var untouched = new MemoryStream(document);
        var schemaError = Assert.Throws<InvalidDataException>(() => plan.Reuse(untouched, changedSchema, TestContext.Current.CancellationToken));
        Assert.StartsWith("Plan.StaleSchema:", schemaError.Message, StringComparison.Ordinal); Assert.Equal(0, untouched.Position); Assert.True(untouched.CanRead);
        byte[] changed = (byte[])document.Clone(); changed[changed.Length - 1] ^= 1;
        using var changedSource = new MemoryStream(changed);
        var inputError = Assert.Throws<InvalidDataException>(() => plan.Reuse(changedSource, schema, TestContext.Current.CancellationToken));
        Assert.StartsWith("Plan.StaleInput:", inputError.Message, StringComparison.Ordinal); Assert.True(changedSource.CanRead);
    }

    [Fact]
    public void FixedClockAndFidelityChangeOptionsWithoutChangingTheSchemaOrFontIdentity()
    {
        byte[] document = Template(); var converter = new WordConverter(); var schema = Schema();
        using var first = new MemoryStream(document); using var later = new MemoryStream(document); using var lenient = new MemoryStream(document);
        ReusableTemplateInspection a = converter.CompileReusableTemplateInspection(first, schema, FixedTime, cancellationToken: TestContext.Current.CancellationToken);
        ReusableTemplateInspection b = converter.CompileReusableTemplateInspection(later, schema, FixedTime.AddMinutes(1), cancellationToken: TestContext.Current.CancellationToken);
        ReusableTemplateInspection c = converter.CompileReusableTemplateInspection(lenient, schema, FixedTime, FidelityMode.Lenient, TestContext.Current.CancellationToken);
        Assert.NotEqual(a.OptionsSha256, b.OptionsSha256); Assert.NotEqual(a.OptionsSha256, c.OptionsSha256);
        Assert.Equal(a.DataSchemaSha256, b.DataSchemaSha256); Assert.Equal(a.FontsSha256, b.FontsSha256);
        Assert.Equal(a.EngineSha256, b.EngineSha256); Assert.Equal(a.ProvidersSha256, b.ProvidersSha256);
        using var invalid = new MemoryStream(document);
        Assert.Throws<ArgumentOutOfRangeException>(() => converter.CompileReusableTemplateInspection(invalid, schema, FixedTime, (FidelityMode)999, TestContext.Current.CancellationToken));
        Assert.Equal(0, invalid.Position);
    }

    [Fact]
    public void ReuseEnforcesInputLimitAndCancellationWithoutClosingStreams()
    {
        byte[] document = Template(); var schema = Schema();
        using var source = new MemoryStream(document);
        ReusableTemplateInspection plan = new WordConverter().CompileReusableTemplateInspection(source, schema, FixedTime, cancellationToken: TestContext.Current.CancellationToken);
        using var oversized = new MemoryStream(new byte[16 * 1024 * 1024 + 1]);
        var error = Assert.Throws<InvalidDataException>(() => plan.Reuse(oversized, schema, TestContext.Current.CancellationToken));
        Assert.StartsWith("Limit.TemplateInspection:", error.Message, StringComparison.Ordinal); Assert.True(oversized.CanRead);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        using var current = new MemoryStream(document);
        Assert.Throws<OperationCanceledException>(() => plan.Reuse(current, schema, cancelled.Token));
        Assert.True(current.CanRead); Assert.Equal(0, current.Position);
        using var compileSource = new MemoryStream(document);
        Assert.Throws<OperationCanceledException>(() => new WordConverter().CompileReusableTemplateInspection(compileSource, schema, FixedTime, cancellationToken: cancelled.Token));
        Assert.True(compileSource.CanRead); Assert.Equal(0, compileSource.Position);
    }

    [Fact]
    public void CancellationDuringSourceReadDoesNotPublishAndLeavesStreamsOpen()
    {
        byte[] document = Template(); var schema = Schema(); var converter = new WordConverter();
        using var source = new MemoryStream(document);
        ReusableTemplateInspection plan = converter.CompileReusableTemplateInspection(source, schema, FixedTime, cancellationToken: TestContext.Current.CancellationToken);
        using var reuseCancellation = new CancellationTokenSource();
        using var reuseSource = new CancelAfterReadStream(document, reuseCancellation);
        Assert.Throws<OperationCanceledException>(() => plan.Reuse(reuseSource, schema, reuseCancellation.Token));
        Assert.True(reuseSource.CanRead); Assert.True(reuseSource.Position > 0);
        using var compileCancellation = new CancellationTokenSource();
        using var compileSource = new CancelAfterReadStream(document, compileCancellation);
        Assert.Throws<OperationCanceledException>(() => converter.CompileReusableTemplateInspection(compileSource, schema, FixedTime, cancellationToken: compileCancellation.Token));
        Assert.True(compileSource.CanRead); Assert.True(compileSource.Position > 0);
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

    [Fact]
    public void DestinationProfilesHaveDistinctIdentitiesAndKeepOutputChecksUnknown()
    {
        byte[] document = Template(); var schema = Schema(); var converter = new WordConverter();
        var destinations = new[] { DocumentFormat.Docx, DocumentFormat.Pdf, DocumentFormat.Xps };
        var plans = destinations.Select(destination =>
        {
            using var source = new MemoryStream(document);
            return converter.CompileReusableTemplateInspection(source, schema, destination, FixedTime,
                cancellationToken: TestContext.Current.CancellationToken);
        }).ToArray();
        Assert.Equal(3, plans.Select(plan => plan.OptionsSha256).Distinct(StringComparer.Ordinal).Count());
        using var defaultSource = new MemoryStream(document);
        ReusableTemplateInspection defaultPlan = converter.CompileReusableTemplateInspection(defaultSource, schema, FixedTime,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(plans[0].OptionsSha256, defaultPlan.OptionsSha256);
        for (int index = 0; index < destinations.Length; index++)
        {
            Assert.Equal(destinations[index], plans[index].Inspection.Destination);
            Assert.Contains(plans[index].Inspection.Findings, item => item.Code == "Plan.UninspectedCoverage" && item.Unknown);
            Assert.Contains(plans[index].Inspection.Findings, item => item.Code == "Plan.ReuseUnavailable" && item.Unknown);
            using var unchanged = new MemoryStream(document);
            Assert.Same(plans[index].Inspection, plans[index].Reuse(unchanged, schema, TestContext.Current.CancellationToken));
        }
        using var invalid = new MemoryStream(document);
        Assert.Throws<ArgumentOutOfRangeException>(() => converter.CompileReusableTemplateInspection(invalid, schema, DocumentFormat.Html,
            FixedTime, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(0, invalid.Position); Assert.True(invalid.CanRead);
    }

    [Fact]
    public async Task ConcurrentReuseIsIsolatedAndKeepsTheSameSnapshot()
    {
        byte[] document = Template(); var schema = Schema();
        using var source = new MemoryStream(document);
        ReusableTemplateInspection plan = new WordConverter().CompileReusableTemplateInspection(source, schema, FixedTime, cancellationToken: TestContext.Current.CancellationToken);
        Task<TemplateInspection>[] tasks = Enumerable.Range(0, 3).Select(_ => Task.Run(() =>
        {
            using var current = new MemoryStream(document);
            return plan.Reuse(current, schema, TestContext.Current.CancellationToken);
        }, TestContext.Current.CancellationToken)).ToArray();
        TemplateInspection[] reports = await Task.WhenAll(tasks);
        Assert.All(reports, report => Assert.Same(reports[0], report));
    }
}
