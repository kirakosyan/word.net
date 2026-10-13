# Binary Word profile

The DOC adapter implements a bounded subset of Microsoft's published
[MS-DOC specification](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-doc/).
OpenMcdf handles the compound container; wordnet owns the format mapping.

## Retained content

- Main-story text in Unicode and compressed Windows-1252 pieces.
- Paragraph boundaries, tabs, manual line breaks and headings 1–6.
- Physical left, center and right paragraph alignment (`sprmPJc80`), including cells
  and supported style inheritance. Other justification forms produce diagnostics.
- Bold and italic runs, paragraph/character style inheritance and direct Boolean
  overrides, including the four MS-DOC toggle operands.
- Absolute half-point font sizes, RGB and indexed colors; simple decimal/bullet lists
  with up to nine levels and supported start overrides.
- Simple top-level editable tables, up to 63 cells per row, and the first repeating
  header row. Absolute preferred widths, unequal cell widths and per-side padding are
  retained; missing geometry uses an equal-width fallback.
- Inline PNG/JPEG encoded bytes and physical dimensions through PICF/OfficeArt BLIPs.
  See the [image profile](document-fidelity.md) for bounds and unsupported transforms.
- Paragraph and character formatting across multiple 512-byte formatting pages.
  Import also handles supported piece modifiers and indirect table properties.

The writer creates Word 97 compound documents with a stylesheet, text piece table,
character and paragraph formatting pages, and their indexes. It emits a final paragraph
outside tables, as required by the format. Adjacent tables need a separating paragraph.

## Explicit limits

DOC import still requires `FidelityMode.BestEffort`: original page geometry, arbitrary
typography, borders, shading and advanced properties are outside the semantic model.
`Doc.Formatting` reports this on every import. Unknown properties receive additional
grouped diagnostics. Output reports loss of code semantics and interactive hyperlinks.

Merged/nested tables fail explicitly. Multiple paragraphs in a cell become line breaks
with a diagnostic. Hidden text is omitted with a diagnostic; field display text is
retained while instructions are omitted. Headers, footers, notes, comments, OLE objects
and revisions are not preserved. Encryption is rejected. Binary output currently
diagnoses loss of image alternative text, custom label styling and unsupported effects.

The parser validates range order, page bounds, record sizes, style references/cycles,
indirect-property recursion, table terminators and row/cell counts. Parsing failures
leave destination streams untouched. Bounds do not constitute a process-memory sandbox.

## Independent verification

The test corpus includes Word 97 table files from Apache POI, a file exercising indirect
table properties, an unsupported merged table, and plain/styled documents independently
written by Apache POI 5.4.1. Provenance and hashes are in the fixture READMEs.

`eng/DocInteropCheck.java` checks exported heading style, bold/italic runs, three table
rows, two cells per row, cell text and the repeating header through Apache POI 5.4.1.
With Java 11+ and Apache `poi`, `poi-scratchpad` 5.4.1 and their Maven dependencies in
an external `jars` directory:

```sh
java -cp "jars/*" eng/DocInteropCheck.java rich.doc
java -cp "jars/*" eng/DocInteropCheck.java --generate-plain tests/wordnet.Conversion.Tests/Fixtures/apache-poi/empty.doc plain.doc
java -cp "jars/*" eng/DocInteropCheck.java --generate-emphasis tests/wordnet.Conversion.Tests/Fixtures/apache-poi/empty.doc poi-emphasis.doc
java -cp "jars/*" eng/DocInteropCheck.java --generate-alignment tests/wordnet.Conversion.Tests/Fixtures/apache-poi/simple-table.doc poi-alignment.doc
java -cp "jars/*" eng/DocInteropCheck.java --check-alignment aligned.doc
java -cp "jars/*" eng/DocInteropCheck.java --generate-appearance tests/wordnet.Conversion.Tests/Fixtures/apache-poi/empty.doc poi-appearance.doc
java -cp "jars/*" eng/DocInteropCheck.java --check-fidelity fidelity.doc tests/wordnet.Conversion.Tests/Fixtures/color-grid.png
```

Create `rich.doc` by converting this Markdown through DOCX to DOC using the public
`WordConverter` API and best-effort mode:

```markdown
# A heading

Plain **bold** and *italic*.

| Name | Value |
| --- | --- |
| Alpha | One |
| Beta | Two |
```

The verifier deliberately uses POI's paragraph-based table access: its default table
iterator assumes a newer table-depth convention and misses legacy Word 97 tables.
This check is structural interoperability, not a Microsoft Word rendering check.
The alignment checker expects three paragraphs containing `Align`, with physical
left, center and right alignment in that order. The mapping follows the published
[paragraph property values](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-doc/484822ee-a9d9-4af4-8423-29fda67a6a58).

The fidelity checker expects the synthetic blue 18-point title, a 120/240-point table,
a decimal list starting at three with a nested bullet, and the 80×40-pixel color-grid
image displayed at 120×60 points. It independently reads these values and image bytes.
