using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace WordNet.Tests;

public sealed class TemplateCompatibilityPolicyLossTests
{
    private static readonly DateTimeOffset Clock = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly int[] ThemeLocation = { 1, 1, 1, 1 };
    private const string FontPermission = "{\"code\":\"Docx.ThemeFont\",\"location\":{\"partId\":\"part-0001\",\"childIndexes\":[1,1,1,1]}}";

    private static byte[] Source(string body) => TestDocuments.Docx(TestDocuments.Body(body));
    private static string Policy(TemplateCompatibilityPlan plan, string loss = "") =>
        "{\"schemaVersion\":\"1.0\",\"planDigest\":\"" + plan.GetIdentitySha256() + "\",\"requiredSemantics\":[\"content.text\"],\"allowedLosses\":[" + loss + "]}";

    [Fact]
    public void ExplicitThemeLossRequiresTheExactPermissionAndRetainsItsObservation()
    {
        byte[] source = Source("<w:p><w:r><w:rPr><w:rFonts w:asciiTheme='minorHAnsi' w:hAnsiTheme='minorHAnsi'/></w:rPr><w:t>Alpha123</w:t></w:r></w:p>");
        using var initial = new MemoryStream(source); var converter = new WordConverter();
        TemplateCompatibilityPlan plan = converter.CompileTemplateCompatibilityPlan(initial, new TemplateDataSchema(), DocumentFormat.Pdf, Clock,
            FidelityMode.BestEffort, TestContext.Current.CancellationToken);
        using JsonDocument json = JsonDocument.Parse(plan.GetCanonicalJson(TestContext.Current.CancellationToken));
        JsonElement finding = Assert.Single(json.RootElement.GetProperty("findings").EnumerateArray());
        Assert.Equal("Docx.ThemeFont", finding.GetProperty("code").GetString());
        Assert.Equal("parse", finding.GetProperty("phase").GetString());
        Assert.Equal(ThemeLocation, finding.GetProperty("locations")[0].GetProperty("childIndexes").EnumerateArray().Select(value => value.GetInt32()).ToArray());
        using var deniedSource = new MemoryStream(source); using var deniedDestination = new MemoryStream(); deniedDestination.WriteByte(42);
        TemplatePolicyOutcome denied = plan.Execute(deniedSource, deniedDestination, Policy(plan), TestContext.Current.CancellationToken);
        Assert.Equal("policy-rejected", denied.State); Assert.Equal("Policy.LossNotAllowed", denied.Code); Assert.Equal(0, deniedSource.Position);
        Assert.Equal(42, Assert.Single(deniedDestination.ToArray()));
        using var wrongSource = new MemoryStream(source); using var wrongDestination = new MemoryStream(); wrongDestination.WriteByte(43);
        TemplatePolicyOutcome wrong = plan.Execute(wrongSource, wrongDestination, Policy(plan, FontPermission.Replace("[1,1,1,1]", "[1,1,1,2]")), TestContext.Current.CancellationToken);
        Assert.Equal("policy-rejected", wrong.State); Assert.Equal("Policy.LossNotAllowed", wrong.Code); Assert.Equal(0, wrongSource.Position);
        Assert.Equal(43, Assert.Single(wrongDestination.ToArray()));
        using var allowedSource = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(44);
        TemplatePolicyOutcome allowed = plan.Execute(allowedSource, output, Policy(plan, FontPermission), TestContext.Current.CancellationToken);
        Assert.Equal("allowed-loss", allowed.State); Assert.Equal("Policy.AllowedLoss", allowed.Code); Assert.Equal("not-performed", allowed.IndependentValidation);
        TemplatePolicyFinding observed = Assert.Single(allowed.Findings);
        Assert.Equal("Docx.ThemeFont", observed.Code); Assert.Equal(DiagnosticSeverity.Warning, observed.Severity);
        Assert.Equal(1, observed.Count); Assert.Equal(DiagnosticPhase.Parse, Assert.Single(observed.Phases)); Assert.False(observed.SourceLocationsAvailable);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(output.ToArray(), 1, 5), StringComparison.Ordinal);
        Assert.Equal(64, allowed.OutputSha256!.Length); Assert.True(allowedSource.CanRead); Assert.True(output.CanWrite);
    }

    [Fact]
    public void UnexpectedMissingLinkDestinationIsObservedAtWriteAndNeverPublished()
    {
        byte[] source = Source("<w:p><w:hyperlink w:anchor='Missing'><w:r><w:t>Alpha123</w:t></w:r></w:hyperlink></w:p>");
        using var initial = new MemoryStream(source);
        TemplateCompatibilityPlan plan = new WordConverter().CompileTemplateCompatibilityPlan(initial, new TemplateDataSchema(), DocumentFormat.Pdf, Clock,
            FidelityMode.BestEffort, TestContext.Current.CancellationToken);
        using JsonDocument json = JsonDocument.Parse(plan.GetCanonicalJson(TestContext.Current.CancellationToken));
        Assert.Empty(json.RootElement.GetProperty("findings").EnumerateArray());
        Assert.Equal("not-performed", json.RootElement.GetProperty("stages").GetProperty("write").GetString());
        using var execution = new MemoryStream(source); using var output = new MemoryStream(); output.WriteByte(45);
        TemplatePolicyOutcome result = plan.Execute(execution, output, Policy(plan), TestContext.Current.CancellationToken);
        Assert.Equal("unexpected-finding", result.State); Assert.Equal("Policy.UnexpectedFinding", result.Code);
        TemplatePolicyFinding finding = Assert.Single(result.Findings);
        Assert.Equal("Pdf.OmittedDestination", finding.Code); Assert.Equal(DiagnosticPhase.Write, Assert.Single(finding.Phases));
        Assert.Equal(1, finding.Count); Assert.False(finding.SourceLocationsAvailable); Assert.Null(result.OutputSha256);
        Assert.Equal(45, Assert.Single(output.ToArray())); Assert.True(execution.CanRead); Assert.True(output.CanWrite);
    }

    [Fact]
    public void StrictNeverDowngradesAndUnsupportedModeRejectsBeforeReading()
    {
        byte[] source = Source("<w:p><w:r><w:rPr><w:rFonts w:asciiTheme='minorHAnsi' w:hAnsiTheme='minorHAnsi'/></w:rPr><w:t>Alpha</w:t></w:r></w:p>");
        using var strict = new MemoryStream(source);
        Assert.Throws<NotSupportedException>(() => new WordConverter().CompileTemplateCompatibilityPlan(strict, new TemplateDataSchema(), DocumentFormat.Pdf,
            Clock, FidelityMode.Strict, TestContext.Current.CancellationToken));
        using var invalid = new MemoryStream(source);
        Assert.Throws<ArgumentOutOfRangeException>(() => new WordConverter().CompileTemplateCompatibilityPlan(invalid, new TemplateDataSchema(), DocumentFormat.Pdf,
            Clock, FidelityMode.Lenient, TestContext.Current.CancellationToken));
        Assert.Equal(0, invalid.Position);
    }

    [Theory]
    [InlineData("<w:p><w:r><w:rPr><w:rFonts w:asciiTheme='majorHAnsi' w:hAnsiTheme='minorHAnsi'/></w:rPr><w:t>Alpha</w:t></w:r></w:p>")]
    [InlineData("<w:p><w:r><w:rPr><w:rFonts w:ascii='Other' w:asciiTheme='minorHAnsi' w:hAnsiTheme='minorHAnsi'/></w:rPr><w:t>Alpha</w:t></w:r></w:p>")]
    [InlineData("<w:p><w:hyperlink w:anchor='Missing' r:id='link'><w:r><w:t>Alpha</w:t></w:r></w:hyperlink></w:p>")]
    [InlineData("<w:p><w:hyperlink w:anchor='Missing'><w:r><w:t>Alpha</w:t></w:r><w:r><w:t>Bravo</w:t></w:r></w:hyperlink></w:p>")]
    public void BestEffortDoesNotAdmitUnqualifiedFormattingOrLinks(string body)
    {
        using var source = new MemoryStream(Source(body));
        var error = Assert.Throws<NotSupportedException>(() => new WordConverter().CompileTemplateCompatibilityPlan(source,
            new TemplateDataSchema(), DocumentFormat.Pdf, Clock, FidelityMode.BestEffort, TestContext.Current.CancellationToken));
        Assert.StartsWith("Plan.SourceProfile:", error.Message, StringComparison.Ordinal); Assert.True(source.CanRead);
    }
}
