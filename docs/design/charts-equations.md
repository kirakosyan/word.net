# ADR B5-OBJECTS: charts and equations in three stages

Historical design checkpoint: published 1.5.0 includes the subsequently implemented
bounded preservation, rendering and editing profiles described in [P2 objects](../p2-objects-typography.md)
and the [capability matrix](../roadmap-102-capability-matrix.json).

Design proposal for [#31](https://github.com/kirakosyan/word.net/issues/31),
2026-10-17. The editing foundation (#11) was merged; editable charts/equations were
then deferred. This design added no model node, adapter, renderer, dependency or public API.
Implementation first required a change to the deferred-scope policy in
[AGENTS.md](../../AGENTS.md) for the selected stage only.

## Decision and supported first subsets

Separate **preservation**, **static rendering** and **semantic editing**. None implies
the others. A valid retained chart part can remain editable in an external office
reader while wordnet exposes no chart editor; a raster preview alone is neither a
retained chart nor an editable equation. Do not reconstruct semantics from a cache.

| Object | P1 preservation | P2 static rendering | P3 public editing |
| --- | --- | --- | --- |
| Chart | Transitional DrawingML `c:chartSpace` for 2D clustered columns (`barDir=col`, `grouping=clustered`) and ordinary non-stacked lines, category axis plus one value axis, embedded literal caches or one safe embedded XLSX. | Same types; linear finite numeric values, category labels, simple axes/gridlines/legend, explicit RGB and ordinary text. Render from admitted data, never execute Excel formulas. | Create/change series names, category strings, finite values and simple title/axis labels; emit consistent caches and a minimal embedded workbook. |
| Equation | Inline/display OMML `oMath`/`oMathPara` with text runs, fractions, subscript, superscript, combined sub/sup, square/nth roots and simple delimiters. | Same constructs with explicit math layout and a qualified caller/pinned math font; expose visual output and a separate textual description. | Provider-neutral immutable expression tree for those constructs; emit real OMML, including numerator/denominator and script structure. |

Out of all first stages: pie/doughnut/scatter/bubble/area/stock/radar/surface/3D,
stacked/combined/secondary axes, logarithmic/date axes, trends/errors/advanced labels,
ChartEx, SmartArt, arbitrary shapes, linked spreadsheets and macro workbooks. Math
excludes matrices, equation arrays/alignment, integrals/sums/limits, accents, tensors,
arbitrary n-ary operators, LaTeX parsing, MathType/OLE and automatic line breaking.
Unsupported types do not become supported merely because their parts can be copied.

## P1: validated source preservation

Add an internal inline/block `PreservedObject` with kind, anchor/extent/alt text and
an immutable, admitted package-part graph. OMML is a validated immutable expression
fragment; charts own only an allowlisted closure: chart XML, its relationships,
optional chart style/color parts, admitted PNG/JPEG resources and one embedded XLSX.
Audit `DocumentModel` traversal, cloning, admission, composition and writers first:
today they have no such node or generic retained-parts store. Never append an arbitrary
ZIP archive to the semantic model or copy unknown relationships wholesale.

Store part content types and hashes, parsed safe references and aggregate costs.
Allocate fresh document drawing IDs, chart part names and relationship IDs on save or
composition; rewrite *all* owner references, including style/color/workbook links.
Unchanged binary resources keep exact hashes; XML equality is semantic after deliberate
ID/name rewrites, not a byte-for-byte package promise. Maintain private session-scoped
object IDs independently of source `rId` values. Missing parts, ambiguous graph owners,
duplicate IDs, wrong content types and cycles fail admission.

P1 preserves supported source formatting which P2 cannot necessarily render. Inspect
the full source against the P2 render subset before rendering; unsupported style/effects
produce an explicit loss, never a claim of faithful reproduction from a thumbnail.
DOCX → DOCX save can be loss-free for the admitted object graph; DOC/PDF/XPS/HTML and
ebooks cannot retain its editable semantics. Strict rejects those losses until a
destination-specific preservation contract exists. Permissive conversion can use an
explicitly requested available PNG/JPEG preview with an editable-object loss and a
cache-may-be-stale warning, or an alt-text placeholder with a visual loss. No preview
is invented when absent. Unknown object forms retain only safely available fallback
content under current loss policy, never an unvalidated opaque graph.

Ordinary text edits around an object can preserve its ID. Editing/removing its anchor,
replacing a containing range, repeating or conditionally deleting its owner must copy,
remap or delete the entire graph atomically; reject until that operation is object-aware.
Shared immutable resources may deduplicate bytes but each placement costs a node.
Cross-document composition must isolate shared graphs so editing one chart cannot
modify another session or placement silently. Reject unsupported story/anchor placement.

## Workbook/data and relationship safety

Require package-local OPC targets resolved by the existing safe canonical rules;
no HTTP/file/UNC targets, remote fonts/images, DDE, OLE, ActiveX, scripts or macros.
Never refresh an external workbook. Reject external relationships anywhere in the
admitted closure, including nested workbook parts, and reject unsigned assumptions
about a file based on extension. Signed object/package preservation needs a separate
decision; rewriting IDs invalidates signatures, so fail rather than retaining a false
signature claim. Use no implicit process launch or spreadsheet calculation engine.

For a preserved workbook, recursively validate a bounded **non-macro XLSX** package
with only the required workbook, sheet, relationship, shared-string, style and property
parts. Reject external links, connections, queries, defined names pointing externally,
embedded objects, VBA, worksheet cell formulas and hidden extra worksheets/data in the first profile.
Accept one plain-value visible sheet, inline/shared strings and finite numeric cells;
chart reference expressions may name only bounded local ranges on that sheet (not calculated formulas). Require chart caches and
workbook values/category counts to agree. Literal-only charts can be retained without a
workbook, but P3 emits one for external data editing. A missing/stale cache/workbook
mismatch fails admission; do not silently choose one source as truth. Retaining a
workbook may disclose data, so describe its presence and require explicit object removal
for redaction. Clearing a preview does not remove embedded data.

Use current ZIP/XML/text/output limits across the outer graph and *all* nested ZIPs;
do not reset budgets per workbook. Charge actual expanded bytes even for parts parsed
only for validation, and debit the decoded-image pixel budget. Proposed caps: 128
objects, 32 parts per chart graph, 16 series, 10,000 points per chart / 100,000 total
points, 100,000 worksheet cells, 10,000 categories per chart before text budgets,
1,024 characters per label, and 10,000 equation nodes at depth at most 32 or the
configured nesting limit. Categories/points must satisfy both applicable caps; all
counts remain subordinate to aggregate node/byte limits. Detect excessive declared
ranges, cycles, NaN/infinity and expansion arithmetic before allocation. Check
cancellation per part/series/node; host isolation owns hard native/CPU/RSS limits.

## P2 and P3: rendering, editing and providers

Implement chart geometry as managed primitives through the existing PDF/XPS/SVG layout
boundary, then optional PNG rasterization through `wordnet.Ocr`. Use current measured
text and explicit fonts; no mandatory native renderer in core. First rendering slice
must define empty/missing data, zero/negative values, axis range/rounding, overlap limits
and point-to-page transformations; fail unsupported geometry instead of producing
plausible invented plots. No full spreadsheet formula engine is selected.

Equation layout needs a separate qualified OpenType MATH metrics reader and stretchy
delimiter policy; ordinary Latin text layout or HarfBuzz shaping alone does not supply
math layout. Reuse existing font admission and embedding permissions, use a suitably
licensed/pinned math font for tests and avoid shipping it until its notice/size review.
Use authored managed OMML/layout code if the small subset is viable. Existing PdfPig
and optional Skia packages remain; no new production dependency is selected. Evaluate
maintained render libraries only in P2's implementation decision with license, five-TFM,
native-asset and maintenance evidence. LibreOffice is an independent visual reader,
not a runtime backend. No proprietary renderer code/resources may be copied.

Proposed, unavailable API additions after P1/P2 qualification:

```text
document.Objects                         // immutable ID/kind/extent/alt snapshots
document.AddChart(ChartDefinition, placement, cancellationToken)
document.UpdateChart(objectHandle, ChartDefinition, cancellationToken)
document.AddEquation(EquationExpression, placement, cancellationToken)
document.UpdateEquation(objectHandle, EquationExpression, cancellationToken)
```

Definitions carry provider-neutral arrays/enums/strings, never XML, Excel formulas,
provider types or file paths. Handles follow session/version validity; mutations clone,
validate, rebuild caches/workbook or OMML, and admit all resources before committing.
No public object editing surface in P1. Each later addition must preserve constructor
metadata/API compatibility on all five TFMs and include XML docs and actual samples.

PDF/UA requires meaningful chart description or equation text semantics separately;
an image tag with arbitrary alt text does not qualify mathematical accessibility.
Keep UA conversion rejected for objects without a qualified semantic representation.
Do not advertise editable or accessible support from a successful PNG comparison.

## Independent assertions and implementation slices

The [acceptance/provenance plan](acceptance.md) defines structural, visual and external
editing controls. Required scenario: preserve a synthetic chart and fraction DOCX;
open it in an independent office reader, change a series value through its chart-data
editor and change the fraction denominator through its equation editor; save, reopen,
and inspect real chart/XLSX and OMML values. Rendering a cached image cannot pass this.
Record reader/version, before/after hashes, source values and exported visual evidence.

Implement P1a graph/model/admission, P1b DOCX preservation/remapping, P2a chart rendering,
P2b math font/layout, then P3a chart editing and P3b OMML editing as separate tasks.
P1b depends on P1a; P2/P3 depend on preservation and their own data/layout gates. Each
requires an explicit stage-specific scope-policy update, independent positive/negative
fixtures, complete source/packed/API checks and affected README, capability, diagnostics,
usage/sample and testing updates. The design issue closes on this plan, not those tasks.

## References

- [DrawingML external chart data](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.drawing.charts.externaldata), [Microsoft chart/embedded-package overview](https://learn.microsoft.com/en-us/office/dev/add-ins/word/create-better-add-ins-for-word-with-office-open-xml).
- [OfficeMath representation](https://devblogs.microsoft.com/math-in-office/officemath/), [OpenType MATH table](https://learn.microsoft.com/en-us/typography/opentype/spec/math).
- [Existing semantic model](../../src/wordnet/Model/DocumentModel.cs), [DOCX drawing reader](../../src/wordnet/Formats/DocxReader.Fidelity.cs), [composition design](../editing-template-design.md).
