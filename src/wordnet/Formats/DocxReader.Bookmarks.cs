using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxReader
{
    private TextRun? ReadBookmark(XElement element)
    {
        int id = Integer(element, W + "id", 0, int.MaxValue);
        if (element.Name == W + "bookmarkStart")
        {
            string name = DocxPackage.RequiredAttribute(element, W + "name");
            if (name.Length > 256 || bookmarks.ContainsKey(id) || bookmarks.ContainsValue(name)) throw new InvalidDataException("Docx.Bookmark: Duplicate or oversized bookmark.");
            bookmarks.Add(id, name);
            return new TextRun("", bookmark: name);
        }
        if (bookmarks.TryGetValue(id, out string? existing)) return new TextRun("", bookmark: "/" + existing);
        context.Report("Docx.BookmarkEndIgnored", "An unmatched bookmark end is ignored while retaining its surrounding content.");
        return null;
    }

    private bool BlockBookmark(XElement element, IEnumerable<DocumentBlock> blocks, List<TextRun> pending)
    {
        if (element.Name != W + "bookmarkStart" && element.Name != W + "bookmarkEnd") return false;
        TextRun? marker = ReadBookmark(element);
        if (marker == null) return true;
        if (element.Name == W + "bookmarkEnd" && pending.Count == 0)
        {
            ParagraphBlock? last = DocumentModel.Expand(blocks).OfType<ParagraphBlock>().LastOrDefault();
            if (last != null) { last.Runs.Add(marker); return true; }
        }
        pending.Add(marker);
        return true;
    }

    private static void AttachBookmarks(List<TextRun> pending, IEnumerable<DocumentBlock> blocks, bool first)
    {
        if (pending.Count == 0) return;
        IEnumerable<ParagraphBlock> paragraphs = DocumentModel.Expand(blocks).OfType<ParagraphBlock>();
        ParagraphBlock? paragraph = first ? paragraphs.FirstOrDefault() : paragraphs.LastOrDefault();
        if (paragraph == null) return;
        if (first) paragraph.Runs.InsertRange(0, pending);
        else paragraph.Runs.AddRange(pending);
        pending.Clear();
    }
}
