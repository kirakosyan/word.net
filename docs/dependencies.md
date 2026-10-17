# Dependency maintenance

## Enterprise preview package boundary (2026-10-15)

NuGet deployment is the consumer requirement; GAC, merging and strong-name signing
were explicitly waived. Existing public signatures and unsigned identity remain;
AssemblyVersion is pinned to 1.0.0.0. Core API compatibility is checked against 1.0.0.

```text
wordnet (net462/net472/netstandard2.0/net8/net10)
  + wordnet.Fonts (managed OFL resources, netstandard2.0)
  + PdfPig 0.1.16 (managed font metrics/subsetting/PDF writer)
  + PdfPig.Filters.Dct.JpegLibrary 0.1.16.1 (managed JPEG decoder, MIT/Apache-2.0 notices)
  + Markdig / AngleSharp / OpenMcdf (managed format adapters)
  + Framework/portable compatibility packages (see committed lock files)

wordnet.Ocr (optional, same five targets)
  + wordnet
  + PdfPig.Rendering.Skia / SkiaSharp / HarfBuzz and explicit native assets
  + caller-provisioned Tesseract 5 and English traineddata
```

Referencing core does not restore Skia/HarfBuzz or an OCR provider. PNG admission and
validation use managed PdfPig; JPEG uses the already-reviewed JpegLibrary decoder;
GIF/BMP decoding and PNG re-encoding are independently authored. Only explicit
FontSource file/directory helpers read disk; ordinary byte/stream exports use embedded
resources and do not discover OS fonts, fetch URLs or write temporary files.

`wordnet.Fonts` embeds 20 unmodified Liberation 2.1.5, Carlito and Caladea faces with
their complete OFL notices and pinned source hashes. Original Noto faces stay in core.
See `THIRD_PARTY_NOTICES.md` and `src/wordnet.Fonts/Resources/provenance.json`.

BenchmarkDotNet 0.15.8 is MIT-licensed, supports .NET 10 and is confined to the
non-packable benchmark tool. Its dependencies do not enter converter packages.
[Upstream changelog](https://benchmarkdotnet.org/changelog/full.html) and
[license](https://github.com/dotnet/BenchmarkDotNet/blob/master/LICENSE.md).

The historical review below describes the 1.0.0 graph; native bindings now belong to
the optional import package. New runtime/native qualification must be reported by
platform rather than inferred from these historical results.

The 1.0.0 desktop release retains these runtime dependencies. On 2026-10-14, locked
restore and the solution's transitive vulnerability/deprecation reports complete with
no warnings or reported vulnerable/deprecated packages. Package/API validation passes
for all four targets. The detailed dependency selection below is the dated alpha.7 review.

Reviewed against nuget.org on **2026-10-13** for **0.1.0-alpha.7**. Use stable
releases that retain all four library targets: `net472`, `netstandard2.0`, `net8.0`
and `net10.0`. Versions are centralized in `Directory.Packages.props`; each project
has a reviewed lock file. Native assets must match their managed bindings.

## Changes in alpha.7

| Dependency | Previous | Current |
| --- | --- | --- |
| xUnit framework | Deprecated `xunit` 2.9.3 | `xunit.v3.mtp-off` 4.0.2 |
| xUnit Visual Studio adapter | 3.1.5 | 4.0.1 |
| Microsoft.NET.Test.Sdk | 17.14.1 | 18.10.1 |
| SkiaSharp / HarfBuzz integration / Skia native assets | 3.119.4 | 4.153.1 |
| HarfBuzzSharp / native assets | 8.3.1.5 | 14.2.1.301 |
| PDF JBIG2 decoder | 0.1.16.1 | 0.1.16.2 |
| PDF JPEG 2000 decoder | 0.1.16 | 0.1.16.1 |
| System.Text.Encoding.CodePages (legacy targets) | 8.0.0 | 10.0.12 |

Test-only compatibility dependencies are also brought up to current stable versions.
This removes the deprecated `System.Collections.Immutable` 1.5.0 dependency. Markdig,
AngleSharp, OpenMcdf, PdfPig, its renderer and Open XML SDK were already current stable
versions at the time of review. Framework reference assemblies 1.0.3 are also current;
a small or old-looking version number alone does not imply an obsolete package.

[xUnit's migration guide](https://xunit.net/docs/getting-started/v3/migration) retains
.NET Framework 4.7.2 and .NET 8 support. Its
[MTP-off package](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform)
keeps the existing VSTest/TRX workflow. Desktop tests are now executables; their
Framework runtime is explicitly `win-x64` so locked restore is consistent on Windows
and Linux. The Android app uses the same v3 attributes/assertions with its own entry
point and awaits v3 theory-data discovery. Tests pass the runner's cancellation token
where supported; explicit cancellation regressions keep their controlled tokens.

Central transitive pinning updates dependencies whose parents specify older minimums.
NuGet promotes pinned runtime dependencies into the package manifest so consumers
receive the versions that were tested. Test packages remain outside the library's
dependency groups. See [licenses and native notices](../THIRD_PARTY_NOTICES.md).

## Retained framework reference asset

The outdated report still lists `Microsoft.NETCore.Platforms` **1.1.0** under
`NETStandard.Library` **2.0.3** for the .NET Standard 2.0 build. This is part of the SDK's
implicit reference graph, not a direct converter dependency. Keep that graph intact;
the generated wordnet package does not declare either package as a runtime dependency.
Changing this asset solely to remove an age warning would not modernize the converter.

```sh
dotnet nuget why src/wordnet/wordnet.csproj Microsoft.NETCore.Platforms --framework netstandard2.0
```

## Repeat the audit

Run locked restore for the solution and the separate Android project, then run each
of these reports for both `wordnet.sln` and `tests/wordnet.Android.Compatibility`:

```sh
dotnet list wordnet.sln package --deprecated --include-transitive --no-restore
dotnet list wordnet.sln package --vulnerable --include-transitive --no-restore
dotnet list wordnet.sln package --outdated --include-transitive --no-restore
```

These commands report findings; a successful exit code alone is not a clean audit.
Review all target-framework sections. The current graph has no NuGet-reported
deprecated packages or known vulnerabilities. All direct references are current stable
versions. The framework reference asset above is the only remaining outdated item.

After upgrades, regenerate and review lock-file changes, repeat locked restore, build
all targets, run real conversion/OCR tests, verify formatting and pack/API compatibility.
Graphics upgrades require Windows/Linux/Android execution: SkiaSharp 4 includes
[behavior changes](https://mono.github.io/SkiaSharp/docs/releases/4.148.0.html), including
[Linux font fallback changes](https://mono.github.io/SkiaSharp/docs/releases/4.153.0.html).
The small fixture suite does not qualify every native image codec or document feature.
See [test evidence](testing.md) for the exact platforms and remaining limits.

## Bucket 4 provider decision

No runtime dependency was added. Safe HTML reuses pinned AngleSharp without browsing,
RTF and TrueType outline decoding are specification-authored, and optional shaping
uses existing HarfBuzzSharp/native 14.2.1.301 through wordnet.Ocr. PNG reuses SkiaSharp
4.153.1. Native state remains per-call; core has no provider types or mandatory native
load. Independent qualification tools are development-only and separately licensed.

Bucket 4's RTF reader directly uses the managed `System.Text.Encoding.CodePages`
provider. Core therefore declares the existing centrally pinned **10.0.12** package
on every target, rather than relying on PdfPig's legacy-target transitive dependency
or the older .NET 8 shared-framework assembly. This preserves the .NET Standard fallback's dependency
closure on .NET 8 as well as .NET 10. It is maintained with dotnet/runtime, uses MIT,
has no native assets, and retains all five library targets. No version was downgraded.
NuGet correctly prunes the redundant .NET 10 dependency because that shared framework
supplies assembly v10. The archive verifier requires it in legacy, Standard and .NET 8 groups; complete packed
conversion/OCR suites exercise both explicit Standard fallback runtimes.

## Bucket 5 dependency proposals

The [four ADRs](later-design-decisions.md) add no dependencies. Proposed ODT adapters,
review algorithms and chart/OMML model work start with managed BCL/existing adapters;
the encryption proposal uses BCL primitives plus existing unmodified MPL-2.0 OpenMcdf,
with its existing notices/source link preserved. No crypto primitives will be authored.
ODF validators, LibreOffice, Python/Java crypto readers and math fonts are prospective
independent qualification prerequisites with their own licenses, not runtime selections.
Any later library/font choice still needs maintenance, license, five-TFM and actual
platform qualification; these designs do not extend the current dependency audit.
