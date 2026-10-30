# Template compatibility plan and loss policy — design for #170

**Design only.** The schemas and examples below do not add a public API or implement
template compilation, policy enforcement or output validation. Roadmap
[#138](https://github.com/kirakosyan/word.net/issues/138) assigns those later stages
to #171 and #172. The separate proposal in
[#99](https://github.com/kirakosyan/word.net/issues/99) remains open until its full
acceptance evidence exists.

## Purpose and boundaries

A plan describes what was inspected and what is known before a caller accepts a
conversion. It is an immutable, bounded snapshot of a particular input, output
profile, data schema and execution environment. It neither runs template data nor
proves that conversion, layout or independent validation will succeed. In
particular, the absence of a diagnostic is not evidence that every desired semantic
is preserved. A result from a later conversion is a separate observation.

The initial implementation boundary for #171 is the admitted DOCX template profile
and existing DOCX/PDF/XPS outputs, with only the owners, data paths, fields, controls,
regions and fragments independently accepted by their prerequisite issues. Unknown
or unqualified combinations remain unknown or fail admission. This design does not
extend any current profile, introduce template evaluation, add a public format pair,
fetch resources, or execute arbitrary caller methods. #172 can enforce a caller's
explicit policy over that bounded plan; it cannot turn an unsupported combination
into a supported one.

## Executable shape and deterministic identity

The proposed [plan schema](design/template-compatibility-plan.schema.json) and
[policy schema](design/template-compatibility-policy.schema.json) use
[JSON Schema 2020-12](https://json-schema.org/draft/2020-12). The [plan](design/template-compatibility-plan.example.json) and
[policy](design/template-compatibility-policy.example.json) are illustrative: their
repeated-digit input/environment hashes are placeholders, not measured artifacts.
The policy's `planDigest` is the SHA-256 of the example plan's canonical bytes.
No document body, attribute value, user name, file path, data value or diagnostic
message is serialized by these default shapes. Hashes and structural locations can
still fingerprint sensitive documents; callers must protect plans and reports.

Canonical bytes use [RFC 8785](https://www.rfc-editor.org/rfc/rfc8785) JSON
canonicalization, UTF-8 and no trailing newline.
Reject duplicate JSON keys, invalid Unicode, unsupported schema versions and unknown
properties. All numbers in version 1 are bounded integers within the interoperable
exact range. Before canonicalization, sort requirements by `pathId`, findings by
`code`, `phase`, `evidence`, then severity, semantic assessments by code, and each
finding's locations by `partId` and numeric child-index sequence. Reject duplicate
requirement IDs, finding keys, semantic assessment codes and locations. Sort policy semantics by ordinal code and allowed losses by code and
location; reject duplicates. The plan digest excludes no fields and is stored in the
policy, not inside the plan. The schema checks structure; these ordering, uniqueness,
stage and policy invariants also require semantic validation. A self-hash is an
identity check, not a signature or proof of provenance.

The six SHA-256 identities bind:

| Identity | Required snapshot |
| --- | --- |
| `input` | Exactly the bounded source bytes read from the caller's initial stream position. |
| `options` | All effective conversion, expansion, fidelity, destination and resource options, including a captured field-clock value where relevant. |
| `dataSchema` | The canonical typed path/owner schema and requiredness, not values. |
| `fonts` | Ordered caller font bytes, fallback policy and any selected local font assets. |
| `providers` | Actual local provider identities and configuration affecting behavior. |
| `engine` | The executing core build and relevant loaded assets/versions. |

The data schema digest is not a digest of supplied data values. A plan can describe
requirements without predicting data-dependent repetition, overflow or output text.
Execution snapshots and bounds actual data separately, reports its own identity when
needed, and compares observed findings with the plan. Changes to any bound identity,
including a font file replaced at the same path, provider/configuration, schema,
sampled clock, effective options or engine, invalidate reuse. If an effective value,
callback, font catalog or provider cannot be snapshotted and identified reliably,
the plan is not reusable; a caller-supplied label alone does not prove equality.
Never hash or serialize credentials as a substitute for safe provider identity.

For test fixtures, retain a separate evidence manifest with fixture/generator hashes,
license/provenance, tools and expected observations. Fixture or output hashes do not
establish fidelity by themselves, and a plan's example hashes are not fixture evidence.

## Requirements, findings and uncertainty

`pathId` is a deterministic opaque alias for a typed path in a caller-owned schema.
The caller keeps the mapping locally; actual names are disclosed only through a
separately requested, clearly sensitive mapping, never by default. The type and owner
are closed values in the schema. `supported` describes admission under the exact
profile; `unsupported` and `unknown` cannot be treated as supported. A required
unknown path, owner or type prevents satisfying the corresponding requirement. No
path lookup invokes arbitrary methods or performs implicit remote access.

A location contains a deterministic opaque `part-0001`-style alias and one-based child indexes;
an empty sequence denotes part-level precision. The caller can resolve the alias in
the same input snapshot; aliases are not stable across edits. Never copy arbitrary
package part names, XML element names or attribute values into this default format.
If location retention is truncated, preserve the count and the truncation flag; do
not fabricate locations or assume that unlisted locations were accepted.

Each finding has a stable machine code, phase, severity, count, locations and a
stable remediation code. Existing diagnostic codes such as `Docx.ThemeFont` retain
their documented meaning; versioned plan codes such as `Plan.UninspectedCoverage`
mark a known gap in inspection. `Remediation.SetExplicitFont` is an example action
identifier, not an automatic repair or a promise that a change is safe. Display text
can be looked up separately in a versioned catalog; it must not contain source values.
The producer emits an explicit unknown whenever it cannot assess required coverage.
It must fail instead of silently dropping findings when its configured count/work
budget is exhausted.

The design's initial example registry is deliberately small:

| Code | Meaning and policy effect |
| --- | --- |
| `content.text` | Required logical text in the admitted source/data profile; a planned `supported` assessment requires complete coverage of the relevant owners and paths. It does not assess presentation or certify written output. |
| `Docx.ThemeFont` | Existing observed loss: theme font semantics are not retained. Explicitly allowing it at one known location does not accept other occurrences. |
| `Plan.UninspectedCoverage` | Proposed unknown coverage; an unknown cannot be accepted as a known loss or used to satisfy a required semantic. |
| `Remediation.SetExplicitFont` | Proposed informational action identifier for the example loss; it never performs a change. |

Future registry additions must version their definitions and tests, preserve the
existing meanings, and specify the affected semantics. Unrecognized codes cannot
be converted into arbitrary messages or accepted by a default policy.

`observed` means that a particular parse, provider, destination check or layout
stage actually ran. `predicted` means a bounded rule anticipates a possible later
finding; `unknown` means the stage or semantic could not be assessed. A parse-only
plan must never mark layout observations as performed. Write and independent
validation are always `not-performed` in a plan; their outcomes belong to the later
execution report. Page/resource estimates are not hard CPU or RSS limits and never
imply successful output. Invalid source, cancellation and resource failures remain
errors rather than successful plans.

## Policy and later observation

The example policy binds one exact plan digest. An allowed loss names both a code and
an exact location; no wildcard or severity-wide downgrade is implied. Unknown codes,
unknown coverage, unlocated/truncated losses and unmatched locations are not silently
accepted. Required semantics come from a versioned closed registry; for example,
`content.text` means preservation of the required logical text, not preservation of
appearance, field refresh, accessibility or every future feature. Each registry entry
must define how it is assessed and which findings or unknowns prevent acceptance.
The plan's `semantics` array records the compiler's bounded assessment for each
requested semantic; `supported` is a pre-execution assessment, not an assertion
that output has already been validated. A missing, unsupported or unknown assessment
rejects the policy. A loss cannot be allowed if it
violates a required semantic. Strict remains strict; a policy cannot override a hard
failure, unsupported source admission, a resource bound or Strict rejection.

Before execution, #172 must compare the live identities with the accepted plan,
validate the explicit policy, and reject stale or inadmissible work before touching
the caller's output. Conversion writes to bounded staging. A later immutable report
must distinguish policy rejection, conversion failure, allowed loss, unexpected
finding and completed conversion. It retains original diagnostic codes, phases,
counts and truncation, and identifies the plan, input, options and output by hashes.
Unexpected write-stage findings are not retroactively predicted by a clean plan.
Independent validation has explicit `not-performed`, `passed` or `failed` status and,
when performed, a validator/version and evidence identity; conversion success alone
cannot set it to passed. A validator result is limited to its stated checks, not a
blanket fidelity or standards guarantee.

Caller streams remain open and are consumed from their current positions. Admission,
policy and staged conversion failures leave output untouched; cancellation or I/O
failure during final copy can leave partial output. No filesystem transaction, hard
process CPU/RSS bound or implicit sharing/telemetry is promised.

## Relationship to existing inspection

`WordConverter.Inspect` and `InspectAsync` already perform bounded parsing and
destination checks, optionally fixed layout, and return `DocumentInspection` with
feature/resource counts, `WouldRejectStrict`, font mappings and grouped
`ConversionDiagnostic` observations. Occurrences include `DiagnosticPhase` and
`SourceLocation`; they are capped at 128 per code and can be truncated. They are
observations of the performed stages, not a complete template execution plan.
Inspect can reject malformed inputs and limits; serialization-only findings and
independent validation are unavailable. #171 must map occurrences without inventing
precision, add bounded typed requirements and explicit unknowns, and keep current
Inspect behavior/API intact. #172 must compare the later conversion's diagnostics
without discarding original phases or counts. No policy or plan API exists yet.

Validate the illustrative schemas and examples with a JSON Schema 2020-12 validator,
including malformed properties, invalid identities, conflicting stages, changed plan
hashes, unknown required semantics, allowed-loss location mismatches and truncated
locations. These checks qualify the design examples only; #171/#172 require their own
independent fixtures, prediction-versus-observation tests and runtime/package gates.
