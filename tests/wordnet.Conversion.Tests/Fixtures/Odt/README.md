# Independent ODF fixtures

The five standalone packages are specification-authored synthetic MIT fixtures from
`eng/generate-odt-fixtures.py`. `manifest.json` pins their exact bytes. They do not use
the production reader/writer. Text/styles/metadata, list starts/nine levels, page
masters/stories, merged tables and an existing project-authored raster have explicit
semantic and independent-reader checks in OdtTests and `eng/verify-odt.py`.

`office-text.odt` is independently serialized by LibreOfficeDev 26.8 from the same
synthetic text semantics. Its profile sets DefaultVersion=10 (ODF 1.3 standard).
`office-manifest.json` records its exact hash, writer build and invocation. Its office
metadata/ZIP bytes need not regenerate deterministically; the retained exact fixture
is the test input. No customer content, embedded fonts or reader binaries are included.

Validation schemas are development-only downloads with pinned hashes and original
OASIS notices. They are not redistributed in runtime packages.
