# First-row table-style reference

These synthetic two-row DOCX files come from the independently authored
`eng/generate-roadmap-138-controls.py` generator; `generate.py` preserves that
package while setting the table-look mask to `0020` (first row enabled) or
`0000` (disabled). The first-row style specifies clear `2266CC` cell shading.
The DOCX SHA-256 values are `f4b83252ac98b49b9e108c46f1db551b3e7f707facbae27b2738b4d0ca9d0437`
and `4fd9c3d23950203f62e7d5190b127f845da6e454fba90adf998f67caaee2cc47`.

LibreOffice 24.2.7.2 on Ubuntu 24.04 exported `look-on.pdf` and `look-off.pdf`
with the headless `writer_pdf_Export` filter and the repository's Noto Sans
Regular font mounted locally. The PDF SHA-256 values are
`e8ef57c7d500510c87f6428942ddbd447d5bba35274b239efae6b076589735d7`
and `6602ed1a707f4d3b93feb980e396cab69cce4ac96ab72e00198d374fb8a18664`.
The font and its OFL license are in `src/wordnet/Resources/Fonts`; the font
SHA-256 is `b85c38ecea8a7cfb39c24e395a4007474fa5a4fc864f6ee33309eb4948d232d5`. The on
fixture paints the two header cells blue; the off fixture paints neither.
This is a color and region control, not proof of identical layout geometry.
The earlier [table reference](../TableBorders/README.md) records measured
horizontal and vertical placement differences, which remain unresolved.

The other three inputs and PDFs retain the ambiguous controls. LibreOffice
honored `w:val` over a conflicting `w:firstRow` flag and applied the conditional
style when `tblLook` was absent. Those observations do not establish portable
precedence. The source profile diagnoses conflicting mask/flag values and
absent masks instead of assuming them. Their DOCX/PDF SHA-256 pairs are:

| Control | DOCX | PDF |
| --- | --- | --- |
| absent look | `1330fc04b459f2c9f7fe61d81c53e2969ca1a55a61802b01af93ce7f8e7de4f2` | `2dc71dbd5de6111f8431aabef179c838c371ca6fd6685fb2c0a0d41a0a48e8fe` |
| mask on, flag off | `e6ff2acbed0263f8fe51c2460355c382cc50c05698da2b1ee2d97ebd301cd54c` | `8cc2f9457049731e01dc05cddbcb4f3e5746b3de45e281ff9b54bcc4a6a7aa5f` |
| mask off, flag on | `2bedfbda8df4cafd7dae6ef01c843a931a429fe0e08834801e8003f0472a2892` | `367890b2e91befeab83806bc50c5f01ea92232a9ab38f83c60e91b497b7ad1c3` |

The independent direct-cell control paints the first header cell `DD4422`
instead of the inherited blue and leaves the second header cell blue. A `nil`
cell-shading control remains blue in LibreOffice; that reset is not admitted by
the source profile. A separate whole-table-style fill plus first-row fill control
paints the blue first row but does not show the expected whole-table fill in the
reference; the combination remains unqualified. Their DOCX/PDF SHA-256 pairs are:

| Control | DOCX | PDF |
| --- | --- | --- |
| direct cell color | `53e2d76c002b0075afef02c0d5a796e49bbbdeb66b6c643f8a90c50331d69306` | `ba773c4fcde617027c1b1fe56eb97b38c87ece04bdcdc727910e6122d7c2dcf6` |
| direct cell nil | `e5682ab2aa66285f964e3f4137489ba8663c998b22fdeb383aebf1d2c19cf15a` | `7f31a69c11af5dada97d9d087306fcf583302fa4b833879c96c373c7250a6374` |
| whole-table plus first row | `5abb3a74c4b25d66db87e6af7dcb3bef5e7dae6ac109b7ec402ce8c529a1395e` | `d795859184400992d19be082ce84fbac01e049fc703eace206df3c5eb729d172` |
