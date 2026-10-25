# Roadmap #99 review and release assessment

Assessment date: 2026-10-25. PR: [#100](https://github.com/kirakosyan/word.net/pull/100).
This receipt supplements the [original implementation validation](roadmap-99-validation.md);
it does not replace the published 1.3.0 or pre-review measurement receipts.

## Review corrections

All 15 review findings have corresponding corrections:

- Default BestEffort conversion again reports omitted structural revision marks instead
  of failing. Unsupported preserved story/nested structural owners receive
  `Review.Unsupported` before records are registered.
- Nested DOC/DOCX instructions preserve their caches without evaluating truncated
  instructions or emitting literal braces as a live field/hyperlink. Unevaluable,
  formatted and multiparagraph scalar caches retain informational diagnostics in Strict.
- Formula display removes insignificant zeros and limits precision to 15 significant
  digits. IF string comparisons are case-sensitive; wildcard patterns retain caches.
- Accept/reject treats a whole-paragraph mark and its text as one change. Removing
  the final tracked row removes the table instead of leaving an invalid empty table.
- Balancing probes keep full-sized lines, including Lenient mode. All section bounds
  are probed together in at most 16 whole-document passes plus a final layout, instead
  of up to 16 passes per section. Section-relative frames preserve page allocation;
  conservative fallback remains possible under coupled page constraints.
- Header footnote capacity deducts repeated headers only for body-row groups.
- Chart binding checks all containing table/row controls. Public DOCX import already
  rejects objects within controls; both table and nested-row cases have regression tests.
- Structural comparison writes each input once for block signatures instead of writing
  a package per block. PDF reconstruction shares lazily initialized font metrics across
  pages and skips kerning/GPOS parsing for metric-only reads.

Regression coverage includes nested simple/complex instructions and binary DOC field
control characters; Strict cache formatting/paragraph ownership; every whole-paragraph
accept/reject combination; final-row removal; story diagnostics; a 1,500-paragraph
comparison; 16 balanced sections; all three fidelity modes; and header footnote capacity.
The PDF import changes also run through the existing independent reconstruction fixtures.

## Validation

- Locked restore with dependency auditing enabled passed.
- Release solution build passed for all five core targets, with zero warnings/errors.
- Formatting verification passed (the workspace loader emitted its existing warning).
- Core, Fonts and OCR packages packed successfully with their required framework assets,
  README and license notices. These are isolated local validation artifacts, not releases.
- APICompat explicitly ran against the published **1.3.0** core archive and found no
  breaking changes. Its SHA-256 is
  `094e21ccbbd3d0d2c3618de4c6c73fbd6511341c36f0ab6f4abc83b4de6e9cfd`,
  repository commit `c723fb8aa4ed70fda5562da7f472e6fef54740b7`.

- Linux x64 full conversion/OCR suites: **1,139 passed, zero skipped** on each of
  .NET 8.0.32 and .NET 10.0.12 (28 additional cases). Hosted gates now require 1,139.
- Independent PyMuPDF validation passed on regenerated native table/story ownership,
  chart/equation alternatives and vector output.

These local results cover the reviewed working tree before its commit. The isolated
local packages retain pre-commit repository metadata and are not publication artifacts.
Fresh hosted CI must qualify the pushed commit; no pre-review CI result automatically
qualifies this corrected source. See the PR checks for its exact run and commit.

## Publication decision

**Not ready to publish as-is.** The changes are candidates for a new minor release,
provisionally **1.4.0**, because they add public APIs and supported conversion profiles.
All three project versions and their package release notes still identify the already
published 1.3.0. No version bump, merge, tag or publishing dispatch has been performed.

Before publication:

1. Select a new synchronized package version, prepare its release notes/README, and
   repeat compatibility validation for that final candidate against published 1.3.0
   as well as the existing historical compatibility gate. Describe the pagination changes and bounded profiles.
2. Complete review and merge. Obtain successful CI on that exact final-main commit,
   including all Windows/Linux x64/ARM64 source and immutable-package lanes, .NET 8/10,
   and the actual Framework 4.7.2 legacy-JIT source/package gates. PR CI is useful evidence
   but cannot replace the final-main artifact required by the release workflow.
3. Retain package hashes, runtime reports and release evidence, then explicitly authorize
   the separate publication workflow described in [releasing](releasing.md).

The retained independent comparison corpus still has 24 failed reference verdicts at
its original tolerances. The original receipt separates these from the 24 passing
feature-specific checks; this review does not turn those failures into passes or make
an unrestricted Word-rendering parity claim. Release claims must remain within the
[documented source profile](roadmap-99-implementation.md).
