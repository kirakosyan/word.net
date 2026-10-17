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

Specification-authored edge cases live directly in RtfTests/HtmlInputTests. Open XML
SDK independently schema-checks imported Word. Emitted RTF is separately read by
LibreOffice and its resulting editable structure inspected, avoiding reliance on
matching WordNet reader/writer bugs. See eng/verify-interoperability.py.
