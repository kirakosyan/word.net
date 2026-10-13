# wordnet

**Word conversion for .NET, with explicit fidelity diagnostics and local English OCR.**

DOC / DOCX ↔ PDF · Markdown · Text · AZW3 · EPUB · MOBI\
DOC / DOCX → XPS

> **Early development — 0.1.0-alpha.8.** All 28 agreed directions have working
> converters and real acceptance tests. Scanned and mixed PDFs can produce editable
> Word text using local English OCR. Production fidelity and full platform qualification
> remain in progress; the profiles below are limited.

## Convert a document

The public namespace is **`WordNet`**. The assembly and provisional NuGet package ID are **wordnet**.
Build the local prerelease package; nothing is published automatically.
The examples below use C# top-level statements in a console application. Each example
is self-contained; add a reference to the local `wordnet` package or project first.

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
Ordinary Word-authored DOCX files also often need best effort: font families, paragraph
spacing and section/page geometry are outside the current model. Harmless document
properties and language/editor metadata produce informational diagnostics instead.
`CanConvert` describes format pairs, not support for every feature in a particular file.

## C# examples

### DOCX to PDF with limits and cancellation

The PDF writer retains supported font sizes, colors, table geometry and inline PNG/JPEG
images. It uses its own A4 layout; inspect diagnostics for features it cannot preserve.
The token below requests cancellation after 30 seconds. Cancellation is checked between
operations; it cannot interrupt a parser or native OCR call already in progress.

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
| DOCX | Editable paragraphs/headings, bold/italic, font sizes/RGB colors, alignment, safe links, decimal/bulleted lists, absolute table widths/cell padding and inline PNG/JPEG |
| DOC | Word 97 binary output and Word 97+ input; text/styles/alignment, font sizes/colors, editable lists/tables, absolute widths/padding and inline PNG/JPEG |
| TXT ↔ Word | UTF-8, Unicode, paragraphs, tabs and line breaks; explicit formatting-loss diagnostics |
| Markdown ↔ Word | CommonMark text, editable list import, headings/emphasis/code/links and aligned pipe tables; explicit losses for richer Word appearance |
| EPUB ↔ Word | Package/spine reading and EPUB 3 writing; XHTML structure/lists, supported inline font/color/table CSS and embedded PNG/JPEG dimensions |
| MOBI ↔ Word | Unencrypted UTF-8 MOBI 6; uncompressed/PalmDOC input and uncompressed output |
| AZW3 ↔ Word | Standalone unencrypted KF8; skeleton/fragment input and single-flow output |
| PDF ↔ Word | Selectable-text/color/image export, digital text and simple raster-image import, optional English OCR for scanned/mixed pages |
| XPS ← Word | XPS 2005 packages with positioned text and embedded fonts |

All direction tests inspect real content. Generated DOCX is independently schema
validated. PDF/ebook/XPS tests check content and containers. External DOC and Kindle
readers also recovered generated content in local interoperability checks.
See [test evidence and its limits](docs/testing.md).

Current limitations:

- DOC retains the [documented formatting/table subset](docs/doc-format.md). The shared
  [formatting/image profile](docs/document-fidelity.md) covers inline PNG/JPEG and simple
  decimal/bulleted lists. Floating/linked/vector images, cropping/effects, advanced list
  labels, headers/footers, notes, original sections and merged/nested tables remain outside it.
- PDF/XPS export uses basic A4 wrapping/pagination, embedded Noto Sans, headings,
  emphasis, run sizes/colors, paragraph/cell alignment, absolute table widths and padding.
  PDF also embeds inline PNG/JPEG at their requested dimensions; oversized content fails.
  Unsupported glyphs and complex shaping fail.
  Original Word pagination is not reproduced.
- PDF import reconstructs editable paragraphs and simple embedded raster images in geometric
  reading order. It does not reconstruct original tables/styles or arbitrary clipping and
  transforms. OCR reconstructs English printed text; handwriting is deferred.
- EPUB accepts supported absolute inline CSS and embedded PNG/JPEG. Broader CSS/media,
  HUFF/CDIC compression, hybrid Kindle containers and DRM remain outside the profile.
  TXT/Markdown/MOBI/AZW3/XPS output diagnoses image loss and retains available alternative
  text in best-effort mode. No remote resources load.
- Password protection, tracked changes/comments and editable charts/equations are
  deferred. Unsupported content must fail or produce loss diagnostics.
- Justified/distributed and direction-dependent paragraph alignment is outside the current
  profile. Markdown prose and plain text report alignment loss; Markdown tables retain
  one alignment per column and diagnose conflicting cell alignments.

## English OCR

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

On Windows supply the absolute DLL path. On Android package native ARM64 libraries
and copy English data into app-private storage; use the extracted native-library path.
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
The configured native library remains loaded for the process lifetime; each recognition
engine and its image/text buffers are released after use.

[The test setup script](eng/prepare-ocr-tests.ps1) provisions pinned prerequisites.
Tesseract and language data are supplied by the host, not included in the wordnet NuGet.

## Frameworks and platforms

The library retains **`net472;netstandard2.0;net8.0;net10.0`** with matching public APIs.

| Consumer | Qualification status / goal |
| --- | --- |
| .NET Framework 4.7.2+ | Local tests through installed Windows Framework and an x64 test host |
| .NET 8 / .NET 10 | All 236 tests pass on native Windows/Linux x64 and ARM64 CI lanes |
| .NET Standard 2.0 | Portable fallback; explicit asset consumption checked on modern .NET |
| .NET for Android | All 236 tests pass on a Samsung ARM64 device, including native English OCR and formatting/images |
| macOS | Deferred; no current release qualification claim |

Compilation and NuGet compatibility do not prove every platform works.
See [platform evidence](docs/testing.md) for exact runtime/architecture limits.
Oldest Framework/Android versions, additional native codecs, trimming/AOT and broader
device coverage still need qualification.

No installed Word, Office automation or remote service is required.
See [dependency and font notices](THIRD_PARTY_NOTICES.md).

## Streams, limits and diagnostics

- Caller streams stay open. Read/write starts at current positions without truncation.
  Supply distinct streams and a fresh destination.
- Output is staged. Parsing, strict-loss and admission-limit failures leave caller output
  untouched. Cancellation/I/O failures during final copying can leave partial output.
- Async methods use async stream I/O with cancellation checkpoints. Parser, renderer
  and native OCR calls are synchronous and cannot be interrupted mid-call.
- Defaults: 16 MiB input, 32 MiB output, 64 MiB container expansion, 1,024 ZIP entries,
  four million characters, nesting depth 64 and 1,000 pages. OCR defaults to 200 DPI
  and 16 million raster/aggregate OCR image pixels per page. Retained document images
  share a separate decoded-pixel budget of `MaximumExpandedBytes / 4` per conversion.
- These checks are not a hard CPU/RSS sandbox. Provider/native allocations can exceed
  admitted data sizes. XML character limits apply per part; DOC byte limits per stream.
- XML entities/DTDs are prohibited. No scripts/macros or remote resources execute/load.
  Retained links use HTTP, HTTPS or mailto. Diagnostics contain feature descriptions,
  not document text, and group repeated warnings.

## Build, test and pack

Install the SDK in [global.json](global.json) and .NET 8/10 runtimes.
Prepare OCR in the **same PowerShell process** used to launch tests:

```powershell
# Linux: install libtesseract5, libfontconfig1 and fonts first (see docs/testing.md).
./eng/prepare-ocr-tests.ps1
dotnet restore wordnet.sln --locked-mode
dotnet build wordnet.sln -c Release --no-restore
dotnet test tests/wordnet.Conversion.Tests -c Release -f net8.0 --no-build --no-restore
dotnet test tests/wordnet.Conversion.Tests -c Release -f net10.0 --no-build --no-restore
dotnet format wordnet.sln --verify-no-changes --no-restore
dotnet pack src/wordnet/wordnet.csproj -c Release --no-build --no-restore --output artifacts/packages
```

Run `net472` tests on Windows with a compatible host. On Windows ARM64, provision
`-Architecture x64` and add `-- RunConfiguration.TargetPlatform=x64` to the test command.
OCR tests fail with setup guidance when prerequisites are missing; none are skipped.

The separate Android device app requires the Android workload and an authorized device:

```powershell
./eng/prepare-android-tests.ps1
dotnet restore tests/wordnet.Android.Compatibility --locked-mode
dotnet build tests/wordnet.Android.Compatibility -c Release --no-restore -t:SignAndroidPackage
./eng/run-android-tests.ps1 -AdbPath /absolute/path/to/adb
```

Agents use `rtk` per [AGENTS.md](AGENTS.md); consumers and CI need not.
The package includes four assemblies, XML API docs, README, MIT license, dependency
notices and font license, plus a symbol package. Packing checks framework API compatibility.
The provisional ID is not a claim to reserve a NuGet name.

## Project guide

| Path | Purpose |
| --- | --- |
| `src/wordnet/` | API, semantic model, format adapters, page layout and OCR |
| `tests/wordnet.Conversion.Tests/` | 28 direction tests, four OCR cases and regressions |
| `tests/wordnet.Android.Compatibility/` | Android device app running the shared conversion/OCR/regression suite |
| [Architecture](docs/architecture.md) | Engine boundaries and resource contracts |
| [Formatting and images](docs/document-fidelity.md) | Supported appearance, tables, lists, image formats and explicit limits |
| [Implementation plan](docs/implementation-plan.md) | Remaining fidelity and qualification work |
| [Testing](docs/testing.md) | Reproducible checks and evidence limits |
| [Dependencies](docs/dependencies.md) | Package upgrades, audit commands and retained framework assets |
| [Contributing](AGENTS.md) | Quality rules |

Implementation uses independently authored code, public specifications and appropriately
licensed dependencies. Preserve notices and fixture provenance.

[MIT](LICENSE) · Copyright © 2026 Armen Kirakosyan.
