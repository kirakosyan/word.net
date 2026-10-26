# wordnet

[![NuGet version](https://img.shields.io/nuget/v/wordnet?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/wordnet)
[![NuGet downloads](https://img.shields.io/nuget/dt/wordnet?style=flat-square&label=downloads)](https://www.nuget.org/packages/wordnet)
[![Build and tests](https://img.shields.io/github/actions/workflow/status/kirakosyan/word.net/ci.yml?branch=main&style=flat-square&label=build)](https://github.com/kirakosyan/word.net/actions/workflows/ci.yml)
[![MIT license](https://img.shields.io/badge/license-MIT-64748b?style=flat-square)](https://github.com/kirakosyan/word.net/blob/main/LICENSE)
[![Donate on Patreon](https://img.shields.io/badge/Donate-Patreon-FF424D?style=flat-square&logo=patreon&logoColor=white)](https://www.patreon.com/16981794/join)

**Document conversion and automation for .NET.** Create, edit and convert Word documents with local processing, bounded resource use and explicit fidelity diagnostics.

- Convert DOC and DOCX to and from PDF, Markdown, text, EPUB, MOBI, AZW3, HTML and RTF. Export Word documents to XPS.
- Populate templates with mail-merge fields, repeated table rows, conditional content and caller-supplied images.
- Compose documents and export editable DOCX or selectable-text PDF without Microsoft Office or a remote service.
- Add optional local English OCR for scanned PDFs.
- Render managed PDF/XPS and SVG previews, with optional PNG previews and documented PDF/A-2b and PDF/UA-1 profiles.
- Inspect documents before conversion and enforce configurable resource limits with explicit fidelity diagnostics.

Windows and Linux · x64 and ARM64 · MIT licensed

[Quick start](#quick-start) · [Automation recipes](#automation-recipes) · [Supported formats](#supported-formats) · [Documentation](#documentation)

## Install

```sh
dotnet add package wordnet
```

The public namespace is `WordNet`. NuGet package and assembly names use `wordnet`.
The core package includes `wordnet.Fonts` and supports managed document export without native providers.

| Package | Purpose |
| --- | --- |
| `wordnet` | Conversion, editing, templates and managed export |
| `wordnet.Fonts` | Embedded open-license fonts; included automatically by `wordnet` |
| `wordnet.Ocr` | Optional provider for all PDF input, English OCR, PNG previews and native typography |

For **all PDF input**, including digital PDFs, install the optional provider at the same version:

```sh
dotnet add package wordnet.Ocr
```

This provider also supplies PNG previews and optional native typography. English OCR additionally requires a local Tesseract 5 library and `eng.traineddata`.
See the [OCR setup guide](https://github.com/kirakosyan/word.net/blob/main/docs/interoperability.md) and [deployment requirements](https://github.com/kirakosyan/word.net/blob/main/docs/testing.md).

## P1 additions in 1.3.0

PR [#85](https://github.com/kirakosyan/word.net/pull/85) merged unequal DOCX columns, [bounded report preprocessing](https://github.com/kirakosyan/word.net/blob/main/docs/report-preprocessing.md), body paragraph repeats, [DOCX ↔ ODT exchange](https://github.com/kirakosyan/word.net/blob/main/docs/odt.md), and [opt-in DOCX review workflows](https://github.com/kirakosyan/word.net/blob/main/docs/review-workflows.md). These additions ship in 1.3.0; earlier 1.2.x packages exclude them. The automation example includes executable P1 recipes. Existing default review interpretation and earlier format pairs remain; 1.3.0 limits ODT to the two DOCX exchanges; 1.4.0 also exports ODT directly to PDF.

## 1.3.0 object and typography additions

Release 1.3.0 adds bounded editable column/line charts and OMML equations,
managed PDF/XPS/SVG rendering, and opt-in Unicode 17 bidi/Hindi paragraphs with a
qualified ordinary PDF/UA-1 profile. Math needs an explicit caller MATH font; native
text needs the optional provider and suitable caller fonts. Objects and native/UA
output have separate combination limits. See the [P2 profile](https://github.com/kirakosyan/word.net/blob/main/docs/p2-objects-typography.md)
and [capability matrix](https://github.com/kirakosyan/word.net/blob/main/docs/p2-capability-matrix.json).
These changes shipped from [PR #86](https://github.com/kirakosyan/word.net/pull/86), building on P0 and P1 work. See the [1.3.0 release record](https://github.com/kirakosyan/word.net/blob/main/docs/releases/1.3.0.md) for qualification and publication status.

## 1.4.0 report and layout additions

Version 1.4.0 adds report chart binding, bounded scalar fields, structural paragraph/row
comparison, direct HTML/ODT-to-PDF conversion, corrected managed line/row metrics, opt-in
final-column balancing, and qualified native-table/story and object-PDF combinations.
There are 40 enabled conversion directions. These additions are absent from 1.3.x.
See the [profile and limits](https://github.com/kirakosyan/word.net/blob/main/docs/roadmap-99-implementation.md)
and [1.4.0 release notes](https://github.com/kirakosyan/word.net/blob/main/docs/releases/1.4.0.md).

Font metrics and table/note placement corrections can change pagination. Unsupported
scalar instructions retain cached results with informational diagnostics, including in
Strict mode. Whole-paragraph review decisions include the paragraph mark and its text.
Independent comparison failures remain recorded; this release makes no universal
rendering-parity claim.

## Quick start

Convert a DOCX document to PDF and inspect any reported fidelity losses. Stage output before replacing an existing file:

```csharp
using System;
using System.IO;
using System.Threading;
using WordNet;

using var source = File.OpenRead("input.docx");
using var staged = new MemoryStream();
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

var options = new ConversionOptions(
    FidelityMode.BestEffort,
    maximumInputBytes: 16 * 1024 * 1024,
    maximumOutputBytes: 32 * 1024 * 1024,
    maximumPages: 100);

var converter = new WordConverter();
ConversionResult result = await converter.ConvertAsync(
    source, staged, DocumentFormat.Docx, DocumentFormat.Pdf,
    options, cancellation.Token);

foreach (ConversionDiagnostic diagnostic in result.Diagnostics)
    Console.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");

File.WriteAllBytes("output.pdf", staged.ToArray());
```

Strict mode is the default and rejects detected unsupported features before copying output. Best effort retains supported content and reports losses. Lenient mode additionally approximates documented layout problems. Malformed input, resource limits, cancellation and I/O errors can still fail in every mode.

For DOCX byte arrays, `ConvertToPdf` and `ConvertToHtml` offer in-memory shortcuts with overloads that return diagnostics.

DOC and PDF import always report formatting or reconstruction loss and require best effort or lenient mode. Use `Inspect` / `InspectAsync` for preflight diagnostics, source locations, font mappings and optional fixed-page layout. `CanConvert` describes format pairs; it does not guarantee support for every feature in a document.

See [conversion usage](https://github.com/kirakosyan/word.net/blob/main/docs/interoperability.md) and [diagnostics](https://github.com/kirakosyan/word.net/blob/main/docs/diagnostics.md).

## Automation recipes

`WordDocument` provides bounded creation, editing, composition and template sessions:

```csharp
using System.IO;
using WordNet;

WordDocument document = WordDocument.Create();
document.AddParagraph("Quarterly report", headingLevel: 1);
document.AddParagraph("Completed 18 projects this quarter.");

using var output = new MemoryStream();
document.Save(output, DocumentFormat.Docx);
File.WriteAllBytes("report.docx", output.ToArray());
```

From a source checkout, one command generates a complete invoice and a two-section report, each as editable DOCX and PDF:

```sh
dotnet run --project examples/WordNet.Automation -c Release -- artifacts/automation-sample
```

The invoice includes repeated line items, a caller-provided logo, optional payment terms and totals calculated in application code. The report includes a summary table, header/footer and page fields refreshed during PDF layout. Both recipes print load, edit and save diagnostics and stage conversions before replacing files.

See the [recipe instructions](https://github.com/kirakosyan/word.net/blob/main/examples/WordNet.Automation/README.md) and [editing and template guide](https://github.com/kirakosyan/word.net/blob/main/docs/editing-and-templates.md) for syntax, limits and expected losses.

To populate an existing template containing a `MERGEFIELD Customer` field:

```csharp
using System;
using System.IO;
using WordNet;

var options = new ConversionOptions(FidelityMode.BestEffort);
using var template = File.OpenRead("template.docx");
WordDocument document = WordDocument.Load(
    template, DocumentFormat.Docx, out ConversionResult loadResult, options);
foreach (ConversionDiagnostic diagnostic in loadResult.Diagnostics)
    Console.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");

DocumentEditResult mergeResult = document.Merge(new { Customer = "Example Company" });
foreach (ConversionDiagnostic diagnostic in mergeResult.Diagnostics)
    Console.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");

using var output = new MemoryStream();
ConversionResult result = document.Save(output, DocumentFormat.Docx);
foreach (ConversionDiagnostic diagnostic in result.Diagnostics)
    Console.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");

File.WriteAllBytes("letter.docx", output.ToArray());
```

Templates use caller-supplied data and images. They do not execute arbitrary code or fetch remote resources.

## Supported formats

Each earlier bidirectional exchange below works with both DOC and DOCX. ODT exchanges with DOCX and exports directly to PDF; HTML also exports directly to PDF. Other pairs require DOC or DOCX on one side. XPS is output-only.

| Format | Implemented profile |
| --- | --- |
| DOCX ↔ DOC | Editable text, styles, lists, tables and supported raster drawings; binary DOC retains a documented subset |
| PDF ↔ Word | Managed selectable-text PDF export; digital text/simple raster import and optional English OCR |
| Markdown / text ↔ Word | Unicode text, headings, emphasis, links, lists and supported pipe tables; formatting losses are diagnosed |
| HTML ↔ Word | UTF-8 HTML5, bounded CSS, nested lists/tables, merged cells, embedded raster images and safe links |
| RTF ↔ Word | Bounded Unicode/code-page text, emphasis, paragraphs, basic lists/tables and PNG/JPEG |
| EPUB ↔ Word | EPUB package/spine reading and EPUB 3 writing with the supported XHTML/CSS/image subset |
| MOBI / AZW3 ↔ Word | Unencrypted MOBI 6 and standalone KF8 profiles; no DRM or hybrid containers |
| Word → XPS | Positioned text, embedded fonts, raster images and shared fixed-page layout |
| DOCX ↔ ODT | Bounded ODF 1.3 text/styles/lists/masters/tables/inline PNG/JPEG; explicit feature/style losses |
| HTML / ODT → PDF | Direct bounded reader-to-layout pipeline; supported text, lists, tables, images and page settings |

The shared layout profile includes section geometry, wrapping and pagination, table widths/merges/repeated headers, bounded drawings, notes and page fields. Optional profiles cover Arabic typography, semantic PDF/UA and previews. These capabilities have specific combination and fidelity limits.

PDF import reconstructs editable content; it does not recover arbitrary original tables, styles or pagination. OCR targets English printed text, with optional rotation/deskew and known two-column reading order. Handwriting is outside the profile.

Password encryption/decryption and DOCX/PDF digital signatures are not implemented and are excluded from the current roadmap at the owner's request. Password-protected input and signed-document workflows remain unsupported; no confidentiality, signature-validity or signer-trust utility is provided. Release 1.3.0 implements the bounded chart/equation subset described above; advanced charts and general mathematical layout remain unsupported. Default DOCX loads render revisions in their accepted state with diagnostics; Strict rejects revision/comment loss. The merged opt-in review profile preserves its declared subset separately. Complete Word pagination parity and arbitrary drawing/CSS behavior are not guaranteed.

See the [fidelity profile](https://github.com/kirakosyan/word.net/blob/main/docs/document-fidelity.md), [DOC subset](https://github.com/kirakosyan/word.net/blob/main/docs/doc-format.md) and [interoperability guide](https://github.com/kirakosyan/word.net/blob/main/docs/interoperability.md).

## English OCR

Add matching `wordnet.Ocr`, then provide a local **Tesseract 5 C API library** and
a directory containing `eng.traineddata`. Native dependencies must match the application
process architecture. Configure `EnglishOcrOptions` with absolute data-directory and
native-library paths, and pass it to `ConversionOptions(englishOcr: ...)`.

On Linux, provision `libtesseract5`, `libfontconfig1` and usable fallback fonts.
On Windows, supply the absolute Tesseract DLL path. Recognition runs locally; models
are never downloaded during conversion. Orientation correction, two-column prose order
and word/line confidence metadata are optional; confidence is not an accuracy guarantee.

See [OCR setup and tests](https://github.com/kirakosyan/word.net/blob/main/docs/testing.md#reproduce-locally),
[the prerequisite script](https://github.com/kirakosyan/word.net/blob/main/eng/prepare-ocr-tests.ps1)
and [OCR metadata](https://github.com/kirakosyan/word.net/blob/main/docs/interoperability.md#ocr-metadata).
Tesseract and language data are supplied by the host, not included in the NuGet package.

## Frameworks and platforms

The library targets `net462`, `net472`, `netstandard2.0`, `net8.0` and `net10.0` with matching public APIs.

| Target | Qualification and requirements |
| --- | --- |
| .NET 8 / .NET 10 | Native Windows/Linux x64 and ARM64 conversion/OCR source and package suites |
| .NET Framework 4.7.2+ | Windows x64; actual 4.7.2 requires the legacy JIT configuration below; 4.8.1 tested with default settings |
| .NET Framework 4.6.2 asset | Builds and executes on Framework 4.8.1; actual 4.6.2 execution remains unqualified |
| .NET Standard 2.0 | Portable fallback with explicit asset-consumption checks on modern .NET |
| Android / macOS | Deferred; no release support claim |

Applications running on **actual Framework 4.7.2 x64** must configure the legacy JIT, preserving existing binding redirects:

```xml
<configuration>
  <runtime>
    <useLegacyJit enabled="1" />
  </runtime>
</configuration>
```

The library cannot configure its host runtime. Compilation and package compatibility are separate from runtime qualification. See [minimum-runtime requirements](https://github.com/kirakosyan/word.net/blob/main/docs/minimum-runtime-qualification.md) and [build-specific test evidence](https://github.com/kirakosyan/word.net/blob/main/docs/testing.md).

## Streams and resource limits

Caller streams remain open. Reading and writing begin at their current positions; output is not truncated. Supply distinct streams and a fresh destination.

Output is staged inside the converter. Parsing, fidelity and admission-limit failures leave caller output untouched; cancellation or I/O errors during final copying can leave partial output. Opening a destination with `File.Create` already truncates it, which is why the quick start stages in memory first.

Defaults bound input to 16 MiB, output to 32 MiB, container expansion to 64 MiB, ZIP entries to 1,024, characters to four million, nesting to 64 and pages to 1,000. These are admission checks, not hard CPU/RSS limits. Async methods use async stream I/O; parsers, renderers and most native calls execute synchronously.

XML entities and DTDs are prohibited. Conversion does not execute scripts/macros or fetch remote resources. For untrusted workloads and hard deadlines, use [isolated hosting](https://github.com/kirakosyan/word.net/blob/main/docs/hosting.md).

## Build and test

Install the pinned SDK from [global.json](https://github.com/kirakosyan/word.net/blob/main/global.json), plus .NET 8/10 runtimes. Provision local OCR prerequisites in the same PowerShell process that launches tests:

```powershell
./eng/prepare-ocr-tests.ps1
dotnet restore wordnet.sln --locked-mode
dotnet build wordnet.sln -c Release --no-restore
dotnet test tests/wordnet.Conversion.Tests -c Release -f net8.0 --no-build --no-restore
dotnet test tests/wordnet.Conversion.Tests -c Release -f net10.0 --no-build --no-restore
dotnet format wordnet.sln --verify-no-changes --no-restore
./eng/pack.ps1
```

Linux requires Tesseract, fontconfig and the documented fonts. Run Framework tests only on a compatible Windows host. See [testing](https://github.com/kirakosyan/word.net/blob/main/docs/testing.md) for full prerequisites and platform limits, and the [comparison harness](https://github.com/kirakosyan/word.net/blob/main/docs/comparison.md) for 24 independently authored business fixtures and office-reference measurements.

## Documentation

| Guide | Contents |
| --- | --- |
| [Usage](https://github.com/kirakosyan/word.net/blob/main/docs/interoperability.md) | Conversion, OCR and configuration examples |
| [Editing and templates](https://github.com/kirakosyan/word.net/blob/main/docs/editing-and-templates.md) | Creation, editing, composition and merge controls |
| [Architecture](https://github.com/kirakosyan/word.net/blob/main/docs/architecture.md) | Engine boundaries, providers and resource contracts |
| [Diagnostics](https://github.com/kirakosyan/word.net/blob/main/docs/diagnostics.md) | Fidelity reports and preflight remediation |
| [Testing](https://github.com/kirakosyan/word.net/blob/main/docs/testing.md) | Build/test setup, independent validation and platform evidence |
| [Comparison harness](https://github.com/kirakosyan/word.net/blob/main/docs/comparison.md) | Reproducible corpus and optional licensed reference comparisons |
| [Current fidelity baseline](https://github.com/kirakosyan/word.net/blob/main/docs/fidelity-103-baseline.md) | Merged source/public-package evidence, retained failures and reduced reproductions |
| [Implementation plan](https://github.com/kirakosyan/word.net/blob/main/docs/implementation-plan.md) | Delivered work and remaining scope |
| [Release notes](https://github.com/kirakosyan/word.net/blob/main/docs/releases/1.4.0.md) | Published packages and exact-artifact evidence |
| [Release procedure](https://github.com/kirakosyan/word.net/blob/main/docs/releasing.md) | Package qualification and manual publishing |
| [Contributing](https://github.com/kirakosyan/word.net/blob/main/AGENTS.md) | Engineering and validation requirements |

## License and support

[MIT](https://github.com/kirakosyan/word.net/blob/main/LICENSE) · Copyright © 2026 Armen Kirakosyan.

Implementation uses independently authored code, published specifications and appropriately licensed dependencies. See [third-party and font notices](https://github.com/kirakosyan/word.net/blob/main/THIRD_PARTY_NOTICES.md).

Report bugs and feature requests through [GitHub Issues](https://github.com/kirakosyan/word.net/issues).
To support development, [donate on Patreon](https://www.patreon.com/16981794/join).
