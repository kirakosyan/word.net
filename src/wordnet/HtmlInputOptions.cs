using System;
using System.Collections.Generic;
using System.IO;

namespace WordNet;

/// <summary>Controls safe HTML input. Resources are caller-supplied; no URI is fetched.</summary>
public sealed class HtmlInputOptions
{
    /// <summary>Gets exact, case-sensitive image source names and copied immutable image values. Configure before concurrent use.</summary>
    public IDictionary<string, TemplateImage> Images { get; } = new Dictionary<string, TemplateImage>(StringComparer.Ordinal);

    internal Dictionary<string, TemplateImage> Snapshot(ConversionOptions limits)
    {
        var copy = new Dictionary<string, TemplateImage>(StringComparer.Ordinal);
        long bytes = 0;
        foreach (var pair in Images)
        {
            if (pair.Key.Length > 4096 || pair.Value == null) throw new ArgumentException("HTML image resource names/values are invalid.");
            if (copy.Count >= limits.MaximumArchiveEntries || (bytes = checked(bytes + pair.Value.Bytes.Length)) > limits.MaximumExpandedBytes)
                throw new InvalidDataException("Limit.HtmlResources: Caller HTML resources exceed resource bounds.");
            copy.Add(pair.Key, pair.Value);
        }
        return copy;
    }
}
