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
    /// <remarks>This inspection covers ordinary body and bounded direct-table-cell merge fields and simple inline controls, and simple root-repeat paths.
    /// Repeated contents, other controls and owners remain uninspected. Selected PDF option conflicts are checked statically; other output checks, layout and validation remain explicitly unknown. The returned report cannot be reused as a policy plan.
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
        // Only a few PDF option conflicts are assessed for the caller's eventual destination.
        var context = new ConversionContext(options, cancellationToken) { Editing = true, Inspecting = true, CaptureTemplateSources = true, Destination = DocumentFormat.Docx };
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
            DiagnosticSeverity Severity, DiagnosticPhase? Phase, bool Truncated, IReadOnlyList<int>? Indices)>();
        var declarations = schema.Paths.ToDictionary(p => p.Path, StringComparer.Ordinal);
        var observedPaths = new Dictionary<string, (int Count, bool ScopeUnknown)>(StringComparer.Ordinal);
        int work = 0; long locationBytes = 0;
        void Add(string code, string remediation, string? path = null, string? part = null, int count = 1, bool unknown = false,
            DiagnosticSeverity? severity = null, DiagnosticPhase? phase = null, bool truncated = false, IReadOnlyList<int>? indices = null)
        {
            if (findings.Count == 4096) throw new InvalidDataException("Limit.TemplateInspection: Finding limit exceeded.");
            locationBytes = checked(locationBytes + 4L * (indices?.Count ?? 0));
            if (locationBytes > options.MaximumExpandedBytes) throw new InvalidDataException("Limit.TemplateInspection: Source positions exceed the expanded-byte limit.");
            findings.Add((code, remediation, path, part, count, unknown, severity ?? (unknown ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning), phase, truncated, indices));
        }
        void Work()
        {
            context.CheckCancellation();
            if (++work > 100000) throw new InvalidDataException("Limit.TemplateInspection: Inspection work limit exceeded.");
        }
        Add("Plan.UninspectedCoverage", "Remediation.ReviewConversion", unknown: true);
        Add("Plan.ReuseUnavailable", "Remediation.Reinspect", unknown: true);
        if (destination == DocumentFormat.Pdf)
        {
            PdfOptions? pdf = options.Pdf;
            if (pdf == null) Add("Plan.PdfOptionsMissing", "Remediation.ConfigurePdf");
            else
            {
                if (pdf.Conformance is not (PdfConformance.Pdf17 or PdfConformance.PdfA2b or PdfConformance.PdfA2a)
                    || pdf.Accessibility is < PdfAccessibility.None or > PdfAccessibility.PdfUa1
                    || pdf.TextBoxReadingOrder is not (PdfTextBoxReadingOrder.Reject or PdfTextBoxReadingOrder.AnchorOrder)
                    || pdf.VectorSemantics is not (PdfVectorSemantics.Reject or PdfVectorSemantics.DescribedAtAnchor or PdfVectorSemantics.Decorative))
                    Add("Plan.PdfOptionsInvalid", "Remediation.FixOutputProfile");
                if (pdf.Conformance == PdfConformance.PdfA2a && pdf.Accessibility != PdfAccessibility.PdfUa1
                    || pdf.Conformance == PdfConformance.PdfA2b && pdf.Accessibility != PdfAccessibility.None)
                    Add("Plan.PdfTaggedArchiveConflict", "Remediation.FixOutputProfile");
                if (context.Typography.Enabled && pdf.Conformance is PdfConformance.PdfA2a or PdfConformance.PdfA2b)
                    Add("Plan.PdfNativeArchiveConflict", "Remediation.FixOutputProfile");
            }
        }
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
            IReadOnlyList<int>? indices = null; bool located = false;
            void ControlProblem(string code)
            {
                if (!located)
                {
                    located = true;
                    if (control.InspectionPart != null && control.InspectionElement != null)
                        indices = XmlSourceLocation.ChildIndices(control.InspectionElement, Work);
                }
                Add(code, "Remediation.FixControl", part: control.InspectionPart, indices: indices);
            }
            try { _ = WordDocument.Tag(control.Tag); }
            catch (InvalidDataException) { ControlProblem("Plan.InvalidControlPath"); }
            catch (NotSupportedException) { ControlProblem("Plan.InvalidControl"); }
            if (!control.Executable) ControlProblem("Plan.InvalidControlStructure");
        }
        void InspectPath(string path, string? part, string kind, string? formatSwitch, Func<IReadOnlyList<int>?> location, bool rootScope = false)
        {
            bool present = observedPaths.TryGetValue(path, out var prior);
            if (!present && observedPaths.Count == 4096) throw new InvalidDataException("Limit.TemplateInspection: Requirement limit exceeded.");
            observedPaths[path] = (checked(prior.Count + 1), prior.ScopeUnknown || scoped && !rootScope);
            if (scoped && !rootScope) return;
            if (!declarations.TryGetValue(path, out TemplateSchemaPath? declared))
            { Add("Plan.MissingPath", "Remediation.DeclarePath", path, part, indices: location()); return; }
            if (!declared.Required) Add("Plan.OptionalPath", "Remediation.RequirePath", path, part, unknown: true, indices: location());
            bool unknownType = kind == "field" && formatSwitch != null && declared.Type == TemplatePathType.Scalar;
            bool compatible = kind switch
            {
                "if" => true,
                "image" => declared.Type == TemplatePathType.Image,
                "repeat" => declared.Type == TemplatePathType.Collection,
                _ => formatSwitch switch
                {
                    "\\@" => declared.Type == TemplatePathType.Date,
                    "\\#" => declared.Type == TemplatePathType.Number,
                    _ => declared.Type is TemplatePathType.Scalar or TemplatePathType.Number or TemplatePathType.Date,
                },
            };
            if (unknownType) Add("Plan.PathTypeUnknown", "Remediation.DeclarePathType", path, part, unknown: true, indices: location());
            else if (!compatible) Add("Plan.PathType", "Remediation.FixPathType", path, part, indices: location());
            string[] segments = path.Split('.');
            for (int i = 1; i < segments.Length; i++)
            {
                Work(); string parent = string.Join(".", segments.Take(i));
                if (!declarations.TryGetValue(parent, out TemplateSchemaPath? ancestor))
                    Add("Plan.ParentTypeUnknown", "Remediation.DeclareParent", parent, part, unknown: true, indices: location());
                else if (ancestor.Type != TemplatePathType.Mapping)
                    Add("Plan.ParentType", "Remediation.FixPathType", parent, part, indices: location());
                else if (!ancestor.Required) Add("Plan.OptionalParent", "Remediation.RequirePath", parent, part, unknown: true, indices: location());
            }
        }
        bool otherOwners = model.Blocks.Any(b => b is not ParagraphBlock)
            || model.Notes.Count != 0 || model.Blocks.Select(b => b.Section).Concat(new[] { model.FinalSection })
                .Where(s => s != null).Any(s => s!.Stories.Any(story => story.Value.Count != 0));
        var paragraphs = model.Blocks.OfType<ParagraphBlock>().ToList();
        var rootParagraphs = new HashSet<ParagraphBlock>();
        if (scoped)
        {
            // A repeat elsewhere does not change the scope of a direct body paragraph.
            // Do not infer that for paragraphs flattened from wrappers or other owners.
            const string prefix = "/document[1]/body[1]/p[";
            foreach (ParagraphBlock paragraph in paragraphs)
            {
                Work(); SourceLocation? location = paragraph.Location;
                string path = location?.Path ?? "";
                if (location?.Part == "word/document.xml" && path.StartsWith(prefix, StringComparison.Ordinal)
                    && path.Length > prefix.Length + 1 && path[path.Length - 1] == ']' && path[prefix.Length] is >= '1' and <= '9'
                    && !path.Substring(prefix.Length + 1, path.Length - prefix.Length - 2).Any(character => character is < '0' or > '9'))
                    rootParagraphs.Add(paragraph);
            }
        }
        foreach (TableBlock table in model.Blocks.OfType<TableBlock>())
        {
            Work();
            SourceLocation? location = table.Location;
            const string prefix = "/document[1]/body[1]/tbl[";
            string path = location?.Path ?? "";
            if (location?.Part != "word/document.xml" || !path.StartsWith(prefix, StringComparison.Ordinal)
                || path.Length <= prefix.Length + 1 || path[path.Length - 1] != ']' || path[prefix.Length] is < '1' or > '9'
                || path.Substring(prefix.Length + 1, path.Length - prefix.Length - 2).Any(character => character is < '0' or > '9')
                || table.Control != null || table.NestedControl != null || table.TypedControlId.HasValue
                || table.RowControls.Count != 0 || table.TypedRowControls.Count != 0 || table.RowRevisions.Count != 0
                || table.Rows.Count == 0 || table.Rows[0].Count == 0) continue;
            var candidates = new List<ParagraphBlock>(); var seen = new HashSet<ParagraphBlock>(); bool simple = true;
            for (int row = 0; row < table.Rows.Count && simple; row++)
            {
                Work();
                if (table.Rows[row].Count != table.Rows[0].Count) { simple = false; break; }
                for (int column = 0; column < table.Rows[row].Count && simple; column++)
                {
                    Work(); TableCell cell = table.Rows[row][column];
                    if (cell.ColumnSpan != 1 || cell.Merge != VerticalMerge.None || cell.TypedControlId.HasValue) { simple = false; break; }
                    string cellPath = path + "/tr[" + (row + 1).ToString(CultureInfo.InvariantCulture)
                        + "]/tc[" + (column + 1).ToString(CultureInfo.InvariantCulture) + "]";
                    int index = 0;
                    foreach (DocumentBlock block in cell.Blocks)
                    {
                        Work();
                        if (block is not ParagraphBlock item) { simple = false; break; }
                        SourceLocation? paragraphLocation = item.Location;
                        if (item.Control != null || item.NestedControl != null || item.TypedControlId.HasValue
                            || item.StructuralRevision != null || item.ParagraphFormatRevision != null || !seen.Add(item)
                            || paragraphLocation == null || paragraphLocation.Part != location.Part
                            || paragraphLocation.Path != cellPath + "/p[" + (index + 1).ToString(CultureInfo.InvariantCulture) + "]")
                        { simple = false; break; }
                        foreach (TextRun run in item.Runs)
                        {
                            Work();
                            if (run.Control != null && (run.Control.InspectionElement == null || run.Control.InspectionRootRepeat)
                                || run.TypedControlId.HasValue || run.Note != null || run.Box != null || run.Object != null || run.Link != null
                                || run.Image != null || run.Bookmark != null || run.Revision != null || run.FormatRevision != null || run.Comment != null)
                            { simple = false; break; }
                            if (run.FieldCache == null) continue;
                            foreach (TextRun cached in run.FieldCache)
                            {
                                Work();
                                if (cached.Control != null || cached.TypedControlId.HasValue || cached.Note != null || cached.Box != null || cached.Object != null
                                    || cached.Image != null || cached.Bookmark != null || cached.FieldInstruction != null || cached.Link != null
                                    || cached.Revision != null || cached.FormatRevision != null || cached.Comment != null)
                                { simple = false; break; }
                            }
                            if (!simple) break;
                        }
                        if (!simple) break;
                        candidates.Add(item);
                        index++;
                    }
                    if (index == 0) simple = false;
                }
            }
            if (simple)
            {
                paragraphs.AddRange(candidates);
                if (scoped) rootParagraphs.UnionWith(candidates);
            }
        }
        var inspectedControls = new HashSet<TemplateControl>();
        foreach (ParagraphBlock paragraph in paragraphs)
        {
            Work();
            if (paragraph.Control != null || paragraph.NestedControls.Count != 0)
            {
                otherOwners = true;
                TemplateControl? control = paragraph.Control;
                var controlElement = control?.InspectionElement;
                if (control == null || controlElement == null || !control.InspectionRootRepeat || !control.Executable
                    || paragraph.NestedControls.Count != 0 || !inspectedControls.Add(control)) continue;
                (string Kind, string Path) tag;
                try { tag = WordDocument.Tag(control.Tag); }
                catch (InvalidDataException) { continue; }
                catch (NotSupportedException) { continue; }
                if (tag.Kind != "repeat") continue;
                IReadOnlyList<int>? repeatIndices = null; bool repeatLocated = false;
                IReadOnlyList<int>? RepeatLocation()
                {
                    if (!repeatLocated)
                    {
                        repeatLocated = true;
                        if (paragraph.Location?.Part != null) repeatIndices = XmlSourceLocation.ChildIndices(controlElement, Work);
                    }
                    return repeatIndices;
                }
                InspectPath(tag.Path, paragraph.Location?.Part, "repeat", null, RepeatLocation, rootScope: true);
                continue;
            }
            bool rootScope = !scoped || rootParagraphs.Contains(paragraph);
            foreach (TextRun run in paragraph.Runs)
            {
                Work();
                if (run.Control != null)
                {
                    otherOwners = true;
                    TemplateControl control = run.Control;
                    var controlElement = control.InspectionElement;
                    if (!rootScope || controlElement == null || !control.Executable || !inspectedControls.Add(control)) continue;
                    (string Kind, string Path) tag;
                    try { tag = WordDocument.Tag(control.Tag); }
                    catch (InvalidDataException) { continue; }
                    catch (NotSupportedException) { continue; }
                    if (tag.Kind == "repeat") continue;
                    string path = tag.Kind == "if" && tag.Path.Length != 0 && tag.Path[0] == '!' ? tag.Path.Substring(1) : tag.Path;
                    IReadOnlyList<int>? controlIndices = null; bool controlLocated = false;
                    IReadOnlyList<int>? ControlLocation()
                    {
                        if (!controlLocated)
                        {
                            controlLocated = true;
                            if (paragraph.Location?.Part != null) controlIndices = XmlSourceLocation.ChildIndices(controlElement, Work);
                        }
                        return controlIndices;
                    }
                    InspectPath(path, paragraph.Location?.Part, tag.Kind, null, ControlLocation, rootScope);
                    continue;
                }
                if (run.Box != null || run.Note != null) { otherOwners = true; continue; }
                if (run.FieldInstruction == null) continue;
                string? part = paragraph.Location?.Part;
                IReadOnlyList<int>? indices = null; bool located = false;
                IReadOnlyList<int>? Location()
                {
                    if (!located)
                    {
                        located = true;
                        if (part != null && run.TemplateFieldElement != null) indices = XmlSourceLocation.ChildIndices(run.TemplateFieldElement, Work);
                    }
                    return indices;
                }
                if (DocumentFields.Kind(run.FieldInstruction) != "MERGEFIELD")
                { Add("Plan.FieldCoverage", "Remediation.ReviewFields", part: part, unknown: true, indices: Location()); continue; }
                if (run.FieldChildren?.Count > 0)
                { Add("Plan.FieldCoverage", "Remediation.ReviewFields", part: part, unknown: true, indices: Location()); continue; }
                (string Path, string? Format, string? Switch) field;
                try { field = WordDocument.MergeField(run.FieldInstruction); }
                catch (InvalidDataException) { Add("Plan.InvalidField", "Remediation.FixField", part: part, indices: Location()); continue; }
                InspectPath(field.Path, part, "field", field.Switch, Location, rootScope);
            }
        }
        if (otherOwners) Add("Plan.OwnerCoverage", "Remediation.ReviewOwners", unknown: true);
        context.CheckCancellation();
        Dictionary<string, string> pathIds = Aliases(findings.Select(f => f.Path).Concat(observedPaths.Keys), "path");
        Dictionary<string, string> partIds = Aliases(findings.Select(f => f.Part), "part");
        var result = findings.Select(f => new TemplateInspectionFinding(f.Code, f.Remediation,
            f.Path == null ? null : pathIds[f.Path], f.Part == null ? null : partIds[f.Part], f.Count, f.Unknown,
            f.Severity, f.Phase, f.Truncated, f.Indices)).ToList();
        context.CheckCancellation();
        var requirements = observedPaths.OrderBy(pair => pathIds[pair.Key], StringComparer.Ordinal).Select(pair =>
        {
            TemplateSchemaPath? declared = null;
            if (!pair.Value.ScopeUnknown) declarations.TryGetValue(pair.Key, out declared);
            return new TemplateInspectionRequirement(pathIds[pair.Key], declared?.Type, declared?.Required, pair.Value.ScopeUnknown, pair.Value.Count);
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
