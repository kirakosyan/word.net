# Conditional table-border controls

The synthetic DOCX controls are independently derived by `generate.py` from the
four-by-four grid in [TableStyleRegions](../TableStyleRegions/README.md). They
define first/last row and column edges, a north-west corner, a disabled look flag,
an interior edge, a combined first-row fill and border, and a parent/child style with and without a `nil` reset.
Distinct RGB edge colors identify each side. Input and output SHA-256 values are
recorded in [manifest.json](manifest.json); regenerated ZIP timestamps can vary,
but decompressed entries were checked for equality.

LibreOffice 24.2.7.2 (`420(Build:2)`) on Ubuntu 24.04 exported the PDFs with
`writer_pdf_Export` and locally mounted Noto Sans Regular (SHA-256
`b85c38ecea8a7cfb39c24e395a4007474fa5a4fc864f6ee33309eb4948d232d5`, OFL in
`src/wordnet/Resources/Fonts/OFL.txt`). It rendered the four specified outer-edge
colors, the selected corner edges and the interior control; the disabled look
did not render its edge. The combined fill-and-border control retained both colors.
The child style with no conditional override did not
render the parent's border, while the explicit child `nil` also produced no edge.
Those inheritance and interior/conflict cases remain explicitly unqualified.
The [earlier border fixture](../TableBorders/README.md) records independent table
placement differences; these controls do not establish pixel-identical geometry
or Word execution.
