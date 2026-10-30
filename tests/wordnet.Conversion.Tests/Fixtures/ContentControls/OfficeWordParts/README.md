# Word-authored glossary parts

These test-only XML parts derive from the public Open XML SDK fixture
[`SDT.docx`](https://github.com/dotnet/Open-XML-SDK/blob/431ab05cf160248cc3885a4a766026d4f8243792/test/DocumentFormat.OpenXml.Tests.Assets/assets/TestDataStorage/v2FxTestFiles/wordprocessing/SDT/SDT.docx)
at commit `431ab05cf160248cc3885a4a766026d4f8243792`. The upstream document's
SHA-256 is `8d297bee1383f87d0fa77e7b2e5573e88517297a88f36f569ec5eaa0f8201878`;
its application metadata identifies Microsoft Office Word. The upstream repository
is MIT licensed; its [license](LICENSE) and [notice](NOTICE) accompany these parts.

`eng/extract-word-glossary-fixture.py` verifies the upstream archive hash, extracts
its single content-control subtree and six glossary parts, and normalizes line
endings to LF. The subtree is XML-reserialized without its outside whitespace; glossary part content is otherwise
unchanged. Document properties, the rest of the document body and unrelated package
parts are not copied. Tests construct a new minimal OPC package around these parts
and independently validate its schema. This derivative is not the original package
or an interactive Word/UI qualification.

To regenerate from the pinned source downloaded explicitly by the developer:

```sh
python eng/extract-word-glossary-fixture.py --input /path/to/SDT.docx \
  --output tests/wordnet.Conversion.Tests/Fixtures/ContentControls/OfficeWordParts
```

| Part | Extract SHA-256 |
| --- | --- |
| `control.xml` | `d61bbda54997b100aaff287d22355d0dd8ec24b8f28b463745669f8d08872526` |
| `glossary-document.xml` | `bdcbba691a783750bc47f5fbc0437d63a640e6e25c7bb1e5ae5c8fdf76b6a9c6` |
| `glossary-relationships.xml` | `de9880c4b866697dcaaf9dab4e0636198293fbb286962864a479904a2c2cbcc3` |
| `glossary-styles.xml` | `f242240b41e4cd0ad1171fba697ba1855d2fc2717e80ab4c7b34a8864a3a1087` |
| `glossary-settings.xml` | `46ba489a4657315f351dfd970ebfe7eb3581ed3f7dfda3c1d394434129fcfcdb` |
| `glossary-font-table.xml` | `4b26f248e2d2b3cef38606254bda9e6937d5fd5e1879125a07344e72720ceb6c` |
| `glossary-web-settings.xml` | `e5cb5c29101de2f2ac6bd08ad01597cdb299b47467b71c61605f4e5eee04743c` |

The released 1.5.0 source and public packages reject the unmodified upstream
glossary graph. Unreleased source admits this derivative in Lenient mode and
tests its local resource bytes, placeholder text and resolved gray appearance
after editing and saving. Its theme-dependent defaults still produce explicit
loss diagnostics and Strict mode rejects them. This one derivative does not
establish all Office-authored controls or complete acceptance of #143.
