# Nested control reference

`generate.py` assembles `nested.docx` independently from the rich block and
inline subtrees in [OfficeWordVariants](../OfficeWordVariants/README.md),
retaining their source IDs, editor metadata and text. It removes their glossary
placeholder references, nests the inline subtree inside the block paragraph,
and adds an independently authored plain-text sibling and explicit Noto Sans
run formatting. The upstream Word-authored parts retain their accompanying
[MIT license](../OfficeWordVariants/LICENSE) and [notice](../OfficeWordVariants/NOTICE).
The nesting itself is synthetic; this is not an Office-authored nested fixture.

LibreOffice 24.2.7.2 (`420(Build:2)`) on Ubuntu 24.04 exported `nested.pdf`
with `writer_pdf_Export`, with the repository's Noto Sans Regular mounted
(SHA-256 `b85c38ecea8a7cfb39c24e395a4007474fa5a4fc864f6ee33309eb4948d232d5`,
OFL in `src/wordnet/Resources/Fonts/OFL.txt`). Independent Poppler extraction
read `This is an SdtBlock. SdtRun and Plain.` in order. The PDF and input hashes
are in [manifest.json](manifest.json). ZIP timestamps may vary; regenerated
decompressed parts are compared instead. The reference demonstrates static
text order, not Word execution, lock enforcement or complete nested ownership.
