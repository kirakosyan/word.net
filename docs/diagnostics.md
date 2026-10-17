# Conversion diagnostics

`ConversionDiagnostic.Code` is machine-readable. `Message` describes a feature or
approximation and excludes document body text. `Severity` is `Info` or `Warning`;
`Count` groups repeated occurrences of the same code during parsing/layout/writing.
Multi-pass layout may report a feature more than once. Counts are observations,
not a count of unique document objects.

Each group also exposes `Occurrences`: up to 128 distinct `(Location, Phase)`
findings. `LocationsTruncated` marks additional locations; `Count` still includes
all observations and multi-pass layout. Location paths are materialized only when
a finding is observed; bounded hash-based deduplication keeps repeated losses cheap. DOCX locations contain a package part and
indexed element path, such as `word/document.xml:/document[1]/body[1]/tbl[1]/tr[1]/tc[2]/p[1]`.
Headers/footers identify their own part. Paths contain no document text, attribute
values, bookmark names or caller filesystem paths. They remain stable for unchanged
input; edits can change indices. `/` explicitly means document/part precision;
Parser findings in DOC, EPUB, Markdown and PDF use that coarser fallback where
physical source mapping is unavailable. Their destination/layout findings use explicit
logical paths such as `/model/body/table[1]/row[1]/cell[2]/paragraph[1]`, or
`/model/section[1]/header-default/paragraph[1]`. These identify the retained parsed
model, not byte offsets or original PDF coordinates. Grouped codes/properties and existing constructors
remain compatible. Counts now observe individual matching blocks for destination
checks, which can expose several locations under one existing code.

## Inspect before conversion

`WordConverter.Inspect` and `InspectAsync` consume input from the current position,
leave the caller stream open and never serialize conversion output. They share the
same bounded readers and destination checks. Strict inspection collects losses
instead of stopping at the first one; `WouldRejectStrict` records observed losses
and glyph/geometry conditions that Strict would reject, including fallbacks reported
as warnings in BestEffort/Lenient. Advisory story repositioning/scaling alone does not
set this flag. Strict inspection follows the BestEffort fallback path when collecting
losses, while actual Strict conversion still rejects them before copying output. Unsupported pairs/encryption/providers, corrupt input, limits and
nonrecoverable geometry still throw. Async inspection uses async input I/O; parsing,
provider calls and layout remain synchronous with cooperative cancellation.

```csharp
using System;
using System.IO;
using WordNet;

using var input = File.OpenRead("invoice.docx");
var options = new ConversionOptions(FidelityMode.BestEffort);
var report = new WordConverter().Inspect(input, DocumentFormat.Docx,
    DocumentFormat.Pdf, options, includeLayout: true);
Console.WriteLine($"Input: {report.InputBytes} bytes; retained tables: {report.Features.Tables}");
foreach (var diagnostic in report.Diagnostics)
    foreach (var occurrence in diagnostic.Occurrences)
        Console.WriteLine($"{diagnostic.Code} [{occurrence.Phase}] {occurrence.Location}: {diagnostic.Message}");
// Reopen or rewind seekable input before conversion: inspection consumes it.
```

`DiagnosticPhase.Parse` identifies adapter findings, `Destination` identifies
shared feature-policy checks, `Layout` covers font resolution and pagination,
`Provider` covers optional PDF import/OCR and `Write` identifies serialization.
Default inspection fully parses the input and resolves fixed-page fonts using the
same caller/bundled font policy. It also checks code fonts, unsupported shaping, XPS
hyperlinks, keep-with-table and cell page controls directly from the model, without
pagination. These findings use the Destination phase in both inspection and conversion.
Font-only inspection records coalesced runs and list-label mappings in one pass. It
does not paginate. `includeLayout: true` runs
full PDF/XPS layout without writing a PDF/XPS; it is invalid for reflowable outputs.
PDF import invokes the same optional provider and configured OCR as conversion,
including their costs and prerequisites. Managed DOCX export inspection needs no
native provider or network access.

`InputBytes`, retained feature/character counts and admitted decoded image pixels
are observations, not forecasts of original content or process memory. DOCX
`ArchiveEntryCount` and `DeclaredExpandedBytes` describe checked ZIP metadata;
null means unavailable for another adapter. Fonts identify actual resolved mappings
for PDF/XPS. `PageCount` is from actual optional layout, never a heuristic estimate.

Inspection cannot observe output-only losses, output byte limits, final I/O failures
or unperformed layout findings. For example, `Pdf.ImageAlternativeText` is emitted
by PDF serialization and is absent from preflight; a font's missing-glyph finding
can require full layout. A clean report does not prove lossless conversion. Inspect
and conversion must use the same options/font sources/clock for comparable findings.

`Loss` entries throw `NotSupportedException` in Strict mode before destination copying;
they become warnings in BestEffort and Lenient. `Report` entries are informational
unless the call explicitly reports a warning (for example Lenient scaling/splitting).
Missing glyphs fail in Strict and use visible fallback characters otherwise.

Lenient also scales oversized images/text, splits oversized rows/groups, constrains
unusable indentation/cell widths and omits oversized header repetition. It is not a
corruption recovery mode. Malformed packages/fonts, limits, cancellation, I/O failures
and invalid caller configuration still throw. Oversized stories are scaled and their
reservation constrained; see the enterprise audit for diagnosed approximations.

`ConversionResult.Metrics` reports page count (null for reflowable HTML), output bytes,
elapsed time, requested/resolved font families and the number of substituted families.
Configure options/font collections before concurrent calls; the converter is stateless.

The table is generated by `eng/generate-diagnostic-catalog.py`; runtime-dependent OCR
confidence values supplement these messages. Failure prefixes such as `Limit.*`,
`Docx.Field`, `Image.Invalid` and `Font.Invalid` identify exceptions, not successful
conversion diagnostics.

| Code | Policy | Meaning |
| --- | --- | --- |
| `Bookmark.Output` | Loss | Binary DOC output retains bookmark text without named bookmark ranges. |
| `Comments.Ignored` | Loss | Comments are omitted from the rendered document. |
| `Doc.AdjacentTables` | Loss | An empty paragraph separates adjacent tables in binary Word output. |
| `Doc.Alignment` | Loss | Unsupported paragraph alignment becomes left aligned. |
| `Doc.Code` | Loss | Inline code semantics are not retained in binary Word output. |
| `Doc.FixedMargin` | Loss | Fixed negative vertical margins use their absolute distance; header overlap behavior is not retained. |
| `Doc.Formatting` | Loss | Binary DOC import reconstructs the supported formatting subset. |
| `Doc.Heading` | Loss | Outline levels beyond six are flattened. |
| `Doc.HiddenText` | Loss | Hidden text is omitted. |
| `Doc.ImageAlternativeText` | Loss | This binary Word profile does not retain image alternative text. |
| `Doc.ImageBorder` | Loss | Picture borders are not retained. |
| `Doc.ImageEffects` | Loss | Mirrored picture transforms are not retained. |
| `Doc.ImageVariants` | Loss | Only the first embedded raster variant is retained. |
| `Doc.ListAppearance` | Loss | List styling, suffixes and custom restart behavior use the standard list profile. |
| `Doc.ListFormat` | Loss | Unsupported list formats become decimal numbering. |
| `Doc.ListLabel` | Loss | Custom list labels become decimal numbers or round bullets. |
| `Doc.MetadataCodePage` | Loss | An unsupported Title/Author metadata code page is not retained. |
| `Doc.MetadataType` | Loss | An unsupported Title/Author property type is not retained. |
| `Doc.OtherStories` | Loss | Notes, comments and textboxes outside the header/footer profile are not imported. |
| `Doc.PageBreak` | Loss | Explicit page breaks become line breaks. |
| `Doc.PictureRecord` | Loss | Unsupported picture shape records are not retained. |
| `Doc.PieceProperties` | Loss | Some piece-level formatting properties are not retained. |
| `Doc.Revisions` | Loss | Tracked changes are not preserved. |
| `Doc.SectionBreak` | Loss | Section breaks other than next-page become next-page breaks. |
| `Doc.SectionLayout` | Loss | Additional section properties are not retained. |
| `Doc.SpecialContent` | Loss | Unsupported object and special-content markers are omitted. |
| `Doc.StyleKind` | Loss | Table and numbering style formatting is not retained. |
| `Doc.TableAppearance` | Loss | Borders, shading and advanced table appearance are not retained. |
| `Doc.TableGrid` | Loss | Omitted leading DOC grid columns are represented by empty cells. |
| `Doc.TableHeaders` | Loss | Noncontiguous header rows are not retained. |
| `Doc.TableWidth` | Loss | Percentage table widths become automatic widths. |
| `Doc.UnsupportedProperty` | Loss | Some document appearance, metadata or advanced formatting properties are not retained. |
| `Docx.AdditionalParts` | Loss | Unrecognized parts such as headers, annotations or embedded content are not preserved in this conversion profile. |
| `Docx.Alignment` | Loss | Justified, distributed and direction-dependent alignment becomes left alignment. |
| `Docx.AlternateContent` | Loss | Alternate content without an understood choice or fallback is omitted. |
| `Docx.AuxiliaryMetadata` | Report | Document properties, font/theme catalogs and editor metadata are not retained; unsupported content formatting is diagnosed separately. |
| `Docx.Block` | Loss | Unsupported document block structure is flattened where supported content is available. |
| `Docx.BookmarkEndIgnored` | Report | An unmatched bookmark end is ignored while retaining its surrounding content. |
| `Docx.CellPadding` | Loss | Unknown cell margin side is omitted. |
| `Docx.Columns` | Loss | Only one to three equal-width body columns are supported. |
| `Docx.Drawing` | Loss | Unsupported or linked drawings are omitted; remote images are never fetched. |
| `Docx.FixedMargin` | Loss | Fixed negative vertical margins use their absolute distance; header overlap behavior is not retained. |
| `Docx.Gutter` | Loss | Binding gutters are not retained. |
| `Docx.HeadingLevel` | Loss | Heading levels greater than six are not supported in Markdown. |
| `Docx.Hyperlink` | Loss | An internal or unresolved hyperlink becomes plain text. |
| `Docx.ImageCropPadding` | Loss | Outward image crop padding is omitted; the original image edge is retained. |
| `Docx.ImageEffects` | Loss | Arbitrary-angle image rotation is not retained. |
| `Docx.Inline` | Loss | Unsupported inline content or annotations are flattened where text is available. |
| `Docx.LanguageMetadata` | Report | Language/proofing metadata is not retained; Unicode text is preserved. |
| `Docx.ListAppearance` | Loss | Centered/right-aligned list labels use left label alignment. |
| `Docx.ListBehavior` | Loss | Unsupported auxiliary numbering behavior is not retained. |
| `Docx.ListFormat` | Loss | An unsupported number format uses decimal numbering. |
| `Docx.ListLabel` | Loss | A private-use bullet without portable glyph semantics uses a round bullet. |
| `Docx.ListRevision` | Loss | Numbering revisions are not retained. |
| `Docx.MissingStyle` | Loss | A referenced style is missing; default text formatting is used. |
| `Docx.NumberingFeature` | Loss | Picture bullets and auxiliary numbering metadata are not retained. |
| `Docx.PageLayout` | Loss | Section features such as headers, footers, columns and page numbering are not retained. |
| `Docx.PageNumberFormat` | Loss | Chapter-based page-number prefixes are not retained. |
| `Docx.ParagraphFormatting` | Loss | Paragraph formatting such as spacing, paragraph-mark styling or page controls is not retained. |
| `Docx.ParagraphIndent` | Loss | Character-unit and direction-dependent indentation is not retained. |
| `Docx.ParagraphSpacing` | Loss | Automatic and character-unit paragraph spacing is not retained. |
| `Docx.RelativeWidth` | Loss | Percentage table widths become automatic widths. |
| `Docx.RunFormatting` | Loss | Unsupported run properties such as font families and complex-script formatting are not retained. |
| `Docx.SectionBreak` | Loss | An unsupported section transition becomes a next-page break. |
| `Docx.Settings` | Loss | Tab and character-spacing settings are not retained. |
| `Docx.Shading` | Loss | Patterned shading uses the solid background color. |
| `Docx.TableCell` | Loss | Nested tables and unsupported cell content are not retained. |
| `Docx.TableFormatting` | Loss | Table row formatting is not retained. |
| `Docx.TableGrid` | Loss | Omitted grid columns are represented by empty cells. |
| `Docx.TableHeaders` | Loss | Noncontiguous repeating header rows are not retained. |
| `Docx.TableIndent` | Loss | Nonabsolute table indentation is not retained. |
| `Docx.TableStructure` | Loss | Table content-control and custom XML wrappers are flattened while retaining their cells and paragraphs. |
| `Docx.ThemeColor` | Loss | Theme color uses its stored RGB fallback. |
| `Docx.ThemeFont` | Loss | Theme fonts without a resolved family use the explicit family or output fallback. |
| `Docx.Underline` | Loss | Unsupported underline decoration uses a single line. |
| `Ebook.ActiveContent` | Loss | Event handlers are omitted. |
| `Ebook.Alignment` | Loss | Unsupported or direction-dependent ebook alignment becomes left alignment. |
| `Ebook.Block` | Loss | Unsupported ebook block formatting was flattened. |
| `Ebook.CellAlignment` | Loss | Multiple cell paragraphs use the first paragraph's alignment. |
| `Ebook.Css` | Loss | CSS beyond supported inline alignment, font size, RGB color and table/image geometry is not preserved. |
| `Ebook.CssColor` | Loss | Only explicit six-digit RGB colors are supported. |
| `Ebook.CssLength` | Loss | Only positive absolute point/pixel dimensions and nonnegative margins are supported. |
| `Ebook.EmptyTable` | Loss | An empty table without rows is omitted. |
| `Ebook.InlineFormatting` | Loss | Unsupported inline formatting is flattened. |
| `Ebook.ListLevel` | Loss | A list starting at a missing ancestor level is moved to the nearest available level. |
| `Ebook.ListStyle` | Loss | Reversed and custom list styles use decimal numbering or bullets. |
| `Ebook.ListValue` | Loss | Per-item numbering overrides are not retained. |
| `Ebook.MergedCells` | Loss | A zero column span is interpreted as one column. |
| `Ebook.NestedTable` | Loss | Nested tables are flattened. |
| `Ebook.Quote` | Loss | Block quote indentation is flattened. |
| `Ebook.TableIndent` | Loss | Table indentation is not retained in ebook output. |
| `Ebook.Whitespace` | Loss | Reflowable ebook content collapses repeated spaces, tabs and paragraph-edge spaces. |
| `Epub.ContentType` | Loss | Non-XHTML spine content is unsupported. |
| `Field.LayoutDidNotConverge` | Loss | Calculated references did not stabilize within four layout passes. |
| `Field.OrphanInstruction` | Report | An orphan field instruction is omitted. |
| `Field.Output` | Loss | Fields use cached result text in this output profile. |
| `Field.UnsupportedUsedCachedResult` | Report | An unsupported, cyclic or unresolved reference retains its cached result. |
| `Font.CffOutlinesConverted` | Report | Static CFF outlines use embedded TrueType subsets with at most one font unit of curve/coordinate approximation. |
| `Font.MissingGlyph` | Report | A face without a usable replacement glyph uses the bundled glyph font for the affected run. |
| `Font.Substituted` | Loss | A requested font family uses a fallback; inspect font resolution metrics for family names. |
| `Formatting.RunAppearance` | Loss | This output profile does not retain requested font families or additional text decoration. |
| `HeaderFooter.Output` | Loss | This output profile does not preserve header/footer stories. |
| `Html.DrawingPosition` | Loss | Positioned drawings use inline frames in reflowable HTML. |
| `Html.HeadersFootersOmitted` | Report | Headers and footers are omitted by the HTML output policy. |
| `Html.ImageOmitted` | Loss | Image embedding is disabled; alternative text is retained. |
| `Html.PageFieldsUsedCachedResult` | Report | Page fields retain their cached text because HTML has no fixed pagination. |
| `Html.PageGeometryIgnored` | Report | Page geometry and section breaks are ignored by the reflowable HTML policy. |
| `Html.PaginationIgnored` | Report | Pagination controls are omitted from the reflowable HTML document. |
| `Html.TabStopsApproximated` | Report | HTML uses non-breaking spaces for tabs; fixed tab alignment and leaders are approximated. |
| `Image.AnchorApproximated` | Loss | This destination uses inline placement for anchored images. |
| `Image.External` | Loss | Remote and data-URI ebook images are not fetched. |
| `Image.Format` | Loss | Only PNG and JPEG ebook images are supported. |
| `Image.Output` | Loss | This output profile cannot embed images; alternative text is retained where available. |
| `Image.Reencoded` | Report | A GIF/BMP raster is re-encoded as PNG; GIF output uses the first frame. |
| `Image.Scaled` | Loss | Image dimensions exceed the 1584-point profile and are scaled proportionally; encoded pixels are retained. |
| `Image.ScaledToFit` | Report | An oversized image is proportionally scaled to fit printable space. |
| `Image.TransformOutput` | Loss | This destination retains original image pixels without crop, rotation or mirroring. |
| `Image.Unsupported` | Loss | Images are not imported yet; their alternative text is retained without fetching resources. |
| `Image.UnsupportedFormat` | Loss | An unsupported image format uses a visible placeholder at its declared size. |
| `Image.WrapApproximated` | Loss | Tight/through wrapping uses the rectangular square-wrap profile. |
| `Kindle.Flows` | Loss | Additional CSS, SVG and resource flows are not retained. |
| `Kindle.ListLayout` | Loss | Kindle list indentation depends on the reading application's reflow layout. |
| `Layout.AnchorBounds` | Report | A positioned drawing extends outside the physical page and is clipped at the page edge. |
| `Layout.CellPageControls` | Loss | Paragraph page controls inside table cells use the table row's pagination policy. |
| `Layout.CodeFont` | Loss | Code content uses the embedded proportional font. |
| `Layout.ContinuousGeometry` | Loss | A continuous section with incompatible physical geometry begins a new page. |
| `Layout.ContinuousPageRestart` | Loss | A page-number restart on a shared physical page takes effect on the following page. |
| `Layout.DocSections` | Loss | Binary DOC output retains next-page sections without columns or displayed-page-number restarts. |
| `Layout.DocTabStops` | Loss | Binary DOC retains tab characters with default stops; custom tab definitions are not retained. |
| `Layout.ExplicitBreak` | Loss | Page and column breaks become line breaks in this output profile. |
| `Layout.Hyperlink` | Loss | This XPS profile retains hyperlink text without an interactive target. |
| `Layout.IndentApproximated` | Report | Unusable indentation is constrained to printable space. |
| `Layout.KeepWithTable` | Loss | Keeping a paragraph with the following table is not supported by this layout profile. |
| `Layout.LineSpacing` | Loss | Line spacing smaller than content height is expanded to avoid clipping or overlap. |
| `Layout.Paragraph` | Loss | Paragraph spacing, indentation and pagination controls are not preserved by this output profile. |
| `Layout.Sections` | Loss | Page geometry and section breaks are not preserved by this reflowable output profile. |
| `Layout.Shaping` | Loss | Unsupported complex-script shaping and supplementary characters use visible fallback characters. |
| `Layout.StoryAreaConstrained` | Report | Oversized header/footer reservation is constrained to retain printable body space. |
| `Layout.StoryPositionApproximated` | Report | A header/footer outside page bounds is moved onto the page. |
| `Layout.StoryScaledToFit` | Report | An oversized header/footer is proportionally scaled to retain body space. |
| `Layout.TabAlignment` | Loss | Unsupported tab alignment uses a left tab stop. |
| `Layout.TabLeader` | Loss | Unsupported tab leaders are omitted. |
| `Layout.TableRowHeight` | Report | An exact row height is increased to retain its content. |
| `Layout.TextBoxContent` | Report | Tables and nested boxes in a positioned text box are flattened to formatted text in reading order. |
| `Layout.TextBoxOverflow` | Report | Text-box content is proportionally reduced to fit its frame. |
| `Layout.TextBoxPadding` | Report | Text-box padding is omitted to retain usable content space. |
| `Layout.TextScaledToFit` | Report | Oversized text is reduced to fit printable space. |
| `Link.UnsupportedTarget` | Loss | Only absolute HTTP, HTTPS and mailto hyperlinks are retained; other targets become text. |
| `List.AdvancedOutput` | Loss | This destination uses decimal/round-bullet list definitions without the richer label profile. |
| `List.Structure` | Loss | Editable list numbering becomes literal labels in this text profile. |
| `Markdown.Alignment` | Loss | Markdown prose cannot preserve paragraph alignment. |
| `Markdown.BlockStructure` | Loss | An unsupported container, such as a block quote, is flattened into its content. |
| `Markdown.CellParagraphs` | Loss | Multiple cell paragraphs become spaces in pipe tables. |
| `Markdown.CodeLanguage` | Loss | Code block language labels are not retained in DOCX. |
| `Markdown.CodeLineBreak` | Loss | Inline code line breaks become spaces in Markdown. |
| `Markdown.EmptyParagraph` | Loss | Markdown does not preserve explicit empty Word paragraphs. |
| `Markdown.Html` | Loss | Inline HTML is retained as literal text, without rendering or executing it. |
| `Markdown.LinkTitle` | Loss | Hyperlink titles are not retained. |
| `Markdown.ListItem` | Loss | A list item without an initial paragraph cannot retain its list marker. |
| `Markdown.ParagraphWhitespace` | Loss | Leading or trailing paragraph whitespace may be normalized by Markdown readers. |
| `Markdown.RunAppearance` | Loss | Markdown cannot represent font sizes and colors. |
| `Markdown.Tab` | Loss | Tabs become four spaces in Markdown prose. |
| `Markdown.TableAlignment` | Loss | Different alignments within a table column become the first cell's alignment. |
| `Markdown.TableCell` | Loss | Complex Markdown table cells are not supported. |
| `Markdown.TableHeader` | Loss | An empty header row is added because pipe tables require a header. |
| `Markdown.TableLineBreak` | Loss | Line breaks in table cells become spaces. |
| `Markdown.UnsupportedBlock` | Loss | Unsupported Markdown block structure could not be preserved. |
| `Markdown.UnsupportedInline` | Loss | Unsupported inline formatting is flattened where text is available. |
| `Notes.Numbering` | Loss | An unsupported note number format uses decimal labels. |
| `Notes.PlacedAtEnd` | Loss | This destination retains notes as numbered end text without editable note semantics. |
| `Notes.Properties` | Loss | Custom note placement and separator properties use the standard page-bottom profile. |
| `Notes.Separator` | Loss | Custom note separator content and appearance use a standard horizontal rule. |
| `Notes.TableLayout` | Loss | Tables inside notes use flattened paragraphs. |
| `Ocr.Columns` | Report | OCR read two text columns down the left column, then down the right column, using a wide uninterrupted gutter. |
| `Ocr.Confidence` | Report | Mean of per-page recognized-word confidences:  |
| `Ocr.English` | Report | English printed-text OCR was used; recognition requires review. |
| `Ocr.Orientation` | Report | OCR selected a rotated or deskewed raster using recognition confidence; review the recognized text. |
| `Pdf.ImageAlternativeText` | Loss | This PDF profile does not create accessibility tags for images. |
| `Pdf.ImageMask` | Loss | Image stencil/soft masks are not retained by the extraction profile. |
| `Pdf.ImagePlacement` | Loss | Images retain their displayed dimensions in reading-order paragraphs; original anchoring, clipping and transforms are not retained. |
| `Pdf.OcrImageLoss` | Loss | OCR replaces the rendered page with editable text; embedded images are not retained. |
| `Pdf.OcrTextReplacement` | Loss | A large image triggered whole-page OCR; existing digital text is replaced by recognition results. |
| `Pdf.Reconstruction` | Loss | PDF reading order is reconstructed into editable paragraphs; original page layout and vector graphics are not retained. |
| `Revisions.Accepted` | Loss | Revision markup is rendered in its accepted state; revision history is not retained. |
| `Shape.Appearance` | Loss | Unsupported text-box effects, theme fills and drawing appearance are not retained. |
| `Shape.GeometryApproximated` | Loss | Only unrotated rectangular text boxes are supported. |
| `Shape.TextBoxFlattened` | Loss | This destination retains text-box paragraphs as inline text. |
| `Shape.TextBoxLayout` | Loss | Text-box autofit and vertical text are not supported. |
| `Table.AutomaticGrid` | Report | Automatic table columns use equal widths when fixed geometry is required. |
| `Table.BorderApproximated` | Loss | An unsupported border style uses a single line. |
| `Table.Decoration` | Loss | This output profile omits cell shading, borders and vertical alignment while retaining cell content. |
| `Table.Geometry` | Loss | This text format cannot retain table widths and cell padding. |
| `Table.Grid` | Loss | Omitted trailing grid columns are represented by empty cells. |
| `Table.GridWidth` | Loss | Unspecified grid widths are approximated using the available table width. |
| `Table.HeaderRepeatOmitted` | Report | An oversized table header is retained once without repetition. |
| `Table.LineScaledToFit` | Report | Unusable cell padding is omitted and an oversized line is scaled to fit printable space. |
| `Table.MergedGroupSplit` | Report | An oversized vertically merged group is split at line boundaries. |
| `Table.Merges` | Loss | Merged-cell geometry is not preserved by this output profile. |
| `Table.NestedFlattened` | Loss | Nested tables retain their text in reading order as cell paragraphs in this output profile. |
| `Table.NestedHeaders` | Loss | Nested-table headers are rendered once; repetition inside a split parent row is not supported. |
| `Table.Pagination` | Loss | Non-splitting table-row controls are not preserved by this reflowable output profile. |
| `Table.RelativeWidthOutput` | Loss | Binary DOC retains the absolute table profile; percentage widths and autofit are not retained. |
| `Table.RowForcedSplit` | Report | An oversized non-splitting row is split at line boundaries. |
| `Table.ShadingApproximated` | Loss | Patterned cell shading uses its solid background color. |
| `Table.WidthApproximated` | Report | Cell width and padding are constrained to printable space. |
| `Template.ControlProperties` | Loss | Control-specific validation, locking, data bindings and appearance are outside the editable tagged-control profile. |
| `Template.FieldStructure` | Loss | Controls inside field results retain cached content without template execution. |
| `Template.MissingValue` | Loss | Missing template data uses the explicitly selected fallback policy. |
| `Template.NestedBlockControls` | Loss | Nested controls sharing a block are outside the editable template profile. |
| `Template.NestedInlineControls` | Loss | Nested controls sharing inline text are outside the editable template profile. |
| `Template.NestedRowControls` | Loss | Nested controls sharing one table row are outside the editable template profile. |
| `Template.TableControl` | Loss | Tagged non-row table wrappers retain cached cells and paragraphs but cannot execute safely. |
| `Template.UnmergedOutput` | Loss | Unexecuted template constructs use cached content without editable template semantics in this destination. |
| `Text.Formatting` | Loss | Plain text cannot represent rich formatting. |
| `Text.TableStructure` | Loss | Table structure becomes tab-separated rows in plain text. |
