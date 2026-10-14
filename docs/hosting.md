# Hosting untrusted document conversions

The library's byte, character, page, nesting and pixel settings are admission checks.
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
