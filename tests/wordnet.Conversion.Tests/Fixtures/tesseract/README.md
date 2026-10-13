# Independent OCR scan

`phototest.tif` and `phototest.gold.txt` are unmodified Tesseract test assets from
[tesseract-ocr/test](https://github.com/tesseract-ocr/test/tree/232ff181c66516116ec0e84c4963f70de15050fd/testing),
pinned at `232ff181c66516116ec0e84c4963f70de15050fd`. The upstream Apache-2.0 license
and testing README are preserved here. These are test fixtures, never NuGet resources.

| File | SHA-256 |
| --- | --- |
| phototest.tif | `d2241a1eb6d6cd2eb6544b8e228c2862c852b7467d16ed636caa32179b780692` |
| phototest.gold.txt | `76ef157baeb6ab275b7d90f310477a8060d5822aea2b2e05d480f459b016a6ed` |

The parent `generate_ocr_corpus.py` converts the bilevel TIFF to RGB and wraps it in
an image-only PDF at 300 DPI. This derivative changes the container, not the text or
page geometry. `ocr-phototest.txt` copies the upstream ground truth with a normalized
final newline. Both derivatives retain the upstream license. Their hashes are recorded
in `../ocr-corpus.json`. No ground truth is inferred from wordnet's output.

This independent scan supplements synthetic degradations. It does not establish
accuracy for arbitrary business documents, forms or reconstructed tables.
