# API and support policy

The public namespace is `WordNet`; package and assembly names are `wordnet`.
Every library build retains matching public APIs for
`net462;net472;netstandard2.0;net8.0;net10.0`. Platform/provider objects never appear in public
signatures. Framework targets and runtime qualification are separate: a build for
4.7.2 executed on 4.8 does not prove execution on 4.7.2 itself.

Starting with 1.0.0, every public API change must
identify source/binary compatibility, update XML documentation and examples, and pass
package validation across all five targets. Add optional behavior through a new overload
while preserving an existing constructor's metadata signature. The OCR correction
overload follows this rule; existing calls and binaries retain correction-disabled behavior.
Column ordering requires the separate `ReadTextColumns` opt-in and preserves the default
native line/paragraph order. Its additional overload preserves earlier constructor signatures.
Packing validates all target APIs and binary compatibility against the
published `1.0.0` package. CI then runs the full suite against the packed artifacts
on all four desktop platforms, including explicitly loading its .NET Standard 2.0
assembly on both modern runtimes. A runtime-identity assertion prevents a fallback test
from accidentally exercising a framework-specific asset instead.

The enterprise preview preserves constructor metadata and existing enum values;
Html and Lenient are appended enum members. New HTML/font/clock settings are settable
properties configured before concurrent calls. Results add diagnostic severity/count
and metrics. AssemblyVersion remains 1.0.0.0; packages/assemblies remain unsigned.
PDF input now requires an explicit `wordnet.Ocr` package reference at the same version.
This is a deployment migration despite preserving public method signatures. Add the
package when upgrading applications that import PDF or use OCR.

After 1.0, incompatible public signatures or removed target frameworks require a major
version and migration notes. Feature additions use minor versions; compatible fixes use
patch versions. Correctness/security fixes may change results, diagnostics or rejection
of malformed input. Do not depend on exact diagnostic prose, output ZIP bytes, PDF object
ordering, timestamps or exception inner types. Diagnostic codes and documented stream,
cancellation and fidelity contracts are the supported integration points.

The 1.0 release scope is Windows/Linux x64 and ARM64. Android and macOS are deferred.
Native dependency architecture, fonts, English language data, minimum OS/API
versions and deployment mode must be tested independently. Trimming/AOT are not qualified.

`eng/run-framework-tests.ps1` runs prebuilt x64 tests without an SDK on the target PC and
records the actual Framework release key. `-RequireFramework472` accepts only Microsoft's
4.7.2 release values, 461808 or 461814. Copy the complete test output directory and the
runner, `prepare-ocr-tests.ps1` and `required-tests.txt` to an isolated test machine;
use `-TestDirectory` and `-ResultsDirectory` explicitly. Do not downgrade a shared PC.
The mapping follows [Microsoft's Framework version table](https://learn.microsoft.com/dotnet/framework/install/versions-and-dependencies).

Alternatively, create a portable ZIP from the complete built test output with
`eng/create-framework-bundle.ps1 -OutputPath artifacts/windows-x64-tests.zip`.
Extract it on the target PC and run `./run.ps1`. For actual 4.7.2 x64, run
`./run.ps1 -RequireFramework472 -JitMode legacy`; this compatibility configuration
passes the current 348-case source and locally packed 1.0.0 suites on actual release
461814. Package/library hashes are recorded with the release evidence. Default-JIT 4.7.2 operation is unsupported for
1.0. Consumers must select the corresponding documented application setting;
the library does not change host JIT settings. See the
[minimum-runtime runbook](minimum-runtime-qualification.md).
The bundle includes fixtures and notices and provisions pinned OCR prerequisites into
its own directory. Return `results/net472.trx` and `results/environment.json`; no SDK,
remote service, publishing credential or repository checkout is required there.
