# Invoice and report automation recipes

## P3 customer statement — source under review

The program also writes `p3-statement.docx` and `p3-statement.pdf`. It repeats
customer paragraphs and nested line-item paragraph/table regions; caller code sums
each customer's decimal totals. The independent MIT template and manifest can be
recreated with `python3 eng/generate-p3-template.py`. This source is pending review
and is not in public 1.4.0.

## P1 recipes under review

The program also runs `P1Recipes.Run`: it groups unsorted revenue, computes decimal
tax/total and a threshold predicate, expands a body paragraph region, saves DOCX/PDF
and exchanges the result with ODT. It records a caller-attributed replacement, writes
reviewed DOCX and accepted/rejected PDFs, and compares matching snapshots.
Generated files use the `p1-` prefix. Regenerate the independent template and manifest
with `python3 eng/generate-p1-templates.py`. These APIs are pending review and are
separate from the published packages.

Generate an invoice and a two-section operations report using the public `WordDocument` API. Each recipe produces editable DOCX and selectable-text PDF, using managed export without Office automation or remote fetching. The combined example references the optional provider for its explicit P2 native paths.

## Run

Install the SDK pinned in `global.json`. From a clean checkout, run:

```sh
dotnet run --project examples/WordNet.Automation -c Release -- artifacts/automation-sample
```

The command restores the managed dependencies, builds the sample and writes:

| Output | Contents |
| --- | --- |
| `invoice.docx` / `invoice.pdf` | Customer, two repeated service rows, embedded logo, optional terms and **266.00 EUR** total |
| `report.docx` / `report.pdf` | Summary, editable metrics table, a second section with delivery plans, shared header/footer and page fields |

The sample uses a fixed 2026-10-19 clock and invariant culture. Normal PDFs contain one invoice page and two report pages. Load, edit and save results print to the console with explicit cumulative-diagnostic labels, including phases with no diagnostics. Edit results inherit load findings; saves inherit load/edit findings, so the same warning may appear under multiple labels. The invoice PDF reports `Pdf.ImageAlternativeText`: its ordinary PDF profile embeds the logo without accessibility tags.

## Adapt the recipes

`AutomationRecipes.InvoiceData` rounds each quantity × unit-price amount to two decimal places with midpoint rounding away from zero, then sums those rounded line amounts. Unit prices retain any sub-cent precision in the printed table. Three lines at 0.005 EUR therefore display 0.01 EUR each and a total of 0.03 EUR. Decimal overflow throws automatically. Formatting and business calculations are application code. The template engine binds the supplied values; it does not evaluate arithmetic expressions.

Pass an empty item collection to retain the table header and show a zero total. Set `showTerms: false` to remove payment terms. Required data uses the engine's default error policy. Customer/description lengths and item count are bounded; the sample permits at most 200 items. The qualified long case has 100 short line items and four PDF pages, with repeated table headers and refreshed page numbers.

`Templates/invoice.docx` and `Templates/report.docx` are independently authored MIT templates. Their `manifest.json` records deterministic input hashes, generator hash and authored page expectations; these expectations are separate from measured office references. Reproduce them with:

```sh
python eng/generate-automation-templates.py
```

The logo is the existing MIT-authored `color-grid.png` fixture copied into the sample output directory at build time. Replace the caller-provided bytes and explicit dimensions for your own application. No file paths or URLs are resolved by merge controls.

Both output formats finish in memory before existing files are replaced. Files are then staged beside their destinations and each replacement is atomic. A stage message is printed after both temporary files exist. Cleanup attempts both temporary files independently and cannot mask a conversion, cancellation or replacement failure; an inaccessible temporary file may require later removal. Cancellation is checked before the replacements begin. Parsing, missing-data, expansion, output-limit and conversion cancellation failures preserve existing files. The two-file replacement is not a filesystem transaction; a disk/I/O failure during replacement can commit one file before the other.

DOCX retains editable PAGE/NUMPAGES instructions and their template caches; an office application can update these on open. PDF layout refreshes page fields independently for every page. No template syntax, engine calculation feature or public API is added by the sample.

The regression suite compiles the same recipe source against every desktop test target. It independently validates DOCX schemas, table rows and image bytes with Open XML SDK, and reads PDF text, page counts, headers/footers and images with PdfPig. It covers empty/long invoices, missing data, expansion/output limits, cancellation before/between saves and after staging, cleanup failures, cumulative diagnostics, sub-cent rounding, repeated table headers and replacement of sentinel-filled outputs.

See [editing and templates](../../docs/editing-and-templates.md), [comparison methodology](../../docs/comparison.md) and [test evidence](../../docs/testing.md).

## P2 object and native chapter recipe

Supply explicit local font paths to run the unpublished P2 recipe as well:

```sh
dotnet run --project examples/WordNet.Automation -c Release -- artifacts/p2-sample tests/wordnet.Conversion.Tests/Fixtures/P2/STIXTwoMath.ttf tests/wordnet.Conversion.Tests/Fixtures/P2/NotoSansDevanagari-Regular.ttf tests/wordnet.Conversion.Tests/Fixtures/Interoperability/NotoSansArabic-Regular.ttf
```

`P2Recipes.Run` updates both chart caches and real workbook cells, updates a real
OMML denominator, appends the objects to the populated report and exports
`p2-report.docx`, PDF, XPS and SVG pages. It writes separate `p2-hi-IN-ua.pdf` and
`p2-ar-ua.pdf` ordinary chapters with explicit languages and checks that attempting
to combine retained objects with native/UA output leaves a sentinel stream untouched.
The test fonts are development assets with retained notices; callers choose their own
embedding-permitted fonts. No font is fetched by the recipe. Encryption/decryption
and DOCX/PDF digital signatures are absent. See the [P2 profile](../../docs/p2-objects-typography.md).
