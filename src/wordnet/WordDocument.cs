using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet;

/// <summary>A bounded document creation, editing and template session.</summary>
/// <remarks>Operations on a session are serialized. Edits commit atomically; saves operate on a copy.
/// Streams remain open. Loading preserves the documented semantic profile with diagnostics, not arbitrary OOXML parts.</remarks>
public sealed partial class WordDocument
{
    private readonly object gate = new object();
    private bool busy;
    private readonly ConversionOptions options;
    private DocumentModel model;
    private IReadOnlyList<ConversionDiagnostic> diagnostics = Array.Empty<ConversionDiagnostic>();
    private WordDocument(DocumentModel model, ConversionOptions options) { this.model = model; this.options = options; }
    /// <summary>Creates an empty document session.</summary>
    public static WordDocument Create(ConversionOptions? options = null) => new WordDocument(new DocumentModel(), options ?? new ConversionOptions());

    /// <summary>Loads a supported readable format from the current stream position, leaving it open.</summary>
    public static WordDocument Load(Stream source, DocumentFormat format, out ConversionResult result, ConversionOptions? options = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(source, nameof(source));
        if (!source.CanRead) throw new ArgumentException("The source must be readable.", nameof(source));
        if (format == DocumentFormat.Xps || (format < DocumentFormat.Doc || format > DocumentFormat.Rtf)) throw new NotSupportedException("Editing.InputFormat: This format has no supported document reader.");
        options ??= new ConversionOptions();
        var context = new ConversionContext(options, cancellationToken) { Destination = DocumentFormat.Docx, Editing = true };
        using var input = new BoundedMemoryStream(options.MaximumInputBytes);
        Transfer(source, input, context, options.MaximumInputBytes);
        DocumentModel loaded = WordConverter.ReadDocument(input, format, context);
        Admit(loaded, context); result = context.Result(0);
        return new WordDocument(loaded, options) { diagnostics = result.Diagnostics };
    }

    /// <summary>Gets or sets supported title metadata.</summary>
    public string Title { get { lock (gate) return model.Title; } set => Edit((m, c) => { CheckText(value, c); m.Title = value; return 1; }, default); }
    /// <summary>Gets or sets supported author metadata.</summary>
    public string Author { get { lock (gate) return model.Author; } set => Edit((m, c) => { CheckText(value, c); m.Author = value; return 1; }, default); }
    /// <summary>Gets immutable snapshots of body paragraphs, including table cells and text boxes.</summary>
    public IReadOnlyList<DocumentParagraph> Paragraphs
    {
        get { lock (gate) return model.BodyParagraphs.Select(p => new DocumentParagraph(p.Runs.Where(r => r.Bookmark == null).Select(Snapshot), p.HeadingLevel)).ToList().AsReadOnly(); }
    }
    /// <summary>Gets the most recently committed session diagnostics.</summary>
    public IReadOnlyList<ConversionDiagnostic> Diagnostics { get { lock (gate) return diagnostics; } }

    /// <summary>Appends one styled paragraph to the final body section.</summary>
    public void AddParagraph(string text, TextFormatting? formatting = null, int headingLevel = 0, CancellationToken cancellationToken = default) =>
        AddParagraph(new[] { new DocumentText(text, formatting) }, headingLevel, cancellationToken);
    /// <summary>Appends styled runs as one paragraph. Heading levels range from zero through six.</summary>
    public void AddParagraph(IEnumerable<DocumentText> runs, int headingLevel = 0, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(runs, nameof(runs));
        if (headingLevel < 0 || headingLevel > 6) throw new ArgumentOutOfRangeException(nameof(headingLevel));
        _ = Edit((m, c) =>
        {
            var p = new ParagraphBlock { HeadingLevel = headingLevel, Section = m.FinalSection ?? m.Blocks.LastOrDefault()?.Section };
            AddRuns(p.Runs, runs, c); m.Blocks.Add(p); return 1;
        }, cancellationToken);
    }
    /// <summary>Appends many text paragraphs in one atomic, bounded operation without cloning the document per paragraph.</summary>
    public void AddParagraphs(IEnumerable<string> paragraphs, TextFormatting? formatting = null, int headingLevel = 0, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(paragraphs, nameof(paragraphs));
        if (headingLevel < 0 || headingLevel > 6) throw new ArgumentOutOfRangeException(nameof(headingLevel));
        _ = Edit((m, c) =>
        {
            int count = 0; long characters = m.CharacterCount;
            PageSettings? section = m.FinalSection ?? m.Blocks.LastOrDefault()?.Section;
            foreach (string text in paragraphs)
            {
                CheckText(text, c); characters = checked(characters + text.Length);
                if (characters > c.Options.MaximumCharacters || ++count > c.Options.MaximumCharacters || characters * 2 + count * 128L > c.Options.MaximumExpandedBytes)
                    throw new InvalidDataException("Limit.Expansion: Paragraph batch exceeds aggregate resource limits.");
                var paragraph = new ParagraphBlock { HeadingLevel = headingLevel, Section = section };
                AddRuns(paragraph.Runs, new[] { new DocumentText(text, formatting) }, c); m.Blocks.Add(paragraph);
            }
            return count;
        }, cancellationToken);
    }

    /// <summary>Appends a simple editable table with one paragraph per cell.</summary>
    public void AddTable(IEnumerable<IEnumerable<string>> rows, bool firstRowIsHeader = false, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(rows, nameof(rows));
        _ = Edit((m, c) =>
        {
            var table = new TableBlock { Section = m.FinalSection ?? m.Blocks.LastOrDefault()?.Section, HasHeaderRow = firstRowIsHeader };
            int cells = 0, characters = 0;
            foreach (IEnumerable<string> row in rows)
            {
                c.CheckCancellation(); var content = new List<TableCell>();
                if (row == null) throw new ArgumentException("Rows cannot be null.", nameof(rows));
                foreach (string text in row)
                {
                    CheckText(text, c); characters = checked(characters + text.Length); c.CheckCharacters(characters);
                    if (++cells > c.Options.MaximumCharacters) throw new InvalidDataException("Limit.Structure: Too many cells.");
                    if (cells * 128L + characters * 2L > c.Options.MaximumExpandedBytes) throw new InvalidDataException("Limit.ExpandedBytes: Table expansion exceeds aggregate resource limits.");
                    var p = new ParagraphBlock(); p.Runs.Add(new TextRun(text)); content.Add(new TableCell(p));
                }
                if (content.Count == 0) throw new ArgumentException("Table rows must contain cells.", nameof(rows));
                table.Rows.Add(content);
            }
            if (table.Rows.Count == 0) throw new ArgumentException("A table must contain rows.", nameof(rows));
            m.Blocks.Add(table); return 1;
        }, cancellationToken);
    }
    /// <summary>Appends a caller-supplied inline image as a paragraph.</summary>
    public void AddImage(TemplateImage image, CancellationToken cancellationToken = default) => AddParagraph(new[] { new DocumentText(image) }, cancellationToken: cancellationToken);

    /// <summary>Replaces the runs in a body paragraph selected from <see cref="Paragraphs"/>.</summary>
    public void ReplaceParagraph(int index, IEnumerable<DocumentText> runs, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(runs, nameof(runs));
        _ = Edit((m, c) =>
        {
            ParagraphBlock[] paragraphs = m.BodyParagraphs.ToArray();
            if (index < 0 || index >= paragraphs.Length) throw new ArgumentOutOfRangeException(nameof(index));
            if (paragraphs[index].Runs.Any(r => r.Bookmark != null || r.FieldInstruction != null || r.Note != null || r.Box != null || r.Control != null)) throw new NotSupportedException("Editing.ProtectedParagraph: Use range-aware editing for structured paragraphs.");
            paragraphs[index].Runs.Clear(); AddRuns(paragraphs[index].Runs, runs, c); return 1;
        }, cancellationToken);
    }

    /// <summary>Saves supported output with diagnostics. Validation failures leave the destination untouched.</summary>
    public ConversionResult Save(Stream destination, DocumentFormat format, ConversionOptions? saveOptions = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(destination, nameof(destination));
        if (!destination.CanWrite) throw new ArgumentException("The destination must be writable.", nameof(destination));
        if ((format < DocumentFormat.Doc || format > DocumentFormat.Rtf)) throw new ArgumentOutOfRangeException(nameof(format));
        lock (gate)
        {
            using var operation = BeginOperation();
            var context = Context(saveOptions ?? options, cancellationToken); context.Destination = format;
            DocumentModel copy = new DocumentCloner(context).Copy(model); Admit(copy, context);
            context.Phase = DiagnosticPhase.Destination; FidelityChecks.Destination(copy, format, context);
            context.Phase = DiagnosticPhase.Write;
            using var output = new BoundedMemoryStream(context.Options.MaximumOutputBytes);
            Write(copy, output, format, context); output.Position = 0;
            Transfer(output, destination, context, context.Options.MaximumOutputBytes);
            return context.Result(output.Length);
        }
    }

    private DocumentEditResult Edit(Func<DocumentModel, ConversionContext, int> edit, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            using var operation = BeginOperation();
            ConversionContext context = Context(options, cancellationToken);
            DocumentModel copy = new DocumentCloner(context).Copy(model);
            int changes = edit(copy, context); NormalizeNotes(copy); Admit(copy, context); context.CheckCancellation();
            ConversionResult result = context.Result(0); model = copy; diagnostics = result.Diagnostics;
            return new DocumentEditResult(changes, diagnostics);
        }
    }
    private static void NormalizeNotes(DocumentModel model)
    {
        var seen = new HashSet<DocumentNote>();
        var counters = new Dictionary<(bool Endnote, PageSettings? Section), int>();
        foreach (DocumentBlock block in model.Blocks)
            foreach (DocumentNote note in DocumentModel.Expand(new[] { block }).OfType<ParagraphBlock>().SelectMany(p => p.Runs).Where(r => r.Note != null).Select(r => r.Note!))
            {
                if (!seen.Add(note)) continue;
                var key = (note.Endnote, note.Policy.Restart == "eachSect" ? block.Section : null);
                int number = counters.TryGetValue(key, out int previous) ? checked(previous + 1) : note.Policy.Start;
                counters[key] = number; note.Number = number; note.Format = note.Policy.Format;
            }
    }
    private OperationScope BeginOperation()
    {
        if (busy) throw new InvalidOperationException("Editing.ReentrantOperation: A document operation cannot reenter its own session.");
        busy = true; return new OperationScope(this);
    }
    private sealed class OperationScope : IDisposable
    {
        private readonly WordDocument owner;
        internal OperationScope(WordDocument owner) { this.owner = owner; }
        public void Dispose() => owner.busy = false;
    }
    private ConversionContext Context(ConversionOptions policy, CancellationToken token)
    {
        var context = new ConversionContext(policy, token) { Editing = true };
        context.CheckCancellation();
        foreach (ConversionDiagnostic d in diagnostics) context.Inherit(d);
        return context;
    }
    private static void Admit(DocumentModel model, ConversionContext context)
    {
        context.CheckCancellation(); context.CheckCharacters(checked(model.CharacterCount + model.Title.Length + model.Author.Length));
        var images = new ConversionContext(context.Options, context.CancellationToken);
        long bytes = model.CharacterCount * 2L; int nodes = 0;
        foreach (ParagraphBlock p in model.Paragraphs)
        {
            context.CheckCancellation(); nodes = checked(nodes + p.Runs.Count + 1);
            foreach (TextRun run in p.Runs) if (run.Image != null) { bytes = checked(bytes + run.Image.Bytes.Length); images.ReserveImagePixels(run.Image.PixelsWide, run.Image.PixelsHigh); }
        }
        if (bytes + nodes * 64L > context.Options.MaximumExpandedBytes) throw new InvalidDataException("Limit.ExpandedBytes: Edited document exceeds aggregate resource limits.");
        foreach (TableBlock table in model.Tables) TableValidation.Validate(table, context);
    }
    private static void AddRuns(List<TextRun> destination, IEnumerable<DocumentText> runs, ConversionContext context)
    {
        int characters = 0, count = 0;
        foreach (DocumentText value in runs)
        {
            context.CheckCancellation();
            if (value == null) throw new ArgumentException("Runs cannot be null.", nameof(runs));
            CheckText(value.Text, context); characters = checked(characters + value.Text.Length); context.CheckCharacters(characters);
            if (++count > context.Options.MaximumCharacters) throw new InvalidDataException("Limit.Structure: Too many runs.");
            if (count * 64L + characters * 2L > context.Options.MaximumExpandedBytes) throw new InvalidDataException("Limit.ExpandedBytes: Run expansion exceeds aggregate resource limits.");
            TextFormatting f = value.Formatting;
            if (f.FontFamily != null) { CheckText(f.FontFamily, context); if (f.FontFamily.Length > 256) throw new ArgumentException("Font-family names exceed the supported bound.", nameof(runs)); }
            if (f.FontSize.HasValue && !(f.FontSize > 0 && f.FontSize <= 1584)) throw new ArgumentOutOfRangeException(nameof(runs), "Invalid font size.");
            if (f.Color != null && (f.Color.Length != 6 || f.Color.Any(c => !Uri.IsHexDigit(c)))) throw new ArgumentException("Colors must be six-digit RGB.", nameof(runs));
            if (value.Image != null)
            {
                CheckText(value.Image.AlternativeText, context);
                if (value.Image.Bytes.Length > context.Options.MaximumExpandedBytes) throw new InvalidDataException("Limit.ExpandedBytes: Image data exceeds the configured bound.");
            }
            DocumentImage? image = value.Image == null ? null : DocumentImage.Read(value.Image.Bytes, value.Image.Width, value.Image.Height, value.Image.AlternativeText, context);
            if (image != null) image.Decorative = value.Image!.Decorative;
            destination.Add(new TextRun(value.Text, f.Bold, f.Italic, fontSize: f.FontSize, color: f.Color?.ToUpperInvariant(), image: image,
                appearance: new RunAppearance { FontFamily = f.FontFamily, Underline = f.Underline }));
        }
    }
    private static DocumentText Snapshot(TextRun r) => r.Image == null ? new DocumentText(r.Text, new TextFormatting
    {
        Bold = r.Bold,
        Italic = r.Italic,
        FontSize = r.FontSize,
        Color = r.Color,
        FontFamily = r.Appearance.FontFamily,
        Underline = r.Appearance.Underline
    }) : new DocumentText(new TemplateImage(r.Image.Bytes, r.Image.FrameWidth, r.Image.FrameHeight, r.Image.AlternativeText, r.Image.Decorative));
    private static void CheckText(string text, ConversionContext context)
    {
        Guard.NotNull(text, nameof(text));
        context.CheckCancellation(); context.CheckCharacters(text.Length);
        try { _ = System.Xml.XmlConvert.VerifyXmlChars(text.Replace('\f', '\n').Replace('\v', '\n')); }
        catch (System.Xml.XmlException e) { throw new ArgumentException("Text contains unsupported XML characters.", nameof(text), e); }
        for (int i = 0; i < text.Length; i++)
            if (char.IsHighSurrogate(text[i])) { if (++i == text.Length || !char.IsLowSurrogate(text[i])) throw new ArgumentException("Text contains an incomplete Unicode character.", nameof(text)); }
            else if (char.IsLowSurrogate(text[i])) throw new ArgumentException("Text contains an incomplete Unicode character.", nameof(text));
    }
    private static void Transfer(Stream source, Stream destination, ConversionContext context, int maximum)
    {
        byte[] buffer = new byte[81920]; long total = 0;
        while (true)
        {
            context.CheckCancellation(); int count = source.Read(buffer, 0, (int)Math.Min(buffer.Length, maximum - total + 1L));
            if (count == 0) break;
            total += count; if (total > maximum) throw new InvalidDataException("Limit.InputBytes: Stream exceeds the configured limit.");
            destination.Write(buffer, 0, count);
        }
    }
    private static void Write(DocumentModel m, Stream output, DocumentFormat format, ConversionContext c)
    {
        if (format == DocumentFormat.Docx) DocxWriter.Write(m, output, c);
        else if (format == DocumentFormat.Doc) DocFormat.Write(m, output, c);
        else if (format == DocumentFormat.Pdf) PdfFormat.Write(m, output, c);
        else if (format == DocumentFormat.Html) HtmlFormat.Write(m, output, c);
        else if (format == DocumentFormat.Rtf) RtfFormat.Write(m, output, c);
        else if (format == DocumentFormat.Xps) XpsFormat.Write(m, output, c);
        else if (format == DocumentFormat.Epub) EpubFormat.Write(m, output, c);
        else if (format is DocumentFormat.Mobi or DocumentFormat.Azw3) KindleFormat.Write(m, output, format == DocumentFormat.Azw3, c);
        else { byte[] bytes = TextFormat.Utf8.GetBytes(format == DocumentFormat.Text ? TextFormat.Write(m, c) : MarkdownFormat.Write(m, c)); output.Write(bytes, 0, bytes.Length); }
    }
}
