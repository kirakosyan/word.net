# Roadmap #102 P1 source profile

This branch implements #104–#111 for review; see the [capability map](roadmap-102-p1-capabilities.json). It is not a package publication or a
claim of qualification on an unexecuted runtime. Historical #103 receipts and
reference thresholds remain unchanged.

## STYLEREF contract (#111)

The initial profile admits `STYLEREF "style"` (or one unquoted identifier), optional
`\l`, and an optional final `\* MERGEFORMAT`. Resolve an exact ordinal paragraph
style ID first; otherwise require a unique ordinal-ignore-case style name. Preserve
source paragraph style IDs and names in DOCX; resolved appearance is direct
formatting. This does not preserve the original style-definition graph.

Only ordinary managed body paragraphs supply text, and only header/footer fields
are evaluated. Search within the owning section: first matching paragraph starting
on the current physical page (`\l`: last), then the nearest preceding paragraph,
then the first following paragraph. Empty/structured headings and fields in source
paragraphs are outside this subset. A split paragraph belongs to its first page.
Missing or ambiguous styles, unsupported switches, body uses
retain their caches with `Field.UnsupportedUsedCachedResult`. Native typography continues to reject fields under its existing structure gate. No title inference,
numbering switches or cross-section fallback is performed.

Calculate after body pagination. Recalculate header/footer reservations and results
for at most four passes alongside reference fields; nonconvergence is diagnosed
through `Field.LayoutDidNotConverge`. DOCX retains editable instructions and safe
caches; saving DOCX does not invent page-sensitive results.

## DOCX defaults and placement (#104–#105)

Resolve document defaults, the selected/default paragraph style and its `basedOn`
chain, character style, then absolute direct run properties. Character styles are
applied before direct properties regardless of element enumeration order. Existing
bold/italic toggle interpretation remains covered by the earlier regressions.
Absent values remain absent in the semantic model until rendering: ordinary DOCX body/cell/story
paragraphs use deterministic 11 pt and zero after-spacing. This replaces the earlier
context-dependent 12 pt body / 11 pt cell policy and implicit 6 pt after-spacing.
Notes without a source size retain their 9 pt context size, and headings retain their existing contextual size. Other readers and newly composed sessions keep their existing policies. This is an
explicit fallback policy, not a claim that every office editor selects these values.

Explicit sizes, inherited values and explicit zero before/after spacing win. Zero,
negative or malformed font sizes fail; duplicate style IDs and cycles fail. Multiple paragraph defaults use the last declaration with an informational diagnostic. Traversal uses the existing nesting bound. Theme/script properties
outside the profile retain their diagnostics. DOCX output materializes admitted
appearance and retains paragraph style IDs/names, without inventing source identities
or claiming preservation of arbitrary style graphs.

Paragraph marks have their own run metrics, used for empty paragraphs and preserved
in DOCX. They do not override nonempty run formatting. Paragraph-relative exclusion
measurement includes the paragraph's before-spacing; keep/page/column remeasurement
retains counter snapshots. Explicit `auto` line amounts multiply resolved line height;
`atLeast` and `exact` amounts use points. Nonempty lines smaller than their content
retain `Layout.LineSpacing` diagnostics and expand to avoid clipping. Empty exact
lines retain their requested height. Automatic/contextual/character-unit combinations
outside this subset remain diagnosed.

## Implicit autofit (#106)

Managed DOCX tables with omitted `tblLayout` now use the bounded content-sizing
policy, like explicit autofit. Preferred absolute/percentage table and cell widths,
span constraints, padding and containing width contribute to sizing. Without a
preferred width the containing width is available. Preferred widths are constrained
to that width. Minimum glyph/image constraints and preferred line advances distribute
columns deterministically; long words can break at glyph boundaries. Explicit fixed
tables retain their grid/cell-width policy, including existing physical-page checks.
Nested tables receive the measured parent cell's inner width. Measurement uses the
same safe glyph fallback and calculated scalar text as line placement, with an
aggregate work allowance of `max(4096, MaximumCharacters * 64)` units per operation.
No-fit content retains existing diagnostics/failure and staged output behavior.

The qualified native implicit-table profile retains its earlier fixed-grid policy,
with informational `Table.NativeImplicitGrid`; new managed sizing does not qualify
new native combinations. Floating tables and other unsupported native structures
remain outside the profile. Binary DOC export diagnoses explicit autofit/relative-width loss. Omitted layout remains unchanged in the shared model for DOC, ODT and HTML; only managed fixed-page sizing opts into implicit autofit.

## Conventional notes (#107)

A leading explicit `footnoteRef`/`endnoteRef` now owns its source separator text.
The renderer no longer inserts a second space before the preserved source space.
Sources without a mark retain the existing generated label/space fallback; DOCX
output writes an explicit marker and that separator. Unsupported marker placements
are diagnosed. Standard separator and continuation-separator notes are emitted.
The existing 40% reservation and indivisible-line bounds remain unchanged. The
conventional 55-line control checks every line exactly once over five pages; the
historical three-versus-two-page continuation difference is not relabeled a defect.

## Typed custom properties (#108)

`WordDocument.CustomProperties` returns detached immutable snapshots sorted by name.
`SetCustomProperty` adds/replaces an immutable `DocumentProperty`; `RemoveCustomProperty`
returns whether a case-insensitive ordinal name existed. Replacement keeps the new
name spelling. Updates clone, admit aggregate resources and commit atomically;
cancellation leaves the session unchanged. Each operation accepts a cancellation
token. Names contain 1–255 XML characters; at most 1024 properties are admitted.
Names, displayed values, document/review text and node estimates count toward the
existing character/expanded-byte limits. Standard Title/Author take precedence over
custom names in DOCPROPERTY lookup. Empty strings are present values; absent names
retain cached fields with `Field.UnsupportedUsedCachedResult`.

| API value | DOCX value | Default display |
| --- | --- | --- |
| `string` | `vt:lpwstr` | Original Unicode text |
| `bool` | `vt:bool` | `true` / `false` |
| `decimal` | `i4` for in-range integers, `r8` for 15-digit exact display, otherwise lossless `decimal`; input also checked `i8` | Invariant decimal |
| `DateTimeOffset` | `vt:filetime` with explicit offset | UTC ISO instant |

Duplicate names (ignoring case), duplicate IDs and malformed admitted values fail.
Finite `r8` values are admitted only when the decimal projection preserves their binary64 numeric value. Unsupported types and linked properties are informationally diagnosed and omitted; their unresolved
fields keep caches. Only DOCX preserves custom-property metadata; other exports
report informational `Metadata.CustomProperties`. This is not arbitrary OPC editing.

```csharp
document.SetCustomProperty(new DocumentProperty("Amount", 1234.565m));
document.SetCustomProperty(new DocumentProperty("Issued", suppliedDateTimeOffset));
// Existing DOCX fields may use:
// DOCPROPERTY Amount \# "#,##0.00"
// DOCPROPERTY Issued \@ "yyyy-MM-dd"
```

## Field pictures (#109)

Pictures require one quoted token after `\#` or `\@`; an optional final
`\* MERGEFORMAT` remains admitted. Numeric values are checked decimals; dates must
be explicitly typed offsets. Invariant culture and UTC are used, without host locale,
calendar, time-zone or current-date inference. Existing DATE/TIME clock behavior and
legacy mail-merge formatting are unchanged.

| Picture | Exact admitted tokens/rules |
| --- | --- |
| Numeric integer | `0` or `#,##0` |
| Fraction | Optional dot followed by 1–10 `0` digits, or 1–10 `#` digits |
| Numeric literals | Optional single-quoted prefix/suffix; no embedded quote/newline |
| Rounding/negatives | Midpoint away from zero to picture precision; leading minus |
| Date | `yyyy yy M MM MMM MMMM d dd ddd dddd H HH h hh m mm s ss tt` |
| Date separators | Hyphen, slash, space, colon, comma, dot; single-quoted literals |
| Escapes in quoted field tokens | `\"` and `\\`; other backslashes remain literal |

Patterns are at most 128 characters, date patterns at most 64 tokens and displayed
picture results at most 1024 characters. Exponents, percentages, sections, arbitrary
format providers, zones and inferred dates are unsupported and preserve complete
caches with informational diagnostics. Decimal overflow/division by zero also use
that safe behavior. Pattern/text expansion and cancellation are admitted before
caller output is copied.

## Nested scalar fields (#110)

DOCX complex begin/instruction/separate/end sequences and nested simple fields retain
a bounded tree. IF operands may be quoted text, invariant decimal literals or typed
nested DOCPROPERTY/arithmetic/IF values; its two results are quoted text or nested
values. Nested fields may occur inside quoted operands/results; values become text
inside that token and cannot introduce operators or switches. Supported numeric,
ordinal-string, Boolean and UTC-date comparisons require matching types. Both child
branches are validated before selecting a result. Supported arithmetic remains
checked decimal `+ - * /` and parentheses. There is no spreadsheet or script engine.

Depth is at most 16 (also bounded by MaximumNestingDepth), with at most 256 nodes per
evaluation, 4096 instruction characters and 256 tokens per node, plus aggregate work
and output bounds. OOXML builds an acyclic tree; evaluation also detects repeated
nodes. Unsupported/missing children preserve the whole outer cache, never partially
execute flattened fragments. Structured or unsupported instruction trees outside
retention keep cached runs with diagnostics. Repeated separators are ignored with `Field.DuplicateSeparator`; genuinely unbalanced ends and unclosed fields fail atomically. DOCX writes admitted nested instructions as
complex field boundaries; no internal placeholder is serialized into instructions.

## Evidence and remaining qualification

`P1LayoutTests`, `P1FieldTests` and `P1StyleReferenceTests` provide independently authored
OOXML, Open XML schema validation, specified string expectations, pinned-font metric
coordinates and independent PDF/XPS extraction. Existing explicit-size, style-toggle,
page/column, table, note, merge, review and native tests remain enabled. Historical
coordinate/raster controls now use detached inputs with their original font sizes,
spacing and fixed grids made explicit by `ExplicitLayoutFixture`; original archives,
reference outputs and tolerances are unchanged. New omitted-value controls remain
separate.

`eng/measure-p1-layout.py` compares unchanged-parent and candidate workers over all
24 original corpus documents and emitted P1 probes, recording hashes, diagnostics,
page/text changes and coordinates. Its source-to-source differences are not independent
rendering verdicts. Fresh office comparisons, runtime checks and final counts are
recorded separately in the local validation section below. No Windows/ARM64,
minimum-Framework or published-package qualification is inferred from Linux execution.

## Local validation — 2026-10-26

All five targets build in Release with zero warnings/errors. Locked restore and local
core pack pass; API validation runs during packing. Package inspection verifies five
DLL/XML pairs, MIT licensing and absence of test/tool assets. Formatting verification
passes; the SDK reports a workspace-loading advisory on this Linux host. Dependency
versions, lock files, package versions and target frameworks are unchanged.

The complete Linux x64 suites pass **1,196/1,196 on .NET 8.0.33** and
**1,196/1,196 on .NET 10.0.12**, each with zero failures/skips and English OCR prepared
in the launching PowerShell process. There are **57 new P1 cases**. Thirteen comparison
controls and twenty historical-evidence guards pass. TRX counters, individual outcomes
and hashes were independently checked. Source implementation belongs to tree `210f9a7`;
the final historical table-control input also explicitly pins its previous spacing.
This local source validation does not execute Windows, ARM64 or Framework runtimes.
Those remain distinct CI gates; no Android/macOS release support or publication is added.

The [receipt](measurements/roadmap-102-p1.json) retains both workers' binary hashes,
36 before/after measurements, all 36 fresh reference verdicts, pinned fonts,
reference executable/PDF identities, unchanged thresholds and runtime report hashes.
Fresh references use office build `26.8.1.1 bf6ef2662401c7d6dce671ab4deb26ee0af3a7c1`;
the downloaded test-only archive SHA-256 is
`30903df3b9f61360d9660cd707de48cd2831469114492a5008ed58a0ac77d044`.
These are new observations, separate from the immutable #103 historical receipt.
The independent binary-DOC reader was unavailable in this fresh measurement; no new
editable-DOC corpus-reader result is inferred from generated files alone.

| Independent glyph comparison | Unchanged parent (pt) | Candidate (pt) |
| --- | ---: | ---: |
| Ligature-free omitted defaults | 15.775 | 0.100 |
| Document-default size | 11.982 | 0.298 |
| Default paragraph style / based-on style | 11.982 | 0.100 |
| Original short mixed fonts | 22.666 | 0.261 |
| Original left floating logo | 60.100 | 1.512 |
| Original ledger long table | 7.826 | 5.500 |
| Original inventory long table | 9.696 | 5.500 |

All 24 originals and 12 probes pass authored text/order and page-count checks.
All **36 strict aggregate reference failures remain failures** at the original
thresholds (including **0.25 pt** glyph displacement). Original reference per-page
text passes 19/24; glyph placement passes 6/24 and raster passes 11/24. For the new
probes, reference per-page text passes 10/12 and glyph placement 3/12; no probe passes
the raster policy. Conventional notes retain five source pages versus two reference
pages under the unchanged reservation policy. Running-header ID/name policy and
implicit sizing retain documented reference differences. Passing individual metric
controls does not establish universal layout, font, accessibility or platform parity.

To reproduce source-to-source geometry after building the parent and candidate
benchmark workers, emit the independent test inputs using
`WORDNET_ROADMAP_EVIDENCE=/absolute/evidence` on the focused P1 test run, then run:

```sh
python eng/measure-p1-layout.py \
  --before-worker /absolute/parent/tools/wordnet.Benchmarks/bin/Release/net10.0/wordnet.Benchmarks.dll \
  --after-worker tools/wordnet.Benchmarks/bin/Release/net10.0/wordnet.Benchmarks.dll \
  --probes /absolute/evidence --output artifacts/p1-measurements
```

Use `eng/capture-office-comparison.py` and `eng/run-comparison.py` with the pinned
fonts and test-only Pillow requirements for fresh references. Keep their nonzero
strict-fidelity exit codes and every failed verdict. Raw local artifacts live under
ignored `artifacts/p1-*`; the committed receipt preserves identities, coordinates and
verdicts without bundling generated packages or external tool binaries.


## PR #133 review corrections

All fifteen inline findings have dedicated fixes and regression coverage. Semantic
run copies retain field trees through inline controls, conditions, repeats and both
composition policies. Nested field codes never reach DOC with internal placeholders:
Strict rejects `Field.NestedOutput`, while best effort exports intact cached text.
Paragraph-mark revisions precede mark formatting in schema order; mark color/code are
retained. Generated code styles preserve their monospace definition after reload.

Composition merges style identities with collision remapping and rewrites admitted
running-header references to stable IDs. It imports matching/new custom properties;
conflicting typed values fail atomically with `Composition.CustomPropertyConflict`.
Style catalog names/IDs participate in session aggregate admission. Unequal-column
continuations retain mark/default metrics. Duplicate paragraph defaults follow the
specified last-wins rule, and repeated separators remain tolerated with diagnostics.

Running-header reservation is confined to its owning section and distinct result
vectors. Static STYLEREF story heights are cached per section during each layout pass;
unrelated stories are not remeasured across the document's pages. Lookup/output work
is charged before expansion, and final unresolved diagnostics are emitted after
convergence. An 80-page/eight-section control checks this under an isolated low layout
budget. The source suite now requires 1,222 cases, including 26 review regressions.
Earlier 1,196-case receipts and geometry observations above remain dated evidence;
final reviewed-head/merge qualification is recorded separately after execution.
