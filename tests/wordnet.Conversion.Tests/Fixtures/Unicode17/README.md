# Unicode 17 boundary conformance

Unmodified official Unicode 17.0.0 GraphemeBreakTest and WordBreakTest data are used
under Unicode License V3; see LICENSE and original file notices. The manifest pins
input-property and conformance-file hashes. The tests verify the retained test-file
hashes and execute all 766 grapheme and 1,944 word cases.

`eng/generate-unicode-tables.py` documents regeneration from the pinned UCD inputs and
emits compact managed tables. No runtime Unicode classifications or StringInfo are
used. Default boundaries preserve UTF-16 source offsets without normalization, case
folding or language-specific dictionary tailoring. This adds no typography claim.
