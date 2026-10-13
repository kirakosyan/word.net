using System;
using System.IO;
using SkiaSharp;
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

    internal static DocumentImage Read(byte[] bytes, double? width, double? height, string alt, ConversionContext context, int reservedWidth = 0, int reservedHeight = 0)
    {
        context.CheckCancellation();
        using SKData data = SKData.CreateCopy(bytes);
        using SKCodec codec = SKCodec.Create(data) ?? throw new InvalidDataException("Image.Invalid: Invalid raster image.");
        string extension = codec.EncodedFormat switch
        {
            SKEncodedImageFormat.Png => "png",
            SKEncodedImageFormat.Jpeg => "jpg",
            _ => throw new NotSupportedException("Image.Format: Only embedded PNG and JPEG images are supported."),
        };
        SKImageInfo info = codec.Info;
        if (reservedWidth == 0) context.ReserveImagePixels(info.Width, info.Height);
        else if (info.Width != reservedWidth || info.Height != reservedHeight)
            throw new InvalidDataException("Image.Dimensions: Encoded dimensions differ from the reserved PDF image dimensions.");
        // Reject truncated images rather than passing corrupt payloads to output documents.
        using var bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            throw new InvalidDataException("Image.Decode: Incomplete or invalid raster data.");
        double w = width ?? (height.HasValue ? height.Value * info.Width / info.Height : info.Width * 0.75);
        double h = height ?? w * info.Height / info.Width;
        if (!ValidDimension(w) || !ValidDimension(h)) throw new InvalidDataException("Image.Dimensions: Image dimensions must be positive finite points, at most 1584.");
        return new DocumentImage(bytes, extension, info.Width, info.Height, w, h, alt);
    }

    private static bool ValidDimension(double value) => value > 0 && value <= 1584 && !double.IsNaN(value);
}
