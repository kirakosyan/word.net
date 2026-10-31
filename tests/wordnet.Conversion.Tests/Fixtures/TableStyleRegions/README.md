# Conditional table-style region controls

The synthetic DOCX inputs are independently constructed by `generate.py`, using
the repository's independent control generator for the OPC scaffold. The 4×4
grid labels each cell A1–D4 and assigns distinct fills to first/last rows and
columns, four corners, and alternating horizontal and vertical bands. Smaller
grids and table-look masks exercise boundaries, disabled flags and overlaps.
The committed packages passed Open XML SDK 3.5.1 validation. Archive timestamps
may differ on regeneration; every decompressed entry was checked for equality.

LibreOffice 24.2.7.2 on Ubuntu 24.04 exported the corresponding PDFs with its
headless `writer_pdf_Export` filter. Noto Sans Regular was supplied locally from
`src/wordnet/Resources/Fonts` (SHA-256
`b85c38ecea8a7cfb39c24e395a4007474fa5a4fc864f6ee33309eb4948d232d5`);
its OFL license is alongside the font. Input and output hashes are recorded in
[manifest.json](manifest.json). These references establish the observed cell
colors and regions for these cases, not pixel-identical table geometry. The
[earlier border fixture](../TableBorders/README.md) records placement differences.

LibreOffice's overlapping regions agree with the order [Microsoft documents for
Office](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oi29500/2ac331d4-cf1e-4fa0-8bca-6da74411e284):
row bands, column bands, first/last columns, first/last rows, then corners. The
published [ECMA-376 ordering](https://c-rex.net/samples/ooxml/e1/part4/OOXML_P4_DOCX_tblStylePr_topic_ID0EPR2T.html)
differs. These controls do not substitute for a Word execution or establish that
all conforming applications use the Office order.
The [OOXML default band size is one](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.tablestylerowbandsize?view=openxml-3.0.1),
which the `bands-implicit` LibreOffice control reproduces. [Microsoft's Word
implementation notes](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oi29500/77519f5b-08b3-4547-9f5d-0c4d87854220)
document a default of zero instead. The source retains the omission in DOCX and
rejects strict non-DOCX output; best-effort uses one and reports the loss. No Word
execution was available to reconcile the differing documented defaults.

The `row-size-two` and `column-size-two` inputs specify two-cell bands, but this
LibreOffice version still rendered alternating single-cell bands. It also painted
alternating bands for the zero-size controls, while Microsoft's notes describe
zero as disabling banding. Those reference differences are retained. Style sizes
zero through three remain editable in DOCX, but non-unit rendering is unqualified
and diagnosed. In one-row or one-column grids
with both first and last regions enabled, LibreOffice selected the first corner;
editable DOCX retains that overlap, while strict non-DOCX output rejects it rather
than claiming portable precedence.

`regions-overrides` applies direct clear RGB fills to a corner and an interior
cell, and LibreOffice renders both in preference to the conditional regions.
`regions-override-nil` retains a separate reference in which a direct `nil` does
not erase the corner color; the source profile diagnoses that reset as
unqualified.

The four `parent-*-child-*` controls test conditional styles across `basedOn`.
LibreOffice omitted the parent's conditional regions when the child defined no
region or a different one. When the child explicitly replaced the parent's
same region, it rendered the child color. The source retains the full chain in
editable DOCX, but diagnoses static output when it would rely on inherited
conditional regions; best-effort output resolves the retained hierarchy.
