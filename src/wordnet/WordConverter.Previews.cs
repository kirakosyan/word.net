using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Layout;
using WordNet.Model;

namespace WordNet;

public sealed partial class WordConverter
{
    /// <summary>Creates bounded per-page SVG or PNG previews after laying out the entire source.</summary>
    /// <remarks>Consumes input from its current position and leaves it open. Ranges are one-based physical pages,
    /// ascending and nonoverlapping; displayed labels and full-source page fields are retained. SVG contains
    /// glyph outlines, so text is not selectable. PNG requires optional wordnet.Ocr native assets.
    /// Configure options before concurrent calls. Native work has cooperative cancellation checkpoints.</remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Additive instance API alongside the existing stateless converter operations.")]
    public DocumentPreview RenderPages(Stream source, DocumentFormat sourceFormat, PreviewOptions? preview = null,
        ConversionOptions? options = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(source, nameof(source)); if (!source.CanRead) throw new ArgumentException("Input must be readable.", nameof(source));
        if (!IsReadable(sourceFormat)) throw new NotSupportedException("Preview.InputFormat: This format has no reader.");
        preview ??= new PreviewOptions(); options ??= new ConversionOptions();
        PreviewFormat format = preview.Format; int dpi = preview.Dpi; long pixelLimit = preview.MaximumPixels;
        ValidatePreview(preview, options);
        PageRange[] ranges = preview.Pages.ToArray();
        LayoutSnapshotOptions? layout = preview.Layout?.Snapshot();
        var context = new ConversionContext(options, cancellationToken) { Destination = DocumentFormat.Pdf };
        using var input = new BoundedMemoryStream(options.MaximumInputBytes); Copy(source, input, context, options.MaximumInputBytes);
        DocumentModel model = ReadDocument(input, sourceFormat, context);
        return RenderModel(model, format, dpi, pixelLimit, ranges, context, layout);
    }

    internal static DocumentPreview RenderModel(DocumentModel model, PreviewOptions preview, ConversionContext context)
    {
        ValidatePreview(preview, context.Options);
        return RenderModel(model, preview.Format, preview.Dpi, preview.MaximumPixels, preview.Pages.ToArray(), context, preview.Layout?.Snapshot());
    }

    private static void ValidatePreview(PreviewOptions preview, ConversionOptions options)
    {
        if (preview.Format is not PreviewFormat.Svg and not PreviewFormat.Png || preview.Dpi < 36 || preview.Dpi > 600
            || preview.MaximumPixels <= 0 || preview.Pages.Count > options.MaximumPages)
            throw new ArgumentException("Preview format, DPI, pixel limit or range count is invalid.", nameof(preview));
    }

    private static DocumentPreview RenderModel(DocumentModel model, PreviewFormat format, int dpi, long pixelLimit, PageRange[] ranges, ConversionContext context, LayoutSnapshotOptions? layout)
    {
        ConversionOptions options = context.Options; context.Phase = DiagnosticPhase.Destination;
        FidelityChecks.Destination(model, DocumentFormat.Pdf, context); context.Phase = DiagnosticPhase.Layout;
        var pages = FixedLayout.Create(model, context); int[] selected = PageSelection.Resolve(ranges, pages.Count);
        DocumentLayoutSnapshot? snapshot = layout == null ? null : CapturePreviewLayout(model, pages, selected, layout, context);
        var output = new List<PagePreview>(); long pixels = 0, bytes = 0;
        context.Phase = DiagnosticPhase.Write;
        foreach (int index in selected)
        {
            context.CheckCancellation(); FixedPage page = pages[index - 1];
            int width = checked((int)Math.Ceiling(page.Settings.Width * dpi / 72)), height = checked((int)Math.Ceiling(page.Settings.Height * dpi / 72));
            pixels = checked(pixels + (long)width * height);
            if (pixels > pixelLimit) throw new InvalidDataException("Limit.PreviewPixels: Aggregate preview dimensions exceed MaximumPixels.");
            byte[] encoded;
            if (format == PreviewFormat.Svg) encoded = SvgPreview.Write(page, context);
            else
            {
                context.ReserveImagePixels(width, height);
                using var pdf = new BoundedMemoryStream(options.MaximumOutputBytes);
                context.PreviewIntermediate = true;
                try { PdfFormat.Write(model, pdf, context, pages, new[] { new PageRange(index, index) }); }
                finally { context.PreviewIntermediate = false; }
                encoded = PreviewBridge.Png(pdf.ToArray(), width, height, context);
            }
            bytes = checked(bytes + encoded.Length); if (bytes > options.MaximumOutputBytes) throw new InvalidDataException("Limit.OutputBytes: Aggregate encoded previews exceed MaximumOutputBytes.");
            output.Add(new PagePreview(index, ListCounter.Number(page.PageNumber, page.SourceSettings.PageNumberFormat), page.Settings.Width, page.Settings.Height, width, height, format, encoded));
        }
        context.PageCount = pages.Count; context.CheckCancellation(); return new DocumentPreview(pages.Count, output, context.Result(bytes), snapshot);
    }
}
