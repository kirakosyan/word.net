# Roadmap #102 P6 qualification — preliminary source evidence

Issue #130 qualifies combinations and local packages after the leaf work is
accepted. This branch is based on the review head of [PR #136](https://github.com/kirakosyan/word.net/pull/136),
which has not merged. The evidence here is preliminary: it cannot establish the
final merged commit, close the leaf issues or qualify a published package.

The independent OOXML tests in `BusinessFidelityTests.P6.cs` combine a scalar
field, body comparison, review decisions, rotated text and vector output. They
inspect editable DOCX schema and PDF text/vector paths, and verify that archive
output rejects the unsupported drawing combination without writing output.
`EditingTemplateTests.P6.cs` combines a mixed report region, a page field and
review decisions, with independent DOCX and PDF inspection. Existing control
tests separately verify that typed controls require an explicit display view
before merge and that unsupported ownership fails without invoking caller data.
These are bounded combinations; they do not imply arbitrary cross-feature support.

The P4/P5 review follow-up passed local Linux x64 locked restore, five-target
Release build, format verification and the full .NET 8 and .NET 10 conversion
and OCR suites (1,484 cases each, no skips). Its local performance comparison
against merged P3 passed at 0.974, using 62 samples per revision. This is evidence
for the review head, not the final P6 revision. With the three preliminary
combination tests, the P6 source suites each passed 1,487 cases on .NET 8 and
.NET 10 with English OCR and no skips. Hosted final-head checks, actual
Framework 4.7.2 execution, final accepted-source package checks and reconciliation
of the leaf issues and roadmaps remain required before #130 can be completed.
No release, package publication, new runtime or platform claim is made here.

On the P4/P5 review head `e7928110fdb07da74d1902bbccbb863e845f2531`,
local Release packing with package validation enabled produced the following
SHA-256 hashes. The core and OCR packages contain all five target assemblies;
the fonts package contains its shared .NET Standard 2.0 asset. These artifacts
were generated locally and are not the published 1.4.0 packages.
`eng/verify-release-package.ps1` verified their source commit, target assets,
package identities, license and notices, and absence of native assets in these
archives.

| Package | SHA-256 |
| --- | --- |
| wordnet | `a68c3ea3433821210433a499b1149765e2a1224bc18dc7b5613353e096c2dbbc` |
| wordnet.Fonts | `3e1bd21d60d8f7431cab45e5caab59ef098a090d256cba0fa4372afeab21129c` |
| wordnet.Ocr | `2cb107eabd9951770f08b15e896651d6deb58951c1d614fdca748f919b8915f0` |

The local runtimes are .NET 8.0.33 and 10.0.12 on Linux x64. Against these
packages, the .NET 8 and .NET 10 consumer suites and the explicitly selected
.NET Standard 2.0 asset on each runtime each passed 1,487 cases with English OCR
and no skips. The latter .NET 10 run was restarted after interruption; the
framework-specific test assembly was restored and its hash verified afterward.
Actual minimum runtime checks remain pending for the final accepted source.

The last documented independent office comparison in the [P1 evidence](roadmap-102-p1.md)
retains 36 strict aggregate failures at unchanged thresholds. Original cases passed
reference per-page text in 19/24, glyph placement in 6/24 and raster comparison in
11/24; the 12 probes passed per-page text in 10/12, glyph placement in 3/12 and
raster comparison in 0/12. These are historical observations, not measurements of
this P6 head. Fresh independent reference results remain required before claiming
whether these gaps have changed.
