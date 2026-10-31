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
