using System;
using System.IO;

namespace WordNet;

/// <summary>Local Tesseract 5 English printed-text recognition and rasterization settings.</summary>
/// <remarks>
/// Provision native Tesseract and eng.traineddata explicitly. No downloads or remote services are used.
/// A loaded native library is retained for the process lifetime; per-page engines and buffers are released.
/// </remarks>
public sealed class EnglishOcrOptions
{
    /// <summary>Creates an English OCR configuration.</summary>
    /// <param name="dataDirectory">Directory containing eng.traineddata.</param>
    /// <param name="nativeLibraryPath">Absolute path of a Tesseract 5 C-API library for the process architecture, including its dependencies.</param>
    /// <param name="resolutionDpi">PDF rasterization resolution, from 72 through 600 DPI.</param>
    /// <param name="maximumImagePixels">Maximum raster pixels per page.</param>
    public EnglishOcrOptions(string dataDirectory, string nativeLibraryPath, int resolutionDpi = 200, int maximumImagePixels = 16 * 1024 * 1024)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory)) throw new ArgumentException("A local data directory is required.", nameof(dataDirectory));
        if (string.IsNullOrWhiteSpace(nativeLibraryPath)) throw new ArgumentException("A native library path is required.", nameof(nativeLibraryPath));
        if (resolutionDpi < 72 || resolutionDpi > 600) throw new ArgumentOutOfRangeException(nameof(resolutionDpi));
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumImagePixels);
#else
        if (maximumImagePixels <= 0) throw new ArgumentOutOfRangeException(nameof(maximumImagePixels));
#endif
        DataDirectory = Path.GetFullPath(dataDirectory);
        NativeLibraryPath = Path.GetFullPath(nativeLibraryPath);
        ResolutionDpi = resolutionDpi;
        MaximumImagePixels = maximumImagePixels;
    }

    /// <summary>Gets the local directory containing the English model.</summary>
    public string DataDirectory { get; }
    /// <summary>Gets the native Tesseract library path.</summary>
    public string NativeLibraryPath { get; }
    /// <summary>Gets the rasterization DPI.</summary>
    public int ResolutionDpi { get; }
    /// <summary>Gets the per-page raster pixel limit.</summary>
    public int MaximumImagePixels { get; }
}
