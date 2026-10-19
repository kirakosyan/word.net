using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using WordNet.Internal;

namespace WordNet;

/// <summary>Supported text appearance, copied when supplied to an editing operation.</summary>
public sealed class TextFormatting
{
    /// <summary>Gets or sets bold emphasis.</summary>
    public bool Bold { get; set; }
    /// <summary>Gets or sets italic emphasis.</summary>
    public bool Italic { get; set; }
    /// <summary>Gets or sets underline.</summary>
    public bool Underline { get; set; }
    /// <summary>Gets or sets the requested font family, or null for the document default.</summary>
    public string? FontFamily { get; set; }
    /// <summary>Gets or sets a positive font size in points, or null for the default.</summary>
    public double? FontSize { get; set; }
    /// <summary>Gets or sets a six-digit RGB color, or null for the default.</summary>
    public string? Color { get; set; }
    internal TextFormatting Copy() => (TextFormatting)MemberwiseClone();
}

/// <summary>Caller-owned image input copied at construction; dimensions are physical points.</summary>
public sealed class TemplateImage
{
    internal byte[] Bytes { get; }
    /// <summary>Creates an inline image input. Encoded pixels are validated when admitted to a document.</summary>
    public TemplateImage(byte[] bytes, double width, double height, string alternativeText = "") : this(bytes, width, height, alternativeText, false) { }
    /// <summary>Creates an image with explicit decorative semantics. Decorative images must have empty alternative text.</summary>
    public TemplateImage(byte[] bytes, double width, double height, string alternativeText, bool decorative)
    {
        if (decorative && !string.IsNullOrEmpty(alternativeText)) throw new ArgumentException("Decorative images must have empty alternative text.", nameof(alternativeText));
        Decorative = decorative;
        Guard.NotNull(bytes, nameof(bytes));
        if (!(width > 0 && width <= 1584)) throw new ArgumentOutOfRangeException(nameof(width));
        if (!(height > 0 && height <= 1584)) throw new ArgumentOutOfRangeException(nameof(height));
        Bytes = (byte[])bytes.Clone(); Width = width; Height = height;
        AlternativeText = alternativeText ?? throw new ArgumentNullException(nameof(alternativeText));
    }
    /// <summary>Reads copied caller image bytes from the current stream position, leaving it open, with a finite byte bound.</summary>
    public static TemplateImage FromStream(Stream source, double width, double height, string alternativeText = "", int maximumBytes = 16 * 1024 * 1024, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(source, nameof(source));
        if (!source.CanRead) throw new ArgumentException("The image stream must be readable.", nameof(source));
        if (!(width > 0 && width <= 1584)) throw new ArgumentOutOfRangeException(nameof(width));
        if (!(height > 0 && height <= 1584)) throw new ArgumentOutOfRangeException(nameof(height));
        Guard.NotNull(alternativeText, nameof(alternativeText));
        Guard.Positive(maximumBytes, nameof(maximumBytes));
        using var buffer = new BoundedMemoryStream(maximumBytes); byte[] chunk = new byte[81920];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = source.Read(chunk, 0, (int)Math.Min(chunk.Length, maximumBytes - buffer.Length + 1L));
            if (count == 0) break;
            cancellationToken.ThrowIfCancellationRequested();
            if (buffer.Length + count > maximumBytes) throw new InvalidDataException("Limit.ImageBytes: Caller image input exceeds its byte bound.");
            buffer.Write(chunk, 0, count);
        }
        return new TemplateImage(buffer.ToArray(), width, height, alternativeText);
    }
    /// <summary>Gets the frame width in points.</summary>
    public double Width { get; }
    /// <summary>Gets the frame height in points.</summary>
    public double Height { get; }
    /// <summary>Gets alternative text.</summary>
    public string AlternativeText { get; }
    /// <summary>Gets whether the author explicitly marked the image as decorative.</summary>
    public bool Decorative { get; }
}

/// <summary>A detached text or inline-image value. The formatting property returns a copy.</summary>
public sealed class DocumentText
{
    private readonly TextFormatting formatting;
    /// <summary>Creates styled text. Replacement text is literal.</summary>
    public DocumentText(string text, TextFormatting? formatting = null)
    { Text = text ?? throw new ArgumentNullException(nameof(text)); this.formatting = formatting?.Copy() ?? new TextFormatting(); }
    /// <summary>Creates an inline-image value.</summary>
    public DocumentText(TemplateImage image) : this("") { Image = image ?? throw new ArgumentNullException(nameof(image)); }
    /// <summary>Gets the text.</summary>
    public string Text { get; }
    /// <summary>Gets a copy of the appearance.</summary>
    public TextFormatting Formatting => formatting.Copy();
    /// <summary>Gets the image input, or null for text.</summary>
    public TemplateImage? Image { get; }
}

/// <summary>An immutable snapshot of one paragraph, including supported run appearance.</summary>
public sealed class DocumentParagraph
{
    internal DocumentParagraph(IEnumerable<DocumentText> runs, int headingLevel) { Runs = new ReadOnlyCollection<DocumentText>(runs.ToList()); HeadingLevel = headingLevel; }
    /// <summary>Gets the copied runs in source order.</summary>
    public IReadOnlyList<DocumentText> Runs { get; }
    /// <summary>Gets the paragraph's current visible text.</summary>
    public string Text => string.Concat(Runs.Select(r => r.Text));
    /// <summary>Gets the heading level, zero for ordinary body text.</summary>
    public int HeadingLevel { get; }
}

/// <summary>An immutable bookmark range snapshot.</summary>
public sealed class DocumentBookmark
{
    internal DocumentBookmark(string name, string text) { Name = name; Text = text; }
    /// <summary>Gets the case-sensitive bookmark name.</summary>
    public string Name { get; }
    /// <summary>Gets the range's current text.</summary>
    public string Text { get; }
}

/// <summary>The style policy for appended paragraphs. Section geometry remains source-owned.</summary>
public enum CompositionStyle
{
    /// <summary>Retain resolved source paragraph and run appearance.</summary>
    KeepSource = 0,
    /// <summary>Use the first destination paragraph of the same heading level as an appearance prototype.</summary>
    UseDestination = 1,
}

/// <summary>Controls missing template values.</summary>
public enum MissingTemplateValue
{
    /// <summary>Fail the operation atomically.</summary>
    Error = 0,
    /// <summary>Retain cached content and report a loss.</summary>
    KeepCached = 1,
    /// <summary>Use empty content and report a loss.</summary>
    Empty = 2,
}

/// <summary>Bounded template execution policy. Configure before use.</summary>
public sealed class TemplateOptions
{
    /// <summary>Gets or sets scalar formatting culture. Defaults to invariant.</summary>
    public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;
    /// <summary>Gets or sets the missing-value policy.</summary>
    public MissingTemplateValue MissingValues { get; set; }
    /// <summary>Gets or sets the maximum generated rows and body-region blocks, default 10,000.</summary>
    public int MaximumRows { get; set; } = 10000;
    /// <summary>Gets or sets the maximum nested repeat depth, default eight.</summary>
    public int MaximumDepth { get; set; } = 8;
}

/// <summary>The completed mutation's change count and grouped diagnostics.</summary>
public sealed class DocumentEditResult
{
    internal DocumentEditResult(int changes, IReadOnlyList<ConversionDiagnostic> diagnostics) { Changes = changes; Diagnostics = diagnostics; }
    /// <summary>Gets the number of matches or template bindings changed.</summary>
    public int Changes { get; }
    /// <summary>Gets load and edit diagnostics without caller data values.</summary>
    public IReadOnlyList<ConversionDiagnostic> Diagnostics { get; }
}
