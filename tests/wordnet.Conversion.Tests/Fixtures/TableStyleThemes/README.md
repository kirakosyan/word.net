# Conditional table-style theme-fill controls

The synthetic DOCX packages are independently constructed by `generate.py` from
`eng/generate-roadmap-138-controls.py`; they contain a two-by-two table with a
first-row conditional style and a bounded local theme whose `accent1` is `2266CC`.
The `theme`, `theme-tint` and `theme-shade` inputs also store `FF0000` as the RGB
fallback. The explicit-blue and explicit-red inputs contain the corresponding
direct RGB styles. The tint and shade use `80`; their OOXML calculations for the
theme value are `90B2E5` and `113366`. The published [shading element](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.shading?view=openxml-3.0.1)
describes the theme and fallback attributes.

LibreOffice 24.2.7.2 (`420(Build:2)`) on Ubuntu 24.04 exported the PDFs with
`writer_pdf_Export`. The three theme cases painted the stored red fallback,
including when a tint or shade was specified. The explicit controls painted red
and blue respectively. This difference is retained; no Word execution was
available and the probe does not establish general theme-fill or geometry parity.
All documents use the locally mounted Noto Sans Regular (SHA-256
`b85c38ecea8a7cfb39c24e395a4007474fa5a4fc864f6ee33309eb4948d232d5`), whose OFL
is in `src/wordnet/Resources/Fonts/OFL.txt`.

The [manifest](manifest.json) records the exact DOCX inputs used for the PDF
captures and the PDF hashes. Regenerating with `python generate.py --output DIR`
may change ZIP timestamps; the decompressed entries were checked for equality.
Tests validate the input and saved packages with Open XML SDK 3.5.1, inspect the
independent PDF colors, and exercise editing, inheritance and loss diagnostics.
