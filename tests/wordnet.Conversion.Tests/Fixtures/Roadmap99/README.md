# Roadmap #99 metric reduction

`metrics-explicit.docx` is an original MIT fixture reduced from `mixed-fonts-short`
in the authored comparison corpus. Each run explicitly requests 11 pt, each paragraph
has zero after-spacing, and the page has a 72 pt top margin. This separates baseline
metrics from the deliberately different fallback-font and default-spacing policies.

`metrics.json` records exact input, font and reference PDF hashes. The expected line
boxes were independently calculated with fontTools from the pinned horizontal headers;
the styles are regular Noto Sans, bold Liberation Serif and italic Liberation Mono.
The `.docx` and `.pdf` are test fixtures, not shipped library resources.

`metrics-office.pdf` was captured using the same office 24.2.7.2/private-fontconfig
Ubuntu 24.04 image as `docs/comparison-1.3.0.md`. The PDF baselines were independently
read with PyMuPDF. The bold baseline still differs by about 0.251 pt from that renderer;
the fixture does not erase or relax the comparison runner's 0.25 pt threshold.
`MixedMetricsTests` separately verifies the declared actual-font line-box policy in
PDF and XPS at its existing 0.03 pt coordinate allowance.
