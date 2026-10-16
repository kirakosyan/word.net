using System;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;
using WordNet.Internal;

namespace WordNet.Ocr;

internal static class OcrImage
{
    internal static SKBitmap Rotate(SKBitmap source, double degrees, EnglishOcrOptions options, ConversionContext context)
    {
        context.CheckCancellation();
        double radians = degrees * Math.PI / 180;
        double cosine = Math.Abs(Math.Cos(radians)), sine = Math.Abs(Math.Sin(radians));
        int width = (int)Math.Ceiling(source.Width * cosine + source.Height * sine - 0.00001);
        int height = (int)Math.Ceiling(source.Width * sine + source.Height * cosine - 0.00001);
        if ((long)width * height > options.MaximumImagePixels) throw new InvalidDataException("Limit.ImagePixels: Corrected OCR raster exceeds the pixel limit.");
        var target = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        try
        {
            using var canvas = new SKCanvas(target);
            canvas.Clear(SKColors.White); canvas.Translate(width / 2f, height / 2f); canvas.RotateDegrees((float)degrees);
            using SKImage image = SKImage.FromBitmap(source);
            canvas.DrawImage(image, new SKRect(-source.Width / 2f, -source.Height / 2f, source.Width / 2f, source.Height / 2f),
                new SKSamplingOptions(SKFilterMode.Linear));
            return target;
        }
        catch { target.Dispose(); throw; }
    }

    internal static double Skew(SKBitmap source, ConversionContext context)
    {
        // Sample a bounded number of dark pixels; score horizontal projections without allocating rotated pages.
        int step = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((double)source.Width * source.Height / 100000)));
        var points = new List<(double X, double Y)>();
        for (int y = 0; y < source.Height; y += step)
        {
            context.CheckCancellation();
            for (int x = 0; x < source.Width; x += step)
            {
                SKColor color = source.GetPixel(x, y);
                if ((color.Red * 299 + color.Green * 587 + color.Blue * 114) / 1000 < 128)
                    points.Add((x - source.Width / 2.0, y - source.Height / 2.0));
            }
        }
        if (points.Count < 100) return 0;
        int size = (source.Width + source.Height) / step + 4;
        var bins = new int[size];
        double baseline = Score(0), best = baseline, angle = 0;
        for (int quarter = -20; quarter <= 20; quarter++)
        {
            context.CheckCancellation();
            double score = Score(quarter * 0.25);
            if (score > best) { best = score; angle = quarter * 0.25; }
        }
        return best > baseline * 1.15 ? angle : 0;

        double Score(double degrees)
        {
            Array.Clear(bins, 0, bins.Length);
            double rad = degrees * Math.PI / 180, sin = Math.Sin(rad), cos = Math.Cos(rad);
            foreach (var point in points)
            {
                int bin = (int)((sin * point.X + cos * point.Y) / step + size / 2.0);
                if (bin >= 0 && bin < bins.Length) bins[bin]++;
            }
            double sum = 0;
            for (int i = 1; i < bins.Length; i++) { double delta = bins[i] - bins[i - 1]; sum += delta * delta; }
            return sum;
        }
    }
}
