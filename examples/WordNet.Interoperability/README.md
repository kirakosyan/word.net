# Interoperability sample

Run from the repository root with .NET 10:

```sh
dotnet run --project examples/WordNet.Interoperability -- artifacts/interoperability-sample
```

The sample produces editable DOCX/RTF, a bounded PDF/UA-1 document and outlined SVG
from a self-contained HTML invoice. It builds against the checkout and uses
APIs introduced in 1.2.0 and included in the
[1.6.2 release](https://github.com/kirakosyan/word.net/releases/tag/v1.6.2).
See [supported profiles and limits](../../docs/interoperability.md) and
[build-specific testing](../../docs/testing.md). No package publication occurs.
