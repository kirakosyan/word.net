# Formatting and image profile

The current implementation extends the alpha.8 appearance profile. It preserves
the original targets and adds net462. The enterprise preview separates native import
into `wordnet.Ocr` and adds managed OFL resources in `wordnet.Fonts`.

| Feature | Supported behavior |
| --- | --- |
| Text appearance | Explicit font sizes and RGB colors through DOCX, DOC and EPUB; measured sizes/colors in PDF and XPS output |
| Inheritance | DOCX document defaults, paragraph and character style chains, direct overrides and automatic-color reset |
| Tables | Absolute preferred table width, grid/cell widths and top/right/bottom/left cell padding; unequal columns in fixed-page output |
| Sections/paragraphs | Next-page sections, page dimensions/orientation/margins, before/after and line spacing, physical indents and supported pagination controls through Word |
| Richer cells | Multiple editable paragraphs, horizontal/vertical spans through Word/EPUB; contiguous repeating headers and line-split long rows in PDF/XPS |
| Lists | Editable decimal numbering and round bullets through DOCX, DOC and EPUB, including nesting up to nine levels and zero-based start values; linked DOCX numbering styles and editable Markdown list import |
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
Continuous/odd/even section breaks, advanced typography, hanging
list indentation and exact original pagination remain outside this profile. Paragraph
indents/spacing and keep controls are retained; keep-with-table is diagnosed. Line spacing
smaller than content height reports loss and expands to avoid clipping. Table cells retain
multiple paragraphs. Fixed layout uses cell/grid
widths ahead of a preferred total width; it does not reproduce Word's autofit algorithm.
DOCX absolute table indentation and DOC absolute row edges are retained through Word.
Tables may overhang text margins while remaining inside the physical page. Omitted grid
columns become empty cells with a loss diagnostic; mixed unspecified grid widths are
approximated with a diagnostic. Content-control/custom XML wrappers retain available
table cells. HTML row headers do not imply repeating column headers. Zero row spans
extend to the row-group end, and overlong spans are clipped with a loss diagnostic.
Negative vertical Word margins use their absolute distance with a loss diagnostic for
the unretained header-overlap behavior.

Anchored raster pictures degrade to inline placement with `Image.AnchorApproximated`.
Unsupported SVG/EMF/WMF use a visible placeholder at their declared extent outside Strict.
Animated GIF uses its first frame. Cropping, mirroring, rotation, filters and other
effects require diagnostics or failure. DrawingML text boxes flatten their current text
with a placement/appearance warning; arbitrary positioned shapes/watermarks are not reproduced.
Remote resources are never fetched. Binary Word output diagnoses loss of alternative
text; PDF is not tagged with accessible alternative text. PNG transparency survives
Word/EPUB exchange; PDF transparency and soft-mask reconstruction need broader qualification.
TXT/Markdown/MOBI/AZW3/XPS output reports image loss and retains available alternative text
in best-effort mode. Strict mode rejects detected loss before touching the destination.

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
Merges cannot cross repeating headers to body rows. Nested structure survives DOCX/HTML;
other outputs retain flattened cell text with `Table.NestedFlattened`. Cell decoration
survives DOCX/HTML and produces `Table.Decoration` elsewhere. Notes retain numbered text
at the document end with `Notes.PlacedAtEnd`, rather than original page-bottom placement.

Fixed-page justified paragraphs expand word spaces; distributed paragraphs expand
character gaps on wrapped lines. Custom left/center/right/decimal tab stops and simple
leaders are measured in fixed layout. HTML uses non-breaking spaces with an explicit
tab approximation report. Binary DOC custom tab definitions are diagnosed as lost.
Advanced ligatures, variable/color fonts, theme/conditional table styles,
complex sections/columns, page numbering restarts and exact Word line breaks remain
outside the profile. Unsupported cached field results retain their runs and pictures;
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
independently validated with pinned veraPDF. Tagged PDF is optional and unimplemented.

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
