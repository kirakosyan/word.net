# Bounded report preprocessing

## 1.4.0 roadmap #99 profile

Version 1.4.0 extends the 1.3.0 profiles with the bounded changes in
[roadmap-99-implementation.md](roadmap-99-implementation.md): direct HTML/ODT PDF export,
chart binding, scalar fields, structural comparison and qualified layout/native/object
combinations. The [1.4.0 release notes](releases/1.4.0.md) record compatibility and
qualification requirements. Earlier release receipts below remain historical evidence.

The additive `ReportData.Prepare(data, plan, options, cancellationToken)` API
snapshots named input once, then stages collection operations and named computed
values. It returns ordinary immutable merge data through `Values`; pass that to
existing `WordDocument.Merge`. Caller objects and document sessions are untouched.
This P1 implementation merged in PR #85 and is separate from published packages.

Plans use immutable DTOs, ASCII identifier paths and explicit expression nodes.
There is no expression language, callback, database access or formula execution.
Collection operations run in declaration order. Computed fields may reference
other named computed fields; dependency cycles, duplicate output names and missing
paths fail before a result is returned. Output names may not overwrite input.

Sorting is stable, uses ordinal strings, and places null first (last descending).
Keys must share a scalar type, with integral/decimal values treated as decimal;
floating-point numbers are rejected. Date ordering requires `DateTimeOffset` and
compares UTC instants. Group order follows the sorted rows, or first occurrence
without sorting. Groups expose `Key`, `Rows` and named aggregate values.
Count includes every row; sum/min/max exclude null. Empty/all-null sum is zero,
min/max are null. Mixed incompatible types and checked decimal overflow fail.

Arithmetic is checked decimal, with explicit round(scale 0–28, ToEven or
AwayFromZero). Null and nonnumeric operands fail. Equality is ordinal for strings,
null equals only null, and incompatible non-null types fail. Ordering null fails.
Boolean and/or evaluate left to right with short circuit; not requires Boolean.
Existing `if:Path` and `if:!Path` continue to consume the resulting booleans.

Rows, groups, key lengths, expression nodes/depth and evaluation steps have finite
limits and also consume the conversion character/expanded-byte/depth allowances.
Cancellation is checked during snapshotting, sorting, grouping and expression
evaluation. Failures contain codes and generic explanations, without input values.

`ReportCollectionOperation` declares the new output name/source path, optional
group/sort paths and `ReportAggregate` declarations. `ReportField` pairs a name
with immutable `ReportExpression.Literal`, `Path`, `Binary`, `Round` or `Not`
nodes. `ReportOptions` sets finite row/group/key/node/depth/work limits. Input
rows are counted across collections; collection evaluation also bounds generated
rows. No computed output may replace an existing root identifier.
Unsupported body repeat structures remain non-executable. DOCX save can retain
their cached content while omitting the unsafe repeat tag with a
`Template.ControlStructure` loss; it does not silently turn them into executable templates.

See the runnable [P1 recipes](../examples/WordNet.Automation/P1Recipes.cs) and
[template generator](../eng/generate-p1-templates.py): unsorted categories become
ordered groups with subtotals/counts, and ordinary merge fields consume computed
tax/total and a threshold predicate. Run the automation example as described in its
[README](../examples/WordNet.Automation/README.md).
