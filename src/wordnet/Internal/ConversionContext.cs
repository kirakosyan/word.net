using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace WordNet.Internal;

internal sealed class ConversionContext
{
    private readonly List<ConversionDiagnostic> diagnostics = new List<ConversionDiagnostic>();
    private readonly HashSet<string> codes = new HashSet<string>(StringComparer.Ordinal);
    private long imagePixels;
    private int listId;
    internal string NextListId() => (++listId).ToString(System.Globalization.CultureInfo.InvariantCulture);

    internal void ReserveImagePixels(int width, int height)
    {
        if (width <= 0 || height <= 0 || (imagePixels = checked(imagePixels + (long)width * height)) > Options.MaximumExpandedBytes / 4)
            throw new InvalidDataException("Limit.ImagePixels: Aggregate decoded RGBA images exceed MaximumExpandedBytes.");
    }

    internal ConversionContext(ConversionOptions options, CancellationToken cancellationToken)
    {
        Options = options;
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
        if (Options.FidelityMode == FidelityMode.Strict)
        {
            throw new NotSupportedException(code + ": " + message);
        }

        Report(code, message);
    }

    internal void Report(string code, string message)
    {
        if (codes.Add(code))
        {
            diagnostics.Add(new ConversionDiagnostic(code, message));
        }
    }

    internal ConversionResult Result(long count) => new ConversionResult(count, diagnostics.AsReadOnly());

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
