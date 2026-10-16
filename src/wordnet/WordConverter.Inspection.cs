using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WordNet.Internal;
using WordNet.Layout;
using WordNet.Model;

namespace WordNet;

public sealed partial class WordConverter
{
    /// <summary>Parses and checks a document without serializing conversion output.</summary>
    /// <param name="source">Readable input, consumed from its current position and left open.</param>
    /// <param name="sourceFormat">Explicit source format.</param>
    /// <param name="destinationFormat">Requested conversion destination.</param>
    /// <param name="options">The same options as conversion; null selects Strict defaults.</param>
    /// <param name="includeLayout">Run full PDF/XPS layout; invalid for reflowable destinations.</param>
    /// <param name="cancellationToken">Cooperative cancellation, with the same parser/provider limits as conversion.</param>
    /// <returns>Observed features, resources and losses; not a guarantee of lossless conversion.</returns>
    /// <remarks>Strict losses are collected. Malformation, resource limits, unsupported pairs/providers and
    /// nonrecoverable geometry still throw. PDF input requires the optional import/OCR provider.
    /// Font checks for PDF/XPS parse caller fonts; layout and output-only findings require later stages.</remarks>
    public DocumentInspection Inspect(Stream source, DocumentFormat sourceFormat, DocumentFormat destinationFormat,
        ConversionOptions? options = null, bool includeLayout = false, CancellationToken cancellationToken = default)
    {
        options ??= new ConversionOptions();
        ValidateInspection(source, sourceFormat, destinationFormat, includeLayout);
        var context = new ConversionContext(options, cancellationToken) { Inspecting = true, Destination = destinationFormat };
        context.CheckCancellation();
        using var input = new BoundedMemoryStream(options.MaximumInputBytes);
        Copy(source, input, context, options.MaximumInputBytes);
        return InspectCore(input, sourceFormat, destinationFormat, context, includeLayout);
    }

    /// <summary>Asynchronously reads bounded input and inspects it without writing conversion output.</summary>
    /// <remarks>Like Inspect, parsing/layout/provider work is synchronous on the calling continuation.
    /// Caller streams remain open. Strict losses are collected, while invalid input and limits fail.</remarks>
    public async Task<DocumentInspection> InspectAsync(Stream source, DocumentFormat sourceFormat, DocumentFormat destinationFormat,
        ConversionOptions? options = null, bool includeLayout = false, CancellationToken cancellationToken = default)
    {
        options ??= new ConversionOptions();
        ValidateInspection(source, sourceFormat, destinationFormat, includeLayout);
        var context = new ConversionContext(options, cancellationToken) { Inspecting = true, Destination = destinationFormat };
        context.CheckCancellation();
        using var input = new BoundedMemoryStream(options.MaximumInputBytes);
        await ReadInputAsync(source, input, context).ConfigureAwait(false);
        return InspectCore(input, sourceFormat, destinationFormat, context, includeLayout);
    }

    private void ValidateInspection(Stream source, DocumentFormat from, DocumentFormat to, bool layout)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(source);
#else
        if (source == null) throw new ArgumentNullException(nameof(source));
#endif
        if (!source.CanRead) throw new ArgumentException("Input stream must be readable.", nameof(source));
        if (!CanConvert(from, to)) throw new NotSupportedException("Format.Pair: This conversion direction has not been implemented.");
        if (layout && to is not DocumentFormat.Pdf and not DocumentFormat.Xps)
            throw new ArgumentException("Inspection layout requires PDF or XPS output.", nameof(layout));
    }

    private static DocumentInspection InspectCore(MemoryStream input, DocumentFormat from, DocumentFormat to, ConversionContext context, bool layout)
    {
        DocumentModel model = ReadDocument(input, from, context);
        var paragraphs = model.Paragraphs.Distinct().ToArray();
        var blocks = model.AllBlocks.SelectMany(Expand).Distinct().ToArray();
        var features = new DocumentFeatures(paragraphs.Length, blocks.OfType<TableBlock>().Count(),
            paragraphs.Sum(p => p.Runs.Count(r => r.Image != null)), paragraphs.Sum(p => p.Runs.Count(r => r.FieldInstruction != null)),
            paragraphs.Count(p => p.List != null));
        int characters = model.CharacterCount;
        context.Phase = DiagnosticPhase.Destination;
        FidelityChecks.Destination(model, to, context);
        if (to is DocumentFormat.Pdf or DocumentFormat.Xps)
        {
            context.Phase = DiagnosticPhase.Layout;
            if (layout) _ = FixedLayout.Create(model, context);
            else _ = new FontResolver(model, context, recordUsage: true);
        }
        context.CheckCancellation();
        return new DocumentInspection(input.Length, characters, features, context.Result(0), layout, context.HasStrictRejection,
            context.ArchiveEntryCount, context.DeclaredExpandedBytes, context.ImagePixels);

        static System.Collections.Generic.IEnumerable<DocumentBlock> Expand(DocumentBlock block)
        {
            yield return block;
            if (block is TableBlock table)
                foreach (DocumentBlock child in table.Rows.SelectMany(r => r).SelectMany(c => c.Content).SelectMany(Expand)) yield return child;
        }
    }
}
