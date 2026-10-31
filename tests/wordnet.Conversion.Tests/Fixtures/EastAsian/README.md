# East Asian test fonts

The three small TrueType files are renamed, subset test derivatives of the
Japanese, simplified Chinese and traditional Chinese faces in Noto Sans CJK
Regular 2.004. The source is the Debian `fonts-noto-cjk` package
`1:20240730+repack1-1`, from <https://github.com/notofonts/noto-cjk>; the original
collection SHA-256 is `b76b0433203017ca80401b2ee0dd69350349871c4b19d504c34dbdd80541690a`.
The source metadata identifies copyright 2014–2021 Adobe. These modified fonts
are provided under the SIL Open Font License 1.1 in [OFL.txt](OFL.txt). They are
test fixtures and are not included in the production packages.

`eng/generate-roadmap-138-cjk-fonts.py` uses fontTools 4.61.1 and verifies the
source digest before selecting explicit fixture characters, renaming the faces and
converting cubic outlines to quadratic outlines with a one-font-unit error bound.
Two independent generations produced the same bytes:

- `RoadmapCJK-JP.ttf`: `01753b2ef4f1417ca0159e5f7410ef738b9525f121efa34eae55e74a6d4c0ed5`
- `RoadmapCJK-SC.ttf`: `0cf8b6aabefc5969c3e4f84190c6e43fb83ab7acff81c64f1ed0b1c2a6d80763`
- `RoadmapCJK-TC.ttf`: `51864cdc52fcac1a6dab7cc6a407183127d5ecda46bcbe77604aa7707481e60c`
