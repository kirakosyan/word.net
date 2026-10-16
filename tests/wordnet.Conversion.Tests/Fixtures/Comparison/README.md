# Authored comparison corpus

Twelve distinct synthetic business documents, independently authored under the
repository MIT license by `eng/generate-comparison-corpus.py`. No customer source,
Office/Aspose code or proprietary resources were used. ZIP timestamps are fixed.
The four categories each contain three independent examples, including explicitly
unsupported business features. The manifest records hashes, required text/order,
authored page expectations, font hashes and expected diagnostics.

Fonts: repository Noto Sans 2.008, SIL OFL 1.1, with existing font license/provenance.
All inputs are independently schema checked with Open XML SDK in the normal suite.
Commercial reference slots start empty. `baseline.json` records the local free run;
its missing Word/Aspose references and detected losses are intentional visible results.
See [reproduction and interpretation](../../../../docs/comparison.md).
