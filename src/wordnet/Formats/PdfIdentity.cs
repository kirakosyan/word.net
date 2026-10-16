using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace WordNet.Formats;

internal static class PdfIdentity
{
    internal static void Normalize(byte[] bytes)
    {
        // The writer's random trailer IDs are outside all content streams. Replace only
        // their hex digits at equal length, preserving every xref offset and stream length.
        int start = Math.Max(0, bytes.Length - 4096);
        string tail = Encoding.ASCII.GetString(bytes, start, bytes.Length - start);
        Match match = Regex.Match(tail, @"/ID\s*\[\s*<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*\]", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!match.Success) throw new InvalidOperationException("Pdf.Identifier: The generated PDF trailer has no recognizable identifiers.");
#if NET8_0_OR_GREATER
        string hex = Convert.ToHexString(SHA256.HashData(bytes.AsSpan(0, start + match.Index)));
#else
        using var hash = SHA256.Create();
        string hex = BitConverter.ToString(hash.ComputeHash(bytes, 0, start + match.Index)).Replace("-", "");
#endif
        for (int group = 1; group <= 2; group++)
            for (int i = 0; i < match.Groups[group].Length; i++) bytes[start + match.Groups[group].Index + i] = (byte)hex[i % hex.Length];
    }
}
