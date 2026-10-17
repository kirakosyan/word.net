using System;
using System.IO;
using WordNet.Internal;

namespace WordNet.Model;

internal sealed class DocumentImage
{
    private DocumentImage(byte[] bytes, string extension, int pixelsWide, int pixelsHigh, double width, double height, string alternativeText)
    { Bytes = bytes; Extension = extension; PixelsWide = pixelsWide; PixelsHigh = pixelsHigh; FrameWidth = width; FrameHeight = height; AlternativeText = alternativeText; }
    internal byte[] Bytes { get; }
    internal string Extension { get; }
    internal string MediaType => Extension == "png" ? "image/png" : "image/jpeg";
    internal int PixelsWide { get; }
    internal int PixelsHigh { get; }
    internal double FrameWidth { get; }
    internal double FrameHeight { get; }
    internal double Width => Rotation % 180 == 0 ? FrameWidth : FrameHeight;
    internal double Height => Rotation % 180 == 0 ? FrameHeight : FrameWidth;
    internal DrawingAnchor? Anchor { get; set; }
    internal int Rotation { get; set; }
    internal bool FlipHorizontal { get; set; }
    internal bool FlipVertical { get; set; }
    internal double CropLeft { get; set; }
    internal double CropRight { get; set; }
    internal double CropTop { get; set; }
    internal double CropBottom { get; set; }
    internal bool Decorative { get; set; }
    internal bool Transformed => Rotation != 0 || FlipHorizontal || FlipVertical || CropLeft != 0 || CropRight != 0 || CropTop != 0 || CropBottom != 0;
    internal string AlternativeText { get; }
    internal DocumentImage Scaled(double factor) => new DocumentImage(Bytes, Extension, PixelsWide, PixelsHigh, FrameWidth * factor, FrameHeight * factor, AlternativeText)
    { Decorative = Decorative, Anchor = Anchor, Rotation = Rotation, FlipHorizontal = FlipHorizontal, FlipVertical = FlipVertical, CropLeft = CropLeft, CropRight = CropRight, CropTop = CropTop, CropBottom = CropBottom };

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
            double scale = 1584 / Math.Max(w, h); w = Math.Min(1584, w * scale); h = Math.Min(1584, h * scale);
        }
        return new DocumentImage(bytes, extension, info.Width, info.Height, w, h, alt);
    }

    private static bool ValidDimension(double value) => value > 0 && !double.IsInfinity(value) && !double.IsNaN(value);
}
