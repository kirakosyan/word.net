# Interoperability, typography and import decisions

This records the implementation contract for roadmap #32, bucket 4: #20, #21,
#22, #23, #25, #26 and #27. It records decisions made before implementation; current merged-source support and qualification are in [interoperability.md](interoperability.md). Bucket 3 merged
as `0b2af843`; its post-merge documentation merged in PR #36 as `141236a3`. No package is published
by this work. All five framework assets and the existing managed export path remain.

## Shared contracts

Add provider-neutral options/results; preserve existing signatures, enum values,
stream ownership, staging, cancellation, Strict losses and default behavior. Caller
collections are copied before processing. Admit text, nesting, images, glyphs,
pages, output bytes and metadata before allocating aggregate results. No implicit
file, font, network, script or Office access. Native providers remain optional.

HTML and RTF exchange with DOC/DOCX through the shared semantic model. RTF is an
appended format value. Inspection and WordDocument loading use the same readers.
New conversion directions require real editable output and independent assertions.

## HTML input (#20)

Use the existing pinned AngleSharp HTML parser without a browsing context, loaders
or scripting. Retain malformed-but-parseable HTML5 behavior. Check tree depth and
aggregate nodes before recursive mapping. Reuse ebook semantic helpers deliberately;
do not advertise the ebook parser as a complete browser or silently inherit its
nested-table flattening.

The first profile covers paragraphs/headings, nested lists, inline emphasis/font
appearance, safe links, merged/nested tables and PNG/JPEG images. Support simple
tag/class/id CSS selectors with specificity and source order; inherited properties
precede local declarations, and inline declarations win except explicit important
rules. CSS lengths use 72 points/inch, 96 pixels/inch, cm/mm and em relative to the
resolved font size. Percentage table widths are relative to their containing block.
Collapse ordinary whitespace across inline boundaries; preserve pre/pre-wrap.

Only data images and caller-supplied named byte resources are resolved. Scripts,
events, external stylesheets, resource functions/imports, unsupported selectors and
layout declarations have explicit loss diagnostics. Hostile URI schemes are never
used. Supported CSS is parsed by bounded own code; no new CSS engine dependency.

## RTF (#21)

Implement our own bounded RTF 1.9.1 byte tokenizer and semantic reader/writer from
the published specification. This keeps licensing and deployment unchanged. Group
state includes run/paragraph properties, code page, Unicode fallback count and
destination. Bound stack depth, numeric control arguments, binary lengths, decoded
characters and pict bytes. Malformed lengths/braces fail before output copying.

Support escaped delimiters, signed UTF-16 Unicode escapes, declared ANSI code pages,
fonts/colors, paragraph/run formatting, editable basic lists, non-nested tables and
PNG/JPEG pict frames. Unknown destinations, linked objects/OLE, unsupported table
merges and field execution are omitted or flattened with explicit losses. Never
execute embedded data. Writing uses escaped Unicode, explicit tables and deterministic
font/color/list identities. Existing code-page support avoids another dependency.

## Typography (#22)

A managed implementation of OpenType shaping plus full Unicode bidi would introduce
a substantial new correctness and maintenance burden. A mandatory HarfBuzz provider
would break the existing managed deployment contract. Select an explicit optional
native implementation behind the existing wordnet.Ocr boundary, whose pinned
SkiaSharp/HarfBuzz assets already cover the five build targets. The public API contains
no Skia or HarfBuzz types. Record the precise native versions/licenses and executed
platforms with qualification; compilation alone is insufficient.

The first profile is horizontal Arabic plus Latin, decimal digits, combining marks
and common punctuation in mixed-direction paragraphs. Resolve caller font fallback
per grapheme cluster; never split surrogate pairs/marks. Preserve the existing Latin
path when the option is absent. Reject unsupported directional controls, scripts and
ambiguous styled joining rather than render unshaped Arabic. Indic shaping, CJK
line breaking, vertical text and hyphenation are separate future work. English OCR
does not gain another language.

HarfBuzz supplies actual glyph/cluster/advance/offset data. Managed TrueType
glyph paths supply the outlines; native typography initially requires static TrueType
faces with bounded composite graphs. Skia remains the optional PNG rasterizer. A provider-neutral immutable geometry representation keeps PDF serialization
managed and permits SVG outlines. A logical Unicode PDF text mapping must accompany
the visual glyphs. The selected mapping uses one invisible embedded-font carrier per shaped word,
UTF-16BE ActualText and horizontal scaling to that word's actual advance. This
passed independent MuPDF logical extraction/search and word-wide highlight geometry
in the preparation probe, including Arabic combining marks. Search geometry has
word granularity; it does not claim individual shaped-glyph selection bounds. Full
integration still requires mixed-direction, wrapping and independent glyph assertions. No private-use Unicode, raster text or guessed glyph mapping may be
advertised as searchable Arabic. Native font/path/buffer ownership is per-call.

## Semantic PDF (#23)

Carry source semantics through wrapping, scaling and placement; paint order is not
logical reading order. Export heading/P, L/LI/Lbl/LBody, Table/TR/TH/TD, image alt text,
language, links and header associations. Page furniture, repeated headers and purely
decorative shapes/images are artifacts. Missing source language/title/alt/header
semantics must yield actionable diagnostics; never invent alternative text.

Provide opt-in tagged output separately from the bounded PDF/UA-1 profile. Preserve
ordinary PDF and PDF/A-2b defaults and their existing tests. PdfPig's public token
writer interface and virtual object writer can buffer the tagged path, find actual page objects, append
structure/parent-tree objects with sequential IDs, patch catalogs/pages/annotations
and regenerate offsets/xref. Avoid private reflection or guessed object IDs. Bound
buffer growth. MCIDs and annotation OBJR mappings must refer to their actual pages.

Qualification requires independent structure/content-tree checks, font/Unicode/link
checks, deterministic output, pinned veraPDF 1.30.2 UA-1 validation and a documented
manual logical-order review. A tagged file or a passed validator alone is not an
arbitrary-document accessibility guarantee. Reuse the pinned image digest from the
PDF/A validator; run offline. Do not claim an unperformed screen-reader session.

## Page export and previews (#25)

Select physical pages using one-based indices, never displayed labels. Validate
ranges, duplicates and order explicitly. Lay out the entire source before selection
so PAGE/NUMPAGES, parity and labels retain source meaning. Remap destinations and
outlines to included pages; diagnose links to excluded destinations.

SVG is a self-contained managed representation with explicit text/outline policy,
escaped content and embedded resources only. PNG uses the already optional Skia
provider and a staged selected-page PDF, avoiding another native dependency. Specify
dimensions in points and pixels/DPI, preserving aspect ratio and page crop bounds.
Bound individual and aggregate pixels/bytes/page counts, with cancellation between
pages and native calls. Results identify source physical page and displayed label.

## Digital PDF reconstruction (#26)

Keep content-order import as the default. The opt-in provider profile accepts known
one/two/three-column prose, geometric paragraph grouping and simple axis-aligned
ruled tables. Retain each text/image once with supported frame geometry. Unsupported
rotation/shear/clipping, forms and ambiguous/borderless tables have diagnosed fallback;
confidence describes a heuristic, not recovered original structure. Retain the
existing reconstruction-loss warning for every PDF import. No native import work
moves into core.

## OCR metadata (#27)

Opt-in immutable page/line/word metadata uses original-page top-left coordinates in
points (72/inch), polygons/AABBs, uncalibrated provider confidence and UTF-16 spans
into canonical editable paragraph text. Specify paragraph separators and mixed-page
ordering. Ordinary diagnostic messages never contain recognized body text.

Retain TSV only for the selected native recognition candidate. Track actual rotation,
deskew and translated bitmap bounds, invert their composition, then scale original
pixels using original bitmap/page dimensions. Bound region/metadata growth. Native
text/TSV pointers never escape; free all buffers even on cancellation/failure. Keep
default recognition text/order/accuracy and page-mean diagnostic semantics compatible.

## Independent validation and acceptance

Use independently authored web-editor HTML, specification RTF edges and independently
produced/read RTF; schema-check Word. Pin Arabic fonts with license/hash provenance
and compare shaping to a separate HarfBuzz implementation/version. Validate PDF text
extraction/search and real glyph geometry. Use independent PDF generators for known
column/table/image ground truth and real English clean/noisy/rotated/deskew scans.
Use independent PDF/SVG raster readers, numeric geometry tolerances and hostile input
controls. Round trips supplement these checks.

Run required locked restore, all-target warning-free build, complete applicable OCR
suites, formatting and package/API checks. Preserve Latin/PDF-A and existing conversion
regressions. Record actual tool versions, counts and platform limits. Keep each issue
open until its entire bounded acceptance profile is verified and merged; bucket 5
remains design-only. After every merge perform the standing reconciliation.

## Native font storage correction (PR #38)

HarfBuzzSharp `Blob.FromStream` in the pinned binding retains a temporary managed
array after its pin ends. The adapter now constructs a `MemoryMode.Duplicate` blob
while its admitted source bytes are pinned, then immediately releases the pin.
HarfBuzz owns the stable copy until session disposal. No dependency/API changes.
A compacting-GC/allocation regression checks glyph IDs, clusters, advances and mark
offsets against the independent Arabic golden after font registration.
