namespace WordNet;

/// <summary>High-level container family inferred from bounded input bytes.</summary>
public enum FormatContainerFamily
{
    /// <summary>The input did not identify a supported container family.</summary>
    Unknown,
    /// <summary>An Office Open XML package.</summary>
    OfficeOpenXml,
    /// <summary>An OpenDocument package.</summary>
    OpenDocument,
    /// <summary>A compound binary file.</summary>
    CompoundFile,
    /// <summary>A PDF signature.</summary>
    Pdf,
}

/// <summary>Strength of a format identity observation, not a conversion or safety guarantee.</summary>
public enum FormatIdentityConfidence
{
    /// <summary>No supported format identity was established.</summary>
    Unknown,
    /// <summary>Only a recognizable container signature was observed.</summary>
    Signature,
    /// <summary>The bounded package identity and required local metadata were consistent.</summary>
    PackageIdentity,
}

/// <summary>Outcome of inspecting a container's identity.</summary>
public enum FormatDetectionStatus
{
    /// <summary>A supported format identity was observed.</summary>
    Recognized,
    /// <summary>The bytes did not establish a supported identity.</summary>
    Unknown,
    /// <summary>The container's identity metadata was malformed or conflicting.</summary>
    Invalid,
    /// <summary>Macro or script content is outside the admitted template profile.</summary>
    UnsupportedMacro,
    /// <summary>The container is protected or encrypted and cannot be opened by this profile.</summary>
    UnsupportedProtected,
}

/// <summary>Immutable format identity observation; it does not establish conversion or content support.</summary>
public sealed class DocumentFormatInspection
{
    internal DocumentFormatInspection(FormatDetectionStatus status, FormatContainerFamily family, FormatIdentityConfidence confidence,
        DocumentFormat format, bool? template, long bytesRead)
    { Status = status; Family = family; Confidence = confidence; Format = format; IsTemplate = template; BytesRead = bytesRead; }
    /// <summary>Gets the identity outcome.</summary>
    public FormatDetectionStatus Status { get; }
    /// <summary>Gets the observed container family.</summary>
    public FormatContainerFamily Family { get; }
    /// <summary>Gets the strength of the identity observation.</summary>
    public FormatIdentityConfidence Confidence { get; }
    /// <summary>Gets the recognized explicit format, or Unknown.</summary>
    public DocumentFormat Format { get; }
    /// <summary>Gets whether the observed identity is a template, or null when unknown.</summary>
    public bool? IsTemplate { get; }
    /// <summary>Gets the number of bounded bytes inspected from the caller's initial position.</summary>
    public long BytesRead { get; }
}
