# Direct table-border reference

`table-borders.docx` is a synthetic 2×2 table containing only the letters A–D.
`generate.py` constructs the independent WordprocessingML package; ZIP timestamps
may differ on regeneration. Its six direct, one-point RGB borders identify the
four outside edges and two inside edges without cell overrides or a table style.
The checked input SHA-256 is
`1943a579ef571cf9ad7216e9d4c8577c9829aeaea9dcf1dcded8b742ebb1f3bf`.

`table-borders.pdf` was exported from that DOCX with LibreOffice 24.2.7.2
(`420(Build:2)`) on Ubuntu 24.04 using the headless `writer_pdf_Export` filter.
Its SHA-256 is
`c1f14cfbf8b4152e2f6d2f0b95eceef29376f5d575aa24388daa71642dae7284`.
Both renderers use the repository's pinned Liberation Sans Regular font, mounted
into LibreOffice's local font directory; the original font SHA-256 is
`76d04c18ea243f426b7de1f3ad208e927008f961dc5945e5aad352d0dfde8ee8`.
The PDF embeds a subset of that font. Its copyright and license are reproduced
in [Liberation-OFL.txt](Liberation-OFL.txt).

The independent comparison covers border colors, the ordering and midpoint of
the inside edges, fixed column width and reading order. It does not establish
pixel-identical placement or qualify styled, merged, nested or multipage tables.

In the pinned Linux x64 working-tree probe, LibreOffice placed the left and right vertical
edges at 66.6 and 306.6 points from the page's left edge; the pending managed
renderer placed them at 72 and 312 points. The two renderers therefore agree on
the 240-point width but differ by 5.4 points horizontally. The LibreOffice top,
inside and bottom horizontal edges were 72.5, 86.2 and 99.8 points from the top;
the managed renderer used 72, 84.65 and 97.30 points. These observed placement
differences remain unresolved, not passing parity results; exact-commit and
other-runtime qualification remain separate.

## Whole-table style inheritance

`generate-style.py` derives a synthetic input from the border fixture: the
six edges are defined in a `Base` table style and the table references a
`Derived` style that uses `w:basedOn`. The checked DOCX SHA-256 is
`c4baac3ea3086c989d26d0242a24b30a15abf77dca309e6cc96e744891f22941`.
LibreOffice 24.2.7.2 rendered the inherited border colors in the pinned-font
`table-style.pdf` (SHA-256
`8744f6b680ce3b68a1c9bdaedc40c3c35e019ac271ac5ee125a261d5e859da77`).
The same 5.4-point horizontal and smaller vertical differences recorded above
remain; this control does not qualify conditional regions or general style parity.
`generate-style-reset.py` adds a child-style `nil` top border. LibreOffice
omitted the inherited red top while retaining the other five border colors.
The reset DOCX and PDF hashes are
`51620b97e678a21a44314272823271755b589fb6ac475a4e57d9cfc20585ee09` and
`38c012471f1cf582429dbaa8942ddf9b119bdaf1d6bddb60751403ec17a5d920`.

## Table-shading controls and retained reference failure

`generate.py --shading` constructs the same table with a direct, clear
`FFF3C0` table fill. The checked DOCX and LibreOffice PDF SHA-256 values are
`6dc4782ffe751858a0a99f4039f3270cf042cc03ddc3d2b50887d461be8eac2d` and
`c6bbd1ec3472ba524d74d9cde5982deef232ece5b13981c6e347d185f1681497`.
LibreOffice did **not** paint the table-level fill in this probe. This failed
reference result is retained and cannot qualify table-shading parity.

`generate.py --cell-shading` creates a separate control with the same color
applied directly to each cell. Its checked DOCX and LibreOffice PDF SHA-256 values
are `a27a29a562f611f7da073d17d0b886447e727343c22c3067d2a3d513376fda35` and
`9f993f53e3c6795d5c2eb8f6473fe1a00d662a3f350b1cc32c9b255d4d0edc34`.
LibreOffice painted the direct-cell color, which checks the reference pipeline
and color only. Table and cell shading have different padding and precedence
semantics, so the control is not evidence that their geometry is equivalent.
Both PDFs use the pinned Liberation Sans font and license described above.
The separate [ODT probe record](../../../../docs/measurements/roadmap-138-table-shading-reference.json)
documents the source hashes, ODF schema checks and LibreOffice reopening result.
