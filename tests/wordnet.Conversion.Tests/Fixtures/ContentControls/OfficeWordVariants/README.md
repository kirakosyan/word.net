# Additional Word-authored control and glossary parts

The XML parts derive from the public Open XML SDK
[`sdtContent.docx`](https://github.com/dotnet/Open-XML-SDK/blob/431ab05cf160248cc3885a4a766026d4f8243792/test/DocumentFormat.OpenXml.Tests.Assets/assets/TestDataStorage/v2FxTestFiles/wordprocessing/SDT/Sdt/sdtContent.docx)
at commit `431ab05cf160248cc3885a4a766026d4f8243792`. Its SHA-256 is
`926024b1393e5293c9736b562343ef3f357e8801956d6c0690fb2178603bf626` and its
application metadata identifies Microsoft Office Word. The source repository's
[MIT license](LICENSE) and [notice](NOTICE) are reproduced here.

`eng/extract-word-control-variants.py` verifies that source hash and extracts
seven control subtrees and six local glossary parts. Control subtrees are
reserialized without surrounding whitespace; glossary parts preserve content
with line endings normalized to LF. The original package, document properties
and other body content are not copied. Tests construct minimal OPC packages
around individual controls and validate the source and saved packages with the
Open XML SDK. These derivatives are not the original document or an interactive
Word execution.

The block and inline rich-text controls and the plain-text control reference the
local `DefaultPlaceholder_22675703` entry. The date-value control has no
placeholder reference. The original combo and dropdown controls reference
`DefaultPlaceholder_22675704`, which is absent from this file's glossary; their
unmodified content also references a character style outside the typed-control
profile. Tests keep those rejections visible, then remove only the run style to
isolate rejection of the unresolved local placeholder. The date-placeholder
control exercises the same unqualified character-style boundary. Independent
synthetic controls cover all supported control kinds separately; these derivatives
do not claim complete Office-authored coverage or Word rendering parity.

Regenerate after explicitly obtaining the pinned source:

```sh
python eng/extract-word-control-variants.py --input /path/to/sdtContent.docx \
  --output tests/wordnet.Conversion.Tests/Fixtures/ContentControls/OfficeWordVariants
```

The [manifest](manifest.json) records the extracted XML SHA-256 values.
