using System;

namespace WordNet.Internal;

internal static partial class ManagedRaster
{
    // PNG pHYs, JFIF APP0 and TIFF IFD0 resolution tags. Metadata is optional;
    // malformed or unspecified densities use the XPS 96-dpi default.
    internal static (double X, double Y) Density(byte[] bytes, string extension)
    {
        if (extension == "png")
        {
            for (int offset = 8; offset <= bytes.Length - 12;)
            {
                uint length = Big(offset);
                if (length > bytes.Length - offset - 12) break;
                if (length == 9 && bytes[offset + 4] == 'p' && bytes[offset + 5] == 'H' && bytes[offset + 6] == 'Y' && bytes[offset + 7] == 's' && bytes[offset + 16] == 1)
                    return Valid(Big(offset + 8) * 0.0254, Big(offset + 12) * 0.0254);
                offset += (int)length + 12;
            }
            return (96, 96);
        }
        (double X, double Y) density = (96, 96);
        for (int offset = 2; offset < bytes.Length;)
        {
            if (bytes[offset++] != 255) break;
            while (offset < bytes.Length && bytes[offset] == 255) offset++;
            if (offset >= bytes.Length) break;
            int marker = bytes[offset++];
            if (marker is 0xd9 or 0xda) break;
            if (marker is 0x01 or >= 0xd0 and <= 0xd7) continue;
            if (offset > bytes.Length - 2) break;
            int length = bytes[offset] * 256 + bytes[offset + 1];
            if (length < 2 || length > bytes.Length - offset) break;
            int start = offset + 2;
            if (marker == 0xe0 && length >= 16 && bytes[start] == 'J' && bytes[start + 1] == 'F' && bytes[start + 2] == 'I' && bytes[start + 3] == 'F' && bytes[start + 4] == 0)
            {
                double unit = bytes[start + 7] == 1 ? 1 : bytes[start + 7] == 2 ? 2.54 : 0;
                if (unit > 0) density = Valid((bytes[start + 8] * 256 + bytes[start + 9]) * unit, (bytes[start + 10] * 256 + bytes[start + 11]) * unit);
            }
            if (marker == 0xe1 && length >= 16 && bytes[start] == 'E' && bytes[start + 1] == 'x' && bytes[start + 2] == 'i' && bytes[start + 3] == 'f' && bytes[start + 4] == 0 && bytes[start + 5] == 0)
            {
                var exif = ExifDensity(bytes, start + 6, offset + length);
                if (exif.HasValue) return exif.Value;
            }
            offset += length;
        }
        return density;

        uint Big(int offset) => ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
    }

    private static (double X, double Y) Valid(double x, double y) => x > 0 && y > 0 && !double.IsInfinity(x) && !double.IsInfinity(y) ? (x, y) : (96, 96);

    private static (double X, double Y)? ExifDensity(byte[] bytes, int start, int end)
    {
        bool little = bytes[start] == 'I' && bytes[start + 1] == 'I';
        if (!little && (bytes[start] != 'M' || bytes[start + 1] != 'M') || Short(2) != 42) return null;
        uint directory = Long(4);
        if (directory > end - start - 2) return null;
        int count = Short((int)directory);
        double x = 0, y = 0, unit = 1;
        for (int i = 0; i < count; i++)
        {
            long entry = (long)directory + 2 + i * 12;
            if (entry > end - start - 12) return null;
            int offset = (int)entry, tag = Short(offset), type = Short(offset + 2);
            if (Long(offset + 4) != 1) continue;
            if (tag == 0x0128 && type == 3) unit = Short(offset + 8) == 3 ? 2.54 : Short(offset + 8) == 2 ? 1 : 0;
            if (tag is not 0x011a and not 0x011b || type != 5) continue;
            uint rational = Long(offset + 8);
            if (rational > end - start - 8) continue;
            uint denominator = Long((int)rational + 4);
            double value = denominator == 0 ? 0 : Long((int)rational) / (double)denominator;
            if (tag == 0x011a) x = value; else y = value;
        }
        return x > 0 && y > 0 && unit > 0 ? Valid(x * unit, y * unit) : null;

        int Short(int offset) => offset < 0 || offset > end - start - 2 ? 0 : little ? bytes[start + offset] | bytes[start + offset + 1] << 8 : bytes[start + offset] << 8 | bytes[start + offset + 1];
        uint Long(int offset) => offset < 0 || offset > end - start - 4 ? 0 : little ? (uint)Short(offset) | ((uint)Short(offset + 2) << 16) : ((uint)Short(offset) << 16) | (uint)Short(offset + 2);
    }
}
