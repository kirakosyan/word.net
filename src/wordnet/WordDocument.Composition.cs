using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet;

public sealed partial class WordDocument
{
    /// <summary>Appends a private copy with new-page source sections and remapped identifiers. The source is unchanged.</summary>
    public DocumentEditResult Append(WordDocument source, CompositionStyle style = CompositionStyle.KeepSource, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(source, nameof(source));
        if (style is not CompositionStyle.KeepSource and not CompositionStyle.UseDestination) throw new ArgumentOutOfRangeException(nameof(style));
        bool self = ReferenceEquals(source, this);
        DocumentModel? snapshot = null; IReadOnlyList<ConversionDiagnostic> sourceDiagnostics = Array.Empty<ConversionDiagnostic>();
        if (!self) lock (source.gate)
        {
            Admit(source.model, new ConversionContext(options, cancellationToken));
            snapshot = new DocumentCloner(new ConversionContext(options, cancellationToken)).Copy(source.model); sourceDiagnostics = source.diagnostics;
        }
        return Edit((m, c) =>
        {
            DocumentModel incoming = self ? new DocumentCloner(c).Copy(m) : snapshot!;
            foreach (ConversionDiagnostic d in sourceDiagnostics) c.Inherit(d);
            if (incoming.Blocks.Count == 0) return 0;
            ValidateRanges(incoming);
            if (m.Blocks.Count > 0)
            {
                PageSettings originalGeometry = m.FinalSection ?? new PageSettings();
                foreach (DocumentBlock block in m.Blocks) block.Section ??= originalGeometry;
            }
            string prefix = UniquePrefix(m);
            Remap(incoming, prefix, m.Notes.Count == 0 ? 0 : m.Notes.Max(n => n.PartId));
            if (style == CompositionStyle.UseDestination)
            {
                if (incoming.Paragraphs.SelectMany(p => p.Runs).Any(r => r.Object != null)) throw new NotSupportedException("Object.Restyle: Object composition retains source appearance only.");
                var prototypes = m.BodyParagraphs.GroupBy(p => p.HeadingLevel).ToDictionary(g => g.Key, g => g.First());
                foreach (ParagraphBlock paragraph in incoming.BodyParagraphs)
                    if (prototypes.TryGetValue(paragraph.HeadingLevel, out ParagraphBlock? prototype))
                    {
                        paragraph.Layout = prototype.Layout.Copy(); paragraph.Alignment = prototype.Alignment; paragraph.IsCodeBlock = prototype.IsCodeBlock;
                        TextRun? appearance = prototype.Runs.FirstOrDefault(r => r.Image == null && r.Bookmark == null);
                        if (appearance == null) continue;
                        for (int i = 0; i < paragraph.Runs.Count; i++)
                        {
                            TextRun r = paragraph.Runs[i];
                            paragraph.Runs[i] = Restyle(r, appearance);
                        }
                    }
            }
            PageSettings fallback = incoming.FinalSection ?? new PageSettings();
            foreach (DocumentBlock block in incoming.Blocks) block.Section ??= fallback;
            PageSettings[] sections = incoming.Blocks.Select(b => b.Section!).Concat(new[] { fallback }).Distinct().ToArray();
            string[] inheritedStories = m.Blocks.Select(b => b.Section).Concat(new[] { m.FinalSection }).Where(s => s != null).SelectMany(s => s!.Stories.Keys).Distinct().ToArray();
            foreach (PageSettings section in sections)
            {
                foreach (string key in inheritedStories) if (!section.Stories.ContainsKey(key)) section.Stories.Add(key, new List<DocumentBlock> { new ParagraphBlock() });
            }
            incoming.Blocks[0].Section!.BreakType = "nextPage";
            if (m.FinalSection != null && m.Blocks.Count > 0 && !ReferenceEquals(m.Blocks[m.Blocks.Count - 1].Section, m.FinalSection)) m.Blocks.Add(new ParagraphBlock { Section = m.FinalSection });
            m.Blocks.AddRange(incoming.Blocks); m.Notes.AddRange(incoming.Notes); m.FinalSection = fallback;
            foreach (var pair in incoming.Review.Revisions) m.Review.Revisions.Add(pair.Key, pair.Value);
            foreach (var pair in incoming.Review.Comments) m.Review.Comments.Add(pair.Key, pair.Value);
            return incoming.Blocks.Count;
        }, cancellationToken, true, objectAware: true);
    }

    private static string UniquePrefix(DocumentModel m)
    {
        var identifiers = new HashSet<string>(m.Paragraphs.SelectMany(p => p.Runs).Where(r => r.Bookmark != null).Select(r => r.Bookmark!), StringComparer.Ordinal);
        identifiers.UnionWith(m.Paragraphs.Where(p => p.List != null).Select(p => p.List!.Id));
        identifiers.UnionWith(m.Review.Revisions.Keys); identifiers.UnionWith(m.Review.Comments.Keys);
        foreach (TextRun run in m.Paragraphs.SelectMany(p => p.Runs).Where(r => r.FieldInstruction != null))
        {
            Match sequence = Regex.Match(run.FieldInstruction!, "^\\s*SEQ\\s+(\\S+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (sequence.Success) identifiers.Add(sequence.Groups[1].Value.Trim('"'));
        }
        int i = 1;
        while (identifiers.Any(n => n.StartsWith("wn" + i.ToString(CultureInfo.InvariantCulture) + "_", StringComparison.Ordinal))) i++;
        return "wn" + i.ToString(CultureInfo.InvariantCulture) + "_";
    }
    internal static void Remap(DocumentModel source, string prefix, int lastNoteId, bool isolateCounters = true, bool isolateExternalReferences = true)
    {
        foreach (var store in new[] { source.Review.Revisions, source.Review.Comments })
        {
            var records = store.Values.ToArray(); store.Clear();
            foreach (ReviewRecord record in records) { record.Id = prefix + "r" + record.Id; store.Add(record.Id, record); }
        }
        foreach (TableBlock table in source.Tables)
            foreach (int row in table.RowRevisions.Keys.ToArray()) table.RowRevisions[row] = prefix + "r" + table.RowRevisions[row];
        var bookmarks = source.Paragraphs.SelectMany(p => p.Runs).Where(r => r.Bookmark != null && !Starts(r.Bookmark, '/'))
            .Select(r => r.Bookmark!).Distinct().Select((name, i) => (Name: name, NewName: prefix + "b" + i.ToString(CultureInfo.InvariantCulture))).ToDictionary(x => x.Name, x => x.NewName, StringComparer.Ordinal);
        foreach (TextRun run in source.Paragraphs.SelectMany(p => p.Runs))
        {
            if (run.Link != null && Starts(run.Link, '#')) MissingTarget(run.Link.Substring(1));
            if (run.FieldInstruction != null)
            {
                Match reference = Regex.Match(run.FieldInstruction, "^\\s*(?:REF|PAGEREF)\\s+(?:\"([^\"]+)\"|(\\S+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                if (reference.Success) MissingTarget(reference.Groups[1].Success ? reference.Groups[1].Value : reference.Groups[2].Value);
            }
            foreach (TextRun cached in run.FieldCache ?? Enumerable.Empty<TextRun>()) if (cached.Link != null && Starts(cached.Link, '#')) MissingTarget(cached.Link.Substring(1));
        }
        void MissingTarget(string name) { if (isolateExternalReferences && !bookmarks.ContainsKey(name)) bookmarks.Add(name, prefix + "u" + bookmarks.Count.ToString(CultureInfo.InvariantCulture)); }
        foreach (DocumentNote note in source.Notes) { note.PartId = checked(++lastNoteId); note.Id = (note.Endnote ? "endnotes:" : "footnotes:") + note.PartId.ToString(CultureInfo.InvariantCulture); }
        foreach (ParagraphBlock paragraph in source.Paragraphs)
        {
            paragraph.Identity = Guid.NewGuid();
            if (paragraph.StructuralRevision != null) paragraph.StructuralRevision = prefix + "r" + paragraph.StructuralRevision;
            if (paragraph.List != null && isolateCounters) paragraph.List.Id = prefix + paragraph.List.Id;
            for (int i = 0; i < paragraph.Runs.Count; i++)
            {
                TextRun r = paragraph.Runs[i]; string? bookmark = r.Bookmark, link = r.Link, field = r.FieldInstruction;
                if (bookmark != null)
                {
                    bool end = Starts(bookmark, '/'); string name = end ? bookmark.Substring(1) : bookmark;
                    if (bookmarks.TryGetValue(name, out string? replacement)) bookmark = (end ? "/" : "") + replacement;
                }
                if (link != null && Starts(link, '#') && bookmarks.TryGetValue(link.Substring(1), out string? target)) link = "#" + target;
                if (field != null)
                {
                    Match match = Regex.Match(field, "^\\s*(REF|PAGEREF|SEQ)\\s+(?:\"([^\"]+)\"|(\\S+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                    if (match.Success)
                    {
                        Group name = match.Groups[2].Success ? match.Groups[2] : match.Groups[3];
                        string? replacement = match.Groups[1].Value.Equals("SEQ", StringComparison.OrdinalIgnoreCase) ? isolateCounters ? prefix + name.Value : null : bookmarks.TryGetValue(name.Value, out string? value) ? value : null;
                        if (replacement != null) field = field.Remove(name.Index, name.Length).Insert(name.Index, replacement);
                    }
                }
                List<TextRun>? cache = r.FieldCache?.Select(cached => new TextRun(cached.Text, cached.Bold, cached.Italic, cached.Code,
                    cached.Link != null && Starts(cached.Link, '#') && bookmarks.TryGetValue(cached.Link.Substring(1), out string? destination) ? "#" + destination : cached.Link,
                    cached.FontSize, cached.Color, cached.Image, cached.Appearance.Copy())).ToList();
                paragraph.Runs[i] = new TextRun(r.Text, r.Bold, r.Italic, r.Code, link, r.FontSize, r.Color, r.Image, r.Appearance, field, r.Note, r.Box, bookmark, r.Control, r.Object?.Remap())
                {
                    FieldCache = cache,
                    Revision = r.Revision == null ? null : prefix + "r" + r.Revision,
                    Comment = r.Comment == null ? null : prefix + "r" + r.Comment,
                    CommentBoundary = r.CommentBoundary
                };
            }
        }
    }
    private static TextRun Restyle(TextRun r, TextRun appearance) => new TextRun(r.Text, appearance.Bold, appearance.Italic, appearance.Code, r.Link,
        appearance.FontSize, appearance.Color, r.Image, appearance.Appearance.Copy(), r.FieldInstruction, r.Note, r.Box, r.Bookmark, r.Control)
    { FieldCache = r.FieldCache?.Select(cached => Restyle(cached, appearance)).ToList(), Revision = r.Revision, Comment = r.Comment, CommentBoundary = r.CommentBoundary };
}
