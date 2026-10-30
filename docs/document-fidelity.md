# Formatting and image profile

Unreleased source adds an explicit managed PDF/SVG display for bounded retained
text revisions, with textual labels as well as colors and searchable deleted PDF
text. An explicit numbered policy also shows supported comments, format changes and
paired paragraph moves. Its exclusions and source-history guarantees are in the
[review profile](review-workflows.md#unreleased-source-detached-text-revision-display).
This is not a published 1.5.0 capability.

Unreleased source also admits rectangular matrix equations with selected alignment
and spacing through editable OMML and ordinary managed fixed output. Its documented
deterministic layout is not a claim of identical Office pagination or general math
fidelity; see the [matrix profile](p2-objects-typography.md#unreleased-source-bounded-matrix-equations).

## Unreleased source: bounded theme appearance

The pending #141 work resolves major/minor Latin theme font slots and RGB theme
colors from the local DOCX theme for ordinary text and direct cell shading/borders,
including admitted settings color mappings and one tint or shade transform.
Direct formatting still follows
the existing style-processing order. Missing or unsupported schemes use explicit
font/RGB values or the existing output fallback. This improves visible PDF/XPS
appearance for the documented subset. When the local theme has all 12 supported
RGB slots and both Latin fonts, a DOCX save reconstructs a bounded theme and retains
resolved text font/color references and supported settings color mappings. It does
not copy arbitrary source theme XML or theme relationships. Original effects,
script fonts and dynamic system colors are not retained; direct cell theme
references are flattened to their resolved colors. `Docx.ThemeFont`,
`Docx.ThemeColor` and `Docx.ThemeData` remain loss diagnostics, so strict mode
rejects the corresponding loss. Composition and comparison reject incompatible or
missing document themes rather than silently changing references.
Typed controls continue to reject theme references, including references inherited
from document styles, because their admitted editable formatting is narrower.

Mixed-script font selection, conditional table styles and drawings, other DrawingML
color transforms, simultaneous tint/shade, and the full style/reset precedence
remain unqualified. See the [testing record](testing.md)
and the [initial 1.5.0 controls](fidelity-138-initial.md#additional-independent-controls).
The color formulas and theme-font priority follow the public OOXML descriptions
for [run colors](https://c-rex.net/samples/ooxml/e1/Part4/OOXML_P4_DOCX_color_topic_ID0EYTHO.html)
and [run fonts](https://c-rex.net/samples/ooxml/e1/Part4/OOXML_P4_DOCX_rFonts_topic_ID0E25RO.html).
No fonts or other resources are fetched.

## 1.5.0 roadmap #102 delivery

The [release notes](releases/1.5.0.md), [capability matrix](roadmap-102-capability-matrix.json)
and [qualification record](roadmap-102-p6-qualification.md) describe the P1–P6 batch.
The bounded fields, controls, reports, review and drawing additions ship together;
all five target frameworks and existing platform limits remain. Historical results
below retain their original source/package identities.

The [initial 1.5.0 fidelity measurements](fidelity-138-initial.md) retain all
current comparison failures. Some requested controls and combinations remain outstanding.


The [1.5.0 P4/P5 profile](roadmap-102-p4-p5.md) describes the bounded review,
text-box and vector additions and their explicit exclusions. It is separate from
the published 1.4.0 profile below.

## Mixed report regions — merged source

PR #135 repeats bounded same-section paragraph/table regions, including one
master-detail level, with cloned identity and reference checks. Explicit cross-tabs
reject non-fitting layouts, and fragments retain source formatting under the
documented placement policy. It does not extend object, revision, nested-table or
section support. See the [template profile](editing-and-templates.md#mixed-report-regions--merged-source).
Unreleased source also admits bounded fragment insertion into a dedicated simple
body-table cell and explicitly opted-in simple fields, as described in that profile.

## P2 control preservation — merged source

The [P2 source profile](content-controls.md) preserves bounded typed DOCX controls,
provides atomic edits and explicit local XML operations, and renders ordinary
managed static PDF/XPS display with diagnosed glyph fallback and a bounded single
underline policy at fixed-output save time. Text exports and explicit display views
keep Unicode glyphs without the fixed-layout restrictions. Common Word editor
scaffolding is omitted informationally within the documented inactive-script
subset. Published 1.5.0 rejects Word-generated glossary category/relationships;
the [unreleased source subset](content-controls.md#unreleased-source-bounded-glossary-metadata)
admits selected inert metadata while rejecting richer graphs.
Unknown/nested/story/table wrappers, richer inherited formatting,
retained review/objects and native/tagged/archive combinations remain rejected.
This profile is included in 1.5.0; its earlier source qualification remains dated evidence.

## Merged roadmap #102 P1 source profile

See [the exact subset](roadmap-102-p1.md) for default DOCX 11 pt/zero after-spacing, paragraph-mark metrics, managed implicit autofit, conventional note separators, typed custom properties, field pictures/nesting and managed STYLEREF headers. These changes merged in PR #133 and are included in 1.5.0. Explicit historical controls and all unchanged reference failures remain separate from later source-policy measurements.


## 1.4.0 roadmap #99 profile

Version 1.4.0 extends the 1.3.0 profiles with the bounded changes in
[roadmap-99-implementation.md](roadmap-99-implementation.md): direct HTML/ODT PDF export,
chart binding, scalar fields, structural comparison and qualified layout/native/object
combinations. The [1.4.0 release notes](releases/1.4.0.md) record compatibility and
qualification requirements. Earlier release receipts below remain historical evidence.

## 1.3.0 source profile

The 1.3.0 source preserves admitted body/cell chart graphs and OMML, renders the qualified vector subset and exposes explicit immutable editing/removal. Native bidi/Hindi/tagged profiles are opt-in. Objects with native/UA/PDF-A, advanced chart/math types and unknown/external graphs remain excluded. See [P2 fidelity and combinations](p2-objects-typography.md).

## Merged P1 column and exchange profile

DOCX preserves one to three explicit body-column widths/gaps in twips. Widths must
be positive; their sum plus gaps must fill the body area within two twips.
Fixed-page text is remeasured when crossing widths; tables, notes and column-relative
anchors use active geometry. Default/equal-column behavior remains. Invalid geometry
fails before output copying. A nested table cannot split across unequal widths;
unsupported cases fail explicitly. Final-column balancing and arbitrary Word
pagination parity remain outside this profile.

[ODT](odt.md) exchanges a bounded ODF 1.3 profile with DOCX, retaining semantic
structure/geometry with explicit losses. [Review preservation](review-workflows.md)
is opt-in; default accepted-state conversion and Strict review-loss rejection remain.
These additions do not change dated historical qualification; their released scope
is recorded separately in the 1.3.0 profile.

The current implementation preserves
the original targets and adds net462. The enterprise preview separates native import
into `wordnet.Ocr` and adds managed OFL resources in `wordnet.Fonts`.

| Feature | Supported behavior |
| --- | --- |
| Text appearance | Explicit font sizes and RGB colors through DOCX, DOC and EPUB; measured sizes/colors in PDF and XPS output |
| Inheritance | DOCX document defaults, paragraph and character style chains, direct overrides and automatic-color reset |
| Tables | Fixed/autofit and absolute/percentage widths, nested cells, spans, padding, RGB borders/shading and vertical alignment in the bounded DOCX/PDF/XPS/HTML profiles |
| Sections/paragraphs | DOCX section transitions, equal-width columns and page labels/restarts; page geometry, paragraph spacing/indents and pagination controls in fixed pages |
| Richer cells | Multiple editable paragraphs, horizontal/vertical spans through Word/EPUB; contiguous repeating headers and line-split long rows in PDF/XPS |
| Lists | Nine-level decimal/letter/Roman/compound numbering through DOCX/PDF/XPS/HTML; DOC/EPUB retain decimal/bullet subsets with diagnostics |
| Images | Bounded PNG/JPEG; GIF/BMP become PNG; encoded-byte preservation for PNG/JPEG through DOCX/DOC/EPUB, physical point dimensions and image-aware line layout |
| Enterprise stories/fields | Default/first/even headers and footers, inherited by section; PAGE/NUMPAGES/SECTIONPAGES/DATE/TIME/TITLE/AUTHOR and safe hyperlink fields; unknown cached results retained |
| HTML output | Inline CSS, nested tables/lists, merged cells, cell borders/shading/vertical alignment, text decoration, safe links and data images |
| Fonts | Caller static TrueType faces and TTC catalogs; Liberation/Carlito/Caladea/Noto fallbacks, real styles, subset PDF embedding and basic legacy kerning |
| PDF illustrations | Real PNG/JPEG export; ordinary embedded raster images reconstructed alongside digital text in geometric reading order |

DOCX half-point sizes and binary Word twip dimensions have their format's precision.
EPUB supports inline `font-size` in points/pixels, normal/bold `font-weight`, six-digit RGB `color`, absolute
table/column/cell/image dimensions and individual cell padding sides. Percentage widths,
named/theme color semantics, stylesheets and a general CSS cascade are not implemented.
DOC uses MS-DOC font/color SPRMs, list records, cell boundaries and padding operands,
and PICF/OfficeArt image records. The legacy OfficeArt image identifier follows RFC 1320;
it is not used for security decisions.
DOC pictures include explicit FBSE store entries containing the image BLIP. Table-header
semantics do not imply direct bold formatting. EPUB package URLs are decoded once before
canonical path validation; encoded traversal and separators cannot escape the package.

Fixed pages use resolved font faces, section geometry (A4 fallback) and flow layout.
The business-document profiles added for roadmap #32 are described in
[business fidelity decisions](business-fidelity-design.md):

- Nested tables retain cell content order, padding, solid shading, top/center/bottom
  alignment and single/double/dashed/dotted RGB borders in PDF/XPS. Shared border
  segments choose width, then style, then source order. Minimum row heights are
  retained; overflowing exact heights fail in Strict/BestEffort and grow with a
  diagnostic in Lenient. Ordinary nested rows
  split at complete line bands. Non-splitting nested rows and merged groups must fit
  one body area. Parent table headers repeat across pages/columns. Nested repeating
  headers and arbitrary floating tables remain outside the profile.
- Explicit fixed/autofit layout and percentage table/cell widths survive DOCX.
  Percentages resolve against the containing body, column or cell. Autofit uses one
  content-constraint pass, including spans, and wraps long words at glyph boundaries.
  Absolute fixed grids keep their previous precedence. Implicit layout retains the
  legacy fixed-grid policy in PDF/XPS, while DOCX preserves omitted `tblLayout`
  as Word's implicit autofit. Historical Word autofit quirks are not reproduced.
- PNG/JPEG crops, flips and clockwise 0/90/180/270-degree rotation preserve source
  bytes through DOCX. PDF uses clipping and affine transforms; XPS uses image brushes
  and transformed frames, with PNG pHYs and JPEG JFIF/EXIF density accounted for.
  HTML uses cropped CSS frames with embedded data images. Crops must leave positive
  area; negative outward padding is clamped with an explicit loss. Arbitrary rotations,
  vector pictures and artistic effects still require explicit diagnostics.
- Raster anchors retain page/margin/column horizontal origins and page/margin/paragraph
  vertical origins, offsets, alignment and wrapping distances. Square and top/bottom
  wrapping use rectangular exclusions. Behind/in-front images have deterministic
  paint order and may overlap stories when explicitly positioned there. Out-of-page
  frames fail in Strict/BestEffort; Lenient retains their placement with page-edge
  clipping and a diagnostic. Bounds are checked after paragraph movement.
  Tight/through wrapping reports its square
  approximation. Keep chains, table anchors and wrapping across page transitions are
  bounded approximations rather than a claim of complete Word drawing layout.
- Positioned rectangular text boxes retain paragraph content, inset padding, RGB fill
  and outline through DOCX/PDF/XPS. Content must fit the declared frame in Strict/
  BestEffort. Lenient flattens tables/nested boxes in reading order and scales
  overflowing content into the frame, with diagnostics. Other destinations flatten
  boxes while retaining nested text, images, links and run formatting where supported. Rotated boxes,
  vertical text, arbitrary geometry, autofit and nested table content are outside the
  fixed-page text-box profile. Reflowable HTML reports inline-frame placement.
- Continuous, next/odd/even-page sections retain physical page geometry. Equal-width
  one-to-three-column bodies fill sequentially, with explicit column/page breaks.
  Final columns are not balanced. Incompatible continuous geometry reports a new-page
  approximation; a page-number restart on a shared page is diagnosed. Decimal, Roman
  and alphabetic labels/restarts are separate from physical page counts. Blank parity
  pages count physically and suppress stories. Unequal columns remain diagnosed.
  Page numbers may start at zero. Explicit page/column breaks become diagnosed line
  breaks in destinations without fixed pagination or DOCX break semantics.
- Nine-level lists retain decimal/alphabetic/Roman formats, compound labels, legal
  numbering (Roman values through 3999), start overrides, supported restarts, label appearance and suffix/tab/
  hanging-indent settings through DOCX/PDF/XPS/HTML. Centered/right-aligned labels report left alignment. Private-use bullets without
  portable glyph meaning and picture bullets remain explicit losses.
- Footnotes reserve a bounded page-bottom area with a separator before body placement.
  Long notes continue at complete line boundaries, with at most 40% of each body area
  reserved for notes. Oversized single note lines fail. Number formats, starts and
  continuous/section/page restarts are retained. DOCX has editable note parts and HTML
  has linked end matter. Tables inside notes report flattened layout. Custom note
  separators/placement and arbitrary nested note stories remain outside the profile.
  Other destinations use bracketed references with the final note label and append
  notes in the final body section. DOCX retains reference run appearance.
- REF, PAGEREF, SEQ and heading-based TOC fields refresh text from bookmarks, counters
  and final pagination in PDF/XPS. TOC supports Word's default `\o "1-3" \h \z \u`
  instruction across cached paragraphs, heading ranges, hyperlinks, `\p` separators
  and unqualified `\n` page-label suppression; REF supports text,
  numbering and hyperlinks; SEQ supports start/current/next and supported number
  formats. Calculation is bounded to four additional layout passes. Unsupported
  switches, missing/cyclic references and structured/mixed-format field results
  retain their cached runs with diagnostics, including pictures and bookmarks.
  DOCX refreshes plain REF/SEQ text without fixed layout; DOCX and HTML retain
  pagination-dependent caches. Other multi-paragraph fields and qualified `\n`
  level ranges remain outside the refresh profile. Body/table/cell bookmarks attach
  to adjacent paragraph boundaries; unmatched ends are tolerated with a diagnostic.

DOC retains its existing absolute-table, inline-image, decimal/bullet and next-page
section subset. Destinations that cannot preserve richer features report the relevant
losses. No remote resources are fetched. Strict rejects detected losses and invalid or
oversized geometry before copying staged output; Lenient retains its documented scaling
and splitting behavior for the older profile. Resource limits apply in every mode.
Default PDF is untagged; the merged bucket 4 profile adds the separate semantic PDF/UA profile. XPS now embeds raster images;
TXT/Markdown/MOBI/AZW3 retain available alternative text with an image-loss diagnostic.

PDF import always diagnoses reconstruction. Scan detection treats an image covering
at least half the page as a scan candidate. With OCR configured, those pages and image-only
pages are recognized as a whole; otherwise ordinary illustrations can remain image objects.
This heuristic is not a classifier for arbitrary page designs. English OCR still produces
editable printed text, not reconstructed tables, fonts or arbitrary page layout.
Whole-page OCR explicitly reports image loss and replacement of any digital text layer.
The confidence diagnostic averages all OCR pages' mean confidences and reports their count.

Images must decode completely, have positive finite dimensions,
and fit the aggregate decoded-pixel allowance (`MaximumExpandedBytes / 4`). Images exceeding
the 22-inch dimension profile scale proportionally with an `Image.Scaled` loss in best
effort; strict mode rejects that loss. Original encoded pixels are retained. PDF raster
dimensions are checked before decoding and cross-checked against decoded data. ZIP/input/
output bounds also apply. Native decoder allocations still require host-level resource
isolation for hard CPU/RSS limits. Strict/BestEffort reject oversized fixed-page geometry.
Lenient scales oversized images/text/stories, constrains unusable indentation/cell
widths and force-splits oversized rows/merged groups with warnings. Resource limits,
malformed input, invalid caller configuration, cancellation and I/O still fail.
Merges cannot cross repeating headers to body rows. Nested structure and cell decoration
reach DOCX/HTML/PDF/XPS. Other destinations retain flattened text with explicit losses.

Fixed-page justified paragraphs expand word spaces; distributed paragraphs expand
character gaps on wrapped lines. Custom left/center/right/decimal tab stops and simple
leaders are measured in fixed layout. HTML uses non-breaking spaces with an explicit
tab approximation report. Binary DOC custom tab definitions are diagnosed as lost.
Advanced ligatures, variable/color fonts, theme/conditional table styles,
unequal columns, advanced typography and exact Word line breaks remain outside the
profile. Unsupported cached field results retain their runs and pictures;
cross-paragraph complex fields can flatten block placement. Review diagnostics.

Static caller CFF faces are converted to quadratic outlines for PDF embedding;
the adaptive approximation has a sub-unit font-coordinate error bound and is
independently raster-checked. Legacy kern and GPOS pair adjustments (formats 1/2,
including extension lookups and X/Y placement/advances) affect measurement and output.
Adjacent equivalently styled merge runs coalesce before shaping. Full script shaping
is not provided. DOC SummaryInformation retains Unicode Title/Author, including
render-time TITLE/AUTHOR fields; safe DOC hyperlink fields retain styled cached runs.

PDF options provide optional heading outlines, uncompressed text/font streams and
PDF/A-2b. The default remains PDF 1.7. PNG images use Flate PNG prediction and an
SMask for alpha; JPEG data remains DCT encoded. PDF/A metadata and link flags are
independently validated with pinned veraPDF. At that earlier checkpoint tagged PDF
was not implemented; the later opt-in tagged and PDF/UA profiles and their limits
are described in [interoperability](interoperability.md) and the
[1.5.0 capability matrix](roadmap-102-capability-matrix.json).

## Evidence

The suite includes independent Apache POI DOC, ReportLab PDF and hand-authored EPUB/DOCX
inputs. Open XML SDK validates generated Word markup. Tests assert editable numbering,
font/color values, independent PDF coordinates, decoded pixel colors, exact image bytes,
dimensions, pagination, image limits, corrupt resources and destination preservation.
`eng/DocInteropCheck.java --check-fidelity` independently reads exported DOC appearance,
table widths, numbering and image bytes/dimensions. Poppler renders the composite PDF
for visual review. These checks establish this subset, not arbitrary document fidelity.
See [testing](testing.md) for runtime evidence and [fixture provenance](../tests/wordnet.Conversion.Tests/Fixtures/README.md).
The two-page layout corpus has an independently positioned ReportLab reference. Tests
compare every glyph baseline within 0.03 points through both DOCX and DOC inputs, in
addition to Poppler-rendered inspection. This corpus is a specification-profile reference,
not a rendering produced by Microsoft Word.

## Published format references

- [MS-DOC character properties](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-doc/7022285b-9621-42e9-ad4d-4e02c115ef18),
  [table properties](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-doc/b39a6648-501c-4361-8366-4f042f579469)
  and [preferred table width](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-doc/4cc72316-1fe7-4c67-89ff-83762f162b8f).
- [MS-DOC orientation operands](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-doc/f35a0396-e4d0-4d8e-8b7f-6114d01b864a)
  define portrait as `0x01` and landscape as `0x02`.
- [HTML table model](https://html.spec.whatwg.org/multipage/tables.html#table-processing-model)
  defines zero row spans and row groups.
- [MS-DOC list definition](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-doc/6682e74f-365f-45e6-8b06-0e898bbf69a1)
  and [picture data](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-doc/dedb7505-8cc4-4053-aa2d-431b18d4bd97).
- [MS-ODRAW inline shape container](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-odraw/d634b55c-9916-4d93-a270-c109f14ed794)
  and [PNG BLIP](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-odraw/7af7d17e-6ae1-4c43-a3d6-691e6b3b4a45).
- [RFC 1320 identifier test vectors](https://www.rfc-editor.org/rfc/rfc1320),
  [EPUB 3.3](https://www.w3.org/TR/epub-33/).

## PR #5 compatibility corrections

Cached DOC/DOCX fields retain paragraph and table-cell boundaries; nested fields
do not move their surrounding cached content. Strict rejects tracked revisions
and comments, while BestEffort/Lenient keep the accepted text with warnings.

Run font hints for East Asian/complex scripts do not erase inherited Latin faces;
underlined tabs and line breaks retain run appearance. Numbered paragraphs with
custom tabs serialize in schema order. PDF/XPS tab positions are measured from
the current body or cell text area, and Lenient line scaling preserves that area's
origin. XPS retains stories, evaluated fields and supported font families without
reporting those retained features as losses.

HTML keeps cached PAGE/NUMPAGES/SECTIONPAGES text with an informational diagnostic.
Its reflowable content never inherits fixed-page size or font-substitution failures.
Optional headers/footers select the first page's variants in the first section.
Unsupported image formats use explicit placeholders in BestEffort/Lenient; malformed
supported raster data still fails. JPEG data after its real EOI marker is accepted,
while an EOI pattern inside metadata cannot validate truncated image data.

## Bucket 4 merged-source profiles

The 1.2.0 [interoperability guide](interoperability.md) adds explicit opt-ins for
Arabic typography, semantic PDF/UA, previews, digital reconstruction and OCR metadata,
plus bounded HTML/RTF exchanges. Existing exclusions describe the default/released
profiles; the linked guide specifies supported additions and remaining limits.

## Historical bucket 5 design proposals

The [bucket 5 ADRs](later-design-decisions.md) record proposals made before those
features were implemented. Published 1.5.0 includes bounded ODT exchange,
review/comparison and chart/equation profiles; use the [capability matrix](roadmap-102-capability-matrix.json)
for current combinations and limits. Encryption/decryption and signatures remain
cancelled. The original design tables are historical, not current support claims.
