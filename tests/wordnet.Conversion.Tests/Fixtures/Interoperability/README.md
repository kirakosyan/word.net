# Interoperability fixture provenance

`editor.html` is independently authored synthetic web-editor content under the
repository MIT license. It contains Unicode, emphasis, a list, table and the existing
MIT color-grid PNG as data, without customer content or remote resources.

`editor.rtf` is produced from that HTML by LibreOfficeDev 26.8.0.0.alpha0,
build `2c87e51eeaa2b413ff4ae097b2705eea1995d8e5` on Linux x64. This is an independent
producer, not WordNet output. LibreOffice is a separate test/qualification tool,
not a runtime or redistributed dependency. Its generated metadata includes creation
times, so byte-identical regeneration is not assumed.

```sh
soffice -env:UserInstallation=file:///tmp/wordnet-rtf-producer --headless \
  --infilter='HTML (StarWriter)' --convert-to 'rtf:Rich Text Format' \
  --outdir tests/wordnet.Conversion.Tests/Fixtures/Interoperability \
  tests/wordnet.Conversion.Tests/Fixtures/Interoperability/editor.html
```

| File | SHA-256 |
| --- | --- |
| editor.html | `590d97c4e39501ad030feb03c838ae8fb83187b8d3f723e0ecd19ee3d608dbd5` |
| editor.rtf | `716d1b33332a62104175de7c5e3ee09d5964a7e3bb0456bb90b221925ceed745` |

Specification-authored edge cases live directly in RtfTests/HtmlInputTests and
InteroperabilityReviewTests (including Word-style repeated row definitions and
primary-raster/redundant-metafile picture containers). Open XML
SDK independently schema-checks imported Word. Emitted RTF is separately read by
LibreOffice and its resulting editable structure inspected, avoiding reliance on
matching WordNet reader/writer bugs. See eng/verify-interoperability.py.

## Arabic and digital PDF fixtures

`NotoSansArabic-Regular.ttf` is the static Noto Sans Arabic face, Copyright 2015–2020
Google LLC, SIL OFL 1.1 (see `Noto-Arabic-OFL.txt`), copied from Debian fonts-noto-core.
SHA-256: `504d7407d86875acf7d04dfaa0fd7524d0b8797723bc4aa18022f29db25b0b6e`.
It is test-only, not a production embedded font or implicit host-font dependency.
HarfBuzz 12.1.0 via uharfbuzz 0.53.0 independently supplies glyph/cluster/offset goldens;
FontTools supplies independent outlines, FriBidi supplies visual-order reference, and
MuPDF checks actual glyph masks, logical Unicode and word-wide search geometry.
The signed negative dot-coordinate and half-unit regression is specification-based.

`generate-pdf.py` independently authors ReportLab 4.4.9 digital PDFs with prose,
interleaved-paint two-column text, ruled/borderless tables, forms, rotation and a
transformed MIT color-grid illustration. `pdf-ground-truth.json` records their exact
SHA-256, producer and numeric baseline/width/image-frame ground truth. No customer
content or WordNet writer generated those inputs. Review additions independently author
first-line/hanging indents, mixed bold/italic prose, 32 lines of uniform prose, a leading
grid and an inline-fallback illustration between paragraphs. Their numeric ground truth
and hashes are recorded in the same manifest.
