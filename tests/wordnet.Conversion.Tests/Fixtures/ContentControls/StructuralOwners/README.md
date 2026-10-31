# Single-row and single-cell control references

The independent [generator](../../../../../eng/generate-roadmap-138-structural-controls.py)
authors two minimal DOCX packages with a rich-text wrapper around one row or one
cell. It uses no wordnet serializer, customer document or proprietary resource.
The committed inputs are deterministic and Open XML SDK 3.5.1 validates both.

LibreOffice 24.2.7.2 (`420(Build:2)`) on Ubuntu 24.04 exported both to PDF with
the pinned Noto Sans Regular font. Poppler extracted `A B` above `C D` in both.
The [observation](reference.json) records input/PDF hashes, word positions, tool
versions, font identity and limitations. Reference PDFs are not committed.
This establishes only the selected static placement; Word execution, nested
typed wrappers, repeated report rows and structural mutation remain unqualified.

Rebuild the inputs in a fresh scratch directory:

```sh
python eng/generate-roadmap-138-structural-controls.py --output /tmp/wordnet-structural
```
