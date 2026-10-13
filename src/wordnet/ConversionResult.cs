using System.Collections.Generic;

namespace WordNet;

/// <summary>A detected conversion loss. Codes are stable; messages describe the affected feature.</summary>
public sealed class ConversionDiagnostic
{
    internal ConversionDiagnostic(string code, string message)
    {
        Code = code;
        Message = message;
    }

    /// <summary>Gets the machine-readable warning code.</summary>
    public string Code { get; }

    /// <summary>Gets the warning description, without document text.</summary>
    public string Message { get; }
}

/// <summary>Reports successfully written output and any detected losses.</summary>
public sealed class ConversionResult
{
    internal ConversionResult(long bytesWritten, IReadOnlyList<ConversionDiagnostic> diagnostics)
    {
        BytesWritten = bytesWritten;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets the number of bytes appended at the destination's initial position.</summary>
    public long BytesWritten { get; }

    /// <summary>Gets an immutable collection of detected losses, grouped by warning code.</summary>
    public IReadOnlyList<ConversionDiagnostic> Diagnostics { get; }
}
