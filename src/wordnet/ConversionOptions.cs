using System;

namespace WordNet;

/// <summary>Determines how conversion handles features that cannot be preserved.</summary>
public enum FidelityMode
{
    /// <summary>Reject any detected loss before writing output.</summary>
    Strict = 0,

    /// <summary>Convert supported content and return diagnostics for detected losses.</summary>
    BestEffort = 1,
}

/// <summary>Immutable policy and resource bounds for one conversion.</summary>
public sealed class ConversionOptions
{
    /// <summary>Creates a conversion policy with finite input, output and parsing bounds.</summary>
    /// <param name="fidelityMode">Whether detected losses fail or become diagnostics.</param>
    /// <param name="maximumInputBytes">Maximum buffered input bytes; one extra byte may be read to detect overflow.</param>
    /// <param name="maximumOutputBytes">Maximum staged output bytes.</param>
    /// <param name="maximumExpandedBytes">Maximum aggregate ZIP expansion, decoded ebook bytes, or individual DOC stream bytes.</param>
    /// <param name="maximumArchiveEntries">Maximum entries in a ZIP container.</param>
    /// <param name="maximumCharacters">Maximum input text or XML characters.</param>
    /// <param name="maximumNestingDepth">Maximum XML, HTML or Markdown nesting depth.</param>
    /// <param name="maximumPages">Maximum imported or generated pages.</param>
    /// <param name="englishOcr">Explicit local English OCR configuration for scanned and mixed PDF pages.</param>
    public ConversionOptions(
        FidelityMode fidelityMode = FidelityMode.Strict,
        int maximumInputBytes = 16 * 1024 * 1024,
        int maximumOutputBytes = 32 * 1024 * 1024,
        int maximumExpandedBytes = 64 * 1024 * 1024,
        int maximumArchiveEntries = 1024,
        int maximumCharacters = 4 * 1024 * 1024,
        int maximumNestingDepth = 64,
        int maximumPages = 1000,
        EnglishOcrOptions? englishOcr = null)
    {
        if (fidelityMode != FidelityMode.Strict && fidelityMode != FidelityMode.BestEffort)
        {
            throw new ArgumentOutOfRangeException(nameof(fidelityMode));
        }

        ValidatePositive(maximumInputBytes, nameof(maximumInputBytes));
        ValidatePositive(maximumOutputBytes, nameof(maximumOutputBytes));
        ValidatePositive(maximumExpandedBytes, nameof(maximumExpandedBytes));
        ValidatePositive(maximumArchiveEntries, nameof(maximumArchiveEntries));
        ValidatePositive(maximumCharacters, nameof(maximumCharacters));
        ValidatePositive(maximumNestingDepth, nameof(maximumNestingDepth));
        ValidatePositive(maximumPages, nameof(maximumPages));
        FidelityMode = fidelityMode;
        MaximumInputBytes = maximumInputBytes;
        MaximumOutputBytes = maximumOutputBytes;
        MaximumExpandedBytes = maximumExpandedBytes;
        MaximumArchiveEntries = maximumArchiveEntries;
        MaximumCharacters = maximumCharacters;
        MaximumNestingDepth = maximumNestingDepth;
        MaximumPages = maximumPages;
        EnglishOcr = englishOcr;
    }

    /// <summary>Gets the loss-handling policy.</summary>
    public FidelityMode FidelityMode { get; }

    /// <summary>Gets the input byte limit.</summary>
    public int MaximumInputBytes { get; }

    /// <summary>Gets the staged output byte limit.</summary>
    public int MaximumOutputBytes { get; }

    /// <summary>Gets the container expansion or individual DOC stream limit.</summary>
    public int MaximumExpandedBytes { get; }

    /// <summary>Gets the ZIP entry-count limit.</summary>
    public int MaximumArchiveEntries { get; }

    /// <summary>Gets the text/XML character limit.</summary>
    public int MaximumCharacters { get; }

    /// <summary>Gets the parser nesting-depth limit.</summary>
    public int MaximumNestingDepth { get; }

    /// <summary>Gets the imported/generated page limit.</summary>
    public int MaximumPages { get; }

    /// <summary>Gets explicitly provisioned English printed-text OCR options, or null to reject scanned input.</summary>
    public EnglishOcrOptions? EnglishOcr { get; }

    private static void ValidatePositive(int value, string name)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, "Resource limits must be positive.");
        }
    }
}
