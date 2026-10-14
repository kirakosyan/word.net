# Dependency maintenance

The 1.0.0 desktop release retains these runtime dependencies. On 2026-10-14, locked
restore and the solution's transitive vulnerability/deprecation reports complete with
no warnings or reported vulnerable/deprecated packages. Package/API validation passes
for all four targets. The detailed dependency selection below is the dated alpha.7 review.

Reviewed against nuget.org on **2026-10-13** for **0.1.0-alpha.7**. Use stable
releases that retain all four library targets: `net472`, `netstandard2.0`, `net8.0`
and `net10.0`. Versions are centralized in `Directory.Packages.props`; each project
has a reviewed lock file. Native assets must match their managed bindings.

## Changes in alpha.7

| Dependency | Previous | Current |
| --- | --- | --- |
| xUnit framework | Deprecated `xunit` 2.9.3 | `xunit.v3.mtp-off` 4.0.2 |
| xUnit Visual Studio adapter | 3.1.5 | 4.0.1 |
| Microsoft.NET.Test.Sdk | 17.14.1 | 18.10.1 |
| SkiaSharp / HarfBuzz integration / Skia native assets | 3.119.4 | 4.153.1 |
| HarfBuzzSharp / native assets | 8.3.1.5 | 14.2.1.301 |
| PDF JBIG2 decoder | 0.1.16.1 | 0.1.16.2 |
| PDF JPEG 2000 decoder | 0.1.16 | 0.1.16.1 |
| System.Text.Encoding.CodePages (legacy targets) | 8.0.0 | 10.0.12 |

Test-only compatibility dependencies are also brought up to current stable versions.
This removes the deprecated `System.Collections.Immutable` 1.5.0 dependency. Markdig,
AngleSharp, OpenMcdf, PdfPig, its renderer and Open XML SDK were already current stable
versions at the time of review. Framework reference assemblies 1.0.3 are also current;
a small or old-looking version number alone does not imply an obsolete package.

[xUnit's migration guide](https://xunit.net/docs/getting-started/v3/migration) retains
.NET Framework 4.7.2 and .NET 8 support. Its
[MTP-off package](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform)
keeps the existing VSTest/TRX workflow. Desktop tests are now executables; their
Framework runtime is explicitly `win-x64` so locked restore is consistent on Windows
and Linux. The Android app uses the same v3 attributes/assertions with its own entry
point and awaits v3 theory-data discovery. Tests pass the runner's cancellation token
where supported; explicit cancellation regressions keep their controlled tokens.

Central transitive pinning updates dependencies whose parents specify older minimums.
NuGet promotes pinned runtime dependencies into the package manifest so consumers
receive the versions that were tested. Test packages remain outside the library's
dependency groups. See [licenses and native notices](../THIRD_PARTY_NOTICES.md).

## Retained framework reference asset

The outdated report still lists `Microsoft.NETCore.Platforms` **1.1.0** under
`NETStandard.Library` **2.0.3** for the .NET Standard 2.0 build. This is part of the SDK's
implicit reference graph, not a direct converter dependency. Keep that graph intact;
the generated wordnet package does not declare either package as a runtime dependency.
Changing this asset solely to remove an age warning would not modernize the converter.

```sh
dotnet nuget why src/wordnet/wordnet.csproj Microsoft.NETCore.Platforms --framework netstandard2.0
```

## Repeat the audit

Run locked restore for the solution and the separate Android project, then run each
of these reports for both `wordnet.sln` and `tests/wordnet.Android.Compatibility`:

```sh
dotnet list wordnet.sln package --deprecated --include-transitive --no-restore
dotnet list wordnet.sln package --vulnerable --include-transitive --no-restore
dotnet list wordnet.sln package --outdated --include-transitive --no-restore
```

These commands report findings; a successful exit code alone is not a clean audit.
Review all target-framework sections. The current graph has no NuGet-reported
deprecated packages or known vulnerabilities. All direct references are current stable
versions. The framework reference asset above is the only remaining outdated item.

After upgrades, regenerate and review lock-file changes, repeat locked restore, build
all targets, run real conversion/OCR tests, verify formatting and pack/API compatibility.
Graphics upgrades require Windows/Linux/Android execution: SkiaSharp 4 includes
[behavior changes](https://mono.github.io/SkiaSharp/docs/releases/4.148.0.html), including
[Linux font fallback changes](https://mono.github.io/SkiaSharp/docs/releases/4.153.0.html).
The small fixture suite does not qualify every native image codec or document feature.
See [test evidence](testing.md) for the exact platforms and remaining limits.
