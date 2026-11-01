# Creating, editing and filling documents

## 1.5.0 roadmap #102 delivery

The [release notes](releases/1.5.0.md), [capability matrix](roadmap-102-capability-matrix.json)
and [qualification record](roadmap-102-p6-qualification.md) describe the P1–P6 batch.
The bounded fields, controls, reports, review and drawing additions ship together;
all five target frameworks and existing platform limits remain. Historical results
below retain their original source/package identities.

The [compatibility-plan design](template-compatibility-plan-design.md) describes
typed requirements and loss policies. The bounded plain-DOCX profile below implements
only a small subset; general template-plan compilation and enforcement remain open.

<a id="unreleased-source-bounded-template-inspection"></a>
## 1.6.0 profile: bounded template inspection

The 1.6.0 #171 profile adds `WordConverter.InspectTemplate` for a DOCX and an explicitly
declared `TemplateDataSchema`. It parses using the same editable DOCX context as
`WordDocument.Load`; the requested destination is recorded, with only the selected
static PDF option checks described below.
It checks ordinary body and direct simple-table-cell `MERGEFIELD` paths and
date/number switches without binding values, calling data getters or writing output.
Declare each dotted parent as a `Mapping`; missing, optional, incorrectly typed or
undeclared parents produce findings. A scalar with an unspecified precise type is
unknown for a date or number switch. Declarations are a caller promise, not a check
that a later data object actually contains a value.

The inspection also checks simple direct body-paragraph and admitted simple-cell
`merge:`, `image:` and `if:` inline controls. These root-owned controls and fields
remain inspectable when a separate repeat occurs elsewhere in the document. The
admitted control has only `tag`, optional `id`/`alias` metadata and one plain text run; it
checks presence, parent mappings and scalar/image type as applicable. An `if:`
control accepts any declared value type and `!` negates the condition. Formatted,
nested, merged-cell, story and other controls remain uninspected. Even when a simple
control is checked, `Plan.ControlCoverage` remains unknown for the whole document.

A directly owned body `repeat:` control containing one paragraph with one plain
text run or one simple field has an unambiguous root collection path. Inspection
checks that path and its declared `Collection` type without evaluating items or
expanding the repeat. A simple `MERGEFIELD` inside that admitted repeat produces
an opaque requirement and a numeric source position for any finding. The actual
merge can select a value from the item or fall back to a parent/root value. By
default its type, presence and lookup scope remain unknown. To declare that every
item in a root collection has a path, use
`TemplateSchemaPath.ForRepeatItem("Items", "Name", TemplatePathType.Scalar)`.
A required item declaration lets inspection identify that item's scope and type
if the field executes. It is a caller promise, not a data check or a guarantee
that the collection is nonempty. For a dotted item path, declare every parent in
the same repeat scope as a required `Mapping`, for example both `Customer` and
`Customer.Name`. A missing, optional or non-mapping parent produces an unknown
`Plan.RepeatFieldParentUnknown`, `Plan.RepeatFieldParentOptional` or
`Plan.RepeatFieldParentTypeMismatch`, respectively, as well as
`Plan.RepeatFieldScopeUnknown`. An optional or missing item declaration and
other repeat contents also retain unknown scope.
An incompatible declared item type produces unknown `Plan.RepeatFieldTypeMismatch`,
because an empty collection may never evaluate the field. A root field with the
same path has a separate requirement using the same path alias; `ScopePathId`
identifies the opaque collection alias and their counts do not combine. The
collection owner can be known while `ScopeUnknown` remains true for lookup.
An unparseable or non-merge field instead
produces unknown `Plan.FieldCoverage`, since an empty collection may never execute it.
Multi-paragraph, nested, table and other repeats stay uninspected, and
`Plan.RepeatScope` remains unknown for all repeated contents.

An explicitly true `w:dirty` attribute on an inspected direct simple field or a
bounded, same-paragraph complex-field begin marker produces unknown
`Plan.FieldMarkedDirty`, with a numeric source position and `Remediation.RefreshField`.
For complex fields the position identifies the begin marker, not the full field.
An unrecognized value produces unknown `Plan.FieldDirtyUnknown`. This records a
request to refresh; inspection does not evaluate the field or establish that its
cached value is stale. A missing or false flag is not proof of freshness.
Nested simple fields, cross-paragraph, locked, nested and repeated complex fields
and other uninspected owners retain their existing unknown coverage.

`TemplateDataSchema.InspectJson` separately checks a JSON object against every
declared root and item path, including declarations unused by a particular
template. A required item path must exist on each item even when an eventual
merge could use a root fallback; an optional missing item path is admitted. A
missing collection leaves its item assessment unknown, and a non-array collection
is a definite mismatch. Null is admitted as an empty value, including for an
empty repeat. JSON strings are not converted to dates or images: a non-null value
cannot satisfy `Date` or `Image` declarations through this JSON-only API. These
checks describe the caller's declarations, not whether a particular merge will
succeed; an unformatted field, for example, can accept a string even when a
`Number` declaration reports a mismatch. No template, caller property getter,
field clock or output is evaluated.

For a PDF destination, inspection flags missing PDF options, invalid conformance,
accessibility, reading-order or vector-semantics enum values, plus PDF/A-2a
without PDF/UA-1, tagged PDF/A-2b, and native typography with archival PDF.
These are static predictions from the supplied
options, with no observed write phase. They do not run a conversion or check
page selections, document-dependent semantics, font resources, layout, writing or validation;
`Plan.UninspectedCoverage` remains unknown. Options can change after inspection,
and this report cannot authorize an output policy.

### Fixed-profile in-process inspection reuse

`CompileReusableTemplateInspection` captures the existing inspection for a fixed DOCX,
PDF or XPS destination under default bounds, built-in fonts, no optional provider and
a caller-supplied fixed field time. The convenience overload selects DOCX. It binds
the source bytes, declaration, destination, fixed options and clock, loaded
font resource bytes, provider profile and module/runtime descriptor. `Reuse` checks
the current schema before reading, then hashes the current source from its current
position with the same 16 MiB input bound. It rechecks the in-process environment
and returns the same immutable snapshot only when the identities match. Streams remain
open; cancellation and limit failures are explicit. The snapshot is valid only in the
same process and is not a serialized plan. Hashes can identify documents or reveal
guessed schema names; protect them as sensitive metadata. The module/runtime
descriptor is not binary attestation and cannot detect code patches that retain the
same module identity.

```csharp
using var template = File.OpenRead("invoice.docx");
var clock = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
ReusableTemplateInspection snapshot = new WordConverter()
    .CompileReusableTemplateInspection(template, schema, DocumentFormat.Pdf, clock);
template.Position = 0;
TemplateInspection checkedReport = snapshot.Reuse(template, schema);
```

The chosen destination is recorded and contributes to the profile fingerprint; it
is not rendered or validated. Only an inspection snapshot is reused. Findings and
unknowns are unchanged, and
`TemplateInspection.IsReusable` remains false: `Plan.ReuseUnavailable` continues to
mean that a reusable conversion-policy plan is unavailable. The snapshot does not
bind data, authorize conversion, enforce a loss policy, validate output or permit
arbitrary caller options, fonts or providers.

<a id="unreleased-source-strict-plain-docx-policy"></a>
### 1.6.0 profile: strict plain DOCX policy

`CompileTemplateCompatibilityPlan` supports only a minimal DOCX package with one
body paragraph, one text run and 1–40 ASCII letters or digits. Other content,
formatting, styles, fields, controls, relationships and extra parts are rejected;
an empty document relationship part is permitted. It accepts only an empty data
schema and a PDF destination, using strict defaults, bundled fonts, no optional
providers and a fixed caller-supplied clock. It checks the source, destination and
one-page layout, but does not write or independently validate output when compiling.
This deliberately small profile does not fill a template or accept any loss.

```csharp
var converter = new WordConverter();
var clock = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
using var source = File.OpenRead("minimal.docx");
TemplateCompatibilityPlan plan = converter.CompileTemplateCompatibilityPlan(
    source, new TemplateDataSchema(), DocumentFormat.Pdf, clock);
string policy = "{\"schemaVersion\":\"1.0\",\"planDigest\":\"" + plan.GetIdentitySha256()
    + "\",\"requiredSemantics\":[\"content.text\"],\"allowedLosses\":[]}";
source.Position = 0;
using var output = new MemoryStream();
TemplatePolicyOutcome outcome = plan.Execute(source, output, policy);
if (outcome.State != "completed") throw new InvalidOperationException(outcome.Code);
// Independently validate or persist output according to the caller's requirements.
```

The policy and plan are version-1 canonicalizable data, but only the original
in-process plan object authorizes execution. Its checked runtime/module descriptor
is not binary attestation, and its hashes can fingerprint sensitive input. The
source is read from its current position, checked again against the plan, and
converted into private staging storage before copying to the caller's current
destination position. Both streams remain open. Rejected policies, changed source
or environment and staged conversion failures leave the destination untouched.
Final-copy I/O or cancellation can leave partial output; the output hash identifies
the staged bytes handed to the stream, not durable storage. The outcome explicitly
reports independent validation as `not-performed`. Strict loss exceptions currently
report a conversion failure rather than a complete write-stage diagnostic; general
loss permissions, output validation and policy execution remain unfinished.

An explicit `FidelityMode.BestEffort` overload admits two additional minimal
single-run sources: one `minorHAnsi` theme-font reference with no theme package,
or one internal hyperlink with an alphanumeric anchor. The former plans one
observed `Docx.ThemeFont` loss at the run and requires a permission for exactly
that code and location. It uses the bundled fallback font, so the original theme
appearance is not guaranteed. For the latter, the source contains no bookmark, so
the plan marks `content.links` unsupported. A policy requiring link destinations
is rejected before reading the execution input. If only `content.text` is required,
writing observes `Pdf.OmittedDestination` and rejects the staged output as an
unexpected finding. Other formatting,
relationships, themes and link constructs remain outside the profile.

```csharp
using var source = File.OpenRead("minimal-theme.docx");
var plan = new WordConverter().CompileTemplateCompatibilityPlan(
    source, new TemplateDataSchema(), DocumentFormat.Pdf, clock, FidelityMode.BestEffort);
string policy = "{\"schemaVersion\":\"1.0\",\"planDigest\":\"" + plan.GetIdentitySha256()
    + "\",\"requiredSemantics\":[\"content.text\"],\"allowedLosses\":["
    + "{\"code\":\"Docx.ThemeFont\",\"location\":{\"partId\":\"part-0001\",\"childIndexes\":[1,1,1,1]}}]}";
source.Position = 0;
using var output = new MemoryStream();
TemplatePolicyOutcome result = plan.Execute(source, output, policy);
if (result.State != "allowed-loss") throw new InvalidOperationException(result.Code);
```

BestEffort is an explicit choice; Strict never accepts this loss. The observed
diagnostic must still match the planned code, location, phase and count before
output is copied. A missing or mismatched permission fails before reading the
source. `content.links` is otherwise unassessed; this does not establish general
link support. Findings in the outcome expose codes, phases and counts without source
paths or messages. These controls do not implement arbitrary loss permissions,
template-data binding or independent validation.

The table subset is a direct `word/document.xml` body table with a rectangular
grid, unmerged cells and ordinary, directly owned paragraphs. Nested or wrapped
tables and rows, merged cells, non-admitted controls, revision-bearing owners, links, notes,
images and other structured cell content are outside this subset. If any table is
present, `Plan.OwnerCoverage` remains unknown even when some simple-cell fields
are inspected; the report does not assert a complete table inventory. Repeated
contents and flattened wrapper scopes continue to make path resolution unknown.

```csharp
var schema = new TemplateDataSchema(
    new TemplateSchemaPath("Customer", TemplatePathType.Mapping),
    new TemplateSchemaPath("Customer.Name", TemplatePathType.Scalar),
    new TemplateSchemaPath("Total", TemplatePathType.Number));
using var input = File.OpenRead("invoice.docx");
TemplateInspection report = new WordConverter().InspectTemplate(
    input, schema, DocumentFormat.Pdf);
```

For a separate JSON check, use the same declarations and examine both errors
and unknowns before deciding what further validation is needed:

```csharp
var itemSchema = new TemplateDataSchema(
    new TemplateSchemaPath("Items", TemplatePathType.Collection),
    TemplateSchemaPath.ForRepeatItem("Items", "Name", TemplatePathType.Scalar));
TemplateDataInspection dataReport = itemSchema.InspectJson(json);
bool needsReview = dataReport.HasErrors || dataReport.HasUnknowns;
```

The default JSON report contains opaque path aliases, codes, zero-based item
indices and certainty only. Indices can reveal data structure. Calling
`GetSensitivePathAliases()` explicitly reveals the declaration names; aliases are
local to each report and must not be assumed to match another inspection. JSON,
array size, depth, expansion, alias, finding and work bounds apply; malformed or
oversized input and cancellation fail without a partial report.

`TemplateDataSchema.IdentitySha256` identifies the declaration set; both reports
expose the same value as `DataSchemaSha256`. The version-1 identity hashes UTF-8
canonical JSON with outer properties `paths` and `schemaVersion`. Each path entry
has ordinally ordered `path`, `required`, `scope` and numeric `type` properties;
root paths precede repeat-item paths, then entries sort by scope and path. The
scope is null for a root declaration. The hash is insensitive to declaration
order and reflects each name, scope, type and requiredness. Hashes can
fingerprint a schema or reveal names through guessing; protect them like other
document metadata. Serializing the schema object itself exposes its `Paths`.
This identifies declarations only: it does not bind template bytes, effective
options, fonts, providers or engine identity and cannot authorize plan reuse.

The [local example](../examples/WordNet.TemplateLint/Program.cs) prints JSON:
`dotnet run --project examples/WordNet.TemplateLint -- invoice.docx invoice-data.json`.
The optional JSON file is read with a character bound and checked against all
example schema declarations, not only paths observed in the template. With data,
the output contains separate `template` and `data` reports; with the template
alone, the original template report is printed. Change its schema declarations
to match the application. Exit 2 denotes a definite inspected path/control or data
problem, malformed input or an I/O error; failed input prints no partial report
and uses a generic error message. Exit 1 includes unknown coverage and must not be
treated as approval. Data findings include stable `Remediation.*` identifiers.
Findings contain opaque `path-0001` and `part-0001` aliases, stable codes
and remediation identifiers. Part aliases identify whole parts. For a direct
simple field or admitted control, a finding may also expose `SourceElementIndices`: one-based element
child positions starting with the root element. They identify a node only within
the inspected input bytes; complex fields and other unlocated findings return null.
Invalid control-path findings for these direct plain controls can also identify the
source element without exposing the invalid tag text.
Positions contain neither element names nor values, but can still reveal document
structure. Their work and retained size are bounded, and failure returns no partial
report. `GetSensitivePathAliases()` and
`GetSensitivePartAliases()` explicitly reveal their mappings. Default JSON excludes
those mappings and data values, but the SHA-256 of input bytes can still fingerprint
a document and must be protected.

This report always sets `IsReusable` to false: it does not bind effective options,
fonts, providers or engine identities and cannot authorize #172 policy execution.
Other controls and repeat scopes, other table structures, stories, notes, text boxes, other fields,
other destination compatibility, layout, writing and independent validation remain
uninspected or unknown. Invalid input and resource limits throw instead of returning
a partial report. Streams are consumed from their current positions and left open;
inspection performs no merge. Item declarations and their collection names in the
schema are caller data and may themselves be sensitive. Up to 4,096 declared paths, requirements, findings and
aliases and 100,000 work items are admitted, in addition to conversion input bounds.
The API is 1.6.0 source, and full #171 acceptance remains open.
The [independent location observation](measurements/roadmap-138-preflight-locations-reference.json)
checks simple body/cell field positions against source XML and confirms that a
complex field remains unlocated; nine altered controls exercise the checker.
The [independent control observation](measurements/roadmap-138-preflight-controls-reference.json)
compares source positions and path/type findings with actual failed and successful
merges, including unknown formatted and repeated controls.
The [independent repeat observation](measurements/roadmap-138-preflight-repeats-reference.json)
checks the root collection prediction, unchanged failed merges and repeated output
while retaining mixed and unsupported scopes as unknown.
The [independent cell-control observation](measurements/roadmap-138-preflight-cell-controls-reference.json)
checks simple cell requirements, invalid-control positions and actual merge outcomes
while a merged-table owner remains uninspected.
The [independent PDF-option observation](measurements/roadmap-138-preflight-pdf-options-reference.json)
compares static findings with rejected staged exports and an independently read
successful ordinary PDF.

The 1.6.0 source for #144 supports an inline typed child inside an admitted rich-text
block control. Inspect the child's `ParentId` snapshot and reacquire handles after
each edit; parent content locks protect children, and replacing a parent containing
children is rejected. The [nested-control profile](content-controls.md#unreleased-source-a-bounded-nested-owner)
documents supported owners, explicit bindings and remaining exclusions.


## Mixed report regions — merged source

PR #135 extends body `repeat:Path` controls to one contiguous, same-section
region of ordinary paragraphs and simple tables. Zero items remove the region;
one or more items reproduce its blocks in order. Inline scalar, image and
conditional controls and existing row repeats in those tables retain their item
scope. Cloned bookmarks, notes, lists and retained template-control IDs are
remapped; aliases survive. `KeepCached` retains unresolved controls with unique IDs.
Composition also remaps retained template-control IDs.

One parent region may contain a child body repeat with a distinct path; at least
one simple table must occur within the parent. Lookup searches the current item,
then enclosing items from nearest to farthest, then the root, using the first
complete path. Children can shadow parent values; no expressions or method calls
are evaluated. Existing nested row repeats keep item-then-root lookup rather than
implicitly inheriting their enclosing row item. Parent and child work
shares the depth, row, character and byte budgets; empty collections remove their
own region. Deeper block nesting and ambiguous same-path wrappers remain rejected.
The runnable [customer statement](../examples/WordNet.Automation/P3Recipes.cs)
prepares subtotals in caller code.

Nested tables, other overlapping block controls, section breaks, positioned or retained
objects and revisions are outside this profile. References and sequence counters
crossing the region, shared notes and ambiguous control properties fail explicitly.
Mixed regions also reject documents with unresolved link or field-structure losses,
because their targets cannot be checked reliably. Generated blocks, rows, cells,
images and metadata consume the existing limits before commit; cancellation and
failures leave the original session unchanged. This profile is included in 1.5.0.

<a id="unreleased-source-deeper-mixed-report-regions"></a>
### 1.6.0 profile: deeper mixed report regions

The 1.6.0 #157 profile extends the same body profile to multiple nested `repeat:Path`
regions, bounded by `TemplateOptions.MaximumDepth`, the conversion nesting limit
and an absolute maximum of 64 control levels. Each ancestor path must differ from
its descendant paths, and each enclosing repeat containing another block repeat
must contain a simple table within its subtree. Sibling regions keep document
order; zero records remove only that region. Lookup still searches the current
item, then enclosing body items from nearest to farthest, then the root. Existing
nested row repeats retain their distinct item-then-root lookup behavior.

Every repeated boundary is checked for crossing bookmarks, fields, links,
sequence counters and shared notes before expansion. A clone remaps contained
bookmark, note, list and retained control identities for each generated item; the
aggregate row, character, byte and reference-work budgets apply across all levels.
`KeepCached` retains unresolved nested wrappers with distinct control IDs. Nested
tables, overlapping non-repeat wrappers, same-path ancestors, section breaks,
positioned objects and unresolved links or field structures remain outside this
profile. More ownership combinations and the full acceptance/runtime evidence for
#157 remain open; this change is not in the published 1.5.0 packages.

`AddCrossTab(records, aggregation, includeTotals, token)` appends an editable
table from explicit `CrossTabRecord` values. Keys are non-empty Unicode strings,
ordered by first appearance using ordinal equality; null/empty keys and control
characters fail, and the
label `Total` is reserved when totals are requested. Count
includes null values, sum ignores them, absent intersections are zero and decimal
arithmetic is checked. Sum cells and totals use the largest input decimal scale;
count cells use integer formatting. Empty input produces a header; optional totals add a totals
column and row. Limits are 100,000 input records, 64 column keys and 10,000 output
cells, in addition to the conversion bounds. Widths use the selected 11-point font
metrics with padding and a safety allowance and must fit the current body column;
a table that does not fit fails with
`Report.CrossTabLayout` instead of shrinking or dropping data. This conservative
test can reject tables that a more sophisticated layout might fit.

<a id="unreleased-source-cross-tab-placement"></a>
### 1.6.0 profile: cross-tab placement

The 1.6.0 #158 profile adds `InsertCrossTabAtBookmark(name, records, aggregation,
includeTotals, token)`. It replaces one dedicated plain body bookmark paragraph
with an editable table while retaining surrounding blocks and the target section.
It uses the same ordering, decimal, count, totals and resource rules as `AddCrossTab`,
and checks the target section's actual column width before committing. The first
row is an editable repeating table header where the ordinary body-table layout
supports pagination.

The bookmark must own its entire unwrapped paragraph and cannot be a list item,
heading, code block or paragraph with keep/page-break behavior, or be enclosed by
another bookmark. Crossing body ranges and links or REF/PAGEREF
fields to the placeholder reject before data enumeration. Other cell destinations,
repeat-region insertion and horizontal tiling remain open in #158; no table is
shrunk or silently truncated to fit.

`InsertCrossTabAtCellBookmark(name, records, aggregation, includeTotals, token)`
adds a separate, narrower cell destination. The target must be a dedicated plain
bookmark paragraph in an ordinary top-level body table with an explicit fixed grid;
any specified cell widths must match that grid. It requires no merges or nested
tables, and no protected row,
table or cell owners. The target row cannot be a repeating header, an exact-height
row or a non-splitting row. All four physical cell margins (`top`, `right`,
`bottom`, `left`) must be explicit on the cell or inherited from the table in
the loaded DOCX/DOTX;
implicit, partial, duplicate or direction-dependent margins reject. The containing
table must fit its section column, and the cross-tab must fit the cell's usable
width. Caller values, limits, reference checks and atomicity match body placement.

The nested table's first row is ordinary content, not a repeating header; its
values can flow across pages within the parent row. A trailing empty paragraph is
added when required by DOCX cell structure. Other cell geometry, nested header
repetition, named report-region placement and broader #158 acceptance remain open.
An [independent comparison](measurements/roadmap-138-crosstab-cell-reference.json)
records a retained implicit-margin difference and the qualified explicit-margin
case; it is not a general Word layout guarantee.

`InsertFragmentAtBookmark(name, fragment, token)` replaces a dedicated plain
body bookmark paragraph with a private copy of a separately loaded DOCX or safe
HTML session. Callers load sources explicitly; streams remain theirs. Source
formatting is kept, colliding styles and list/bookmark/note/template-control IDs
are remapped, and the destination section is used. Sections/stories, nested tables,
history, positioned text boxes, source load warnings and
informational diagnostics for omitted/flattened field instructions are rejected.
Published 1.5.0 also rejects typed controls, retained chart and equation fragments; the 1.6.0
source subset is described below.
This original overload also rejects fields. Internal links must resolve
in the fragment. Target REF/PAGEREF fields or links to the placeholder must be
resolved before insertion; unrelated fields remain intact. Both sessions are
unchanged on failure; repeated insertions create independent identities.
Crossing source bookmark ranges fail, and nested ranges are limited by the
destination's `MaximumNestingDepth`.

The 1.6.0 source also provides `InsertFragmentAtBookmarkWithOptions` and
`InsertFragmentAtTableCellBookmarkWithOptions` with a `FragmentInsertionOptions`
argument. `Style` defaults to `CompositionStyle.KeepSource`. The explicit
`UseDestination` policy applies the placeholder paragraph's alignment, layout,
paragraph style and first ordinary run appearance to source body/cell paragraphs;
it retains their text, list/heading structure, links, bookmarks and field tokens.
Source note appearance is unchanged. Detached typed-control display and code-styled
content/placeholders cannot use this restyling policy, and both sessions remain
unchanged on failure.

The 1.6.0 source adds `InsertFragmentAtTableCellBookmark(name, fragment, token)`
for a dedicated whole-paragraph bookmark inside an unmerged, single-column-span
cell of a top-level body table. It keeps the host grid, widths and neighboring
blocks, and admits multiple paragraphs and simple source tables. A paragraph is
added at the end of the cell if the inserted source would otherwise leave a table
as its last block. Nested source tables, merged/protected cells, nested-table
destinations and stories remain outside this partial #146 profile. The original
body API keeps its existing placement rule.

The options methods permit the explicit
`FragmentFieldHandling.AcceptCachedResults` choice. It admits simple `MERGEFIELD`,
`REF`, `PAGEREF`, `SEQ`, `PAGE`, `NUMPAGES`, `SECTIONPAGES`, `DATE` and `TIME`
instructions, without nested fields or structured caches. `REF` admits the `\n`
list-label switch; `PAGEREF` admits no switches. Hyperlink reference switches remain
unsupported. Sequence switches must match the existing supported evaluator;
page/date/time fields admit no switches. Field-bearing fragments require each
bookmark range to remain inside one paragraph. Reference targets must exist inside
the source fragment and are remapped with its bookmarks; sequence
names are isolated. `Fragment.CachedFields` records the caller's explicit acceptance:
cached values can be stale, while existing DOCX/PDF/XPS rules refresh supported
references or page fields in the destination. Template merge is explicit. Other
field kinds and unresolved targets fail atomically. These source changes are not
in the published 1.5.0 package; #146 remains open for its full acceptance and
independent qualification.

```csharp
// Both sessions are loaded separately; this explicitly accepts cached field values.
destination.InsertFragmentAtTableCellBookmarkWithOptions("Terms", clause,
    new FragmentInsertionOptions { Fields = FragmentFieldHandling.AcceptCachedResults });
```

<a id="unreleased-source-retained-objects-in-fragments"></a>
### 1.6.0 profile: retained objects in fragments

The 1.6.0 source for #147 admits the existing bounded chart and equation placements in
body and simple-cell fragment destinations. Repeated insertion gives each placement
a new identity, preserves its admitted chart graph or equation tree and description,
and leaves the source session unchanged. Callers can update each inserted chart or
equation independently. Admission checks the combined destination model and object
budgets before committing. Fragments containing report controls or retained
history remain unsupported with these objects, as do positioned text boxes.
Object-bearing fragments retain source appearance; `UseDestination` rejects them.
This increment does not qualify additional tagged, archival or native combinations;
#147 remains open for its remaining acceptance and runtime qualification work.

<a id="unreleased-source-typed-controls-in-fragments"></a>
### 1.6.0 profile: typed controls in fragments

The 1.6.0 source for #147 also accepts admitted typed-control fragments at whole-paragraph
body and simple-cell bookmarks, including bounded nested owners. Each insertion
remaps control IDs and copies local XML stores to new deterministic document-local
identities. Binding caches and their apply/write-back state are preserved; insertion
does not synchronize them. Copies can be edited and synchronized independently,
and the source session is unchanged. A destination can already contain admitted
typed controls, provided the selected bookmark is outside every protected owner.

Admitted static glossary entries and their local styles, settings, font-table and
web-settings resources can be copied with the controls. Each copied entry receives
a new document-local name and GUID where present; placeholder references follow
the new name. Compatible resources are retained once. Different bytes for the same
resource kind or part conflicts reject before insertion; source load losses still
reject rather than being flattened implicitly. No active or external relationships
are admitted.

Date controls require matching source and destination date policies. A fragment
with typed controls and a table requires a body destination; report controls or
history cannot be combined with these typed fragments. `UseDestination` rejects
typed-control restyling. The source and combined destination are admitted against
the destination's captured resource and work limits before committing. Broader
resource-graph merging remains open in #147.

The [created-cell composition observation](measurements/roadmap-138-cell-composition-reference.json)
checks a table containing created plain and rich block controls copied into a body
bookmark. Copied IDs and values remain independent of the source and an existing
host control; attempting to insert that table-bearing fragment into a cell rejects
without changing either document. This is a bounded source observation, not a claim
about interactive Word editing or every composition owner.

The 1.6.0 source permits admitted typed controls and retained chart/equation
objects in the same document or fragment when their paragraph owners are
separate. Objects cannot share a paragraph with an inline, block or structural
typed control; this protects control locks and whole-owner edits. After insertion,
typed values and bindings and object data/descriptions can be edited independently.
Ordinary managed DOCX, PDF and XPS are covered by the bounded profile; native,
tagged, archival and retained-review combinations retain their existing restrictions.

## Opt-in typed controls — merged source

See [typed-control usage](content-controls.md) for the opt-in DOCX entry point,
immutable handles, explicit checkbox/list/date/rich/plain edits, placeholders and
local XML operations. Typed tags never execute; ordinary content/template edits
require a detached display view. That view keeps original Unicode checkbox text;
glyph fallback and managed underline restrictions run only at later PDF/XPS save.
The source profile explicitly lists Word editor metadata, inactive-script bounds
and the bounded 1.6.0 glossary metadata subset. Richer Word-generated graphs
remain rejected. Existing
template-only loading below is unchanged.
The implementation merged in PR #134 and is included in 1.5.0.

## Merged typed properties and field workflows

See [the P1 usage and token matrix](roadmap-102-p1.md#typed-custom-properties-108) for immutable custom-property snapshots, atomic updates and DOCPROPERTY lookup. The same profile specifies numeric/date pictures, retained nested scalar IF fields and managed running headers. Legacy merge formatting remains unchanged. These additions merged in PR #133 and are included in 1.5.0.


## 1.4.0 roadmap #99 profile

Version 1.4.0 extends the 1.3.0 profiles with the bounded changes in
[roadmap-99-implementation.md](roadmap-99-implementation.md): direct HTML/ODT PDF export,
chart binding, scalar fields, structural comparison and qualified layout/native/object
combinations. The [1.4.0 release notes](releases/1.4.0.md) record compatibility and
qualification requirements. Earlier release receipts below remain historical evidence.

## Released 1.3.0 profile

The 1.3.0 API adds immutable `ChartDefinition`/`ChartSeries`, closed `EquationExpression`, versioned `Objects` snapshots and explicit create/update/remove methods. Ordinary edits retain objects only where supported; template execution and protected owner rewrites reject. See the [object API and limits](p2-objects-typography.md).

## Merged P1 report and review additions

[ReportData](report-preprocessing.md) produces deeply read-only ordinary merge values
from grouping/sorting/aggregates, checked decimal expressions and typed predicates.
Existing Merge/MergeJson syntax and missing-value behavior remain.

A body `repeat:Path` control may contain contiguous paragraphs/list items in one
section. Expansion preserves order/appearance and remaps closed bookmarks, numbering
and notes. Item scope precedes root scope; an empty collection removes the region.
Generated body blocks and table rows share MaximumRows; character/byte/depth limits
still apply before commit. Cross-region bookmarks, positioned objects, section
changes and unsupported overlapping block controls reject execution. Malformed repeat
regions cannot be serialized into executable partial controls. Single-row repeats
retain their existing behavior.

[Review APIs](review-workflows.md) are separate from ordinary Replace/Merge operations.
Only explicitly recorded replacements create history. Retained history protects
ordinary edits/templates until resolved; append remaps supported history.

`WordDocument` provides the bounded editing/template profile for roadmap #32,
bucket 3 (#11–#15). It keeps the existing converter API and five framework assets.
It adds no runtime dependency and uses the same managed offline DOCX/PDF writers.
See [the API decisions](editing-template-design.md) for ownership and architecture.
This API merged in PR #35 (`0b2af843`) and is included in published 1.2.0.
See the [release evidence](release-1.2.0-readiness.md). The [sample instructions](../examples/WordNet.Automation/README.md)
reproduce complete invoice and multi-section report recipes. The sample computes totals in application code, uses existing repeat/if controls and stages both output formats before replacing files. It prints load/edit/save diagnostics; ordinary invoice PDF reports its image accessibility limitation. No new engine arithmetic or template syntax is introduced.

## Create, edit and save

```csharp
using System.IO;
using WordNet;

var policy = new ConversionOptions(FidelityMode.Lenient);
var document = WordDocument.Create(policy);
document.Title = "Invoice";
document.AddParagraph("Invoice", new TextFormatting { Bold = true }, headingLevel: 1);
document.AddTable(new[] { new[] { "Description", "Amount" }, new[] { "Consulting", "120.50" } }, firstRowIsHeader: true);
using var word = File.Create("invoice.docx");
ConversionResult result = document.Save(word, DocumentFormat.Docx);
```

`Load(stream, format, out result, options, token)` accepts existing readable formats.
`Save` accepts DOCX, DOC, PDF, HTML, XPS and the existing text/ebook outputs, including
DOCX-to-DOCX editing. Every save returns the conversion diagnostics. A load is an
interpretation of the [semantic profile](document-fidelity.md); unsupported parts
are diagnosed and are not retained as arbitrary OOXML. Original diagnostic counts
and locations survive edits/saves. A subsequent Strict save rejects earlier losses.
Unexecuted templates saved outside DOCX use diagnosed cached content.

`Paragraphs` returns immutable body snapshots, including nested table cells and
text boxes. `ReplaceParagraph` replaces one unstructured paragraph's runs and
retains its paragraph settings. `DocumentText` describes copied text appearance or
an inline `TemplateImage`. Image bytes are copied at construction, validated on
admission, and retain their declared dimensions in points. Existing links, lists,
stories, drawings, notes and fields stay in the internal model within its profile.
`TemplateImage.FromStream` reads from the caller's current position, leaves the
stream open and enforces a configurable byte bound and cancellation.

Operations on one session serialize, including input enumeration/property snapshotting.
Reentrant mutations from caller callbacks are rejected. All edits use private copies
and commit only after validation/admission; cancellation, invalid syntax and limit
failures leave the session unchanged. Save works on a copy and stages output before
copying to the caller's current stream position without truncation. Streams stay
open. A failure during final I/O can leave partial bytes, as with `WordConverter`.
Configure shared options/data before use. Separate sessions may run concurrently.

For reports built from many text paragraphs, use one atomic batch:

```csharp
document.AddParagraphs(reportLines, new TextFormatting { FontSize = 11 });
```

`AddParagraphs` enumerates once and validates/commits the batch once. Repeated
individual mutations each copy and validate the session; use batches for large
reports. Invalid or cancelled batches leave all existing content unchanged.

## Replace text and edit bookmarks

```csharp
DocumentEditResult changed = document.Replace("{{Customer}}", "Example Company");
document.ReplaceRegex(@"Invoice\s+[0-9]+", "Invoice 42", System.TimeSpan.FromMilliseconds(100));
document.AddBookmark("Recipient", paragraphIndex: 0, start: 0, length: 7);
document.ReplaceBookmark("Recipient", new[] { new DocumentText("Customer") });
document.InsertAtBookmark("Recipient", new[] { new DocumentText(" name") }, atEnd: true);
```

Matching covers body, nested cells, notes, boxes and header/footer stories, within
one paragraph at a time. Fields, images, bookmarks, notes, boxes and tagged controls
are barriers. Matching uses invariant culture; case-sensitive matching is the
default. Literal matches are leftmost/non-overlapping. Regex patterns have at most
4096 characters and a positive timeout of at most one second. Literal replacement
has no fixed time budget; cancellation and admission limits still apply. Run slicing
advances through source runs once. The regex matching phase
also checks that time budget between paragraphs/matches; one in-progress regex
match can consume another timeout interval. Replacement text is literal (no `$1`
expansion), inherits the first matched run's appearance, and retains untouched
prefix/suffix styles. Empty searches, zero-length matches, invalid patterns,
surrogate-splitting matches and excessive expansion fail atomically.

`Bookmarks` exposes named text ranges. Creating a bookmark uses UTF-16 offsets in
one plain-text body paragraph. Replacing/inserting supports paragraph-local ranges
and styled text/images, preserving paired markers. Cross-paragraph/story ranges
and overwriting nested bookmarks, fields, notes or boxes are explicitly rejected.
Paragraph replacement and bookmark insertion/replacement also reject template
controls that would otherwise be deleted or split.
This API is not an automatic redaction guarantee or tracked-edit engine.

## Compose documents

`destination.Append(source, CompositionStyle.KeepSource)` copies supported content,
sections and stories, starting the appended content on a new page. Resolved source
formatting survives colliding style IDs. Lists, bookmark names, internal links,
REF/PAGEREF targets, SEQ names and note IDs are isolated from the destination.
Unresolved source targets remain unresolved, even if the destination has that name.
Stories explicitly stop inheritance where the source has no corresponding story.
Source sessions remain unchanged, including self-appends.

`UseDestination` uses the destination's first body paragraph of the same heading
level as an appearance/layout prototype for appended body paragraphs. It retains
source section geometry, stories, list identities and explicit content. This is a
documented role-based style rule rather than an arbitrary Word style-catalog merge.
DOCX retains supported field tokens and refreshes plain REF/SEQ results; PDF/XPS
refresh their supported page-dependent references after composition. Unsupported
source features retain diagnostics. Aggregate destination resource limits apply.
In 1.6.0 #141 source, a destination with body, note or story content and a source must have identical
retained bounded themes, or both have no retained theme. A conflict fails before
the edit commits, including with `UseDestination`; an empty destination may adopt
the source theme. Document comparison likewise rejects differing retained themes.
The [theme profile](document-fidelity.md#unreleased-source-bounded-theme-appearance)
describes the retained subset and loss diagnostics.

## Populate templates

```csharp
using System.Globalization;

document.Merge(new
{
    Customer = new { Name = "Example Company" },
    Items = new[] { new { Description = "Consulting", Amount = 120.50m } },
    HasTax = true,
    Logo = new TemplateImage(File.ReadAllBytes("logo.png"), 80, 40, "Company logo")
}, new TemplateOptions { Culture = CultureInfo.InvariantCulture });
```

The caller reads logo bytes explicitly; the engine does not load files or URLs.
`Merge` snapshots dictionaries or public instance properties. Indexers and methods
are excluded; caller-requested property getters execute during snapshot traversal.
Data cycles/excessive depth, nonfinite numbers, invalid keys and excessive arrays
are rejected. `MergeJson` accepts a bounded JSON object, rejects duplicate keys,
comments and malformed syntax, and uses the same evaluator. No scripting engine,
database lookup, implicit filesystem/network access or runtime expression execution
is involved. Supported scalars are strings, booleans, finite numbers, dates, GUIDs
and null (empty text). Other value types, including enums, characters and time spans,
are rejected during snapshotting. Paths use case-sensitive ASCII identifiers of at most 64
characters, separated by dots, with at most 256 characters in total. Repeat-item
scope is checked before root scope.

Simple and complex Word `MERGEFIELD` instructions, including split instruction
runs, support `MERGEFIELD Path`, `\@ "date format"`, `\# "number format"` and
`\* MERGEFORMAT`. Date/number switches require matching scalar types; format strings
have at most 128 characters and standard numeric precision at most 1000. Formatting
uses the explicitly selected culture (invariant by default). Repeated field targets
are allowed. The bounded Word-format mapping treats numeric `%` as a literal, maps
date `AM/PM` and `am/pm` to culture-specific upper/lower designators, and preserves
quoted literals and escapes. Standard .NET numeric formats such as `F2` are also
accepted. Per-mille scaling and unclosed quotes/escapes fail explicitly; this is
not the complete Word format language. Other field grammars and switch combinations
are explicit failures.
Ordinary `WordConverter` calls continue to use merge-field caches.

Content-control `w:tag/@w:val` values use this exact grammar:

| Tag | Placement and behavior |
| --- | --- |
| `merge:Path` | One block paragraph or one adjacent inline control group; retains first-run appearance |
| `image:Path` | Same scalar placements; replaces cached content with a caller `TemplateImage`, or empty content for null |
| `if:Path`, `if:!Path` | Block or inline content; retains/removes it according to the value or its negation |
| `repeat:Path` | One table row, a body paragraph region, or (in PR #135) a mixed paragraph/simple-table body region; repeats for array items in source order |

False values are null, false, numeric zero, an empty string or an empty array.
Conditions can contain scalar fields/inline controls. Nesting controls that share
the same block, row or run group is outside the profile and diagnosed on load,
except for the bounded mixed master-detail body repeats described above.
Merge rejects these flattened nested constructs before executing any condition.
Block controls inside cells are supported; cell-level table wrappers are diagnosed
and cannot execute. Multi-block/multi-row control boundaries survive DOCX save/load,
so unsupported scalar/repeat regions remain unsupported after serialization.
Control-specific validation, locking, data binding and appearance also report loss. No
arithmetic, comparisons, calls, array indexing or general-purpose expressions are
supported. Controls without a recognized template-operation prefix retain ordinary
cached-content conversion behavior; template validation does not apply to their
unrelated Word/add-in properties or tags.

Repeats execute before conditions/scalars/images in each generated row. Nested
repeats in nested tables default to a maximum depth of eight; generated rows default
to at most 10,000. Bounds also apply to the input snapshot, characters, structure,
images and aggregate expansion. Empty arrays/null remove the marked row; an empty
table is removed. Horizontal spans, decoration, heights and row controls survive.
List/SEQ numbering continues within a repeated region. Cloned bookmarks/notes have
unique identities; note labels are renumbered in reference order. Cloned repeat-row
notes bind against the item scope before root fallback. References to
bookmarks elsewhere in the same document keep their original targets, while local
cloned bookmark targets are remapped. Vertically merged
repeat rows and bookmark ranges crossing a repeat boundary fail explicitly.

Missing data defaults to an atomic error. `MissingValues = KeepCached` retains the
unresolved construct; `Empty` substitutes empty content (false for a condition,
empty for a collection/image). Both explicit fallbacks report `Template.MissingValue`,
which Strict rejects. Null is a supplied empty value, not missing. Diagnostics use
codes and source locations without including field names or data values. Invalid
images, syntax or expansion fail atomically. Supported executed markers are removed;
ordinary non-merge fields remain editable and follow the existing field profile.

The 1.6.0 source work for #159 adds [bounded table formulas](table-formulas.md), including
dependency-aware refresh after admitted report expansion and edits. It requires
simple invariant numeric cells and retains all table formula caches together when
the table or a dependency falls outside that profile. This work is not in the
published 1.5.0 packages.

## Run the standalone sample

```sh
dotnet restore examples/WordNet.Automation --locked-mode
dotnet run --project examples/WordNet.Automation -c Release -- /absolute/output/directory
```

The sample creates a cover letter, loads an independently authored invoice with
repeating items/optional terms/logo, edits its text and assembles an appendix. It
exports three editable DOCX/PDF pairs and prints diagnostics. Its logo reuses the
repository's authored raster fixture; no Office or native rendering provider is
required for execution. Tests independently validate DOCX schemas and PDF content,
page geometry, image bytes/dimensions and editable structure. Broader template
compatibility and arbitrary Word style/layout parity remain unqualified.
