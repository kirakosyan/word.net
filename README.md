# wordnet

[![NuGet version](https://img.shields.io/nuget/v/wordnet?style=flat-square&logo=nuget&logoColor=white&label=NuGet&color=2563eb)](https://www.nuget.org/packages/wordnet)
[![NuGet downloads](https://img.shields.io/nuget/dt/wordnet?style=flat-square&logo=nuget&logoColor=white&label=downloads&color=2563eb)](https://www.nuget.org/packages/wordnet)
[![Build and tests](https://img.shields.io/github/actions/workflow/status/kirakosyan/word.net/ci.yml?branch=main&style=flat-square&logo=github&label=build)](https://github.com/kirakosyan/word.net/actions/workflows/ci.yml)
[![MIT license](https://img.shields.io/badge/license-MIT-64748b?style=flat-square)](https://github.com/kirakosyan/word.net/blob/main/LICENSE)

[![Windows x64 and ARM64](https://img.shields.io/badge/Windows-x64%20%7C%20ARM64-16803d?style=flat-square&logo=data%3Aimage%2Fsvg%2Bxml%3Bbase64%2CPHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyNCAyNCI%2BPHBhdGggZmlsbD0id2hpdGUiIGQ9Ik0wIDBoMTF2MTFIMHptMTMgMGgxMXYxMUgxM3pNMCAxM2gxMXYxMUgwem0xMyAwaDExdjExSDEzeiIvPjwvc3ZnPg%3D%3D)](#frameworks-and-platforms)
[![Linux x64 and ARM64](https://img.shields.io/badge/Linux-x64%20%7C%20ARM64-16803d?style=flat-square&logo=linux&logoColor=white)](#frameworks-and-platforms)

**Word conversion and document automation for .NET, with explicit fidelity diagnostics and local English OCR.**

DOC / DOCX ↔ PDF · Markdown · Text · AZW3 · EPUB · MOBI · HTML · RTF\
DOC / DOCX → XPS

> **Business-document automation and interoperability.** Adds bounded
> layout fidelity, editing/composition/templates, safe HTML/RTF exchange, optional
> Arabic typography, semantic PDF/UA, previews and richer PDF/OCR import metadata.
> Publication and qualification status are in the [release ledger](docs/release-1.2.0-readiness.md).
> PDF input/OCR, PNG previews and native typography require matching `wordnet.Ocr`.
> See the
> [enterprise audit](https://github.com/kirakosyan/word.net/blob/main/docs/enterprise-audit.md) for the issue #4 acceptance profile;
> Word page counts match the 36 authored business fixtures; broader layout fidelity
> and actual .NET 4.6.2 runtime qualification remain unqualified.

The 1.2 release series contains the comparison/preflight, business rendering and editing/template
work from PRs #33–#35, plus bucket 4 merged in [#37](https://github.com/kirakosyan/word.net/pull/37)
as `44c9e362`. Buckets 1–4 are complete within their documented profiles. Bucket 4 adds
safe HTML/RTF exchange, optional Arabic typography, semantic PDF/UA, selected pages/previews,
digital PDF reconstruction and OCR metadata. All 15 review findings are resolved; all
20 CI checks passed on reviewed head `2af6a43`, whose tree matches the merge. The suite
requires 829 conversion/OCR cases, including the GC/font-lifetime regression merged in
PR #38. See [profiles and usage](docs/interoperability.md),
[build-specific evidence](docs/testing.md) and the [implementation plan](docs/implementation-plan.md).
All three 1.2.0 packages are publicly available. Their 11 DLLs match the qualified
CI artifact from release commit `3202a1f`; see the [publication evidence](docs/release-1.2.0-readiness.md).
Bucket 5's [four design deliverables](docs/later-design-decisions.md) merged in PR #39;
ODT, review-history editing/comparison, encryption and chart/equation implementation
remain deferred. These proposals add no format support or executable APIs.

The release's exact-main CI passed all 12 checks. Automated Docker/KVM guests ran
its source and immutable packages on actual Framework 4.7.2 x64 with legacy JIT:
each passed 829/829 and verified removal of its containers, network, VM files and
newly pulled image. Publication added nine complete 829-case suites across native
Windows/Linux x64/ARM64. See the [release evidence](docs/release-1.2.0-readiness.md).

## Convert a document

For the 1.2.0 document creation, editing, composition and mail-merge API, see the
[editing and templates guide](https://github.com/kirakosyan/word.net/blob/main/docs/editing-and-templates.md)
and the standalone `examples/WordNet.Automation` sample. This adds a bounded
`WordDocument` session API alongside the existing converter.

The public namespace is **`WordNet`**. The assembly and NuGet package ID are **wordnet**.

```sh
dotnet add package wordnet
```

The three release packages share one version: `wordnet.Fonts`, `wordnet`, and
optional `wordnet.Ocr`. Local packages are produced by `eng/pack.ps1`; public releases
use the explicit publishing workflow. Upgrading from 1.0.0 requires adding
`wordnet.Ocr` for PDF input and OCR. Validate representative deployment documents against the
[implemented profiles](#implemented-profiles) and inspect diagnostics. Stable versioning
covers this documented subset; it does not promise complete Microsoft Word fidelity.
Local packaging is also supported. Publication requires an explicit maintainer-run
[release workflow](https://github.com/kirakosyan/word.net/blob/main/docs/releasing.md); ordinary pushes never publish packages.
The examples below use C# top-level statements in a console application. Each example
is self-contained; add a reference to the `wordnet` package or project first.

```csharp
using System;
using System.IO;
using WordNet;

var converter = new WordConverter();
using var source = File.OpenRead("input.docx");
using var destination = File.Create("output.epub");

ConversionResult result = await converter.ConvertAsync(
    source, destination,
    DocumentFormat.Docx, DocumentFormat.Epub,
    new ConversionOptions(FidelityMode.BestEffort));

foreach (ConversionDiagnostic diagnostic in result.Diagnostics)
    Console.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
```

Starting with alpha.6, replace `using wordnet;` with `using WordNet;` and update any
fully qualified type names. This is a source and binary API change; rebuild consumers
against the new package. Package references continue to use `wordnet`.

Strict mode is the default: detected losses throw before output is copied.
Best-effort mode converts supported content and returns warnings. DOC and PDF import
always report formatting/reconstruction losses and therefore require best-effort mode.
Ordinary Word-authored DOCX files often need best effort for font substitution,
advanced typography and unsupported section types. Harmless document
properties and language/editor metadata produce informational diagnostics instead.
`CanConvert` describes format pairs, not support for every feature in a particular file.
Use `WordConverter.Inspect` / `InspectAsync` for bounded preflight with source locations,
font mappings and optional PDF/XPS layout; see the [remediation report example](https://github.com/kirakosyan/word.net/blob/main/docs/diagnostics.md#inspect-before-conversion).
The [reproducible comparison harness](https://github.com/kirakosyan/word.net/blob/main/docs/comparison.md) provides twelve synthetic
business documents and optional caller-provisioned Word/Aspose references.

## C# examples

### Managed PDF and self-contained HTML from DOCX bytes

Referencing `wordnet` brings its managed dependencies and `wordnet.Fonts`. DOCX export
does not require Office, installed fonts, Skia, Tesseract, disk writes or network access.
NuGet deployment is supported; assemblies are unsigned and no GAC variant is supplied.

```csharp
using System;
using WordNet;

byte[] inputDocx = System.IO.File.ReadAllBytes("input.docx"); // Or your template-merge bytes.
var converter = new WordConverter();
var options = new ConversionOptions(FidelityMode.Lenient)
{
    Clock = () => DateTimeOffset.UtcNow,
    Html = new HtmlOptions { MaxContentWidthPx = 700, IncludeHeadersAndFooters = false }
};
byte[] pdf = converter.ConvertToPdf(inputDocx, out ConversionResult pdfResult, options);
byte[] html = converter.ConvertToHtml(inputDocx, out ConversionResult htmlResult, options);
foreach (ConversionResult result in new[] { pdfResult, htmlResult })
{
    foreach (ConversionDiagnostic diagnostic in result.Diagnostics)
        Console.WriteLine($"{diagnostic.Code} ({diagnostic.Count}): {diagnostic.Message}");
    Console.WriteLine($"Wrote {result.BytesWritten} bytes; pages: {result.Metrics.PageCount}");
}
```

`FontSource.FromStream` reads named static TrueType/CFF OpenType faces and TTC collections;
`FontSource.FromDirectory` reads an explicitly selected directory. Add the returned
faces to `options.Fonts.Sources`. Legacy kern and GPOS pair adjustments are supported.
Variable/color fonts and general complex shaping remain outside the default profile;
the 1.2.0 [Arabic opt-in](docs/interoperability.md#optional-arabic-typography) has a separate bounded contract.
Configure options before sharing them between calls.
Use the `out ConversionResult` overloads or the stream API to inspect grouped
diagnostics and conversion metrics. The byte-only overloads intentionally return
only the output bytes. Catalog fonts select the closest regular (400) or bold (700)
weight, so filename ordering does not let Light/Black variants replace Regular.

HTML is reflowable: page fields retain cached text with
`Html.PageFieldsUsedCachedResult`, without running PDF pagination or font resolution.
When headers/footers are enabled, only the variants applicable to the first page
of the first section appear; even/default/later-section stories are not stacked.

`options.Pdf` controls content/font stream compression, optional heading outlines and
`PdfConformance.PdfA2b` (default: ordinary PDF 1.7). Archival corpus outputs are checked
with veraPDF; this default profile is untagged. The 1.2.0 [accessibility opt-in](docs/interoperability.md#semantic-pdf-and-pdfua-1) is separate.

### DOCX to PDF with limits and cancellation

The PDF writer retains supported font sizes, colors, table geometry and inline PNG/JPEG
images. The renderer uses retained section geometry with an A4 fallback;
inspect diagnostics for features it cannot preserve.
The token below requests cancellation after 30 seconds. Cancellation is checked between
operations and through Tesseract's recognition monitor. Other native/provider calls may
not cooperate promptly; hard deadlines require an isolated worker.

```csharp
using System;
using System.IO;
using System.Threading;
using WordNet;

var converter = new WordConverter();
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
using var source = File.OpenRead("input.docx");
using var destination = File.Create("output.pdf");

var options = new ConversionOptions(
    FidelityMode.BestEffort,
    maximumInputBytes: 16 * 1024 * 1024,
    maximumOutputBytes: 32 * 1024 * 1024,
    maximumPages: 100);

ConversionResult result = await converter.ConvertAsync(
    source, destination, DocumentFormat.Docx, DocumentFormat.Pdf,
    options, cancellation.Token);

Console.WriteLine($"Wrote {result.BytesWritten:N0} bytes.");
foreach (ConversionDiagnostic diagnostic in result.Diagnostics)
    Console.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
```

### Legacy DOC to DOCX

DOC import requires best-effort mode because the supported profile cannot preserve
every binary Word feature. Reverse the formats and file extensions to export DOC.

```csharp
using System;
using System.IO;
using WordNet;

var converter = new WordConverter();
using var source = File.OpenRead("legacy.doc");
using var destination = File.Create("modern.docx");

ConversionResult result = converter.Convert(
    source, destination, DocumentFormat.Doc, DocumentFormat.Docx,
    new ConversionOptions(FidelityMode.BestEffort));

foreach (ConversionDiagnostic diagnostic in result.Diagnostics)
    Console.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
```

### Markdown string to Word in memory

This example produces a real DOCX containing a heading, bold text and an editable
numbered list. The converter leaves both caller-owned streams open.

```csharp
using System;
using System.IO;
using System.Text;
using WordNet;

string markdown = "# Release notes\n\nBuilt with **wordnet**.\n\n1. Format text\n2. Embed images\n";
using var source = new MemoryStream(Encoding.UTF8.GetBytes(markdown));
using var destination = new MemoryStream();

ConversionResult result = new WordConverter().Convert(
    source, destination, DocumentFormat.Markdown, DocumentFormat.Docx,
    new ConversionOptions()); // Strict mode: fail on detected unsupported features.

byte[] docx = destination.ToArray();
File.WriteAllBytes("release-notes.docx", docx);
Console.WriteLine($"Created {result.BytesWritten:N0} bytes of DOCX content.");
```

For a UTF-8 plain-text string, use `DocumentFormat.Text` instead of `Markdown`.
When passing the output stream to another conversion, rewind it with
`destination.Position = 0` first.

### Reject detected losses before replacing a file

Strict mode throws `NotSupportedException` when it encounters unsupported features.
For example, exporting styled Word content to plain text loses formatting. Converting
into memory first keeps an existing destination file unchanged if conversion fails;
opening that file with `File.Create` beforehand would already truncate it.

```csharp
using System;
using System.IO;
using WordNet;

using var source = File.OpenRead("input.docx");
using var staged = new MemoryStream();

try
{
    new WordConverter().Convert(
        source, staged, DocumentFormat.Docx, DocumentFormat.Text,
        new ConversionOptions(FidelityMode.Strict));

    File.WriteAllBytes("output.txt", staged.ToArray());
}
catch (NotSupportedException error)
{
    Console.WriteLine($"Conversion rejected: {error.Message}");
}
```

Use `FidelityMode.BestEffort` when an approximation is acceptable and inspect
`ConversionResult.Diagnostics`. Malformed input, resource limits and I/O failures can
still throw; best effort does not turn every failure into a warning.

The file examples also work with the supported EPUB/MOBI/AZW3/Markdown/text exchanges
by changing the explicit format arguments and file paths. At least one side must be
`DocumentFormat.Doc` or `DocumentFormat.Docx`; XPS is an output format only.

## Implemented profiles

Each bidirectional row exchanges with **both DOC and DOCX**. DOC ↔ DOCX is included.
Word means DOC and DOCX; Open XML means DOCX. XPS is output-only.

| Format | Current implementation |
| --- | --- |
| DOCX | Editable paragraphs/headings, bold/italic, font sizes/RGB colors, alignment, safe links, richer editable lists, nested/decorated tables, notes, section/column settings and supported raster drawings |
| DOC | Word 97 binary output and Word 97+ input; text/styles/alignment, font sizes/colors, editable lists/tables, absolute widths/padding and inline PNG/JPEG |
| TXT ↔ Word | UTF-8, Unicode, paragraphs, tabs and line breaks; explicit formatting-loss diagnostics |
| Markdown ↔ Word | CommonMark text, editable list import, headings/emphasis/code/links and aligned pipe tables; explicit losses for richer Word appearance |
| EPUB ↔ Word | Package/spine reading and EPUB 3 writing; XHTML structure/lists, supported inline font/color/table CSS and embedded PNG/JPEG dimensions |
| MOBI ↔ Word | Unencrypted UTF-8 MOBI 6; uncompressed/PalmDOC input and uncompressed output |
| AZW3 ↔ Word | Standalone unencrypted KF8; skeleton/fragment input and single-flow output |
| PDF ↔ Word | Selectable-text/color/image export, digital text and simple raster-image import, optional English OCR for scanned/mixed pages |
| XPS ← Word | XPS 2005 packages with positioned text, embedded fonts/raster images and the shared fixed-page table/section profile |
| HTML ↔ Word (1.2.0 profile) | UTF-8 HTML5, inline CSS, nested lists/tables and merged cells, embedded raster images, safe links, run decoration and optional header/footer blocks; bounded safe CSS/data/caller-image import |
| RTF ↔ Word (1.2.0 profile) | Bounded Unicode/code-page text, emphasis/paragraphs, basic lists/tables and embedded PNG/JPEG |

All direction tests inspect real content. Generated DOCX is independently schema
validated. PDF/ebook/XPS tests check content and containers. External DOC and Kindle
readers also recovered generated content in local interoperability checks.
See [test evidence and its limits](https://github.com/kirakosyan/word.net/blob/main/docs/testing.md).

Current limitations:

- DOC retains the [documented formatting/table subset](https://github.com/kirakosyan/word.net/blob/main/docs/doc-format.md). The shared
  [formatting/image profile](https://github.com/kirakosyan/word.net/blob/main/docs/document-fidelity.md) covers inline PNG/JPEG and simple
  decimal/bulleted lists. Linked/vector images, arbitrary drawing effects, unequal columns and complete
  Word pagination parity remain outside it. Header/footer stories
  and fields survive DOCX/DOC exchange. DOCX preserves editable notes, richer numbering and drawings; PDF/XPS render the
  bounded business profiles described in the fidelity guide. DOC retains its existing
  subset with explicit losses.
  Conversion preserves next-page section geometry, paragraph spacing/indents,
  multiple cell paragraphs and horizontal/vertical merges through Word and EPUB.
- PDF/XPS export uses section-aware wrapping/pagination, selected embedded font subsets, headings,
  emphasis, run sizes/colors, paragraph/cell alignment, absolute table widths and padding.
  PDF also embeds PNG/JPEG; GIF/BMP become PNG. Supported anchors use rectangular wrapping and positioned frames.
  Strict mode rejects missing glyphs; other modes use visible fallback characters.
  The authored business corpus meets the recorded Word page-count tolerance;
  exact line breaks and arbitrary template parity are not guaranteed.
  Long ordinary table rows split between lines and repeat contiguous headers. Oversized
  non-splitting rows and vertically merged groups fail explicitly in Strict/BestEffort;
  Lenient splits them and scales oversized images/text, constrains indentation and
  reserves/scales oversized stories. Malformed inputs, limits, cancellation and I/O
  still fail; see the enterprise audit for the diagnosed fidelity approximations.
- PDF import reconstructs editable paragraphs and simple embedded raster images in geometric
  reading order. It does not reconstruct original tables/styles or arbitrary clipping and
  transforms. OCR reconstructs English printed text; handwriting is deferred.
- EPUB accepts supported absolute inline CSS and embedded PNG/JPEG. Broader CSS/media,
  HUFF/CDIC compression, hybrid Kindle containers and DRM remain outside the profile.
  TXT/Markdown/MOBI/AZW3 output diagnoses image loss and retains available alternative
  text in best-effort mode. No remote resources load.
- Password protection and editable charts/equations are deferred. DOCX revisions render
  their accepted state with a `Revisions.Accepted` warning in BestEffort/Lenient;
  comments produce `Comments.Ignored`. Strict rejects both losses before output is copied.
- Fixed-page export supports justified/distributed alignment and aligned tab stops.
  Direction-dependent paragraph alignment remains outside the profile. Markdown prose and plain text report alignment loss; Markdown tables retain
  one alignment per column and diagnose conflicting cell alignments.

## English OCR

Add `wordnet.Ocr` at the same package version for **all PDF input**, including digital
PDF import. Its dependencies carry the optional native renderer; the core export
package has no native dependencies. Existing conversion APIs and OCR options remain
in `WordNet`. Missing the provider produces a setup error before output is copied.

Provide a local **Tesseract 5 C API library**, its native dependencies, and a directory
containing `eng.traineddata`. The architecture must match the application process.

```csharp
using System;
using System.IO;
using WordNet;

var converter = new WordConverter();
var options = new ConversionOptions(
    FidelityMode.BestEffort,
    englishOcr: new EnglishOcrOptions(
        dataDirectory: "/absolute/path/to/tessdata",
        nativeLibraryPath: "/absolute/path/to/libtesseract.so.5"));

using var scanned = File.OpenRead("scan.pdf");
using var word = File.Create("editable.docx");
ConversionResult result = converter.Convert(
    scanned, word, DocumentFormat.Pdf, DocumentFormat.Docx, options);

foreach (ConversionDiagnostic diagnostic in result.Diagnostics)
    Console.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
```

On Windows supply the absolute DLL path.
Linux OCR requires `libtesseract5`, `libfontconfig1` and usable system fonts for PDF
fallback. The validation container registers the repository's pinned Noto Sans fonts.
PdfPig/SkiaSharp rasterize pages. OCR runs locally and never downloads models during
conversion. Pages identified as scans (large page images), and image-only pages, are
recognized once as whole pages when OCR is configured, avoiding duplicated digital overlays.
Ordinary illustrations alongside digital text are retained as images unless the large-image
heuristic selects whole-page OCR. `Pdf.OcrImageLoss` reports discarded page images;
`Pdf.OcrTextReplacement` reports replacement of an existing digital text layer.
Confidence is the arithmetic mean of each OCR page's recognized-word confidence,
including blank pages, with the page count reported. It is not an accuracy guarantee.
Use `new EnglishOcrOptions(dataDirectory, nativeLibraryPath,
correctOrientation: true)` for optional right-angle rotation and bounded deskew. It costs
additional OCR passes, reusing one engine per page. Column-first reading order is a
separate opt-in: `new EnglishOcrOptions(dataDirectory, nativeLibraryPath,
correctOrientation: false, readTextColumns: true)`. Use it only for known two-column
prose; it can separate labels from values in forms and tables. The default preserves
Tesseract's line and paragraph order. Complex page layouts and reconstructed tables
remain outside the OCR profile.
The configured native library remains loaded for the process lifetime; each recognition
engine and its image/text buffers are released after use.

[The test setup script](https://github.com/kirakosyan/word.net/blob/main/eng/prepare-ocr-tests.ps1) provisions pinned prerequisites.
Tesseract and language data are supplied by the host, not included in the wordnet NuGet.

## Frameworks and platforms

The library targets **`net462;net472;netstandard2.0;net8.0;net10.0`** with matching public APIs.
The new net462 asset builds and runs on this machine's Framework 4.8.1; actual 4.6.2
execution remains unqualified. Earlier release platform evidence below applies to 1.0.0.

| Consumer | Release profile |
| --- | --- |
| .NET Framework 4.7.2+ | Windows x64; actual 4.7.2 requires the legacy JIT setting below. Framework 4.8.1 is tested with default settings |
| .NET 8 / .NET 10 | Native Windows/Linux x64 and ARM64, complete source and exact-package conversion/OCR suites |
| .NET Standard 2.0 | Portable fallback; explicit asset consumption checked on modern .NET |
| .NET for Android | Deferred; no support claim in 1.0.0 |
| macOS | Deferred; no support claim in 1.0.0 |

Compilation and NuGet compatibility do not prove every platform works.
See [platform evidence](https://github.com/kirakosyan/word.net/blob/main/docs/testing.md) for exact runtime/architecture limits.
The tested desktop operating systems are Windows Server 2019 (minimum-Framework profile),
Windows Server 2022, Windows 11 and Ubuntu 24.04. Other OS versions, additional native
codecs and trimming/AOT need separate qualification.

The current Framework minimum-runtime compatibility configuration is explicit: actual
Framework 4.7.2 x64 requires `<useLegacyJit enabled="1" />` under the application's
`<runtime>` configuration. Default JIT operation on actual 4.7.2 is unsupported for this
release because of the reproduced runtime code-generation defect. Preserve existing
assembly binding redirects when adding the setting:

```xml
<configuration>
  <runtime>
    <useLegacyJit enabled="1" />
  </runtime>
</configuration>
```

See the [configuration and evidence](https://github.com/kirakosyan/word.net/blob/main/docs/minimum-runtime-qualification.md). The library
cannot configure its host runtime. Android API 21 remains unresolved and outside this release.

No installed Word, Office automation or remote service is required.
See [dependency and font notices](https://github.com/kirakosyan/word.net/blob/main/THIRD_PARTY_NOTICES.md).

## Streams, limits and diagnostics

- Caller streams stay open. Read/write starts at current positions without truncation.
  Supply distinct streams and a fresh destination.
- Output is staged. Parsing, strict-loss and admission-limit failures leave caller output
  untouched. Cancellation/I/O failures during final copying can leave partial output.
- Async methods use async stream I/O with cancellation checkpoints. Parser, renderer
  and native OCR calls are synchronous. Tesseract recognition receives cancellation through
  its progress monitor; other provider operations still require external deadlines.
- Defaults: 16 MiB input, 32 MiB output, 64 MiB container expansion, 1,024 ZIP entries,
  four million characters, nesting depth 64 and 1,000 pages. OCR defaults to 200 DPI
  and 16 million raster/aggregate OCR image pixels per page. Retained document images
  share a separate decoded-pixel budget of `MaximumExpandedBytes / 4` per conversion.
- These checks are not a hard CPU/RSS sandbox. Provider/native allocations can exceed
  admitted data sizes. XML character limits apply per part; DOC byte limits per stream.
  See [isolated hosting and resource campaigns](https://github.com/kirakosyan/word.net/blob/main/docs/hosting.md) and the [API policy](https://github.com/kirakosyan/word.net/blob/main/docs/api-compatibility.md).
- XML entities/DTDs are prohibited. No scripts/macros or remote resources execute/load.
  Retained links use HTTP, HTTPS or mailto. Diagnostics contain feature descriptions,
  not document text, and group repeated warnings.

## Build, test and pack

Install the SDK in [global.json](https://github.com/kirakosyan/word.net/blob/main/global.json) and .NET 8/10 runtimes.
Prepare OCR in the **same PowerShell process** used to launch tests:

```powershell
# Linux: install libtesseract5, libfontconfig1 and fonts first (see docs/testing.md).
./eng/prepare-ocr-tests.ps1
dotnet restore wordnet.sln --locked-mode
dotnet build wordnet.sln -c Release --no-restore
dotnet test tests/wordnet.Conversion.Tests -c Release -f net8.0 --no-build --no-restore
dotnet test tests/wordnet.Conversion.Tests -c Release -f net10.0 --no-build --no-restore
dotnet format wordnet.sln --verify-no-changes --no-restore
./eng/pack.ps1
```

Run `net472` tests on Windows with a compatible host. On Windows ARM64, provision
`-Architecture x64` and add `-- RunConfiguration.TargetPlatform=x64` to the test command.
OCR tests fail with setup guidance when prerequisites are missing; none are skipped.

The deferred Android device harness remains available for development; it requires the
Android workload and an authorized device:

```powershell
./eng/prepare-android-tests.ps1
dotnet restore tests/wordnet.Android.Compatibility --locked-mode
dotnet build tests/wordnet.Android.Compatibility -c Release --no-restore -t:SignAndroidPackage
./eng/run-android-tests.ps1 -AdbPath /absolute/path/to/adb
```

Agents use `rtk` per [AGENTS.md](https://github.com/kirakosyan/word.net/blob/main/AGENTS.md); consumers and CI need not.
The core package includes five framework assets, XML API docs, README, MIT license, dependency
notices and font license, plus a symbol package. Packing checks framework API compatibility.
See [release checks and publishing](https://github.com/kirakosyan/word.net/blob/main/docs/releasing.md) and the
[1.2.0 release notes](docs/releases/1.2.0.md) and [qualification/publication status](docs/release-1.2.0-readiness.md).
Historical [1.1.0 notes](docs/releases/1.1.0.md) retain their original scope.

## Project guide

| Path | Purpose |
| --- | --- |
| `src/wordnet/` | API, semantic model, managed format adapters and page layout |
| `src/wordnet.Fonts/` | Managed embedded Liberation/Carlito/Caladea font resources and OFL notices |
| `src/wordnet.Ocr/` | Optional PDF import and native English OCR providers |
| `tests/wordnet.Conversion.Tests/` | 36 directions, English OCR and regressions; 829 required cases |
| `tests/wordnet.Android.Compatibility/` | Android device app running the shared conversion/OCR/regression suite |
| `tools/wordnet.Qualification/` | Isolated malformed-input, concurrent conversion and OCR resource campaigns |
| [Architecture](https://github.com/kirakosyan/word.net/blob/main/docs/architecture.md) | Engine boundaries and resource contracts |
| [Formatting and images](https://github.com/kirakosyan/word.net/blob/main/docs/document-fidelity.md) | Supported appearance, tables, lists, image formats and explicit limits |
| [Implementation plan](https://github.com/kirakosyan/word.net/blob/main/docs/implementation-plan.md) | Remaining fidelity and qualification work |
| [Testing](https://github.com/kirakosyan/word.net/blob/main/docs/testing.md) | Reproducible checks and evidence limits |
| [Dependencies](https://github.com/kirakosyan/word.net/blob/main/docs/dependencies.md) | Package upgrades, audit commands and retained framework assets |
| [Releasing](https://github.com/kirakosyan/word.net/blob/main/docs/releasing.md) | Production gates and manual NuGet Trusted Publishing |
| [Contributing](https://github.com/kirakosyan/word.net/blob/main/AGENTS.md) | Quality rules |

Implementation uses independently authored code, public specifications and appropriately
licensed dependencies. Preserve notices and fixture provenance.

[MIT](https://github.com/kirakosyan/word.net/blob/main/LICENSE) · Copyright © 2026 Armen Kirakosyan.
