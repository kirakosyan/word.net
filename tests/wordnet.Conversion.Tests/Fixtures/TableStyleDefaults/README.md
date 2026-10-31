# Implicit default table-style controls

`generate.py` derives three synthetic documents from the independently authored
`row-column-priority.docx` in [TableStyleRegions](../TableStyleRegions/README.md).
The four-by-four grid and local first-row/first-column style are identical. The
`default` document marks the style as the table default and omits `tblStyle`;
`explicit` references the same default style; `no-default` marks it false and
omits the reference. The published [OOXML style rule](https://c-rex.net/samples/ooxml/e1/Part4/OOXML_P4_DOCX_style_topic_ID0E5ZQT.html)
applies the last default style of a type to objects without an explicit style.

LibreOffice 24.2.7.2 (`420(Build:2)`) on Ubuntu 24.04 exported these PDFs with
`writer_pdf_Export`, using the same pinned Noto Sans Regular and OFL provenance
as the parent controls. It painted the explicit style but not the implicit
default; `no-default` also had no fill. The retained PDFs record this difference,
not a Word result or a general rendering verdict. Strict non-DOCX output reports
the unresolved difference; editable DOCX retains the implicit relationship.
Archive timestamps may differ when generated, but decompressed parts match.
Input and output SHA-256 hashes are listed in [manifest.json](manifest.json).
