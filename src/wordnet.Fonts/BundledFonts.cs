using System;
using System.IO;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("wordnet")]

namespace WordNet.Fonts;

internal static class BundledFonts
{
    internal static (string Family, byte[] Bytes)? Read(string family, bool bold, bool italic)
    {
        string? name = family.ToUpperInvariant() switch
        {
            "ARIAL" or "LIBERATION SANS" => "LiberationSans",
            "TIMES NEW ROMAN" or "LIBERATION SERIF" => "LiberationSerif",
            "COURIER NEW" or "LIBERATION MONO" => "LiberationMono",
            "CALIBRI" or "CARLITO" => "Carlito",
            "CAMBRIA" or "CALADEA" => "Caladea",
            _ => null,
        };
        if (name == null) return null;
        string face = bold ? italic ? "BoldItalic" : "Bold" : italic ? "Italic" : "Regular";
        using Stream stream = typeof(BundledFonts).Assembly.GetManifestResourceStream("WordNet.Fonts.Resources." + name + "-" + face + ".ttf")
            ?? throw new InvalidOperationException("Font.Resource: A bundled fallback face is missing.");
        using var bytes = new MemoryStream(); stream.CopyTo(bytes);
        string resolved = name == "LiberationSans" ? "Liberation Sans" : name == "LiberationSerif" ? "Liberation Serif" : name == "LiberationMono" ? "Liberation Mono" : name;
        return (resolved, bytes.ToArray());
    }
}
