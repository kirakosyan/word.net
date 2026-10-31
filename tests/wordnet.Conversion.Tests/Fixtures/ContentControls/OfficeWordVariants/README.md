# Additional Word-authored control and glossary parts

The XML parts derive from the public Open XML SDK
[`sdtContent.docx`](https://github.com/dotnet/Open-XML-SDK/blob/431ab05cf160248cc3885a4a766026d4f8243792/test/DocumentFormat.OpenXml.Tests.Assets/assets/TestDataStorage/v2FxTestFiles/wordprocessing/SDT/Sdt/sdtContent.docx)
at commit `431ab05cf160248cc3885a4a766026d4f8243792`. Its SHA-256 is
`926024b1393e5293c9736b562343ef3f357e8801956d6c0690fb2178603bf626` and its
application metadata identifies Microsoft Office Word. The source repository's
[MIT license](LICENSE) and [notice](NOTICE) are reproduced here.

`eng/extract-word-control-variants.py` verifies that source hash and extracts
seven control subtrees, two main-document character styles and six local glossary
parts. Control and selected style subtrees are reserialized without surrounding
whitespace; glossary parts preserve content with line endings normalized to LF.
The original package, document properties
and other body content are not copied. Tests construct minimal OPC packages
around individual controls and validate the source and saved packages with the
Open XML SDK. These derivatives are not the original document or an interactive
Word execution.

The block and inline rich-text controls and the plain-text control reference the
local `DefaultPlaceholder_22675703` entry. The date-value control has no
placeholder reference. The original combo and dropdown controls reference
`DefaultPlaceholder_22675704`, which is absent from this file's glossary; tests
keep that rejection visible both with and without their character-style reference.
The date-placeholder control uses the extracted `PlaceholderText` style and is
admitted in Lenient mode with an explicit loss for materializing the style. Strict
mode still rejects that loss. Independent
synthetic controls cover all supported control kinds separately; these derivatives
do not claim complete Office-authored coverage or Word rendering parity.

For the date placeholder, [date-style-reference.json](date-style-reference.json)
records text and color extracted with Poppler from a LibreOffice PDF produced by
the independent scratch [generator](../../../../../eng/generate-roadmap-138-control-style.py).
That generator pins explicit Noto Sans defaults. The reference PDF itself is not
committed; its SHA-256, input identity, tool versions and limits are recorded.

Regenerate after explicitly obtaining the pinned source:

```sh
python eng/extract-word-control-variants.py --input /path/to/sdtContent.docx \
  --output tests/wordnet.Conversion.Tests/Fixtures/ContentControls/OfficeWordVariants
```

The [manifest](manifest.json) records the extracted XML SHA-256 values.
