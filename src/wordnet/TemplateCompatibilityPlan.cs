using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using WordNet.Internal;

namespace WordNet;

/// <summary>An in-process, bounded compatibility plan for the documented plain DOCX-to-PDF profile.</summary>
/// <remarks>The current profile admits only a minimal DOCX package containing one alphanumeric run of at most 40
/// characters, an empty data schema, Strict defaults, bundled fonts and no optional providers. Templates with fields,
/// controls, other text or structured parts are not admitted. The plan is not portable and its runtime/module identity
/// is not binary attestation. Canonical plans and hashes can fingerprint sensitive input. Layout is inspected, but writing
/// and independent validation are not performed while compiling; a plan does not prove output fidelity.</remarks>
public sealed class TemplateCompatibilityPlan
{
    private readonly PlainDocxPolicyPlan plan;
    internal TemplateCompatibilityPlan(PlainDocxPolicyPlan plan) => this.plan = plan;

    /// <summary>Returns privacy-safe canonical JSON describing this plan, with write and validation marked not performed.</summary>
    public string GetCanonicalJson(CancellationToken cancellationToken = default) => plan.CanonicalJson(cancellationToken);

    /// <summary>Returns the SHA-256 of the canonical plan bytes.</summary>
    public string GetIdentitySha256(CancellationToken cancellationToken = default) => plan.PlanSha256(cancellationToken);

    /// <summary>Executes the exact bound source under an explicit version-1 policy, staging PDF output before copying.</summary>
    /// <remarks>The policy binds this plan digest and may require <c>content.text</c>; this Strict-only profile permits
    /// no loss. Caller streams remain open and are consumed from their current positions. Policy, stale-source and staged
    /// conversion failures leave destination untouched. Cancellation or I/O failure during the final copy can leave partial
    /// output. The reported output hash identifies staged bytes handed to the destination, not durable storage. No independent
    /// validator is run. Unknown or unexpected diagnostics reject staged output.</remarks>
    public TemplatePolicyOutcome Execute(Stream source, Stream destination, string policyJson, CancellationToken cancellationToken = default) =>
        new TemplatePolicyOutcome(plan.Execute(source, destination, policyJson, cancellationToken));
}

/// <summary>A privacy-safe observed outcome for a bounded compatibility-plan execution.</summary>
public sealed class TemplatePolicyOutcome
{
    private readonly ReadOnlyCollection<TemplatePolicyFinding> findings;
    internal TemplatePolicyOutcome(PlainDocxPolicyOutcome outcome)
    {
        State = outcome.State; Code = outcome.Code; PlanSha256 = outcome.PlanSha256; ExpectedInputSha256 = outcome.ExpectedInputSha256;
        ObservedInputSha256 = outcome.ObservedInputSha256; OptionsSha256 = outcome.OptionsSha256; OutputSha256 = outcome.OutputSha256;
        IndependentValidation = outcome.IndependentValidation;
        findings = Array.AsReadOnly(outcome.Findings.Select(item => new TemplatePolicyFinding(item)).ToArray());
    }

    /// <summary>Gets completed, policy-rejected, source-changed, environment-changed, conversion-failed or unexpected-finding.</summary>
    public string State { get; }
    /// <summary>Gets the stable machine-readable outcome code.</summary>
    public string Code { get; }
    /// <summary>Gets the canonical plan fingerprint.</summary>
    public string PlanSha256 { get; }
    /// <summary>Gets the source fingerprint captured by the plan.</summary>
    public string ExpectedInputSha256 { get; }
    /// <summary>Gets the observed source fingerprint when input was read, otherwise null.</summary>
    public string? ObservedInputSha256 { get; }
    /// <summary>Gets the fixed output-profile fingerprint.</summary>
    public string OptionsSha256 { get; }
    /// <summary>Gets the staged-output fingerprint after successful final copying, otherwise null.</summary>
    public string? OutputSha256 { get; }
    /// <summary>Gets <c>not-performed</c>; this profile does not run independent validation.</summary>
    public string IndependentValidation { get; }
    /// <summary>Gets privacy-safe summaries of unexpected diagnostics, if any.</summary>
    public IReadOnlyList<TemplatePolicyFinding> Findings => findings;
}

/// <summary>A privacy-safe summary of an unexpected conversion diagnostic.</summary>
public sealed class TemplatePolicyFinding
{
    private readonly ReadOnlyCollection<DiagnosticPhase> phases;
    internal TemplatePolicyFinding(PlainDocxPolicyFinding finding)
    {
        Code = finding.Code; Severity = finding.Severity; Count = finding.Count; LocationsTruncated = finding.LocationsTruncated;
        SourceLocationsAvailable = finding.SourceLocationsAvailable; phases = Array.AsReadOnly(finding.Phases.ToArray());
    }
    /// <summary>Gets the original stable diagnostic code.</summary>
    public string Code { get; }
    /// <summary>Gets the original severity.</summary>
    public DiagnosticSeverity Severity { get; }
    /// <summary>Gets the original occurrence count.</summary>
    public int Count { get; }
    /// <summary>Gets whether the original diagnostic truncated locations.</summary>
    public bool LocationsTruncated { get; }
    /// <summary>Gets observed phases; source part names and paths are not exposed.</summary>
    public IReadOnlyList<DiagnosticPhase> Phases => phases;
    /// <summary>Gets false: this profile does not expose source locations in outcome findings.</summary>
    public bool SourceLocationsAvailable { get; }
}

public sealed partial class WordConverter
{
    /// <summary>Compiles the documented narrow plain-DOCX-to-PDF compatibility plan.</summary>
    /// <remarks>Only an empty data schema and PDF destination are accepted. Input is consumed from its current position
    /// and remains open. The plan is in-process, uses Strict defaults and bundled fonts, and does not inspect arbitrary
    /// templates or run output validation. See <see cref="TemplateCompatibilityPlan"/> for its exact source limits.</remarks>
    public TemplateCompatibilityPlan CompileTemplateCompatibilityPlan(Stream source, TemplateDataSchema schema,
        DocumentFormat destination, DateTimeOffset fieldTime, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(source, nameof(source)); Guard.NotNull(schema, nameof(schema));
        cancellationToken.ThrowIfCancellationRequested();
        if (schema.Paths.Count != 0) throw new NotSupportedException("Plan.DataProfile: This profile admits only an empty data schema.");
        if (destination != DocumentFormat.Pdf) throw new NotSupportedException("Plan.DestinationProfile: This profile admits only PDF output.");
        return new TemplateCompatibilityPlan(PlainDocxPolicyPlan.Compile(this, source, fieldTime, cancellationToken));
    }
}
