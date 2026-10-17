using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace WordNet.Internal;

internal sealed class ConversionContext
{
    internal string PageNumberFormat { get; set; } = "decimal";
    private readonly List<ConversionDiagnostic> diagnostics = new List<ConversionDiagnostic>();
    private readonly Dictionary<string, ConversionDiagnostic> codes = new Dictionary<string, ConversionDiagnostic>(StringComparer.Ordinal);
    private readonly Dictionary<string, FontUsage> fontUsage = new Dictionary<string, FontUsage>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> substitutedFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    internal bool RecordSubstitution(string family) => substitutedFamilies.Add(family);
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    internal byte[][] FontData { get; set; } = Array.Empty<byte[]>();
    internal int? PageCount { get; set; }
    internal DocumentFormat Destination { get; set; }
    internal DiagnosticPhase Phase { get; set; }
    internal SourceLocation Location { get; set; } = new SourceLocation("document", "/");
    internal bool Inspecting { get; set; }
    internal bool Editing { get; set; }
    internal bool HtmlImport { get; set; }
    internal bool PreviewIntermediate { get; set; }
    internal OcrMetadataOptions OcrMetadata { get; }
    internal List<OcrPage> OcrPages { get; } = new List<OcrPage>();
    private long metadataBytes;
    private int metadataRegions;
    internal void ReserveOcrMetadata(int regions, long bytes)
    {
        metadataRegions = checked(metadataRegions + regions); metadataBytes = checked(metadataBytes + bytes);
        if (metadataRegions > OcrMetadata.MaximumRegions || metadataBytes > Math.Min(OcrMetadata.MaximumBytes, Options.MaximumExpandedBytes))
            throw new InvalidDataException("Limit.OcrMetadata: Structured OCR metadata exceeds its aggregate admission budget.");
    }
    internal bool HasStrictRejection { get; private set; }
    internal FidelityMode EffectiveMode => Inspecting && Options.FidelityMode == FidelityMode.Strict ? FidelityMode.BestEffort : Options.FidelityMode;
    internal int? ArchiveEntryCount { get; set; }
    internal long? DeclaredExpandedBytes { get; set; }
    internal long ImagePixels => imagePixels;

    internal IDisposable At(SourceLocation? location) => new LocationScope(this, location ?? Location);

    private sealed class LocationScope : IDisposable
    {
        private readonly ConversionContext context;
        private readonly SourceLocation previous;
        internal LocationScope(ConversionContext context, SourceLocation location)
        { this.context = context; previous = context.Location; context.Location = location; }
        public void Dispose() => context.Location = previous;
    }
    private DateTimeOffset? fieldTime;
    internal DateTimeOffset FieldTime => fieldTime ??= (Options.Clock ?? throw new ArgumentException("Conversion clock must not be null."))();
    internal void RecordFont(string requested, string resolved)
    {
        string key = requested + "\0" + resolved;
        if (!fontUsage.ContainsKey(key)) fontUsage.Add(key, new FontUsage(requested, resolved));
    }
    private long imagePixels;
    private int listId;
    private long ocrConfidenceSum;
    private int ocrPages;
    internal void AddOcrConfidence(int confidence) { ocrConfidenceSum += confidence; ocrPages++; }
    internal string NextListId() => (++listId).ToString(System.Globalization.CultureInfo.InvariantCulture);

    internal void ReserveImagePixels(int width, int height)
    {
        if (width <= 0 || height <= 0 || (imagePixels = checked(imagePixels + (long)width * height)) > Options.MaximumExpandedBytes / 4)
            throw new InvalidDataException("Limit.ImagePixels: Aggregate decoded RGBA images exceed MaximumExpandedBytes.");
    }

    internal ConversionContext(ConversionOptions options, CancellationToken cancellationToken)
    {
        Options = options;
        OcrMetadata = (options.OcrMetadata ?? throw new ArgumentException("OCR metadata options must not be null.")).Snapshot();
        CancellationToken = cancellationToken;
    }

    internal ConversionOptions Options { get; }
    internal CancellationToken CancellationToken { get; }

    internal void CheckCancellation() => CancellationToken.ThrowIfCancellationRequested();

    internal void CheckCharacters(int count)
    {
        if (count > Options.MaximumCharacters)
        {
            throw new InvalidDataException("Limit.Characters: Document exceeds the character limit.");
        }
    }

    internal void Loss(string code, string message)
    {
        CheckCancellation();
        HasStrictRejection = true;
        if (EffectiveMode == FidelityMode.Strict)
        {
            throw new NotSupportedException(code + ": " + message);
        }

        Report(code, message, DiagnosticSeverity.Warning);
    }

    internal void ReportStrictRejection(string code, string message)
    {
        HasStrictRejection = true;
        Report(code, message, DiagnosticSeverity.Warning);
    }

    internal void Report(string code, string message, DiagnosticSeverity severity = DiagnosticSeverity.Info)
    {
        if (!codes.TryGetValue(code, out ConversionDiagnostic? existing))
        {
            var diagnostic = new ConversionDiagnostic(code, message, severity);
            codes.Add(code, diagnostic);
            diagnostics.Add(diagnostic);
        }
        else existing.Count = checked(existing.Count + 1);
        codes[code].Observe(Location, Phase);
    }

    internal void Inherit(ConversionDiagnostic source)
    {
        CheckCancellation();
        if (source.Severity == DiagnosticSeverity.Warning)
        {
            HasStrictRejection = true;
            if (EffectiveMode == FidelityMode.Strict) throw new NotSupportedException(source.Code + ": " + source.Message);
        }
        if (codes.TryGetValue(source.Code, out ConversionDiagnostic? existing)) existing.Include(source);
        else { ConversionDiagnostic copy = source.Copy(); codes.Add(copy.Code, copy); diagnostics.Add(copy); }
    }

    internal ConversionResult Result(long count)
    {
        if (ocrPages > 0)
        {
            DiagnosticPhase previousPhase = Phase;
            Phase = DiagnosticPhase.Provider;
            using (At(new SourceLocation(DocumentFormat.Pdf.ToString(), "/")))
                Report("Ocr.Confidence", "Mean of per-page recognized-word confidences: "
                    + ((double)ocrConfidenceSum / ocrPages).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                    + "/100 across " + ocrPages.ToString(System.Globalization.CultureInfo.InvariantCulture) + " OCR page(s).");
            Phase = previousPhase;
        }
        elapsed.Stop();
        var fonts = fontUsage.Values.ToList().AsReadOnly();
        return new ConversionResult(count, diagnostics.AsReadOnly(), new ConversionMetrics(PageCount, count, elapsed.Elapsed,
            fonts, fonts.Where(f => !f.RequestedFamily.Equals(f.ResolvedFamily, StringComparison.OrdinalIgnoreCase))
                .Select(f => f.RequestedFamily).Distinct(StringComparer.OrdinalIgnoreCase).Count()), OcrPages.AsReadOnly());
    }

    internal string? SafeLink(string? link)
    {
        if (link != null && !link.Any(char.IsControl) && Uri.TryCreate(link, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeMailto))
        {
            return link;
        }

        Loss("Link.UnsupportedTarget", "Only absolute HTTP, HTTPS and mailto hyperlinks are retained; other targets become text.");
        return null;
    }
}
