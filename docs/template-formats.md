# Unreleased source: template formats and format inspection

Roadmap [#166](https://github.com/kirakosyan/word.net/issues/166) adds explicit
macro-free DOTX and ODF 1.3 OTT identities in pending source. These changes are
not part of the published 1.5.0 packages. They reuse the existing bounded DOCX
and ODT content profiles; a template identity does not expand support for every
Word or ODF feature.

The following table lists the DOCX/DOTX/ODT/OTT directions in this source profile.
`WordDocument` can load and save each of these formats; the converter's same-format
directions remain unsupported. PDF and XPS are output-only in this table.

| Input | DOCX | DOTX | ODT | OTT | PDF | XPS |
| --- | --- | --- | --- | --- | --- | --- |
| DOCX | session save | yes | yes | yes | yes | yes |
| DOTX | yes | session save | yes | yes | yes | yes |
| ODT | yes | yes | session save | yes | yes | no |
| OTT | yes | yes | yes | session save | yes | no |

Other directions remain governed by `WordConverter.CanConvert`. An explicit source
format must match the package's actual main content type or ODF mimetype and
manifest; an extension does not change its identity. DOTX output uses the template
main content type. OTT output writes the template mimetype first and uses the same
type in the ODF manifest. Supported review history and typed controls can be loaded
explicitly from DOTX using the existing review overload or the format-aware
`LoadWithControls` overload. ODT/OTT editing uses the existing ODF mapping and its
diagnostics rather than retaining arbitrary source styles or unsupported features.
Direct DOCX/DOTX exchange retains admitted merge fields and tagged template controls.
Other control metadata requires the explicit typed-control loading profile; a
conversion that flattens an unadmitted control reports `Template.UnsupportedControl`
and strict mode rejects that loss.

`WordConverter.DetectFormat` inspects bounded bytes without a file-name hint. Its
immutable result reports the container family, format where recognized, nullable
template identity, confidence and a status for unknown, invalid, macro-bearing or
protected input. Confidence describes the observed signature or package identity;
it does not assert that every part is valid, safe, or supported for conversion.
Use `CanConvert` and `Inspect` separately for a requested pair and feature profile.
PDF identification is signature-only.

```csharp
using System.IO;
using WordNet;

using var source = File.OpenRead("incoming-template");
DocumentFormatInspection identity = WordConverter.DetectFormat(source);
if (identity.Status == FormatDetectionStatus.Recognized && identity.Format == DocumentFormat.Dotx)
{
    var document = WordDocument.Load(source, DocumentFormat.Dotx, out var result);
    // Review result.Diagnostics before editing or exporting.
}
```

Seekable streams are restored to their original position after inspection, including
on failure, and remain open. A nonseekable stream is consumed from its current
position through EOF, or at most one byte beyond the configured input limit when
the limit is exceeded. Archive-entry, expanded-byte, aggregate XML-character and
nesting limits apply; cancellation and resource failures throw. Inspection never
fetches links or executes macros, scripts, fields or other document content.

The bounded detector recognizes OOXML macro-enabled main types and VBA project
parts/relationships, ODF `Basic`/`Scripts` and script metadata, encrypted ODF
manifest entries, common encrypted compound-file wrappers and standard ZIP entry
encryption flags. ZIP64 identity is reported as unknown by the inspector and is
outside the DOCX/DOTX template-exchange profile. Other active content, unusual
encryption forms and full package validity are not ruled out by a recognized result.
Macro-enabled templates, encryption,
decryption and signatures remain unsupported. Unknown features and unsupported
ODF/DOCX mappings retain their existing failures or loss diagnostics.

The focused tests create independent Open XML and ODF packages, inspect output
identity with the Open XML SDK and raw ODF metadata, compare PDF text with PdfPig,
and exercise review/control/field retention, mismatch, malformed, active,
protected, resource, stream and cancellation cases. Exact runtime and package
qualification of this increment remains separate.
