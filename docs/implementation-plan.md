# wordnet implementation plan

Updated 2026-10-13 for the unpublished **0.1.0-alpha.10** candidate.

The six production workstreams are tracked in [production readiness](production-readiness.md).
This candidate adds section/paragraph layout, merged multi-paragraph tables, repeating
headers and long-row splitting, independent geometric and OCR corpora, isolated
malformed-input/resource campaigns, and package/API qualification automation. The shared
suite has 348 cases after PR review regressions. The earlier 301-case actual Framework
4.7.2 source and packaged baseline passed with the explicit legacy x64 JIT configuration;
the revised converter requires fresh minimum-runtime qualification. Android API 21 qualification remains unresolved
and further investigation is deferred at the user's request. Keep all four
targets and defer macOS. Do not relabel the candidate production-ready without completing
those environment gates and reviewing its declared feature profile.

Alpha.6 changes the public namespace to `WordNet`, with matching namespaces on every
target. Consumers must update imports/qualified names and rebuild. Package and assembly
identities stay `wordnet`; the converter feature profile is unchanged from alpha.5.

Alpha.7 updates the graphics/native assets, PDF decoders and compatibility dependencies,
and migrates the shared test suite to xUnit v3. All four library targets are preserved.
See [dependency maintenance](dependencies.md) for versions and the retained build-only
.NET Standard reference dependency.

Alpha.8 added font sizes/colors, table widths/cell padding, editable decimal/bullet
lists and bounded inline PNG/JPEG through Word/EPUB and PDF export/import. Alpha.9
had 236 cases, including independent input, schema/geometry/pixel assertions and
malformed-resource regressions. No runtime dependencies or target frameworks changed.

All 28 agreed directions and local English OCR for scanned/mixed PDF input now have
working implementations and real tests. This is a functional baseline. High document
fidelity and all-platform qualification remain unfinished.

Use independently authored code, public specifications and appropriately licensed
dependencies. Preserve fixture provenance and notices; exclude proprietary resources.

## Agreed scope

- DOC and DOCX, including DOC ↔ DOCX.
- Each Word format ↔ PDF, MD, TXT, EPUB, MOBI and AZW3.
- Each Word format → XPS; no XPS input.
- English printed OCR; handwriting and more OCR languages deferred.
- Core content first; explicit diagnostics for unsupported features. Password protection,
  revisions/comments and editable charts/equations remain deferred.
- Preserve `net472;netstandard2.0;net8.0;net10.0` and matching APIs.
- Qualify Windows/Linux x64/ARM64 and Android ARM64; Framework remains Windows.
  macOS is deferred by the user.

## Completed baseline

| Area | Evidence |
| --- | --- |
| Public API | Streams, async I/O, checkpoints, finite admission limits and loss diagnostics |
| DOCX / Markdown / text | Editable subset, independent Open XML validation and regressions |
| DOC | Headings, bold/italic styles/runs and editable simple tables; independent Apache POI fixtures and export checks |
| EPUB | OPF/spine import, EPUB 3 output, structure and hostile-input checks |
| MOBI / KF8 | PalmDB text formats, PalmDOC decoder and KF8 mapping; external unpacking |
| PDF / XPS | Embedded-font layout, wrapping/pagination and coordinate checks; PDF preview |
| OCR | Local Tesseract, rasterization and exact editable text on scanned/mixed fixtures |
| Frameworks | Four targets preserved; desktop suite and an Android device harness sharing the same tests |
| Paragraph alignment | Physical left/center/right alignment through Word, ebooks and fixed pages; Markdown table columns; 24 new tests including independent DOC input and output validation |
| Formatting and images | Word/EPUB appearance and list mapping, independent DOC/PDF/EPUB inputs, PNG/JPEG pixels and physical dimensions; see the fidelity profile |
| Runtime/package validation | Full suites on exercised Windows/Linux/Android runtimes, explicit .NET Standard consumption; see testing evidence for build-specific results and limits |

Minimal-content tests do not establish complete DOC/KF8 compatibility, arbitrary PDF
reading order, rich page fidelity, real-world OCR accuracy or every platform.

## Remaining work

1. **Independent corpora and interoperability:** rich DOC and KF8 fragment/index variants,
   multi-spine EPUB, real viewer checks, malformed compression and fuzzing. DOC formatting
   records and multi-story behavior need broader validation.
2. **Document semantics:** continuous/parity sections, richer styles/numbering, notes and metadata.
   Model each feature before adapters claim preservation.
3. **DOC fidelity:** extend the implemented [formatting/table profile](doc-format.md)
   with richer typography, image effects and supported objects from MS-DOC.
4. **Layout:** shaping, font selection/fallback, advanced line breaking, continuous sections,
   keep-with-table behavior and floating images. Extend the pinned reference corpus.
5. **PDF/OCR:** complex columns/tables, image transforms and precise positions; broader
   independently sourced scan corpora beyond the eight measured regression scenarios.
6. **Ebooks:** CSS/media/navigation/metadata and independently validated KF8 variants.
   Keep unsupported compression/DRM explicit.
7. **Platforms:** maintain four Windows/Linux CI lanes and Android ARM64 device tests.
   Native Windows/Linux x64 and ARM64 CI now passes all 301 conversion/OCR tests on
   .NET 8/10 with default runtime settings; Windows x64 also passes Framework.
   Check native codecs, OCR, model provisioning, fonts, architecture, trimming/AOT and
   minimum OS/API requirements.
8. **Resources:** expand deterministic mutation seeds and longer production-like workloads.
   Isolated fuzzing, throughput/RSS campaigns and host guidance now exist; admission bounds
   still cannot enforce hard native CPU/RSS deadlines within an application process.

## Release gates

- Every required direction and scanned/mixed OCR case executes meaningful assertions;
  no required case is skipped or represented by placeholder output.
- Independent readers/fixtures validate supported features. Round trips supplement them.
- Define corpus-based character/word-error, reading-order and visual thresholds. Exact
  matches on the small clean OCR fixtures are not broad benchmarks.
- Test deferred/unsupported features for explicit rejection or documented losses.
- Execute packed NuGet consumers on each claimed platform, including the explicit
  .NET Standard asset and oldest claimed Framework runtime.
- Complete security, dependency audit/license/native deployment, locked restore,
  formatting, deterministic build, API compatibility and documentation checks.
- Establish compatibility/versioning policy, performance budgets and reproducible
  publication before releasing to NuGet.

No additional product questions block progress. Licensed representative documents
would improve coverage, but owned/public fixtures allow implementation to continue.
