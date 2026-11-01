using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using WordNet.Model;

namespace WordNet.Internal;

// Implementation of the public, deliberately narrow staged-execution profile; not a general template plan.
internal sealed class PlainDocxPolicyPlan
{
    private const int MaximumInputBytes = 16 * 1024 * 1024;
    private readonly WordConverter converter;
    private readonly ReusableTemplateInspection snapshot;
    private readonly CompatibilityPlanData plan;
    private readonly TemplateDataSchema schema;
    private readonly DateTimeOffset fieldTime;
    private readonly FidelityMode fidelityMode;
    private readonly PlainDocxPolicyInput sourceKind;

    private PlainDocxPolicyPlan(WordConverter converter, ReusableTemplateInspection snapshot, CompatibilityPlanData plan, TemplateDataSchema schema,
        DateTimeOffset fieldTime, FidelityMode fidelityMode, PlainDocxPolicyInput sourceKind)
    { this.converter = converter; this.snapshot = snapshot; this.plan = plan; this.schema = schema; this.fieldTime = fieldTime; this.fidelityMode = fidelityMode; this.sourceKind = sourceKind; }

    internal string CanonicalJson(CancellationToken cancellationToken = default) => plan.CanonicalJson(cancellationToken);
    internal string PlanSha256(CancellationToken cancellationToken = default) => plan.CanonicalSha256(cancellationToken);

    internal static PlainDocxPolicyPlan Compile(Stream source, DateTimeOffset fieldTime, CancellationToken cancellationToken = default)
        => Compile(new WordConverter(), source, fieldTime, cancellationToken);

    internal static PlainDocxPolicyPlan Compile(WordConverter converter, Stream source, DateTimeOffset fieldTime, CancellationToken cancellationToken = default)
        => Compile(converter, source, fieldTime, FidelityMode.Strict, cancellationToken);

    internal static PlainDocxPolicyPlan Compile(WordConverter converter, Stream source, DateTimeOffset fieldTime, FidelityMode fidelityMode, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(converter, nameof(converter)); Guard.NotNull(source, nameof(source));
        if (!source.CanRead) throw new ArgumentException("The source must be readable.", nameof(source));
        if (fidelityMode is not FidelityMode.Strict and not FidelityMode.BestEffort) throw new ArgumentOutOfRangeException(nameof(fidelityMode));
        byte[] input = ReadBounded(source, cancellationToken);
        (string Text, PlainDocxPolicyInput Kind) profile;
        using (var current = new MemoryStream(input, writable: false)) profile = PlainDocxPolicyProfile.Read(current, fidelityMode == FidelityMode.BestEffort, cancellationToken);
        DateTimeOffset fixedTime = fieldTime.ToUniversalTime(); var schema = new TemplateDataSchema();
        var parseContext = new ConversionContext(Options(fixedTime, fidelityMode), cancellationToken) { Inspecting = true, Destination = DocumentFormat.Pdf };
        DocumentModel model;
        using (var current = new MemoryStream(input, writable: false)) model = WordConverter.ReadDocument(current, DocumentFormat.Docx, parseContext);
        ParagraphBlock[] paragraphs = model.Paragraphs.Distinct().ToArray();
        if (paragraphs.Length != 1 || paragraphs[0].Text != profile.Text || !ExpectedDiagnostics(parseContext.Result(0).Diagnostics, profile.Kind))
            throw new NotSupportedException("Plan.Inspection: The parsed text does not match the narrow source profile.");
        ReusableTemplateInspection snapshot;
        using (var current = new MemoryStream(input, writable: false))
            snapshot = converter.CompileReusableTemplateInspection(current, schema, DocumentFormat.Pdf, fixedTime, fidelityMode, cancellationToken);
        DocumentInspection inspection;
        using (var current = new MemoryStream(input, writable: false))
            inspection = converter.Inspect(current, DocumentFormat.Docx, DocumentFormat.Pdf, Options(fixedTime, fidelityMode), includeLayout: true, cancellationToken);
        if (!inspection.LayoutPerformed || inspection.PageCount != 1 || inspection.WouldRejectStrict != (profile.Kind == PlainDocxPolicyInput.ThemeFont)
            || !ExpectedDiagnostics(inspection.Diagnostics, profile.Kind)
            || inspection.Features.Paragraphs != 1 || inspection.Features.Tables != 0 || inspection.Features.Images != 0
            || inspection.Features.Fields != 0 || inspection.Features.ListItems != 0 || inspection.Characters is < 1 or > 40)
            throw new NotSupportedException("Plan.Inspection: The source did not satisfy the narrow clean inspection profile.");
        using (var current = new MemoryStream(input, writable: false)) _ = snapshot.Reuse(current, schema, cancellationToken);
        string json = "{\"schemaVersion\":\"1.0\",\"identities\":{\"input\":\"" + snapshot.InputSha256
            + "\",\"options\":\"" + snapshot.OptionsSha256 + "\",\"dataSchema\":\"" + snapshot.DataSchemaSha256
            + "\",\"fonts\":\"" + snapshot.FontsSha256 + "\",\"providers\":\"" + snapshot.ProvidersSha256
            + "\",\"engine\":\"" + snapshot.EngineSha256 + "\"},\"sourceFormat\":\"docx\",\"destinationFormat\":\"pdf\","
            + "\"stages\":{\"parse\":\"performed\",\"destination\":\"performed\",\"layout\":\"performed\",\"write\":\"not-performed\",\"validation\":\"not-performed\"},"
            + "\"requirements\":[],\"semantics\":[{\"code\":\"content.text\",\"assessment\":\"supported\"}],\"findings\":["
            + (profile.Kind == PlainDocxPolicyInput.ThemeFont ? "{\"code\":\"Docx.ThemeFont\",\"phase\":\"parse\",\"evidence\":\"observed\",\"severity\":\"warning\",\"count\":1,\"locationsTruncated\":false,\"locations\":[{\"partId\":\"part-0001\",\"childIndexes\":[1,1,1,1]}],\"remediation\":\"Remediation.SetExplicitFont\"}" : "")
            + "],\"observedInputBytes\":"
            + input.Length.ToString(CultureInfo.InvariantCulture) + ",\"estimatedPages\":1}";
        CompatibilityPlanData plan = CompatibilityPlanData.Parse(json, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested(); return new PlainDocxPolicyPlan(converter, snapshot, plan, schema, fixedTime, fidelityMode, profile.Kind);
    }

    internal PlainDocxPolicyOutcome Execute(Stream source, Stream destination, string policyJson, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(source, nameof(source)); Guard.NotNull(destination, nameof(destination)); Guard.NotNull(policyJson, nameof(policyJson));
        if (ReferenceEquals(source, destination)) throw new ArgumentException("Input and output must be different streams.", nameof(destination));
        if (!source.CanRead) throw new ArgumentException("The source must be readable.", nameof(source));
        if (!destination.CanWrite) throw new ArgumentException("The destination must be writable.", nameof(destination));
        cancellationToken.ThrowIfCancellationRequested();
        CompatibilityPolicyData policy = CompatibilityPolicyData.Parse(policyJson, cancellationToken);
        string admission = plan.CheckAdmission(policy, strict: fidelityMode == FidelityMode.Strict, cancellationToken);
        if (admission != "Policy.Admitted") return Outcome("policy-rejected", admission, null, null, cancellationToken);
        byte[] input = ReadBounded(source, cancellationToken); string inputHash = Hash(input);
        string? stale = CheckCurrent(input, cancellationToken);
        if (stale != null) return Outcome(stale == "Plan.StaleInput" ? "source-changed" : "environment-changed", stale, inputHash, null, cancellationToken);

        using var staged = new MemoryStream(); ConversionResult conversion;
        try
        {
            using var current = new MemoryStream(input, writable: false);
            conversion = converter.Convert(current, staged, DocumentFormat.Docx, DocumentFormat.Pdf, Options(fieldTime, fidelityMode), cancellationToken);
        }
        catch (NotSupportedException) { return Outcome("conversion-failed", "Policy.ConversionFailed", inputHash, null, cancellationToken); }
        catch (InvalidDataException) { return Outcome("conversion-failed", "Policy.ConversionFailed", inputHash, null, cancellationToken); }
        if (!ExpectedDiagnostics(conversion.Diagnostics, sourceKind)) return Outcome("unexpected-finding", "Policy.UnexpectedFinding", inputHash, null, cancellationToken, conversion.Diagnostics);
        byte[] output = staged.ToArray(); string outputHash = Hash(output);
        stale = CheckCurrent(input, cancellationToken);
        if (stale != null) return Outcome(stale == "Plan.StaleInput" ? "source-changed" : "environment-changed", stale, inputHash, null, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        for (int position = 0; position < output.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested(); int count = Math.Min(81920, output.Length - position);
            destination.Write(output, position, count); position += count;
        }
        return sourceKind == PlainDocxPolicyInput.ThemeFont
            ? Outcome("allowed-loss", "Policy.AllowedLoss", inputHash, outputHash, cancellationToken, conversion.Diagnostics)
            : Outcome("completed", "Policy.Completed", inputHash, outputHash, cancellationToken);
    }

    private string? CheckCurrent(byte[] input, CancellationToken cancellationToken)
    {
        try { using var source = new MemoryStream(input, writable: false); _ = snapshot.Reuse(source, schema, cancellationToken); return null; }
        catch (InvalidDataException error) when (error.Message.StartsWith("Plan.StaleInput:", StringComparison.Ordinal)) { return "Plan.StaleInput"; }
        catch (InvalidDataException error) when (error.Message.StartsWith("Plan.EnvironmentChanged:", StringComparison.Ordinal)) { return "Plan.EnvironmentChanged"; }
    }

    private PlainDocxPolicyOutcome Outcome(string state, string code, string? observedInput, string? output, CancellationToken cancellationToken,
        IReadOnlyList<ConversionDiagnostic>? diagnostics = null) =>
        new PlainDocxPolicyOutcome(state, code, plan.CanonicalSha256(cancellationToken), snapshot.InputSha256, observedInput, snapshot.OptionsSha256, output, diagnostics);

    private static ConversionOptions Options(DateTimeOffset fieldTime, FidelityMode fidelityMode) => new ConversionOptions(fidelityMode, maximumInputBytes: MaximumInputBytes) { Clock = () => fieldTime };

    private static bool ExpectedDiagnostics(IReadOnlyList<ConversionDiagnostic> diagnostics, PlainDocxPolicyInput kind)
    {
        if (kind != PlainDocxPolicyInput.ThemeFont) return diagnostics.Count == 0;
        if (diagnostics.Count != 1) return false;
        ConversionDiagnostic finding = diagnostics[0];
        if (finding.Code != "Docx.ThemeFont" || finding.Severity != DiagnosticSeverity.Warning || finding.Count != 1
            || finding.LocationsTruncated || finding.Occurrences.Count != 1) return false;
        DiagnosticOccurrence occurrence = finding.Occurrences[0];
        return occurrence.Phase == DiagnosticPhase.Parse && occurrence.Location.Part == "word/document.xml"
            && occurrence.Location.Path == "/document[1]/body[1]/p[1]/r[1]";
    }

    private static byte[] ReadBounded(Stream source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); using var buffer = new MemoryStream(); byte[] chunk = new byte[8192];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested(); int requested = (int)Math.Min(chunk.Length, MaximumInputBytes - buffer.Length + 1);
            int count = source.Read(chunk, 0, requested); if (count == 0) break;
            if (buffer.Length + count > MaximumInputBytes) throw new InvalidDataException("Limit.CompatibilityPlan: Source bytes exceed the bound.");
            buffer.Write(chunk, 0, count);
        }
        cancellationToken.ThrowIfCancellationRequested(); return buffer.ToArray();
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

internal sealed class PlainDocxPolicyOutcome
{
    private readonly ReadOnlyCollection<PlainDocxPolicyFinding> findings;
    internal PlainDocxPolicyOutcome(string state, string code, string plan, string expectedInput, string? observedInput, string options, string? output,
        IReadOnlyList<ConversionDiagnostic>? diagnostics = null)
    {
        State = state; Code = code; PlanSha256 = plan; ExpectedInputSha256 = expectedInput; ObservedInputSha256 = observedInput; OptionsSha256 = options; OutputSha256 = output;
        findings = Array.AsReadOnly((diagnostics ?? Array.Empty<ConversionDiagnostic>()).Select(item => new PlainDocxPolicyFinding(item)).ToArray());
    }
    internal string State { get; }
    internal string Code { get; }
    internal string PlanSha256 { get; }
    internal string ExpectedInputSha256 { get; }
    internal string? ObservedInputSha256 { get; }
    internal string OptionsSha256 { get; }
    internal string? OutputSha256 { get; }
    internal IReadOnlyList<PlainDocxPolicyFinding> Findings => findings;
    internal string IndependentValidation { get; } = "not-performed";
}

internal sealed class PlainDocxPolicyFinding
{
    private readonly ReadOnlyCollection<DiagnosticPhase> phases;
    internal PlainDocxPolicyFinding(ConversionDiagnostic diagnostic)
    {
        Code = diagnostic.Code; Severity = diagnostic.Severity; Count = diagnostic.Count; LocationsTruncated = diagnostic.LocationsTruncated;
        phases = Array.AsReadOnly(diagnostic.Occurrences.Select(item => item.Phase).Distinct().OrderBy(item => item).ToArray());
    }
    internal string Code { get; }
    internal DiagnosticSeverity Severity { get; }
    internal int Count { get; }
    internal bool LocationsTruncated { get; }
    internal IReadOnlyList<DiagnosticPhase> Phases => phases;
    internal bool SourceLocationsAvailable { get; }
}
