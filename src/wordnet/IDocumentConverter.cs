using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WordNet;

/// <summary>Defines stream-based document conversion and capability discovery.</summary>
/// <remarks>
/// Implementations report only available format pairs and leave caller-owned streams open.
/// A supported pair does not guarantee that every document feature is supported.
/// </remarks>
public interface IDocumentConverter
{
    /// <summary>Reports whether this implementation can perform the specified conversion.</summary>
    /// <param name="sourceFormat">The source document format.</param>
    /// <param name="destinationFormat">The destination document format.</param>
    /// <returns>Whether the conversion is available in this implementation and environment.</returns>
    bool CanConvert(DocumentFormat sourceFormat, DocumentFormat destinationFormat);

    /// <summary>Converts a document synchronously using caller-owned streams.</summary>
    /// <param name="source">A readable stream positioned at the start of the source document.</param>
    /// <param name="destination">A writable stream that receives the converted document.</param>
    /// <param name="sourceFormat">The explicit source format.</param>
    /// <param name="destinationFormat">The explicit destination format.</param>
    void Convert(
        Stream source,
        Stream destination,
        DocumentFormat sourceFormat,
        DocumentFormat destinationFormat);

    /// <summary>Converts a document asynchronously using caller-owned streams.</summary>
    /// <param name="source">A readable stream positioned at the start of the source document.</param>
    /// <param name="destination">A writable stream that receives the converted document.</param>
    /// <param name="sourceFormat">The explicit source format.</param>
    /// <param name="destinationFormat">The explicit destination format.</param>
    /// <param name="cancellationToken">A token for cancelling the operation.</param>
    /// <returns>A task representing completion of the conversion.</returns>
    Task ConvertAsync(
        Stream source,
        Stream destination,
        DocumentFormat sourceFormat,
        DocumentFormat destinationFormat,
        CancellationToken cancellationToken = default);

    /// <summary>Converts supported content using explicit fidelity and resource policies.</summary>
    /// <param name="source">Input, read from its current position.</param>
    /// <param name="destination">Output, written at its current position without truncation.</param>
    /// <param name="sourceFormat">Explicit input format.</param>
    /// <param name="destinationFormat">Explicit output format.</param>
    /// <param name="options">Immutable conversion policy.</param>
    /// <param name="cancellationToken">Cancellation requested by the caller.</param>
    /// <returns>Written byte count and detected losses.</returns>
    ConversionResult Convert(Stream source, Stream destination, DocumentFormat sourceFormat, DocumentFormat destinationFormat,
        ConversionOptions options, CancellationToken cancellationToken = default);

    /// <summary>Converts using asynchronous stream I/O and explicit policies.</summary>
    /// <param name="source">Input, read from its current position.</param>
    /// <param name="destination">Output, written at its current position without truncation.</param>
    /// <param name="sourceFormat">Explicit input format.</param>
    /// <param name="destinationFormat">Explicit output format.</param>
    /// <param name="options">Immutable conversion policy.</param>
    /// <param name="cancellationToken">Cancellation requested by the caller.</param>
    /// <returns>Written byte count and detected losses.</returns>
    Task<ConversionResult> ConvertAsync(Stream source, Stream destination, DocumentFormat sourceFormat, DocumentFormat destinationFormat,
        ConversionOptions options, CancellationToken cancellationToken = default);
}
