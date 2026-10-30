# Bounded P2 objects and typography

## 1.4.0 roadmap #99 profile

Version 1.4.0 extends the 1.3.0 profiles with the bounded changes in
[roadmap-99-implementation.md](roadmap-99-implementation.md): direct HTML/ODT PDF export,
chart binding, scalar fields, structural comparison and qualified layout/native/object
combinations. The [1.4.0 release notes](releases/1.4.0.md) record compatibility and
qualification requirements. Earlier release receipts below remain historical evidence.

The work in #64–#73 and the revised integrated checkpoint #82 ships in 1.3.0,
building on P1 PR #85. Encryption/decryption and DOCX/PDF signatures
(#74–#81) were cancelled and are absent. The staged design in
[charts and equations](design/charts-equations.md) remains the implementation contract.

Preservation admits body/cell inline clustered-column and ordinary line charts,
with one category/value axis, finite linear data, and an optional single visible
plain-value XLSX sheet. Charts retain an allowlisted local part graph; embedded
data never executes or refreshes. Cache/range values must agree. OMML admits text,
rows, fractions, sub/superscripts, square/nth roots and paired simple delimiters.
Object placements, graph bytes and nested ZIP/XML work share operation limits.
Outer and nested ZIP entries share the archive allowance. Chart/workbook XML is
scanned against aggregate node/text costs before allocating its element trees.
Caps remain 128 objects, 32 chart parts, 16 series, 10,000 points per chart,
100,000 total points/cells, 1,024 characters per label, and 10,000 math nodes/depth 32.

The chart renderer uses managed text/vector primitives from semantic values.
Missing values are gaps; zero is a real point. Columns use zero as baseline; lines
break at gaps. The value axis includes zero, uses finite linear scaling and at most
eight ticks with a 1/2/5 decimal step. All-zero data uses the explicit range [-1,1].
Empty data, non-finite geometry, ranges too extreme to resolve, or labels that
cannot fit their declared slots fail. No thumbnail substitutes for source data.
Independent geometry checks use a locked 0.5-point tolerance.

Equation layout requires a caller-supplied, embedding-permitted static font with
an admitted OpenType MATH 1.0 table and Unicode format-12 cmap. Fraction/script/root geometry uses actual MATH
constants and qualified variants/assemblies; ordinary Latin metrics are not a
fallback. The test math font is a pinned OFL development asset and is not bundled
into production packages. Unsupported math and excessive construction work fail. Script sizing stops at the font's scriptscript size; radical rules meet the selected glyph's actual top. Control-character formatting does not override visible equation run formatting.

Creation/update APIs use immutable definitions and session/version handles.
Mutations stage complete graphs/expressions and resource admission before commit;
stale/foreign handles and cancellation leave the session unchanged. Chart editing
rebuilds both caches and real XLSX cells. Equation editing emits genuine OMML.
Ordinary operations that cannot safely retain/remap object owners reject explicitly.
Object PDF/XPS/SVG export reports editable-object loss; PDF/A and tagged/UA object output and
native-typography/object combinations remain outside this profile.

Native typography remains opt-in and provider-based. Its qualified ordinary
paragraph extensions preserve logical Unicode separately from glyph order, keep
clusters intact and reject unsupported stories/fields/objects/vertical layout.
Tagged native output requires its own logical carrier/structure and independent
validation. Neither native shaping nor tags alone establish general accessibility.
No new platform or OCR language is qualified. The [1.3.0 release record](releases/1.3.0.md)
links the completed exact-main qualification and public-package verification.

## Public object editing

`Objects` returns immutable snapshots with private session/version validity. Use
`AddChart`, `UpdateChart`, `AddEquation`, `UpdateEquation` and `RemoveObject`;
refresh handles after every successful edit. Removal deletes the entire placement
and its workbook graph. Removing a newly added object also removes its dedicated
empty paragraph; imported paragraph/cell structure is retained. Ordinary paragraph/table appends and metadata edits retain
objects; replacing an object owner, executing templates around it, restyling
composition, review ranges and matching-structure comparison reject until aware.

A `ChartDefinition` copies category and series lists, finite nullable values,
explicit RGB colours, title, legend choice and category/value axis titles.
Static rendering places the value title horizontally above the plot. It uses the
documented canonical vector profile and does not claim pixel parity with every
retained source style. Reversed/custom axes, visible column borders, marker/effect/data-label styling and
other unqualified appearances remain preservable only when the graph is admitted;
static export rejects those appearances. Unknown graph namespaces and other valid out-of-profile constructions fail session
admission with `NotSupportedException`; malformed graphs, unsafe relationships,
ambiguous IDs and cache/workbook mismatches raise `InvalidDataException`.
Ordinary managed Lenient/BestEffort conversion can omit an unsupported chart/equation with
`Object.UnsupportedLoss`. Strict conversion and document-session loading reject
that loss. Native typography and tagged/UA/PDF-A destinations also reject unsupported
objects before omission, preserving their separate combination boundaries. Malformed input, resource limits and unsafe relationships remain hard
errors in every mode; admitted objects with unsupported visible appearance still
reject static rendering. Word inline anchor/edit identifiers are harmless metadata.

Equations use the closed immutable `EquationExpression` factories. Size is in
points. Public trees must also fit serialized OMML nesting, so very deep semantic
trees may hit the XML limit first. Descriptions are separate session/export metadata;
OMML has no ordinary alt-text field, so reloading derives a bounded textual
expression description. Callers should update meaningful descriptions when editing
business semantics. `UpdateChart` and `UpdateEquation` accept a `description` overload: null
preserves the old description, an empty string clears it, and a new value updates
it atomically with the content. Images and thumbnail caches never stand in for these objects.

```csharp
var options = new ConversionOptions(FidelityMode.BestEffort);
options.Fonts.MathFamily = "STIX Two Math";
options.Fonts.Sources.Add(FontSource.FromFile("STIX Two Math", "caller-math.ttf"));
var doc = WordDocument.Create(options);
doc.AddChart(new ChartDefinition(ChartKind.Line, new[] { "Q1", "Q2" },
    new[] { new ChartSeries("Amount", "2468AC", new double?[] { 12, null }) }));
doc.AddEquation(EquationExpression.Fraction(
    EquationExpression.Text("x"), EquationExpression.Text("3")));
// Save DOCX for editable semantics; PDF/XPS export diagnoses their loss.
```

## Unreleased source: bounded matrix equations

Pending #168 source work adds rectangular `EquationExpression.Matrix` trees with
at most 64 rows, 64 columns and 1,024 cells. Rows and alignment arrays are copied;
`Children` is row-major and a null cell becomes an empty text node. At least one
cell must be nonempty. Matrices have no implicit brackets; use `Delimiter` for
those. Column alignments can be left, center or right; row and column spacing
can be standard, one-and-a-half or twice the standard value. The existing tree,
serialized OMML, cancellation and aggregate resource limits still apply.

The managed layout centers the complete grid on the font's MATH axis. Each column
has the width of its widest cell and standard column spacing is one em. Each row
uses the largest cell extents and a one-em minimum strut; the gap is the font's
nonnegative MATH leading plus zero, half an em or one em for the selected row
spacing. Empty cells draw no placeholder. These deterministic rules do not claim
pixel equivalence with every Office renderer. Layout requires the same explicitly
selected MATH font as existing equations and never silently uses ordinary font metrics.

The admitted OMML matrix has rectangular rows, a centered baseline, hidden empty
placeholders, supported column alignment groups and standard relative spacing.
Its mathematical runs retain the existing uniform-size and supported plain/italic
appearance limits; other retained appearances are not silently rendered.
Visible placeholders, top/bottom baselines, custom spacing, minimum column widths
and unknown matrix properties remain unsupported with `Equation.Matrix*` errors.
Existing `Object.UnsupportedLoss` behavior governs ordinary lenient conversion of
valid unsupported objects; strict/session and other profile boundaries remain unchanged.
Public editing, DOCX composition and ordinary managed PDF/XPS/SVG have focused
source tests; this work is not in published 1.5.0 and does not add native, tagged or
archival qualification.

```csharp
var grid = EquationExpression.Matrix(new[] {
    new EquationExpression?[] { EquationExpression.Text("a"), EquationExpression.Text("b") },
    new EquationExpression?[] { null, EquationExpression.Fraction(EquationExpression.Text("x"), EquationExpression.Text("2")) }
}, new[] { EquationMatrixAlignment.Left, EquationMatrixAlignment.Right },
   EquationMatrixSpacing.Standard, EquationMatrixSpacing.OneAndHalf);
doc.AddEquation(EquationExpression.Delimiter(grid, "[", "]"));
```

## Native text and accessible output

Set `Typography.Enabled` and `Typography.ExtendedBidi` explicitly. The old enabled
Arabic profile and managed default remain separate and retain their earlier tests.
The extended profile applies Unicode 17 UAX #9 levels, isolate run sequences, bracket
resolution, mirroring and line reordering while retaining logical text. Embedding
and isolate controls must balance within each paragraph; default nesting is 32,
configurable through `MaximumBidiDepth` (1–125). Paragraph bidi work is capped at
131,072 UTF-16 units plus the byte budget. Controls are zero-width and produce no
visible outlines. Wrapping retains shaped words and Unicode 17 graphemes; a word
that cannot fit fails rather than splitting a conjunct.

Set `Typography.Devanagari` to admit Hindi U+0900–U+097F with Latin, ordinary
punctuation and contextual ZWJ/ZWNJ. HarfBuzz receives `deva`/`hi` and a caller
static font covering the entire grapheme/conjunct. Pre-base matras, reph, conjuncts,
combining signs and explicit join/nonjoin cases have pinned independent goldens.
Other Indic scripts, unattached marks, split styled clusters, lists, tables,
stories, fields, notes, drawings and history remain excluded. OCR remains English.

For tagged PDF/PDF-UA-1, supply a title, valid heading hierarchy and explicit
`Pdf.Language` (`hi[-IN]` for Hindi, `ar` for Arabic). Native Arabic plus Devanagari
in one document needs separately qualified language spans and is rejected.
Visible glyph outlines are artifacts outside semantic MCIDs; exactly one logical
ActualText carrier belongs to each source token. Link annotations retain their
source owner and ParentTree references. Fully covered carrier pages also import
as logical editable text; mixed uncovered pages keep the existing reconstruction
path. Missing or unsupported UA semantics fail even in Lenient mode. PDF/A and
native XPS remain rejected. A validator pass covers this bounded profile, not
universal accessibility or a claim of assistive-technology certification.

## Qualification and reproduction

The development fonts are pinned in `Fixtures/P2/math-reference.json` and
`devanagari-reference.json` with OFL notices. FontTools 4.61.1 extracts MATH constants,
italic corrections, corner kerns, glyph boxes, variants and assemblies. uharfbuzz
0.53.0 / HarfBuzz 12.1.0 supplies independent Hindi glyph/cluster/mark vectors;
FriBidi 1.0.16 supplies independent bidi samples. The complete unmodified Unicode 17
conformance inputs and hashes are retained: 91,707 character cases and 770,241
abstract directional combinations, alongside the earlier UAX #29 cases.

Set `WORDNET_P2_EVIDENCE` while running the suite to retain real DOCX/PDF/SVG output.
`eng/verify-p2-office.py` invokes the pinned development UNO client against installed
LibreOffice Writer/Calc/Math. CI pins all three components to
`4:24.2.7-0ubuntu0.24.04.7`; the client disables macros. It changes the chart from 10 to 42 through the chart data
model and the retained XLSX B2 cell through Calc, and changes the formula denominator
through the equation model. LibreOffice retains imported XLSX separately from the
chart cache, so the driver returns Calc's saved stream through Writer's documented
interop property; it does not patch ZIP bytes or pretend that changing a cache
alone edits the workbook. `eng/verify-p2.py --office-edited` checks actual chart/XLSX
values, OMML denominator, measured vector geometry and MATH baselines/rules within
the locked 0.5 pt tolerance, logical reading order and structure/link references.
The veraPDF 1.30.2 pinned image validates native Hindi, Arabic and link UA positives
and rejects untagged controls. Reading-order inspection checks one semantic occurrence
per token, headings before paragraphs, explicit language and link ownership.

The public [combined recipe](../examples/WordNet.Automation/README.md) produces
an ordinary report with editable objects and separate native UA chapters. The
[capability matrix](p2-capability-matrix.json) explicitly rejects unsupported
combinations. Encryption/decryption and DOCX/PDF signatures are absent by owner
request. Version 1.3.0 is published; its [release record](releases/1.3.0.md) links exact-main
source/package/runtime evidence. Compilation alone does not
qualify Framework, native architectures or a new platform.
