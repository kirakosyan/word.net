using System;
using System.IO;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Rendering.Skia;
using WordNet.Internal;

namespace WordNet.Ocr;

internal static class PreviewRaster
{
    internal static byte[] Render(byte[] bytes, int width, int height, ConversionContext context)
    {
        context.CheckCancellation();
        using PdfDocument pdf = PdfDocument.Open(bytes, SkiaRenderingParsingOptions.Instance); pdf.AddSkiaPageFactory();
        using SKBitmap rendered = pdf.GetPageAsSKBitmap(1, (float)(width / pdf.GetPage(1).Width), SKColors.White);
        context.CheckCancellation();
        using var target = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(target)) { canvas.Clear(SKColors.White); canvas.DrawBitmap(rendered, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear)); }
        using SKImage image = SKImage.FromBitmap(target); using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        if (data.Size > context.Options.MaximumOutputBytes) throw new InvalidDataException("Limit.OutputBytes: Encoded PNG exceeds its byte bound.");
        context.CheckCancellation(); return data.ToArray();
    }
}
