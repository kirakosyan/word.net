using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using SkiaSharp;
using WordNet.Formats;
using WordNet.Internal;

namespace WordNet.Ocr;

internal static class EnglishOcr
{
    internal static string Recognize(SKBitmap bitmap, EnglishOcrOptions options, ConversionContext context)
    {
        if (!File.Exists(Path.Combine(options.DataDirectory, "eng.traineddata"))) throw new FileNotFoundException("Ocr.LanguageData: eng.traineddata must be provisioned locally.");
        if ((long)bitmap.Width * bitmap.Height > options.MaximumImagePixels) throw new InvalidDataException("Limit.ImagePixels: OCR image exceeds the pixel limit.");
        using var module = new NativeModule(options.NativeLibraryPath);
        Create create = module.Function<Create>("TessBaseAPICreate");
        Delete delete = module.Function<Delete>("TessBaseAPIDelete");
        Init init = module.Function<Init>("TessBaseAPIInit3");
        SetImage image = module.Function<SetImage>("TessBaseAPISetImage");
        SetInteger resolution = module.Function<SetInteger>("TessBaseAPISetSourceResolution");
        RecognizeImage recognize = module.Function<RecognizeImage>("TessBaseAPIRecognize");
        GetText getText = module.Function<GetText>("TessBaseAPIGetUTF8Text");
        GetConfidence confidence = module.Function<GetConfidence>("TessBaseAPIMeanTextConf");
        Delete deleteText = module.Function<Delete>("TessDeleteText");
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
            if (recognize(api, IntPtr.Zero) != 0) throw new InvalidOperationException("Ocr.Recognition: Tesseract recognition failed.");
            context.CheckCancellation();
            IntPtr pointer = getText(api);
            if (pointer == IntPtr.Zero) throw new InvalidOperationException("Ocr.Recognition: Tesseract returned no result.");
            try
            {
                var bytes = new List<byte>();
                long maximum = (long)context.Options.MaximumCharacters * 4;
                for (int offset = 0; ; offset++)
                {
                    if (offset > maximum) throw new InvalidDataException("Limit.Characters: OCR output exceeds the character limit.");
                    byte value = Marshal.ReadByte(pointer, offset); if (value == 0) break; bytes.Add(value);
                }
                string text = TextFormat.Decode(bytes.ToArray(), context);
                context.Report("Ocr.English", "English printed-text OCR was used; recognition requires review.");
                context.Report("Ocr.Confidence", "Mean recognized-word confidence: " + confidence(api).ToString(CultureInfo.InvariantCulture) + "/100.");
                return text.TrimEnd('\n', '\r');
            }
            finally { deleteText(pointer); }
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
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetConfidence(IntPtr api);
}
