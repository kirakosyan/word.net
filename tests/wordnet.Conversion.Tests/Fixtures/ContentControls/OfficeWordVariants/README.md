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

## Six control kinds captured in Word 16.0.20625

On November 22, 2026, an isolated desktop Word instance created rich text,
plain text, checkbox, dropdown, combo and date controls in a new synthetic
document. Each was tagged with its kind. The checkbox used ASCII X and a
hyphen as its selected and cleared symbols. The two lists offered Alpha and
Beta with values a and b, and selected Beta. The date was February 29, 2024.

`word-20625-six-controls.xml` contains the six actual `w:sdt` subtrees
extracted from the Word range's Flat OPC XML, each placed in a plain paragraph.
The test builds an independent minimal package and creates three local,
named placeholder entries with synthetic prompt text for Word's references.
It checks each kind, the shared placeholder references, editing, saved XML
and the static PDF. The unreduced Flat OPC package, including its Office
glossary extension metadata, still rejects typed loading with
`Control.Unsupported` at the glossary root. The original package and its
document properties are excluded from this fixture.

Fixture SHA-256: `d2d15d89a8e83180b704625d183ecf1f8f2cdb1dc25607e699865ba93ad4a453`.
