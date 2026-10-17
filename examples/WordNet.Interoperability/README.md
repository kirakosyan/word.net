# Interoperability sample

Run from the repository root with .NET 10:

```sh
dotnet run --project examples/WordNet.Interoperability -- artifacts/interoperability-sample
```

The sample produces editable DOCX/RTF, a bounded PDF/UA-1 document and outlined SVG
from a self-contained HTML invoice. Its project references use the
merged bucket 4 APIs from PR #37, included in the 1.2.0 candidate. They are absent
from published 1.1.0; check [publication status](../../docs/release-1.2.0-readiness.md).
See [supported profiles and limits](../../docs/interoperability.md) and
[build-specific testing](../../docs/testing.md). No package publication occurs.
