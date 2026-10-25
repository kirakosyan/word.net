# Creating, editing and filling documents

## Released 1.3.0 profile

The 1.3.0 API adds immutable `ChartDefinition`/`ChartSeries`, closed `EquationExpression`, versioned `Objects` snapshots and explicit create/update/remove methods. Ordinary edits retain objects only where supported; template execution and protected owner rewrites reject. See the [object API and limits](p2-objects-typography.md).

## Merged P1 report and review additions

[ReportData](report-preprocessing.md) produces deeply read-only ordinary merge values
from grouping/sorting/aggregates, checked decimal expressions and typed predicates.
Existing Merge/MergeJson syntax and missing-value behavior remain.

A body `repeat:Path` control may contain contiguous paragraphs/list items in one
section. Expansion preserves order/appearance and remaps closed bookmarks, numbering
and notes. Item scope precedes root scope; an empty collection removes the region.
Generated body blocks and table rows share MaximumRows; character/byte/depth limits
still apply before commit. Cross-region bookmarks, positioned objects, section
changes and overlapping nested block controls reject execution. Malformed repeat
regions cannot be serialized into executable partial controls. Single-row repeats
retain their existing behavior.

[Review APIs](review-workflows.md) are separate from ordinary Replace/Merge operations.
Only explicitly recorded replacements create history. Retained history protects
ordinary edits/templates until resolved; append remaps supported history.

`WordDocument` provides the bounded editing/template profile for roadmap #32,
bucket 3 (#11–#15). It keeps the existing converter API and five framework assets.
It adds no runtime dependency and uses the same managed offline DOCX/PDF writers.
See [the API decisions](editing-template-design.md) for ownership and architecture.
This API merged in PR #35 (`0b2af843`) and is included in published 1.2.0.
See the [release evidence](release-1.2.0-readiness.md). The [sample instructions](../examples/WordNet.Automation/README.md)
reproduce complete invoice and multi-section report recipes. The sample computes totals in application code, uses existing repeat/if controls and stages both output formats before replacing files. It prints load/edit/save diagnostics; ordinary invoice PDF reports its image accessibility limitation. No new engine arithmetic or template syntax is introduced.

## Create, edit and save

```csharp
using System.IO;
using WordNet;

var policy = new ConversionOptions(FidelityMode.Lenient);
var document = WordDocument.Create(policy);
document.Title = "Invoice";
document.AddParagraph("Invoice", new TextFormatting { Bold = true }, headingLevel: 1);
document.AddTable(new[] { new[] { "Description", "Amount" }, new[] { "Consulting", "120.50" } }, firstRowIsHeader: true);
using var word = File.Create("invoice.docx");
ConversionResult result = document.Save(word, DocumentFormat.Docx);
```

`Load(stream, format, out result, options, token)` accepts existing readable formats.
`Save` accepts DOCX, DOC, PDF, HTML, XPS and the existing text/ebook outputs, including
DOCX-to-DOCX editing. Every save returns the conversion diagnostics. A load is an
interpretation of the [semantic profile](document-fidelity.md); unsupported parts
are diagnosed and are not retained as arbitrary OOXML. Original diagnostic counts
and locations survive edits/saves. A subsequent Strict save rejects earlier losses.
Unexecuted templates saved outside DOCX use diagnosed cached content.

`Paragraphs` returns immutable body snapshots, including nested table cells and
text boxes. `ReplaceParagraph` replaces one unstructured paragraph's runs and
retains its paragraph settings. `DocumentText` describes copied text appearance or
an inline `TemplateImage`. Image bytes are copied at construction, validated on
admission, and retain their declared dimensions in points. Existing links, lists,
stories, drawings, notes and fields stay in the internal model within its profile.
`TemplateImage.FromStream` reads from the caller's current position, leaves the
stream open and enforces a configurable byte bound and cancellation.

Operations on one session serialize, including input enumeration/property snapshotting.
Reentrant mutations from caller callbacks are rejected. All edits use private copies
and commit only after validation/admission; cancellation, invalid syntax and limit
failures leave the session unchanged. Save works on a copy and stages output before
copying to the caller's current stream position without truncation. Streams stay
open. A failure during final I/O can leave partial bytes, as with `WordConverter`.
Configure shared options/data before use. Separate sessions may run concurrently.

For reports built from many text paragraphs, use one atomic batch:

```csharp
document.AddParagraphs(reportLines, new TextFormatting { FontSize = 11 });
```

`AddParagraphs` enumerates once and validates/commits the batch once. Repeated
individual mutations each copy and validate the session; use batches for large
reports. Invalid or cancelled batches leave all existing content unchanged.

## Replace text and edit bookmarks

```csharp
DocumentEditResult changed = document.Replace("{{Customer}}", "Example Company");
document.ReplaceRegex(@"Invoice\s+[0-9]+", "Invoice 42", System.TimeSpan.FromMilliseconds(100));
document.AddBookmark("Recipient", paragraphIndex: 0, start: 0, length: 7);
document.ReplaceBookmark("Recipient", new[] { new DocumentText("Customer") });
document.InsertAtBookmark("Recipient", new[] { new DocumentText(" name") }, atEnd: true);
```

Matching covers body, nested cells, notes, boxes and header/footer stories, within
one paragraph at a time. Fields, images, bookmarks, notes, boxes and tagged controls
are barriers. Matching uses invariant culture; case-sensitive matching is the
default. Literal matches are leftmost/non-overlapping. Regex patterns have at most
4096 characters and a positive timeout of at most one second. Literal replacement
has no fixed time budget; cancellation and admission limits still apply. Run slicing
advances through source runs once. The regex matching phase
also checks that time budget between paragraphs/matches; one in-progress regex
match can consume another timeout interval. Replacement text is literal (no `$1`
expansion), inherits the first matched run's appearance, and retains untouched
prefix/suffix styles. Empty searches, zero-length matches, invalid patterns,
surrogate-splitting matches and excessive expansion fail atomically.

`Bookmarks` exposes named text ranges. Creating a bookmark uses UTF-16 offsets in
one plain-text body paragraph. Replacing/inserting supports paragraph-local ranges
and styled text/images, preserving paired markers. Cross-paragraph/story ranges
and overwriting nested bookmarks, fields, notes or boxes are explicitly rejected.
Paragraph replacement and bookmark insertion/replacement also reject template
controls that would otherwise be deleted or split.
This API is not an automatic redaction guarantee or tracked-edit engine.

## Compose documents

`destination.Append(source, CompositionStyle.KeepSource)` copies supported content,
sections and stories, starting the appended content on a new page. Resolved source
formatting survives colliding style IDs. Lists, bookmark names, internal links,
REF/PAGEREF targets, SEQ names and note IDs are isolated from the destination.
Unresolved source targets remain unresolved, even if the destination has that name.
Stories explicitly stop inheritance where the source has no corresponding story.
Source sessions remain unchanged, including self-appends.

`UseDestination` uses the destination's first body paragraph of the same heading
level as an appearance/layout prototype for appended body paragraphs. It retains
source section geometry, stories, list identities and explicit content. This is a
documented role-based style rule rather than an arbitrary Word style-catalog merge.
DOCX retains supported field tokens and refreshes plain REF/SEQ results; PDF/XPS
refresh their supported page-dependent references after composition. Unsupported
source features retain diagnostics. Aggregate destination resource limits apply.

## Populate templates

```csharp
using System.Globalization;

document.Merge(new
{
    Customer = new { Name = "Example Company" },
    Items = new[] { new { Description = "Consulting", Amount = 120.50m } },
    HasTax = true,
    Logo = new TemplateImage(File.ReadAllBytes("logo.png"), 80, 40, "Company logo")
}, new TemplateOptions { Culture = CultureInfo.InvariantCulture });
```

The caller reads logo bytes explicitly; the engine does not load files or URLs.
`Merge` snapshots dictionaries or public instance properties. Indexers and methods
are excluded; caller-requested property getters execute during snapshot traversal.
Data cycles/excessive depth, nonfinite numbers, invalid keys and excessive arrays
are rejected. `MergeJson` accepts a bounded JSON object, rejects duplicate keys,
comments and malformed syntax, and uses the same evaluator. No scripting engine,
database lookup, implicit filesystem/network access or runtime expression execution
is involved. Supported scalars are strings, booleans, finite numbers, dates, GUIDs
and null (empty text). Other value types, including enums, characters and time spans,
are rejected during snapshotting. Paths use case-sensitive ASCII identifiers of at most 64
characters, separated by dots, with at most 256 characters in total. Repeat-item
scope is checked before root scope.

Simple and complex Word `MERGEFIELD` instructions, including split instruction
runs, support `MERGEFIELD Path`, `\@ "date format"`, `\# "number format"` and
`\* MERGEFORMAT`. Date/number switches require matching scalar types; format strings
have at most 128 characters and standard numeric precision at most 1000. Formatting
uses the explicitly selected culture (invariant by default). Repeated field targets
are allowed. The bounded Word-format mapping treats numeric `%` as a literal, maps
date `AM/PM` and `am/pm` to culture-specific upper/lower designators, and preserves
quoted literals and escapes. Standard .NET numeric formats such as `F2` are also
accepted. Per-mille scaling and unclosed quotes/escapes fail explicitly; this is
not the complete Word format language. Other field grammars and switch combinations
are explicit failures.
Ordinary `WordConverter` calls continue to use merge-field caches.

Content-control `w:tag/@w:val` values use this exact grammar:

| Tag | Placement and behavior |
| --- | --- |
| `merge:Path` | One block paragraph or one adjacent inline control group; retains first-run appearance |
| `image:Path` | Same scalar placements; replaces cached content with a caller `TemplateImage`, or empty content for null |
| `if:Path`, `if:!Path` | Block or inline content; retains/removes it according to the value or its negation |
| `repeat:Path` | A control wrapping exactly one table row; repeats for array items in source order |

False values are null, false, numeric zero, an empty string or an empty array.
Conditions can contain scalar fields/inline controls. Nesting controls that share
the same block, row or run group is outside the profile and diagnosed on load.
Merge rejects these flattened nested constructs before executing any condition.
Block controls inside cells are supported; cell-level table wrappers are diagnosed
and cannot execute. Multi-block/multi-row control boundaries survive DOCX save/load,
so unsupported scalar/repeat regions remain unsupported after serialization.
Control-specific validation, locking, data binding and appearance also report loss. No
arithmetic, comparisons, calls, array indexing or general-purpose expressions are
supported. Controls without a recognized template-operation prefix retain ordinary
cached-content conversion behavior; template validation does not apply to their
unrelated Word/add-in properties or tags.

Repeats execute before conditions/scalars/images in each generated row. Nested
repeats in nested tables default to a maximum depth of eight; generated rows default
to at most 10,000. Bounds also apply to the input snapshot, characters, structure,
images and aggregate expansion. Empty arrays/null remove the marked row; an empty
table is removed. Horizontal spans, decoration, heights and row controls survive.
List/SEQ numbering continues within a repeated region. Cloned bookmarks/notes have
unique identities; note labels are renumbered in reference order. Cloned repeat-row
notes bind against the item scope before root fallback. References to
bookmarks elsewhere in the same document keep their original targets, while local
cloned bookmark targets are remapped. Vertically merged
repeat rows and bookmark ranges crossing a repeat boundary fail explicitly.

Missing data defaults to an atomic error. `MissingValues = KeepCached` retains the
unresolved construct; `Empty` substitutes empty content (false for a condition,
empty for a collection/image). Both explicit fallbacks report `Template.MissingValue`,
which Strict rejects. Null is a supplied empty value, not missing. Diagnostics use
codes and source locations without including field names or data values. Invalid
images, syntax or expansion fail atomically. Supported executed markers are removed;
ordinary non-merge fields remain editable and follow the existing field profile.

## Run the standalone sample

```sh
dotnet restore examples/WordNet.Automation --locked-mode
dotnet run --project examples/WordNet.Automation -c Release -- /absolute/output/directory
```

The sample creates a cover letter, loads an independently authored invoice with
repeating items/optional terms/logo, edits its text and assembles an appendix. It
exports three editable DOCX/PDF pairs and prints diagnostics. Its logo reuses the
repository's authored raster fixture; no Office or native rendering provider is
required for execution. Tests independently validate DOCX schemas and PDF content,
page geometry, image bytes/dimensions and editable structure. Broader template
compatibility and arbitrary Word style/layout parity remain unqualified.
