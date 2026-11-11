# 1.6.1 — bounded document import resource use

`wordnet`, `wordnet.Fonts` and `wordnet.Ocr` advance together to 1.6.1. This patch
addresses [issues #175–#181](https://github.com/kirakosyan/word.net/pull/182).

## Fixes

- Bound DOC indirect-property fan-out and repeated property applications.
- Share validated image bytes across repeated DOCX, DOC and EPUB placements;
  bound placement counts, encoded bytes and HTML image-output expansion.
- Cache RTF font names and reject oversized names before run expansion.
- Cache DOCX styles and share note/story definitions, table-style caches and
  relationship maps; bound property work and use set-based bookmark lookup.
- Use direct KF8 range copies on every runtime, reject empty/overlapping ranges,
  check cancellation per skeleton and bound fragment-shifting work.
- Bound PDF filter expansion before full allocation, including document-level
  streams; use a separate per-page image allowance so ordinary multipage scanned
  PDFs remain readable. Preserve empty Flate streams and the existing parser depth.
- Split text incrementally and bound imported blocks and runs before model growth.

## Compatibility and limits

New positive, per-conversion snapshots on `ConversionOptions` are `MaximumBlocks`
(default 131,072), `MaximumRuns` (262,144) and `MaximumImagePlacements` (10,000).
Existing constructor signatures and all five library target frameworks remain.
The stricter limits apply in every fidelity mode; applications processing larger
trusted documents can raise them deliberately. RTF font names are capped at 256
characters. PDF LZW dictionaries must clear before further additions when full.
See [exact accounting and diagnostics](https://github.com/kirakosyan/word.net/blob/v1.6.1/docs/hosting.md#import-resource-fixes-in-161).
These checks do not impose a hard process-memory ceiling.

The [1.6.0 feature profiles](https://github.com/kirakosyan/word.net/blob/v1.6.1/docs/releases/1.6.0.md) remain in effect. Windows/Linux x64/ARM64 are
the desktop release scope; actual Framework 4.7.2 x64 requires the documented legacy
JIT. Actual Framework 4.6.2, Android/macOS, additional OCR languages, encryption and
signatures remain unqualified or deferred.

## Verified publication — 2026-11-11

Published **1.6.1** for [wordnet](https://www.nuget.org/packages/wordnet/1.6.1), [wordnet.Fonts](https://www.nuget.org/packages/wordnet.Fonts/1.6.1) and [wordnet.Ocr](https://www.nuget.org/packages/wordnet.Ocr/1.6.1), with all three symbol packages accepted by NuGet.

- Source: [`e2dadeaef361aa1651bf37d1c9325d43de4d4bb2`](https://github.com/kirakosyan/word.net/commit/e2dadeaef361aa1651bf37d1c9325d43de4d4bb2), merged in [PR #182](https://github.com/kirakosyan/word.net/pull/182), closing #175–#181.
- [Final-main CI 43083836263](https://github.com/kirakosyan/word.net/actions/runs/43083836263) and [publication 43091742624](https://github.com/kirakosyan/word.net/actions/runs/43091742624) passed. The same immutable CI archives were published; there was no publication-time rebuild.
- All **2,511** conversion/OCR cases passed in every desktop source, native-package, .NET Standard fallback and publication lane, with no failures or skips. Independent validators, malformed-input/concurrency campaigns, API compatibility, audit, formatting and performance gates passed.
- All **41** import-resource regression cases passed the unchanged 60-second/512-MiB worker guards on Linux x64 (.NET 8/10) and Windows x64 (.NET 8/10/Framework). Largest sampled peak: **342.08 MiB**; longest worker: **7.87 seconds**.
- Fresh actual **Framework 4.7.2 x64** source and package guests each passed all 2,511 cases with release key **461814**, verified legacy JIT and exact binary hashes. Package guest DLLs match the published artifact. Hosted VM, container, network, disk/state and owned-image cleanup receipts are verified.
- Public repository-signed NuGet downloads preserve every original payload entry from CI. A clean public-only consumer verifies Text → DOCX → PDF → DOCX → Text on .NET 8/10 and matches the loaded core/fonts/provider DLL hashes to the tested archives.

The six package/symbol assets below are the original immutable CI archives. NuGet repository signing changes the public archive hashes; the evidence includes both hashes and payload comparisons. `wordnet-1.6.1-qualification.zip` contains the exact-commit test reports, minimum-runtime and cleanup evidence, publication receipts and public-consumer results. `SHA256SUMS.txt` covers the release package and qualification assets. Local post-publication cleanup is recorded in the task evidence. The GitHub asset-upload workflow is recorded separately from source qualification.
