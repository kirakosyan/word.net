using System;
using System.Collections.Generic;
using System.IO;

namespace WordNet;

/// <summary>An immutable caller-supplied OpenType/TrueType face. Caller streams remain open.</summary>
public sealed partial class FontSource
{
    private readonly byte[] data;

    /// <summary>Reads a named font face from the stream's current position, leaving it open.</summary>
    /// <param name="familyName">The family used in document font requests.</param>
    /// <param name="source">The font bytes; at most 16 MiB.</param>
    /// <param name="bold">Whether this is the bold face.</param>
    /// <param name="italic">Whether this is the italic face.</param>
    public FontSource(string familyName, Stream source, bool bold = false, bool italic = false)
    {
        if (string.IsNullOrWhiteSpace(familyName)) throw new ArgumentException("Font family must not be empty.", nameof(familyName));
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(source);
#else
        if (source == null) throw new ArgumentNullException(nameof(source));
#endif
        using var buffer = new MemoryStream();
        var chunk = new byte[8192]; int count;
        while ((count = source.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + count > 16 * 1024 * 1024) throw new InvalidDataException("Font.Limit: A font face exceeds 16 MiB.");
            buffer.Write(chunk, 0, count);
        }
        if (buffer.Length < 12) throw new InvalidDataException("Font.Invalid: Font header is missing.");
        data = buffer.ToArray(); FamilyName = familyName; Bold = bold; Italic = italic; Weight = bold ? 700 : 400;
    }

    /// <summary>Gets the requested family name associated with this face.</summary>
    public string FamilyName { get; }
    /// <summary>Gets whether this is a bold face.</summary>
    public bool Bold { get; }
    /// <summary>Gets whether this is an italic face.</summary>
    public bool Italic { get; }

    /// <summary>Reads an explicitly supplied file. No OS font discovery is performed.</summary>
    /// <param name="familyName">The family used in document requests.</param>
    /// <param name="path">The caller-selected font file.</param>
    /// <param name="bold">Whether this is the bold face.</param>
    /// <param name="italic">Whether this is the italic face.</param>
    /// <returns>An immutable font source.</returns>
    public static FontSource FromFile(string familyName, string path, bool bold = false, bool italic = false)
    {
        using var stream = File.OpenRead(path);
        return new FontSource(familyName, stream, bold, italic);
    }

    internal byte[] Bytes => data;
    internal int Weight { get; private set; }
}

/// <summary>Configures font resolution without consulting installed fonts. Configure before conversion.</summary>
public sealed class FontOptions
{
    /// <summary>Gets or sets the exact caller-supplied regular static MATH face for equation layout. No automatic math fallback is used.</summary>
    public string? MathFamily { get; set; }
    /// <summary>Gets caller-supplied faces. Exact family and style matches take priority.</summary>
    public IList<FontSource> Sources { get; } = new List<FontSource>();
    /// <summary>Gets case-insensitive requested-family to replacement-family mappings.</summary>
    public IDictionary<string, string> Substitutions { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gets or sets the fallback family, defaulting to embedded Noto Sans.</summary>
    public string DefaultFallback { get; set; } = "Noto Sans";
}
