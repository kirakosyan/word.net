# Typed DOCX content controls — roadmap #102 P2 source

## 1.5.0 roadmap #102 delivery

The [release notes](releases/1.5.0.md), [capability matrix](roadmap-102-capability-matrix.json)
and [qualification record](roadmap-102-p6-qualification.md) describe the P1–P6 batch.
The bounded fields, controls, reports, review and drawing additions ship together;
all five target frameworks and existing platform limits remain. Historical results
below retain their original source/package identities.


PR [#134](https://github.com/kirakosyan/word.net/pull/134) implements #112–#117 as
one owner-authorized batch; its bounded profile is included in 1.5.0. The [design record](content-controls-design.md)
and [acceptance map](content-controls-acceptance.json) describe the contract and
independent gates. The [review qualification receipt](measurements/roadmap-102-p2-review.json) and [initial receipt](measurements/roadmap-102-p2.json) record historical local execution. Final release and runtime evidence is recorded separately in the [release record](releases/1.5.0.md).

## Unreleased source: bounded glossary metadata

The pending source work for #143 preserves a static glossary category with a
bounded name and `default` or `placeholder` gallery on the existing admitted
placeholder entries. It also admits up to four relationships from the glossary part
to `word/glossary/styles.xml`, `word/glossary/settings.xml`,
`word/glossary/fontTable.xml` and `word/glossary/webSettings.xml`. A closed inert
subset of local styles, inherited character styles, settings and font metadata is
admitted, including the [`notTrueType` font catalog flag](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.nottruetype?view=openxml-3.0.1) and the static default
placeholders in independently sourced [Word-authored fixtures](../tests/wordnet.Conversion.Tests/Fixtures/ContentControls/OfficeWordParts/README.md)
and [additional control variants](../tests/wordnet.Conversion.Tests/Fixtures/ContentControls/OfficeWordVariants/README.md).
The placeholder's supported resolved appearance is materialized when displayed;
local glossary style identities are not imported into the main document. Valid
resource bytes and relationship IDs survive typed edits, bindings and DOCX
save/reload. No font or other resource is fetched. Missing and cyclic style bases,
duplicate, dangling, external, nested, active or unknown relationships and
unlisted metadata fail explicitly.
The font flag is preserved as metadata; it does not embed, execute or fetch a font.
An authored list control whose named glossary entry is absent remains unsupported.
Unreleased source can resolve a bounded main-document character-style chain in a
control run, including the Word-authored date-placeholder example. Its resolved
appearance is materialized and `Control.CharacterStyle` reports that editable
character-style identity is lost; Strict mode rejects the loss. Missing, cyclic,
script-dependent and behavior-bearing styles remain unsupported. This is not a
claim that arbitrary character-style references survive editing.

The fixture's theme-dependent defaults retain the existing `Docx.ThemeFont` and
other applicable fallback diagnostics; Strict mode rejects those losses. When
local styles contain East Asian or complex-script slots, placeholder text outside
the admitted Latin/default-script profile is rejected with `Control.ScriptFormatting`.
This profile does not qualify arbitrary Word-generated glossaries or every control kind,
resource graph and template combination required by #143, which remains open.
Published 1.5.0 retains the earlier rejection below; ordinary loading remains
separate from opt-in typed preservation.

## Unreleased source: a bounded nested owner

Pending #144 source admits inline typed children inside ordinary paragraphs of
a block rich-text control in the body or an otherwise admitted simple table
cell. The parent must not be showing a placeholder or have a local XML binding.
All six existing child kinds retain their explicit values, local placeholders,
bindings, source order and unique IDs. The immutable `ParentId` snapshot property
identifies an immediate typed parent; it is null for top-level controls. Handles
remain session/version scoped, so reacquire parent and child snapshots after edits.

A parent's content lock blocks child value, placeholder and binding operations;
the child's own lock also applies. Parent content replacement and binding changes
are rejected while children exist, so a parent edit cannot erase a child or bypass
its lock. Child edits and explicit binding application refresh the parent's value
state. Shared XML targets retain the existing all-selected and agreement rules;
failed edits and binding conflicts leave the document and local XML unchanged.
The existing aggregate control/work limits and configured XML nesting limit apply.

Editable DOCX preserves the wrapper hierarchy; ordinary managed PDF/XPS and an
explicit display view show the child text. An independently assembled
[Word-derived control](../tests/wordnet.Conversion.Tests/Fixtures/ContentControls/NestedOwners/README.md)
and LibreOffice rendering check text order and the schema, without establishing a
Word-authored nested template or Word execution. This is a limited source increment:
deeper nesting, inline-in-inline and block children, nested or multi-row/cell wrappers,
nested and table-wrapped header/footer or other story owners, repeated report rows and typed-control
composition remain outside this subset. #144 remains open. Ordinary loading is
unchanged and does not create typed owners.

## Unreleased source: plain header and footer owners

Pending #144 also admits an inline typed control in a top-level plain header/footer
paragraph or a block typed control containing ordinary paragraphs directly in a
header/footer. The six existing kinds, locks, placeholders and explicit local XML
bindings use the same bounded rules as body owners. IDs are unique across the body
and distinct referenced stories; missing IDs are assigned after reserving every
explicit ID. An inherited or multiply referenced story has one owner and one set of
bindings. Its edited display is reused on each page that selects that story.

Snapshots identify these owners as `inline header/footer paragraph` or `block
header/footer paragraphs`. Their `ParentId` is null. Editing and binding operations
remain atomic, honor locks and require fresh handles; a shared XML target still
requires all affected controls to agree. Editable DOCX and an explicit display view
retain the selected stories. Ordinary managed PDF/XPS presents static text; it does
not create interactive PDF controls. The
[synthetic Word-derived reference](../tests/wordnet.Conversion.Tests/Fixtures/ContentControls/StoryOwners/README.md)
and LibreOffice PDF check selected header, body and footer text placement over two
pages, not Word execution or universal geometry. Table-wrapped or nested story
controls, notes, comments, text boxes and typed-control composition remain outside
this increment; #144 remains open.

## Unreleased source: single row and cell wrappers

Pending #144 source preserves an unbound, non-placeholder rich-text control around
one row or one cell of a top-level body table. A wrapped row contains ordinary
cells with one paragraph each; a wrapped cell contains one ordinary paragraph.
An explicit cell width must be a valid absolute or percentage width; automatic
and other width forms are rejected for these wrappers.
Merged, nested, specially formatted or multi-row/cell wrappers and typed children
inside these structural controls remain unsupported. `Owner` identifies the row
or cell, and its detached content snapshot contains the row's or cell's paragraphs
in order. IDs, literal tags, locks and wrapper placement survive metadata edits,
cloning and DOCX save/reload.

The structural wrapper is read-only in this increment: paragraph replacement,
wrapper removal, binding operations and inserting another typed control into that
table reject instead of bypassing ownership. An explicit display view strips the
wrapper, and ordinary managed PDF/XPS show the cached text. The independently
authored [row and cell controls](../tests/wordnet.Conversion.Tests/Fixtures/ContentControls/StructuralOwners/README.md)
and LibreOffice observations check selected text and relative cell positions.
They do not establish live Word execution, structural editing, nested controls or
repeated report-row identity. #144 remains open.

## Unreleased source: creating an inline control

Pending #145 adds immutable `ContentControlDefinition` inputs and
`InsertContentControl(paragraphIndex, offset, definition)` for an ordinary unstyled
body or simple-table-cell paragraph. The index follows the current `Paragraphs`
snapshot; the offset counts UTF-16 code units and must not split a Unicode scalar.
The returned control is a fresh session handle. Each subsequent committed edit
invalidates earlier mutation handles. Definitions and list items are copied and
bounded before insertion, and IDs are assigned without colliding with existing
typed controls.

```csharp
WordDocument form = WordDocument.Create();
form.AddParagraph("Name: ");
DocumentContentControl name = form.InsertContentControl(0, 6,
    new ContentControlDefinition(ContentControlKind.PlainText, initialText: "Ada"));
form.SetControlText(name, "Grace");
```

All six existing kinds are admitted inline. Text defaults to empty, list controls
default to no selection, dates default to empty with a `yyyy-MM-dd` picture, and a
checkbox defaults to an unchecked `-` glyph in Noto Sans (`X` when checked).
The default lock is unlocked and no placeholder is invented. Definitions can
provide initial values, a lock, literal tag/alias and, for supported scalar kinds,
a `ContentControlBinding` to an already retained local XML store. The initial
value is set before the lock, and the binding is not applied automatically; callers
must explicitly synchronize it. Namespace mappings and list inputs are copied.
Tags remain inert and no network resource is fetched.

Creation rejects paragraphs with other controls, fields, links, bookmarks, objects,
lists or structural breaks, along with styled paragraphs, repeated rows, merged or
nested tables, text boxes and story owners. This increment does not create block
controls or introduce a new local XML store. Template composition and broader
placement remain open in #145 and #147.

## Unreleased source: removing an admitted control

Pending #145 adds `RemoveControl(handle, preserveContent: true)` for admitted
typed owners. By default it removes the wrapper and keeps its current text and
run formatting. Passing `false` removes the control's text as well; a block owner
retains an empty paragraph shell so that its cell, story or section stays valid.
Successful removal invalidates earlier snapshots just like other committed edits.

A wrapper lock prevents removal. A content lock permits explicit wrapper removal
with the content preserved but blocks deleting that content. A parent's content
lock protects its child, and a parent containing typed children cannot be removed.
The read-only row and cell wrappers above cannot be removed by this operation.
An edited binding must first be applied, written back or detached. Retained local
XML and glossary resources are never implicitly deleted: removing the final
control is rejected while those resources remain. Removal is atomic on failure
and cancellation. Checkbox metadata no longer supplies its control-specific glyph
fallback after removal; retained text and run formatting become ordinary content.
Template composition and the remaining #145 scope stay open.

## Load, inspect and edit

Typed preservation is opt-in for DOCX and, with the format-aware overload, DOTX.
Existing `Load` and ordinary conversion
retain their existing cached/template interpretation. Typed loading never executes
merge-looking tags, fetches a resource or automatically applies XML bindings.
Unknown kinds/properties, unsafe parts and unsupported owners fail in every mode.

```csharp
using System.IO;
using System.Linq;
using WordNet;

using var input = File.OpenRead("form.docx");
WordDocument document = WordDocument.LoadWithControls(input, out var loaded);
// Use IDs from the authored template, not tags or paragraph indexes as handles.
DocumentContentControl checkbox = document.ContentControls.Single(c => c.Id == 42);
document.SetControlChecked(checkbox, true);
// Every committed edit invalidates previous mutation handles.
var refreshed = document.ContentControls;
using var staged = new MemoryStream();
ConversionResult saved = document.Save(staged, DocumentFormat.Docx);
File.WriteAllBytes("completed.docx", staged.ToArray());
```

`LoadWithControls(stream, controlOptions, out result, conversionOptions, token)`
captures the date mode once. Both overloads read the current stream position and
leave caller streams open. Mutations serialize, clone, validate/admit and commit
once. Failure/cancellation leaves model, version and committed diagnostics intact.
Save works on a copy and stages output; final caller I/O can still fail partway
through copying. Continue staging files before replacement.

`ContentControls` returns deeply detached, immutable snapshots in source order:
ID, kind, literal tag/alias, owner, lock, placeholder/value state, display, checkbox
state, selected stored value, combo free text, typed date, items, copied content
and binding metadata/state. Formatting getters return copies. Snapshots stay
readable after edits. The immutable collection is reused within a committed version;
repeated reads do not re-index or re-copy the document. Reacquire handles for mutations after **every** committed edit,
including metadata changes and zero-change operations. Foreign/stale/wrong-kind
handles fail. Saving and failed operations do not invalidate handles.

Missing IDs are assigned deterministically after reserving all explicit IDs;
duplicate IDs fail. Tags/aliases may repeat. Metadata title/author/custom-property
edits preserve controls. Ordinary content edits, Replace, bookmarks, Append,
Merge/MergeJson, document comparison and retained review/object combinations reject while typed controls
are retained. Merge rejects before evaluating caller data getters/JSON. Use an
explicit display view for subsequent ordinary editing; no typed tag is promoted
into executable template syntax.

## Kinds and owners

The published profile has inline owners in an ordinary body/table-cell paragraph,
or block owners containing ordinary body/cell paragraphs. The unreleased story
increment above adds bounded plain header/footer owners. A containing table is outside
ordinary paragraph controls; the separate structural profile above admits a row or
cell wrapper. Scalar controls have one formatting set and at most one paragraph;
checkbox content, including placeholders, is one character. No row/cell/table
wrappers except the unreleased subset above; no notes, boxes, section changes, or nested controls except for the
unreleased subset above; no embedded
objects or retained history inside a control. Crossing or enclosing field/bookmark
ranges fail; independent fields/bookmarks wholly outside owners are retained. A
closed body/cell bookmark immediately preceding a block control can be attached
to its first paragraph by the ordinary reader; final owner validation rejects that
placement explicitly before typed inference or editing can remove it.

| Kind | Explicit edit | Semantics and limits |
| --- | --- | --- |
| Checkbox | `SetControlChecked` | Preserve declared BMP glyph/font metadata and checked state; omitted metadata uses published Office 2010 defaults. Reject malformed/nonprintable/surrogate glyph codes. |
| Dropdown | `SetControlSelection` | Exact ordinal stored-value membership; null clears selection. Empty stored value differs from no selection. |
| Combo | `SetControlSelection`, `SetControlComboText` | Choose a member or bounded literal free text; empty text clears. Label/bound-member collisions require the selection API. |
| Date | `SetControlDate` | Explicit immutable value matching the captured date mode; null clears fullDate/display. |
| Inline rich text | `ReplaceControlContent` | Copied `DocumentText` runs: bold, italic, single underline, font, size, RGB; no images/fields/structural breaks. |
| Block rich text | `ReplaceControlParagraphs` | Copied `ContentControlParagraph` values with ordinary paragraphs and admitted appearance; empty input retains one empty paragraph. |
| Plain text | `SetControlText` | Literal text; LF soft breaks require source multiLine. No rich XML interpretation. |

Rich snapshots retain admitted paragraph alignment, heading role, spacing, line
rule, keep controls and paragraph-mark appearance. New paragraph inputs expose
alignment, heading and before/after spacing; reusing a snapshot also retains its
other admitted paragraph appearance. New ordinary paragraphs use the DOCX
11-pt/zero-after fallback. Source styles remain stable within the originating
session; foreign snapshots materialize appearance/heading role with information,
without importing foreign style identities. Unsupported inherited numbering,
indent/tab definitions, code styles and richer run/paragraph properties reject.
Fields/bookmarks/other controls outside a replacement remain intact. Explicit or
style-inherited paragraph direction (`w:bidi`) survives DOCX save/reload and same-session
paragraph snapshot replacement; this adds no native-control rendering claim.

Common Word 2010+ paragraph/text IDs and revision-session attributes, proofing
markers, cached `lastRenderedPageBreak` and bounded language metadata are admitted
and omitted with informational `Control.EditorMetadata`. IDs/session attributes
require their eight-hex-digit lexical form. These are editor metadata, distinct
from the retained functional control `w:id`; actual revision markup stays rejected.
Inactive `cs`/`eastAsia` font slots, `hint`, `szCs`, `bCs` and `iCs` are admitted for
Latin-range text (letters/digits through U+024F and neutral punctuation/symbols)
with that same information. Non-Latin letters/digits, later combining marks or
supplementary characters with those script slots reject as
`Control.ScriptFormatting`, rather than silently applying the primary appearance.
Language metadata alone does not restrict ordinary Unicode text. Theme fonts and
other unknown/active formatting remain outside this subset. The authored
`word-editor.xml` fixture exercises this editor scaffolding for all six kinds;
it is not captured Word output or a general Word compatibility qualification.

List values and nonempty effective labels must each be unique; an omitted label
uses its value. A dropdown nonmember cache stays explicitly unresolved, with
information, until a valid selection. An unbound combo nonmember cache is free
text. Unbound lastValue is preserved but is not an index/selection authority.

Placeholders are separate from typed values. `ShowControlPlaceholder` explicitly
restores preserved authored content; no prompt is invented. Preserve a local
placeholder reference only with its admitted glossary entry/relationship: one
ordinary paragraph of text, with bounded name/description/GUID and static
bbPlcHdr/content metadata. Missing, duplicate, unresolved built-in or richer entries
fail. A control without a glossary reference can retain its original placeholder
cache. Scalar placeholder formatting/size must fit its kind. Showing a placeholder
clears a typed date/selection; it cannot be written back as a value.

**Published 1.5.0 rejects Word-generated glossaries** when they include `w:category`
or their own styles/settings/fontTable/webSettings relationships. The unreleased
source subset above admits only the specified inert structures; richer parts are not
silently discarded. Default Word placeholder/template files can require additional
qualification beyond this subset.

## Dates

Default `ContentControlDateMode.UtcInstant` requires explicit-offset/Z XML dateTime
input and displays UTC. `ContentControlDateValue.FromInstant(offsetValue)` normalizes
an explicitly supplied instant. Calendar mode uses
`new ContentControlLoadOptions { DateMode = ContentControlDateMode.DateOnly }` and
`ContentControlDateValue.FromDate(year, month, day)`, years 1–9999. Imported calendar
values must be canonical midnight UTC fullDate; no non-midnight time/offset is
silently truncated. Save emits schema-valid dateTime; reopening with the same
explicit policy preserves interpretation. Mixed modes are outside this subset.

Require an explicit [F2 picture](roadmap-102-p1.md#field-pictures-109), Gregorian,
and absent/en-US/en-GB locale metadata with fixed English/invariant labels.
DateOnly pictures reject time tokens outside literals. Unsupported masks/locales/
calendars, invalid leap days, offset-less instants and excessive precision fail.
Absent fullDate retains a literal unresolved cache or placeholder without text
inference. Typed values refresh a mismatched cache informationally. Date controls
do not consult the clock; PDF document timestamps retain their existing clock
policy. Legacy DATE/TIME and merge-field formatting are unchanged.

## Explicit local XML binding

Bindings retain admitted local customXml data/properties parts, internal
relationships, store identity and prefix mappings. Matching GUIDs is
case-insensitive; property GUIDs are emitted in the schema-required uppercase
form with information. No remote relationships, DTD/entities, processing
instructions, implicit synchronization, schema fetching or closest-match search.
Admitted local stores, including unbound stores in a control session, survive
explicit detachment; detaching a binding does not delete caller XML data.

- `ApplyControlBindings(handles, token)` explicitly replaces selected values from XML.
- `WriteBackControlBindings(handles, token)` explicitly updates existing XML targets.
- `DetachControlBindings(handles, token)` explicitly removes mappings from unlocked controls.

Every selection is admitted before commit. A bound value edit becomes Dirty.
DOCX save requires explicit apply (discard the edit), write-back or detach.
An unchanged Unevaluated binding can be preserved, with information that Word or
another consumer may refresh it on open. The library does not control that
consumer's refresh behavior. All controls sharing a write-back target must be
selected and agree after conversion; conflicting values fail atomically.

Paths use `/QName` child steps, optional redundant `[1]` that cannot hide multiple
matches, and an optional final `/@QName`. Each step must be singleton; unprefixed
names mean the empty namespace. Prefix mappings are explicit. No `//`, `..`,
wildcards, unions, arbitrary indices/predicates/functions, namespace updates,
missing-target creation or structured/mixed subtree replacement. Element targets
contain literal text only. Stored strings are assigned as XML text/attributes,
escaped on write; markup and merge-looking values never execute.

| Value | Binding conversion |
| --- | --- |
| Checkbox | Exact true/false/1/0 input; canonical true/false write-back |
| Dropdown | Exact stored member value; display its label |
| Combo | Member value or literal nonmember; reject a nonmember colliding with another label |
| Calendar date | Matching DateOnly policy and storeMappedDataAs=date; canonical yyyy-MM-dd |
| Instant | Matching UTC policy and storeMappedDataAs=dateTime; explicit-offset input, UTC output |
| Plain text | Literal XML text/attribute string, with admitted multiline policy |

Rich-text dataBinding is rejected because OOXML defines it to be ignored. Date
text/omitted storage cannot supply a typed date without inference and fails
binding operations. Null, placeholder, unresolved and missing list selections
need a concrete value before write-back; a plain-text empty string is concrete.

## Output and editing locks

DOCX preserves admitted editable controls and local parts. Ordinary managed PDF/XPS
show selectable static value/placeholder content and report `Control.StaticDisplay`;
there are no interactive PDF fields. Available admitted checkbox glyphs render
with their face, otherwise ASCII `[x]`/`[ ]` reports `Control.GlyphFallback` while
DOCX declarations remain intact. No OS font discovery or new graphics/native
engine is added. Fonts are explicit caller assets or existing embedded fonts.

Single underline for control text uses bounded managed strokes at 0.1 em below
its baseline and max(0.5 pt, em/16) thickness; font-specific Word decoration parity
is not claimed. Underline with tab runs or justified/distributed paragraphs is
outside this initial PDF/XPS static profile and rejects at fixed-output save time.
Text/HTML/Markdown/other editable exports and display-view creation do not apply
that managed-stroke restriction or parse glyph fonts. They retain the original
Unicode checkbox text, including the default ☒/☐, without ASCII glyph fallback. Other inherited run decoration
remains outside the admitted control profile. Native typography, tagged/archive
PDF and retained-object/review combinations remain rejected pending separate
qualification. Existing unexecuted legacy fields outside owners retain their
ordinary export diagnostics; control preservation does not resolve those fields.

Other destinations reject editable loss in Strict and flatten display in explicit
BestEffort/Lenient with `Control.OutputLoss`. `CreateControlDisplayView(out result,
token)` returns a separate ordinary-editing session with wrappers, typed tags,
locks and local binding data removed; it reports that explicit loss informationally.
A view retains only bounded private glyph/font provenance for later PDF/XPS
preparation; its text is unchanged when it is created. Subsequent ordinary text
edits take precedence over that provenance, and default source-preserving Append
keeps the deferred display behavior. The original, its locks and its handles are unchanged. Any remaining source
warnings keep the existing Strict-save policy.

| Lock | Typed value/placeholder/application | Binding detach |
| --- | --- | --- |
| absent / unlocked | Allowed by kind | Allowed |
| sdtLocked | Allowed; wrapper deletion remains unavailable | Rejected |
| contentLocked | Rejected, including write-back | Rejected |
| sdtContentLocked | Rejected | Rejected |

No control insertion, wrapper deletion or lock-removal API is added. Locks are
editing behavior, never encryption, authentication or confidentiality.

## Resource and diagnostic contract

Input/output/expanded bytes, ZIP entries, characters, nesting and pages retain the
existing ConversionOptions bounds. DOCX output archive counts/expansion are checked
before copying to the caller. Additional ceilings are:

| Resource | Ceiling |
| --- | --- |
| Controls / local XML stores / glossary entries | 1,024 / 64 / 128 |
| Tag, alias, glossary name/description | 256 UTF-16 units |
| List items / aggregate items | 256 per control / 4,096 total |
| Item label/value / glyph font name | 1,024 / 256 units |
| Date picture / tokens / display | 128 units / 64 / 1,024 units |
| Namespace mappings / prefix / URI | 16 / 64 / 1,024 units |
| Path / child depth / QName component | 1,024 units / 16 / 64 units |
| Retained XML/control nodes, attributes included | min(100,000, MaximumCharacters) |
| Traversal/evaluation work | max(4,096, MaximumCharacters × 64), checked 64-bit |
| Detached paragraph input | 65,536 runs / 4,194,304 UTF-16 units before aggregate editing admission |

Retained metadata, source caches, XML names/values and structure estimates join
aggregate text/expanded-byte admission, including private display-view glyph
provenance. Shared placeholder prototypes are counted
once with bounded references. Checkbox font parsing uses a shared operation cache
with a conservative aggregate expanded-byte allowance. Replacement enumeration,
cloning/parsing, path walks and writes check cancellation before commit. These
finite logical budgets do not claim hard CPU/RSS isolation.

Hard failures use `Control.Malformed`, `DuplicateId`, `Unsupported`, `ProtectedOwner`,
`Locked`, `StaleHandle`, `Kind`, `Selection`, `DateMode`, `DateFormat`, `DateValue`,
`PlainText`, `Placeholder`, `ScriptFormatting`, `UnderlineProfile`, `OutputProfile`, `BindingDirty`,
`Binding.Store/Path/Type/Conflict` and `Limit.Controls` (plus existing resource/package
codes). Informational outcomes include `Control.IdAssigned`, `CacheRefreshed`,
`ChoiceUnresolved`, `DateUnresolved`, `GlyphFallback`, `GuidNormalized`,
`StyleMaterialized`, `EditorMetadata`, `StaticDisplay` and `BindingNotApplied`. `Control.OutputLoss`
is a fidelity loss for implicit destination flattening and information for the
explicit display-view operation. Diagnostics never include tag/alias, XML or
control values. No arbitrary scripting, forms UI, legacy ActiveX/forms, extra OCR
language, encryption/signature or new platform claim is introduced.

## Review corrections and qualification — 2026-10-27

All eight findings on PR #134 have concrete fixes or the requested explicit subset
clarification. Final owner validation prevents body/cell bookmarks from being lost;
Word-shaped editor scaffolding and paragraph direction are handled within the
bounds above; fixed-output preparation is deferred; snapshots reuse a committed
version; zero-control work and an unreachable post-write check are removed.

Qualified source `c15d3a8f0433f5e9ea3bc1d3efe57b88083b3872` passes **1,390/1,390** cases on
both local Linux x64 .NET 8.0.33 and 10.0.12, zero failures/skips, English OCR
prepared in the launching process. The 168 control cases include **35 new review
regressions**. All 34 acceptance groups map to passing methods on both runtimes.
Locked restore, five-target Release build (zero warnings/errors), formatting,
core pack/API compatibility and archive inspection pass. The [review receipt](measurements/roadmap-102-p2-review.json)
records comment-to-fix/test mapping, source/test trees, counters and artifact hashes.
Its final documentation follow-up preserves the qualified code/test trees.

The previous head `e006a88` passed all 24 hosted check conclusions, including
source/package/explicit-Standard, actual Framework and Android compilation jobs;
those results do not qualify this revised code. Final reviewed-head hosted checks subsequently passed before merge; the dated
receipt preserves its original pending-at-recording status. No Framework execution occurs on this Linux host, and no
new platform, general Word/consumer UI, native-control rendering, release or package
publication is claimed. The original receipt and reference failures stay dated.

## Local qualification — 2026-10-26

Qualified source commit `6f091129b0cccbc70be306a75d51215b64a64102` passes the complete
**1,355/1,355** cases on each of Linux x64 .NET 8.0.33 and 10.0.12, with zero
failures/skips and English OCR prepared in the launching PowerShell process.
The 133 new P2 cases cover all 34 mapped acceptance groups with independent
Office2010 schema, OPC/XML, PDF text/baseline and XPS stroke assertions. The two
runs use America/Los_Angeles and UTC respectively; date tests also exercise two
host cultures and a captured interpretation policy.

Locked restore, all-five-target Release build (zero warnings/errors), formatting
verification and core pack/API compatibility validation pass. Package inspection
confirms the five DLL/XML pairs match the qualified build, five symbol assets,
MIT/dependency notices and no test/customer/credential payload. These validation
archives retain the unchanged version 1.4.0 and are **not** the public packages.
The [receipt](measurements/roadmap-102-p2.json) records source/test trees, TRX,
fixture/font/archive hashes, per-class counts and tooling limits. The following
receipt/docs commit does not change the qualified source/test trees.

No Framework test host ran on that Linux machine. At that checkpoint, hosted
Windows/Linux x64/ARM64, exact-package/explicit-Standard and actual-minimum-runtime
checks remained separate and pending for the new PR head; later qualification is
recorded above. No consumer Word/UI appearance or refresh, pixel
parity, Android/macOS execution, new platform or publication is claimed. Resource
regressions cover selected independent/aggregate ceilings, not every possible
threshold or hard CPU/RSS isolation. Historical reference failure verdicts and
qualification receipts remain unchanged.
