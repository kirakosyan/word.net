using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet;

/// <summary>Converts Word documents to and from the supported document and ebook formats.</summary>
/// <remarks>
/// Instances have no mutable shared conversion state and may be used concurrently.
/// Format support is limited to the documented development profiles; XPS is output only.
/// Input is read from the current position. Caller streams remain open. Output is staged
/// before copying to the destination's current position, without truncating existing data.
/// Parsing/loss failures leave the destination untouched; cancellation or I/O failure during
/// the final copy may leave partial output. Parsing runs on the calling
/// continuation; async methods use asynchronous stream I/O without creating worker threads.
/// </remarks>
public sealed class WordConverter : IDocumentConverter
{
    /// <inheritdoc />
    public bool CanConvert(DocumentFormat sourceFormat, DocumentFormat destinationFormat) =>
        sourceFormat != destinationFormat &&
        ((IsWord(sourceFormat) && (IsReadable(destinationFormat) || destinationFormat is DocumentFormat.Xps or DocumentFormat.Html))
        || (IsWord(destinationFormat) && IsReadable(sourceFormat)));

    private static bool IsWord(DocumentFormat format) => format is DocumentFormat.Doc or DocumentFormat.Docx;

    /// <summary>Converts DOCX bytes to PDF, leaving all processing in memory.</summary>
    /// <param name="docx">The complete DOCX package.</param>
    /// <param name="options">Conversion policy, or null for the strict defaults.</param>
    /// <returns>The generated PDF bytes.</returns>
    public byte[] ConvertToPdf(byte[] docx, ConversionOptions? options = null) => ConvertBytes(docx, DocumentFormat.Pdf, options);

    /// <summary>Converts DOCX bytes to a self-contained UTF-8 HTML5 document.</summary>
    /// <param name="docx">The complete DOCX package.</param>
    /// <param name="options">Conversion policy, or null for the strict defaults.</param>
    /// <returns>The generated UTF-8 HTML bytes.</returns>
    public byte[] ConvertToHtml(byte[] docx, ConversionOptions? options = null) => ConvertBytes(docx, DocumentFormat.Html, options);

    private byte[] ConvertBytes(byte[] docx, DocumentFormat destination, ConversionOptions? options)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(docx);
#else
        if (docx == null) throw new ArgumentNullException(nameof(docx));
#endif
        using var source = new MemoryStream(docx, writable: false);
        using var output = new MemoryStream();
        Convert(source, output, DocumentFormat.Docx, destination, options ?? new ConversionOptions());
        return output.ToArray();
    }
    private static bool IsReadable(DocumentFormat format) => format is DocumentFormat.Doc or DocumentFormat.Docx or DocumentFormat.Text or DocumentFormat.Markdown or DocumentFormat.Epub or DocumentFormat.Pdf or DocumentFormat.Mobi or DocumentFormat.Azw3;

    /// <inheritdoc />
    public void Convert(Stream source, Stream destination, DocumentFormat sourceFormat, DocumentFormat destinationFormat) =>
        Convert(source, destination, sourceFormat, destinationFormat, new ConversionOptions());

    /// <inheritdoc />
    public async Task ConvertAsync(Stream source, Stream destination, DocumentFormat sourceFormat, DocumentFormat destinationFormat,
        CancellationToken cancellationToken = default)
    {
        await ConvertAsync(source, destination, sourceFormat, destinationFormat, new ConversionOptions(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ConversionResult Convert(Stream source, Stream destination, DocumentFormat sourceFormat, DocumentFormat destinationFormat,
        ConversionOptions options, CancellationToken cancellationToken = default)
    {
        Validate(source, destination, sourceFormat, destinationFormat, options);
        var context = new ConversionContext(options, cancellationToken);
        context.CheckCancellation();
        using var input = new BoundedMemoryStream(options.MaximumInputBytes);
        Copy(source, input, context, options.MaximumInputBytes);
        using var output = new BoundedMemoryStream(options.MaximumOutputBytes);
        Execute(input, output, sourceFormat, destinationFormat, context);
        output.Position = 0;
        Copy(output, destination, context);
        return context.Result(output.Length);
    }

    /// <inheritdoc />
    public async Task<ConversionResult> ConvertAsync(Stream source, Stream destination, DocumentFormat sourceFormat, DocumentFormat destinationFormat,
        ConversionOptions options, CancellationToken cancellationToken = default)
    {
        Validate(source, destination, sourceFormat, destinationFormat, options);
        var context = new ConversionContext(options, cancellationToken);
        context.CheckCancellation();
        using var input = new BoundedMemoryStream(options.MaximumInputBytes);
        byte[] buffer = new byte[81920];
        int count;
        while (true)
        {
            int requested = (int)Math.Min(buffer.Length, (long)options.MaximumInputBytes - input.Length + 1);
#if NET8_0_OR_GREATER
            count = await source.ReadAsync(buffer.AsMemory(0, requested), cancellationToken).ConfigureAwait(false);
#else
            count = await source.ReadAsync(buffer, 0, requested, cancellationToken).ConfigureAwait(false);
#endif
            context.CheckCancellation();
            if (count == 0)
            {
                break;
            }

            input.Write(buffer, 0, count);
        }

        using var output = new BoundedMemoryStream(options.MaximumOutputBytes);
        Execute(input, output, sourceFormat, destinationFormat, context);
        output.Position = 0;
        while ((count = output.Read(buffer, 0, buffer.Length)) != 0)
        {
            context.CheckCancellation();
#if NET8_0_OR_GREATER
            await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
#else
            await destination.WriteAsync(buffer, 0, count, cancellationToken).ConfigureAwait(false);
#endif
        }

        return context.Result(output.Length);
    }

    private void Validate(Stream source, Stream destination, DocumentFormat from, DocumentFormat to, ConversionOptions options)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);
#else
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (destination == null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }
#endif

        if (ReferenceEquals(source, destination))
        {
            throw new ArgumentException("Input and output must be different streams.", nameof(destination));
        }

        if (!source.CanRead)
        {
            throw new ArgumentException("Input stream must be readable.", nameof(source));
        }

        if (!destination.CanWrite)
        {
            throw new ArgumentException("Output stream must be writable.", nameof(destination));
        }

        if (!CanConvert(from, to))
        {
            throw new NotSupportedException("Format.Pair: This conversion direction has not been implemented.");
        }
    }

    private static void Execute(MemoryStream input, Stream output, DocumentFormat from, DocumentFormat to, ConversionContext context)
    {
        context.Destination = to;
        context.CheckCancellation();
        input.Position = 0;
        DocumentModel document;
        if (from == DocumentFormat.Docx)
        {
            document = DocxReader.Read(input, context);
        }
        else if (from == DocumentFormat.Doc) document = DocFormat.Read(input, context);
        else if (from == DocumentFormat.Epub) document = EpubFormat.Read(input, context);
        else if (from == DocumentFormat.Pdf) document = PdfImportBridge.Read(input, context);
        else if (from is DocumentFormat.Mobi or DocumentFormat.Azw3) document = KindleFormat.Read(input, from == DocumentFormat.Azw3, context);
        else
        {
            string text = TextFormat.Decode(input.ToArray(), context);
            document = from == DocumentFormat.Text ? TextFormat.Read(text, context) : MarkdownFormat.Read(text, context);
        }

        context.CheckCancellation();
        context.CheckCharacters(document.CharacterCount);
        FidelityChecks.Destination(document, to, context);
        if (to == DocumentFormat.Docx)
        {
            DocxWriter.Write(document, output, context);
        }
        else if (to == DocumentFormat.Doc) DocFormat.Write(document, output, context);
        else if (to == DocumentFormat.Epub) EpubFormat.Write(document, output, context);
        else if (to == DocumentFormat.Pdf) PdfFormat.Write(document, output, context);
        else if (to == DocumentFormat.Html) HtmlFormat.Write(document, output, context);
        else if (to == DocumentFormat.Xps) XpsFormat.Write(document, output, context);
        else if (to is DocumentFormat.Mobi or DocumentFormat.Azw3) KindleFormat.Write(document, output, to == DocumentFormat.Azw3, context);
        else
        {
            string text = to == DocumentFormat.Text ? TextFormat.Write(document, context) : MarkdownFormat.Write(document, context);
            byte[] bytes = TextFormat.Utf8.GetBytes(text);
            output.Write(bytes, 0, bytes.Length);
        }

        context.CheckCancellation();
    }

    private static void Copy(Stream source, Stream destination, ConversionContext context, long maximumReadBytes = int.MaxValue)
    {
        byte[] buffer = new byte[81920];
        int count;
        long total = 0;
        while ((count = source.Read(buffer, 0, (int)Math.Min(buffer.Length, maximumReadBytes - total + 1))) != 0)
        {
            context.CheckCancellation();
            destination.Write(buffer, 0, count);
            total += count;
        }
    }
}
