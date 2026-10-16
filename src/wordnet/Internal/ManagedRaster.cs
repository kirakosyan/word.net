using System;
using System.Collections.Generic;
using System.IO;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Filters.Dct.JpegLibrary;
using UglyToad.PdfPig.Tokens;
using UglyToad.PdfPig.Writer;

namespace WordNet.Internal;

internal static class ManagedRaster
{
    internal static (string Extension, int Width, int Height) Identify(byte[] bytes)
    {
        if (bytes.Length >= 10 && bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == '8' && bytes[5] == 'a')
            return ("gif", LegacyRaster.Little16(bytes, 6), LegacyRaster.Little16(bytes, 8));
        if (bytes.Length >= 54 && bytes[0] == 'B' && bytes[1] == 'M')
        {
            int height = LegacyRaster.Little32(bytes, 22);
            if (height == int.MinValue) throw new InvalidDataException("Image.Dimensions: Invalid BMP height.");
            return ("bmp", LegacyRaster.Little32(bytes, 18), Math.Abs(height));
        }
        if (bytes.Length >= 24 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71
            && bytes[4] == 13 && bytes[5] == 10 && bytes[6] == 26 && bytes[7] == 10)
        {
            if (bytes[12] != 'I' || bytes[13] != 'H' || bytes[14] != 'D' || bytes[15] != 'R')
                throw new InvalidDataException("Image.Invalid: PNG must start with its image header.");
            return ("png", Big32(bytes, 16), Big32(bytes, 20));
        }
        if (bytes.Length < 4 || bytes[0] != 255 || bytes[1] != 216)
            throw new NotSupportedException("Image.UnsupportedFormat: Expected an embedded PNG, JPEG, GIF or BMP image.");
        int offset = 2;
        while (offset < bytes.Length)
        {
            if (bytes[offset++] != 255) throw new InvalidDataException("Image.Invalid: Invalid JPEG marker.");
            while (offset < bytes.Length && bytes[offset] == 255) offset++;
            if (offset >= bytes.Length) break;
            int marker = bytes[offset++];
            if (marker is 0xD9 or 0xDA) break;
            if (marker is 0x01 or >= 0xD0 and <= 0xD7) continue;
            if (offset > bytes.Length - 2) break;
            int length = (bytes[offset] << 8) | bytes[offset + 1];
            if (length < 2 || offset > bytes.Length - length) break;
            if (marker is >= 0xC0 and <= 0xCF && marker is not 0xC4 and not 0xC8 and not 0xCC)
            {
                if (length < 8) break;
                return ("jpg", (bytes[offset + 5] << 8) | bytes[offset + 6], (bytes[offset + 3] << 8) | bytes[offset + 4]);
            }
            offset += length;
        }
        throw new InvalidDataException("Image.Invalid: Missing or truncated JPEG frame header.");
    }

    internal static void Validate(byte[] bytes, string extension, ConversionContext context)
    {
        try
        {
            if (extension == "png")
            {
                PngAdmission.Validate(bytes, context);
                using var validator = new PdfDocumentBuilder();
                // AddPng inflates and unfilters the complete image using PdfPig's managed reader.
                validator.AddPage(1, 1).AddPng(bytes, new PdfRectangle(0, 0, 1, 1));
            }
            else
            {
                if (!HasJpegEnd(bytes))
                    throw new InvalidDataException("Image.Decode: Truncated JPEG data.");
                var filter = new JpegLibraryDctDecodeFilter();
                _ = filter.Decode(bytes, new DictionaryToken(new Dictionary<NameToken, IToken>()), DefaultFilterProvider.Instance, 0);
            }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IndexOutOfRangeException or OverflowException)
        {
            throw new InvalidDataException("Image.Decode: Incomplete or invalid raster data.", error);
        }
    }

    private static bool HasJpegEnd(byte[] bytes)
    {
        // Skip length-delimited metadata and byte-stuffed entropy data. An EOI byte
        // pattern inside an EXIF thumbnail must not validate a truncated main image.
        if (bytes.Length < 4 || bytes[0] != 255 || bytes[1] != 216) return false;
        int offset = 2; bool entropy = false;
        while (offset < bytes.Length)
        {
            if (bytes[offset++] != 255) { if (entropy) continue; return false; }
            while (offset < bytes.Length && bytes[offset] == 255) offset++;
            if (offset == bytes.Length) return false;
            int marker = bytes[offset++];
            if (entropy && (marker == 0 || marker is >= 0xd0 and <= 0xd7)) continue;
            if (marker == 0xd9) return entropy;
            if (marker == 0x01) continue;
            if (offset > bytes.Length - 2) return false;
            int length = (bytes[offset] << 8) | bytes[offset + 1];
            if (length < 2 || length > bytes.Length - offset) return false;
            offset += length;
            // Progressive JPEGs can have multiple scans, with tables between them.
            entropy = marker == 0xda;
        }
        return false;
    }

    private static int Big32(byte[] bytes, int offset)
    {
        uint value = ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
        if (value > int.MaxValue) throw new InvalidDataException("Image.Dimensions: Image dimension exceeds the supported range.");
        return (int)value;
    }
}
