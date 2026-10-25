# DOCX and ODT exchange

## 1.4.0 roadmap #99 profile

Version 1.4.0 extends the 1.3.0 profiles with the bounded changes in
[roadmap-99-implementation.md](roadmap-99-implementation.md): direct HTML/ODT PDF export,
chart binding, scalar fields, structural comparison and qualified layout/native/object
combinations. The [1.4.0 release notes](releases/1.4.0.md) record compatibility and
qualification requirements. Earlier release receipts below remain historical evidence.

The P1 implementation merged in PR #85 and ships in 1.3.0 with `DocumentFormat.Odt = 12`.
`CanConvert` enables exactly DOCX → ODT and ODT → DOCX. ODT document sessions,
same-format conversion, DOC/ODT and other ODT exchanges remain unsupported.

```csharp
using var source = File.OpenRead("input.odt");
using var output = new MemoryStream();
ConversionResult result = new WordConverter().Convert(source, output,
    DocumentFormat.Odt, DocumentFormat.Docx,
    new ConversionOptions(FidelityMode.BestEffort));
foreach (ConversionDiagnostic diagnostic in result.Diagnostics)
    Console.WriteLine(diagnostic.Code);
File.WriteAllBytes("output.docx", output.ToArray());
```

The bounded ODF 1.3 text-package profile retains paragraphs/headings, explicit
spaces/tabs/breaks, resolved supported appearance, title/author, nine-level ordinary
lists and restarts, simple page masters/header/footer stories, bounded tables with
rectangular spans/repetition/cell appearance, inline PNG/JPEG frames, and safe links
and bookmarks. Named style identity is flattened with a diagnostic. Physical units
are pt/in/cm/mm/pc. Source page masters become next-page sections.
Ordinary XML whitespace collapses according to ODF text rules across inline spans;
explicit space/tab/break elements remain literal. List headers and continuation
paragraphs are unnumbered. List paragraphs can carry body master transitions, and
table-cell lists retain numbering. Master-story styles live in styles.xml.
Unused declarations and soft page breaks are no-ops. Annotation/note subtrees,
tracked-change stores and unknown non-flow containers are omitted with explicit
losses; their metadata and deleted text never enter the live body.

FODT, ODF 1.4, encrypted/signed/active packages, templates, dynamic masters,
unequal ODT columns, nested tables, floating/vector/OLE objects and spreadsheet
calculation are outside this profile. Unsupported formatting/fields/metadata/notes
produce explicit losses or rejection. Strict rejects losses before copying staged
output; permissive modes never execute content or fetch resources.

ODF package admission is separate from DOCX. It requires a first uncompressed exact
mimetype with no extra field, manifest/media-type agreement and unique canonical
package paths. Traversal, encoded/external image targets, duplicate/case-ambiguous
names, encryption/signatures and scripts fail in every fidelity mode. Actual
decompressed bytes, aggregate XML/model resources, styles/ancestry, repetition
products, cells and image pixels are bounded with cancellation checks. Resource
limits supplement host process limits.

The independent MIT fixture generator is
[generate-odt-fixtures.py](../eng/generate-odt-fixtures.py). The external check
[verify-odt.py](../eng/verify-odt.py) validates output with SHA-256-pinned OASIS 1.3
schemas and LibreOffice, including editable list starts, nine levels, two master
geometries/stories and styled headers, cell lists, merged cells, exact raster hashes and 60 × 30 pt frame geometry
within 0.5 pt. Schema assets are development-only downloads and retain OASIS notices;
they are not part of the runtime packages. This is a tested subset, not a claim of
full ODF conformance or arbitrary office pagination parity.
