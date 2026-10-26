# Typed DOCX content controls — roadmap #102 P2 source

PR [#134](https://github.com/kirakosyan/word.net/pull/134) implements #112–#117 as
one owner-authorized batch. This is source under review, absent from public
1.4.0; it does not publish packages. The [design record](content-controls-design.md)
and [acceptance map](content-controls-acceptance.json) describe the contract and
independent gates. Qualification results are recorded separately after execution.

## Load, inspect and edit

Typed preservation is opt-in and DOCX-only. Existing `Load` and ordinary conversion
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
readable after edits; reacquire them for mutations after **every** committed edit,
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

All controls have inline owners in an ordinary body/table-cell paragraph, or block
owners containing ordinary body/cell paragraphs. A containing table is outside the
control. Scalar controls have one formatting set and at most one paragraph;
checkbox content, including placeholders, is one character. No row/cell/table
wrappers, headers/footers, notes, boxes, section changes, nested controls, embedded
objects or retained history inside a control. Crossing or enclosing field/bookmark
ranges fail; independent fields/bookmarks wholly outside owners are retained.

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
Fields/bookmarks/other controls outside a replacement remain intact.

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
outside this initial static profile and rejects. Other inherited run decoration
remains outside the admitted control profile. Native typography, tagged/archive
PDF and retained-object/review combinations remain rejected pending separate
qualification. Existing unexecuted legacy fields outside owners retain their
ordinary export diagnostics; control preservation does not resolve those fields.

Other destinations reject editable loss in Strict and flatten display in explicit
BestEffort/Lenient with `Control.OutputLoss`. `CreateControlDisplayView(out result,
token)` returns a separate ordinary-editing session with wrappers, typed tags,
locks and local binding data removed; it reports that explicit loss informationally.
The original, its locks and its handles are unchanged. Any remaining source
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
aggregate text/expanded-byte admission. Shared placeholder prototypes are counted
once with bounded references. Checkbox font parsing uses a shared operation cache
with a conservative aggregate expanded-byte allowance. Replacement enumeration,
cloning/parsing, path walks and writes check cancellation before commit. These
finite logical budgets do not claim hard CPU/RSS isolation.

Hard failures use `Control.Malformed`, `DuplicateId`, `Unsupported`, `ProtectedOwner`,
`Locked`, `StaleHandle`, `Kind`, `Selection`, `DateMode`, `DateFormat`, `DateValue`,
`PlainText`, `Placeholder`, `UnderlineProfile`, `OutputProfile`, `BindingDirty`,
`Binding.Store/Path/Type/Conflict` and `Limit.Controls` (plus existing resource/package
codes). Informational outcomes include `Control.IdAssigned`, `CacheRefreshed`,
`ChoiceUnresolved`, `DateUnresolved`, `GlyphFallback`, `GuidNormalized`,
`StyleMaterialized`, `StaticDisplay` and `BindingNotApplied`. `Control.OutputLoss`
is a fidelity loss for implicit destination flattening and information for the
explicit display-view operation. Diagnostics never include tag/alias, XML or
control values. No arbitrary scripting, forms UI, legacy ActiveX/forms, extra OCR
language, encryption/signature or new platform claim is introduced.
