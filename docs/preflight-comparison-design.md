# Preflight and comparison decisions

Issues #6 and #24 extend the existing managed conversion pipeline and test tools.

Preflight is an additive `WordConverter.Inspect/InspectAsync` API, not an addition
to `IDocumentConverter` (which would break implementers). It consumes bounded input
from the current stream position, leaves the stream open, parses with the same
adapters and runs destination fidelity checks. Inspection collects Strict losses
instead of throwing on the first loss, following the BestEffort fallback path.
The Strict-rejection flag also tracks glyph and geometry fallback conditions. Corruption, unsupported formats/providers,
limits and cancellation still fail. Optional fixed-page layout uses the same layout
engine without serializing output. Parse-only inspection cannot predict writer,
provider or layout findings; a clean report never proves lossless conversion.

Grouped diagnostics retain Code/Message/Severity/Count. Add bounded, deduplicated
occurrences with phase and source location. DOCX locations use part and indexed
element paths, without text, attribute values or caller file paths. Blocks carry
these locations into destination checks and layout. Other adapters use document-level parsing findings and explicitly identified logical
model paragraph/table-cell/story paths for destination and layout findings. Locations identify an
unchanged input, not an identity across edits. Paths materialize on observation, and a bounded hash set deduplicates locations.
Location storage is capped per code;
Count remains the total observation count, including multi-pass layout.

The comparison tool stays in the non-packable benchmark project. A deterministic,
independently authored twelve-file corpus spans invoices, contracts, reports and
letterheads, including known unsupported features. Commercial assemblies/fonts and
licenses remain caller-provisioned and outside Git. Each input is measured in a
fresh worker for cold conversion and repeated in that worker for warm samples.
Independent PDF extraction/raster comparison happens outside measured conversion.
Reference provenance distinguishes Word, Aspose and test-only raster baselines;
evaluation artifacts are recorded but excluded from defect verdicts. Metrics are
separate, with no combined quality score. Existing CI gates remain mandatory. The older revision performance harness excludes
the new comparison worker when `WordnetSourceProject` selects an earlier core API.
