# wordnet implementation and delivery status

## 1.4.0 roadmap #99 profile

Version 1.4.0 extends the 1.3.0 profiles with the bounded changes in
[roadmap-99-implementation.md](roadmap-99-implementation.md): direct HTML/ODT PDF export,
chart binding, scalar fields, structural comparison and qualified layout/native/object
combinations. The [1.4.0 release notes](releases/1.4.0.md) record compatibility and
qualification requirements. Earlier release receipts below remain historical evidence.

## Released 1.3.0 — #64–#73 and revised #82

Release 1.3.0 implements validated chart/OMML graphs, DOCX preservation,
managed vectors and MATH metrics/layout, immutable object editing/removal, Unicode
17 bidi and opt-in Hindi Devanagari, and a qualified ordinary native tagged/UA
profile. The [profile](p2-objects-typography.md) and machine-readable
[capability matrix](p2-capability-matrix.json) specify the limits and unsupported
combinations. The executable P2 recipe composes a managed object report and exports
separate native chapters. Publication completed on 2026-10-20 after review, issue
reconciliation and exact-main production gates; new platform claims remain excluded.
The [release record](releases/1.3.0.md) identifies the tag, public packages and evidence.
Crypto issues #74–#81 are owner-cancelled, closed as not planned and absent.
Final source/package/CI evidence belongs to the release record and P2 PR; no completion is inferred
from successful compilation or the older P1 results.

## P1 delivery — issues #48–#63, PR #85

The owner assigned the next P2 chart/equation and typography tracks (#64–#73) and
the revised integrated checkpoint #82. Encryption and digital signatures (#74–#81)
were explicitly cancelled on 2026-10-19 and closed as not planned, not completed.
Those features remain absent. P2 implementation/evidence belongs to its separate PR.

The owner requested all sixteen P1 leaf issues from roadmap #45. PR #85 implements
columns (#48), bounded reporting/body repeats (#49–#52), ODF package/mapping/exchange
(#53–#57), and review identity/preservation/decisions/recording/Unicode/comparison
(#58–#63), following their hard dependencies. It merged on 2026-10-19 as
`73f935732a788c69a934edfa5d71f42635e02031`, with tree equality to reviewed head
`d733a755663d776c4fa119ef18e599f3f3559b78`. All fifteen review threads are resolved,
all final-head CI jobs passed, and all sixteen issues closed automatically. P1 is
included in the published 1.3.0 packages alongside P2 PR #86. The dated
qualification below describes the earlier P1 source checkpoint.

Concrete profiles are in [report preprocessing](report-preprocessing.md), [ODT](odt.md)
and [review workflows](review-workflows.md). Qualification and platform limits are in
[testing](testing.md). Each leaf and roadmaps #45/#32 record the merged evidence
and residual profile limits. Historical evidence below remains tied to its original
revisions.

## Historical P0 adoption delivery — merged PR #83, 2026-10-19

PR [#83](https://github.com/kirakosyan/word.net/pull/83) merged as `6cf54fd2a406880feb18abb1ee495b8c77bcc3e8`; its tree matches reviewed head `e8e6cbfada0f261d3153934f730ec3b3f42be870`. All fifteen review threads were answered and resolved and all final-head push/PR checks passed. Issues [#46](https://github.com/kirakosyan/word.net/issues/46) and [#47](https://github.com/kirakosyan/word.net/issues/47) are complete and closed; roadmap [#45](https://github.com/kirakosyan/word.net/issues/45) retains the remaining unassigned work, beginning with #48. The broader #46–#52 adoption checkpoint is still incomplete; no P1 task is started by this reconciliation.

The existing corpus now has 24 independently authored fixtures, including twelve new business cases. Linux-only office capture pins fonts and records real references separately from authored expectations. Ranked findings retain every failed document/engine verdict; the [comparison report](comparison-p0.md) records twelve strict-policy differences without relaxing tolerances or changing the converter. The section-transition fixture now changes one column to two, and reference page-size unit rounding is identified explicitly. Licensed desktop Word/Aspose remain unavailable.

The invoice/report recipes use the existing public API, caller images and repeat/if controls. Application code rounds line amounts before summing; logs explicitly describe cumulative diagnostics. Staging/cancellation/cleanup and real replacement are independently tested. The required conversion/OCR suite is **855**, including fourteen recipe cases; thirteen Python tests cover comparison/capture contracts. [Testing evidence](testing.md) identifies the exact source/package/runtime checks and their limits.

This is merged source, separate from published packages. No new engine API, dependency version, target framework, platform claim, release tag or NuGet publication is added. The older presentation-patch and release descriptions below retain their dated evidence; the concise README delivered in #83 supersedes their earlier header layout.

The 1.2.1 presentation patch replaces the README's HTML-sized OS icons with six
consistent Markdown badges, including readable Windows/Linux x64/ARM64 labels.
It versions all three packages together and compares APIs against published 1.2.0;
the converter profile remains the same. PR #43 merged as
`9f5e85af69242d344cce2e5a7e590aa6971db497`, with tree equality to
`c46ee0a447b295a326a41bb8fa84e30225320428` and all 24 final-head checks passed.
All four source/package actual-4.7.2 reports passed 829/829 with verified cleanup;
all 24 job logs are free of the Node.js 20 warning. The documentation-only follow-up
corrects NuGet's stripped relative file links and records this reconciliation.
Publication remains pending final reconciled-main qualification. Use the live
[roadmap record](https://github.com/kirakosyan/word.net/issues/32) for its exact
SHA/run/artifact selection; see [patch release notes](releases/1.2.1.md).

Reconciled 2026-10-18 against merged PRs #33–#42 and the verified public 1.2.0 release.
Publication and minimum-runtime evidence remain tied to their exact release commit.
The live
[roadmap #32](https://github.com/kirakosyan/word.net/issues/32) and linked issue
acceptance criteria track delivery. This page records the source state at that
reconciliation; package publication and runtime qualification have separate gates.

CI maintenance upgrades the pinned checkout, .NET setup and artifact actions to
releases declaring Node.js 24 across all four workflows. NuGet/login already declares
Node.js 24. SDK roll-forward is disabled to preserve the declared 10.0.401 toolchain
and Android lock-file inputs. Publication triggers, least-privilege permissions and
exact-artifact gates remain unchanged; see [workflow validation](testing.md#github-actions-runtime-maintenance--2026-10-18).
The 1.2.0 publishing workflow succeeded at release commit `3202a1f`; all three public
packages' 11 DLLs match its qualified artifact. A fresh public-only restore/build
passes; see the [ledger](release-1.2.0-readiness.md).

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
which passed all 20 checks. At that reconciliation the required suite was **829**; the 828-case
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

1. Preserve the [verified 1.2.0 release evidence](release-1.2.0-readiness.md).
   All three packages are published and publicly verified. P1 issues #48–#63 merged
   in [PR #85](https://github.com/kirakosyan/word.net/pull/85). The assigned next
   checkpoint is qualification/review of P2 #64–#73 and revised #82 in
   [PR #86](https://github.com/kirakosyan/word.net/pull/86); cryptographic features
   #74–#81 are cancelled. The bucket 5
   [merged design slices](later-design-decisions.md) remain bounded by their
   individual implementation and policy gates.
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
are verified. Published 1.2.0 versions all three packages and keeps the 829-case
suite/five core/OCR assets. All 26 scoped roadmap issues have checked acceptance;
#32 is closed as a completed bounded milestone. See [readiness](release-1.2.0-readiness.md)
for exact release/public-package evidence; future maintenance builds are distinct artifacts.

At the user's request to execute and clean up the Docker checks automatically,
PR #40 merged as `cd541f13cce3311a15a98e86fb9e656d9405e193`, with tree equality to
`242488b9399717044b1b9583b10b9d73b2cd5c7e` and all 24 checks passed. It adds
[hosted Windows VM qualification](hosted-minimum-runtime.md). Source and
immutable-package jobs enforce actual Framework 4.7.2, legacy JIT and all 829 cases,
retain evidence and verify owned VM/container/image disposal. Candidate push and
PR merge-ref source/package runs each passed 829/829 on actual release 461814 with
observed legacy JIT; all four evidence sets and cleanup receipts were independently
checked. All 26 scoped issues and roadmap #32 remain closed with checked criteria;
this release work adds no new runtime capability. PR #41 merged documentation as
`91838ebfa9c5eeef38716b72757b3b017d1ad347`; its tree equals `4b600e96f778f4f9252ba9beef37eb3d0ae1b4f3`,
which passed all 24 checks. Its main CI passed all 12 checks, and source/immutable-package
actual-4.7.2 suites each passed 829/829 with independently verified cleanup.
The README OS icons identify the existing Windows/Linux x64/ARM64 profile; support,
providers and runtime behavior are unchanged. Later main revisions require their own
exact-main package qualification. PR #42 merged as `3202a1f`; its 12 main checks and
source/package actual-4.7.2 runs passed. Publish run `39129534970` then published
that exact artifact; all three public packages and their 11 DLLs are verified.
See the [dated evidence ledger](release-1.2.0-readiness.md) and
[live publication record](https://github.com/kirakosyan/word.net/issues/32).
