# wordnet implementation and delivery status

Reconciled 2026-10-17 against merged PRs #33–#39. Version 1.2.0 is a production
release candidate; publication and minimum-runtime gates remain explicit.
The live
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
| Bucket 4: interoperability, typography, accessibility and import | PR [#37](https://github.com/kirakosyan/word.net/pull/37) merged as `44c9e362`; #20–#23 and #25–#27 closed with verified acceptance. All 15 review findings resolved, all 20 checks passed on `2af6a43`, and the merge tree matches that head. 828 required cases, including 33 review regressions; independent readers/validators and exact packed fallback runs pass. See [profiles/usage](interoperability.md). |
| Bucket 5: later design decisions | [Four design deliverables](later-design-decisions.md) for #28–#31 merged in PR #39, with API/provider decisions, profiles, safety/resource limits, fixture provenance and staged acceptance plans. Runtime implementation remains deferred; design acceptance does not claim support. |

PR #38 merged documentation reconciliation and a native font-buffer lifetime fix
as `2f16d47ac007bf1a5affb41bc0b7653f5198c9f8`. Its tree matches head `e09c17c7ef43e38bcdf9717e535caa02b31d7812`,
which passed all 20 checks. The current required suite is **829**; the 828-case
PR #37 qualification remains dated historical evidence. See [testing](testing.md).
No package was published. Bucket 5 is merged design work; runtime implementation is deferred.

Bucket 2's [supported profile](document-fidelity.md) and
[design decisions](business-fidelity-design.md) cover nested/decorated tables,
percentage widths/autofit, anchored/transformed rasters, rectangular text boxes,
continuous/parity sections and columns, multilevel numbering, page-bottom footnotes
and bounded TOC/cross-reference refresh. These are implemented source capabilities;
perfect Word pagination, arbitrary drawing layout and unrestricted field evaluation
are outside the profile.

Historical bucket-3 commit `0b2af843a40b8c866119594a887e8dac1f5c7340` contains **696** required
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
  MOBI and AZW3; each Word format → XPS and HTML. XPS remains output-only. HTML/RTF input and RTF output are merged bucket 4 source additions, for 36 directed exchanges.
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

1. Complete the [1.2.0 release gates](release-1.2.0-readiness.md), including fresh
   actual-Framework-4.7.2 source and exact-main-package evidence, then publish through
   the existing workflow. Afterwards select a bounded follow-up from bucket 5's
   [merged design slices](later-design-decisions.md). ODT is the recommended next candidate; deferred
   review/encryption/object implementation needs a separate explicit scope decision.
   [Bucket 4 limits](interoperability.md) remain explicit: native
   typography/UA combinations, complex table semantics and approximate reconstruction.
2. Broaden independent rich-DOC/KF8/EPUB and scan corpora, viewer checks, malformed
   input campaigns and production-like resource measurements. The bounded binary
   [DOC profile](doc-format.md), ebook CSS and complex PDF reading order retain limits.
3. Preserve native desktop source/package/Standard lanes and refresh exact-package,
   actual-minimum-runtime evidence for each release. Older successful runtime reports
   do not qualify a new revision. Keep fonts, codecs, OCR and architecture explicit.

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

PR #39 merged the four design-only issues as `4eb9ed93aadbe48dee6ca482a39f81ff489f4171` after all 20 checks
passed on `d3961ec7980d34147188c105e531921d259eeecf`; tree equality and acceptance/closure
are verified. The 1.2.0 candidate versions all three packages and keeps the 829-case
suite/five core/OCR assets. All 26 scoped roadmap issues have checked acceptance;
#32 is closed as a completed bounded milestone. See [readiness](release-1.2.0-readiness.md) for current gates;
published NuGet remains 1.1.0 until publication and public-package verification finish.
