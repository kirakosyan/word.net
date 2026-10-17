# Bucket 5: later design decisions

Design deliverables for roadmap [#32](https://github.com/kirakosyan/word.net/issues/32),
authored 2026-10-17. Status: **proposed for review**. Issues #28–#31 close on these
designs and acceptance plans after merge; they do not authorize or deliver runtime
implementation. The existing 36-direction matrix, 829 required cases, Strict behavior,
five TFMs and released package 1.1.0 remain separate from these proposals.

| Issue / decision | Proposed first profile | Implementation gate |
| --- | --- | --- |
| [#28 ODT](https://github.com/kirakosyan/word.net/issues/28) / [B5-ODT](design/odt.md) | Managed ODF 1.3 text-package semantic subset, DOCX ↔ ODT only; styles/lists/page masters/simple tables/inline rasters and explicit losses. | Separate bounded implementation slices, package safety and independent ODF/office-reader qualification. HTML/RTF precedes it and is already delivered. |
| [#29 review history](https://github.com/kirakosyan/word.net/issues/29) / [B5-REVIEW](design/review-history.md) | Preserve supported DOCX text revisions/classic comments, explicit decisions, explicit recording and bounded text comparison as four capabilities. | Explicit deferred-scope change per stage; #11 editing foundation is delivered. Existing histories default to rejection during future comparison. |
| [#30 protection](https://github.com/kirakosyan/word.net/issues/30) / [B5-CRYPTO](design/docx-protection.md) | Explicit Agile AES-256-CBC/SHA-512 DOCX envelope utility with authenticated staging, caller passwords and bounded KDF. | Separate scope decision, specification vectors, expert crypto/security review and actual runtime/provider qualification. |
| [#31 objects](https://github.com/kirakosyan/word.net/issues/31) / [B5-OBJECTS](design/charts-equations.md) | Safe DOCX part preservation, static rendering and semantic editing as separate stages; two chart types and a small OMML subset. | Explicit stage-specific scope change; safe graph/workbook handling, independent visuals and external chart/equation editability control. |

The [fixture provenance and acceptance plan](design/acceptance.md) defines independent
synthetic producers, meaningful positive/negative assertions, external edit controls,
resource/cancellation/output-safety cases and the evidence required before advertising
support. It distinguishes future plans from executed tests and existing seed provenance.

## Shared architecture and compatibility decision

Build on the existing private semantic model, independent format adapters and atomic
`WordDocument` sessions. Preserve `net462;net472;netstandard2.0;net8.0;net10.0`, enum
values/constructor metadata and provider-neutral public APIs. Proposed APIs in these
documents are sketches, unavailable in source/packages and subject to normal API review.
Do not expose SDK/XML/native objects, implicitly fetch resources or introduce speculative
runtime dependencies. Managed BCL/ZIP/XML and the existing dependency graph are the
preferred starting point; future selections still require maintenance/license/TFM
review, locked dependencies, notices and actual execution evidence.

Revisions need range-aware node identities; charts need a narrowly validated retained
part graph. Neither can be bolted on as an untracked arbitrary XML bag. Extend cloning,
composition, resource admission, diagnostic inheritance and every affected destination
check before enabling edits. Encrypted-envelope operations sit outside ordinary
conversion; authenticating bytes does not imply their document features are supported.

No feature changes the ownership contract: caller streams remain open/current-position,
edits commit only after validation, output is staged and parsing/loss/cancellation
before final copy leaves the destination untouched. Final-copy cancellation/I/O can
leave partial bytes. Input/expanded/text/node/page/pixel/work limits are defense in
depth; hard CPU/RSS/deadline enforcement remains the host's responsibility.

Every new capability starts with bounded authored fixtures, external readers and
explicit losses. Preservation is not rendering, rendering is not editing, comparison
is not interpreting historical review intent, and encryption is not editing restriction
or signature validation. Unsupported/malformed/resource failures remain explicit.

## Implementation queue after design review

The issue-level slices in each ADR are the proposed task specifications; none is
started or assigned by this PR. Prioritize **ODT-1 → ODT-4** if format expansion is
selected next, since HTML/RTF and business/editing foundations are delivered and ODT
does not first require a deferred review/crypto/object scope change. This is a design
recommendation, not automatic authorization to implement deferred functionality or a
release promise. R1 → R2/R2b → R3 → R4, E1 → E2 → E3 → E4 and P1a → P1b → the
separate rendering/editing slices remain distinct alternatives.

Before each implementation, open a bounded follow-up issue using its ADR slice,
acceptance rows, dependency and docs/qualification gates. Record the selected profile
and any required AGENTS policy change in that PR. Do not reopen completed buckets
1–4 or treat a completed design issue as a runtime-support checkbox.

## Review acceptance trace

| Issue criterion | Design evidence |
| --- | --- |
| #28 architecture, API, safety/losses | B5-ODT decisions, pair gating, mapping and package admission. |
| #28 provenance, independent interoperability | Acceptance plan provenance/tool table and O-01–O-08, existing authored seed links. |
| #28 slices/assertions/docs, HTML/RTF ordering | B5-ODT numbered slices and prerequisite rationale. |
| #29 fidelity/privacy/ranges/API/bounds | B5-REVIEW model, identity, privacy, proposed overloads and work budget. |
| #29 distinct capabilities, existing-history comparison policy | R1–R4 table, decision semantics, RejectIfPresent and comparison acceptance rows. |
| #29 runtime unchanged / separate scope | B5-REVIEW scope boundary and this documentation-only diff. |
| #30 algorithms/threat/API | B5-CRYPTO allowlist/rejections, explicit utility, threat/resource model. |
| #30 independent negatives, lifetime/logging, unchanged runtime | Acceptance E-01–E-08; B5-CRYPTO lifetime/output safety and implementation gate. |
| #31 types/stages and external editable structure/visuals | B5-OBJECTS P1–P3 table; acceptance P1/P2/P3 external editor controls. |
| #31 embedded data/relationships/bounds and scope policy | B5-OBJECTS graph/workbook admission, no fetching, budget and stage-specific policy gate. |

## Validation and delivery status

This change is documentation only: no production/test/project/lock/fixture binary
changes, no added runtime support and no package publication. Validation comprises
review against the four live issue checklists and current implementation, published
specification/provider/license checks, relative-link/anchor and whitespace checks,
and a diff inspection confirming those boundaries. Future fixture plans are not
reported as tests that ran. See [testing](testing.md) for build-specific runtime evidence
and [implementation plan](implementation-plan.md) for merge/release reconciliation.
