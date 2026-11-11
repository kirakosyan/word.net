# Hosting untrusted document conversions

The library's byte, character, page, nesting, block, image-placement and pixel settings are admission checks.
They do not cap total process memory or native decoder CPU time. Use a separate worker
process or container for untrusted documents. A cancellation token is cooperative, and
cannot replace an external deadline.

## Worker contract

- Run as an unprivileged identity with no credentials and no network access. Mount only
  the input, an output directory and read-only fonts/OCR assets. Never mount a user's
  home directory or the host's container-management socket.
- Set operating-system memory/CPU/process limits. Terminate and replace the worker when
  its deadline expires; do not reuse a process after a native crash or out-of-memory error.
- Bound queue length and concurrent workers. Each OCR conversion may allocate an engine,
  raster and correction buffers. Start with one OCR job per worker and measure before
  increasing concurrency.
- Stage to a new output file and rename it only after success and application validation.
  Caller-stream ownership and final-copy failure behavior are documented in the API.
- Record format pair, diagnostic codes, duration, input/output sizes and worker outcome.
  Do not log document text, passwords, credentials or original file names by default.

`InvalidDataException` denotes malformed input or an admission-limit failure;
`NotSupportedException` denotes unsupported features, strict-mode losses or format pairs.
`OperationCanceledException` denotes cancellation. Host I/O/setup/native-loading errors
remain actionable failures. Dependencies' malformed DOC/PDF container failures are mapped
to format-specific `InvalidDataException` messages; the original exception is retained
as the inner exception for local investigation.

## Reproducible qualification

Build the solution first, then run the tool without rebuilding it while workers are active:

```powershell
dotnet tools/wordnet.Qualification/bin/Release/net10.0/wordnet.Qualification.dll self-test
dotnet tools/wordnet.Qualification/bin/Release/net10.0/wordnet.Qualification.dll fuzz tests/wordnet.Conversion.Tests/Fixtures artifacts/qualification 128
dotnet tools/wordnet.Qualification/bin/Release/net10.0/wordnet.Qualification.dll benchmark tests/wordnet.Conversion.Tests/Fixtures artifacts/qualification 100
./eng/prepare-ocr-tests.ps1
dotnet tools/wordnet.Qualification/bin/Release/net10.0/wordnet.Qualification.dll ocr tests/wordnet.Conversion.Tests/Fixtures artifacts/qualification 1
```

The malformed-input campaign executes 128 deterministic cases for each of eight readers
(1,024 cases). Cases include the unmodified seed, truncation, bit flips, invalid field
values, zeroed headers, missing prefixes and trailing data. Each runs in a fresh process,
with a 10-second cooperative deadline and an external 30-second/512-MiB guard. Unexpected
managed exceptions, native exits, guard violations or destination changes fail the campaign.
This is a reproducible mutation campaign, not coverage-guided fuzzing or proof of security.
The supervisor self-test covers short-lived successful workers, nonzero exits, external
deadlines and memory-guard termination. An observed Windows exit/memory-sampling race is handled without
masking the worker's final exit code or unrelated monitoring errors.
The regular test suite separately covers entities, ZIP expansion/traversal, nesting,
malformed offsets, image limits, cancellation and strict-loss destination preservation.

The benchmark uses four concurrent operations on one converter, checks canonical output
content, and covers all 28 directions. A 100-iteration run records 2,800 measured conversions,
plus content-check conversions and a 50,000-character round trip. Its guard is five minutes
and 768 MiB. These are regression ceilings for this small corpus, not throughput promises
for arbitrary documents. OCR runs each corpus PDF to DOC and DOCX with a 45-second token
and an external 60-second/512-MiB guard per case. Accuracy and reading order are checked
separately by the shared OCR tests.

JSON evidence records runtime, architecture, seed/library hashes, worker outcomes, elapsed
time, sampled peak working set and conversion timings. Memory sampling is every 50 ms;
it may miss short-lived peaks and is not an OS-enforced sandbox. Run in a restricted host
for security qualification. Hosted native x64 CI runs all three campaigns; configuration
alone does not establish a pass. No customer data is sent anywhere by the harness.

## Pending source fixes for #175–#181

These changes are proposed together and are not a new published release. Existing
constructor signatures and all five library targets remain unchanged.

- `ConversionOptions.MaximumBlocks` defaults to 32,768 and is snapshotted per call.
  It counts imported paragraphs (including empty and synthetic paragraphs), tables
  and note records across body, cells, stories and ebook chapters. A parse that
  exceeds the bound fails with `Limit.Blocks` before creating the next model block.
  It does not constrain blocks created later through the editing API, which has
  its own admission checks. Plain text is split incrementally, without first
  allocating an array for every newline.
- `MaximumImagePlacements` defaults to 10,000. Repeated DOCX/EPUB image parts and DOC
  picture offsets share admitted encoded bytes; placement geometry remains separate.
  Unique encoded resources are charged against `MaximumExpandedBytes`, and existing
  per-placement pixel accounting remains. HTML also budgets base64 output before
  allocating its XML tree's image attributes.
- `MaximumExpandedBytes` now also bounds aggregate PDF filter-decoding work before
  provider expansion. Flate and LZW streams are counted before full materialization;
  RunLength streams are counted before decoding. Filter-chain stages and repeated
  decodes are charged separately, as are predictor scanline buffers and LZW dictionary
  sequences. Native image filters have a declared-dimension admission check; these
  checks are still not a process-memory guarantee. Full LZW dictionaries must clear
  before further additions, matching the bounded provider profile. Unfiltered bytes
  remain subject to the input-byte limit.
- DOC property applications, including cached indirect references, share a
  `MaximumExpandedBytes` work budget. Nested indirect properties are rejected;
  one-level references are parsed once per Data-stream offset. DOCX style results
  are cached separately for paragraph and character styles, copied before direct
  overrides, and backed by a property-work limit. Note readers share immutable
  definitions and parse relationships once per note part. Bookmark names use a set.
  Cached style/resource diagnostics are emitted on first resolution; placement-specific
  checks and diagnostics still run for each placement.
- RTF font names are limited to 256 characters and materialized once per font
  record. KF8 uses direct array copies on all targets, checks cancellation for every
  skeleton, rejects overlapping/out-of-order skeleton ranges (including fragment
  bytes), and bounds cumulative fragment-shifting work by `MaximumExpandedBytes`.

All admission failures apply in every fidelity mode and leave staged caller output
untouched. Configure the two new positive limits before sharing options across calls.
Hosts can increase them deliberately for larger trusted documents.

`eng/test-import-resources.ps1` runs the adversarial regression class in a separate
process under a 60-second, sampled 512-MiB working-set guard and verifies a fresh,
complete xUnit report. Use `-Framework net8.0` or `net10.0`; `net472` requires Windows.
The corpus includes the 1-GiB Flate content stream, repeated 16-MiB DOCX image,
exponential/linear DOC property shapes, large styles/notes, RTF fonts, late KF8
skeletons and a newline-only input at the default character limit. The guard samples
process peaks every 50 ms and is a regression check, not an OS-enforced sandbox.
