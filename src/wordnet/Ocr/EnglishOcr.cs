using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using SkiaSharp;
using WordNet.Internal;

namespace WordNet.Ocr;

internal static class EnglishOcr
{
    internal static string Recognize(SKBitmap bitmap, EnglishOcrOptions options, ConversionContext context)
    {
        (string Text, int Confidence, bool Columns) best = RecognizeSingle(bitmap, options, context);
        double bestScore = Score(best), correction = 0;
        if (options.CorrectOrientation)
        {
            for (int angle = 0; angle < 360; angle += 90)
            {
                using SKBitmap rotated = OcrImage.Rotate(bitmap, angle, options, context);
                double skew = OcrImage.Skew(rotated, context);
                if (angle == 0 && skew == 0) continue;
                using SKBitmap corrected = OcrImage.Rotate(rotated, skew, options, context);
                var candidate = RecognizeSingle(corrected, options, context);
                double score = Score(candidate);
                if (score > bestScore) { best = candidate; bestScore = score; correction = angle + skew; }
            }
            if (correction != 0) context.Report("Ocr.Orientation", "OCR selected a rotated or deskewed raster using recognition confidence; review the recognized text.");
        }
        context.Report("Ocr.English", "English printed-text OCR was used; recognition requires review.");
        if (best.Columns) context.Report("Ocr.Columns", "OCR read two text columns down the left column, then down the right column, using a wide uninterrupted gutter.");
        context.AddOcrConfidence(best.Confidence);
        return best.Text;

        static double Score((string Text, int Confidence, bool Columns) result) => result.Confidence * Math.Min(1, result.Text.Count(char.IsLetterOrDigit) / 20.0);
    }

    private static (string Text, int Confidence, bool Columns) RecognizeSingle(SKBitmap bitmap, EnglishOcrOptions options, ConversionContext context)
    {
        if (!File.Exists(Path.Combine(options.DataDirectory, "eng.traineddata"))) throw new FileNotFoundException("Ocr.LanguageData: eng.traineddata must be provisioned locally.");
        if ((long)bitmap.Width * bitmap.Height > options.MaximumImagePixels) throw new InvalidDataException("Limit.ImagePixels: OCR image exceeds the pixel limit.");
        NativeModule module = NativeModule.Load(options.NativeLibraryPath);
        Create create = module.Function<Create>("TessBaseAPICreate");
        Delete delete = module.Function<Delete>("TessBaseAPIDelete");
        Init init = module.Function<Init>("TessBaseAPIInit3");
        SetImage image = module.Function<SetImage>("TessBaseAPISetImage");
        SetInteger resolution = module.Function<SetInteger>("TessBaseAPISetSourceResolution");
        RecognizeImage recognize = module.Function<RecognizeImage>("TessBaseAPIRecognize");
        GetText getText = module.Function<GetText>("TessBaseAPIGetUTF8Text");
        GetPageText getTsv = module.Function<GetPageText>("TessBaseAPIGetTsvText");
        GetConfidence confidence = module.Function<GetConfidence>("TessBaseAPIMeanTextConf");
        Delete deleteText = module.Function<Delete>("TessDeleteText");
        Create createMonitor = module.Function<Create>("TessMonitorCreate");
        Delete deleteMonitor = module.Function<Delete>("TessMonitorDelete");
        SetCancel setCancel = module.Function<SetCancel>("TessMonitorSetCancelFunc");
        IntPtr api = create();
        if (api == IntPtr.Zero) throw new InvalidOperationException("Ocr.Initialize: Could not allocate OCR engine.");
        try
        {
            context.CheckCancellation();
            if (init(api, NativeModule.Utf8(options.DataDirectory), NativeModule.Utf8("eng")) != 0) throw new InvalidOperationException("Ocr.Initialize: English model could not be loaded.");
            using SKBitmap gray = bitmap.Copy(SKColorType.Gray8) ?? throw new InvalidOperationException("Ocr.Image: Could not convert raster to grayscale.");
            image(api, gray.GetPixels(), gray.Width, gray.Height, 1, gray.RowBytes);
            resolution(api, options.ResolutionDpi);
            context.CheckCancellation();
            IntPtr monitor = createMonitor();
            if (monitor == IntPtr.Zero) throw new InvalidOperationException("Ocr.Initialize: Could not allocate OCR progress monitor.");
            Cancel callback = (_, _) => context.CancellationToken.IsCancellationRequested;
            int status;
            try { setCancel(monitor, callback); status = recognize(api, monitor); }
            finally { GC.KeepAlive(callback); deleteMonitor(monitor); }
            context.CheckCancellation();
            if (status != 0) throw new InvalidOperationException("Ocr.Recognition: Tesseract recognition failed.");
            string text = ReadText(getText(api), Math.Min(int.MaxValue - 1L, (long)context.Options.MaximumCharacters * 4));
            context.CheckCharacters(text.Length);
            string tsv = ReadText(getTsv(api, 0), context.Options.MaximumExpandedBytes);
            string? columns = OcrReadingOrder.Columns(tsv, bitmap.Width, bitmap.Height, context);
            return (columns ?? text.TrimEnd('\n', '\r'), confidence(api), columns != null);

            string ReadText(IntPtr pointer, long maximum)
            {
                if (pointer == IntPtr.Zero) throw new InvalidOperationException("Ocr.Recognition: Tesseract returned no result.");
                try
                {
                    var bytes = new List<byte>();
                    for (int offset = 0; ; offset++)
                    {
                        if (offset % 4096 == 0) context.CheckCancellation();
                        if (offset >= maximum) throw new InvalidDataException("Limit.OcrText: OCR text or geometry exceeds the configured limit.");
                        byte value = Marshal.ReadByte(pointer, offset); if (value == 0) break; bytes.Add(value);
                    }
                    return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
                }
                finally { deleteText(pointer); }
            }
        }
        finally { delete(api); }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Create();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Delete(IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Init(IntPtr api, byte[] data, byte[] language);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetImage(IntPtr api, IntPtr data, int width, int height, int bytesPerPixel, int bytesPerLine);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetInteger(IntPtr api, int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int RecognizeImage(IntPtr api, IntPtr monitor);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetText(IntPtr api);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetPageText(IntPtr api, int page);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetConfidence(IntPtr api);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool Cancel(IntPtr state, int words);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetCancel(IntPtr monitor, Cancel callback);
}
