using System;
using System.Security.Cryptography;

namespace WordNet.Formats;

internal static class PdfIdentity
{
    internal static void Normalize(byte[] bytes)
    {
        // The writer's random trailer IDs are outside all content streams. Replace only
        // their hex digits at equal length, preserving every xref offset and stream length.
        int start = Math.Max(0, bytes.Length - 4096);
        int marker = -1, first = 0, firstLength = 0, second = 0, secondLength = 0;
        for (int index = start; index + 3 <= bytes.Length; index++)
        {
            if (bytes[index] != '/' || bytes[index + 1] != 'I' || bytes[index + 2] != 'D') continue;
            int cursor = index + 3; White(ref cursor);
            if (!Take(ref cursor, '[')) continue;
            White(ref cursor);
            if (!Take(ref cursor, '<')) continue;
            int a = cursor; while (cursor < bytes.Length && Hex(bytes[cursor])) cursor++;
            int aLength = cursor - a; if (aLength == 0 || !Take(ref cursor, '>')) continue;
            White(ref cursor); if (!Take(ref cursor, '<')) continue;
            int b = cursor; while (cursor < bytes.Length && Hex(bytes[cursor])) cursor++;
            int bLength = cursor - b; if (bLength == 0 || !Take(ref cursor, '>')) continue;
            White(ref cursor); if (!Take(ref cursor, ']')) continue;
            marker = index; first = a; firstLength = aLength; second = b; secondLength = bLength; break;
        }
        if (marker < 0) throw new InvalidOperationException("Pdf.Identifier: The generated PDF trailer has no recognizable identifiers.");
#if NET8_0_OR_GREATER
        string hex = Convert.ToHexString(SHA256.HashData(bytes.AsSpan(0, marker)));
#else
        using var hash = SHA256.Create();
        string hex = BitConverter.ToString(hash.ComputeHash(bytes, 0, marker)).Replace("-", "");
#endif
        for (int i = 0; i < firstLength; i++) bytes[first + i] = (byte)hex[i % hex.Length];
        for (int i = 0; i < secondLength; i++) bytes[second + i] = (byte)hex[i % hex.Length];
        void White(ref int position) { while (position < bytes.Length && bytes[position] < 128 && char.IsWhiteSpace((char)bytes[position])) position++; }
        bool Take(ref int position, char value) { if (position >= bytes.Length || bytes[position] != value) return false; position++; return true; }
        static bool Hex(byte value) => value is >= (byte)'0' and <= (byte)'9' or >= (byte)'A' and <= (byte)'F' or >= (byte)'a' and <= (byte)'f';
    }
}
