# Issue 103 independent fidelity contrasts

Twelve synthetic MIT-authored DOCX packages generated directly from published OOXML
by `eng/generate-fidelity-reductions.py`. No document-library serializer, customer
documents, proprietary resources or office-produced inputs are used. The wrap case
derives from the existing independently authored `Comparison/floating-logo-left.docx`
and changes only explicit size/spacing/paragraph-mark properties. Font identities
come unchanged from the existing comparison manifest.

Generate scratch copies with
`python eng/generate-fidelity-reductions.py --output artifacts/fidelity-103/reductions`
from the repository root. The default is that same ignored artifacts directory;
fixture-directory output is refused. The committed manifest records
generator/input/font hashes, required text/order, authored page expectations and
the hypothesis for each contrast. It is separate from the unchanged original
24-document corpus. The long-note three-page expectation describes the existing
bounded reservation policy, not an independent reference result.

These historical deflated archives retain their original hashes and generator
identity in the measurement receipt. Current scratch generation uses `ZIP_STORED`
with fixed timestamp, producer OS and attributes, so new archive bytes are portable
across OS/zlib versions. Guards compare every uncompressed XML/image byte with the
measured packages and check both manifests' own archive hashes. Only the new
generator identity, archive encoding/hashes and corrected table-hypothesis wording
differ. The committed manifest must still exactly match its historical receipt.
Do not overwrite these measured archives or substitute scratch hashes into that receipt.

The contrasts cover omitted/direct/document-default/based-on styling, explicit and
inherited paragraph spacing, explicit-format image wrapping, implicit/autofit/fixed
tables and marked/continued notes. They are probes, not twelve new supported-feature
claims. Three expected-warning checks and all twelve aggregate independent-reference
checks fail in the recorded baseline; those failures are deliberately retained.

Limitations for follow-up probes: `first` forms an `fi` reference ligature, so the
default/spacing cases cannot supply comparable glyph displacement; #104 needs a
new ligature-free cohort. Agreeing explicit table/grid/cell widths and short rows
make the three table variants convergence controls, not autofit/row-splitting probes.
Marked notes omit a conventional preserved-space run and separator notes, so their
label-spacing observation cannot establish a converter defect in #107. Those source
bytes remain historical evidence; improved controls require separate measurements.

Open XML SDK 3.5.1 independently validates all twelve packages. Separate pinned
office PDFs, source/package output and retained failures are described in
[`docs/fidelity-103-baseline.md`](../../../../docs/fidelity-103-baseline.md).
