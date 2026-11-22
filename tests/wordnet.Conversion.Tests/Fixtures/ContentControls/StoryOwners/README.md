# Header and footer control reference

`generate.py` assembles `stories.docx` independently, placing the rich-inline
subtree from [OfficeWordVariants](../OfficeWordVariants/README.md) in a header
and an independently authored plain-text block control in a footer. The Word
subtree keeps its source ID, but its glossary placeholder is removed. Placement
in a story is synthetic; the fixture is not a Word-authored story template. The
upstream part retains its [MIT license](../OfficeWordVariants/LICENSE) and
[notice](../OfficeWordVariants/NOTICE).

LibreOffice 24.2.7.2 (`420(Build:2)`) on Ubuntu 24.04 exported `stories.pdf`
with `writer_pdf_Export`, using the repository's Noto Sans Regular
(SHA-256 `b85c38ecea8a7cfb39c24e395a4007474fa5a4fc864f6ee33309eb4948d232d5`,
OFL in `src/wordnet/Resources/Fonts/OFL.txt`). Independent Poppler extraction
shows `Reference SdtRun header`, the body, and `Footer label` on both pages.
The [manifest](manifest.json) records the input and output hashes; ZIP timestamps
may vary, so regenerated decompressed parts are compared instead. This checks the
selected static text and story placement, not Word execution, every story variant,
bindings, lock enforcement or pixel-identical geometry.

## Nested header and empty glossary

`generate-nested.py` copies the licensed, Word-derived wrapper and its two
inline children from the NestedOwners fixture into the existing two-page
header. It adds a local glossary relationship and an empty glossary document.
Both the placement and the empty glossary are synthetic. The generator writes
fixed ZIP timestamps. [Nested fixture provenance](../NestedOwners/README.md)

On November 22, 2026, desktop Word 16.0.20625 opened the file read-only with
macros disabled. Print layout reported two pages. The header range read
`This is an SdtBlock.  SdtRun and Plain.` and the footer read `Footer label`.
Word's header content-control collection identified `26134427` as the parent
of both `25057961` and `26000000`. The manifest records the input hash. Tests
compare the managed IDs and page count, inspect editable XML and check the
header and footer text positions relative to the body on both PDF pages.

The isolated NuGet 1.6.2 consumer loaded assembly SHA-256
`e27f5355ee328aded61d5d3039601034e6bc9f183e5844b45090a490c9751d04`.
The nested fixture raised `Control.ProtectedOwner`. Restoring the original
single-level header while keeping the empty glossary raised `Control.Unsupported`.
