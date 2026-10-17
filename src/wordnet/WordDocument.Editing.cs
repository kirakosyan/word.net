using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet;

public sealed partial class WordDocument
{
    /// <summary>Replaces non-overlapping literal matches across adjacent plain-text runs within each paragraph/story.</summary>
    public DocumentEditResult Replace(string search, string replacement, bool ignoreCase = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(search)) throw new ArgumentException("Search text cannot be empty.", nameof(search));
        if (search.Length > 4096) throw new ArgumentException("Search text exceeds the pattern limit.", nameof(search));
        return ReplaceCore(Regex.Escape(search), replacement, ignoreCase, TimeSpan.FromSeconds(1), cancellationToken, literal: true);
    }
    /// <summary>Replaces bounded regex matches with literal text. Zero-length matches and surrogate-splitting matches fail atomically.</summary>
    public DocumentEditResult ReplaceRegex(string pattern, string replacement, TimeSpan timeout, bool ignoreCase = false, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(pattern, nameof(pattern));
        if (pattern.Length == 0 || pattern.Length > 4096) throw new ArgumentException("Regex patterns must contain 1–4096 characters.", nameof(pattern));
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(1)) throw new ArgumentOutOfRangeException(nameof(timeout));
        return ReplaceCore(pattern, replacement, ignoreCase, timeout, cancellationToken);
    }
    private DocumentEditResult ReplaceCore(string pattern, string replacement, bool ignoreCase, TimeSpan timeout, CancellationToken token, bool literal = false)
    {
        Guard.NotNull(replacement, nameof(replacement));
        var regex = new Regex(pattern, RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None), literal ? Regex.InfiniteMatchTimeout : timeout);
        return Edit((m, c) =>
        {
            CheckText(replacement, c); int changes = 0; long expansion = m.CharacterCount;
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            foreach (ParagraphBlock paragraph in m.Paragraphs)
            {
                c.CheckCancellation(); var output = new List<TextRun>();
                if (!literal && elapsed.Elapsed > timeout) throw new TimeoutException("Editing.RegexTimeout: Matching exceeded the operation time bound.");
                for (int i = 0; i < paragraph.Runs.Count;)
                {
                    if (!Plain(paragraph.Runs[i])) { output.Add(paragraph.Runs[i++]); continue; }
                    var runs = new List<TextRun>();
                    while (i < paragraph.Runs.Count && Plain(paragraph.Runs[i])) runs.Add(paragraph.Runs[i++]);
                    string text = string.Concat(runs.Select(r => r.Text)); int position = 0, runIndex = 0, offset = 0;
                    for (Match match = regex.Match(text); match.Success; match = match.NextMatch())
                    {
                        c.CheckCancellation();
                        if (!literal && elapsed.Elapsed > timeout) throw new TimeoutException("Editing.RegexTimeout: Matching exceeded the operation time bound.");
                        if (match.Length == 0) throw new ArgumentException("Editing.ZeroLengthMatch: Zero-length regex matches are unsupported.", nameof(pattern));
                        Boundary(text, match.Index); Boundary(text, match.Index + match.Length);
                        expansion = checked(expansion + replacement.Length - match.Length);
                        if (expansion > c.Options.MaximumCharacters) throw new InvalidDataException("Limit.Characters: Replacement expansion exceeds the character limit.");
                        if (++changes > c.Options.MaximumCharacters) throw new InvalidDataException("Limit.Matches: Too many replacement matches.");
                        CopyUntil(match.Index, true);
                        TextRun prototype = runs[runIndex];
                        if (replacement.Length > 0) output.Add(prototype.WithText(replacement));
                        CopyUntil(match.Index + match.Length, false);
                    }
                    CopyUntil(text.Length, true);
                    void CopyUntil(int end, bool keep)
                    {
                        while (runIndex < runs.Count)
                        {
                            TextRun run = runs[runIndex];
                            if (offset == run.Text.Length) { runIndex++; offset = 0; continue; }
                            if (position == end) break;
                            int count = Math.Min(end - position, run.Text.Length - offset);
                            if (keep && count > 0) output.Add(run.WithText(run.Text.Substring(offset, count)));
                            offset += count; position += count;
                        }
                    }
                }
                paragraph.Runs.Clear(); paragraph.Runs.AddRange(output);
            }
            return changes;
        }, token);
    }

    /// <summary>Gets immutable named range snapshots, including body, tables and stories.</summary>
    public IReadOnlyList<DocumentBookmark> Bookmarks
    {
        get
        {
            lock (gate)
            {
                var active = new Dictionary<string, System.Text.StringBuilder>(StringComparer.Ordinal);
                var result = new List<DocumentBookmark>();
                foreach (ParagraphBlock paragraph in model.Paragraphs)
                    foreach (TextRun run in paragraph.Runs)
                        if (run.Bookmark is string name)
                        {
                            if (Starts(name, '/'))
                            { if (active.TryGetValue(name.Substring(1), out var text)) { result.Add(new DocumentBookmark(name.Substring(1), text.ToString())); active.Remove(name.Substring(1)); } }
                            else active[name] = new System.Text.StringBuilder();
                        }
                        else foreach (var text in active.Values) text.Append(run.Text);
                return result.AsReadOnly();
            }
        }
    }
    /// <summary>Creates a named range within plain text in one body paragraph, using UTF-16 offsets.</summary>
    public void AddBookmark(string name, int paragraphIndex, int start, int length, CancellationToken cancellationToken = default)
    {
        ValidateBookmarkName(name);
        _ = Edit((m, c) =>
        {
            if (m.Paragraphs.SelectMany(p => p.Runs).Any(r => r.Bookmark == name)) throw new ArgumentException("A bookmark with this name already exists.", nameof(name));
            ParagraphBlock[] paragraphs = m.BodyParagraphs.ToArray();
            if (paragraphIndex < 0 || paragraphIndex >= paragraphs.Length) throw new ArgumentOutOfRangeException(nameof(paragraphIndex));
            ParagraphBlock p = paragraphs[paragraphIndex];
            if (p.Runs.Any(r => !Plain(r))) throw new NotSupportedException("Editing.BookmarkStructure: New ranges require one plain-text paragraph.");
            string text = p.Text;
            if (start < 0 || length < 0 || start > text.Length - length) throw new ArgumentOutOfRangeException(nameof(start));
            Boundary(text, start); Boundary(text, start + length);
            var output = new List<TextRun>(); Slice(p.Runs, 0, start, output); output.Add(new TextRun("", bookmark: name));
            Slice(p.Runs, start, length, output); output.Add(new TextRun("", bookmark: "/" + name)); Slice(p.Runs, start + length, text.Length - start - length, output);
            p.Runs.Clear(); p.Runs.AddRange(output); return 1;
        }, cancellationToken);
    }
    /// <summary>Replaces one paragraph-local bookmark range with supported styled text/images, retaining its boundaries.</summary>
    public DocumentEditResult ReplaceBookmark(string name, IEnumerable<DocumentText> content, CancellationToken cancellationToken = default) => EditBookmark(name, content, false, false, cancellationToken);
    /// <summary>Inserts supported content inside a bookmark at its start or end, retaining its boundaries.</summary>
    public DocumentEditResult InsertAtBookmark(string name, IEnumerable<DocumentText> content, bool atEnd = false, CancellationToken cancellationToken = default) => EditBookmark(name, content, true, atEnd, cancellationToken);
    private DocumentEditResult EditBookmark(string name, IEnumerable<DocumentText> content, bool insert, bool end, CancellationToken token)
    {
        ValidateBookmarkName(name); Guard.NotNull(content, nameof(content));
        return Edit((m, c) =>
        {
            ParagraphBlock[] all = m.Paragraphs.ToArray();
            var starts = all.SelectMany(p => p.Runs.Select((r, i) => (Paragraph: p, Run: r, Index: i))).Where(x => x.Run.Bookmark == name).ToArray();
            var ends = all.SelectMany(p => p.Runs.Select((r, i) => (Paragraph: p, Run: r, Index: i))).Where(x => x.Run.Bookmark == "/" + name).ToArray();
            if (starts.Length != 1 || ends.Length != 1) throw new InvalidDataException("Editing.BookmarkRange: A unique closed bookmark is required.");
            var a = starts[0]; var b = ends[0];
            if (!ReferenceEquals(a.Paragraph, b.Paragraph) || a.Index >= b.Index) throw new NotSupportedException("Editing.BookmarkStructure: This profile edits ranges within one paragraph.");
            if (a.Paragraph.Runs.Skip(a.Index).Take(b.Index - a.Index + 1).Any(r => r.Control != null))
                throw new NotSupportedException("Editing.BookmarkStructure: Template controls cannot be split or overwritten.");
            if (!insert && a.Paragraph.Runs.Skip(a.Index + 1).Take(b.Index - a.Index - 1).Any(r => r.Bookmark != null || r.Note != null || r.Box != null || r.FieldInstruction != null))
                throw new NotSupportedException("Editing.BookmarkStructure: Nested ranges and field/note structures cannot be overwritten.");
            var runs = new List<TextRun>(); AddRuns(runs, content, c);
            if (!insert) a.Paragraph.Runs.RemoveRange(a.Index + 1, b.Index - a.Index - 1);
            a.Paragraph.Runs.InsertRange(insert && end ? b.Index : a.Index + 1, runs); return 1;
        }, token);
    }
    private static bool Plain(TextRun r) => r.Image == null && r.Note == null && r.Box == null && r.Bookmark == null && r.FieldInstruction == null && r.Control == null;
    private static bool Starts(string text, char c) => text.Length > 0 && text[0] == c;
    private static void Boundary(string text, int index)
    { if (index > 0 && index < text.Length && char.IsHighSurrogate(text[index - 1]) && char.IsLowSurrogate(text[index])) throw new ArgumentException("Editing.UnicodeBoundary: An edit splits a Unicode character."); }
    private static void Slice(List<TextRun> runs, int start, int length, List<TextRun> output)
    {
        int position = 0, end = checked(start + length);
        foreach (TextRun run in runs)
        {
            int a = Math.Max(start, position), b = Math.Min(end, position + run.Text.Length);
            if (b > a) output.Add(run.WithText(run.Text.Substring(a - position, b - a)));
            position += run.Text.Length; if (position >= end) break;
        }
    }
    private static void ValidateBookmarkName(string name)
    {
        Guard.NotNull(name, nameof(name));
        if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z_0-9]{0,39}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) throw new ArgumentException("Bookmark names must use 1–40 ASCII identifier characters.", nameof(name));
    }
}
