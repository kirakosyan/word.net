# Interoperability, typography and import

Bucket 4 merged in PR #37 as `44c9e3629f95ed2c984a325a384174303550cb53`, completing #20, #21, #22, #23, #25, #26 and #27 within the profiles below. These APIs are
included in the 1.2.0 candidate; use project references or verified local packages
until [publication](release-1.2.0-readiness.md) completes. Package 1.1.0 and merged buckets 1–3
have separate historical qualification. The [architecture decision](interoperability-design.md)
preceded implementation. No package publication is part of this work.

## Supported matrix and deployment

The normal converter now has 36 directed Word exchanges: the original 28, two HTML
exports, two HTML imports and four RTF exchanges. `DocumentFormat.Rtf` is appended;
existing enum values and constructors remain. `WordDocument.Load` accepts HTML/RTF;
`Save` writes RTF. Public additions match all five target frameworks and contain no
provider types. XPS remains output-only.

HTML, RTF, PDF tagging and outlined SVG are managed and offline. PNG, PDF input/OCR
and opt-in typography require matching `wordnet.Ocr` and native assets. No font
or resource discovery, Office automation, script execution or implicit fetching occurs.
Configure options/collections before concurrent calls. Input/output staging, caller
stream ownership, Strict losses and cancellation contracts remain. Native calls have
cooperative checkpoints; host isolation supplies hard deadlines and RSS limits.

## Safe HTML input

`HtmlInputOptions.Images` maps exact, case-sensitive resource names to copied
`TemplateImage` values. Data PNG/JPEG base64 images are also accepted. Other images
produce a loss and alternative-text fallback. CSS resource functions/imports,
external stylesheets, scripts, events, frames and active objects never execute/load.

HTML5 recovery uses AngleSharp without a browsing context. Editable paragraphs,
headings, inline bold/italic/underline/strike, fonts/sizes/colors/backgrounds, links,
nested lists and merged/nested tables are mapped through the shared model. Ordinary
whitespace collapses across inline boundaries; `pre`/`pre-wrap` retain it.
Simple compound tag/class/id selectors use specificity, source order and important
precedence; inline declarations otherwise win. Font properties inherit. Inline backgrounds
paint nested text through transparent children until the nearest block; a nearer opaque
background overrides them. Explicit nonempty image alt text overrides a decorative caller
resource; explicit empty alt and a decorative resource with no element alt remain artifacts.

Lengths use 72 pt/in, 96 px/in, cm/mm and font-relative em. Font-size percentages are
relative to inherited size; table/cell percentages use their containing width.
Table widths, cell padding/shading and image physical sizes retain supported geometry.
Named/hex RGB colors and the documented simple selector subset are bounded; browser
layout, flex/grid, positioned CSS, advanced selectors and unsupported declarations
produce `Html.Css` losses. Stylesheets, declarations, matching work, DOM depth/nodes,
decoded images and aggregate bytes are admitted before mapping.

```csharp
var converter = new WordConverter();
var options = new ConversionOptions(FidelityMode.BestEffort);
options.HtmlInput.Images.Add("logo", new TemplateImage(pngBytes, 72, 36, "Company logo"));
using var html = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
    "<h1>Invoice</h1><p>Amount <b>42.50</b></p><img src='logo' alt='Company logo'>"));
using var word = new MemoryStream();
converter.Convert(html, word, DocumentFormat.Html, DocumentFormat.Docx, options);
```

## RTF exchange

The own RTF 1.9.1 byte reader/writer handles escaped delimiters, signed UTF-16
Unicode escapes and fallback counts, declared single-byte ANSI code pages, run and
paragraph appearance, first-level list definitions, non-nested basic tables and
PNG/JPEG hex/binary pictures with physical goals. Group state/numeric arguments,
Unicode expansion, lists/fonts/colors, binary/picture bytes and output are bounded.
Word-style repeated row definitions are accepted only after complete cells with identical
edges; nested/incomplete/mismatched definitions fail. Word `shppict` PNG/JPEG containers
retain the primary raster once and skip the adjacent redundant `nonshppict` fallback.
A fallback without a supported primary reports a loss. Cell terminators end the final
paragraph; explicit empty final paragraphs are retained.

Unknown controls/destinations, OLE, linked objects, field execution, unsupported
picture scale/table offsets, nested lists/tables and cell merges are omitted or
flattened with `Rtf.*` diagnostics. DBCS byte runs and Symbol character mapping are
outside this profile. Special charset bytes retain their Latin-1 code values with an
`Rtf.FontCharset` loss in BestEffort/Lenient; Strict rejects atomically. Unicode escapes
retain their specified values without a symbol-font mapping. Cached field
results can remain visible, but instructions never execute. This is an interchange
subset, not complete historical RTF fidelity. Use `CanConvert` for format pairs.
Independent LibreOffice-produced input and a separate LibreOffice reader qualify
editable Unicode, emphasis, lists/tables and image frames; round trips are supplementary.

## Optional Arabic typography

Set `options.Typography.Enabled = true` and supply appropriate static TrueType
Arabic faces through `options.Fonts.Sources`. Regular, bold and italic requests need
matching faces. Fallback covers complete grapheme clusters; Unicode marks and
surrogate pairs are never separated into fallback characters. The first native
profile accepts BMP Arabic, Latin, decimal digits, combining marks and common
punctuation in horizontal ordinary text paragraphs. Source DOCX `bidi`/HTML `dir`
and `Typography.Direction` determine base direction. Source alignment remains physical.

HarfBuzz supplies glyph IDs, UTF-16 clusters, advances and mark offsets. Bounded own
TrueType decoding preserves signed deltas, implied half points and composite
transforms without executing hinting. PDF and SVG use actual shaped outlines.
PDF includes embedded-font invisible word carriers with UTF-16BE ActualText; Unicode
extraction and word searches retain marks. Search highlights have word granularity.
Extractors can insert layout line breaks between visually reordered carriers; remove
those layout breaks when comparing a single logical source line.

```csharp
var options = new ConversionOptions();
options.Typography.Enabled = true;
options.Typography.Direction = TextDirection.Auto;
options.Fonts.Sources.Add(FontSource.FromFile("Noto Sans Arabic", callerArabicFontPath));
byte[] pdf = new WordConverter().ConvertToPdf(docxBytes, options);
```

The managed Latin path is unchanged when this option is absent. Native token length
is capped at 16,384 UTF-16 units, glyph work defaults to one million per layout pass,
font bytes/outline points/path operations use aggregate admission budgets, and
composite cycles/depth/expansion are checked before path access. Native faces/fonts,
buffers and per-call caches are disposed. Concurrency shares no mutable native state.

Directional embedding/isolate controls, ambiguous style changes within joined Arabic
words, other scripts/supplementary characters, fields/bookmarks/notes/drawings/tabs,
justified/distributed native text, vertical text and words wider than their column
are rejected explicitly. The bounded bidi algorithm covers the qualified simple
strong/number/neutral runs; it does not implement every Unicode bracket/embedding
case. Indic shaping, CJK line breaking and hyphenation remain separate follow-ups.
Native typography with tagged/PDF-UA, PDF/A output or XPS is explicitly unsupported in this
increment. English-only OCR is unchanged.

## Semantic PDF and PDF/UA-1

`Pdf.Accessibility = PdfAccessibility.Tagged` exports source heading/P, list
L/LI/Lbl/LBody, table/TR/TH/TD, Figure/Alt and Link relationships. Actual page MCIDs,
parent-tree entries and annotation OBJRs map back to source order through wrapping
and pagination. Header/footer furniture, repeated table headers, decorative images
and drawing rules are artifacts. Simple column-header scope and horizontal cell
spans are supported; explicit complex vertical-merge associations are outside UA-1.

Set `Pdf.Language` explicitly to the document language using a supported BCP 47 language[-Script][-Region] tag and supply the source
title, a heading hierarchy starting at level one without upward skips, meaningful image alt text and marked column-header rows. `TemplateImage` has
an additive five-argument constructor with `decorative: true` and empty alt. DOCX
preserves the decorative extension; HTML `alt=''` explicitly marks decoration.
Missing alt is different from explicit decoration. Language runs are not inferred;
this initial profile assumes the caller-specified document language applies throughout.

`PdfAccessibility.PdfUa1` requires those semantics even under BestEffort. It rejects
unsupported notes/text boxes, missing link relationships/destinations, vertically
merged tables and combination with PDF/A or native typography. Ordinary PDF/PDF-A
remain the defaults. Tagged mode reports missing semantics; tags alone do not establish
UA compliance. Generated REF/PAGEREF/TOC links, repeated-header links and decorative-image
links without a source Link relationship omit the annotation with `Pdf.LinkSemantics`
in Tagged BestEffort/Lenient; Strict and UA-1 reject before copying output. Their visual
content remains. Parent-tree keys are sorted across page and annotation entries.
Only the bounded authored profile has independent UA-1 qualification.

```csharp
WordDocument document = WordDocument.Create();
document.Title = "Invoice";
document.AddParagraph("Invoice", headingLevel: 1);
document.AddParagraph("Amount due: 42.50");
var options = new ConversionOptions();
options.Pdf.Accessibility = PdfAccessibility.PdfUa1;
options.Pdf.Language = "en-US";
using var output = new MemoryStream();
document.Save(output, DocumentFormat.Pdf, options);
```

The tagged serializer buffers bounded generated tokens, discovers actual page and
annotation references, appends sequential structure objects and rewrites offsets.
It reimplements the public token-writer interface for final xref; there is no private
reflection or guessed object-ID range. UTF-16BE hex preserves accessible descriptions.

## Physical pages and previews

`Pdf.Pages.Add(new PageRange(first, last))` selects one-based physical source pages,
inclusive, ascending, nonoverlapping and in range. Empty means all pages. Complete
layout happens first, retaining source PAGE/NUMPAGES, section parity and displayed
labels; destinations/outlines map to included pages. Missing/excluded destinations
produce a loss; the UA profile requires valid included link destinations.

`WordConverter.RenderPages` accepts readable input and returns immutable copied
`PagePreview` bytes, source physical identity, label and point/pixel dimensions.
`PreviewOptions` selects pages, SVG/PNG, DPI 36–600 and an aggregate pixel limit.
SVG 1.1 contains self-contained outlined, non-selectable text, escaped XML and data
images. PNG uses an internally staged selected-page PDF and optional native rasterizer.
Bounds apply to all preview dimensions at the selected DPI, not only PNG. Crop/rotation,
image frames, aspect ratio, actual ink and aggregate bytes are checked. Raster intermediates
omit annotations, so links to other source pages do not create preview destination losses.
Font parsing, decoded outlines and SVG paths are cached across pages within each render
call; unique glyphs consume the outline budget once. New glyphs and aggregate output
retain their admission bounds; concurrent calls do not share mutable caches.

```csharp
var selection = new PreviewOptions { Format = PreviewFormat.Png, Dpi = 144 };
selection.Pages.Add(new PageRange(2, 3));
using var source = new MemoryStream(docxBytes);
DocumentPreview preview = new WordConverter().RenderPages(source, DocumentFormat.Docx, selection);
foreach (PagePreview page in preview.Pages)
    File.WriteAllBytes($"page-{page.SourcePage}.png", page.GetBytes());
```

## Digital PDF reconstruction

`PdfImport.ReconstructLayout = true` opts into known one/two/three-column horizontal
LTR prose, baseline/indent paragraph grouping, complete axis-aligned ruled grids and
supported PNG/JPEG illustrations. Set `PdfImport.Columns` explicitly; this does not
claim arbitrary column discovery. Editable cells preserve measured column widths;
text/images are assigned once. First-line and hanging indents remain in one paragraph,
and adjacent same-size/bold/italic glyphs become compact editable runs with line breaks.
Spacing is assigned after ordering all flow blocks, including table leading gaps and
inline-image fallback heights. Page-relative anchors share an existing flow paragraph.
Orthogonal image frames retain supported position,
rotation/flip and dimensions. Element/grid-search/pixel/byte work is bounded.

Forms, rotated/bidi text, borderless tables, shear/clipping and ambiguous grids use
diagnosed fallback; no original editable structure is invented. Diagnostic confidence
is a geometric heuristic. Vertical flow/pagination around columns and tables remains
approximate and reports `Pdf.ReconstructionFlow`. All PDF imports retain the
`Pdf.Reconstruction` loss. Scanned pages still require explicit English OCR; enabling
reconstruction does not bypass that requirement. The original import default remains.

## OCR metadata

Set `OcrMetadata.Enabled = true` alongside explicitly provisioned English OCR.
`ConversionResult.OcrPages` is empty by default. Immutable page/word/line/paragraph
regions expose uncalibrated provider confidence, polygon/AABB and nullable UTF-16 spans
into `OcrPage.Text`. That canonical editable body text uses LF within paragraphs and
one LF between paragraphs; `DocumentTextStart` counts canonical body text across
mixed digital/OCR pages. Later editing or lossy export can change those offsets.
Unmapped regions have null starts/zero length and a diagnostic.

Coordinates are top-left points, 72/inch, on the original displayed page/crop/rotation
plane before OCR correction, rather than raw unrotated PDF user-space coordinates.
Only the winning recognition candidate retains TSV. Its actual rotation, deskew and
translated canvas transforms are inverted before clipping to the original raster and
scaling to page dimensions. Default budgets are 50,000 regions and 4 MiB estimated
metadata, further constrained by expansion limits. Native text/TSV buffers are freed
before immutable results escape. Ordinary diagnostics never include recognized body
text. Confidence is not calibrated accuracy; no handwriting or new OCR language is added.

## Independent evidence and limits

Run `dotnet run --project examples/WordNet.Interoperability -- artifacts/sample` for
a self-contained HTML → editable Word/RTF, PDF/UA and SVG example.
See [testing](testing.md) for exact build/runtime evidence and commands. Authored
fixtures and pinned font hashes/licenses are in `Fixtures/Interoperability`.
Independent ReportLab ground truth, LibreOffice, MuPDF, HarfBuzz/FontTools/FriBidi and
pinned veraPDF supplement schema and regression tests. Qualification on Linux x64
does not qualify Windows/ARM64, actual Framework 4.6.2, Word/Aspose, Android/macOS or
trimming/AOT. No universal accessibility, browser, typography or PDF recovery claim follows.
