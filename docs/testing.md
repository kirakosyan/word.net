# Conversion tests and qualification

Pending #142 direct-border tests inspect editable WordprocessingML, fixed-page and
HTML/ODT color output, report-row expansion, disabled edges, destination losses,
conflicts and pagination rejection. A [pinned-font LibreOffice fixture](../tests/wordnet.Conversion.Tests/Fixtures/TableBorders/README.md)
independently checks color, grid geometry and reading order. It is not proof of
pixel-identical placement, conditional styles or untested runtimes.
The [exact-source border receipt](measurements/roadmap-138-table-borders-4f54e32.json)
records local suite and package checks, including the failed first pack attempt.
Additional direct table-shading tests cover editable markup, output colors,
report-row expansion and diagnosed unsupported combinations. LibreOffice did not
render the table-level shading in the retained probe; its separate direct-cell
control confirms color but does not qualify table-shading parity.
The [working-tree ODT probe](measurements/roadmap-138-table-shading-reference.json)
also passed pinned ODF 1.3 schemas and reopened in LibreOffice with the expected
fill and text. `eng/verify-table-shading-odt.py` checks the synthetic ODT against
locally supplied, checksum-pinned schemas without network access.
The [exact-source shading receipt](measurements/roadmap-138-table-shading-57481ba.json)
records local checks for that increment. Whole-table style tests additionally
check editable inheritance and edge resets, collision-safe composition,
report expansion, comparison and explicit unsupported combinations. Independent
LibreOffice fixtures confirm inherited border colors and a disabled inherited
edge, while the measured placement differences and shading-reference limitation
remain.
The [exact-source table-style receipt](measurements/roadmap-138-table-styles-771760a.json)
records the inheritance increment. Additional independently rendered first-row
[enabled/disabled look fixtures](../tests/wordnet.Conversion.Tests/Fixtures/TableStyleConditions/README.md)
check color, region and direct clear-cell precedence, editable inheritance and masks, explicit flag retention,
report expansion, comparison, and diagnosed conflicts and pagination. The
explicit-flag controls use the Office 2010 Open XML schema; tests do not infer
portable behavior for conflicting flags or absent look masks.
The [first-row exact-source receipt](measurements/roadmap-138-first-row-5cb70e4.json)
records its local qualification. A separate [region reference](../tests/wordnet.Conversion.Tests/Fixtures/TableStyleRegions/README.md)
checks 4×4 and 2×2 grids, horizontal/vertical bands, edge and corner precedence,
direct cell overrides and flags. It retains the independent conditional-style
inheritance mismatch and checks the resulting output loss. It checks editable
band-size inheritance and explicit loss for non-unit rendering. It retains
LibreOffice's non-default band-size mismatch and one-row/column observations;
these do not qualify those rendering cases or provide a Word execution.
The [exact-source region receipt](measurements/roadmap-138-table-regions-541aef9.json)
records the local build, suite, matrix and package results for that increment.
The default-style increment's first .NET 8 full run encountered a full temporary
filesystem; the [failure record](measurements/roadmap-138-default-5fc8215-disk-failure.json)
retains the 444 affected test names and messages and the original TRX hash. The
[exact-source receipt](measurements/roadmap-138-default-style-5fc8215.json)
records the successful complete retry, .NET 10 suite, matrix and package checks.
The independent [default-style controls](../tests/wordnet.Conversion.Tests/Fixtures/TableStyleDefaults/README.md)
record that LibreOffice rendered the explicit style but omitted its implicit
default. Tests also validate editable default identity, composition conflicts,
new-table behavior, comparison, invalid flags and untouched outputs. They do not
qualify default-style rendering in Word or unused style definitions.
Separate [theme-fill controls](../tests/wordnet.Conversion.Tests/Fixtures/TableStyleThemes/README.md)
retain LibreOffice's red-fallback observations for plain, tinted and shaded
conditional references. Tests check editable token/fallback retention, re-resolving
an edited theme, child overrides, malformed or missing references, comparison and
strict output rejection; they do not establish Word rendering parity.
The [exact-source theme-style receipt](measurements/roadmap-138-theme-style-a502c6c.json)
records the local suite, matrix and package results for that increment. Its first
hosted Framework source guest failed before startup because the disposable runner
had 37 GiB free and the VM admission check requires 60 GiB; no Framework tests ran
in that guest. A fresh-runner retry passed, as did the actual Framework source and
package guests in the push workflow; the failed attempt and both results remain in
the receipt. They qualify that intermediate head only.
Independent [conditional-border controls](../tests/wordnet.Conversion.Tests/Fixtures/TableStyleBorders/README.md)
check outer-edge colors, enabled/disabled look flags, selected corners and a
combined fill and border. Tests cover editable styles, inherited edges and resets,
composition, comparison, report expansion and explicit conflict losses. The
reference omitted the inherited edge; interior and competing-edge precedence,
pixel-identical geometry and Word execution remain unqualified.
The [exact-source conditional-border receipt](measurements/roadmap-138-conditional-borders-0567bf8.json)
records its local suite, matrix and package checks, including earlier failed attempts.

Pending #144 nested-control tests check all existing child kinds, parent/child
locks, local placeholders, explicit binding and conflicts, stale handles, simple
cell ownership, editable schema and detached display. A [Word-derived synthetic
nested fixture](../tests/wordnet.Conversion.Tests/Fixtures/ContentControls/NestedOwners/README.md)
and LibreOffice PDF independently check text order. This does not establish Word
execution, deeper nesting, nested structural or story owners, structural editing
or repeated-row identity.
The [exact-source nested-owner receipt](measurements/roadmap-138-nested-2fa1a71.json)
records the local suite, capability matrix and package checks.

Pending #144 plain story-owner tests check all six control kinds in top-level
header and footer placements, selected page variants, shared inherited stories,
IDs, binding conflicts, locks and cancellation. A [synthetic Word-derived story
reference](../tests/wordnet.Conversion.Tests/Fixtures/ContentControls/StoryOwners/README.md)
compares two-page LibreOffice text placement; it does not establish Word execution,
other story ownership or repeated report-row identity.

Pending #144 structural-owner tests preserve single plain row and cell wrappers,
check metadata, locking, display views, owner identity and protected operations,
and reject nested controls, merged cells and unsupported or malformed cell widths.
Two independently authored inputs and
[LibreOffice observations](../tests/wordnet.Conversion.Tests/Fixtures/ContentControls/StructuralOwners/reference.json)
check text and relative cell positions. They do not establish live Word execution,
structural mutation or repeated-row identity.
The [exact-source receipt](measurements/roadmap-138-structural-c9f11e6.json)
records local test, matrix and package evidence and both corrected focused-test failures.

Pending #147 object-fragment tests insert the independently authored P2 chart and
equation fixture into body and simple-cell bookmarks twice, inspect schema and
workbook bytes, and check independent edits and fixed output. Restyling, resource
limits and cancellation are checked for atomic failure. Additional native, tagged
or archival combinations remain unqualified.
The [reference receipt](measurements/roadmap-138-object-fragment-reference.json)
retains an initial LibreOffice Math omission and the successful rerun with its
matching Math component, plus chart-value, text, bar geometry and OMML observations.
The [exact-source receipt](measurements/roadmap-138-object-fragment-23c5069.json)
records the complete local suites, package checks and byte-identical reference probe.

The [Framework verifier failure record](measurements/roadmap-138-framework-41170714031.json)
retains a hosted failure where all 2,003 guest tests passed but the host compared
that total for equality with an older 1,925-test minimum. The corrected verifier
keeps the minimum and no-skip requirements and reports the actual test count.
Both [package-guest reports](measurements/roadmap-138-framework-411707-package-guests.json)
also record 2,003 passing cases with the same verifier failure; their original
hosted conclusions remain failures.
The later [Framework receipt](measurements/roadmap-138-framework-a34ef58.json)
records four successful 2,042-case source and package guests and distinguishes the
push head from GitHub's PR merge commit.
The following [PR guest failure](measurements/roadmap-138-framework-bc9995f-failure.json)
preserves an early VM exit without completed test evidence; it is not a Framework pass.
The [fresh retry and other guests](measurements/roadmap-138-framework-bc9995f-retry.json)
passed 2,065 cases each, while the original failure remains recorded.
The later [exact-head push receipt](measurements/roadmap-138-framework-fbe9a3d-push.json)
records both actual Framework guests passing 2,105 cases. Its separate PR Linux
[failure](measurements/roadmap-138-ci-fbe9a3d-linux-failure.json) remains recorded.
The [PR retry](measurements/roadmap-138-framework-fbe9a3d-pr-retry.json) passed
all jobs, including both actual Framework guests at 2,105 cases each. These
results do not qualify later source.

Pending typed-fragment tests check all six created kinds, nested parent/child locks,
global IDs, deterministic independent binding stores, repeated insertion, explicit
binding synchronization and editable DOCX. A Word-derived nested fixture checks
static text. Conflicting glossary resources, mismatched date policies, report
owners, restyling, resource exhaustion and cancellation remain explicit rejection
or failure cases.
The [independent binding reference](measurements/roadmap-138-typed-fragment-reference.json)
records schema checks, isolated store values and LibreOffice static text.
The [exact-source receipt](measurements/roadmap-138-typed-fragment-431dbfc.json)
records local suites, matrix and package checks and reproduces the independently
rendered DOCX with the exact source/package assembly.

Pending glossary-fragment tests exercise all six control kinds, repeated entry and
GUID remapping, placeholder restoration, compatible local resource retention,
conflicts and entry limits. The [independent reference](measurements/roadmap-138-glossary-fragment-reference.json)
checks generated package relationships, two distinct entries and LibreOffice text;
it does not establish live Word execution or unrestricted resource merging.
The [exact-source receipt](measurements/roadmap-138-glossary-fragment-6d219a1.json)
records complete local suites and package checks, and a separate four-resource
specimen with a restored styled placeholder and independent static text.

Pending mixed-fragment tests insert controls and charts/equations together into
body and simple-cell bookmarks, verify independent IDs and edits, and reject shared
paragraph and structural owners. The [reference receipt](measurements/roadmap-138-mixed-fragment-reference.json)
records editable XML/workbook values, reference chart geometry and text, as well
as a different default text-extraction order. It does not qualify tagged, archival
or native output or live Word editing.
The [exact-source receipt](measurements/roadmap-138-mixed-fragment-fb29555.json)
records the complete local suites, matrix and package checks and reproduces the
independently rendered DOCX with the exact source/package assembly.

The [performance observation](measurements/roadmap-138-performance-a34ef58.json)
retains a hosted push failure at a 1.332 candidate/reference ratio against the
unchanged 1.25 limit. A separate PR job passed against a different baseline and
runner. The difference between the two push reference workers suggests variability
but does not establish its cause or erase the failed gate. A local repeat with the
same source revisions passed the unchanged gate at a 0.855 ratio; its four full
measurement reports are retained with the original hosted observation.

Pending #143 character-style tests cover inherited/overridden color, invalid style
graphs and an additional Word-derived date placeholder. The independently generated
[LibreOffice observation](../tests/wordnet.Conversion.Tests/Fixtures/ContentControls/OfficeWordVariants/date-style-reference.json)
checks text and color. Character-style identity is lost with a diagnostic in this
bounded increment, and strict mode rejects it.

Pending #145 creation tests exercise all six kinds, local binding, source and saved
OOXML, editing, selection and date modes, immutable inputs, Unicode offsets,
locks, unsupported placements and rollback. Removal tests check preserved run formatting,
deletion versus preservation, lock combinations, nested parents, stale/foreign
handles, shared stories, table-cell schema, local resource retention and
cancellation. Pending block-creation tests exercise all six kinds, saved schema,
existing local bindings, locks, IDs across typed/template owners, fragment copying
and atomic rejection. The [independent form observation](measurements/roadmap-138-block-controls-reference.json)
checks direct block owners, IDs, static values and LibreOffice text; cell/story
block creation and broader ownership remain outside this increment.
The [exact-source control receipt](measurements/roadmap-138-controls-1c0dce7.json)
records the full local suites, matrix, package checks and independent form output.

Pending #159 source work has a [table formula test profile](table-formulas.md):
independently authored DOCX and LibreOffice numeric checks, schema and fixed-output
inspection, cultures, graph failures, atomic caches and report/edit refresh. The
independent probe's percentage-picture limitations are recorded with the fixture;
it does not qualify a public package or untested runtime.

Pending #158 body-bookmark cross-tab tests check editable grid and totals,
surrounding text, multi-page header repetition, the destination section's width,
unsafe references, nesting, reentrancy and cancellation. The [independent reference](measurements/roadmap-138-crosstab-placement-reference.json)
checks hand-calculated cells and LibreOffice text and geometry. It does not qualify
cell destinations, repeat-region insertion or unrestricted report nesting.
The [exact-source receipt](measurements/roadmap-138-crosstab-placement-c0c31aa.json)
records the complete local suites, package checks and reproduced reference document.

The separate pending #158 cell profile checks fixed and inherited explicit
margins, surrounding content, multi-page values, missing or ambiguous margins,
unsafe owners and atomic cancellation. Its [independent comparison](measurements/roadmap-138-crosstab-cell-reference.json)
retains the implicit-margin mismatch and checks the qualified explicit-margin
DOCX, PDF text and selected column origins against LibreOffice. Nested header
repetition and broader cell ownership remain unqualified.
The [exact-source receipt](measurements/roadmap-138-crosstab-cell-96ca2fa.json)
records both complete local suites, package checks and the repeated specimen.

Pending #151 native merged-table tests exercise Hindi clusters, horizontal and
vertical cells, repeated headers, pagination and atomic rejection. The independent
[reference observation](measurements/roadmap-138-native-merged-reference.json)
checks text and cell ownership while retaining the row-spacing and Arabic
differences. It does not establish right-to-left, tagged, archival or strict parity.
The [exact-source receipt](measurements/roadmap-138-native-merged-a91b49d.json)
records complete local suites, package verification and the repeated specimen.

Pending #152 native scalar-field tests exercise refreshed Hindi and Arabic values,
invariant scalar expressions, editable field retention, aggregate bounds and
atomic rejection. The [independent reference](measurements/roadmap-138-native-fields-reference.json)
records the Hindi vertical and Arabic text-extraction failures at unchanged
tolerances; both are explicit losses and Strict rejects them.
The [exact-source receipt](measurements/roadmap-138-native-fields-bc157ce.json)
records the complete local suites, package checks and reproduced specimens.

Pending #153 tests standalone equations beside Hindi and Arabic native text,
SVG vectors, malformed objects, cancellation, resource bounds and unsupported
owners. The [independent reference](measurements/roadmap-138-native-equation-reference.json)
retains the fraction geometry and Arabic extraction failures, the reference math
font difference, and the initial missing-Math-component failure. Notes and charts
remain outside this increment.
The [exact-source receipt](measurements/roadmap-138-native-equation-4787103.json)
records complete local suites, package checks and repeated independent specimens.

Pending #154 text-box tests exercise explicit anchor ordering, links, multiple
boxes, page selection and unsupported semantic owners. The independent
[reference observation](measurements/roadmap-138-accessible-box-reference.json)
records source/saved DOCX schema checks, pinned veraPDF, ParentTree and reading-order
inspection and four corruption controls; it is not a screen-reader study.
The [exact-source receipt](measurements/roadmap-138-accessible-box-42524ca.json)
records complete local suites, package checks and the repeated specimens.
The pending Linux workflow runs these checks with test-only pypdf 6.10.0 and
retains the generated evidence; its hosted result requires a separate run.

Pending #155 tests described and explicitly decorative vectors across all six
admitted geometries, page selection, repeated figures, fill and stroke, and
separately opted-in text boxes. The [independent observation](measurements/roadmap-138-accessible-vectors-reference.json)
records independently authored DOCX, schema and LibreOffice geometry checks,
PDF/UA validation, ParentTree and source-order checks, and negative controls.
The [exact-source receipt](measurements/roadmap-138-accessible-vectors-7e3876e.json)
records both complete local suites, the capability matrix and package checks.
The pending Linux workflow validates the generated corpus; hosted execution,
screen-reader behavior and other drawing combinations remain open.

Pending #156 tests PDF/A-2a and PDF/UA-1 together for bounded managed text,
tables, links, alpha images, chart/equation alternatives and separate positioned drawings.
The [independent observation](measurements/roadmap-138-accessible-archive-reference.json)
records both validators, metadata/font/ParentTree checks and separate corruption
controls. The archive validator alone accepted a missing-language control; the UA
validator rejected it, so neither verdict substitutes for the other. Notes,
mixed object/drawing ownership and native output remain unqualified.
The [exact-source receipt](measurements/roadmap-138-accessible-archive-9cdbe58.json)
records both complete local suites, the capability matrix and package checks.

Pending #160 uses five synthetic python-docx/Pillow drawings to inspect repeated
editable saves, vector bounds, selectable text, image pixels and PDF links in
PDF/XPS/SVG. Pinned PyMuPDF and an offline LibreOffice reference measure frame
geometry at 0.5 points and retain separate text-origin and direction verdicts.
All five reference text positions differ by more than 0.5 points; the diagnostic
is required rather than treating these specimens as a parity pass. The
[observation](measurements/roadmap-138-rotation-reference.json) retains each
measurement, file hash and earlier failed checks. Six corrupted
controls test angles, reference selection, diagnostics, transforms, links and
image identity. Flips, upright intent, page edges, cancellation and autofit
boundaries also have targeted tests. Other office implementations and hosted
runtime results require separate evidence.
The [exact-source receipt](measurements/roadmap-138-rotation-8737a8a.json) records
both complete local suites, package and API checks, repeated samples and the
[local performance comparison](measurements/roadmap-138-rotation-performance-8737a8a.json).

Pending #161 tests default rectangle, triangle and diamond presets, editable
alternative metadata, PDF/XPS/SVG output, invalid adjustments and output bounds.
The [independent observation](measurements/roadmap-138-preset-shapes-reference.json)
compares authored polygon vertices and edges with LibreOffice at 0.5 points and
includes substituted-geometry controls. Groups, arrows and broader appearance
and accessibility combinations remain open.
The [exact-source receipt](measurements/roadmap-138-preset-shapes-6b4b3a8.json)
records complete local suites, package checks and the repeated specimens.

Pending #162 checks independently authored DrawingML gradients across four
cardinal directions, five filled shapes and local-theme stops. Pinned readers
inspect editable DOCX, PDF axial shading/clipping, XPS and SVG brushes, and sample
PDF/XPS colors against an offline LibreOffice reference. Locally an independent
Inkscape raster also checks SVG. The [observation](measurements/roadmap-138-gradients-reference.json)
retains an initial invisible-XPS failure, its corrected stop container, a failed
remapped-accent reference comparison and corruption controls. Unsupported effects, alpha, other angles and conformance
combinations are not qualified by these specimens.
The [exact-source receipt](measurements/roadmap-138-gradients-5549be0.json)
records the complete local suites, package checks and repeated independent files;
hosted exact-head and actual Framework qualification remain separate.
The later [PR source-guest failure](measurements/roadmap-138-framework-6d021ea-pr-source-failure.json)
occurred during its OCR download before tests began and is retained independently
of any retry or push-side guest results.
The [separate successful guests](measurements/roadmap-138-framework-6d021ea-successes.json)
passed 2,253 cases each on actual Framework 4.7.2 for push source/package and
PR-merge package, with verified cleanup and exact binary/artifact identities.
The [PR source retry](measurements/roadmap-138-framework-6d021ea-pr-source-retry.json)
also passed 2,253 cases with verified cleanup; the original setup failure remains.

Pending #171 tests compare body merge-path findings with actual successful and
missing-data merges, and exercise types, parent declarations, repeat uncertainty,
privacy, input bounds, stream ownership, cancellation and schema snapshots. They
do not qualify reusable plan identity, output stages or other owners.
The [independent observation](measurements/roadmap-138-template-inspection-reference.json)
checks four synthetic templates against actual merges, an independent DOCX reader,
Open XML schema validation and deliberately corrupted reports; it also preserves
the initial diagnostic-fixture and framework-analyzer failures.
The [exact-source receipt](measurements/roadmap-138-template-inspection-0230fcb.json)
records both complete local suites, the matrix, package verification and repeated
independent examples. Its [performance record](measurements/roadmap-138-template-inspection-performance-0230fcb.json)
passes the unchanged gate but notes substantial host timing drift between reference
rounds. Hosted checks for this newer source remain separate.

Pending #165 tests all 19,338 unmodified Unicode 17 default line-break cases,
grapheme-safe use in managed PDF, nonbreaking spaces, font and owner rejection,
local dictionary interaction and cancellation. Four independently authored and
schema-checked DOCX specimens use OFL-licensed, reproducibly subset test fonts;
PyMuPDF, pypdf and offline LibreOffice compare selectable text and glyph origins at
0.5 points. Plain Japanese and traditional Chinese pass; Japanese punctuation
differs by up to 1.2 points and the simplified Chinese numeric-wrap sample differs
by 44.7 points horizontally and 16.01 vertically. Six corrupted controls verify
the checks. The [reference receipt](measurements/roadmap-138-cjk-reference.json)
retains both differences, the CFF source-face failure and earlier test failures.
The [exact-source receipt](measurements/roadmap-138-cjk-5dd89f1.json) records
the complete local suites, capability matrix, package checks and repeated
independent specimens. Its [performance evidence](measurements/roadmap-138-cjk-performance-5dd89f1.json)
retains the first failed harness build and the measured comparison. Hosted
exact-head and actual Framework qualification remain separate.
Other office implementations, broader fallback, grapheme shaping and output profiles
remain unqualified.

Pending #164 tests copied local dictionaries, explicit language and limits,
retained editable/searchable text, visible hyphen outlines, unqualified profiles,
pagination, cancellation and strict loss behavior. The [independent observation](measurements/roadmap-138-hyphenation-reference.json)
checks a python-docx source against Pyphen's US English points, three PDF readers,
font outline metrics and corrupted controls. The justified LibreOffice reference
retains the 0.58-point origin and 2.26-point following-line differences at the
unchanged 0.5-point tolerance. The first-line-only search highlight and broader
typography and full #164 acceptance remain open.
The [exact-source receipt](measurements/roadmap-138-hyphenation-00bf1f8.json)
records both complete local suites, the matrix, package checks and independent
specimens. The [performance record](measurements/roadmap-138-hyphenation-performance-00bf1f8.json)
retains an initial failed ratio and a passing repeat with substantial timing drift;
hosted exact-head performance and Framework execution remain separate gates.

Pending #167 preview-layout tests check opt-in text privacy, selected pages,
immutable snapshots, glyph boxes, source-located diagnostics, unsupported owners
and resource limits. The [independent observation](measurements/roadmap-138-layout-snapshot-reference.json)
compares SVG glyph geometry using fontTools and bounds it within Poppler PDF word
boxes. It does not qualify cell, object, native or review-owner identities.
The [exact-source receipt](measurements/roadmap-138-layout-snapshot-5b6eb7e.json)
records the complete local suites, package checks and reproduced reference geometry.

Pending #145 block-control creation covers six typed kinds, initial values and
locks, local bindings, wrapper removal, fragment isolation and protected-owner
failures. Its [independent reference](measurements/roadmap-138-block-controls-reference.json)
checks DOCX schema and LibreOffice-extracted values; the [exact-source receipt](measurements/roadmap-138-block-controls-e44ef15.json)
records complete local suites, package checks and the reproduced specimen. Cell
and story block creation remain open.

Pending #157 mixed-region tests cover three and five nested body levels, sibling
order, empty collections, scope lookup, retained control IDs, notes and atomic
reference/depth/row-limit failures. The independently authored [reference case](measurements/roadmap-138-deep-regions-reference.json)
checks saved control hierarchy, cloned bookmarks and complete PDF text with
LibreOffice and Poppler. The [exact-source receipt](measurements/roadmap-138-deep-regions-b9d16fd.json)
records both full local suites, package checks and the repeated specimen. Cell
repeats and broader ownership remain unqualified.
The [local performance receipt](measurements/roadmap-138-performance-8f6b5ea.json)
compares the same-source successor with its previous head under the unchanged
single-core gate; it does not erase the earlier hosted performance failure.

Pending #163 clustered horizontal-bar tests check editable workbook data, signed
vector geometry, binding and explicit rejection of unsupported forms. The
[independent comparison](measurements/roadmap-138-horizontal-bars-reference.json)
retains the initial label-placement problem and the remaining failures against the
original 0.5-point absolute geometry tolerance. BestEffort and Lenient diagnose
that approximation; Strict, native, tagged and archival output are not qualified.
The [exact-source receipt](measurements/roadmap-138-horizontal-bars-61ae229.json)
records both complete suites, package checks and the reproduced specimen.
The [local performance receipt](measurements/roadmap-138-performance-4eaa2d7.json)
compares the same-source successor against the previous GitHub head with the
unchanged threshold; the earlier hosted performance failure remains recorded.

Pending #163 stacked-bar and column tests cover signed accumulators, zero and
missing values, editing and prepared-data binding, DOCX schema, vector output,
overflow, unsupported overlap and protected output profiles. Two independently
authored XlsxWriter chart/workbook fixtures passed Open XML SDK validation, and
eight corrupted controls exercise graph, diagnostics, output content, legend and vector
checks. The [reference observation](measurements/roadmap-138-stacked-reference.json)
retains both LibreOffice placement failures at the unchanged 0.5-point tolerance.
The [exact-source receipt](measurements/roadmap-138-stacked-c1188c1.json) records
both complete suites, matrix, packages and reproduced specimens; its [performance
receipt](measurements/roadmap-138-stacked-performance-c1188c1.json) compares the
same source with the preceding PR head under the unchanged gate.

Pending #163 point-label tests cover editable chart-wide and uniform series flags,
binding, signed points, fit, aggregate text bounds, cancellation and unsupported
profiles. Independently generated XlsxWriter bar and column specimens use explicit
flags and pass Open XML SDK validation; eight corrupted controls check graph,
diagnostics, content, orientation and cross-format label positions. The
[reference observation](measurements/roadmap-138-chart-labels-reference.json)
records office text and position differences without changing the tolerance.
The [exact-source receipt](measurements/roadmap-138-chart-labels-9f038d0.json)
records both full suites, matrix, packages and reproduced specimens. Its
[performance receipt](measurements/roadmap-138-chart-labels-performance-9f038d0.json)
retains both rounds and the visible timing drift under the unchanged gate.

Pending #150 [comparison-selection tests](review-workflows.md#unreleased-source-comparison-selection)
check the retained original appearance/story, inherited-format resets, text and
structural changes, combined work budgets, unchanged input sessions and rejection
of unsupported properties and story topology. Additional [paragraph-history tests](review-workflows.md#unreleased-source-paragraph-format-revisions)
validate OOXML, independent decisions alongside separate paragraph insertions and
deletions, PDF geometry, inherited resets, cells, composition, section ordering and
unsupported-owner controls. They do not establish untested
structural combinations or runtime/package qualification.

## 1.5.0 roadmap #102 delivery

The [release notes](releases/1.5.0.md), [capability matrix](roadmap-102-capability-matrix.json)
and [qualification record](roadmap-102-p6-qualification.md) describe the P1–P6 batch.
The bounded fields, controls, reports, review and drawing additions ship together;
all five target frameworks and existing platform limits remain. Historical results
below retain their original source/package identities.

The [compatibility-plan design](template-compatibility-plan-design.md) includes
schema and example checks. Run `python -m pip install -r eng/template-plan-requirements.txt`
in a development environment, then `python eng/test-template-plan-design.py`. These
checks validate the design examples, not converter behavior, policy enforcement,
independent output validation or any runtime/package qualification.

The [initial 1.5.0 fidelity measurements](fidelity-138-initial.md) record a separate
36-input source/public-package comparison and schema checks. They do not complete
the additional controls and platform qualification required by roadmap #138.
Run `python eng/test-roadmap-138-ledger.py` to check the derived failure index
against its retained raw reports; the generated ledger remains dated baseline
evidence, not a fresh execution or current-source qualification.

The #140 documentation audit checked the current public package versions and
release commit and reviewed all tracked Markdown links, including sample guides.
Its [receipt](measurements/roadmap-138-documentation-audit.json) is documentation
evidence, not a new runtime qualification or package publication.

Unreleased #141 tests independently construct schema-validated OOXML themes and
compare local Latin font and color resolution against explicit formatting with
PdfPig coordinates/colors and XPS markup. A [Word-authored theme XML control](../tests/wordnet.Conversion.Tests/Fixtures/Theme/OfficeWordParts/README.md)
is reused under its upstream MIT license with caller-configured open-font substitutions;
it is not an interactive Word execution. Tests cover inherited/default styles,
direct overrides, tint/shade endpoints, settings mappings, system-color fallbacks,
headers and footnotes, direct cell shading/borders, missing and malformed parts,
output atomicity and cancellation. The editable text subset is checked by changing
the saved theme XML independently and comparing the new PDF font, color and glyph
positions with explicit-format controls. Separate tests validate generated DOCX
schema, theme mapping and composition conflicts and confirm source extensions and
relationships are not copied. Typed-control tests reject inherited theme references
before they could be emitted outside the admitted control profile.
The tests do not establish complete editable theme preservation, every style
interaction, script slot, conditional table style or drawing support, or
public-package availability. On Linux x64, theme head `b47ff0a` passed the
five-target Release build and both full .NET 8 and .NET 10 suites, 1,597 cases
each including English OCR. Its complete [push](https://github.com/kirakosyan/word.net/actions/runs/41137881478)
and [PR](https://github.com/kirakosyan/word.net/actions/runs/41137886101) workflows
passed Windows/Linux x64 and ARM source/package lanes and actual Framework 4.7.2
source/package execution. Those results do not qualify later template changes.

Unreleased #166 tests construct independent DOCX/DOTX and ODF text/template
packages. Open XML SDK validation, ODF mimetype/manifest checks, PdfPig text and
XPS glyph inspection cover the advertised package identities and conversions.
The tests also exercise typed controls, review and field retention, explicit
mismatch and control loss, ZIP/CFB negatives, macro/protected classification,
stream position and ownership, aggregate limits and cancellation. These checks
do not establish general package validity or additional platform qualification.
On template head `444bf2e`, locked restore, the five-target Release build and local
core/fonts/OCR package checks passed on Linux x64; the complete .NET 8 and .NET 10
suites each passed 1,609 cases with English OCR and no skips. The
[local receipt](measurements/roadmap-138-template-444bf2e.json) records identities;
hosted exact-head qualification is separate. Both hosted Windows Framework source
lanes for `444bf2e` failed seven template tests: Framework's ZIP fixture generation
emitted a compressed mimetype, and its ZIP API rejected a deliberately invalid
entry name before inspection. The Linux x64 resource benchmark also failed because
its direction guard still expected 40 pairs after the template routes were added.
On `83ab990`, the portable fixtures fixed six of the seven Framework failures, but
both Windows Framework source runs still failed the malformed-name test: Framework
rejected the name while loading ZIP entries before the detector could classify it.
On `5a654f5`, detection classified that input correctly, but both Framework runs
passed 1,619 cases and failed the new conversion assertion because the DOCX reader
enumerated the archive separately. The subsequent shared package-reader correction
requires a fresh hosted run. The updated local resource benchmark retains the
original 40-pair guard and passed 32 iterations across all 53 directions; hosted
results are separate. An independent check used the authored
[`Odt/text.odt`](../tests/wordnet.Conversion.Tests/Fixtures/Odt/README.md) fixture
(SHA-256 `50f1feca909fa19bbb38aee93f5649f8451becc8a6d792b1b432bd832b133b09`).
LibreOffice 24.2.7.2 opened generated OTT and DOTX outputs and recovered the
expected synthetic text; the OTT also passed the pinned ODF 1.3 schemas. The
[probe](../eng/roadmap-138-template-probe.cs) and
[verification script](../eng/verify-template-identity.py) reproduce the checks.
This observation does not qualify a public package.

Unreleased #143 glossary tests use independently authored OPC/XML packages and
Open XML schema checks for category metadata, bounded local resources, typed edits,
bindings, placeholder display and byte/relationship preservation. Negative cases
cover external, dangling, duplicate, nested, active and unsupported resources,
default loading, cancellation and untouched output. A schema-validated derivative
of a [Word-authored glossary](../tests/wordnet.Conversion.Tests/Fixtures/ContentControls/OfficeWordParts/README.md)
now checks local resource preservation and independently inspected gray placeholder
text in Lenient mode. Strict theme fallback and unsafe style graphs remain negative
controls. [Additional Word-authored variants](../tests/wordnet.Conversion.Tests/Fixtures/ContentControls/OfficeWordVariants/README.md)
exercise block/inline rich text, plain text, dates and the static `notTrueType`
font-catalog flag. The tests retain the source's missing list-placeholder references
as negatives, alongside malformed or active font metadata. The first nine-case
working-tree run failed six cases because the prior validator rejected that font
flag before reaching the controls; the follow-up admits the bounded Boolean flag.
The [exact-source glossary receipt](measurements/roadmap-138-glossary-f7d6a7f.json)
records that increment's local checks and the failed attempts.
Broader Office-authored
kinds and resource-graph acceptance required by
#143 remain outstanding. On the local Linux x64 working tree containing these
changes, the complete .NET 8 and .NET 10 suites each passed 1,620 cases with
English OCR; the executable source matrix passed 84 mapped cases. Hosted Windows
Framework and other platform results for this change remain separate and require
their own exact-commit runs.

Unreleased #146 tests independently validate inserted cell-fragment DOCX structure,
host widths and bookmark/note identities with Open XML, and inspect PDF text,
geometry, text colors and inline images with PdfPig. Field opt-in checks reference remapping, refreshed
values, retained merge caches, isolated sequence counters, invalid switches,
placeholder-style overrides, unsupported owners, cancellation and untouched
source/destination sessions.
The full #146 acceptance and cross-platform qualification remain open.

The [unreleased source matrix](roadmap-138-source-matrix.json) maps bounded positive
and negative combinations across the pending roadmap work to test methods. Run
`python eng/verify-roadmap-138-source-matrix.py --run` after locked restore to
validate the mapping and execute those tests; add `--framework net8.0` to use
.NET 8. This is not public-package or platform qualification.

Unreleased #148 review tests independently inspect Open XML history, PdfPig
selectable text, mark color and coordinates, and SVG glyph paths and marked titles.
They cover body and simple-cell revisions, multi-page text, source immutability,
attribution escaping, profile and owner rejections, cancellation and bounds.
Full issue acceptance and exact-runtime qualification remain open.
Unreleased #149 tests inspect overlapping and table-cell anchors, comment bodies
and independent list numbering, exact format snapshots and paired move endpoints in
PDF/SVG. They also cover comments combined with moves or format history, empty and multipage notes,
source preservation, unsupported structural changes, cancellation and bounds.
The expanded local Linux x64 .NET 8 and .NET 10 suites each passed 1,578 cases
without skips, with English OCR prepared in the launching process. Locked restore
and the five-target Release build also passed. On annotation head `b0776aa`, the
[push workflow](https://github.com/kirakosyan/word.net/actions/runs/41132995601)
passed the Windows/Linux x64 and ARM source and package lanes, including actual
Framework 4.7.2 [source](https://github.com/kirakosyan/word.net/actions/runs/41132995601/job/124334009444)
and [package](https://github.com/kirakosyan/word.net/actions/runs/41132995601/job/124337339781)
execution. This does not qualify the later theme changes.
On earlier text-display head `ca732f2`, locked restore and the five-target Release build passed, and the
complete .NET 8 and .NET 10 suites each passed 1,567 cases with English OCR
prepared in the launching process and no skips. Its hosted Windows/Linux x64 and
ARM source/package lanes passed. The push workflow's actual Framework 4.7.2
[source](https://github.com/kirakosyan/word.net/actions/runs/41127722806/job/124317622187)
and [package](https://github.com/kirakosyan/word.net/actions/runs/41127722806/job/124320865003)
gates each passed exactly 1,567 cases with the required legacy JIT; the
[PR workflow](https://github.com/kirakosyan/word.net/actions/runs/41127732182)
also passed both gates. Both complete hosted workflows succeeded. That evidence
belongs to `ca732f2`, not the later annotation or theme changes.

For the earlier draft head `4d35259`, the actual Framework 4.7.2 source guest
reported 1,522/1,522 passing with no skipped tests, but the
[host gate](https://github.com/kirakosyan/word.net/actions/runs/41120440306/job/124293436141)
failed because its bundle still required exactly 1,498. The
[guest evidence](https://github.com/kirakosyan/word.net/actions/runs/41120440306/artifacts/13009293177)
retains the test report and identity/cleanup records. The later fragment head
required 1,559 cases; the subsequent text-display head required 1,567. The then-current
suite required 1,592. Exact-head Framework execution remains a separate gate for later changes.


## P4/P5 review and drawing profile — historical checkpoints

The [P4/P5 profile](roadmap-102-p4-p5.md) has independently authored OOXML
fixtures for formatting history, move ranges, sections, story variants, rotated
and linked text boxes, autofit and vector geometry. Tests inspect Open XML schema,
accepted/rejected output, PDF text and geometry, XPS paths and SVG outlines, with
negative ownership, resource and unsupported-combination controls. Final source,
package and platform qualification are reported separately from the dated P3
results below; see the [1.5.0 release record](releases/1.5.0.md).

On Linux x64, locked restore and the five-target Release build passed. The complete
.NET 10 and .NET 8 suites each passed 1,471 tests with English OCR provisioned in
the test process and no skips. Format verification and local pack passed; the
package contains all five target assets, XML documentation and license notices.
Framework execution requires a compatible Windows host; hosted checks and other
platform qualification are separate.

The PR review follow-up adds comparison, drawing and revision regressions. The
final local Linux x64 .NET 8 and .NET 10 suites each passed 1,484 tests with OCR
and no skips; hosted checks are recorded with the PR. The original
Framework 4.7.2 guest report passed 1,471 cases, but its host gate failed because
the pinned case count still said 1,435; the count now tracks the enlarged suite.

## Mixed report regions — merged source

The independently authored OOXML tests in
`EditingTemplateTests.MixedReports.cs` exercise zero, one and multiple records,
DOCX schema and content order, multipage selectable PDF, nested row repeats,
inline images/conditions, identity remapping, boundary and resource failures,
and cancellation. The image uses the existing `color-grid.png` fixture. These
tests qualify this bounded source only; the P2 receipts below remain historical,
and the earlier public 1.4.0 and platform qualification were unchanged at that checkpoint.
On Linux x64 the full .NET 8 and .NET 10 conversion suites each passed 1,408/1,408
with no skips and English OCR prepared in the test process. Locked restore,
five-target Release build, formatting verification and local pack passed; the pack
contains all five target assets and license notices. Framework execution requires
a compatible Windows host and hosted checks for this PR remain separate.
These results precede the subsequent #119–#121 implementation. Master-detail,
cross-tab and fragment tests additionally inspect schema-valid DOCX and PDF text
and geometry, caller-prepared totals, identity remapping, malicious/missing
resources, expansion limits and failure atomicity. The runnable statement fixture
is independently authored by `eng/generate-p3-template.py` under MIT and has a
recorded SHA-256 manifest. Final full-suite and hosted results for the batch are
recorded separately from the earlier 1,408-case run in the 1.5.0 release evidence.

The PR #135 review follow-up adds parent/child boundary references and shared-note
negative controls, strict loading of inactive controls, legacy row-scope checks,
fragment field/display-view checks, and independently inspected quarterly tables
with consistent decimal scales. The statement fixture now uses stored ZIP entries
with fixed metadata, avoiding compressor-dependent archive hashes.
An exact-head hosted stress retry exposed a wall-clock timeout in PDF trailer ID
matching; the bounded byte scan now avoids a time-limited regex and tests verify
the digest, preserved byte offsets and malformed candidates. The earlier timed-out
and failed stress runs remain distinct from the new head's qualification. The
Linux x64 desktop-job deadline is 30 minutes because that lane also performs
independent office checks and the full 64-by-200 gate; its workload and assertions
are unchanged. Other desktop job deadlines remain 20 minutes.

## P2 controls acceptance and qualification

The [source profile](content-controls.md), [acceptance map](content-controls-acceptance.json)
and [authored fixture provenance](../tests/wordnet.Conversion.Tests/Fixtures/ContentControls/README.md)
track #112–#117 independently. Tests validate live DOCX state/parts with the Open XML
SDK and raw XML, public snapshots/atomicity, pinned-font selectable PDF/XPS text,
underline coordinates, local XML directions/conflicts, malformed input and bounds.
The [review receipt](measurements/roadmap-102-p2-review.json) records qualified source
`c15d3a8f0433f5e9ea3bc1d3efe57b88083b3872`: **1,390/1,390** pass on each local Linux x64 modern runtime,
zero failures/skips with English OCR prepared in the launching process. All 168
control cases, including 35 review regressions, pass; all 34 groups have mapped
passing methods. Independent Word-shaped input/output schema validation covers
all six kinds, and raw XML/PDF/XPS/text checks cover the reviewed behavior. Locked
restore, five-target zero-warning Release build, formatting, core pack/API and
archive inspection pass. No dependency or target is changed.

See the [review qualification limits](content-controls.md#review-corrections-and-qualification--2026-10-27)
for the explicit glossary/script subset and unexecuted local platform/consumer lanes.
The [initial receipt](measurements/roadmap-102-p2.json) remains dated evidence of
1,355 cases. Final reviewed-head hosted checks subsequently passed before merge; public-package
availability remains separate from this revised local source verification.

## Merged roadmap #102 P1 qualification

The [P1 evidence/profile](roadmap-102-p1.md#evidence-and-remaining-qualification) adds independent OOXML/property schema, pinned-font/default/mark coordinates, demanding autofit/row splitting, conventional note continuation, typed pictures under two cultures, nested-field safety and running-header page/section/variant checks. Existing historical raster/coordinate inputs receive explicit legacy settings in detached copies; their committed originals and tolerances stay unchanged. The final reviewed head passes 1,222/1,222 locally on both Linux x64 modern runtimes and all 24 hosted checks, including desktop source/package/Standard and actual Framework 4.7.2 source/package gates. [Merge qualification](roadmap-102-p1.md#final-review-and-merge-qualification--2026-10-26) and the [exact receipt](measurements/roadmap-102-p1-review.json) distinguish those results and raw-hosted-report inspection limits from dated release evidence below. No new package was published.


## Issue #103 fidelity refresh - merged evidence

[PR #131](https://github.com/kirakosyan/word.net/pull/131) merged on 2026-10-26 as
`ab3843880572f811d84b410e437c86b80f8ff59f`, with tree equality to reviewed head `09825b6`. All thirteen
review threads are resolved. Final-head [push CI](https://github.com/kirakosyan/word.net/actions/runs/40331031493),
including source/package/Standard and actual Framework 4.7.2 source/package jobs,
passed; both Android API compilation runs also passed. The identical-tree
[PR CI](https://github.com/kirakosyan/word.net/actions/runs/40331042390) was retrying
an Ubuntu package-download timeout at merge. This qualifies the tested source tree
separately from historical measurements; the PR-time CI and package status are historical.

The [new baseline receipt](fidelity-103-baseline.md) measures clean source
`ec7816b` and independently restored public 1.4.0 against all 24 original references
plus 12 independent OOXML contrasts. All original authored/editable/diagnostic gates
pass; all strict reference failures remain retained. It also records the new probes'
warning, glyph-tokenization and note-policy differences. Thirteen comparison controls,
six evidence-tool tests and twelve independent schema validations passed locally.
This is Linux x64 measurement evidence, not new engine support or release qualification;
full library suites and other runtimes belong to their own exact-head CI results.

The review follow-up has twenty evidence-tool tests. Scratch generation protects
measured fixtures and uses stored ZIP parts with fixed metadata; guards compare
exact historical part bytes and each manifest's own archive hashes. Other controls
cover duplicate IDs at the expected count, early pinned-POI validation, listed stable
NuGet selection (including gzip/pagination), an offline restore with conflicting
source pins, summary reproduction, and rejection of changed raw/reference/compact
evidence. Unexecuted schema checks stay explicit. The generator uses LF checkout.

## 1.4.0 roadmap #99 profile

Version 1.4.0 extends the 1.3.0 profiles with the bounded changes in
[roadmap-99-implementation.md](roadmap-99-implementation.md): direct HTML/ODT PDF export,
chart binding, scalar fields, structural comparison and qualified layout/native/object
combinations. The [1.4.0 release notes](releases/1.4.0.md) record compatibility and
qualification requirements. Earlier release receipts below remain historical evidence.

## Unreleased matrix checks

Pending #168 source tests independently construct OMML matrices and validate DOCX
with Open XML SDK, inspect logical PDF marked content and vector paths, verify XPS
and SVG output, and check column edges and the MATH baseline with the pinned font.
They exercise copied inputs, snapshot updates, composition, empty cells, default
and unsupported properties, limits and cancellation. These tests do not by
themselves qualify other runtime or output profiles.
The [414707d receipt](measurements/roadmap-138-matrix-414707d.json) records its
local full-suite and package checks and the partial hosted observation at that time;
later commits require their own exact-head checks.

Pending #169 tests independently construct and schema-validate OMML sums, products
and integrals, compare limit placement and display glyph selection to pinned MATH
metrics, and inspect managed PDF/XPS/SVG vectors and logical text. They cover
snapshot/composition, missing limits, nested matrices, unsupported properties,
missing glyphs, explicit size fallback, cancellation and strict untouched output.
The [independent font check](../eng/verify-nary-font.py) uses pinned FontTools 4.61.1
to verify the font hash, display minimum, limit metrics, sum/product/integral
variant glyphs and integral assembly parts. A bounded in-memory font control forces
the assembly path. The Linux P2 CI lane runs the font check separately from the converter.

## Released 1.3.0 qualification

The required suite is **1,050** cases: the 957-case P1 head plus 93 P2 cases, including 34 review regressions.
P2 adds independent OOXML/XLSX, MATH font metrics/variants/assemblies, editable object
operations, managed chart/math geometry, native bidi/Hindi and native tagged/UA
regressions. The two UAX #9 cases execute all **91,707** character cases and
**770,241** abstract directional combinations against pinned Unicode 17 inputs;
these are not counted as extra converter cases. Earlier UAX #29 cases remain.

`WORDNET_P2_EVIDENCE` retains the generated corpus. The independent office driver,
vector/MATH/reading verifier and pinned veraPDF UA check run in the Linux x64 CI lane,
with schema/content/glyph assertions in every applicable source/packed suite.
`eng/p2-requirements.txt` pins development tools. Font and UNO/compiler provenance
is retained under `Fixtures/P2` and `eng/p2-tool-pins.json`; none is shipped in NuGet.
The concrete reproduction, predeclared 0.5 pt geometry tolerance, exact subset and
unsupported combinations are in [the P2 profile](p2-objects-typography.md).
Completed exact-main source/package/runtime and publication checks are linked from the
[1.3.0 release record](releases/1.3.0.md). Historical P1 and review-head qualification
below remains separate; these results do not qualify additional platforms.

The final metadata-boundary regressions additionally cover newline, tab and
length-plus-one chart descriptions. These valid out-of-profile DOCX values raise
`NotSupportedException` during session load and produce explicit unsupported-object
loss during permissive conversion; they never surface as caller argument errors.

Eight additional end-to-end negatives verify that ordinary unsupported-object losses
cannot bypass native typography or PDF tagged/UA/A combination rejection, in either
Lenient or BestEffort mode. Expandable sentinel streams ensure that an output-stream
capacity error cannot masquerade as a correct feature rejection.

### Review revision 5a5c26c — 2026-10-20

All eleven review findings have concrete fixes and 23 additional regressions.
At `5a5c26ccfe3798d6994345feaa145f20f253913e`, six complete Linux x64 source,
package and explicit Standard runs on .NET 8/10 each pass **1,039/1,039** with no
failures/skips (6,234 passes). Locked restore, five-target Release build, formatting,
API-compatible six-archive packaging and the executable recipes pass. The current
NuGet audit reports no known vulnerable direct/transitive packages.

Fresh independent Writer/Calc/Math edits and chart/MATH/logical-structure measurements
pass. FontTools BoundsPen independently measures the exported radical glyph top and
its adjoining SVG rule (error below 0.000001 pt). The package identity receipt checks
all 11 DLL assets, exact source README, commit metadata and exclusion of development
fonts/fixtures. Local reports remain under ignored `artifacts/review-*`; hosted final
revision and exact-main evidence belongs to [PR #86](https://github.com/kirakosyan/word.net/pull/86).
Subsequent documentation edits do not change the tested library; release packages
still require new exact-main qualification under [releasing](releasing.md).

## Merged P1 qualification — PR #85, 2026-10-19

PR [#85](https://github.com/kirakosyan/word.net/pull/85) merged as
`73f935732a788c69a934edfa5d71f42635e02031`. Its tree is identical to reviewed
head `d733a755663d776c4fa119ef18e599f3f3559b78`; all fifteen review threads are
resolved. Final-head [CI 39313440331](https://github.com/kirakosyan/word.net/actions/runs/39313440331)
and [Android compilation 39313440362](https://github.com/kirakosyan/word.net/actions/runs/39313440362)
passed. All five desktop source/performance jobs, four packed jobs and both actual
Framework 4.7.2 source/package jobs passed. Six complete local Linux x64 runs
(source, packed and explicit Standard on .NET 8/10) each passed **957/957** with
zero failures/skips. These are merged-source checks, not package publication.

The P1 head required **957** cases (855 baseline plus 102 P1 cases, including review regressions).
The P1 branch adds independent OOXML/ODF fixtures and regressions for unequal-column
flow, report preprocessing, paragraph repeats, review preservation/decisions/recording
and matching-structure comparison. Unicode 17 conformance executes all **766 grapheme**
and **1,944 word** cases, with SHA-256 checks on the original test files. These execute
inside two suite cases, not as extra converter cases.

`eng/verify-odt.py` uses pinned OASIS 1.3 schemas and LibreOfficeDev 26.8 build
`2c87e51eeaa2b413ff4ae097b2705eea1995d8e5`. Six independent output checks retain
text/order, list starts/nine levels, two master geometries and stories, merged cells,
exact raster hashes and 60 × 30 pt frame geometry within 0.5 pt, plus styled headers,
cell numbering and section transitions on list paragraphs. Local artifacts and
`verification.json` are under ignored `artifacts/p1-odt`; original synthetic fixtures,
generators, schema pins and conformance pins are committed.

Final PR-head source/package results are recorded on the PR. Local runtime execution
is Linux x64 on .NET 8/10; Framework is not run on Linux. Other supported native and
actual Framework guest qualification belongs to the corresponding CI lanes.
Compilation alone does not qualify those runtimes. Historical results below are
separate from this merged implementation.

## P0 merged delivery — PR #83, 2026-10-19

PR [#83](https://github.com/kirakosyan/word.net/pull/83) merged as `6cf54fd2a406880feb18abb1ee495b8c77bcc3e8`, with tree equality to final reviewed head `e8e6cbfada0f261d3153934f730ec3b3f42be870`. All fifteen review threads are resolved. All final-head push/PR checks pass: [PR CI](https://github.com/kirakosyan/word.net/actions/runs/39287800867), [push CI](https://github.com/kirakosyan/word.net/actions/runs/39287792368), and their two Android compilation runs. This evidence belongs to that tested head/merge-ref tree; it does not publish or qualify a new exact-main package for release.

The required suite is **855**. Complete local Linux x64 .NET 8.0.32 and .NET 10.0.12 suites each pass 855/855 with zero failures/skips. Source CI covers native Windows/Linux x64/ARM64, and Windows x64 Framework. Packed CI exercises exact artifacts and explicit Standard fallback. Hosted actual Framework 4.7.2 source/package lanes each pass 855 cases on release 461814 with their documented legacy-JIT configuration. Push guest receipts identify reviewed head `e8e6cbfada0f261d3153934f730ec3b3f42be870`; PR guest receipts identify merge ref `0a9d54061e5e3d045eadfa3bc4aaca125dec4261`, whose tree matches that head. Android compilation remains separate from device execution; actual Framework 4.6.2, macOS and Android support remain unqualified/deferred.

Locked restore, five-target Release build, formatting, six package/symbol archives, package metadata/API checks, deterministic fixtures, JSON schemas and README links pass. Independent Open XML/PdfPig/Poppler checks cover recipe structure, exact images, totals, pagination and page fields. All 24 authored/POI corpus cases and thirteen Python comparison/capture controls pass. Local TRX counters and all 1,710 individual results are verified in `artifacts/review-tests-final/verification.json`; those files remain local evidence, not hosted artifacts.

Both final CI runs archive genuine pinned-office PDFs, references, snapshots, rasters and complete ranked findings in `business-comparison`. The final push [artifact 12205600534](https://github.com/kirakosyan/word.net/actions/runs/39287792368/artifacts/12205600534) has SHA-256 `3544f29574eb58ab5ef4165c87900549d85fe9502bd1163a49c182a88bdf7ac2`. Twelve full office comparisons remain failures under the locked text/glyph/page/raster policy. The corrected one-to-two-column fixture measures 18.239 pt locally, and the A4 height mismatch is explicitly attributed to LibreOffice unit conversion. CI writer/core versions are pinned to `4:24.2.7-0ubuntu0.24.04.7`; local Debian 25.2.3.2 captures remain separate. See [the comparison report](comparison-p0.md). No Word/Aspose parity or unavailable commercial-reader execution is claimed.


## PR #83 review corrections — 2026-10-19

The review follow-up addresses all fifteen inline notes. It adds four recipe cases (sub-cent invoice rounding, cumulative diagnostics, cancellation after staging and cleanup-error preservation), so the required suite is **855**. The focused corpus/recipe run passes **38/38** and the Python comparison/capture suite passes **13/13**. All-target Release compilation passes with zero warnings/errors. Full source/package/runtime results must be read from the final PR head and retained CI artifacts; the initial 851-case evidence below remains historical.

The corrected continuous-section input is one column before and two after the break. All 24 authored/POI corpus checks pass; twelve recaptured Debian LibreOffice references retain twelve strict-policy failures. The changed case now measures 18.239 pt, replacing the initial two-to-two-column fixture's 243.750 pt observation. Reference page-size unit rounding is identified separately from converter defects. The refreshed hashes and producer identity are in `Comparison/p0-baseline.json`.

CI pins Ubuntu writer/core packages to `4:24.2.7-0ubuntu0.24.04.7`, and capture explicitly requires Linux fontconfig. Shared font/cohort/renderer helpers prevent drift. Both exit status and ranked findings use one failure predicate; the required CI retention gate checks complete per-engine findings and all failed verdicts, including diagnostic expectations and editable-content loss. Template hashes and declared sample page counts are exercised by Python and xUnit respectively. No core API, target framework or runtime dependency changed.


## Initial P0 candidate `948863b` — local evidence, 2026-10-19

This working tree, based on `7a09ef6b5d7dfcec8ee9507741b2b78016f14761`, implements #46 and #47 with twelve new corpus cases and ten shared recipe cases. This initial candidate required **851** cases (829 previous + 12 corpus + 10 recipes). This section qualifies local Linux x64 source and local packaging, separate from merged source, public packages and historical platform evidence below.

- Locked restore and all-five-target Release build pass with zero build warnings/errors. API-compatible packaging creates all six package/symbol archives for `wordnet.Fonts`, `wordnet` and `wordnet.Ocr` against the configured public 1.2.0 baseline. Archive inspection verifies all five core assets and the cleaned README.
- Complete English OCR/conversion suites pass **851/851 on .NET 10.0.12** and **851/851 on .NET 8.0.32**, with zero failures/skips. Pinned English OCR is provisioned in the same PowerShell process that launches both suites. Reports are `artifacts/p0-tests/net10.trx` and `net8.trx`; the machine-readable counter/hash receipt is `artifacts/p0-tests/verification.json`.
- All 24 corpus inputs pass independent Open XML schema validation, authored required text/order, exact authored pagination and expected diagnostic checks. Apache POI 5.4.1 independently recovers all required text from the 24 editable DOC exports. Five comparison controls pass, including missing-text, extra-page and shifted-glyph negative controls.
- Twelve genuine LibreOffice 25.2.3.2 references execute under pinned Noto Sans/Liberation fonts. All twelve match page counts and all twelve fail the existing strict office geometry/raster policy. [The ranked report](comparison-p0.md) retains these defects without converter changes or relaxed thresholds. Licensed desktop Word and licensed Aspose are unavailable. CI captures its own exact office build and archives PDFs, snapshots, rasters, references and ranked verdicts in `business-comparison`.
- The documented recipe command produces the four expected files without the optional native provider. Poppler independently verifies invoice customer/line-item/266.00 total text, report content, one/two-page pagination and refreshed page fields. Shared tests cover editable rows, exact image bytes, empty and 100-item/four-page invoices, required-data and expansion/output limits, cancellation and preservation/replacement of existing files.
- Formatting verification, generator byte/hash reproducibility, JSON schemas, README/NuGet links, package README equality, actionlint 1.7.12 and diff checks pass. `dotnet format` reports workspace-load warnings for this host but exits successfully with no formatting changes required. RTK was unavailable; the prescribed .NET commands ran directly.

Actual Framework execution, Windows/ARM64 and packed/Standard runtime suites are not local results on this Linux x64 host; they remain the applicable CI lanes. Android/macOS remain deferred and no workload was installed. No package publication, release tag or credentials change is part of this work. Historical sections retain their original counts and exact-artifact boundaries.


## NuGet README rendering patch — 1.2.1 preparation

NuGet disables raw HTML while rendering Markdown. The earlier OS `<img>` tags
therefore became visible text on the 1.2.0 package page. The replacement header
uses six Markdown images on NuGet's allowed Shields host with matching flat-square
styling and explicit alternative text. A Markdig pipeline with HTML disabled
renders all six as images without escaped `<img>` text; the old header is the
negative control. Validate the README extracted from each final package as well.
The live 1.2.0 page also confirms NuGet clears relative documentation links to
empty `href` values. README file links now use absolute GitHub URLs, while internal
anchors remain. Link validation applies NuGetGallery's HTTP(S)/fragment rule and
checks the corresponding repository files locally.
Package identity and the PDF producer assertion advance to 1.2.1; API comparison
uses public 1.2.0. Current-head source/package/minimum-runtime results belong to
the patch PR; 1.2.0's reports remain historical evidence for that shipped artifact.

PR [#43](https://github.com/kirakosyan/word.net/pull/43) merged as
`9f5e85af69242d344cce2e5a7e590aa6971db497`; its tree equals final head
`c46ee0a447b295a326a41bb8fa84e30225320428`, whose 24 checks passed
([push](https://github.com/kirakosyan/word.net/actions/runs/39131042415),
[PR](https://github.com/kirakosyan/word.net/actions/runs/39131046861)). All four
downloaded source/package minimum-runtime reports contain 829 individual passes
without failures/skips, actual release 461814/x64, observed legacy JIT, matching
commit/library/test/configuration provenance and all cleanup flags true. Both
six-archive NuGet artifacts match the DLLs executed in their package guests.
All 24 complete job logs contain zero Node.js 20 deprecation/forced-runtime warnings.
Local locked restore, all-target build, both 829-case modern suites, formatting,
dependency audit, API baseline 1.2.0, all six archives and package metadata/notices pass.
This documentation-only follow-up uses rendering, URL, local-link and diff checks;
final-main qualification and publication remain tracked in the live roadmap record.

## GitHub Actions runtime maintenance — 2026-10-18

Checkout, .NET setup and artifact actions now use SHA-pinned releases that declare
Node.js 24 in their manifests. NuGet/login 1.2.0 already uses Node.js 24 and retains
its existing pin. All four workflows pass actionlint 1.7.12. Parsed workflow comparison
confirms that only external action references changed in the workflows; triggers,
permissions, matrices, commands, artifact inputs and publication gates are identical.
An initial Android run exposed setup-dotnet's newer roll-forward behavior: `latestPatch`
installed SDK 10.0.402 and changed the implicit ILLink dependency to 10.0.13, violating
the committed lock. `global.json` now disables SDK roll-forward and keeps 10.0.401;
locked restore remains enforced. No runtime-forcing environment variable is required.
Native CI validates the updated action implementations;
its runs and current-head results are recorded on the maintenance PR and roadmap #32.

The successful [1.2.0 publish run 39129534970](https://github.com/kirakosyan/word.net/actions/runs/39129534970)
uses release commit `3202a1fcfed9b1210b1eadb8a5e3509d7c2adba9` and immutable packages
from main CI `39126846669`. Its four desktop lanes produced nine independently
downloaded, archive-digest/CRC-checked TRX reports: each contains 829 individually
passing cases, with no failures/skips (7,461 executions). Package and symbol pushes
for all three IDs were accepted. All three public packages are now listed. Their
nuspec source commit and all 11 DLL hashes match the tested CI artifact; signed ZIP
hashes are retained separately. A new public-only NuGet cache restored and built the
package-mode consumer without warnings. See the [release ledger](release-1.2.0-readiness.md).

## Verified release main — 2026-10-18, before OS-icon documentation

PR #41 merged as `91838ebfa9c5eeef38716b72757b3b017d1ad347`, with tree equality to
`4b600e96f778f4f9252ba9beef37eb3d0ae1b4f3` and all 24 final-head checks passed.
Its [main CI 39124192159](https://github.com/kirakosyan/word.net/actions/runs/39124192159)
passed all **12** checks. Actual Framework 4.7.2 source and immutable-package suites
each passed **829/829** with no failures/skips on release 461814/x64, observed legacy
JIT and matching commit/library/test/configuration hashes. All three executed package
DLLs match the six-archive NuGet artifact. Reports and cleanup receipts were downloaded
and independently verified. All 12 Windows guests across the named release revisions
through this snapshot removed their containers, networks, VM state and newly pulled
images; no local qualification Docker resources remain.

The [ledger](release-1.2.0-readiness.md) records immutable artifact IDs and hashes.
README OS icons change no runtime profile, but a new main produces a different
release artifact and needs its own exact-main qualification before publication.
Use the [live publication record](https://github.com/kirakosyan/word.net/issues/32)
for the current SHA/run. Version 1.2.0 remains unpublished at this reconciliation.

## PR #40 release preparation — merged 2026-10-18

PR [#40](https://github.com/kirakosyan/word.net/pull/40) squash-merged as
`cd541f13cce3311a15a98e86fb9e656d9405e193`; its tree equals head
`242488b9399717044b1b9583b10b9d73b2cd5c7e`, which passed **24/24** checks.
The new [hosted Docker/KVM lane](hosted-minimum-runtime.md) supplies actual Windows
Server 2019/Framework 4.7.2. Source and immutable candidate-package suites each passed
**829/829**, without skips/failures, on release **461814**, x64, with observed
`compatjit.dll` and the intended legacy-JIT configuration. Both push and PR merge-ref
runs passed; their four downloaded report sets and cleanup receipts were independently
verified. Each removed its containers, network, VM disk/ISO/staging and newly pulled
image. No VM disks or credentials are retained in uploaded artifacts.

The [release ledger](release-1.2.0-readiness.md) records run/artifact IDs and hashes.
Six local evidence-rejection controls cover stale/tampered bundles, newer runtimes,
missing JIT/configuration/hash evidence, skips/failures/partial suites and incomplete
exports; these controls are separate from the actual runtime passes. Candidate package
execution does not qualify the different final-main artifact, and no 1.2.0 publication
is claimed. Historical sections below retain their original counts and boundaries.

## PR #38 native font-lifetime follow-up — merged 2026-10-17

The documentation reconciliation also fixes a failure exposed by its own CI: an
initial Linux ARM64 process exited 139 after 370 passing cases; a Windows packed
.NET 10 run then reported an invalid outline glyph. Investigation found that the
HarfBuzzSharp stream-blob factory retains an unpinned temporary managed array.
The adapter now duplicates admitted font bytes into native-owned storage while
the source is pinned. A new regression forces compacting collections and reuses
large temporary allocations before checking an independently generated Arabic
glyph/cluster/advance/offset golden. It fails before the fix with a missing-glyph
error after collection and passes with owned storage. The current suite requires
**829** cases; the 828-case PR #37 evidence below remains historical.

PR [#38](https://github.com/kirakosyan/word.net/pull/38) squash-merged as
`2f16d47ac007bf1a5affb41bc0b7653f5198c9f8`. Its tree equals final head
`e09c17c7ef43e38bcdf9717e535caa02b31d7812`, whose **20/20** checks passed
([push CI](https://github.com/kirakosyan/word.net/actions/runs/39070508323),
[PR CI](https://github.com/kirakosyan/word.net/actions/runs/39070512461)).
The failed 2388075 attempts are superseded, not counted as passing qualification.
Local validation passes locked restore, all-five-target Release build, formatting,
all three packages/API/commit checks and 829/829 source cases on each .NET 8/10 runtime.
Four fresh-cache packed runs each pass 829/829 (3,316 passed), including explicit
Standard fallback on both modern runtimes; no cases failed or skipped. The pinned
[binding source](https://github.com/mono/SkiaSharp/blob/4783f51448f9b070dda4f87b83e941c9599e466e/binding/HarfBuzzSharp/Blob.cs)
confirms the stream-factory ownership issue. Exact artifacts/reports are hashed in
`artifacts/pr38-native-lifetime/verification.json` in the qualification workspace.
This native ownership fix changes neither public APIs nor dependencies. Actual
minimum runtimes, released packages and the historical PR #37 run remain distinct.

## Evidence by revision

PR [#37](https://github.com/kirakosyan/word.net/pull/37) squash-merged on 2026-10-17
as `44c9e3629f95ed2c984a325a384174303550cb53`. All **15** submitted review findings are resolved; all **20**
checks passed on final reviewed head `2af6a4358e7596a8d82046086c04374e96d7eea7`
([CI run](https://github.com/kirakosyan/word.net/actions/runs/39067299512),
[PR CI run](https://github.com/kirakosyan/word.net/actions/runs/39067303481)). The merged
tree equals that head. Native Windows/Linux x64/ARM64 source and packed lanes pass,
including explicit Standard fallback on .NET 8/10 and the hosted Windows x64 Framework lane.
The required suite is **828**, including 33 review regressions. Seven bucket-4 issues
have verified acceptance and closure; roadmap #32 records completed buckets 1–4.
At that checkpoint this was unreleased source, separate from published 1.1.0,
actual-minimum-runtime qualification and exact-main-package release qualification;
the subsequent 1.2.0 release included it.

PR [#35](https://github.com/kirakosyan/word.net/pull/35) was squash-merged on
2026-10-17 as `0b2af843a40b8c866119594a887e8dac1f5c7340`, with **696** required
conversion/OCR cases, including **86** authored editing/template cases. All **15**
review findings were addressed, replied to and resolved. All **20** CI check runs
passed on the final reviewed head `383ea15f1bb21d96f38a61a2eaee42980cae0124`
([push CI](https://github.com/kirakosyan/word.net/actions/runs/39034264216),
[PR CI](https://github.com/kirakosyan/word.net/actions/runs/39034268071)).
The expected-head merge was verified and its tree equals that reviewed head.
Issues #11–#15 and #17 have checked acceptance criteria, merge evidence and verified
closure; at that reconciliation, roadmap #32 recorded completed buckets 1–3 and the next bucket 4.

PR [#34](https://github.com/kirakosyan/word.net/pull/34), commit
`1c44d97f7378401ed8ee67a9ae50b2197499c4bd`, had **610** required cases and 20 successful
pre-merge checks. PR #35's initial 664-case head
`811d6a26af91ebbd95249f4189230be99ddb483e` also passed 20 checks; those are historical
results, separate from the final review-regression evidence below.

The PR CI checks include native Windows/Linux x64/ARM64 source and packed-asset
lanes, compile checks and the single-core PDF performance/memory gate. These are
PR-head results; they do not replace exact-main-package release qualification,
actual minimum-Framework execution, desktop Word or licensed Aspose references.
Local runs and independent-reader evidence are detailed below. Historical release
sections retain their original case counts. See the [delivery status and post-merge
checklist](implementation-plan.md) before interpreting an old count as current.

## Bucket 4 merged-source qualification

The merged implementation covered #20–#23 and #25–#27 and was unreleased at this
checkpoint; see the
[bounded profiles/API](interoperability.md) and [decisions](interoperability-design.md).
The required suite expands from 696 to **828** cases (795 initial plus 33 review regressions): 15 HTML, 21 RTF, 12 typography,
10 accessibility, 16 previews, 14 geometric PDF reconstruction and 16 OCR metadata/control
cases plus 28 cross-feature review cases are described by their actual test classes (the aggregate count is authoritative).
Historical PR #35 counts above remain historical, not a claim about these additions.

Independent checks on Linux x64 use LibreOfficeDev 26.8 alpha build
`2c87e51eeaa2b413ff4ae097b2705eea1995d8e5`, ReportLab 4.4.9,
PyMuPDF 1.26.6/MuPDF 1.26.11, Pillow 12.3.0, uharfbuzz 0.53.0/HarfBuzz 12.1.0,
FontTools 4.61.1 and FriBidi 1.0.16. Noto Arabic font hash/license and PDF ground truth accompany
the fixtures. No commercial reference engine is used. The production optional shaper
uses HarfBuzzSharp/native 14.2.1.301; the independent binding/version is deliberately
separate. Actual Arabic glyph IDs, UTF-16 clusters, advances and mark offsets match;
independent glyph-mask IoU is **1.0**, and both base directions match FriBidi token order.
MuPDF extraction retains logical Unicode/marks after removing extractor-inserted layout
line breaks; word search spans measure **63.12 pt** for the 30 pt Arabic golden.

The own outline decoder fixes negative signed Y deltas and implied half-unit points;
a dedicated Arabic-dot regression and cyclic-composite rejection protect that path.
Independent SVG/PNG ink-mask IoU is **0.9333** for the Latin preview control. Direct
pixels verify cropped/rotated/flipped rasters and clipping bounds, alongside page labels
and original PAGE/NUMPAGES. LibreOffice reads emitted RTF into real editable OOXML,
including exact Unicode/emphasis, one paragraph per ordinary table cell, preserved
multi-paragraph/explicit-empty cells and a **60 × 30 pt** PNG frame.

The 33 review regressions cover generated/artifact link degradation, sorted ParentTree
page/annotation mappings, Word-style RTF repeated rows and picture containers, special
charset losses, Strict cross-page-link PNG previews, 80-page outline-budget reuse,
continued unique-glyph admission, alt/decorative precedence, nested highlights and
2,000-run list semantics. Independent ReportLab additions check first-line/hanging
indents, compact style runs, 32-line prose and measured baselines after tables/inline
images. The qualification benchmark now includes and validates all 36 directions;
the previous 30-direction guard caused both x64 CI failures despite passing source suites.

Pinned offline veraPDF **1.30.2**, image digest
`sha256:d5ee329657cf9bc4b2400392dd54c7d0a0ce9980ff6fa2da5590eebeec007cdb`,
accepts three UA-1 fixtures (invoice, multi-page lists/images/table and an early-page link), rejects the
untagged negative control, and retains **38/38 PDF/A-2b** corpus results. Separate
MuPDF inspection verifies structure roles, parent trees, unique MCIDs, actual page
references, annotations/OBJRs, Unicode text, embedded TrueType programs and source
reading order. Repeated table headers remain artifacts; one semantic Figure retains
alt text while the explicitly decorative second image is an artifact. Fixed-clock
PDF tagging is deterministic. Ordinary PDF/PDF-A defaults remain separate profiles.

Manual reading-order review on 2026-10-17 inspected the emitted hierarchy/content
mappings against source and viewed the invoice raster: H1 Invoice → P Summary first →
row-wise Product/Price, Service/42.50 table → Terms link. The two-page sample orders
heading → two labelled list items → final instruction → described Figure → table
rows 1–49, with column header semantics only once despite visual repetition on page 2.
The Terms descriptions/destinations and image alt were checked in actual objects.
This is a bounded manual structure/visual review, not a claimed NVDA/JAWS session or
arbitrary-document accessibility guarantee.

```sh
# Prepare OCR in the same PowerShell process as both complete suites (see AGENTS).
dotnet restore wordnet.sln --locked-mode
dotnet build wordnet.sln -c Release --no-restore -m:1
# Set WORDNET_INTEROP_ARTIFACTS to an absolute output directory for authored tests.
python3 eng/verify-interoperability.py artifacts/bucket4/interop
python3 eng/verify-typography-accessibility.py artifacts/bucket4/interop
python3 eng/validate-pdf-accessibility.py artifacts/bucket4/interop
# Generate the existing archival corpus with wordnet.ManagedExport.Probe, then:
python3 eng/validate-pdf-accessibility.py artifacts/bucket4/pdfa --flavour 2b --output artifacts/bucket4/pdfa-report.xml
```

Complete suite results and the final reviewed revision are recorded with PR #37;
all source targets compile, and local suites execute on .NET 8.0.32 and 10.0.12.
The review-fix build passes **828/828 on each runtime**, with zero failed/skipped cases,
a warning-free solution build, locked restore and formatting verification. The repaired
36-direction concurrent qualification benchmark passes 32 iterations (1,152 measured,
content-validated exchanges). These local results preceded the refreshed PR-head CI. Its packaged Standard-on-.NET-8
lane then exposed an undeclared CodePages 10 dependency (modern assets passed). The
follow-up explicitly declares the centrally pinned 10.0.12 provider for every core TFM;
archive validation now checks the required legacy/Standard/.NET 8 dependency groups
(.NET 10 legitimately prunes its shared-framework assembly), and packed fallback qualification
runs on both modern runtimes. Source and release evidence remain separate.

The final CodePages dependency fix also passes a fresh-cache local package run:
**828/828** on modern .NET 8 and .NET 10, plus **828/828** on the explicit Standard
asset under each runtime (**3,312** passed; zero failed/skipped). Archive metadata
and matching commit/companion assets are verified. Independent LibreOffice cell
paragraph counts, MuPDF/FontTools/FriBidi geometry and source order, three pinned
UA-1 positives/untagged negative and 38 PDF/A-2b files pass again from this build.

The optional Python qualification tools are external development dependencies; the
validator uses Docker's local Unix socket, exact copied bytes and `--network none`,
including in environments where host bind mounts refer to another filesystem.
Run the self-contained `examples/WordNet.Interoperability` sample for editable HTML,
RTF, semantic PDF and managed SVG. Actual execution here is Linux x64 .NET 8/10;
Native Windows/Linux x64/ARM64 source/packed CI results are linked above. Framework
compilation does not qualify 4.6.2 execution. No Word/Aspose, Android/macOS or trimming/AOT claim follows.

## PR #35 review regressions

Thirty-two new cases address the 15 review findings, bringing the editing/template
profile to **86** authored cases and the complete suite to **696**. Independent
OOXML fixtures and schema validation check external/local repeat references,
cell block controls and diagnosed unsupported wrappers, text boxes inside cells,
inherited shared headers, multi-block/row control boundaries (including cross-section and empty-region cases), wrapped section/note
paragraphs, cached controls inside fields, rejected flattened nested execution,
item-scoped repeated notes, Word percent/meridiem/literal formats, unrelated Word
controls and protected inline-control edits. Independent EPUB input exercises
scaled-image rounding through public snapshots. Large cases replace 30,000 styled
runs and atomically create a 20,000-paragraph report with the bulk API, including
cancellation and limit rollback.

Locked restore, warning-free all-target Release builds, **696/696** complete
conversion/OCR cases on each local Linux x64 .NET **8.0.32** and **10.0.12**
(zero failures/skips), formatting, five comparison controls, package/API validation
and package-content inspection pass. The added `AddParagraphs` API is documented
in all five core assets; dependencies are unchanged. The public sample also runs.
Reports are under ignored `artifacts/pr35-review`. These local results do not add
Windows/Framework, ARM64, minimum-runtime, Word/Aspose or PDF/UA qualification.

## Document editing and templates — roadmap #32, section 3

The [editing/template guide](editing-and-templates.md) and
[design contract](editing-template-design.md) describe the additive public API and
bounded grammar. The initial **54** authored editing/template cases brought the
required suite to **664** before review. Fixtures independently construct OOXML and Open XML SDK
validates editable output; PDF assertions check actual text, pages, geometry,
images and labels. Cases cover styled cross-run replacement, Unicode/regex barriers,
bookmark pairs, atomic failure, callback reentrancy and serialized sessions;
composition style/list/bookmark/story/image collisions, default page geometry and
unresolved references; and scalar/JSON/culture merge, nested repeats, conditions,
image bytes/dimensions, note identities, row spans/decoration, missing/null data,
syntax/row/depth/expansion bounds and cancellation. Existing conversion behavior
still uses cached merge-field content.

The standalone `examples/WordNet.Automation` project exercises only the public
core API. It creates a letter, fills an independently authored invoice and assembles
an appendix, exporting DOCX and PDF. MuPDF 1.26.11 independently reads the invoice's
populated text and the assembled document's three pages. The sample emits the
expected image alternative-text/accessibility diagnostic. Generated outputs remain
under ignored `artifacts/automation-sample`.

Locked restore, warning-free all-target Release builds, **664/664** complete
conversion/OCR cases on each Linux x64 .NET **8.0.32** and **10.0.12** (no skips),
format verification, five comparison controls, NuGet/API validation and package
inspection pass. Runtime dependencies remain unchanged and all five assets contain
the same documented editing API. Reports are retained in ignored
`artifacts/editing-templates/tests`. This change does not add local Windows,
Framework-runtime, ARM64, desktop Word/Aspose or arbitrary-template qualification.

## Business-document fidelity — roadmap #32, section 2

The bounded table, drawing, section, numbering, note and reference profiles are
specified in [business fidelity decisions](business-fidelity-design.md) and the
[fidelity guide](document-fidelity.md). That increment brought the suite to **610 cases**, including
86 authored business-fidelity cases and removal of the obsolete XPS image-loss case.
Both complete source suites pass **610/610**, with zero failures/skips, on Linux
x64 .NET **8.0.32** and **10.0.12**. Locked restore, warning-free Release builds
of all five core assets, format verification and NuGet package/API validation pass.
The five Python comparison controls also pass; corpus fixture bytes are unchanged
and the four newly supported cases have updated expected-loss manifests.

The new cases independently validate DOCX markup with Open XML SDK and assert PDF
text, precise positions, physical pages versus labels, linked TOCs/references,
footnote placement, nested row splitting, invalid geometry and destination safety.
An asymmetric four-color raster with a corner marker checks cropping and both
mirror axes across right-angle rotations. Skia-rendered PDF samples for nested
cells, text boxes and image transforms were inspected. MuPDF **1.26.11** through
PyMuPDF **1.26.6** independently renders all **12** XPS transform combinations,
checks **48** transform pixel samples, **8** PNG/JFIF/EXIF density cases at 72/144 dpi,
and validates the nested table's text/colors. The review regressions cover all
reflowable break fallbacks, zero-based page labels, negative crop padding,
body/table/cell bookmarks, DOCX refresh without layout, Lenient drawing/row/box
fallbacks, final note labels/appearance/sections, implicit table layout, Word's
multi-paragraph default TOC, rich field caches and nested text-box content.

Reproduce the optional independent XPS check after the required build:

```sh
WORDNET_BUSINESS_ARTIFACTS=/absolute/path/to/artifacts/business-fidelity/rasters \
  dotnet test tests/wordnet.Conversion.Tests -c Release -f net10.0 --no-build --no-restore \
  --filter FullyQualifiedName~BusinessFidelityTests
python eng/verify-business-xps.py /absolute/path/to/artifacts/business-fidelity/rasters
```

PyMuPDF is a separately installed qualification tool under its own license, not a
wordnet package dependency. Reports and reviewed samples are retained locally in
ignored `artifacts/business-fidelity/`. This run does not qualify Windows,
Framework execution, ARM64, desktop Word/Aspose parity or arbitrary template layout.


## Comparison corpus and preflight

The additive preflight API and twelve-document comparison corpus are described in
[diagnostics](diagnostics.md) and [comparison reproduction](comparison.md). New tests
independently schema-check the authored invoices/contracts/reports/letterheads,
assert identical parse/destination/layout findings against real conversion, check
stable cell/story locations, bounded grouping, privacy, cancellation, stream ownership,
encryption and resource failures, and distinguish provider/writer-only findings.
The optional commercial runner does not change existing required CI gates.
Local Linux x64 evidence: locked restore and warning-free Release builds of all
five assets, **510/510** conversion/OCR tests on each of .NET 8.0.32 and 10.0.12,
format verification and package/API validation pass. All twelve free comparison
cases pass exact text/page checks and independent POI editable DOC reading order;
three Python control tests pass, including actual displaced/missing-text/extra-page
PDFs and stale-raster rejection. The historical performance harness also builds
against the earlier core API. Raw reports are under ignored `artifacts/comparison*`.
Word/Aspose reference execution and Windows/ARM64/Framework runtime qualification
were not performed here.


## PR #33 review corrections

The review adds fifteen regression cases for Strict inspection through full layout,
model-only fixed-page losses without pagination, caller-font glyph fallback and list
mappings, Lenient rejection prediction, and row/cell/numbering/note source locations.
The complete **525-case** suite passes on Linux x64 .NET 8.0.32 and 10.0.12 with no
failures or skips. Locked restore, all five library targets, formatting and package
API compatibility checks pass. The corpus now requires its exact expected warning
set, in addition to text, page and independent editable-DOC checks.

Five Python controls cover text/displacement/page regressions, stale rasters,
missing/unexpected loss diagnostics, portable manifest hashes and CRLF/LF renderer
identities. PowerShell 7.5.3 locally passes the native-stderr/failure checks and
captures the actual Poppler banner identically to Python. CI additionally executes
the same helper tests under Windows PowerShell 5.1. Desktop Word and licensed Aspose
were not provisioned locally; their reference availability remains explicit.

Raw review reports are under ignored `artifacts/pr33-review`. Location paths are
materialized only for diagnostic observations, with bounded hash-based deduplication.
The single-core ten-page export smoke check remains subject to the existing latency
and memory caps; it is not a replacement for the alternating-revision CI gate.

## Stable 1.1.0 release qualification

The maintainer requested a stable release after the review corrections below.
The stable candidate keeps the same converter implementation and all **487 cases**;
the PDF producer assertion now expects version 1.1.0. Local source runs pass
487/487 without failures/skips on .NET 8, .NET 10 and the installed Framework 4.8.1.
Locked restore, warning-free Release builds of all five assets, API validation
against 1.0.0 and formatting verification pass. Reports are retained under ignored
`artifacts/release-1.1.0`.
The local three-package set also passes all 487 cases on each of five lanes:
.NET 8, .NET 10, Framework 4.8.1, and the explicit netstandard2.0 asset on .NET 8/10.
The transitive dependency audit reports no known vulnerabilities.

Stable publication still requires the complete native desktop CI matrix on the
final main commit, fresh source and exact-main-package execution on actual
Framework 4.7.2 with the documented legacy JIT, and authorization for all three
package IDs. Earlier alpha qualification is historical evidence. Preserve the
final immutable package hashes and runtime reports with the release.

## PR #5 review candidate — 1.1.0-alpha.2

The required suite now has **487 cases**, including **36 new review regressions**.
They check multi-paragraph/cell and nested fields in DOC/DOCX, numbered-heading
font registration, independent HTML reflow, Strict revision rejection, byte-array
result reporting, JPEG termination/trailing data, unsupported-image placeholders,
font weights/inheritance, DOCX schema ordering, cell tab origins, decorated tabs,
XPS retained features, first-page HTML stories and Lenient cell scaling.

All five library targets compile with warnings-as-errors. Locked restore, formatting,
release-version checks and the transitive dependency vulnerability audit pass.
The initial review build (`636621d`) passes **487/487** on each of .NET 8.0.32 and .NET 10.0.12
with zero failures/skips. Independent veraPDF 1.30.2 validation also passes
**38/38** archival corpus files; reports are under `artifacts/pr5-review/pdfa-final`.
The initial Linux x64 source reports are retained under ignored
`artifacts/pr5-review/source-final`; exact-package reports are retained under
`artifacts/pr5-review/package-tests`. Each full run must execute all 487 cases
without failures or skips. `OMP_THREAD_LIMIT=1` caps native OCR worker threads on
this five-CPU host without changing OCR assertions or runtime security settings.

The follow-up scaling correction anchors indented continuation lines to the cell's
text area and scaled header-table rows to the page margin. The existing regression
covers both cases; the 40 focused review/pagination/raster cases pass on .NET 8/10.
The refreshed final-candidate evidence is retained under `artifacts/pr5-final`.

The alpha.1 results below qualify the earlier revision. Publication of alpha.2
requires successful exact-main CI, current actual-Framework-4.7.2 legacy-JIT
source/package evidence, and authorization for all three NuGet package IDs.
Windows/ARM64/Android runtime and minimum-Framework execution are not claimed for
this Linux review task.

## Enterprise acceptance profile — 2026-10-16

The source suite now has **451 cases**, including the original 28 directions and
English OCR, two HTML directions, the 40-document authored enterprise corpus, font
catalog/TTC/CFF/GPOS and image pixel/alpha checks, header/footer/field pagination, aligned
tabs, HTML safety/decoration, Lenient geometry and three raster regression baselines.
The complete source suite runs on Windows 11 x64 .NET 8, .NET 10.0.12 and installed
Framework 4.8.1 (the net472 test asset), with no failures/skips. All five library
targets build with warnings-as-errors. The net462 managed-export executable also
runs on Framework 4.8.1; this does not qualify an actual 4.6.2 installation.

`eng/test-package.ps1` exercises the exact three-package preview on net8/net10/net472,
then explicitly substitutes the core netstandard2.0 DLL on both modern runtimes.
It checks suite completeness, framework identity, package manifests and font notices.
The package gate requires all **451 cases in each of those five lanes** with
zero failures/skips (2,255 package cases). Source verification separately requires all
451 cases in each of the three desktop lanes (1,353 source cases).
API validation compares against 1.0.0; its old provider references are resolved for
comparison without adding native dependencies back to core. No GAC/single-DLL or
strong-name gate applies under the consumer's NuGet-only deployment decision.

The 40 authored files and manifest are described in
[enterprise provenance](../tests/wordnet.Conversion.Tests/Fixtures/Enterprise/README.md).
`word-pages.csv` records independent Microsoft Word 16.0 / build 16.0.20430 counts
and hashes for all 38 valid inputs. **36/36 business cases match exactly**, including
1/5/25-page variants and ten-page text/table and text/table/image benchmarks. The two hostile-size stress inputs
are categorized separately before measurement; they are not business parity cases.
This meets the page-count tolerance on this authored set, not arbitrary template or
line-break parity. `eng/measure-word-reference.ps1` regenerates the Word measurements
with an isolated, read-only Word instance. Office is not a production dependency.
`eng/update-enterprise-goldens.ps1` regenerates
the embedded-font, 72-DPI PDF regression PNGs for review; the tests compare pixels
with a documented antialiasing tolerance. Native rendering remains test-only.

`tools/wordnet.ManagedExport.Probe` exports eight mixed text/story/nested-table/raster
fixtures using one converter. With a fixed Clock, **64 workers × 200 iterations**
produce 12,800 byte-identical PDF/HTML pairs and load no Skia/HarfBuzz/OCR provider.
Run from an isolated copy of the probe output so Windows file locks cannot interfere
with subsequent builds. A 4 GiB GC heap cap bounds the stress process; this is not a
per-document memory guarantee. The probe reports elapsed time and sampled memory.

The BenchmarkDotNet 0.15.8 project supports the authored ten-page text/table/image fixture:

```powershell
$env:WORDNET_BENCHMARK_INPUT = (Resolve-Path tests/wordnet.Conversion.Tests/Fixtures/Enterprise/benchmark-10-pages-images.docx).Path
dotnet run --project tools/wordnet.Benchmarks -c Release -- --filter '*' --job short --warmupCount 3 --iterationCount 5 --artifacts artifacts/enterprise-audit/benchmarks
```

The same project contains a qualification harness using the common 1.0 stream API.
`eng/run-performance-gate.ps1 -ReferenceRevision <commit>` archives the reference,
builds the identical current harness against both revisions, and alternates two warm
workers per variant on the same runner with a verified single-processor affinity.
Each worker warms 20 conversions and measures 31; output must contain ten pages,
ten tables and two images. The comparison pools 62 measured samples per variant.

On Core Ultra 9 185H / Windows 11 x64 / .NET 10.0.12, candidate median was **75.796 ms**
versus **141.330 ms** for 1.0.0 (ratio **0.5363**), on affinity `0000000000000001`.
Maximum sampled managed heap was **25,058,640 bytes**. The more conservative upper
bound, collected heap before conversion plus all managed bytes allocated during it,
was **40,122,840 bytes**. Both are below 100,000,000 bytes. This bound includes
temporary allocations even when collected; it is distinct from process RSS.
Fixture SHA-256: `258aa1dccf93e95b3442b3a642cd3f32e15ec32cb971cc6cccb38b01188e0c11`.
Raw local reports are under ignored `artifacts/performance-completion1/measurements`.
These measurements identify the exact assembly hash, including an uncommitted build.

CI runs the same comparison against the PR base (or previous pushed revision), on
the same hardware/runtime/input, and rejects median ≥300 ms, managed upper bound or
observed peak ≥100 MB, or a regression >25%. Seven guard regressions in
`eng/test-performance-gate.ps1` reject incomplete, mismatched and failing evidence.
Historical BDN text/table-only measurements were 60.887 ms PDF / 1.300 ms HTML;
they had no image or affinity restriction and are not the acceptance measurements.

`eng/validate-pdfa.ps1` exports all 38 valid authored fixtures with PDF/A-2b and
outlines, then validates each with veraPDF 1.30.2, pinned by image digest. **38/38 pass**.
CI repeats this independent check and uploads its XML. Tests also assert metadata
uniqueness, embedded/selectable text, compression controls, actual-page hierarchical
outlines, PNG prediction/alpha and link flags. Tagged PDF was not implemented at
this historical checkpoint; later opt-in profiles are documented above.
PNG admission bounds chunk lengths and zlib expansion to the declared scanline
geometry (including Adam7) before provider allocations, verifies Adler32 and checks
cancellation. Regressions reject excessive IDAT expansion and absent chunk payloads.

Native Ubuntu 24.04 x64 CI also passed the comparable single-core gate: 28.559 ms
median versus 53.861 ms baseline, ratio 0.5302, conservative managed bound 38,258,104
bytes. Run 38820196850 retains the measurements; its separate desktop jobs failed
at the probe RID restore and were corrected independently.

The [final implementation CI](https://github.com/kirakosyan/word.net/actions/runs/38822484354)
passes all 26 Windows/Linux x64/ARM64 source/package lanes: 11,726 cases, no skips,
plus the independent PDF/A, performance, concurrency and resource gates. The older
qualification benchmark now preserves the original 28 directions and DOM-validates
both new output-only HTML directions instead of trying to read HTML back as Word.

Actual Framework 4.7.2 x64 (release 461814) separately passes **451/451**, including
OCR, with the documented legacy JIT (`compatjit.dll` observed). The software-emulated
run took 1479.881 seconds; native prerequisites were copied to the guest's local disk.
The exact net462 package asset also passes managed-only mixed PDF/HTML probes there.
The audit records package hashes, the retained earlier failure and evidence locations.

The Android ARM64 test app compiles in CI. No new Android device execution, actual
4.6.2 runtime, macOS or AOT qualification is claimed. Compilation or VM startup is
never treated as runtime qualification.

## Historical 1.0 matrix

All **28 directions** run real acceptance tests:

- DOC ↔ DOCX: two.
- Each DOC/DOCX ↔ PDF, Markdown, TXT, AZW3, EPUB and MOBI: 24.
- DOC/DOCX → XPS: two.

Four more cases cover **scanned and mixed PDF → DOC and DOCX**.
They assert editable text and reading order. Embedding the page image cannot pass.
Missing OCR prerequisites fail with guidance, rather than silently skipping.

The 1.0 suite contained **348 active cases**. [CI run 38629510750](https://github.com/kirakosyan/word.net/actions/runs/38629510750)
passes all nine native source and 17 exact-package runtime lanes: **9,048 passing cases,
zero failures/skips**. These include native Windows/Linux x64 and ARM64 .NET 8/10,
Windows x64 Framework, and explicit Standard-asset execution on both modern runtimes.
All Windows/Linux x64 mutation, concurrent conversion and OCR resource campaigns pass.
The dated 301-case reports below describe the earlier converter revision.

The 1.0 release scope excludes Android and macOS at the maintainer's request. Historical
Android results remain evidence for those earlier builds; they are not current support claims.

Current 1.0 source and local package qualification also execute all 348 cases on actual Framework 4.7.2
x64 (release 461814), using the explicit legacy JIT application configuration. Every
case passes with no skips. The environment records `compatjit.dll` 4.7.3460.0 and the
unmodified configuration hash. See [minimum-runtime evidence](minimum-runtime-qualification.md#current-10-desktop-qualification--2026-10-14).

The suite also covers Unicode, structural formatting, long ebook records, independent
DOCX validation, PDF wrapping/pagination/glyph bounds, XPS resources, strict loss,
streams, cancellation, limits and malformed/hostile input.

## Reproduce locally

Install the SDK in `global.json` and .NET 8/10 runtimes. In one PowerShell process:

```powershell
# Linux: install libtesseract5, libfontconfig1, fontconfig and fonts (see below).
./eng/prepare-ocr-tests.ps1
dotnet restore wordnet.sln --locked-mode
dotnet build wordnet.sln -c Release --no-restore
dotnet test tests/wordnet.Conversion.Tests -c Release -f net8.0 --no-build --no-restore
dotnet test tests/wordnet.Conversion.Tests -c Release -f net10.0 --no-build --no-restore
```

Setup sets `WORDNET_TESSDATA` and `WORDNET_TESSERACT_LIBRARY` in that process.
Windows prerequisite/model downloads are SHA-256 pinned. Override `-NativeLibraryPath`
for a custom library; Linux/macOS use locally installed Tesseract 5. Conversion itself
never downloads prerequisites.

On Windows ARM64, provision x64 OCR for the installed x64 Framework host:

```powershell
./eng/prepare-ocr-tests.ps1 -Architecture x64
dotnet test tests/wordnet.Conversion.Tests -c Release -f net472 --no-build --no-restore -- RunConfiguration.TargetPlatform=x64
```

Switch setup back to ARM64 before native modern tests. Framework tests run only on
Windows. Agents prefix commands with `rtk` per AGENTS.md.

## PR #3 review regressions — 2026-10-14

Forty-seven new cases cover Word-default tables that overhang text margins, physical-page
rejection, negative table indentation, omitted grid columns, wrapped cells, unspecified
grid widths, absolute DOC row edges, raw DOC orientation operands, signed vertical
margins, HTML span/empty-table compatibility, row headers, and default versus opt-in OCR
column order with paragraph boundaries. Test DOCX/EPUB markup is authored independently
in `ProductionReviewTests`; DOC operands/row boundaries are inspected or modified
according to published binary fields, independently of the production reader.

All **348 cases pass**, without failures/skips, on AK-PC Windows x64 .NET 8, .NET 10
and the installed Framework **4.8.1** host targeting `net472`. Locked solution restore,
warning-free Release builds of all four library targets, and formatting checks pass.
Reports are retained under ignored `artifacts/pr-review/test-results/`.
Actual Framework 4.7.2 and Android device execution were not repeated for these converter
changes. The earlier 301-case minimum-runtime results below do not qualify the new revision.
Android API 21 remains unresolved and further device/emulator work is deferred by the user.

Apache POI 5.4.1 passes both `--check-layout` and `--check-layout-corpus`, including new
assertions for the actual portrait `0x01` and landscape `0x02` operands. Its high-level
`getDmOrientPage()` exposes a Boolean, so the verifier uses POI's section index and sprm
parser on the original section bytes to distinguish the published values. The managed
suite also checks emitted bytes directly and imports explicit `0`, `1`, and `2` operands.

Framework runner checks pass on Windows PowerShell 5.1 and PowerShell 7, including a
simulated transient loader-enumeration error followed by a module appearing after one
second. The polling uses the existing overall deadline. Android process-command checks
cover plain toolbox `ps` on API 21/25 and toybox `ps -A` on API 26/36 without device execution.
These are harness checks, not platform conversion qualification.

The standalone Framework runner also passes all 348 cases on installed Framework 4.8.1,
with actual JIT-module evidence. Its generated ZIP includes the new polling helper and
the current required-case count. Android locked restore and Release APK compilation pass;
the updated harness was not executed on a device.

The locally packed validation version `0.1.0-alpha.10.review.1`, built from converter
commit `0f039b3`, passes all 348 cases on each of five asset/runtime lanes: `net8.0`,
`net10.0`, `net472` on installed Framework 4.8.1, and explicit `netstandard2.0` loading
on .NET 8 and .NET 10. Restore uses an isolated cache and a distinct validation version
to avoid reusing an earlier unpublished alpha.10 package or its lock-file hash.
The preserved archive is `artifacts/pr-review/wordnet.0.1.0-alpha.10.review.1.tested.nupkg`,
SHA-256 `e06105dac7fe8277a3c9f38e2d1cae1a0f2a0c698226984d1cddf8e68a06d63b`.
Package structure, notices and framework/API checks pass; nothing was published.
Reports are under `artifacts/pr-review/package-results/`.

## Production candidate — initial alpha.10, 2026-10-13

The shared suite now has **301 active cases**, preserving all 28 directions and original
scanned/mixed OCR cases. New assertions cover section geometry/empty final sections,
paragraph spacing/indent inheritance, keep controls, merged multi-paragraph tables,
repeating headers, long-row splitting, malformed-provider regressions and runtime asset
identity. `eng/required-tests.txt` is the common minimum used by CI and device/Framework
runners; missing cases and skips fail qualification.

`LayoutCorpusTests` compares every output glyph baseline to an independently positioned
ReportLab reference within 0.03 points. Apache POI independently reads exported DOC
layout/table records. Two Poppler-rendered reference-corpus pages have been visually
inspected. The [fixture manifest](../tests/wordnet.Conversion.Tests/Fixtures/README.md)
records producers, fixed fonts, licenses, source revisions and hashes.

Eight OCR scenarios run to both DOC and DOCX: clean, rotated 90/180 degrees, skewed,
noisy, low-resolution, two-column and an independently supplied Tesseract test scan.
The required normalized CER is ≤3%, WER ≤8%, plus editable text and column reading order.
The local ARM64 run records CER/WER zero for all 16 outputs; desktop TRX contains
`OCR_METRIC` lines. This is a bounded regression corpus, not a general OCR benchmark.

The new qualification tool runs 1,024 deterministic mutations in separate processes,
four-way concurrent conversions across all 28 directions and isolated OCR memory/deadline
checks. See [hosting](hosting.md) for commands, thresholds and limitations.
CI repeats the campaigns on native Windows/Linux x64 and uploads JSON measurements.

`eng/test-package.ps1 -Version 0.1.0-alpha.10 -Commit <full-source-sha>` verifies one
packed artifact and runs the complete .NET 8/10 suite against it. `-IncludeFramework`
adds Windows Framework. It then extracts that archive's `netstandard2.0` DLL and runs
both modern suites again, with a required runtime-identity assertion. CI performs this
on four native desktop platforms using the same package artifact. API validation compares
all four targets and the published alpha.9 baseline. These checks do not publish a package.

Implementation commit `264a29c40ddc74b6c97f198fcb2e6b934527faf9` passes
[native desktop CI](https://github.com/kirakosyan/word.net/actions/runs/38466027553):
all nine source runtime lanes and 17 exact-package lanes pass 301 cases each, with no
failures/skips (7,826 cases total). The package lanes include explicit Standard-asset
execution on both .NET 8 and 10 for every native Windows/Linux architecture. CI also
passes 1,024 isolated mutations, 896 concurrent measured conversions and 16 isolated OCR
cases on each native x64 OS. JSON reports include library hashes, runtime, duration and
sampled memory; see [production measurements](production-readiness.md#measurements-and-limits).

The CI package's SHA-256 is
`5037daf3f5a770c443dcd143b56f701cb9ec37c2135b69a21c4f8115892a3f5d`.
Local Windows source and packed suites also pass. The Samsung SM-S926B/Android 16 ARM64
device passes all 301 source and locally packed cases on .NET 10.0.12. It also passes
all 301 cases against the downloaded CI artifact with the SHA-256 above, using a separate
package cache and candidate lock file. That makes 8,127 passing desktop/device cases for
the CI artifact/source qualification set. Two initial
device failures were test-data integer-to-byte binding differences in the reflection
runner; typed byte constants now execute those malformed-PDF tests. Earlier device
attempts timed out while the activity was suspended. The harness now keeps its visible
screen awake and records per-test progress; unlocking/bringing it forward remains
necessary if the device's keyguard suspends the app.

Local Framework is installed 4.8.1 (release 533509), not actual 4.7.2. Actual Framework
4.7.2 and Android API 21 remain open gates; macOS is deferred. Evidence is under
`artifacts/production-tests/`, including downloaded CI reports, package and assembly hashes.

The returned standalone-bundle report from an additional Windows x64 PC passes all
301 cases with no failures/skips in 15.08 seconds on 2026-10-13. Its test case names and
both assembly hashes match the supplied bundle. The environment records Framework
release `533509`, which is [Framework 4.8.1 on Windows 11](https://learn.microsoft.com/en-us/dotnet/framework/install/versions-and-dependencies#net-framework-481),
and `exactFramework472: false`. This adds execution evidence for the installed newer
runtime; it does not close the actual 4.7.2 gate. The retained reports and verification
summary are under `artifacts/production-tests/returned-windows-x64-20261013/`:

- TRX SHA-256: `d8be41134ce654fd1a593127876ad1d6cffd1f20360e15a01cb1a30baa4bf258`.
- Environment SHA-256: `765230beeaaf687dcb08ee0949cb35df959229a68d33b89cd20761983e4d874f`.
- Library SHA-256: `271c239235216397ffa92a061e7f21075e5dd1945f9dea6518f7f40ecea0006a`.
- Test executable SHA-256: `782f9f67cdd971afe170d747f876dd0e6448abff4fec1d21796b5596a3ea1609`.

`eng/run-framework-tests.ps1` can run the complete prebuilt x64 output on another Windows
PC without an SDK and records assembly hashes and the Framework release key. Add
`-RequireFramework472` to enforce the actual minimum-runtime gate. See the
[API/support policy](api-compatibility.md) for copying the runner and prerequisites.

## AK-PC runtime investigation — 2026-10-14

Library and conversion test sources remain at `70bfc5585d4544bac89e7246d88cb5fc0a31c5e0`.
The conversion implementation is unchanged. Compatibility harnesses now select
explicit runtime configurations and record them in qualification evidence.

| Environment | Executed result |
| --- | --- |
| Native AK-PC .NET 8 / .NET 10 | 301 passing cases each, no failures/skips |
| Native AK-PC Framework 4.8.1, release 533509 | 301 passing cases, no failures/skips; final standalone runner: 42.477 seconds |
| Actual Framework 4.7.2, release 461814 | 301 executed, 268 passed, 33 failed, no skips; reproduced under Hyper-V with CLR/JIT 4.7.4150.0 and default JIT settings |
| Actual Framework 4.7.2, explicit legacy x64 JIT | 301 passed, no failures/skips; source run 21.193 seconds, packaged asset run 19.395 seconds on CLR/compatjit 4.7.3460.0 |
| API 21 ARM64 software emulator, .NET 10.0.12 interpreter | 301 passing source cases, no failures/skips; eventual report collected after the original runner timed out |
| API 21 ARM64 default JIT | Startup signal 11 before tests, also reproduced by a minimal app without WordNet/provider references |
| API 21 ARM64 interpreter repeat runs | Native graphics crashes after 141 cases (workload 36.1.69) and 106 cases (36.1.2); no complete report |

The Framework runs use library SHA-256
`b00a533397bc7bc250353f56e6a3644cb722f48b46f4c27d02b63f6eac43ae04`
and test executable SHA-256
`a6805040c166b22a2b4a04ec371948d34e4754f0b01d23bfc3d3f3bd64bb8f79`.
The interpreter APK SHA-256 is
`906aa507d2a0c42f63a1920c2ee8233d5f75e820b54a7353ad6b40a6739cc9e7`.
That APK predates the new harness metadata and timings; its execution mode was
recorded at build time and matched against the installed APK before report collection.
The packaged 4.7.2 run used archive SHA-256
`5e12d8a77c93797f23a081834a4002e8954cc967dcedd31dae6152420ce20b75`.
Its environment report confirms `compatjit.dll` loaded, records the child-only legacy
setting and hashes the unmodified application configuration. The archive's Framework
asset and the restored consumer's library are byte-identical. Default-JIT 4.7.2
operation is still unqualified; consumers must select the documented application setting.
API 21 source results add no exact-package or physical-device qualification. See the
[runbook](minimum-runtime-qualification.md) for reproduction and limits.
The later native graphics crashes prevent treating the earlier interpreter pass as
a repeatable baseline. Further Android investigation stopped at the user's request;
the API 21 failure remains unresolved.

Reports are retained under ignored `artifacts/runtime-diagnosis/`, including
`host-tests/`, `ak-pc-native-final/`, `framework472-hyperv/` and the Android baseline
XML/status/build manifest. The original software-emulated failures remain in
`artifacts/ak-pc-qualification/`. VM disks/system images are disposable after reports
are archived; they are not release artifacts.

The Framework runner now records CLR/JIT file versions and known diagnostic overrides,
rejects those overrides for the actual-minimum gate, enforces an external process
deadline, preserves previous reports and captures both output streams. Direct process
ownership fixes a missing exit code observed with Windows PowerShell 5.1 redirection.
`eng/test-framework-runner.ps1` verifies timeout rejection, termination of the exact
worker, fast failure exit-code preservation and stale-report rejection on Windows
PowerShell 5.1 and PowerShell 7 locally. CI is configured to repeat the checks on
Windows x64. Its negative fixtures are not conversion results.

The Android runner handles a UTF-8 BOM transported by ADB, records the APK hash and
requested execution mode before startup, checks the report's actual mode/API/architecture,
and detects process exit without waiting for the full suite deadline. The app records
case arguments, durations, API and execution mode. `eng/test-android-report.ps1` checks
BOM/plain XML and rejection of incomplete or non-report output; CI includes those
parser checks without requiring an Android workload. The rebuilt JIT app reproduced
the API 21 crash, and the updated runner detected its exit without a complete report.
Removing `am start -W` lets the host own the startup wait; that legacy Android command
had remained blocked after the native crash.

The evidence archive is `artifacts/runtime-diagnosis/qualification-evidence-20261014.zip`,
SHA-256 `b3a969f0ed9d242f5bd4db340edd9f72960eede61523e9cf21b64024cd7e1fbe`.
After verification, both Windows VM disks, the Hyper-V registration, Docker qualification
container/image and isolated Android system images were removed at the user's request,
reclaiming 31.29 GiB on the host. Docker Desktop and unrelated caches remain installed.
`artifacts/runtime-diagnosis/cleanup-status.json` records the completed cleanup.

The final repair evidence is retained in
`artifacts/runtime-repair/qualification-evidence-20261014-final.zip`, SHA-256
`b4aeaf076fbe98cdd72777fe2f0bb2cd06823180027105bb772fb6d5965bd119`.
Its 41 entries include a hash manifest, source/package Framework reports, runner
negative checks, and both later Android native-crash reports. The exact tested NuGet
archive is preserved separately as
`artifacts/runtime-repair/wordnet.0.1.0-alpha.10.tested.nupkg`.
The repair VM and its installation media (38.37 GiB of file lengths), temporary Docker
debugger container, and isolated API 21 emulator directory were removed after preserving
evidence. Shared Android SDKs, Docker Desktop and unrelated caches were retained.

## PR review regressions — alpha.9, 2026-10-13 (prior revision)

That suite contains **236 cases**, including all 28 directions, the four original OCR
acceptance cases, and 39 review regressions. The new tests check DOC image-store records
and nested numbering against binary field definitions, zero starts and list restarts,
linked numbering styles and cycles, style inheritance, moved text, table-header emphasis,
EPUB URL handling and image scaling, PDF text-matrix scaling, Kindle output budgets,
multi-page confidence including blank scans, concurrent OCR and final-write cancellation.

All 236 cases pass without failures or skips on each of these runtime lanes:

| Runtime | Evidence |
| --- | --- |
| Windows x64 | Hosted Windows Server 2022: .NET 8, .NET 10 and Framework |
| Windows ARM64 | Hosted Windows 11 and local ARM64: .NET 8 and .NET 10 |
| Linux x64 | Native hosted Ubuntu 24.04: .NET 8 and .NET 10, default runtime settings |
| Linux ARM64 | Native hosted Ubuntu 24.04: .NET 8 and .NET 10, default runtime settings |
| Android ARM64 | Samsung SM-S926B, Android 16, .NET 10.0.12; 236 shared cases including OCR |

The [desktop CI run](https://github.com/kirakosyan/word.net/actions/runs/38442966251)
tests implementation commit `b39d3ff`; its nine reports contain 2,124 passing cases.
Local Windows reports and the device report are under `artifacts/test-results/review/`,
with downloaded CI reports in its `ci/` directory. The local x64 Framework host also
passes all 236 cases. This closes the earlier native Linux x64 qualification gap for
the documented profile; broader fidelity, minimum OS versions and macOS remain deferred.
The updated OCR loader retains native libraries for process lifetime, and the prior
Linux x64 exit-139 crash did not recur in these complete native runs.

Locked restore, all four library targets, formatting, package API compatibility and
packing pass. Apache POI independently validates the revised DOC image records and
formatting. The Android incremental build initially failed at activity startup; a clean
`dotnet build tests/wordnet.Android.Compatibility -c Release --no-restore -t:Rebuild`,
followed by `-t:SignAndroidPackage`, regenerated the app and all 236 device cases passed.
The Android workflow now provisions its pinned native libraries and language data before
building the shared test application; its [successful CI job](https://github.com/kirakosyan/word.net/actions/runs/38442966308)
remains compilation-only.

## Formatting and images — alpha.8, 2026-10-13 (prior revision)

All **197 cases pass without failures or skips** on each of these eight runtime runs
(1,576 passing cases total):

| Runtime | Execution evidence |
| --- | --- |
| Windows ARM64 | .NET 8 and .NET 10, native processes |
| Windows x64 Framework | Installed Framework, x64 test host on the ARM64 machine |
| Linux ARM64 | Ubuntu 24.04 container, .NET 8.0.31 and .NET 10.0.12, default runtime settings |
| Linux x64 | Ubuntu 24.04 container under QEMU, .NET 8.0.31 and .NET 10.0.12, scoped `DOTNET_EnableWriteXorExecute=0` |
| Android ARM64 | Samsung SM-S926B, Android 16, .NET 10.0.12; shared suite and native English OCR |

The suite grows from 150 to 197 cases: 48 new formatting/image/defensive cases replace
one former Markdown-list-loss case with editable-list coverage. All 28 directions and
four scanned/mixed OCR cases remain active. Assertions cover fonts/colors, style inheritance,
table geometry/padding, editable lists, real PNG/JPEG pixels and bytes, dimensions,
pagination, independent DOC/PDF/EPUB inputs, malformed images and strict destination safety.

All four library targets build without warnings. Locked solution/Android restores,
formatting and package API compatibility checks pass. The inspected alpha.8 package
contains the four DLL/XML pairs, README, MIT/dependency/font notices, and no test-only
dependencies. Separate NuGet consumers build for all four targets and execute representative
formats, font/color/table/list/image assertions and English OCR on Windows Framework and
.NET 8/.NET 10. Explicit .NET Standard 2.0 loading is identified and exercised on .NET 8/10.
Runtime dependencies are unchanged from alpha.7.

Apache POI 5.4.1 independently reads the generated DOC title's 18-point RGB font,
unequal 120/240-point cells, list start/nesting and exact image bytes/physical dimensions.
The composite PDF was rendered with Poppler and visually checked for spacing, image
aspect ratio, clipping and overlap; numeric assertions supplement this single-page review.

Reports are under `artifacts/test-results/alpha8/`; `verification.json` records report
counters/hashes and package/assembly hashes. Linux containers reuse the alpha.7 prerequisite
images, then overlay the current sources (excluding host bin/obj), restore locked
dependencies and rebuild all targets before executing. They do not run the old assemblies.
Android's stopped activity needed the screen awakened and its own activity brought forward
before the suite completed. No conversion/OCR case was bypassed.

Native Linux x64/default-runtime qualification, minimum OS/runtime versions, broad
real-world fidelity and noisy OCR remain separate work. macOS remains deferred.

## Dependency refresh checks — 2026-10-13

Alpha.7 updates the graphics/native dependencies and PDF decoders and migrates the
tests to xUnit v3. See [the dependency audit](dependencies.md) for exact versions.

- All four library targets build with no warnings. Solution and Android locked restores,
  formatting and package API compatibility checks pass.
- All 150 tests pass with no failures or skips on Windows ARM64 .NET 8/.NET 10,
  Windows x64 .NET Framework and Android ARM64 (Samsung SM-S926B, Android 16).
- Linux ARM64 (Ubuntu 24.04 container) passes all 150 tests on each .NET 8.0.31 and
  .NET 10.0.12 with default runtime settings.
- Linux x64 (Ubuntu 24.04 container under QEMU) also passes all 150 tests on each
  .NET 8.0.31 and .NET 10.0.12. It uses the previously documented container-local
  `DOTNET_EnableWriteXorExecute=0` workaround; native x64/default-runtime qualification
  remains separate. Across these eight runtime runs, 1,200 tests pass and none skip.
- A separate consumer of the alpha.7 NuGet builds for all four targets. Representative
  conversions, DOC formatting/tables and real English OCR pass on Windows x64 .NET
  Framework and Windows ARM64 .NET 8/.NET 10,
  including explicit loading and runtime identification of the .NET Standard 2.0 asset.
- Package inspection confirms that updated runtime pins are present in the dependency
  groups, with no test packages or implicit framework reference packages shipped.
- NuGet reports no deprecated packages or known vulnerabilities for the solution or
  the Android project, including transitive dependencies. Reports and runtime results
  for this build are under `artifacts/test-results/alpha7/`.
  `verification.json` records each report's counters/hash and the package/assembly hashes.

## Namespace migration checks — 2026-10-13

Alpha.6 changes the root namespace to `WordNet`, including embedded font resource
names. The NuGet package ID and assembly name remain `wordnet`. Consumers must update
their imports/qualified names and rebuild.

- Locked restore, Release build for all four library targets, formatting and package
  API compatibility checks pass without warnings.
- All 150 tests pass with no failures or skips on Windows ARM64 .NET 8/.NET 10,
  Windows x64 .NET Framework, and Android ARM64 (Samsung SM-S926B, Android 16).
  The Android app completed after relaunching its stopped activity.
- A separate consumer of the alpha.6 NuGet compiles for all four targets with
  `using WordNet;`. Representative conversions, DOC formatting/tables and English OCR
  pass on .NET 8/.NET 10, including explicit loading of the .NET Standard 2.0 asset.
- The package contains the four matching DLL/XML pairs and `WordNet` API documentation.
  Reports are under `artifacts/test-results/alpha6/`.

Linux was not rerun for this namespace migration; the alpha.5 results below describe
the previous build. macOS remains deferred.

## Alpha.5 local evidence — 2026-10-12

| Check | Result |
| --- | --- |
| Release build | All four library targets, no warnings |
| Windows ARM64 .NET 8 / .NET 10 | 150 tests per target, 0 failed/skipped |
| Windows x64 Framework / .NET 10 | 150 tests per target, 0 failed/skipped, x64 processes on the ARM64 host |
| Linux ARM64, Ubuntu 24.04 container | 150 tests on each .NET 8.0.31 / .NET 10.0.12, 0 failed/skipped |
| Linux x64, Ubuntu 24.04 container under QEMU | 150 tests on each .NET 8.0.31 / .NET 10.0.12, 0 failed/skipped; container-local runtime workaround described below |
| Android ARM64, Samsung SM-S926B, Android 16 | Same 150 tests on .NET 10.0.12, 0 failed/skipped, including 28 directions and four OCR cases |
| DOC interoperability | Apache POI 5.4.1 verifies exported headings, emphasis, editable cells/rows, repeating headers and left/center/right alignment; independent input fixtures |
| MOBI / KF8 interoperability | KindleUnpack recovered generated text; no device rendering check |
| PDF visual check | Poppler-rendered page layout and alignment inspected; numeric wrapped-line and table-cell alignment assertions; broad visual corpus still pending |
| Formatting/package | Locked restore, formatting and framework API compatibility checks |
| Packed alpha.5 | Full 150-test suite on Windows Framework/.NET 8/.NET 10, Linux ARM64 and emulated x64 .NET 8/.NET 10, and Android ARM64; separate representative consumers also pass |
| Explicit .NET Standard asset | Runtime identity verified; new format engines and OCR executed on .NET 8 and .NET 10 |
| Package inspection | Four DLL/XML pairs, README, MIT/dependency notices and embedded-font license/provenance present |

The Framework host uses the installed runtime, not necessarily the oldest 4.7.2 runtime.
These results cover the specified profiles, not general rich DOC/KF8 fidelity or real-world
OCR accuracy. Windows x64 .NET 8, the oldest claimed Framework/Android runtimes and broader
device/native-codec coverage remain unqualified. Linux x64 now executes the complete suite
under QEMU with a container-local executable-memory workaround. This does not establish
native x64 performance or default-runtime qualification; a native Linux x64 CI run is still
required for those claims. Four Windows/Linux CI lanes are
configured; configuration is not evidence of remote execution. macOS is deferred.

## Linux and Android runners

`eng/linux-tests.Dockerfile` restores locked dependencies, builds all four library targets,
provisions Tesseract and registers the pinned Noto Sans faces for PDF fallback. Its test
runner executes .NET 8 and .NET 10. For example, from the repository root:

```sh
docker build -f eng/linux-tests.Dockerfile -t wordnet-validation .
docker run --rm --mount type=bind,source=/absolute/path/to/results,target=/results wordnet-validation
```

For **local x64 emulation on the ARM64 host only**, the default .NET executable-memory
aliasing failed inside MSBuild/reflection before tests could run. Disabling hardware
intrinsics or ReadyToRun did not fix it. The following scoped workaround allowed all
four library targets to build and the .NET 8/10 conversion/OCR suites to complete:

```sh
docker build --platform linux/amd64 --build-arg DOTNET_EnableWriteXorExecute=0 -f eng/linux-tests.Dockerfile -t wordnet-validation:x64 .
docker run --rm --platform linux/amd64 -e DOTNET_EnableWriteXorExecute=0 --mount type=bind,source=/absolute/path/to/results,target=/results wordnet-validation:x64
```

The build argument applies only during the validation-image build. The runtime override
is passed to that test container, never to the library, NuGet, host or native CI lanes.
It disables .NET's W^X executable-memory separation; it is a validation workaround, not
a deployment recommendation. The setting is documented in the
[.NET runtime source](https://github.com/dotnet/runtime/blob/v10.0.0/src/coreclr/inc/clrconfigvalues.h).
Keep native tests on runtime defaults. The runner records architecture, runtime, override
and package mode alongside the TRX reports. Alpha.5 reports are under
`artifacts/test-results/alpha5/`; older reports in other directories describe prior builds.
The local `verification.json` records the package/assembly hashes and verifies that each
listed report contains all 150 passing cases, including 28 directions, four OCR cases
and 24 new alignment regressions. No conversion or OCR case is skipped.

On an ordinary Linux host, install `libtesseract5`, `libfontconfig1` and `fontconfig`, copy
`src/wordnet/Resources/Fonts/*.ttf` into `~/.local/share/fonts/wordnet/`, then run `fc-cache -f`.
The fontconfig-enabled Skia asset is required for rasterizing unembedded PDF text; the
former no-dependencies asset failed mixed-page OCR in the minimal Linux environment.

The Android app links the same desktop test sources and fixtures. It discovers every
Fact/InlineData case, awaits async tests and writes a machine-readable XML result.
It has no network permission. Prerequisites and outputs remain under ignored `artifacts/`.

```powershell
./eng/prepare-android-tests.ps1
dotnet restore tests/wordnet.Android.Compatibility --locked-mode
dotnet build tests/wordnet.Android.Compatibility -c Release --no-restore -t:SignAndroidPackage
./eng/run-android-tests.ps1 -AdbPath /absolute/path/to/adb
```

The runner installs only `org.wordnet.validation`, clears that app's previous report,
launches its tests and fails on missing results, failures or skipped cases. Add `-Serial`
when several devices are connected. It bundles Tesseract4Android native ARM64 assets and
English data solely as test prerequisites. The app is debuggable so ADB can retrieve its
private report, and disables trimming/AOT; those modes are not qualified by this run.

## Test the actual NuGet

Both test projects accept `WordnetPackageVersion` to replace their project reference
with the local package. Source and package modes keep separate lock files; package-mode
locks are generated under ignored `obj/packaged/`. The validation configuration maps
`wordnet` exclusively to `artifacts/packages` and other dependencies to nuget.org.

```powershell
dotnet restore tests/wordnet.Conversion.Tests -p:WordnetPackageVersion=0.1.0-alpha.9 --configfile eng/NuGet.Packages.Config
dotnet build tests/wordnet.Conversion.Tests -c Release --no-restore -p:WordnetPackageVersion=0.1.0-alpha.9
./eng/prepare-ocr-tests.ps1
dotnet test tests/wordnet.Conversion.Tests -c Release -f net10.0 --no-build --no-restore -p:WordnetPackageVersion=0.1.0-alpha.9
```

Repeat for .NET 8 and Windows Framework with the appropriate OCR architecture. For
Android, pass the same property/configuration during restore and build, then use
`run-android-tests.ps1` normally. For the Linux runner, set `WORDNET_PACKAGE_VERSION`
after restoring/building that package configuration. Restore again without the property
to return to source development. Never treat a previously cached package as a new build;
use a new prerelease version when implementation changes.

## Fixtures and assertions

[Fixture provenance](../tests/wordnet.Conversion.Tests/Fixtures/README.md) records sources
and hashes. DOCX XML parts, EPUB/MOBI generators and ReportLab/Pillow PDFs are independent
of production serialization. AZW3 originates with wordnet and was externally unpacked;
it is not an independent writer fixture.

| Output | Current assertions |
| --- | --- |
| DOCX | Open XML SDK schema validation, editable text and supported structure |
| DOC | Compound storage, FIB/text, 17 formatting/table regressions, independent input and external output checks |
| Alignment | 24 additional regressions: independent DOC table input, Word/ebook exchange, DOCX defaults/styles/overrides, Markdown columns, PDF/XPS positions, wrapped lines and cell positions, invalid values and explicit loss |
| Appearance/images | 48 cases for font/color inheritance, editable lists, table geometry/padding, embedded raster bytes/pixels/dimensions, independent DOC/PDF/EPUB input, pagination and hostile resources |
| PDF | Parsed content, page count, glyph bounds, wrapping and baselines |
| XPS | Sequence/pages, dimensions, glyph text/coordinates and font resources |
| EPUB | Mimetype/container and XHTML text/structure |
| MOBI / AZW3 | PalmDB/MOBI headers, actual XHTML and supplemental external unpacking |
| OCR | Exact editable English text in expected reading order on scanned/mixed fixtures |

The original clean fixtures use exact matching. The alpha.10 corpus additionally measures
noise, columns, skew and rotation against numerical CER/WER thresholds. Complex tables,
forms and arbitrary real-world scan quality remain outside this qualification.

## Further qualification

Current regressions cover ZIP limits/paths/duplicates, XML entities/depth, encryption,
corrupt DOC/Kindle offsets, truncation, unsupported styling, resource limits, glyphs
and cancellation. Native recognition receives cancellation through Tesseract's progress
monitor; initialization, rasterization and other provider calls can still require a host deadline.

Expand the corpus for images/styles/sections/lists, metadata, Unicode/shaping, noisy OCR
and independent ebook producers. Pin fonts/renderers and numerical tolerances.
Round trips alone can hide matching reader/writer bugs.

Packing checks API compatibility. Inspect assemblies/XML docs, README/notices/licenses
and dependency groups. Execute representative packed consumers and explicitly load
the .NET Standard asset. Android device tests now exercise native loading, fonts/models
and conversions; broaden devices/API levels, memory checks and trimming/AOT coverage.

## Bucket 5 design validation

The [design/acceptance plan](later-design-decisions.md) is documentation only.
Issue-to-design traceability, current source/API/resource contracts, published
specifications and provider/license boundaries were reviewed; changed local links,
anchors, whitespace and the final diff were checked. Planned cases and external
editing/security reviews have not run and are not new runtime qualification.
The 829 required cases belong to the merged implementation, not bucket 5 support.
Future executable samples and build/platform evidence are gates in each ADR; the
existing automation/interoperability samples continue to demonstrate implemented APIs.

## 1.2.0 release candidate

The versioned candidate retains 829 required cases and uses published 1.1.0 as the
API baseline. Current source, fresh-cache package and exact-main CI qualification
are recorded in the release PR linked from roadmap #32 and its CI. The
[release readiness ledger](release-1.2.0-readiness.md) records the remaining gates.
The earlier PR evidence above is dated source history. Actual Framework 4.7.2
current-source and immutable-main-package runs remain publication gates; no current
pass is inferred from historical qualification or hosted newer Framework execution.

The [hosted minimum-runtime jobs](hosted-minimum-runtime.md) run the prepared source
and clean-package bundles on real Windows 2019/Framework 4.7.2 guests under Linux KVM.
Their independent report checks and negative controls are separate from the 829
converter cases. Actual runtime passes and cleanup must be recorded after execution;
this infrastructure addition alone does not qualify the candidate.
