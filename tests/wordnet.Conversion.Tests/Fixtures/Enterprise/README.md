# Independently generated enterprise corpus

`eng/generate-enterprise-fixtures.py` authors Transitional WordprocessingML and OPC
ZIP containers directly with Python's standard library; Pillow 12.3.0 encodes the
owned color-grid raster into PNG/JPEG/GIF/BMP. No commercial library or customer
document is used. Archives have fixed timestamps. The 40 DOCX files cover all eight
requested languages, explicit 1/5/25-page variants, a ten-page benchmark, fields,
header/footer variants, aligned footer tabs, decoration, nested tables, merge
artifacts, notes, tiny runs and malformed/expanded-limit inputs.

`manifest.csv` records SHA-256, required text and outcome. `specified_pages` describes
authored explicit page breaks; zero means content-dependent pagination. These are
**not Word-generated page counts**. The separate `word-pages.csv` was measured with
installed Microsoft Word 16.0, build 16.0.20430, using
`eng/measure-word-reference.ps1` on read-only copies. It records all 38 valid inputs,
font metadata and their source hashes. The business category contains 36 inputs;
tiny-run/oversized-row stress inputs are recorded separately before comparison.
All 36 business page counts match wordnet exactly. This narrow authored corpus
does not establish arbitrary template layout or line-break parity. Reference PDFs
are kept under ignored artifacts; no Office code, proprietary resources or installed
fonts are copied into the repository. Office is needed only to regenerate references.

`caller-fonts.ttc` contains two tiny, renamed subsets of Liberation Sans 2.1.5, generated
by `eng/generate-font-fixtures.py` using fontTools 4.61.1 (MIT). Font outlines remain
under SIL OFL 1.1. The renamed family is Wordnet Fixture Sans; bundled original font
licenses/provenance are in `src/wordnet.Fonts/Resources`. `caller-fonts.json` records
the collection hash and an independently calculated AV kerning width.

The same generator authors renamed OFL `gpos-pair.ttf`, `gpos-class.ttf` and
`gpos-extension.ttf` with controlled pair/class/value records, plus `caller-cff.otf`
with exact quadratic-to-cubic outline conversion. `font-fixtures.json` records their
SHA-256 hashes. Font timestamps are pinned to the licensed source font. A test uses
Skia's independent CFF and TrueType rasterizers to compare the converted outlines.

`Goldens/*.png` are reviewed wordnet PDF regression renders (PdfPig Rendering Skia
0.1.16.4 / Skia 4.153.1, embedded fonts, 72 DPI, white background, fixed clock).
They are not Microsoft Word output. Regenerate with `eng/update-enterprise-goldens.ps1`,
inspect each changed image, then run the tests without regeneration enabled. Pixel
tests allow at most 0.2% significantly changed pixels and 0.25 average channel error.

No invoice/customer material belongs in this directory. Private acceptance inputs
and their outputs stay outside the repository and solution.

`word-generated-stories.doc` is an independent Microsoft Word 16.0 binary export
of the authored `letter-stories.docx`, produced by
`eng/generate-word-header-fixture.ps1`. Personal information is removed, font
embedding is disabled, and the file contains no macro/project/object streams.
The adjacent JSON records source/output hashes and producer. It verifies header
fields and the special terminal-paragraph formatting rule independently of the
wordnet DOC writer. Exported wordnet DOCs were also opened and printed by Word:
five-page default stories and two-page first/even stories retain their pagination.
