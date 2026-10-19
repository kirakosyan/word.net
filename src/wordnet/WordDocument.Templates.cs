using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet;

public sealed partial class WordDocument
{
    /// <summary>Snapshots caller dictionaries/public properties and executes the bounded template grammar.</summary>
    public DocumentEditResult Merge(object data, TemplateOptions? templateOptions = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(data, nameof(data)); TemplateOptions policy = TemplatePolicy(templateOptions);
        return MergeData(context => TemplateData.Snapshot(data, context, policy.MaximumRows), policy, cancellationToken);
    }
    /// <summary>Executes a template with bounded, duplicate-key-rejecting JSON object data.</summary>
    public DocumentEditResult MergeJson(string json, TemplateOptions? templateOptions = null, CancellationToken cancellationToken = default)
    {
        TemplateOptions policy = TemplatePolicy(templateOptions);
        return MergeData(context => TemplateData.Snapshot(TemplateData.Json(json, context), context, policy.MaximumRows), policy, cancellationToken);
    }
    private static TemplateOptions TemplatePolicy(TemplateOptions? source)
    {
        source ??= new TemplateOptions();
        Guard.NotNull(source.Culture, nameof(source));
        if (source.MaximumDepth < 1 || source.MaximumDepth > 64 || source.MaximumRows < 1 || source.MaximumRows > 100000
            || source.MissingValues is not MissingTemplateValue.Error and not MissingTemplateValue.Empty and not MissingTemplateValue.KeepCached) throw new ArgumentOutOfRangeException(nameof(source));
        return new TemplateOptions { Culture = CultureInfo.ReadOnly((CultureInfo)source.Culture.Clone()), MissingValues = source.MissingValues, MaximumDepth = source.MaximumDepth, MaximumRows = source.MaximumRows };
    }
    private DocumentEditResult MergeData(Func<ConversionContext, Dictionary<string, object?>> data, TemplateOptions policy, CancellationToken token) => Edit((m, c) =>
    {
        if (c.Result(0).Diagnostics.Any(d => d.Code is "Template.NestedBlockControls" or "Template.NestedInlineControls" or "Template.NestedRowControls" or "Template.TableControl"))
            throw new NotSupportedException("Template.ControlStructure: Flattened nested or unsupported table controls cannot be executed safely.");
        Dictionary<string, object?> root = data(c);
        var processedNotes = new HashSet<DocumentNote>();
        int changes = 0, rows = 0, identity = 0;
        long expansion = m.CharacterCount;
        long structuralExpansion = 0;
        string prefix = UniquePrefix(m) + "t";
        Blocks(m.Blocks, root, 0, true);
        foreach (List<DocumentBlock> story in m.Blocks.Select(b => b.Section).Concat(new[] { m.FinalSection }).Where(s => s != null).Distinct().SelectMany(s => s!.Stories.Values).Distinct()) Blocks(story, root, 0);
        var referenced = new HashSet<DocumentNote>(DocumentModel.Expand(m.Blocks).OfType<ParagraphBlock>().SelectMany(p => p.Runs).Where(r => r.Note != null).Select(r => r.Note!));
        m.Notes.RemoveAll(n => !referenced.Contains(n));
        foreach (DocumentNote note in m.Notes.ToArray()) ProcessNote(note, root, 0);
        ValidateRanges(m);
        return changes;

        void Blocks(List<DocumentBlock> blocks, object? scope, int depth, bool body = false)
        {
            for (int i = 0; i < blocks.Count; i++)
            {
                c.CheckCancellation(); DocumentBlock block = blocks[i];
                if (block.Control != null)
                {
                    if (!block.Control.Executable) throw new NotSupportedException("Template.ControlStructure: Invalid region boundaries cannot be executed.");
                    (string kind, string path) = Tag(block.Control.Tag);
                    if (kind == "repeat")
                    {
                        if (!body) throw new NotSupportedException("Template.RepeatRegion: Paragraph regions are supported only in the body.");
                        int end = i + 1;
                        while (end < blocks.Count && ReferenceEquals(blocks[end].Control, block.Control)) end++;
                        if (blocks.Skip(end).Any(b => ReferenceEquals(b.Control, block.Control))
                            || blocks.Skip(i).Take(end - i).Any(b => b is not ParagraphBlock || !ReferenceEquals(b.Section, block.Section)))
                            throw new NotSupportedException("Template.RepeatRegion: A body repeat requires contiguous paragraphs in one section.");
                        if (depth >= Math.Min(policy.MaximumDepth, c.Options.MaximumNestingDepth)) throw new InvalidDataException("Limit.TemplateDepth: Repeated regions exceed the nesting limit.");
                        if (!Value(path, scope, out object? sequence)) { i = end - 1; continue; }
                        if (sequence != null && sequence is not List<object?>) throw new InvalidDataException("Template.Collection: A repeat requires an array value.");
                        var source = new DocumentModel(); source.Blocks.AddRange(blocks.Skip(i).Take(end - i));
                        TextRun[] content = source.Paragraphs.SelectMany(p => p.Runs).ToArray();
                        if (content.Any(r => r.Box != null)) throw new NotSupportedException("Template.RepeatRegion: Positioned objects cannot be repeated in body regions.");
                        var starts = new HashSet<string>(content.Where(r => r.Bookmark != null && !Starts(r.Bookmark, '/')).Select(r => r.Bookmark!), StringComparer.Ordinal);
                        var ends = new HashSet<string>(content.Where(r => r.Bookmark != null && Starts(r.Bookmark, '/')).Select(r => r.Bookmark!.Substring(1)), StringComparer.Ordinal);
                        if (!starts.SetEquals(ends)) throw new NotSupportedException("Template.RepeatBookmarks: Bookmark ranges cannot cross a repeat-region boundary.");
                        source.Notes.AddRange(content.Where(r => r.Note != null).Select(r => r.Note!).Distinct());
                        var generated = new List<DocumentBlock>();
                        foreach (object? item in sequence as List<object?> ?? new List<object?>())
                        {
                            c.CheckCancellation();
                            rows = checked(rows + source.Blocks.Count);
                            if (rows > policy.MaximumRows) throw new InvalidDataException("Limit.TemplateRows: Generated blocks and rows exceed the configured limit.");
                            expansion += source.CharacterCount;
                            structuralExpansion += source.Paragraphs.Sum(p => (p.Runs.Count + 1) * 64L)
                                + source.Paragraphs.SelectMany(p => p.Runs).Where(r => r.Image != null).Sum(r => (long)r.Image!.Bytes.Length);
                            LimitExpansion();
                            DocumentModel clone = new DocumentCloner(c).Copy(source);
                            foreach (DocumentBlock child in clone.Blocks) { child.Control = null; child.Section = block.Section; }
                            Remap(clone, prefix + (++identity).ToString(CultureInfo.InvariantCulture) + "_", m.Notes.Select(n => n.PartId).DefaultIfEmpty().Max());
                            m.Notes.AddRange(clone.Notes); Blocks(clone.Blocks, item, depth + 1, true);
                            foreach (DocumentNote note in clone.Notes) ProcessNote(note, item, depth + 1);
                            generated.AddRange(clone.Blocks);
                        }
                        blocks.RemoveRange(i, end - i); blocks.InsertRange(i, generated); i += generated.Count - 1; changes++; continue;
                    }
                    if (!Value(path, scope, out object? value)) continue;
                    if (kind == "if")
                    {
                        changes++;
                        if (!Condition(path, value)) { blocks.RemoveAt(i--); continue; }
                        block.Control = null;
                    }
                    else if (kind is "merge" or "image")
                    {
                        if (block is not ParagraphBlock scalar || blocks.Count(b => ReferenceEquals(b.Control, block.Control)) != 1
                            || scalar.Runs.Any(r => r.Bookmark != null || r.Note != null || r.Box != null || r.Control != null)) throw new NotSupportedException("Template.ControlStructure: Scalar block controls require one paragraph without embedded ranges.");
                        TextRun prototype = scalar.Runs.FirstOrDefault() ?? new TextRun("");
                        scalar.Runs.Clear(); scalar.Runs.Add(Bind(prototype, kind, value, null)); block.Control = null; changes++;
                    }
                    else throw new NotSupportedException("Template.ControlStructure: Repeats require a single table row.");
                }
                if (block is ParagraphBlock paragraph) Paragraph(paragraph, scope, depth);
                else { var table = (TableBlock)block; Table(table, scope, depth); if (table.Rows.Count == 0) blocks.RemoveAt(i--); }
            }
        }
        void Table(TableBlock table, object? scope, int depth)
        {
            var output = new List<List<TableCell>>(); var heights = new Dictionary<int, (double Height, bool Exact)>();
            var lefts = new Dictionary<int, double>(); var nonsplitting = new HashSet<int>(); var controls = new Dictionary<int, TemplateControl>(); int headers = 0;
            for (int index = 0; index < table.Rows.Count; index++)
            {
                c.CheckCancellation(); List<TableCell> row = table.Rows[index];
                if (table.RowControls.TryGetValue(index, out TemplateControl? control))
                {
                    var tag = Tag(control.Tag);
                    if (tag.Kind != "repeat") throw new NotSupportedException("Template.RowControl: A row control must use repeat:Path.");
                    if (table.RowControls.Values.Count(value => ReferenceEquals(value, control)) != 1) throw new NotSupportedException("Template.RepeatRegion: A repeating control must wrap exactly one table row.");
                    if (depth >= Math.Min(policy.MaximumDepth, c.Options.MaximumNestingDepth)) throw new InvalidDataException("Limit.TemplateDepth: Repeated regions exceed the nesting limit.");
                    if (!Value(tag.Path, scope, out object? value)) { controls.Add(output.Count, control); Add(row, scope, depth, false); continue; }
                    if (value != null && value is not List<object?>) throw new InvalidDataException("Template.Collection: A repeat requires an array value.");
                    if (row.Any(cell => cell.Merge != VerticalMerge.None)) throw new NotSupportedException("Template.VerticalMergeRegion: Vertically merged repeat rows are unsupported.");
                    var source = new DocumentModel(); var holder = new TableBlock(); holder.Rows.Add(row); source.Blocks.Add(holder);
                    TextRun[] content = source.Paragraphs.SelectMany(p => p.Runs).ToArray();
                    var starts = new HashSet<string>(content.Where(r => r.Bookmark != null && !Starts(r.Bookmark, '/')).Select(r => r.Bookmark!), StringComparer.Ordinal);
                    var ends = new HashSet<string>(content.Where(r => r.Bookmark != null && Starts(r.Bookmark, '/')).Select(r => r.Bookmark!.Substring(1)), StringComparer.Ordinal);
                    if (!starts.SetEquals(ends)) throw new NotSupportedException("Template.RepeatBookmarks: Bookmark ranges cannot cross a repeat-row boundary.");
                    source.Notes.AddRange(content.Where(r => r.Note != null).Select(r => r.Note!).Distinct());
                    foreach (object? item in value as List<object?> ?? new List<object?>())
                    {
                        c.CheckCancellation(); if (++rows > policy.MaximumRows) throw new InvalidDataException("Limit.TemplateRows: Generated rows exceed the configured limit.");
                        expansion += source.CharacterCount;
                        structuralExpansion += source.Paragraphs.Sum(p => (p.Runs.Count + 1) * 64L) + source.Paragraphs.SelectMany(p => p.Runs).Where(r => r.Image != null).Sum(r => (long)r.Image!.Bytes.Length);
                        LimitExpansion();
                        DocumentModel clone = new DocumentCloner(c).Copy(source);
                        Remap(clone, prefix + (++identity).ToString(CultureInfo.InvariantCulture) + "_", m.Notes.Select(n => n.PartId).DefaultIfEmpty().Max(), isolateCounters: false, isolateExternalReferences: false);
                        m.Notes.AddRange(clone.Notes); Add(((TableBlock)clone.Blocks[0]).Rows[0], item, depth + 1);
                        foreach (DocumentNote note in clone.Notes) ProcessNote(note, item, depth + 1);
                    }
                    changes++;
                }
                else Add(row, scope, depth);

                void Add(List<TableCell> cells, object? item, int currentDepth, bool process = true)
                {
                    int target = output.Count;
                    if (index < table.HeaderRowCount) headers++;
                    if (table.RowHeights.TryGetValue(index, out var height)) heights.Add(target, height);
                    if (table.RowLefts.TryGetValue(index, out double left)) lefts.Add(target, left);
                    if (table.NonSplittingRows.Contains(index)) nonsplitting.Add(target);
                    foreach (TableCell cell in cells)
                    {
                        var blocks = cell.Blocks.ToList(); if (process) Blocks(blocks, item, currentDepth);
                        if (blocks.Count == 0) blocks.Add(new ParagraphBlock());
                        cell.Content.Clear(); cell.Content.AddRange(blocks); cell.Paragraphs.Clear(); cell.Paragraphs.AddRange(DocumentModel.CellParagraphs(blocks));
                    }
                    output.Add(cells);
                }
            }
            table.Rows.Clear(); table.Rows.AddRange(output); table.HeaderRowCount = headers;
            table.RowHeights.Clear(); foreach (var item in heights) table.RowHeights.Add(item.Key, item.Value);
            table.RowLefts.Clear(); foreach (var item in lefts) table.RowLefts.Add(item.Key, item.Value);
            table.NonSplittingRows.Clear(); table.NonSplittingRows.UnionWith(nonsplitting); table.RowControls.Clear(); foreach (var item in controls) table.RowControls.Add(item.Key, item.Value);
        }
        void ProcessNote(DocumentNote note, object? scope, int depth)
        {
            if (!processedNotes.Add(note)) return;
            Blocks(note.Blocks, scope, depth);
            if (note.Blocks.Count == 0) note.Blocks.Add(new ParagraphBlock());
        }
        void Paragraph(ParagraphBlock paragraph, object? scope, int depth)
        {
            var output = new List<TextRun>();
            for (int i = 0; i < paragraph.Runs.Count; i++)
            {
                c.CheckCancellation(); TextRun run = paragraph.Runs[i];
                if (run.Control != null)
                {
                    var group = new List<TextRun> { run };
                    while (i + 1 < paragraph.Runs.Count && ReferenceEquals(paragraph.Runs[i + 1].Control, run.Control)) group.Add(paragraph.Runs[++i]);
                    var tag = Tag(run.Control.Tag);
                    if (!Value(tag.Path, scope, out object? value)) { output.AddRange(group); continue; }
                    if (tag.Kind == "if")
                    {
                        if (Condition(tag.Path, value))
                        {
                            var child = new ParagraphBlock(); child.Runs.AddRange(group.Select(r => Clean(r, r.Text, r.FieldInstruction)));
                            Paragraph(child, scope, depth); output.AddRange(child.Runs);
                        }
                    }
                    else if (tag.Kind is "merge" or "image")
                    {
                        if (group.Any(r => r.Bookmark != null || r.Note != null || r.Box != null)) throw new NotSupportedException("Template.ControlStructure: Scalar controls cannot overwrite embedded ranges.");
                        output.Add(Bind(run, tag.Kind, value, null));
                    }
                    else throw new NotSupportedException("Template.ControlStructure: Inline repeats are unsupported.");
                    changes++; continue;
                }
                if (run.Box != null) { Blocks(run.Box.Blocks, scope, depth); if (run.Box.Blocks.Count == 0) run.Box.Blocks.Add(new ParagraphBlock()); }
                if (run.FieldInstruction != null && DocumentFields.Kind(run.FieldInstruction) == "MERGEFIELD")
                {
                    var field = MergeField(run.FieldInstruction);
                    if (!Value(field.Path, scope, out object? value)) { output.Add(run); continue; }
                    output.Add(Bind(run, "merge", value, field.Format, field.Switch)); changes++;
                }
                else output.Add(run);
            }
            paragraph.Runs.Clear(); paragraph.Runs.AddRange(output);
        }
        TextRun Bind(TextRun prototype, string kind, object? value, string? format, string? formatKind = null)
        {
            if (kind == "image")
            {
                if (value == null) return BoundText(prototype, "");
                if (value is not TemplateImage image) throw new InvalidDataException("Template.Image: An image placeholder requires caller image bytes and dimensions.");
                if (image.Bytes.Length > c.Options.MaximumExpandedBytes) throw new InvalidDataException("Limit.ExpandedBytes: Template image exceeds the configured bound.");
                CheckText(image.AlternativeText, c);
                DocumentImage admitted = DocumentImage.Read(image.Bytes, image.Width, image.Height, image.AlternativeText, c); admitted.Decorative = image.Decorative;
                return new TextRun("", image: admitted);
            }
            if (value is Dictionary<string, object?> or List<object?> or TemplateImage) throw new InvalidDataException("Template.Scalar: This binding requires a supported scalar value.");
            if (value != null && (formatKind == "\\@" && value is not DateTime and not DateTimeOffset
                || formatKind == "\\#" && value is not byte and not sbyte and not short and not ushort and not int and not uint and not long and not ulong and not decimal and not float and not double))
                throw new InvalidDataException("Template.Format: A date/number switch requires matching scalar data.");
            string text;
            CultureInfo culture = policy.Culture;
            if (format != null && formatKind != null)
            {
                (format, bool lowerMeridiem) = WordFormat(format, formatKind);
                if (lowerMeridiem)
                {
                    culture = (CultureInfo)culture.Clone();
                    culture.DateTimeFormat.AMDesignator = culture.DateTimeFormat.AMDesignator.ToLower(culture);
                    culture.DateTimeFormat.PMDesignator = culture.DateTimeFormat.PMDesignator.ToLower(culture);
                }
            }
            try { text = value == null ? "" : value is IFormattable formattable ? formattable.ToString(format, culture) : Convert.ToString(value, culture) ?? ""; }
            catch (FormatException) { throw new InvalidDataException("Template.Format: Unsupported scalar formatting."); }
            CheckText(text, c); expansion += text.Length; LimitExpansion(); return BoundText(prototype, text);
        }
        bool Value(string path, object? scope, out object? value)
        {
            string key = Starts(path, '!') ? path.Substring(1) : path;
            if (Lookup(scope, key, out value) || !ReferenceEquals(scope, root) && Lookup(root, key, out value)) return true;
            if (policy.MissingValues == MissingTemplateValue.Error) throw new InvalidDataException("Template.MissingValue: Required template data is missing.");
            c.Loss("Template.MissingValue", "Missing template data uses the explicitly selected fallback policy.");
            return policy.MissingValues == MissingTemplateValue.Empty;
        }
        void LimitExpansion()
        {
            if (expansion > c.Options.MaximumCharacters) throw new InvalidDataException("Limit.Characters: Template expansion exceeds the character limit.");
            if (expansion * 2 + structuralExpansion > c.Options.MaximumExpandedBytes) throw new InvalidDataException("Limit.ExpandedBytes: Template expansion exceeds the aggregate resource bound.");
        }
    }, token);

    private static (string Kind, string Path) Tag(string tag)
    {
        int colon = tag.IndexOf(':');
        if (colon < 0) throw new NotSupportedException("Template.Tag: Unsupported content-control tag.");
        string kind = tag.Substring(0, colon), path = tag.Substring(colon + 1);
        if (kind is not "merge" and not "image" and not "if" and not "repeat") throw new NotSupportedException("Template.Tag: Unsupported content-control operation.");
        ValidatePath(kind == "if" && Starts(path, '!') ? path.Substring(1) : path); return (kind, path);
    }
    private static (string Path, string? Format, string? Switch) MergeField(string instruction)
    {
        if (instruction.Length > 4096) throw new InvalidDataException("Template.FieldSyntax: Merge instruction exceeds its bound.");
        string[] args = Regex.Matches(instruction, "\"([^\"]*)\"|(\\S+)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Cast<Match>().Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToArray();
        if (args.Length < 2 || instruction.Count(c => c == '"') % 2 != 0) throw new InvalidDataException("Template.FieldSyntax: Invalid merge-field syntax.");
        ValidatePath(args[1]); string? format = null, formatSwitch = null;
        for (int i = 2; i < args.Length; i++)
        {
            string key = args[i]; if (++i >= args.Length) throw new InvalidDataException("Template.FieldSyntax: Missing merge switch value.");
            if (key == "\\*" && args[i].Equals("MERGEFORMAT", StringComparison.OrdinalIgnoreCase)) continue;
            if (key is not "\\@" and not "\\#" || format != null || args[i].Length > 128) throw new InvalidDataException("Template.FieldSyntax: Unsupported merge switch.");
            format = args[i]; formatSwitch = key;
            Match precision = Regex.Match(format, "^[A-Za-z]([0-9]+)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (precision.Success && (!int.TryParse(precision.Groups[1].Value, out int digits) || digits > 1000)) throw new InvalidDataException("Template.Format: Numeric precision exceeds its bound.");
        }
        return (args[1], format, formatSwitch);
    }
    private static (string Format, bool LowerMeridiem) WordFormat(string format, string kind)
    {
        var result = new System.Text.StringBuilder(); char quote = '\0'; bool lower = false;
        for (int i = 0; i < format.Length; i++)
        {
            char value = format[i];
            if (value == '\\')
            {
                if (++i == format.Length) throw new InvalidDataException("Template.Format: An escape must have a following character.");
                result.Append('\\').Append(format[i]); continue;
            }
            if (quote != '\0') { result.Append(value); if (value == quote) quote = '\0'; continue; }
            if (value is '\'' or '"') { quote = value; result.Append(value); continue; }
            if (kind == "\\@" && i + 5 <= format.Length && string.Compare(format, i, "AM/PM", 0, 5, StringComparison.OrdinalIgnoreCase) == 0)
            { lower |= format[i] == 'a'; result.Append("tt"); i += 4; }
            else if (kind == "\\#" && value == '%') result.Append("\\%");
            else if (kind == "\\#" && value == '\u2030') throw new InvalidDataException("Template.Format: Per-mille formatting is outside the supported Word numeric profile.");
            else result.Append(value);
        }
        if (quote != '\0') throw new InvalidDataException("Template.Format: A quoted literal is not closed.");
        return (result.ToString(), lower);
    }
    private static void ValidatePath(string path)
    { if (path.Length > 256 || !path.Split('.').All(TemplateData.Identifier)) throw new InvalidDataException("Template.Path: Unsupported property path."); }
    private static bool Lookup(object? scope, string path, out object? value)
    {
        value = scope;
        foreach (string part in path.Split('.')) if (value is not Dictionary<string, object?> dictionary || !dictionary.TryGetValue(part, out value)) { value = null; return false; }
        return true;
    }
    private static bool Condition(string path, object? value)
    {
        bool truth = value switch
        {
            null => false,
            bool b => b,
            string s => s.Length > 0,
            List<object?> list => list.Count > 0,
            byte or sbyte or short or ushort or int or uint or long or ulong or decimal or float or double => Convert.ToDouble(value, CultureInfo.InvariantCulture) != 0,
            _ => true
        };
        return Starts(path, '!') ? !truth : truth;
    }
    private static TextRun Clean(TextRun r, string text, string? field) => new TextRun(text, r.Bold, r.Italic, r.Code, r.Link, r.FontSize, r.Color,
        r.Image, r.Appearance.Copy(), field, r.Note, r.Box, r.Bookmark)
    { FieldCache = r.FieldCache };
    private static TextRun BoundText(TextRun r, string text) => new TextRun(text, r.Bold, r.Italic, r.Code, r.Link, r.FontSize, r.Color, appearance: r.Appearance.Copy());
    private static void ValidateRanges(DocumentModel model)
    {
        var starts = new HashSet<string>(StringComparer.Ordinal); var ends = new HashSet<string>(StringComparer.Ordinal);
        foreach (TextRun run in model.Paragraphs.SelectMany(p => p.Runs))
            if (run.Bookmark is string name)
            {
                bool end = Starts(name, '/'); string key = end ? name.Substring(1) : name;
                if (!(end ? ends : starts).Add(key)) throw new InvalidDataException("Editing.BookmarkRange: Duplicate bookmark boundary.");
            }
        if (!starts.SetEquals(ends)) throw new InvalidDataException("Editing.BookmarkRange: An edit would leave an incomplete bookmark range.");
    }
}
