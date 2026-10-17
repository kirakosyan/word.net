# Document automation sample

This public-API sample accompanies PR #35, merged as `0b2af843`. The API is in the
1.2.0 candidate; this sample uses the merged core through project references.
Check [publication status](../../docs/release-1.2.0-readiness.md) before installing a package.

From the repository root, run:

```sh
dotnet run --project examples/WordNet.Automation -c Release -- artifacts/automation-sample
```

The sample creates a letter, loads and fills its independently authored invoice
with repeating rows, an optional section and a caller logo, then assembles an
appendix. Outputs include editable DOCX and managed PDF. Inspect returned diagnostics;
PDF image alternative text remains a documented accessibility limitation.

The final reviewed source passed 696/696 complete cases on each local Linux x64
.NET 8/10 runtime and all 20 native desktop CI checks. MuPDF independently verifies
populated invoice text and the three-page assembled PDF. See [the usage guide](../../docs/editing-and-templates.md)
for syntax and bounds, and [testing](../../docs/testing.md) for exact revisions and
platform limits. Generated files belong under ignored `artifacts/`.
