using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using WordNet.Fonts;
using WordNet.Internal;

namespace WordNet;

/// <summary>An in-process, identity-bound snapshot of a bounded DOCX template inspection.</summary>
/// <remarks>This object can reuse its inspection only after checking the current input and schema. Its fixed profile uses
/// built-in fonts and no optional provider; it is not a portable serialized plan, conversion authorization or loss policy.
/// Findings and later-stage unknowns retain the same limits as <see cref="TemplateInspection"/>. Hashes can fingerprint
/// sensitive documents or schema names. The module/runtime fingerprint is not binary attestation and cannot detect code
/// patches that retain the same module identity. Caller streams remain open.</remarks>
public sealed class ReusableTemplateInspection
{
    private readonly TemplateInspection inspection;
    private readonly object process;
    private readonly DateTimeOffset fieldTime;
    private readonly FidelityMode fidelityMode;
    internal ReusableTemplateInspection(TemplateInspection inspection, DateTimeOffset fieldTime, FidelityMode fidelityMode,
        string options, string fonts, string providers, string engine)
    {
        this.inspection = inspection; process = TemplateInspectionIdentity.Process;
        this.fieldTime = fieldTime; this.fidelityMode = fidelityMode;
        OptionsSha256 = options; FontsSha256 = fonts; ProvidersSha256 = providers; EngineSha256 = engine;
    }

    /// <summary>Gets the SHA-256 of the bounded source bytes consumed during compilation.</summary>
    public string InputSha256 => inspection.InputSha256;
    /// <summary>Gets the immutable inspection as it was compiled. Use <see cref="Reuse"/> to check a current input before reusing it.</summary>
    public TemplateInspection Inspection => inspection;
    /// <summary>Gets the immutable data-declaration fingerprint.</summary>
    public string DataSchemaSha256 => inspection.DataSchemaSha256;
    /// <summary>Gets the fixed DOCX inspection profile and field-clock fingerprint.</summary>
    public string OptionsSha256 { get; }
    /// <summary>Gets the fingerprint of built-in font resources in the loaded assemblies.</summary>
    public string FontsSha256 { get; }
    /// <summary>Gets the fingerprint of the fixed no-optional-provider profile.</summary>
    public string ProvidersSha256 { get; }
    /// <summary>Gets a fingerprint of the loaded core module identity and runtime descriptor, not binary attestation.</summary>
    public string EngineSha256 { get; }

    /// <summary>Returns the original immutable inspection after verifying the current input, schema and in-process environment.</summary>
    /// <remarks>The source is read from its current position and left open. A mismatched schema or environment fails before
    /// consuming it. A mismatched input fails after reading it; cancellation and input limits remain explicit. Environment
    /// checks cannot detect runtime code patches that leave the module identity unchanged. This method reuses an inspection
    /// only: it does not bind data, validate output, permit losses or authorize a later conversion.</remarks>
    public TemplateInspection Reuse(Stream source, TemplateDataSchema schema, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(source, nameof(source)); Guard.NotNull(schema, nameof(schema));
        if (!source.CanRead) throw new ArgumentException("The source must be readable.", nameof(source));
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(inspection.DataSchemaSha256, schema.IdentitySha256, StringComparison.Ordinal))
            throw new InvalidDataException("Plan.StaleSchema: The template declaration has changed.");
        CheckEnvironment(cancellationToken);
        string input = TemplateInspectionIdentity.Input(source, cancellationToken);
        if (!string.Equals(inspection.InputSha256, input, StringComparison.Ordinal))
            throw new InvalidDataException("Plan.StaleInput: The template source has changed.");
        CheckEnvironment(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return inspection;
    }

    private void CheckEnvironment(CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(process, TemplateInspectionIdentity.Process)
            || !string.Equals(OptionsSha256, TemplateInspectionIdentity.Options(new ConversionOptions(fidelityMode, maximumInputBytes: TemplateInspectionIdentity.MaximumInputBytes), fieldTime), StringComparison.Ordinal)
            || !string.Equals(EngineSha256, TemplateInspectionIdentity.Engine(), StringComparison.Ordinal)
            || !string.Equals(ProvidersSha256, TemplateInspectionIdentity.Providers(), StringComparison.Ordinal)
            || !string.Equals(FontsSha256, TemplateInspectionIdentity.Fonts(cancellationToken), StringComparison.Ordinal))
            throw new InvalidDataException("Plan.EnvironmentChanged: The inspection environment has changed.");
    }
}

public sealed partial class WordConverter
{
    /// <summary>Compiles a reusable in-process DOCX inspection using fixed defaults, built-in fonts and a fixed field clock.</summary>
    /// <remarks>The inspection and all its unknowns are unchanged. Only DOCX inspection with default resource bounds is
    /// admitted; no caller callback, external font or optional provider is captured. Reuse validates the input and declaration
    /// and returns the inspection only. It does not compile a portable policy plan or authorize output.</remarks>
    public ReusableTemplateInspection CompileReusableTemplateInspection(Stream source, TemplateDataSchema schema,
        DateTimeOffset fieldTime, FidelityMode fidelityMode = FidelityMode.Strict, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(source, nameof(source)); Guard.NotNull(schema, nameof(schema));
        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset fixedTime = fieldTime.ToUniversalTime();
        var options = new ConversionOptions(fidelityMode, maximumInputBytes: TemplateInspectionIdentity.MaximumInputBytes) { Clock = () => fixedTime };
        string optionsHash = TemplateInspectionIdentity.Options(options, fixedTime);
        string engine = TemplateInspectionIdentity.Engine(), providers = TemplateInspectionIdentity.Providers();
        string fonts = TemplateInspectionIdentity.Fonts(cancellationToken);
        TemplateInspection result = InspectTemplate(source, schema, DocumentFormat.Docx, options, cancellationToken);
        if (!string.Equals(engine, TemplateInspectionIdentity.Engine(), StringComparison.Ordinal)
            || !string.Equals(providers, TemplateInspectionIdentity.Providers(), StringComparison.Ordinal)
            || !string.Equals(fonts, TemplateInspectionIdentity.Fonts(cancellationToken), StringComparison.Ordinal))
            throw new InvalidDataException("Plan.EnvironmentChanged: The inspection environment has changed.");
        cancellationToken.ThrowIfCancellationRequested();
        return new ReusableTemplateInspection(result, fixedTime, fidelityMode, optionsHash, fonts, providers, engine);
    }
}

internal static class TemplateInspectionIdentity
{
    internal static readonly object Process = new object();
    internal const int MaximumInputBytes = 16 * 1024 * 1024;
    private const long MaximumFontBytes = 64L * 1024 * 1024;

    internal static string Options(ConversionOptions options, DateTimeOffset fixedTime) => Digest(writer =>
    {
        if (options.EnglishOcr != null || options.Hyphenation.Dictionary != null || options.HtmlInput.Images.Count != 0
            || options.Fonts.Sources.Count != 0 || options.Fonts.Substitutions.Count != 0 || options.Pdf.Pages.Count != 0)
            throw new NotSupportedException("Plan.Profile: The reusable inspection profile does not admit caller resources or providers.");
        writer.Write("wordnet-reusable-docx-inspection-options-v1"); writer.Write((int)options.FidelityMode);
        writer.Write(options.MaximumInputBytes); writer.Write(options.MaximumOutputBytes); writer.Write(options.MaximumExpandedBytes);
        writer.Write(options.MaximumArchiveEntries); writer.Write(options.MaximumCharacters); writer.Write(options.MaximumNestingDepth);
        writer.Write(options.MaximumPages); writer.Write(fixedTime.UtcDateTime.Ticks); writer.Write(options.BalanceFinalColumns);
        TypographyOptions typography = options.Typography;
        writer.Write(typography.Enabled); writer.Write(typography.ExtendedBidi); writer.Write(typography.Devanagari);
        writer.Write((int)typography.EastAsianLineBreak); writer.Write(typography.MaximumBidiDepth);
        writer.Write((int)typography.Direction); writer.Write(typography.MaximumGlyphs);
        HyphenationOptions hyphenation = options.Hyphenation;
        writer.Write(hyphenation.MinimumPrefix); writer.Write(hyphenation.MinimumSuffix);
        writer.Write(hyphenation.MaximumConsecutiveLines); writer.Write(hyphenation.MaximumWork);
        writer.Write(options.OcrMetadata.Enabled); writer.Write(options.OcrMetadata.MaximumRegions); writer.Write(options.OcrMetadata.MaximumBytes);
        writer.Write(options.PdfImport.ReconstructLayout); writer.Write(options.PdfImport.Columns);
        writer.Write(options.PdfImport.DetectRuledTables); writer.Write(options.PdfImport.MaximumElements);
        writer.Write(options.Html.MaxContentWidthPx); writer.Write(options.Html.IncludeHeadersAndFooters); writer.Write(options.Html.EmbedImages);
        PdfOptions pdf = options.Pdf;
        writer.Write((int)pdf.Accessibility); writer.Write(pdf.Language != null); if (pdf.Language != null) writer.Write(pdf.Language);
        writer.Write((int)pdf.TextBoxReadingOrder); writer.Write((int)pdf.VectorSemantics); writer.Write(pdf.OutlineHeadings);
        writer.Write(pdf.Compress); writer.Write((int)pdf.Conformance);
        writer.Write(options.Fonts.MathFamily != null); if (options.Fonts.MathFamily != null) writer.Write(options.Fonts.MathFamily);
        writer.Write(options.Fonts.DefaultFallback);
    });

    internal static string Engine() => Digest(writer =>
    {
        Assembly core = typeof(WordConverter).Assembly;
        writer.Write("wordnet-reusable-docx-inspection-engine-v1"); writer.Write(core.FullName ?? "");
        writer.Write(core.ManifestModule.ModuleVersionId.ToString("D")); writer.Write(Environment.Version.ToString());
        writer.Write(Environment.OSVersion.VersionString); writer.Write(IntPtr.Size); writer.Write(Environment.Is64BitOperatingSystem);
    });

    internal static string Providers() => Digest(writer => writer.Write("wordnet-reusable-docx-inspection-no-optional-provider-v1"));

    internal static string Fonts(CancellationToken cancellationToken)
    {
        Assembly[] assemblies = { typeof(WordConverter).Assembly, typeof(BundledFonts).Assembly };
        long total = 0; int resources = 0;
        return Digest(writer =>
        {
            writer.Write("wordnet-reusable-docx-inspection-fonts-v1");
            foreach (Assembly assembly in assemblies)
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.Write(assembly.FullName ?? ""); writer.Write(assembly.ManifestModule.ModuleVersionId.ToString("D"));
                foreach (string name in assembly.GetManifestResourceNames().Where(item => item.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)).OrderBy(item => item, StringComparer.Ordinal))
                {
                    if (++resources > 128) throw new InvalidDataException("Limit.TemplateEnvironment: Too many built-in font resources.");
                    using Stream font = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("Font.Resource: A bundled face is missing.");
                    writer.Write(name); writer.Write(Hash(font, MaximumFontBytes, ref total, cancellationToken));
                }
            }
            if (resources == 0) throw new InvalidOperationException("Font.Resource: Built-in font resources are unavailable.");
        });
    }

    internal static string Input(Stream source, CancellationToken cancellationToken)
    {
        long total = 0;
        return Hash(source, MaximumInputBytes, ref total, cancellationToken);
    }

    private static string Hash(Stream source, long maximum, ref long total, CancellationToken cancellationToken)
    {
        using var algorithm = SHA256.Create(); var buffer = new byte[8192];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int requested = (int)Math.Min(buffer.Length, maximum - total + 1);
            int count = source.Read(buffer, 0, requested);
            if (count == 0) break;
            total = checked(total + count);
            if (total > maximum) throw new InvalidDataException("Limit.TemplateInspection: Input or environment bytes exceed the bound.");
            algorithm.TransformBlock(buffer, 0, count, buffer, 0);
        }
        cancellationToken.ThrowIfCancellationRequested();
        algorithm.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Hex(algorithm.Hash!);
    }

    private static string Digest(Action<BinaryWriter> write)
    {
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, Encoding.UTF8, leaveOpen: true)) { write(writer); writer.Flush(); }
        bytes.Position = 0; using var algorithm = SHA256.Create(); return Hex(algorithm.ComputeHash(bytes));
    }

    private static string Hex(byte[] bytes)
    {
#if NET10_0_OR_GREATER
        return Convert.ToHexStringLower(bytes);
#elif NET8_0_OR_GREATER
        return Convert.ToHexString(bytes).ToLowerInvariant();
#else
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
#endif
    }
}
