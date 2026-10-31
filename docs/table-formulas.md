# Unreleased source: bounded table formulas

This profile is pending source work for [#159](https://github.com/kirakosyan/word.net/issues/159).
It is not part of the published 1.5.0 packages. Exact source, runtime and package
qualification remain separate; the issue remains open until its remaining criteria
and merged evidence are reconciled.

For DOCX/DOTX editing and conversion to DOCX/DOTX, PDF or XPS, a table containing
an admitted formula can refresh `SUM(ABOVE)`, `SUM(LEFT)`, `SUM(A1:B2)` and
single-cell references such as `A1`. They can participate in the existing checked
decimal arithmetic and optional numeric-picture grammar. Ranges are inclusive,
rectangular and ordered from upper left to lower right. Addresses use columns
`A`–`Z` and rows `1`–`256` without leading zeroes; they never refer to a different
table or external document. Letter case is ignored. There are at most 256 formulas
and 8,192 reference visits per table; dependency depth is at most 64 and never
exceeds the configured nesting limit. General functions, named ranges, multiple
arguments and cross-table references are outside this profile.

Admission requires a rectangular table of at most 256 rows and 26 columns, with
no horizontal or vertical merges, unresolved row controls or preserved row
revisions. Every referenced cell must have one paragraph consisting either of
plain invariant decimal text or one `=` field with only surrounding whitespace.
Object, note, box, control and revision content is excluded. Plain input accepts
an optional sign and decimal point, but not a thousands separator, a localized
decimal comma, an empty cell or nonnumeric text. Header rows use the same rules as
other rows: numeric header cells contribute; a referenced text or blank header
causes the table's caches to be retained. Positional sums cover all earlier cells
in the same column or row. An empty positional range has a sum of zero. These
deliberate exclusions avoid guessing Word behavior for blank, textual or merged
cells; they are not a claim of general Word formula compatibility.

Dependencies are calculated before their consumers, using unformatted decimal
results rather than rounded display caches. As in the existing scalar profile,
arithmetic uses checked decimal operations and is normalized to 15 significant
digits; numeric pictures round midpoint values away from zero with up to ten
decimal places. A trailing `%` in the admitted numeric picture is a literal
percent sign: for a fractional value, use `= A1 * 100 \# "0.00%"` to display
`0.15` as `15.00%`. It does not implicitly multiply the value. Formatting and
numeric cell parsing are invariant; when populating cells through report merge,
choose `TemplateOptions.Culture` explicitly so that generated numbers use the
same invariant format.

All `=` field results in one participating table are staged together. A cycle,
unsupported field or cell, invalid address, divide-by-zero, overflow or exceeded
table graph bound discards those staged results and retains every source cache in
that table. `Field.UnsupportedUsedCachedResult` reports this informational fallback,
including in Strict mode; it does not assert the cached values are current. Other
tables are evaluated separately. The existing conversion-wide resource limits and
cancellation remain hard failures before publishing output. The source document and
caller streams retain their existing ownership and atomicity contract.

On each save or fixed-output conversion the graph is rebuilt after admitted edits
and report-row expansion, so computed caches and displayed values reflect the
current table. Document reference refresh preserves the computed table results.
For other destinations the existing cached-field and fidelity policy applies.
`WordConverter` does not add same-format DOCX-to-DOCX conversion; use
`WordDocument.Load` and `Save` to update editable DOCX caches.

The [synthetic fixture provenance](../tests/wordnet.Conversion.Tests/Fixtures/README.md#table-formula-interoperability-probe--2026-10-31)
records an independent LibreOffice check of selected numeric sums and dependencies,
and explicitly identifies its percentage-picture and non-table-field limitations.
The source tests also inspect schema-valid output, fixed PDF/XPS text, culture,
expansion/edit refresh, cycles, invalid references, arithmetic and work limits.
