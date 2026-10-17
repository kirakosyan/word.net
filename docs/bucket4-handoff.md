# Bucket 4 completion status

PR [#37](https://github.com/kirakosyan/word.net/pull/37) squash-merged as
`44c9e3629f95ed2c984a325a384174303550cb53` on 2026-10-17. Its tree matches reviewed head `2af6a4358e7596a8d82046086c04374e96d7eea7`.
All 15 review threads are resolved and all 20 head CI checks passed. All seven
issues (#20–#23 and #25–#27) have checked acceptance criteria, merged evidence and
verified closure; roadmap #32 records completed buckets 1–4.

See [supported profiles/API](interoperability.md), [architecture decisions](interoperability-design.md)
and [build-specific qualification](testing.md). The 828-case suite includes 33 review
regressions; the CodePages dependency fix also passes four full local packed runs,
including explicit Standard fallback on both .NET 8/10. Merged source remains
separate from published 1.1.0 and historical qualification. No package was published.
Bucket 5 (#28–#31) remains design-only; deferred implementation requires its scope decision.

Documentation reconciliation and native font-storage follow-up merged in PR #38
as `2f16d47ac007bf1a5affb41bc0b7653f5198c9f8`, with 20 passing exact-head checks and a matching tree.
The additional compacting-GC regression brings the current suite to 829.
The [bucket 5 design proposals](later-design-decisions.md) are the pending next work;
ODT/review/encryption/object runtime support is not delivered by those documents.
