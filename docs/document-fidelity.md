# Formatting and image profile

Alpha.8 implements the following independently authored fidelity increment. It preserves
all four library targets and introduces no new runtime dependencies or public provider types.

| Feature | Supported behavior |
| --- | --- |
| Text appearance | Explicit font sizes and RGB colors through DOCX, DOC and EPUB; measured sizes/colors in PDF and XPS output |
| Inheritance | DOCX document defaults, paragraph and character style chains, direct overrides and automatic-color reset |
| Tables | Absolute preferred table width, grid/cell widths and top/right/bottom/left cell padding; unequal columns in fixed-page output |
| Lists | Editable decimal numbering and round bullets through DOCX, DOC and EPUB, including nesting up to nine levels and zero-based start values; linked DOCX numbering styles and editable Markdown list import |
| Images | Bounded inline PNG/JPEG, encoded-byte preservation through DOCX/DOC/EPUB, physical point dimensions and image-aware line layout |
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

Fixed pages use the pinned Noto Sans faces, A4 geometry and simple flow layout. Original
font families, page/section geometry, advanced typography, hanging list indentation and
original pagination remain outside this profile. Table cells contain one paragraph;
additional paragraphs/complex lists flatten with a diagnostic. Fixed layout uses cell/grid
widths ahead of a preferred total width; it does not reproduce Word's autofit algorithm.

Only inline raster pictures are preserved. Floating anchors, vector/animated formats,
cropping, mirroring, rotation, filters and other effects require diagnostics or failure.
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
isolation for hard CPU/RSS limits. Oversized fixed-page content fails instead of silently
resizing images or clipping a line/table row.

## Evidence

The suite includes independent Apache POI DOC, ReportLab PDF and hand-authored EPUB/DOCX
inputs. Open XML SDK validates generated Word markup. Tests assert editable numbering,
font/color values, independent PDF coordinates, decoded pixel colors, exact image bytes,
dimensions, pagination, image limits, corrupt resources and destination preservation.
`eng/DocInteropCheck.java --check-fidelity` independently reads exported DOC appearance,
table widths, numbering and image bytes/dimensions. Poppler renders the composite PDF
for visual review. These checks establish this subset, not arbitrary document fidelity.
See [testing](testing.md) for runtime evidence and [fixture provenance](../tests/wordnet.Conversion.Tests/Fixtures/README.md).

## Published format references

- [MS-DOC character properties](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-doc/7022285b-9621-42e9-ad4d-4e02c115ef18),
  [table properties](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-doc/b39a6648-501c-4361-8366-4f042f579469)
  and [preferred table width](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-doc/4cc72316-1fe7-4c67-89ff-83762f162b8f).
- [MS-DOC list definition](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-doc/6682e74f-365f-45e6-8b06-0e898bbf69a1)
  and [picture data](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-doc/dedb7505-8cc4-4053-aa2d-431b18d4bd97).
- [MS-ODRAW inline shape container](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-odraw/d634b55c-9916-4d93-a270-c109f14ed794)
  and [PNG BLIP](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-odraw/7af7d17e-6ae1-4c43-a3d6-691e6b3b4a45).
- [RFC 1320 identifier test vectors](https://www.rfc-editor.org/rfc/rfc1320),
  [EPUB 3.3](https://www.w3.org/TR/epub-33/).
