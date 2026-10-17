# Business-document layout decisions

The implementation for roadmap #32, section 2, extends the existing internal
semantic model and managed PDF/XPS pipeline. No public API or runtime dependency
is added. DOCX remains the editable interchange format. Unsupported binary DOC
representations and reflowable-format losses must be reported at the destination.

Tables use a deterministic, bounded sizing pass. Fixed absolute grids retain their
existing precedence. Percentage widths resolve against the containing text area;
autofit distributes minimum and preferred content constraints, including spans,
without iterative remeasurement. Nested cells are measured recursively within
the admitted document depth. Pagination breaks between complete line bands;
non-splitting rows and merged groups retain the existing explicit size failures.
Cell backgrounds paint before text. Shared borders choose the widest border,
then double/single/dashed/dotted, then the preceding cell on a tie.

Raster transforms retain source bytes and use clipping and affine transforms at
export. Crops must leave a positive source rectangle inside the image. Rotations
are clockwise multiples of 90 degrees around the frame center; the rotated frame
determines flow bounds. Anchors use physical page, margin or paragraph origins.
Square wrapping reserves a rectangular exclusion; top/bottom wrapping reserves
the whole body width. Positioned text boxes share the bounded paragraph engine.

Sections retain their transition type, equal-width column count (one to three),
gutter, page-number restart and decimal/letter/Roman format. Continuous sections
with incompatible page geometry require an explicit approximation. Columns fill
in reading order; final-column balancing is deliberately not promised. Physical
page counts remain separate from displayed labels and section page counts.

Numbering definitions are per-document identities with nine levels. Counters
retain start/restart rules, compound labels, number formats, suffix and label
appearance. Notes retain their reference identity and kind. Page-bottom footnotes
reserve space before body placement; long notes split at complete line boundaries.
Endnotes remain linked end matter in HTML and editable note parts in DOCX.

Fields execute only a small grammar: REF, PAGEREF, SEQ and heading-based TOC,
alongside the existing fields. Pagination-dependent results use a bounded number
of passes. Unsupported switches, missing/cyclic references or non-convergence
retain cached results with diagnostics; no field expression executes code or
loads remote content.

DOCX serialization never requires fixed layout. It refreshes plain REF/SEQ text
and retains pagination-dependent caches. Structured or mixed-format field caches
keep their original runs; only the supported fixed-page heading TOC replaces its
multi-paragraph cache. Empty paragraphs from that replaced result are removed.
Word's default TOC display switches are accepted; unsupported range policies stay
explicit. Block-level bookmark boundaries attach to adjacent content paragraphs.

Lenient fixed-page layout grows overflowing exact-height rows, permits page-edge
clipping of bleed drawings and reduces overflowing text-box content into its frame.
Tables/nested boxes in text boxes flatten in reading order. Each approximation
reports a strict-rejection diagnostic; Strict/BestEffort retain explicit failures.
Non-paginated destinations map page/column controls to diagnosed line breaks and
flatten text boxes without discarding nested runs. Note references resolve their
final label at output time, and appended notes inherit the final body section.
XPS image viewboxes use natural 96th-inch units derived from bounded PNG pHYs,
JPEG JFIF and TIFF IFD0 EXIF density metadata, independent of physical frame size.

Verification uses hand-authored OOXML, asymmetric raster markers, Open XML schema
validation, independent PDF text/coordinates and rendered pixels. These tests
qualify the documented profile, not arbitrary Microsoft Word pagination parity.
