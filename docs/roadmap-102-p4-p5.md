# Roadmap #102 P4/P5 source profile under review

## Bounded run-format revisions (#122)

An ordinary body or cell text run may own one formatting revision. Its current
appearance is the accepted state; the prior snapshot is the rejected state. The
admitted changed properties are bold, italic, point size and RGB color. Other
properties must be equivalent in both snapshots. Comparison normalizes equivalent
run boundaries, aligns equal text by character spans, and emits an independent
`w:rPrChange` record with caller attribution for each changed span. Inserted and
deleted text retains the existing text-revision behavior. A formatting revision
cannot overlap a text revision or a structural owner. Multiple records, work and
text are subject to the existing aggregate review and comparison limits.

Opt-in preservation rejects property history in stories, fields, hyperlinks,
controls, objects or paragraph/table/section properties. It rejects unsupported
property changes and ambiguous nested histories. Decisions and detached views
retain the chosen appearance, including explicit off values against inherited
styles; intersecting comments require an explicit comment decision first. Saved
DOCX contains real `w:rPrChange` markup. This source is pending review and has not
been published as a package. Word-specific complex-script variants and proofing
properties in formatting history remain unsupported, including variants that
mirror ordinary run properties; they are not silently dropped.

## Whole-paragraph moves (#123)

Only exact whole ordinary paragraphs that occur once on each side may be paired;
duplicate, edited, overlapping and cross-section candidates are rejected. Each
side has a paragraph-mark revision and tracked run content inside an independently
identified move range. The paired ranges share a name; each range start/end uses
its own identifier. A decision on either side resolves both sides and their tracked
text together. Identifiers and pair references must be remapped during composition,
and ambiguous comments or other histories must fail before mutation. Candidate
search and pair validation consume the same aggregate comparison/review budgets.
The bounded import requires matching authors and explicit timestamps on the range,
mark and tracked text. The published ISO/IEC example for the paragraph-mark
`moveFrom` element describes the separate range, mark and run requirements:
[MoveFrom](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.movefrom).

## Sections and stories (#124–#125)

Structural comparison aligns admitted body paragraphs and simple table rows
independently within matching sections. Section geometry, break types and shared
story identity must match, with a maximum of 64 sections and one aggregate work
budget. Cross-section moves, section insertion/deletion and changed geometry are
rejected. Plain insertion/deletion history can be preserved and decided in ordinary
header/footer paragraphs. Comparison visits each shared or inherited story once;
story topology must match. Story formatting history, fields, controls, objects,
comments and structured story changes remain unsupported. Decision views leave the
source session untouched and rendering requires explicit history decisions.

## Positioned text boxes (#126–#128)

Quarter-turn rotation is clockwise around the unrotated frame center. The transformed
rectangle drives wrapping and page-bound checks; selectable text in ordinary managed
PDF and XPS and glyph outlines in SVG use the same transform. Rotated boxes admit
ordinary paragraphs; fields, links, images, nested content, native typography and
tagged/archive PDF combinations reject. DOCX keeps the editable frame and transform.

`a:normAutofit` explicitly opts into bounded shrink-to-fit. Only the default source
intent is accepted; nondefault editor scale/spacing values are rejected. A single
fit calculation scales font and line spacing without changing the frame; every
resulting font must be at least six points or output fails. The default non-autofit
overflow policy is unchanged. Fixed output is determined locally and may differ from
an editor's later interactive reflow.

Linked boxes use the DOCX story ID and ordered contiguous sequence, with one head
owning all ordinary text and up to 63 continuation boxes in the same section.
When multiple heads have the same default zero ID, ownership is ambiguous and the
chain is rejected; an explicit distinct story ID is needed to disambiguate it.
Layout wraps at the smallest usable chain width, allocates complete lines in order,
and fails if the final box cannot hold the remainder. This conservative width policy
may wrap earlier than an interactive editor. Empty intermediate frames are allowed.
Rotated or autofit chains, nested content, histories and composition through chains
are rejected. Managed ordinary fixed output is qualified separately from tagged,
archive and native typography profiles. Source drawing-layer values are retained;
overlapping drawings with explicit layers, disabled overlap and placement outside
cells remain unsupported for managed rendering of rotated, fitted and linked boxes.

## Vector shapes (#129)

The admitted profile contains anchored line, ellipse and default-adjustment rounded
rectangle geometry with opaque solid RGB fill and/or solid stroke, bounded physical
coordinates and explicit fill/no-fill. DOCX retains editable DrawingML and optional
alternative text. Managed PDF, XPS and SVG use vector paths; ellipses and corners use
the standard four-cubic approximation. Behind/foreground paint and rectangular wrap
exclusion follow the retained anchor. Groups, transforms, shape effects, gradients,
adjustments, overlapping drawings with explicit layers, disabled overlap and unqualified cell placement,
and unrecognized geometries reject or report loss. Tagged/archive PDF
and native typography combinations remain unsupported, even with alternative text.

All of these source changes remain under review. They are not part
of the published 1.4.0 packages.
