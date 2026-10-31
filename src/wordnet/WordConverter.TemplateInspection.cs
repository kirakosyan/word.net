using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet;

public sealed partial class WordConverter
{
    /// <summary>Inspects a bounded DOCX template and a declaration of data paths without binding values or writing output.</summary>
    /// <remarks>This first inspection covers ordinary body merge fields only. Controls, repeated scopes, other owners,
    /// output checks, layout and validation remain explicitly unknown. The returned report cannot be reused as a policy plan.
    /// Source is consumed from its current position and left open. Schema names are never included in default findings.</remarks>
    public TemplateInspection InspectTemplate(Stream source, TemplateDataSchema schema, DocumentFormat destination,
        ConversionOptions? options = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(source, nameof(source)); Guard.NotNull(schema, nameof(schema));
        if (!source.CanRead) throw new ArgumentException("The source must be readable.", nameof(source));
        if (destination is not (DocumentFormat.Docx or DocumentFormat.Pdf or DocumentFormat.Xps)
            || destination != DocumentFormat.Docx && !CanConvert(DocumentFormat.Docx, destination)) throw new ArgumentOutOfRangeException(nameof(destination));
        options ??= new ConversionOptions();
        // Template binding begins from WordDocument.Load, whose reader destination is DOCX.
        // The caller's requested eventual destination is not assessed in this snapshot.
        var context = new ConversionContext(options, cancellationToken) { Editing = true, Inspecting = true, Destination = DocumentFormat.Docx };
        context.CheckCancellation();
        using var input = new BoundedMemoryStream(options.MaximumInputBytes);
        Copy(source, input, context, options.MaximumInputBytes);
        input.Position = 0;
        string inputHash;
        using (var algorithm = SHA256.Create())
        {
            byte[] hash = algorithm.ComputeHash(input);
#if NET10_0_OR_GREATER
            inputHash = System.Convert.ToHexStringLower(hash);
#elif NET8_0_OR_GREATER
            inputHash = System.Convert.ToHexString(hash).ToLowerInvariant();
#else
            inputHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
#endif
        }
        context.CheckCancellation();
        DocumentModel model = ReadDocument(input, DocumentFormat.Docx, context);
        WordDocument.Admit(model, context);
        var findings = new List<(string Code, string Remediation, string? Path, string? Part, int Count, bool Unknown,
            DiagnosticSeverity Severity, DiagnosticPhase? Phase, bool Truncated)>();
        var declarations = schema.Paths.ToDictionary(p => p.Path, StringComparer.Ordinal);
        var observedPaths = new Dictionary<string, int>(StringComparer.Ordinal);
        int work = 0;
        void Add(string code, string remediation, string? path = null, string? part = null, int count = 1, bool unknown = false,
            DiagnosticSeverity? severity = null, DiagnosticPhase? phase = null, bool truncated = false)
        {
            if (findings.Count == 4096) throw new InvalidDataException("Limit.TemplateInspection: Finding limit exceeded.");
            findings.Add((code, remediation, path, part, count, unknown, severity ?? (unknown ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning), phase, truncated));
        }
        void Work()
        {
            context.CheckCancellation();
            if (++work > 100000) throw new InvalidDataException("Limit.TemplateInspection: Inspection work limit exceeded.");
        }
        Add("Plan.UninspectedCoverage", "Remediation.ReviewConversion", unknown: true);
        Add("Plan.ReuseUnavailable", "Remediation.Reinspect", unknown: true);
        foreach (ConversionDiagnostic diagnostic in context.Result(0).Diagnostics)
        {
            Work();
            string[] parts = diagnostic.Occurrences.Select(o => o.Location.Part).Distinct(StringComparer.Ordinal).ToArray();
            DiagnosticPhase[] phases = diagnostic.Occurrences.Select(o => o.Phase).Distinct().ToArray();
            bool truncated = diagnostic.LocationsTruncated || diagnostic.Occurrences.Count != 1 || parts.Length != 1 || phases.Length != 1;
            Add(diagnostic.Code, "Remediation.ReviewDiagnostic", part: parts.Length == 1 ? parts[0] : null, count: diagnostic.Count,
                severity: diagnostic.Severity, phase: phases.Length == 1 ? phases[0] : null, truncated: truncated);
            if (truncated) Add("Plan.LocationCoverage", "Remediation.ReviewSource", unknown: true);
        }
        if (model.Controls.HasControls || model.Review.HasHistory || model.ObjectCount != 0)
            Add("Plan.ProtectedOwner", "Remediation.ReviewOwners", unknown: true);
        TemplateControl[] controls = WordDocument.TemplateControls(model).Distinct().ToArray();
        bool scoped = controls.Any(c => c.Tag.StartsWith("repeat:", StringComparison.Ordinal));
        if (controls.Length != 0) Add("Plan.ControlCoverage", "Remediation.ReviewControls", count: controls.Length, unknown: true);
        if (scoped) Add("Plan.RepeatScope", "Remediation.ReviewScopes", unknown: true);
        foreach (TemplateControl control in controls)
        {
            Work();
            try { _ = WordDocument.Tag(control.Tag); }
            catch (InvalidDataException) { Add("Plan.InvalidControlPath", "Remediation.FixControl"); }
            catch (NotSupportedException) { Add("Plan.InvalidControl", "Remediation.FixControl"); }
            if (!control.Executable) Add("Plan.InvalidControlStructure", "Remediation.FixControl");
        }
        bool otherOwners = model.Blocks.Any(b => b is not ParagraphBlock)
            || model.Notes.Count != 0 || model.Blocks.Select(b => b.Section).Concat(new[] { model.FinalSection })
                .Where(s => s != null).Any(s => s!.Stories.Any(story => story.Value.Count != 0));
        foreach (ParagraphBlock paragraph in model.Blocks.OfType<ParagraphBlock>())
        {
            Work();
            if (paragraph.Control != null || paragraph.NestedControls.Count != 0) { otherOwners = true; continue; }
            foreach (TextRun run in paragraph.Runs)
            {
                Work();
                if (run.Control != null || run.Box != null || run.Note != null) { otherOwners = true; continue; }
                if (run.FieldInstruction == null) continue;
                string? part = paragraph.Location?.Part;
                if (DocumentFields.Kind(run.FieldInstruction) != "MERGEFIELD")
                { Add("Plan.FieldCoverage", "Remediation.ReviewFields", part: part, unknown: true); continue; }
                if (run.FieldChildren?.Count > 0)
                { Add("Plan.FieldCoverage", "Remediation.ReviewFields", part: part, unknown: true); continue; }
                (string Path, string? Format, string? Switch) field;
                try { field = WordDocument.MergeField(run.FieldInstruction); }
                catch (InvalidDataException) { Add("Plan.InvalidField", "Remediation.FixField", part: part); continue; }
                bool present = observedPaths.TryGetValue(field.Path, out int count);
                if (!present && observedPaths.Count == 4096) throw new InvalidDataException("Limit.TemplateInspection: Requirement limit exceeded.");
                observedPaths[field.Path] = checked(count + 1);
                if (scoped) continue;
                if (!declarations.TryGetValue(field.Path, out TemplateSchemaPath? declared))
                { Add("Plan.MissingPath", "Remediation.DeclarePath", field.Path, part); continue; }
                if (!declared.Required) Add("Plan.OptionalPath", "Remediation.RequirePath", field.Path, part, unknown: true);
                bool unknownType = field.Switch != null && declared.Type == TemplatePathType.Scalar;
                bool compatible = field.Switch switch
                {
                    "\\@" => declared.Type == TemplatePathType.Date,
                    "\\#" => declared.Type == TemplatePathType.Number,
                    _ => declared.Type is TemplatePathType.Scalar or TemplatePathType.Number or TemplatePathType.Date,
                };
                if (unknownType) Add("Plan.PathTypeUnknown", "Remediation.DeclarePathType", field.Path, part, unknown: true);
                else if (!compatible) Add("Plan.PathType", "Remediation.FixPathType", field.Path, part);
                string[] segments = field.Path.Split('.');
                for (int i = 1; i < segments.Length; i++)
                {
                    Work(); string parent = string.Join(".", segments.Take(i));
                    if (!declarations.TryGetValue(parent, out TemplateSchemaPath? ancestor))
                        Add("Plan.ParentTypeUnknown", "Remediation.DeclareParent", parent, part, unknown: true);
                    else if (ancestor.Type != TemplatePathType.Mapping)
                        Add("Plan.ParentType", "Remediation.FixPathType", parent, part);
                    else if (!ancestor.Required) Add("Plan.OptionalParent", "Remediation.RequirePath", parent, part, unknown: true);
                }
            }
        }
        if (otherOwners) Add("Plan.OwnerCoverage", "Remediation.ReviewOwners", unknown: true);
        context.CheckCancellation();
        Dictionary<string, string> pathIds = Aliases(findings.Select(f => f.Path).Concat(observedPaths.Keys), "path");
        Dictionary<string, string> partIds = Aliases(findings.Select(f => f.Part), "part");
        var result = findings.Select(f => new TemplateInspectionFinding(f.Code, f.Remediation,
            f.Path == null ? null : pathIds[f.Path], f.Part == null ? null : partIds[f.Part], f.Count, f.Unknown,
            f.Severity, f.Phase, f.Truncated)).ToList();
        context.CheckCancellation();
        var requirements = observedPaths.OrderBy(pair => pathIds[pair.Key], StringComparer.Ordinal).Select(pair =>
        {
            TemplateSchemaPath? declared = null;
            if (!scoped) declarations.TryGetValue(pair.Key, out declared);
            return new TemplateInspectionRequirement(pathIds[pair.Key], declared?.Type, declared?.Required, scoped, pair.Value);
        }).ToList();
        return new TemplateInspection(inputHash, destination, result, requirements,
            pathIds.ToDictionary(p => p.Value, p => p.Key, StringComparer.Ordinal),
            partIds.ToDictionary(p => p.Value, p => p.Key, StringComparer.Ordinal));
    }

    private static Dictionary<string, string> Aliases(IEnumerable<string?> names, string prefix)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal); int index = 0;
        foreach (string name in names.Where(n => n != null).Select(n => n!).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal))
        {
            if (index == 4096) throw new InvalidDataException("Limit.TemplateInspection: Alias limit exceeded.");
            result.Add(name, prefix + "-" + (++index).ToString("D4", CultureInfo.InvariantCulture));
        }
        return result;
    }
}
