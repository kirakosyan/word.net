using System;
using System.IO;
using WordNet.Internal;

namespace WordNet.Model;

internal sealed class DocumentImage
{
    private DocumentImage(byte[] bytes, string extension, int pixelsWide, int pixelsHigh, double width, double height, string alternativeText)
    { Bytes = bytes; Extension = extension; PixelsWide = pixelsWide; PixelsHigh = pixelsHigh; Width = width; Height = height; AlternativeText = alternativeText; }
    internal byte[] Bytes { get; }
    internal string Extension { get; }
    internal string MediaType => Extension == "png" ? "image/png" : "image/jpeg";
    internal int PixelsWide { get; }
    internal int PixelsHigh { get; }
    internal double Width { get; }
    internal double Height { get; }
    internal string AlternativeText { get; }
    internal DocumentImage Scaled(double factor) => new DocumentImage(Bytes, Extension, PixelsWide, PixelsHigh, Width * factor, Height * factor, AlternativeText);

    internal static DocumentImage Placeholder(double width, double height, string alt, ConversionContext context)
    {
        // A visible checkerboard, with the original physical extent, needs no graphics provider.
        byte[] pixels = { 180, 180, 180, 255, 240, 240, 240, 255, 240, 240, 240, 255, 180, 180, 180, 255 };
        return Read(LegacyRaster.Png(pixels, 2, 2), width, height, alt, context);
    }

    internal static DocumentImage Read(byte[] bytes, double? width, double? height, string alt, ConversionContext context, int reservedWidth = 0, int reservedHeight = 0)
    {
        context.CheckCancellation();
        var info = ManagedRaster.Identify(bytes);
        string extension = info.Extension;
        if (reservedWidth == 0) context.ReserveImagePixels(info.Width, info.Height);
        else if (info.Width != reservedWidth || info.Height != reservedHeight)
            throw new InvalidDataException("Image.Dimensions: Encoded dimensions differ from the reserved PDF image dimensions.");
        if (extension is "gif" or "bmp")
        {
            bytes = extension == "gif" ? LegacyRaster.Gif(bytes, info.Width, info.Height) : LegacyRaster.Bmp(bytes, info.Width, info.Height);
            context.Report("Image.Reencoded", "A GIF/BMP raster is re-encoded as PNG; GIF output uses the first frame.");
            extension = "png";
        }
        // Admission happens before provider allocation; decode completely with managed providers.
        ManagedRaster.Validate(bytes, extension, context);
        double w = width ?? (height.HasValue ? height.Value * info.Width / info.Height : info.Width * 0.75);
        double h = height ?? w * info.Height / info.Width;
        if (!ValidDimension(w) || !ValidDimension(h)) throw new InvalidDataException("Image.Dimensions: Image dimensions must be positive finite points.");
        if (Math.Max(w, h) > 1584)
        {
            context.Loss("Image.Scaled", "Image dimensions exceed the 1584-point profile and are scaled proportionally; encoded pixels are retained.");
            double scale = 1584 / Math.Max(w, h); w *= scale; h *= scale;
        }
        return new DocumentImage(bytes, extension, info.Width, info.Height, w, h, alt);
    }

    private static bool ValidDimension(double value) => value > 0 && !double.IsInfinity(value) && !double.IsNaN(value);
}
