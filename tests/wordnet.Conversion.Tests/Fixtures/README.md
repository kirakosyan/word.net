# Fixture provenance

These small XML document parts and the Markdown sample were independently authored
for wordnet on 2026-10-12 and are covered by the repository's MIT license. They contain
synthetic content and no customer data. The test helper packages the XML parts into
DOCX containers independently of the production reader and writer.

- `plain-document.xml`: Unicode, XML escaping, tabs, line breaks, whitespace and empty paragraphs.
- `rich-document.xml`: a heading, direct bold/italic formatting, a hyperlink and a header table.
- `sample.md`: CommonMark text constructs plus a pipe table; no remote resource is fetched.

Assertions encode the expected structure and text directly. Generated DOCX output is
also read and schema-validated with the test-only Open XML SDK. No pagination or
rendering fidelity is claimed by these initial DOCX fixtures.

## Additional synthetic fixtures — 2026-10-12

Except for the identified Apache template/table derivatives, document content is original
synthetic wordnet test text under MIT. These files contain no customer data. Tool licenses
apply to the tools, not ownership of original inserted text. The canonical body has two lines:

```text
Wordnet conversion test
English printed text 12345.
```

| File | Producer and purpose |
| --- | --- |
| digital.pdf | `generate_pdf_fixtures.py`, ReportLab 4.4.9; independent digital text input |
| scanned.pdf | Same script, Pillow 12.3.0/Noto Sans raster plus ReportLab; image-only, no hidden text layer |
| mixed.pdf | Same raster plus a digital footer: `Digital footer remains editable.` |
| plain.epub | `generate_ebook_fixture.py`, original Python standard-library EPUB generator, independent of production serialization |
| plain.mobi | Same generator; independently constructed PalmDB/MOBI 6 with PalmDOC literal-run compression |
| plain.doc | Apache POI 5.4.1 HWPF inserts original text into the Apache `empty.doc` template; independent producer |
| poi-emphasis.doc | Apache POI 5.4.1 produces a bold-and-italic text run in the same template |
| poi-alignment.doc | Apache POI 5.4.1 changes the first row of Apache `simple-table.doc` to left/center/right paragraph alignment; retains upstream content and Apache-2.0 notices |
| invalid-empty-papx.doc | Earlier NPOI rewrite with an invalid empty paragraph-formatting range; negative regression only |
| plain.azw3 | Original text serialized by wordnet and independently unpacked by KindleUnpack 0.83; not an independent writer fixture |

The PDF/ebook Python generators are included here and can be run directly. PDFs pin
ReportLab invariant metadata. `eng/DocInteropCheck.java` reproduces the new DOC fixtures
with `--generate-plain` or `--generate-emphasis`, followed by the template and output
paths. Use Apache POI/poi-scratchpad 5.4.1 and its Maven dependencies on the classpath.
For `poi-alignment.doc`, use `--generate-alignment` with `apache-poi/simple-table.doc`.
The Apache template and its derivatives retain Apache-2.0 notices in `apache-poi/`;
the inserted synthetic text is original wordnet content. See that directory's README
for upstream binary table fixtures, licenses, pinned source revision and hashes.
KF8 was produced by converting the canonical UTF-8 text through DOCX to AZW3 using
wordnet with best-effort options, then validating with KindleUnpack.
The old NPOI fixture is intentionally malformed and must never be an acceptance input.

KindleUnpack revision: `bf0ca6ece4e73494625e7950be3e259b6260774c` from
[upstream](https://github.com/kevinhendricks/KindleUnpack).
It recovered both generated MOBI and KF8 text. KF8 unpacking also reported unrecognized
resource records for the indexes; no Kindle-device rendering claim follows from this check.
External validation tools are not runtime/test project dependencies and are not shipped.

| File | SHA-256 |
| --- | --- |
| digital.pdf | `64ea0ef0e774c7923b3ef1023ed06f1e57fc568bb71cd73f9b33a3b42344f99d` |
| scanned.pdf | `5134b14a80dce4bda2662969cfc2f050d5881c6e8cd2c17a93f5cf6828181a70` |
| mixed.pdf | `152403b2cf8177b3a80af1b57a79bf17a6c8a737af288454e01b38cd94da21d7` |
| plain.doc | `66fd2431b1de986135f902f46d66d39719249804fb35057f4821bdfa140631e4` |
| poi-emphasis.doc | `c7379adc9850f5665d2bd96ead0204b993e6a02856f899a03315a0c4c6926ee7` |
| poi-alignment.doc | `27ec443e6e3bed74ee9f994db31994f47c4a20908cffd36e191b75a054e6f8b9` |
| invalid-empty-papx.doc | `5a632947e6dce38bad77c4117b6fe5220ff1a70d863e257100a4a05a9f54ae0e` |
| plain.epub | `5d6d2921437fa067baef368fef073b1545c7f47fa9cfc2d04f4f6670f3e7c3ab` |
| plain.mobi | `74afd8ea0850b3a1e519020879a19a82c9f389be1c210723225665196ccd6188` |
| plain.azw3 | `8fc637abb499d0e14f695db534ee17a6b68589aaad0ceb674b4a3ea62ad7b1bc` |

OCR exact-text assertions establish zero character/word errors on these clean English
fixtures only. Reading order and editable content are asserted. No noisy-scan, table,
multi-column, layout-reconstruction or broad accuracy qualification is implied.

## Formatting and illustration fixtures — 2026-10-13

`generate_fidelity_fixtures.py` uses Pillow and ReportLab to create original MIT-licensed
red/blue test grids and a PDF with text before and after an illustration. PNG/JPEG are
80×40 pixels; the PDF places the image at 120×60 points. The alpha PNG has 50% opacity.
The generator pins PDF invariant metadata; no external media or customer files are used.
`FidelityDocuments.cs` independently packages original DOCX markup, and
`DocumentFidelityEdgeTests.cs` constructs a separate EPUB with inline CSS, dimensions and
numbering. Assertions use specification values, an independent reader and decoded pixels.

`poi-appearance.doc` is generated by Apache POI 5.4.1 using `--generate-appearance`
in `eng/DocInteropCheck.java` and `apache-poi/empty.doc`. It contains original text at
18 points in indexed blue. The Apache template/derivative retains the Apache-2.0 notices
in `apache-poi/`; inserted text is MIT. This independent producer is not shipped in NuGet.

| File | SHA-256 |
| --- | --- |
| color-grid.png | `819a8859701dfac5d024b3b311d1fc46a3ec7a863a58d1360dd77afac06a7df8` |
| color-grid.jpg | `782be8626883a627f7b71a3493874bc10e8135e8d9113247f4f9475e1ae16f52` |
| color-grid-alpha.png | `3248f4deb843e3cdcae500bacb5025df1a08518919bf3b19622f8c88eaa6968e` |
| illustrated.pdf | `09bf82db2122a06ed76f04a773d3574feb0315bf801858c4d49761e88ccdd836` |
| poi-appearance.doc | `4785e1b5d8dc060e42d9025f19e168f2809a0b1f6c5bfccccb6fd0c3e993ee05` |

## Review regression fixtures — 2026-10-13

`generate_review_fixtures.py` uses Pillow and ReportLab for original MIT synthetic inputs:
`large-cover.png` is a 1600×2560 solid-color cover; `matrix-scaled.pdf` uses a one-unit
font with a 12× text matrix, small character gaps, a real word gap and an illustration.
`multipage-ocr.pdf` contains the clean scan, the scan plus digital footer, and a blank
image page. `blank-scan.pdf` provides the independent single-page confidence baseline.
The OCR images use the pinned Noto Sans face. PDF invariant metadata is enabled.
No external document contents or images were copied.

| File | SHA-256 |
| --- | --- |
| large-cover.png | `847d7f4cf488fe09a1b5ae368230a3750dcb82c44e20ea74af2733ad10e0f12d` |
| matrix-scaled.pdf | `18bfa983322d41957b0272dedd581fc540a0b4b3a1927ed7e598f6dff4e547e5` |
| multipage-ocr.pdf | `a4529a38c87f80cb214a4dd5d91824fe056bce169e33f54939625e24333f8307` |
| blank-scan.pdf | `13df1b38aa6981a2a1380684a9716ecc480fac1a4df48e3159df1efc34cb6d85` |

## Section, table and OCR qualification corpus — 2026-10-13

`generate_layout_corpus.py` authors an original MIT DOCX directly as XML and independently
places its expected PDF text using ReportLab 4.4.9 and the pinned Noto Sans faces.
Pair positioning is independently calculated through HarfBuzz's C API, using the
native tool from HarfBuzzSharp.NativeAssets.Win32 14.2.1.301 (test generation only).
Set `WORDNET_HARFBUZZ_LIBRARY` to that explicitly pinned library; the helper disables
ligatures/contextual substitutions while retaining kerning and verifies clusters.
The two pages contain portrait/landscape sections, margins, spacing/indents, styled
text, multi-paragraph cells and horizontal/vertical merges. `LayoutCorpusTests` compares
every glyph baseline against the independent reference within 0.03 points, for both
DOCX and a DOC intermediate. This supplements Open XML schema checks and the separate
pagination/long-row assertions. `layout-corpus.json` records both file hashes.
The PDF output was also rendered with Poppler 26.07.0 at 96 DPI and both pages inspected
for clipping, overlap, baseline alignment and table boundaries. This is a small pinned
corpus, not a general comparison with Word's pagination engine.

`poi-layout.doc` is independently generated with Apache POI 5.4.1, `--generate-layout`
in `eng/DocInteropCheck.java`, and `apache-poi/empty.doc`. It contains one original
paragraph with spacing, indents and keep controls, plus section margins. Its SHA-256 is
`5b00d0217602d370adf605052722dea0cf26410df0ec4d495f56ec25e767f41e`.
The Apache template/derivative retains the notices in `apache-poi/`. The verifier's
`--check-layout-corpus` independently reads exported DOC section geometry, paragraph
spacing/indents, multiple cell paragraphs and vertical merge flags.

`generate_ocr_corpus.py` uses Pillow 12.3.0 and ReportLab 4.4.9. Seven original MIT scans
cover clean text, 90/180-degree rotation, 3-degree skew, seeded noise, reduced resolution
and two columns. Each PDF contains a raster only; there is no hidden digital answer.
Text ground truth is adjacent to each PDF. The eighth scan, `ocr-phototest.pdf`, wraps
the unmodified pixels from the independently supplied Tesseract `phototest.tif` and
uses its supplied ground truth. Its Apache-2.0 license, pinned revision and source hashes
are preserved in [tesseract/](tesseract/README.md). No recognition implementation is copied.

`ocr-corpus.json` records PDF hashes, producers and thresholds. `OcrCorpusTests` requires
character error rate ≤3% and word error rate ≤8%, using Levenshtein distance after
whitespace normalization. Both Word targets must contain editable recognized text;
column cases also check left-column-before-right-column order. Metrics are written to
desktop TRX output. Synthetic pages use 200-DPI rasterization, the upstream scan 300 DPI.
These eight cases define a regression corpus, not an accuracy guarantee for arbitrary
scans, forms, tables or handwriting. Fixtures and generating/validation tools do not ship
in the NuGet package.

## PR #3 review cases — 2026-10-14

`ProductionReviewTests.cs` independently authors synthetic DOCX/EPUB markup in memory,
under MIT. The DOC regression cases modify section sprm operands and absolute
`rgdxaCenter` edges in a generated compound file, then assert the specification values
directly or inspect output with independent PDF/Open XML readers. The orientation tests
use [MS-DOC SBOrientationOperand](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-doc/f35a0396-e4d0-4d8e-8b7f-6114d01b864a),
where `0x01` is portrait and `0x02` is landscape. They do not rely on a matching WordNet
reader/writer round trip to determine those values. Apache POI's separate verifier also
checks the original section operands. Synthetic TSV coordinates check paragraph boundaries
inside opt-in OCR columns; the existing independent raster corpus checks default native
line order versus explicitly requested column order. No additional binary fixture is needed.
