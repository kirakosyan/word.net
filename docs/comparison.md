# Reproducible business-document comparison

The [issue #103 refresh](fidelity-103-baseline.md) records the selected clean source
and independently restored public 1.4.0 measurements, plus twelve separate reduced
contrasts. `eng/refresh-fidelity-baseline.py` captures references and retains original
verdicts; `eng/test-fidelity-baseline.py` checks evidence identity/completeness and
deterministic fixture generation. The original corpus and tolerances below are unchanged.
The review fixes keep generation under `artifacts/`, protect the measured fixtures,
use portable stored ZIPs for new scratch output, and validate explicitly located
runner-side POI jars before measuring. The refresh runner selects listed stable
NuGet releases and restores a consumer without source transitive pins. Its
`--summarize` mode reproduces the historical tables without re-measuring inputs;
new runs record their own summary, raw-evidence audit and observed environment.

The twenty-four independent MIT-authored examples in
`tests/wordnet.Conversion.Tests/Fixtures/Comparison` cover three invoices, contracts,
reports and letterheads in the original twelve-case cohort, plus twelve P0 cases
covering mixed fonts, long tables, footnotes, continuous sections, floating logos
and multi-page reports. Names, amounts and organisations are invented.
`eng/generate-comparison-corpus.py` regenerates deterministic DOCX ZIPs and the
manifest, independently of wordnet serialization. No customer documents, commercial
binaries, license keys or proprietary fonts belong in Git. Inputs are independently
Open XML schema validated by the mandatory conversion suite.

The generator writes portable POSIX paths and UTF-8/LF JSON; `.gitattributes` keeps
its source and JSON hashes stable across Windows and Unix checkouts. The manifest
records input hashes, pinned Noto Sans/Liberation font hashes/versions, authored
text in reading order, explicit page breaks and known losses. Its page counts are
**authored expectations**, not measurements by Word. Nested tables/decoration,
percentage widths, Roman numbering, columns, revision acceptance, missing fonts and
positioned VML text remain visible baseline limitations. Detected losses are recorded
separately from text, page and raster verdicts. Both the runner and conversion suite
require the exact warning-code set in `knownLosses`: a missing expected warning or
a new unlisted loss fails the corpus gate; informational diagnostics remain advisory. Existing Enterprise PNGs remain
reviewed **wordnet regression goldens**, not Word or Aspose references.

## Reproduce the free baseline

Use the SDK in `global.json`, Python 3.10+, Poppler `pdftoppm` and JDK 17+.
Raster metrics pin Pillow 12.3.0. POI 5.4.1 and its test-only dependencies are pinned
by SHA-256 in `eng/comparison-poi.json` (Apache-2.0; see dependency notices).

```sh
dotnet restore wordnet.sln --locked-mode
dotnet build wordnet.sln -c Release --no-restore
python -m venv artifacts/comparison-venv
artifacts/comparison-venv/bin/pip install -r eng/comparison-requirements.txt
python eng/generate-comparison-corpus.py
python eng/prepare-comparison-poi.py
artifacts/comparison-venv/bin/python eng/test-comparison.py
artifacts/comparison-venv/bin/python eng/run-comparison.py --poi-classpath artifacts/comparison-poi
```

On Windows use the venv's `Scripts/python.exe` and `Scripts/pip.exe`. Pass `--dotnet`
and `--renderer` for tools outside PATH. The optional POI reader extracts actual
editable DOC body text in source order, independently of wordnet's reader. It checks
all required text fragments in order; no missing-text tolerance applies. Omitting
`--poi-classpath` records `independent-reader-not-provided` unless `antiword` is
available or selected with `--editable-reader`. Antiword rejects some small valid
DOC streams; such reader failures stay visible. POI is the reproducible corpus reader.

The runner starts one fresh process per document/provider, sequentially on the same
host/runtime and inherited CPU affinity. Conversion timing excludes orchestration,
reference extraction, rasterization and editable DOC export. Each worker reports:

- First-conversion cold latency (including first-conversion JIT, excluding process
  startup, explicit provider loading and font-directory catalog setup).
- Every warm conversion's latency, total process managed allocations during that
  interval, output bytes, post-conversion RSS and OS process high-water RSS.
- Runtime/OS/architecture, CPU description, processor count, machine and affinity,
  every loaded assembly's version/hash, and the identical supplied font file hashes.
- PDF page dimensions/count, selectable text in content order and glyph baselines
  in points, plus conversion diagnostics with phases and source locations.

`workerWallMilliseconds` includes process startup, editable export and PDF extraction;
it must not be compared as conversion-only latency. RSS includes the CLR, JIT and
loaded fonts/providers. It is neither managed heap size nor a per-document bound.
Keep raw samples; no opaque quality score or superiority claim is produced.
Failures/timeouts are retained per document and make the command fail. Missing
references remain `not-provided`, rather than an invented passing comparison.

## Optional Word and Aspose references

### Independent open-source office capture

On Linux, install LibreOffice Writer and fontconfig, then capture the P0 cohort with a private
fontconfig catalog containing only the manifest's verified font bytes:

```sh
python eng/capture-office-comparison.py --output artifacts/p0-office
artifacts/comparison-venv/bin/python eng/run-comparison.py --cohort p0-2026-10 \
  --iterations 1 --references artifacts/p0-office/references.json \
  --output artifacts/p0-comparison-office
```

Capture rejects Windows/macOS because their LibreOffice builds do not honor this fontconfig isolation. CI pins both `libreoffice-writer` and `libreoffice-core` to `4:24.2.7-0ubuntu0.24.04.7`; updating that environment requires an explicit workflow change. Local Debian 25.2.3.2 evidence remains a distinct producer.

This extends the existing manifest/reference contracts and comparison runner.
`kind: libreoffice`, `artifactMode: open-source` explicitly identify these references;
they are never labeled Word or licensed Aspose results. Capture records the exact
office version/build, launcher/binary, input, font and PDF hashes and renderer banner.
Use a fresh output directory when changing the font manifest. Temporary office
profiles are isolated and removed; stale PDFs cannot become successful captures.

`--cohort baseline` selects the original twelve inputs; `--cohort all` (the default)
measures all 24. New fixture metadata identifies its feature and authored expectation
basis. Neither corpus generation nor reference capture changes converter behavior.

The comparison command retains its nonzero exit on differences. `results.json`
contains all measurements and unchanged verdicts; `defects.json` ranks failures by
conversion, authored/editable text, diagnostic expectations, pagination, reference text/order, glyph placement, page geometry and raster differences. Findings identify their engine, retain every failed verdict and only attribute candidate wordnet source paths to wordnet results. Each finding names a minimal fixture and candidate implementation
source locations; these are triage starting points, not proven root causes. The
short inputs isolate features, while the long variants exercise their larger scope.
See [the P0 comparison report](comparison-p0.md) for recorded results and limitations.

CI requires all 24 authored/content/diagnostic gates. It also requires twelve complete
independent office measurements and validates that every failed document/engine verdict has exactly one ranked finding, while retaining the known office fidelity differences
in a separate nonblocking comparison step. `business-comparison` archives raw office
PDFs/references, engine PDFs, snapshots, rasters and ranked findings, including failures.
Office binaries and generated references stay out of Git and shipped packages.

### Commercial reference tools

A caller-provisioned, legitimately licensed Aspose.Words .NET version and its local
dependencies can be loaded by the non-packable benchmark worker. No Aspose package
is restored or shipped. Use an ignored/output directory outside the checkout for
commercial binaries and secrets, then run on the same host as the wordnet worker:

```sh
python eng/run-comparison.py --engine both --poi-classpath artifacts/comparison-poi \
  --aspose-assembly /private/aspose/Aspose.Words.dll \
  --aspose-license /private/aspose/license.lic \
  --capture-aspose-reference artifacts/comparison/aspose-references.json
python eng/run-comparison.py --poi-classpath artifacts/comparison-poi \
  --references artifacts/comparison/aspose-references.json
```

The license is passed only to `License.SetLicense` using the child process environment;
it is not recorded. No license means `artifactMode: evaluation`. Evaluation/trial
PDFs, truncation and extra watermark text are explicitly excluded from all defect
verdicts, including authored-content checks. They cannot become licensed goldens.
A failing license load is an explicit worker failure. The worker records the exact
provided Aspose assembly version/hash; this repository does not supply a license
or claim qualification of unexecuted Aspose versions.

For independent Word references, use **licensed desktop Word on Windows**, install
the pinned Noto Sans faces, then run the test-only capture script:

```powershell
./eng/capture-word-comparison.ps1 -OutputDirectory artifacts/word-comparison `
  -InstalledFontDirectory C:/Windows/Fonts -ArtifactMode licensed
python eng/run-comparison.py --references artifacts/word-comparison/references.json
```

Renderer capture reads native stdout/stderr directly, including on Windows
PowerShell 5.1, and normalizes CRLF/LF banners on both capture and comparison.
`eng/test-comparison-renderer.ps1` checks the stderr and failure paths without Word.
Capture defaults to `evaluation`; select `licensed` only for a legitimately licensed
desktop installation. The script opens authored inputs read-only, disables macros, records Word version/
build and input/font/PDF hashes, closes its own Word instance and saves references
outside Git. It does not alter documents or automate Office in production. Each
reference declares `kind: word`, `aspose`, `libreoffice` or `raster-golden`, `artifactMode`, version,
input/PDF hashes, font manifest and renderer version. Existing Word page-count CSVs
and test-only goldens remain separately identified; they are not relabeled Aspose.

## Tolerances and evidence

Every required text fragment must survive in reading order, and authored/reference
page counts must match exactly. Reference comparisons additionally require identical
per-page normalized text and glyph sequences before measuring maximum baseline
displacement (at most **0.25 pt**). Page dimensions tolerate only **0.01 pt**.
All pages rasterize to RGB at **96 DPI**, using the same recorded Poppler version.
The existing reviewed raster policy permits at most **0.2%** significantly changed
pixels (sum RGB delta >96) and mean channel error **0.25/255**. These antialiasing
allowances never waive the separate exact text/page checks. Different input/font
hashes or renderer versions yield an explicit incomparable finding.

`eng/test-comparison.py` independently authors actual PDFs and proves that a 12 pt
shift fails glyph and raster checks, missing text fails extraction, and an extra
page fails pagination. It also rejects reordered content. Mandatory CI repeats the
free corpus and these controls while retaining all previous conversion/OCR,
performance, concurrency and package gates. Commercial tools remain optional.

Machine-readable contracts: `eng/comparison-manifest.schema.json`,
`eng/comparison-reference.schema.json`, `eng/comparison-result.schema.json`.
Raw reports, PDFs, editable DOCs and rasters are under ignored `artifacts/comparison`.
The checked-in `Comparison/baseline.json` is a compact record of the local free run,
including detected fidelity losses and missing commercial references; it does not
replace raw measurements or establish Word/Aspose parity.
