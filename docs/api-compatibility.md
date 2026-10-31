# API and support policy

## Unreleased roadmap #138 API work

The pending #144 source adds the nullable `DocumentContentControl.ParentId`
snapshot property for admitted nested typed owners. This additive property does
not change existing constructor or method signatures; handles retain their
session/version rules. See the [nested-control profile](content-controls.md#unreleased-source-a-bounded-nested-owner)
for the ownership and locking limits. It is not in the published 1.5.0 API.

`InsertFragmentAtTableCellBookmark`, the distinct fragment-options methods and
`FragmentInsertionOptions` add bounded opt-in placement and field policies while
retaining the original fragment signatures. `CreateReviewDisplay` returns a
detached `DocumentReviewDisplay` with immutable revision metadata and explicit
managed PDF/SVG output. `ReviewAnnotationPolicy.NumberedEndNotes` opts into
numbered comments, format changes and paired move indicators. See the [fragment](editing-and-templates.md) and
[review](review-workflows.md#unreleased-source-detached-text-revision-display)
profiles for bounds and exclusions. These signatures are pending source work,
not part of the published 1.5.0 API; exact-framework and package qualification
are reported separately.

Pending #147 broadens existing fragment methods to admit the separately documented
[typed-control](editing-and-templates.md#unreleased-source-typed-controls-in-fragments)
and [chart/equation](editing-and-templates.md#unreleased-source-retained-objects-in-fragments)
profiles, including mixed fragments with separate owners and bounded local glossary
copying, without adding signatures.
Protected-owner and resource failures remain explicit.

Pending #158 adds `WordDocument.InsertCrossTabAtBookmark` and
`WordDocument.InsertCrossTabAtCellBookmark` on all target frameworks. They use
the existing prepared-record and aggregation types; the released append API is
unchanged. Cell placement requires fixed geometry and explicit physical margins.

Pending #163 adds `ChartKind.ClusteredBar` without changing existing enum values
or chart constructors. It uses the existing immutable chart and report-binding
APIs; its managed rendering approximation and profile restrictions are explicit.

Pending #153 admits a bounded standalone-equation/native-paragraph combination
through the existing options and math-font APIs. It adds diagnostics but no public
signatures; notes, charts, mixed inline owners and conformance combinations remain open.

Pending #154 adds `PdfTextBoxReadingOrder` and `PdfOptions.TextBoxReadingOrder`.
The default rejects or diagnoses text boxes as before; `AnchorOrder` explicitly
opts into the bounded source-order profile on all target frameworks.

Pending #155 adds `PdfVectorSemantics` and `PdfOptions.VectorSemantics`. Its
default is `Reject`; callers can explicitly select described figures or
decorative vector artifacts in the bounded managed accessible-PDF profile.

Pending #156 appends `PdfConformance.PdfA2a` without changing existing values or
defaults. It requires the explicit `PdfAccessibility.PdfUa1` semantic profile.

Pending #164 adds `HyphenationLexicon`, `HyphenationOptions` and the additive
`ConversionOptions.Hyphenation` property. A null dictionary preserves default
layout; the initial one-page managed PDF profile reports its search-geometry loss.

Pending #161 adds three default vector preset geometries using existing conversion
and session methods, without new public signatures. It also diagnoses loss of
their authored alternatives and vector titles; existing API shapes remain intact.

Pending #162 extends the existing DOCX and managed conversion paths with bounded
linear gradient paints. It adds no public signatures or new runtime dependencies;
other gradient geometries, transparency and conformance combinations stay outside
the admitted profile.

The pending #168 source API appends `EquationKind.Matrix` and adds immutable
`EquationExpression.Matrix`, `EquationMatrixAlignment` and `EquationMatrixSpacing`.
Existing enum values and equation signatures are retained. The [matrix profile](p2-objects-typography.md#unreleased-source-bounded-matrix-equations)
records the bounds, geometry, unsupported variants and separate qualification status.

The pending #169 API appends `EquationKind.Nary` and adds `EquationExpression.Nary`,
`EquationNaryOperator` and `EquationLimitPlacement`; existing expression factories
and enum values remain unchanged. See the [n-ary profile](p2-objects-typography.md#unreleased-source-n-ary-equations).

The pending [#159 table-field profile](table-formulas.md) changes bounded field
evaluation without adding a public API. Its deterministic culture, cached-result
fallback and per-table limits are part of the documented behavior; it is not yet
available in the published 1.5.0 packages.

Pending #150 adds default-enabled `ComparisonOptions.CompareStoryText` and
`CompareRunFormatting` switches. See the [selection profile](review-workflows.md#unreleased-source-comparison-selection)
for original-value retention. The same pending source adds
`CompareParagraphFormatting` and bounded [paragraph-format revisions](review-workflows.md#unreleased-source-paragraph-format-revisions).

## 1.5.0 roadmap #102 delivery

The [release notes](releases/1.5.0.md), [capability matrix](roadmap-102-capability-matrix.json)
and [qualification record](roadmap-102-p6-qualification.md) describe the P1–P6 batch.
The bounded fields, controls, reports, review and drawing additions ship together;
all five target frameworks and existing platform limits remain. Historical results
below retain their original source/package identities.

The [template compatibility-plan design](template-compatibility-plan-design.md)
proposes later APIs but adds no public signatures or package/runtime support.


## P2 controls API — merged source

The [P2 source API](content-controls.md) adds `LoadWithControls`, detached
session/version control snapshots, explicit typed setters, rich replacements,
placeholder/display views and existing-local-binding apply/write-back/detach.
Snapshots are cached per committed version; failures/save keep them live and
successful edits invalidate them. Display views retain Unicode text and defer
managed glyph/underline preparation until PDF/XPS save.
Existing Load/ReviewLoadOptions metadata and all five targets remain. No provider
or XML/SDK type enters the public API. The profile is included in 1.5.0; source
review, package validation and actual runtime qualification remain distinct evidence.

## Merged roadmap #102 P1 API additions

The [P1 profile](roadmap-102-p1.md) adds `DocumentPropertyType`, immutable `DocumentProperty` constructors for string/bool/decimal/DateTimeOffset, and `WordDocument.CustomProperties`, `SetCustomProperty` and `RemoveCustomProperty`. These provider-independent APIs are shared across all five targets and included in 1.5.0. Existing signatures remain compatible; package availability and runtime qualification are separate facts.


## 1.4.0 compatibility

All three package versions advance together to 1.4.0. Chart-data binding, scalar fields,
structural review/comparison and direct HTML/ODT PDF export are additive. Package
validation compares every target against published 1.3.0. AssemblyVersion remains
1.0.0.0 and all five core/OCR assets remain. Font/table/note corrections can change
pagination; cached unsupported scalar fields keep informational diagnostics in Strict,
and whole-paragraph review decisions cascade across the mark/text unit. See the
[release notes](releases/1.4.0.md) for migration and combination limits.

Earlier version-specific sections below are historical policy and qualification.

The public namespace is `WordNet`; package and assembly names are `wordnet`.
Every library build retains matching public APIs for
`net462;net472;netstandard2.0;net8.0;net10.0`. Platform/provider objects never appear in public
signatures. Framework targets and runtime qualification are separate: a build for
4.7.2 executed on 4.8 does not prove execution on 4.7.2 itself.

Starting with 1.0.0, every public API change must
identify source/binary compatibility, update XML documentation and examples, and pass
package validation across all five targets. Add optional behavior through a new overload
while preserving an existing constructor's metadata signature. The OCR correction
overload follows this rule; existing calls and binaries retain correction-disabled behavior.
Column ordering requires the separate `ReadTextColumns` opt-in and preserves the default
native line/paragraph order. Its additional overload preserves earlier constructor signatures.
Packing validates all target APIs and binary compatibility against the
published `1.0.0` package. CI then runs the full suite against the packed artifacts
on all four desktop platforms, including explicitly loading its .NET Standard 2.0
assembly on both modern runtimes. A runtime-identity assertion prevents a fallback test
from accidentally exercising a framework-specific asset instead.

The enterprise preview preserves constructor metadata and existing enum values;
Html and Lenient are appended enum members. New HTML/font/clock settings are settable
properties configured before concurrent calls. Results add diagnostic severity/count
and metrics. AssemblyVersion remains 1.0.0.0; packages/assemblies remain unsigned.
PDF input now requires an explicit `wordnet.Ocr` package reference at the same version.
This is a deployment migration despite preserving public method signatures. Add the
package when upgrading applications that import PDF or use OCR.

After 1.0, incompatible public signatures or removed target frameworks require a major
version and migration notes. Feature additions use minor versions; compatible fixes use
patch versions. Correctness/security fixes may change results, diagnostics or rejection
of malformed input. Do not depend on exact diagnostic prose, output ZIP bytes, PDF object
ordering, timestamps or exception inner types. Diagnostic codes and documented stream,
cancellation and fidelity contracts are the supported integration points.

The 1.0 release scope is Windows/Linux x64 and ARM64. Android and macOS are deferred.
Native dependency architecture, fonts, English language data, minimum OS/API
versions and deployment mode must be tested independently. Trimming/AOT are not qualified.

`eng/run-framework-tests.ps1` runs prebuilt x64 tests without an SDK on the target PC and
records the actual Framework release key. `-RequireFramework472` accepts only Microsoft's
4.7.2 release values, 461808 or 461814. Copy the complete test output directory and the
runner, `prepare-ocr-tests.ps1` and `required-tests.txt` to an isolated test machine;
use `-TestDirectory` and `-ResultsDirectory` explicitly. Do not downgrade a shared PC.
The mapping follows [Microsoft's Framework version table](https://learn.microsoft.com/dotnet/framework/install/versions-and-dependencies).

Alternatively, create a portable ZIP from the complete built test output with
`eng/create-framework-bundle.ps1 -OutputPath artifacts/windows-x64-tests.zip`.
Extract it on the target PC and run `./run.ps1`. For actual 4.7.2 x64, run
`./run.ps1 -RequireFramework472 -JitMode legacy`; at that checkpoint this configuration
passed the 348-case source and locally packed 1.0.0 suites on actual release
461814. Package/library hashes are recorded with the release evidence. Default-JIT 4.7.2 operation is unsupported for
1.0. Consumers must select the corresponding documented application setting;
the library does not change host JIT settings. See the
[minimum-runtime runbook](minimum-runtime-qualification.md).
The bundle includes fixtures and notices and provisions pinned OCR prerequisites into
its own directory. Return `results/net472.trx` and `results/environment.json`; no SDK,
remote service, publishing credential or repository checkout is required there.

## Bucket 4 additive API

Unreleased #145 source adds the provider-neutral `WordDocument.InsertBlockContentControl`
for an empty ordinary body paragraph and keeps the existing inline creation API.
See the [bounded block profile](content-controls.md#unreleased-source-creating-a-block-control);
it is not in published 1.5.0 packages.

Unreleased #167 source adds `PreviewOptions.Layout` and immutable preview-layout
snapshot types. They expose no provider or model types; text is opt-in and
unqualified geometry is nullable. See the [snapshot profile](interoperability.md#unreleased-source-preview-layout-snapshots).
The published 1.5.0 API does not include them.

Published 1.2.0 includes the merged source from PR #37, which appends `Rtf`, adds provider-neutral typography/import/metadata
options, immutable OCR/preview results, PDF accessibility/language/page selection and
`WordConverter.RenderPages`. The existing four-argument `TemplateImage` constructor
is retained; explicit decoration uses a new five-argument overload. Default managed
Latin/PDF and OCR text behavior remain. See [usage and deployment](interoperability.md).

## Historical bucket 5 API proposals

The [design index](later-design-decisions.md) records API sketches from before their
implementation. Published 1.5.0 includes bounded `DocumentFormat.Odt`,
review/comparison and chart/equation APIs described in the [capability matrix](roadmap-102-capability-matrix.json).
`DocxProtection` and signature utilities remain cancelled. The historical design did
not itself change overload metadata or qualify a package or runtime.

## 1.2.0 compatibility baseline

Release packing compares against published 1.1.0, including its additive public
surface. All five target assets, existing enum values and constructor metadata remain;
AssemblyVersion stays 1.0.0.0. New APIs are additive. This baseline check does not
qualify older runtime execution; [actual-4.7.2 evidence](release-1.2.0-readiness.md)
and exact-package publication gates remain separate.

## 1.2.1 presentation patch

All three packages advance together to 1.2.1 to deliver a corrected NuGet README.
No public signatures or provider contracts change. API packing compares against
published 1.2.0; AssemblyVersion remains 1.0.0.0 and all five core/OCR assets remain.
The PDF producer identifies the new package version. Its exact release artifact
still needs native desktop and actual-minimum-runtime qualification before publishing.
