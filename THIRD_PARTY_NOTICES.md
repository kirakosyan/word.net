# Third-party notices

wordnet's own code is distributed under the repository's MIT license. Dependency
packages retain their own licenses and notices. Versions and dependency hashes are
recorded in `Directory.Packages.props` and project lock files.

## Markdig 1.4.0 — runtime dependency

[Project](https://github.com/xoofx/markdig) · BSD-2-Clause ·
[License at the package source revision](https://github.com/xoofx/markdig/blob/56e9c238584a44a169f174c881855c049768634c/license.txt)

Used for CommonMark and pipe-table parsing. It has no native runtime assets. Framework
and .NET Standard consumers also receive Microsoft System.Memory and its supporting
packages through NuGet; retain the notices supplied by those packages when redistributing.

```text
Copyright (c) 2016-2026, Alexandre Mutel
All rights reserved.

Redistribution and use in source and binary forms, with or without modification
, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.
THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## Development dependencies

- [Open XML SDK 3.5.1](https://github.com/dotnet/Open-XML-SDK): MIT; independent DOCX
  reading and schema validation in tests only. It is not a wordnet runtime dependency.
- The test SDK, xUnit, test runner and Framework reference assemblies are development
  dependencies. They are private package references and are not shipped as library
  runtime dependencies.

## Additional runtime dependencies

These are unmodified NuGet dependencies, resolved separately by NuGet rather than
copied into wordnet source. Preserve their package licenses and native third-party
notices when distributing a bundled application. The lock files record exact hashes.

| Package | Version | License | Purpose / source |
| --- | --- | --- | --- |
| AngleSharp | 1.8.4 | MIT | [HTML parser](https://github.com/AngleSharp/AngleSharp); no network loader configured |
| OpenMcdf | 3.3.0 | MPL-2.0 | [Compound binary storage](https://github.com/openmcdf/openmcdf); unmodified library |
| PdfPig | 0.1.16 | Apache-2.0 | [PDF reading/writing and font metrics](https://github.com/UglyToad/PdfPig) |
| PdfPig.Rendering.Skia | 0.1.16.4 | Apache-2.0 | [PDF rasterization](https://github.com/BobLd/PdfPig.Rendering.Skia) |
| PdfPig.Filters.Dct.JpegLibrary | 0.1.16.1 | Apache-2.0 | [PDF JPEG decoding](https://github.com/BobLd/UglyToad.PdfPig.Filters.Dct.JpegLibrary) |
| PdfPig.Filters.Jbig2.PdfboxJbig2 | 0.1.16.2 | Apache-2.0 | [PDF JBIG2 decoding](https://github.com/BobLd/UglyToad.PdfPig.Filters.Jbig2.PdfboxJbig2) |
| PdfPig.Filters.Jpx.OpenJpeg | 0.1.16.1 | BSD-2-Clause | [PDF JPEG 2000 decoding](https://github.com/BobLd/UglyToad.PdfPig.Filters.Jpx.OpenJpeg) |
| SkiaSharp, SkiaSharp.HarfBuzz and native asset packages | 4.153.1 | MIT (bindings); bundled native notices also apply | [SkiaSharp](https://github.com/mono/SkiaSharp) |
| HarfBuzzSharp and native asset packages | 14.2.1.301 | MIT (bindings); bundled native notices also apply | [HarfBuzzSharp](https://github.com/mono/SkiaSharp) |

Microsoft.Bcl.HashCode and System.* compatibility dependencies use MIT licenses.
Framework reference assemblies and NETStandard.Library are build/reference assets.
For OpenMcdf source corresponding to the unmodified package, see its NuGet repository
revision and [upstream releases](https://github.com/openmcdf/openmcdf/releases).

## Embedded fonts

Noto Sans Regular, Bold, Italic and Bold Italic are embedded resources and are embedded
in generated fixed-page documents. Copyright 2018 The Noto Project Authors.
They are distributed unmodified under SIL Open Font License 1.1. The complete license
is in `src/wordnet/Resources/Fonts/OFL.txt` and `licenses/NotoSans-OFL.txt` in the NuGet
package. Source revision and file hashes are in the font directory's README.

The managed `wordnet.Fonts` resource package additionally distributes unmodified
Liberation Sans/Serif/Mono 2.1.5, Carlito and Caladea (regular/bold/italic/bold italic),
under SIL Open Font License 1.1. Full notices and exact download URLs/revisions/hashes
are in `src/wordnet.Fonts/Resources/*-OFL.txt` and `provenance.json`, and are included
in that NuGet package. Reserved font names are preserved. The Liberation authors
describe compatibility with Arial/Times New Roman/Courier New; Carlito is the Calibri
fallback and Caladea the Cambria fallback. Fallbacks are diagnosed and do not establish
Word pagination parity. No proprietary Microsoft font is redistributed.

## Optional local OCR and test prerequisites

- [Tesseract](https://github.com/tesseract-ocr/tesseract): Apache-2.0; loaded through its
  public C API from a caller-specified native library. Its supporting libraries retain
  their own licenses. No Tesseract binary is included in the wordnet NuGet.
- [tessdata_fast English model 4.1.0](https://github.com/tesseract-ocr/tessdata_fast/tree/4.1.0):
  Apache-2.0; provisioned separately. `eng/prepare-ocr-tests.ps1` pins its SHA-256.
- Windows test setup extracts native Tesseract/Leptonica binaries from
  [FieldCure.DocumentParsers.Ocr 1.2.0](https://www.nuget.org/packages/FieldCure.DocumentParsers.Ocr/1.2.0).
  This is a test prerequisite download, not a managed dependency or redistributed
  part of wordnet. Retain upstream notices if redistributing those binaries yourself.
- Fixture generators use ReportLab (BSD) and Pillow (HPND). External fixture validation
  used NPOI/HWPF (Apache-2.0) and KindleUnpack (GPL-3.0). These tools are not copied,
  linked or included in the library/package; their role is recorded in fixture provenance.

## Binary Word fixtures and Android validation

- Apache POI 5.4.1 (Apache-2.0) independently generates DOC input and validates exported
  formatting/tables. Its binary fixtures and full upstream LICENSE/NOTICE are under
  `tests/wordnet.Conversion.Tests/Fixtures/apache-poi/`. Generated template derivatives
  retain those notices. Neither the Java tools nor fixtures ship in the wordnet NuGet.
- The Tesseract test repository's `phototest.tif` and supplied text ground truth are
  Apache-2.0 test fixtures. Their full license, pinned source revision and hashes are in
  `tests/wordnet.Conversion.Tests/Fixtures/tesseract/`. A raster-only PDF derivative is
  used for OCR regression tests; neither it nor the source fixtures ship in NuGet.
- Android test setup extracts ARM64 native assets from
  [Tesseract4Android 4.9.0](https://github.com/adaptech-cz/Tesseract4Android/tree/4.9.0):
  Apache-2.0 wrapper/Tesseract 5.5.1, Leptonica 1.85.0 (BSD-2-Clause), libjpeg 9f
  (IJG license), and libpng 1.6.48 (libpng license). These assets are bundled only in
  the local validation APK. The AAR and English model downloads are SHA-256 pinned.
  Preserve upstream notices when redistributing any validation APK/native assets.
- Linux rasterization uses the fontconfig-enabled Skia native package. Hosts must
  provide `libfontconfig1` and usable fonts for PDF font fallback; tests register the
  repository's pinned Noto Sans fonts. This does not change the font license above.
