# Word-authored theme control

`theme1.xml` is the unmodified theme part of the public Open XML SDK
[`SDT.docx`](https://github.com/dotnet/Open-XML-SDK/blob/431ab05cf160248cc3885a4a766026d4f8243792/test/DocumentFormat.OpenXml.Tests.Assets/assets/TestDataStorage/v2FxTestFiles/wordprocessing/SDT/SDT.docx)
at commit `431ab05cf160248cc3885a4a766026d4f8243792`. The fixture's application
metadata identifies Microsoft Office Word. The upstream repository is MIT licensed;
its [license](../../ContentControls/OfficeWordParts/LICENSE) and
[notice](../../ContentControls/OfficeWordParts/NOTICE) accompany the existing
related glossary control. No font binaries, customer content or document body
are copied.

The source archive SHA-256 is
`8d297bee1383f87d0fa77e7b2e5573e88517297a88f36f569ec5eaa0f8201878`;
the extracted XML SHA-256 is
`bf4992e3fa7832e5c68fdbc4ec5189c8e5d347fef03c5ba2522ad704bde5078f`.
Tests construct a new minimal package around this part and authored text, validate
the OOXML independently, and use explicitly configured open-font substitutions.
This is not an interactive Word rendering or universal theme qualification.

Regenerate into a fresh directory after explicitly downloading the pinned source:

```sh
python eng/extract-word-theme-fixture.py --input /path/to/SDT.docx \
    --output /path/to/new-output-directory
```
