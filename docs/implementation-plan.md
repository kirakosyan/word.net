# wordnet implementation and delivery status

Reconciled 2026-10-17 against merged PRs #33–#35. The live
[roadmap #32](https://github.com/kirakosyan/word.net/issues/32) and linked issue
acceptance criteria track delivery. This page records the source state at that
reconciliation; package publication and runtime qualification have separate gates.

## Current delivery

| Work | Delivery and evidence |
| --- | --- |
| Enterprise baseline, issue #4 | Delivered in PR #5 and the 1.1.0 release profile: managed PDF/HTML export, stories/fields, caller fonts and optional native PDF/OCR provider; see the [audit](enterprise-audit.md). |
| Bucket 1: measurement and adoption diagnostics | PR [#33](https://github.com/kirakosyan/word.net/pull/33) merged as `2a18874`; #6 and #24 closed. Twelve authored business documents, optional caller-provisioned Word/Aspose comparison, bounded preflight and location-aware diagnostics. |
| Bucket 2: business-document rendering and fidelity | PR [#34](https://github.com/kirakosyan/word.net/pull/34) merged as `1c44d97`; #7, #8, #9, #10, #16, #18 and #19 closed. #17's composition-collision acceptance landed in #35; all eight issues are closed. |
| Bucket 3: document editing and templates | PR [#35](https://github.com/kirakosyan/word.net/pull/35) merged as `0b2af843`; #11–#15 and #17 are closed with verified acceptance evidence. The merged source adds `WordDocument`, replacement/bookmarks, composition, scalar merge, repeating rows, conditions and image placeholders. See the [design](editing-template-design.md), [usage](editing-and-templates.md) and `examples/WordNet.Automation`. |
| Bucket 4: interoperability, typography, accessibility and import | Implementation candidate for #20, #21, #22, #23, #25, #26 and #27 on `codex/interoperability-typography-import`; architecture decisions preceded source. See [profiles/usage](interoperability.md). Review fixes add 33 regressions (828 total), passing locally on Linux x64 .NET 8/10 with independent readers/validators. Await exact-head CI and merge reconciliation before closure. |
| Bucket 5: later design decisions | #28–#31 remain design-only. ODT needs a bounded future profile; revisions/comments/comparison, encryption and chart/equation implementation remain deferred. |

Bucket 2's [supported profile](document-fidelity.md) and
[design decisions](business-fidelity-design.md) cover nested/decorated tables,
percentage widths/autofit, anchored/transformed rasters, rectangular text boxes,
continuous/parity sections and columns, multilevel numbering, page-bottom footnotes
and bounded TOC/cross-reference refresh. These are implemented source capabilities;
perfect Word pagination, arbitrary drawing layout and unrestricted field evaluation
are outside the profile.

Main commit `0b2af843a40b8c866119594a887e8dac1f5c7340` contains **696** required
conversion/OCR cases, including **86** authored editing/template cases. All 15 review
findings were addressed and resolved; all **20** CI check runs passed on reviewed
head `383ea15f1bb21d96f38a61a2eaee42980cae0124` before its expected-head squash merge.
The merged source tree is identical to that head. Both complete local Linux x64
.NET 8/10 suites passed without skips; package/API checks and the public sample pass.
See [testing](testing.md) for exact runs, independent readers, historical release
evidence and qualification limits. The source remains unreleased; this evidence
does not establish Word/Aspose parity or exact-main-package release qualification.

## Preserved contracts

- DOC and DOCX, including DOC ↔ DOCX; each Word format ↔ PDF, MD, TXT, EPUB,
  MOBI and AZW3; each Word format → XPS and HTML. XPS remains output-only. HTML/RTF input and RTF output are bucket 4 candidate additions.
- English printed OCR, with caller-provisioned native dependencies through
  `wordnet.Ocr`; handwriting and additional OCR languages remain deferred.
- Matching public APIs on `net462;net472;netstandard2.0;net8.0;net10.0`, with
  managed export and provider types outside the portable public surface.
- Windows/Linux x64/ARM64 desktop scope. Framework 4.7.2 x64 requires the
  documented legacy JIT configuration; actual 4.6.2 remains unqualified. Android,
  macOS and trimming/AOT require separate qualification before support claims.
- Caller-owned streams, cancellation checkpoints, bounded admission, staged output
  and explicit loss diagnostics. Host isolation is required for hard CPU/RSS limits.
- Independent MIT-authored implementation, published specifications, licensed
  dependencies and fixture provenance; no proprietary resources or implicit fetching.

## Remaining work

1. Review and merge the bucket 4 implementation candidate after independent qualification
   and full required checks; reconcile all seven issues and the roadmap after merge.
   [Documented limits](interoperability.md) remain explicit, including typography/UA
   combinations, complex table semantics and approximate reconstructed vertical flow.
2. Broaden independent rich-DOC/KF8/EPUB and scan corpora, viewer checks, malformed
   input campaigns and production-like resource measurements. The bounded binary
   [DOC profile](doc-format.md), ebook CSS and complex PDF reading order retain limits.
3. Preserve native desktop source/package/Standard lanes and refresh exact-package,
   actual-minimum-runtime evidence for each release. Older successful runtime reports
   do not qualify a new revision. Keep fonts, codecs, OCR and architecture explicit.
4. Complete the four later design issues within their design-only boundaries.
   Any deferred implementation needs a separate scope decision.

## After every merge

1. Verify the merged SHA, final review resolution and checks. Update the local main
   branch without overwriting user work; identify which acceptance criteria landed.
2. Reconcile linked issues: check only verified criteria, record the PR/commit,
   independent evidence and residual limits, and close completed issues. Keep partial
   issues open with a concrete remaining requirement. Verify automatic closing
   references took effect; a PR reference alone does not complete an issue.
3. Update roadmap #32's checkboxes, delivery table, dependencies and next bucket.
   Re-read issue state rather than trusting a stale roadmap snapshot.
4. Refresh this plan and affected README, architecture, capability, API/usage,
   diagnostic catalog, samples and testing evidence. Distinguish merged source,
   pending review and released packages. Preserve dated historical evidence and
   report checks that actually ran, skips and qualification limits.
5. Commit documentation changes through the repository's PR workflow, then proceed
   to the next bucket. Do not publish packages or change release credentials as part
   of this reconciliation. See the separate [release procedure](releasing.md).

For implementation, the locked restore, all-target Release build, complete applicable
conversion/OCR suites, formatting and package/API checks in [AGENTS.md](../AGENTS.md)
remain required. Documentation-only reconciliation uses diff, link and consistency
checks; it does not require rerunning unchanged conversion code.
