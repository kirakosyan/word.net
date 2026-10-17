# ADR B5-ODT: bounded ODT exchange

Design proposal for [#28](https://github.com/kirakosyan/word.net/issues/28),
2026-10-17. This records a future implementation contract. ODT is unsupported today;
no enum, conversion direction, dependency or runtime behavior changes in this work.
See the [bucket 5 decision index](../later-design-decisions.md).

## Decision and alternatives

Use an independently authored managed ODF adapter over the existing semantic model,
not DOCX-to-HTML-to-ODT conversion. HTML would erase page masters, list identities and
table controls. Target the **ODF 1.3 text package subset**, with explicit version
checking; FODT, templates, master documents, ODF 1.4 extensions and encrypted packages
are excluded initially. Supporting a subset of an OASIS standard does not establish
full ODF conformance.

Reuse `Internal/PackageArchive.cs` and bounded XML parsing after auditing ODF path and
manifest rules; implement an ODF-specific package layer rather than weakening DOCX
path rules. Keep all five library TFMs and managed/offline operation. Built-in ZIP,
XML and the existing image admission code need no new runtime package. An ODF SDK
would duplicate package/model policy and expand maintenance; Java ODF Toolkit and
LibreOffice process automation are unsuitable mandatory portable-core dependencies.
LibreOffice remains an external test reader. OASIS schemas and validator tools are
development assets with their own notices; they are not relicensed as MIT code.

HTML/RTF should precede implementation: their adapters establish offline resource
handling, explicit losses and independent exchange tests without adding ODF style
resolution simultaneously. Those dependencies are now delivered in PR #37. They
provide reusable tests and experience, not an obligation to expand ODT scope.

## Format and API proposal

After a separately accepted implementation issue, append `DocumentFormat.Odt = 12`
if that value is still free. Preserve existing enum values and overload metadata.
Initially enable only **ODT → DOCX** and **DOCX → ODT** in `CanConvert`; add an explicit
pair gate before broadening `IsReadable`, whose current Word hub would otherwise
advertise ODT exchanges with DOC too. `Unknown`, ODT → ODT and ODT → other formats
remain false. Keep ODT unsupported in `WordDocument.Load/Save` in the first exchange
profile; document-session support needs a separate follow-up. Replace numeric-range
assumptions with explicit format predicates when adding the enum so future members
cannot silently expand editing support. Do not add a second document model or expose
ODF XML types publicly.

Use current `ConversionOptions`, ownership and result contracts. Strict rejects
every detected unsupported feature before copying output; BestEffort/Lenient report
losses and retain supported content. Malformed packages, encryption, unsafe resources
and limit breaches fail in every mode. No fidelity mode executes active content.
Proposed `Odt.*` diagnostic names below are reserved design vocabulary, not emitted
codes or a new supported API.

## Semantic mapping and expected losses

| ODF input / DOCX source | Shared representation and first output profile |
| --- | --- |
| Paragraphs, headings, spans, spaces, tabs, line breaks | Paragraph/run order, heading level, explicit whitespace and current tab profile; reject invalid numeric whitespace counts before expansion. |
| Named, automatic and default styles | Resolve family-specific defaults → parent chain → local properties, then supported direct properties. Detect cycles/missing parents. Emit deterministic automatic styles keyed by supported resolved appearance; retain appearance, diagnose lost style identity. No conditional styles or theme equivalence claim. |
| Font size/family, bold/italic, underline, RGB, alignment, spacing, indentation | Existing run/paragraph appearance in points. Convert cm/mm/in/pt with finite bounds; percentages only where the existing model defines an unambiguous base. Theme/complex text effects and unsupported units report `Odt.StyleLoss`. |
| Lists | Map list IDs, up to nine levels, start values/restarts and decimal/alphabetic/Roman/bullet labels; diagnose picture bullets, unsupported label alignment and numbering formats. Preserve restart semantics rather than matching generated labels only. |
| Page layouts/master pages | One next-page section per supported master-page transition; physical size/margins and simple header/footer stories. Arbitrary section nesting, linked sections, unequal columns, dynamic master-page rules and parity layout outside that mapping report `Odt.SectionLoss`. |
| Tables | Explicit column widths, bounded percentage widths, rectangular spans, row headers, cell padding/background/borders and paragraph content. Expand repeated rows/columns under aggregate budgets; validate covered cells and merge rectangles. No formulas, database tables or spreadsheet calculation. Nested tables require a later slice. |
| Inline PNG/JPEG `draw:frame` | Existing admitted raster bytes, physical frame and alternative text. Package-local resources only. Floating frames, vector/OLE objects, transforms beyond the existing profile and unsupported raster types diagnose `Odt.DrawingLoss`; available text/alt remains in permissive modes. |
| `dc:title`, creator | Existing Title/Author; diagnose omitted custom/user metadata and editing statistics. Do not invent authors or timestamps. |
| Bookmarks and ordinary hyperlinks | Remap unique identifiers; safe literal links only, never follow them. Fields/TOC retain supported cached visible text with `Odt.FieldLoss`; no OpenFormula evaluation. |
| Notes, review history, charts/math, forms, scripts | Outside initial exchange profile. Preserve available accepted visible text/alt with explicit losses where safely parseable; encrypted, script-bearing and embedded executable packages are rejected. No opaque arbitrary part copying. |

Fixed-page equality is not an acceptance criterion for a reflowable exchange. Pinned
reader renderings must retain text/order, image aspect and table structure; pagination
differences and documented style flattening are expected losses.

## Package and resource admission

Require ZIP ODT with first, uncompressed `mimetype` containing exactly
`application/vnd.oasis.opendocument.text`, no extra field on that entry; require
`META-INF/manifest.xml`, its root media type, and exactly one declaration per required
resource. Validate actual content types rather than trusting extension/manifest alone.
Read `content.xml`, optional `styles.xml` and `meta.xml`; diagnose omitted safe settings
and thumbnails. Reject duplicate/case-ambiguous names, traversal, absolute/file/network
IRIs, encoded separators and references leaving the package. Resolve a permitted
relative `./Pictures/name.png` exactly once without touching disk. No external image
fallback or document-linked resource loading. Reject encryption/signature declarations
rather than pretending to verify or preserve them.

Apply `MaximumInputBytes`, `MaximumOutputBytes`, `MaximumExpandedBytes`,
`MaximumArchiveEntries`, `MaximumCharacters`, `MaximumNestingDepth` and `MaximumPages`.
In addition to declared ZIP lengths, debit actual decompressed bytes across *all*
reads; XML characters and materialized model nodes across parts; pixels through the
existing aggregate image budget. Cap total resolved styles at 4,096, parent traversal
at the smaller of 32 and the nesting limit, list levels at nine, and expanded table
cells at 100,000 or the aggregate byte/node allowance, whichever is smaller. Check
repetition products with checked arithmetic **before** allocation. Shared resources
are admitted once; repeated semantic references still cost nodes. Cancellation is
checked per entry, style and expansion batch. Hard CPU/RSS deadlines need host isolation.

## Implementation slices and acceptance

1. **ODT-1 package/reader**, no advertised format: implement package preflight, styles
   and plain paragraphs; hostile manifest/path/DTD/expansion cases leave output sentinel
   unchanged in all modes. Add a standalone independently authored ODT fixture generator.
2. **ODT-2 writer and exchange gate**, depends on 1: plain text/styles/metadata; verify
   OASIS schema validity and LibreOffice ODT → DOCX and DOCX → ODT reading. Assert exact
   Unicode/whitespace, resolved appearance, metadata and only the two advertised pairs.
3. **ODT-3 business subset**, depends on 2: list restarts, master pages, simple tables
   and inline rasters. Independently assert table spans/widths, list IDs, image hashes,
   physical sizes and reading order; compare pinned renderings with declared tolerances.
4. **ODT-4 qualification**, depends on 3: full source and packed suites, Standard fallback,
   five-target API validation, malformed-input campaign and documentation. Only this
   slice can declare the bounded profile supported; nested tables remain separately scoped.

Each slice must update README/directions, fidelity, API/usage, diagnostic catalog,
sample and build-specific testing documentation. The [acceptance/provenance plan](acceptance.md)
specifies independently authored seeds, external reader controls and evidence gates.
No runtime implementation is needed to close the present design issue.

## References

- [ODF 1.3 packages, OASIS Standard 2021-04-27](https://docs.oasis-open.org/office/OpenDocument/v1.3/os/part2-packages/OpenDocument-v1.3-os-part2-packages.html), sections 3–4.
- [ODF 1.3 schema](https://docs.oasis-open.org/office/OpenDocument/v1.3/os/part3-schema/OpenDocument-v1.3-os-part3-schema.html), styles, text, tables and drawing elements.
- [Existing model](../../src/wordnet/Model/DocumentModel.cs), [package admission](../../src/wordnet/Internal/PackageArchive.cs), [public format](../../src/wordnet/DocumentFormat.cs).
