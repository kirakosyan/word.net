# Issue 103 independent fidelity contrasts

Twelve synthetic MIT-authored DOCX packages generated directly from published OOXML
by `eng/generate-fidelity-reductions.py`. No document-library serializer, customer
documents, proprietary resources or office-produced inputs are used. The wrap case
derives from the existing independently authored `Comparison/floating-logo-left.docx`
and changes only explicit size/spacing/paragraph-mark properties. Font identities
come unchanged from the existing comparison manifest.

Regenerate with `python eng/generate-fidelity-reductions.py`. The manifest records
generator/input/font hashes, required text/order, authored page expectations and
the hypothesis for each contrast. It is separate from the unchanged original
24-document corpus. The long-note three-page expectation describes the existing
bounded reservation policy, not an independent reference result.

ZIP producer metadata and deflate bytes can vary across operating systems and zlib
versions. The guard tests require repeated generation on one host to be byte-identical,
then compare every uncompressed XML/image byte with the measured packages. Both
generated and committed archive hashes are checked against their own manifests;
all other manifest fields must agree. The committed manifest must also match the
historical measurement receipt. Preserve these measured archives and hashes when
regenerating on another host; archive-byte differences alone are not content drift.

The contrasts cover omitted/direct/document-default/based-on styling, explicit and
inherited paragraph spacing, explicit-format image wrapping, implicit/autofit/fixed
tables and marked/continued notes. They are probes, not twelve new supported-feature
claims. Three expected-warning checks and all twelve aggregate independent-reference
checks fail in the recorded baseline; those failures are deliberately retained.

Open XML SDK 3.5.1 independently validates all twelve packages. Separate pinned
office PDFs, source/package output and retained failures are described in
[`docs/fidelity-103-baseline.md`](../../../../docs/fidelity-103-baseline.md).
