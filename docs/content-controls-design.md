# Typed content-control contract — roadmap #102 C0

**Status: design and bounded implementation merged in [PR #134](https://github.com/kirakosyan/word.net/pull/134).**
The owner subsequently authorized all P2 issues #112–#117 as one batch, superseding
this ADR's original per-leaf scheduling gate. Implementation follows the same
technical dependencies; each leaf keeps independent acceptance evidence. See the
[current source/API profile](content-controls.md) and
[acceptance map](content-controls-acceptance.json). Public 1.4.0 excludes these
additions; they were subsequently included in 1.5.0.

P1 merged in [PR #133](https://github.com/kirakosyan/word.net/pull/133), commit
`2a944ac9101598bc3be2d856e5bad116123998a3`, with tree equality to qualified head
`db1fa3c69a34dbb1fd0ec102f039a0a0fb20d562`; its
[P1 receipt](measurements/roadmap-102-p1-review.json) remains historical evidence.
The final source profile records implementation refinements: schema-canonical GUID
casing, admitted stores surviving explicit detach, copied paragraph-input bounds,
the bounded managed underline policy, and subsequent review corrections to final
owner validation, Word-shaped editor metadata, direction and deferred fixed-output
preparation. See the source profile for the intentional Word-generated glossary
rejection and inactive-script limits. These changes add no dependency, target
framework, publication or platform claim.

## Context and decision

The existing [editing contract](editing-template-design.md) preserves executable
`merge:`, `image:`, `if:` and `repeat:` tags. `DocxReader.Templates.ReadControl`
otherwise diagnoses control-specific properties. It does not provide typed form
state, persistent OOXML IDs, editing locks or XML synchronization. Existing
`WordDocument.Load` and ordinary cached conversion keep those contracts.

Add a separate, explicit DOCX preservation entry point in C1. Store typed controls
in a private semantic store with stable owner references, separate from
`TemplateControl`. All readers, cloners, admission, edits and writers must handle
that store together. No public XML/SDK/provider types or arbitrary part editor are
introduced. A central copy path must retain unrelated field trees, styles,
bookmarks and control metadata; reconstructing text runs must not flatten them.

Preservation is all-or-failure for opted-in controls, including BestEffort and
Lenient. Unknown types, unknown behavior-bearing properties, unsupported owners,
unsafe parts and incompatible combinations fail before publishing a session.
There is no unvalidated raw-XML escape hatch or automatic downgrade to template
execution. Callers wanting the existing cache interpretation use the existing
entry point and its diagnostics.

| Stage | Bounded capability in PR #134 | Acceptance gate |
| --- | --- | --- |
| C0 #112 | This contract and the [acceptance matrix](content-controls-acceptance.json) | Design/specification/link review only |
| C1 #113 | Common store/owners/handles/locks/placeholders and `w14:checkbox` | Independent checkbox preservation, edit and display evidence |
| C2 #114 | `w:dropDownList` and `w:comboBox` | Item/selection/empty/free-text evidence |
| C3 #115 | `w:date` | Explicit date-mode and F2-format evidence |
| C4 #116 | `w:richText`, including an omitted type declaration | Formatted inline/paragraph replacement evidence |
| C5 #117 | Local binding for admitted scalar controls, including narrow `w:text` | Safe XML graph, singleton paths and both explicit directions |

The original stages were designed to reject not-yet-implemented kinds. In this complete batch, C5 owns the minimal plain-text
scalar adapter required by #117; rich text containing only text is still rich text
and cannot stand in for `w:text` binding.

## Owners, identity and placeholders

Admit inline `w:sdt` within one ordinary body/cell paragraph, and block `w:sdt`
containing ordinary paragraphs directly in the body or an ordinary table cell.
Checkbox/list/date/plain-text controls contain one formatting set and at most one
paragraph; checkbox content is one character, including a cached placeholder. Rich controls
may contain multiple formatted runs or, for block owners, multiple paragraphs.
The containing table stays outside the control. No table/row/cell wrappers,
section boundaries, headers/footers, notes, boxes, nested controls, embedded
objects, retained revisions/comments or crossing field/bookmark boundaries are
admitted inside a control. C4 explicitly rejects tables inside its content.

Retain signed 32-bit `w:id`, ordinal tag/alias text, admitted default/end run
formatting, lock values and type metadata. Existing IDs are stable on save;
duplicate IDs fail in this bounded profile, rather than applying Word's ID repair.
Missing IDs receive the next unused positive ID in document order with an
informational diagnostic. Exhaustion fails. The writer never derives IDs from
tags, labels, runtime hashes or random GUIDs. Tags and aliases need not be unique;
lookup returns all matches, and every mutation requires a particular handle.

`w:showingPlcHdr`, cached display and typed value are separate. Placeholder text
is never a Boolean, selection, date, binding value or template path. Empty text in
text controls, a selected empty list value, no selection and a placeholder remain
distinct in snapshots; do not invent states that the admitted markup cannot retain.
An explicit typed setter clears the placeholder flag. Showing a placeholder again
is an explicit edit and requires preserved authored placeholder content.

Preserve `w:placeholder/w:docPart` only with its resolved local glossary part,
relationship and named entry. The initial glossary subset is one ordinary
paragraph with admitted text formatting, no fields, objects, controls or external
relationships. Shared entries are retained once; missing/duplicate names,
unresolved built-in references and richer entries fail preservation. A control
with no reference can retain its original cached placeholder. Do not fetch or
invent an application-global glossary, default prompt or proprietary resource.
Store names, source cache and glossary text all consume aggregate budgets.

## Sessions and explicit edits

Snapshots are deeply detached, read-only values. Each includes kind, OOXML ID,
owner description, tag/alias, lock, placeholder/display, type-specific state and
an opaque session/version/control handle. Collections cannot expose mutable model
or caller storage. Public formatting getters return copies, as existing
`DocumentText.Formatting` does. Internal owner identities are distinct from
OOXML IDs; paragraph indexes and tag strings are not mutation handles.

Every operation is serialized on its session. Validate the handle's session,
version, live identity, kind, owner and lock, then clone, validate all input,
charge aggregate work, check cancellation, admit and commit once. Every committed
model edit invalidates prior handles, including an edit returning zero changes;
snapshots remain readable. A failed/cancelled edit leaves model, version and
committed diagnostics unchanged. Snapshots/save do not advance the version.
Reacquire handles after a successful edit; a save/reload creates a new session.

Metadata-only title/author/custom-property edits may retain controls through the
central copy/admission path and invalidate handles. Ordinary content edits,
Replace/bookmark insertion, Append and template execution initially reject a
session or source containing retained typed controls. This conservative boundary
prevents an unqualified owner rewrite or imported-ID collision. Use an explicit
detached display view before those operations. Content inside a control is edited
only by its typed setter. Existing review/object protections remain in force;
typed preservation combined with retained review/object stores is unqualified and
rejected initially. Independent acceptance is needed to widen any of these gates.

The implemented API uses existing `ConversionResult`/`DocumentEditResult` result contracts. See [usage](content-controls.md) for a complete example:

```csharp
// Source API in PR #134; included in 1.5.0.
var document = WordDocument.LoadWithControls(input, controlLoadOptions,
    out ConversionResult loaded, conversionOptions, cancellationToken);
IReadOnlyList<DocumentContentControl> controls = document.ContentControls;
DocumentContentControl checkbox = controls.Single(c => c.Id == 42);
DocumentEditResult edited = document.SetControlChecked(checkbox, true, cancellationToken);
// Reacquire a handle after any committed edit.
document.SetControlSelection(selectionHandle, storedValue, cancellationToken);
document.SetControlComboText(comboHandle, literalText, cancellationToken);
document.SetControlDate(dateHandle, explicitDateValue, cancellationToken);
document.ReplaceControlContent(inlineRichHandle, copiedRuns, cancellationToken);
document.ReplaceControlParagraphs(blockRichHandle, copiedParagraphs, cancellationToken);
document.SetControlText(plainTextHandle, literalText, cancellationToken);
document.ShowControlPlaceholder(handle, cancellationToken);
var display = document.CreateControlDisplayView(out ConversionResult losses, cancellationToken);
```

`LoadWithControls` accepts DOCX only, reads from the caller's current stream
position and leaves it open. Do not add an ambiguous `Load(..., null, ...)`
overload beside `ReviewLoadOptions`. Reuse `DocumentText` for copied text input;
rich block replacement needs a narrow detached paragraph value retaining admitted
paragraph appearance, rather than exposing internal blocks. No control insertion,
arbitrary property/part editing or lock-removal API is planned in this batch.
Type mismatches and null arguments fail explicitly; null date/selection and empty
rich content have the separate semantics below.

All five targets retain the same additive API, including `netstandard2.0` and
Framework. Use portable immutable date components and `DateTimeOffset`, not
`DateOnly`, spans, provider handles or newer-runtime-only collection types.
Capture load options once, including the chosen date mode; later caller option
changes cannot reinterpret a live session.
Save stages the working copy/output; validation and pre-copy cancellation cannot
alter the destination. Caller I/O failure or cancellation during the final copy
can leave a partial caller stream, so file replacement still uses caller staging.
Hard CPU/RSS deadlines remain a host-isolation responsibility.

## Tags and locks

A retained typed control's `merge:`, `image:`, `if:` or `repeat:` tag is literal
metadata and never executes. Preserve duplicate tags, and do not infer the kind
from a prefix. Calling Merge/MergeJson on a typed session fails before evaluating
caller getters or JSON. The explicit display view removes typed wrappers,
bindings, locks, IDs and their executable-looking tags in its copy, reports that
loss, and leaves the original untouched. It does not promote a typed tag into a
legacy template. Separate legacy templates outside controls retain the existing
behavior only after this explicit view. Overlapping/nested typed/template owners
fail. The ordinary template-only entry point and its existing losses stay intact.

| Lock | Edit value/content | Delete wrapper |
| --- | --- | --- |
| absent / `unlocked` | Allowed by the kind's rules | No deletion API in this batch |
| `sdtLocked` | Allowed | Rejected |
| `contentLocked` | Rejected | No deletion API in this batch |
| `sdtContentLocked` | Rejected | Rejected |

Locks apply to typed setters, placeholder resets and XML-to-control application.
No operation may bypass a locked control through ordinary replacement or binding.
Detaching binding metadata initially requires an unlocked control. A detached
display copy is an explicit export, not an edit/unlock of the original. Locks
provide editing behavior, no encryption, authentication or confidentiality.

## Typed state and display

**Checkbox, C1.** Retain checked state and optional checked/unchecked hexadecimal
BMP glyph/font declarations, including their absence. Apply the published Office
2010 defaults (unchecked; U+2612/U+2610 in MS Gothic) when absent. Reject malformed
booleans, non-BMP/surrogate codes, multiple characters and retained deleted runs.
Keep the original declared glyph/font in editable DOCX. Managed output uses an
available admitted glyph; otherwise display selectable ASCII `[x]` or `[ ]` and
report `Control.GlyphFallback`. Do not introduce a font/native dependency to hide
an unavailable glyph. A non-placeholder inconsistent cached glyph is refreshed from typed state
with `Control.CacheRefreshed`; surrounding text/formatting stays intact.

**Lists, C2.** Retain ordered `(displayText, value)` items and optional
`lastValue`. Effective label is displayText if present, otherwise value. Require
unique ordinal stored values and unique, nonempty effective labels; duplicate
labels/values fail this initial subset. An empty stored value is legal and is
distinct from no selection. Unbound selection is established by an exact cached
label match, not by interpreting `lastValue` as an index or selection ID: ECMA
defines that attribute for XML-mapped display/cache behavior. A dropdown nonmember
cache is an explicit unresolved state with intact cache and information, never item zero.
Unchanged unresolved DOCX may be preserved; selecting repairs it explicitly.
An unbound combo nonmember cache is literal free text. Empty cache with no
placeholder means no selection. Dropdown setters require membership; combo setters
permit bounded literal free text. A combo free-text setter rejects text matching
an item label (or a bound member value): the caller must use the selection setter
to avoid losing stored-value identity on reload. Empty combo text clears to no
selection. Null selection clears
to empty without inventing a placeholder. Until C5, dataBinding fails opted-in
preservation. C5 applies only exact member values to dropdowns; combo nonmembers
become literal free text, rejecting a nonmember that ambiguously matches another
item's label. Edits maintain the binding's lastValue when applicable.

**Dates, C3.** Preserve fullDate, dateFormat, lid, calendar, storeMappedDataAs,
placeholder and admitted formatting. The load policy explicitly chooses one date
mode for the session: `UtcInstant` by default or `DateOnly`. Instant input requires
an explicit offset/Z and displays in UTC. DateOnly accepts only canonical midnight
UTC fullDate and uses caller-supplied year/month/day, years 1–9999. It never drops
an imported non-midnight time or offset. Save uses schema-valid dateTime fullDate;
the mode is an explicit load policy, not inferred from its picture or secretly
encoded in private OOXML. Reopening with the same policy preserves interpretation.
Mixed date modes in one session are outside this first subset.

Admit Gregorian with absent/en-US/en-GB locale metadata, case-insensitively, using
fixed English/invariant labels. Require an explicit dateFormat from the
[F2 matrix](roadmap-102-p1.md#field-pictures-109); DateOnly rejects time tokens.
Absent masks, unsupported locales/calendars/tokens, invalid leap days and
offset-less instants fail preservation. No host culture, local zone, current
clock or text-to-date inference is used. Missing fullDate retains an explicitly
unresolved literal cache or placeholder, with no inferred date. Setting null
clears fullDate and display; showing an authored placeholder is separate.
Typed values regenerate display with an informational cache-refresh diagnostic
when it differs. Date storage metadata is preserved even when C5 cannot apply it.

**Rich text, C4.** Inline replacement is a copied sequence of admitted formatted
text runs; block replacement is a copied sequence of ordinary paragraphs with
admitted spacing, alignment, heading role and run appearance. Empty content is a
value, not a placeholder. Preserve fields/bookmarks/other controls wholly outside
the owner, including nested scalar field metadata. Reject fields/bookmarks inside
or crossing a replacement owner, images, objects, revisions, tables and nested
controls. Unsupported paragraph properties must fail, not be silently flattened.
The default omitted type is richText under the specification. No general fragment
insertion, HTML import, formula evaluation or rich XML injection is admitted.

**Plain text, C5 foundation.** Admit explicit `w:text` with one formatting set,
at most one paragraph and optional bounded soft line breaks when multiLine is true.
Reject line breaks otherwise and reject paragraph/table markup. Expose a literal
string and copied formatting; binding never upgrades it to rich content.

## Local custom XML binding, C5

Preserve the referenced local customXml part, its item-properties part, internal
relationships, storeItemID and explicit namespace mappings. Match store GUIDs
case-insensitively; require one store per ID and reject missing/duplicate stores,
unsafe part paths, external relationships and unresolved references. This bounded
profile requires storeItemID and explicit mappings for every prefixed path;
it does not search arbitrary parts or use a consumer's closest-match heuristic.
Admitted local custom XML stores, including unbound stores within a control session, are retained so explicit detachment cannot delete caller XML data. Out-of-profile parts keep explicit fidelity diagnostics.
DTD/entities, remote schema/resource resolution and processing instructions are
rejected; schema URIs are inert metadata and never fetched.

Implemented operations are `ApplyControlBindings(handles, token)` (XML to controls),
`WriteBackControlBindings(handles, token)` (controls to XML), and explicit unlocked
`DetachControlBindings(handles, token)`. Loading, saving and ordinary edits never
silently synchronize. All selected bindings are validated before either side
commits. Applying is an explicit decision to replace current control values;
write-back never changes a control to make it fit a target type.

Path grammar, interpreted by a small bounded walker rather than a general XPath
engine: `path := '/' step ('/' step)* ('/@' QName)?`, where
`step := QName ('[1]')?`. For example
`/p:invoice/p:customer/@name` with `xmlns:p='urn:invoice'`. Each step must match
exactly one child; `[1]` does not hide multiple matches. Unprefixed names mean
the empty namespace. Mappings use whitespace-separated `xmlns:prefix='URI'`
declarations with valid, unique NCName prefixes; no implicit default mapping.
Reject `//`, `.`, `..`, wildcards, unions, functions,
other predicates/indices, undeclared/duplicate prefixes and namespace-attribute
updates. Missing targets fail; no implicit element/attribute creation. Element
targets must have text-only content; never replace a mixed or structured subtree.

| Control value | XML application / write-back |
| --- | --- |
| Checkbox | XML Schema `true`, `false`, `1`, `0`; canonical true/false output |
| Dropdown | Exact ordinal stored value; display the associated label |
| Combo | Exact stored value or bounded literal free text |
| DateOnly | `storeMappedDataAs=date`, canonical `yyyy-MM-dd` |
| UtcInstant | `storeMappedDataAs=dateTime`, explicit-offset XML dateTime; UTC output |
| Plain text | Literal XML text/attribute value, escaped on write |

Date text/omitted storage mode cannot supply typed dates without inference and
fails binding operations; it may still be preserved. Placeholder, unresolved or
null values cannot be written back without an explicit concrete value. Rich-text
binding is rejected: ECMA requires dataBinding to be ignored for richText, and
binding it would falsely suggest interoperable semantics. No script, merge
expression, general XPath, external data adapter or rich-markup interpretation.

Track binding state as unevaluated, synchronized or edited/dirty. A value edit to
a bound control makes it dirty. DOCX save then requires explicit apply (discard
the local edit), write-back or detach; never export a newly contradictory cache
silently. An unchanged unevaluated graph may be preserved with
`Control.BindingNotApplied`. Word or another consumer can refresh it on open;
the library's explicit-sync policy does not govern that application's behavior.
Write-back groups controls by target node, requires all sharing controls in the
selection and rejects divergent converted values atomically. No duplicate-node
last-writer-wins policy. Bindings retain part and mapping identities, not ZIP byte
identity or arbitrary XML serialization order.

## Output, diagnostics and limits

DOCX retains admitted editable controls and their metadata/parts. Managed PDF/XPS
emit static visible value/placeholder text with `Control.StaticDisplay`; these are
not interactive form fields. Native typography, retained objects/review and
tagged/archive combinations need separate acceptance and remain rejected in the
initial profile. Other writers report `Control.OutputLoss`: Strict rejects before
copying output; explicit BestEffort/Lenient flatten the display in a working copy.
The explicit display view reports the same loss and retains no binding data.

The implementing source profile documents the actual code catalog. The original design families are: `Control.Unsupported`,
`Malformed`, `DuplicateId`, `ProtectedOwner`, `Locked`, `StaleHandle`,
`TemplateExecution`, `BindingDirty`; `Binding.Store`, `Path`, `Type`, `Conflict`;
and `Limit.Controls` for hard rejection. `Control.IdAssigned`, `CacheRefreshed`,
`ChoiceUnresolved`, `GlyphFallback`, `StaticDisplay` and `BindingNotApplied` are
informational for the specified admitted behavior. Only `Control.OutputLoss` is a
fidelity-mode loss. Diagnostics contain codes/owner locations, never tag, alias,
custom XML, cached/display text, values or secrets. Existing template diagnostics
stay unchanged for ordinary loads. Malformed/security/resource failures do not
become Lenient success.

| Design ceiling | Bound per operation/document |
| --- | --- |
| Controls / glossary entries | 1,024 / 128 |
| Tag, alias and glossary entry name | 256 UTF-16 units each |
| List items | 256 per control, 4,096 aggregate |
| Item label/value and font name | 1,024 / 256 units |
| Date picture | 128 units, 64 tokens, 1,024 displayed units (F2) |
| Binding stores / namespace mappings | 64 / 16 per binding |
| Path / child depth / QName component | 1,024 units / 16 / 64 units |
| Namespace prefix / URI | 64 / 1,024 units |
| Custom XML nodes, attributes included | min(100,000, MaximumCharacters) aggregate |
| Measured traversal/evaluation work | max(4,096, MaximumCharacters × 64), checked 64-bit accounting |

These are additional finite ceilings; caller ConversionOptions can only tighten
effective admission through input/output/expanded bytes, archive entries,
characters, nesting and pages. Charge source XML/ZIP expansion once during input;
count every retained metadata string, item, node and decoded part in model
admission. Shared glossary/store data counts once, owner references count as
structure, and every expanded display counts toward text/output budgets. Cloning
immutable data is not permission to omit it from logical admission. Check
cancellation during parsing, enumeration, copying, path traversal and output
construction. Reject limit+1, checked overflow, deep/wide graphs and aggregate
many-small-part amplification before commit/caller output copying.

## Independent acceptance and deferred scope

The [matrix](content-controls-acceptance.json) assigns positive, negative,
atomicity, copy-boundary, export and resource cases to every executable leaf.
Fixtures must be independently authored from the specifications with provenance,
expected XML/state/text/geometry and pinned open-license fonts. Use the existing
test-only Open XML SDK schema validator plus direct package/relationship assertions
and independent fixed-output readers. Round-trip equality alone is insufficient.
Record unavailable Word/office UI or reference tools instead of claiming their
behavior. Keep actual API/managed glyph fallback and page text selectable.

Each executable leaf runs the current [required checks](../AGENTS.md), all five
API assets, full English-OCR .NET 8/10 suites and compatible Windows Framework
tests. Exact-package/Standard, desktop architecture and actual-minimum-runtime
evidence remain separate gates, with counts and any unavailable lanes explicit.
No skipped placeholder tests count as acceptance. Q1 consolidates evidence and
does not defer each leaf's independent proof.

The initial C0-only increment validated specification references, links, matrix completeness and consistency
with current copy/version/stream/resource contracts. Its original design commit added no engine test or
runtime qualification; the separately authorized runtime batch has its own evidence in the source profile. See the [implementation plan](implementation-plan.md).
Full form UI, PDF forms, legacy form fields/ActiveX, repeating sections, building
block galleries, picture controls, arbitrary nested controls/owners, property or
structural revisions, control composition, encryption/signatures, extra OCR
languages and new platform claims are deferred or excluded.

## Historical C0-only validation — 2026-10-26, commit 31e4259

Validated JSON syntax, 34 unique case IDs, all shared-case references, exact
technical dependencies versus queue predecessors, five-target identity and the
explicit unimplemented/unexecuted flags. At that commit the planned fixture directory did not
exist and the required source suite remained 1,222. Checked 201 local/file-mapped
links and anchors across the affected guides, all 11 primary reference URLs
(HTTP 200), the official Part 1 ZIP hash and diff whitespace. Current source review
covered template dispatch, DocumentText copying, Edit's clone/admit/version
transaction, review/object handles and conversion resource/stream guarantees.
No engine test, control fixture or new runtime/package qualification was added by that initial design-only commit. Subsequent implementation/evidence belongs to the source profile, not this dated receipt.

## Specification basis

Reviewed 2026-10-26; these sources define markup, while the bounds above are this
library's bounded subset decisions:

- [ECMA-376 publication](https://ecma-international.org/publications-and-standards/standards/ecma-376/), Part 1 fifth edition December 2016, §17.5.2 and §22.5; Parts 2/3 for packaging/compatibility. The inspected official Part 1 ZIP SHA-256 is `9d0bcad9cf06054785b03762fcfadbf6bab7e54a5f9d69434e34b7fd464d4129`.
- [sdtPr and its child types](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.sdtproperties?view=openxml-3.0.1), [stable IDs](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.sdtid?view=openxml-3.0.1), [locks](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.lock?view=openxml-3.0.1).
- [Office 2010 checkbox definition/defaults](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.office2010.word.sdtcontentcheckbox?view=openxml-3.0.1).
- [Dropdown](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.sdtcontentdropdownlist?view=openxml-3.0.1) and [combo](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.sdtcontentcombobox?view=openxml-3.0.1), plus Part 1 §17.5.2.5/.15 for lastValue's binding-specific behavior.
- [Date controls](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.sdtcontentdate?view=openxml-3.0.1), [rich text](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.sdtcontentrichtext?view=openxml-3.0.1) and [plain text](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.sdtcontenttext?view=openxml-3.0.1).
- [dataBinding](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.databinding?view=openxml-3.0.1): rich-text exclusion, store identity and XPath metadata. C5 deliberately rejects the broader XPath/consumer fallback possibilities.
