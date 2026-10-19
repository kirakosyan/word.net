using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxReader
{
    private readonly ReviewStore review = new ReviewStore();
    private bool insideRevision;
    private long commentNodes, commentCharacters;
    private void ReadReviewComments()
    {
        if (!context.PreserveReview) return;
        foreach (XElement relation in relationships.Values)
        {
            string type = (string?)relation.Attribute("Type") ?? "";
            string role = type.Substring(type.LastIndexOf('/') + 1);
            if (role.StartsWith("comments", StringComparison.Ordinal) && role != "comments" || role is "people" or "person")
                throw new NotSupportedException("Review.Unsupported: Modern review metadata is outside the preservation profile.");
        }
        XElement[] relations = relationships.Values.Where(r => (string?)r.Attribute("Type") == DocxPackage.DocumentRelationships.NamespaceName + "/comments").ToArray();
        if (relations.Length > 1) throw new InvalidDataException("Review.Comments: Multiple comment parts.");
        if (relations.Length == 0) return;
        string name = DocxPackage.ResolvePart(mainPart, relations[0]); knownParts.Add(name);
        XElement root = package.ReadXml(name).Root ?? throw new InvalidDataException("Review.Comments: Missing comment root.");
        if (root.Name != W + "comments") throw new InvalidDataException("Review.Comments: Invalid root.");
        foreach (XElement comment in root.Elements())
        {
            if (comment.Name != W + "comment") throw new NotSupportedException("Review.Unsupported: Unsupported comment body.");
            ReviewRecord record = ReviewMetadata(comment);
            commentCharacters = checked(commentCharacters + record.Author.Length + record.Initials.Length);
            if (review.Comments.ContainsKey(record.Id)) throw new InvalidDataException("Review.Id: Duplicate comment identifier.");
            if (comment.Elements().Any(e => e.Name != W + "p") || comment.Descendants().Any(e => e.Name == W + "ins" || e.Name == W + "del" || e.Name == W + "drawing" || e.Name == W + "fldSimple" || e.Name == W + "sdt"
                || e.Name == W + "fldChar" || e.Name == W + "instrText" || e.Name == W + "hyperlink" || e.Name == W + "bookmarkStart" || e.Name == W + "bookmarkEnd"
                || e.Name.LocalName.StartsWith("comment", StringComparison.Ordinal) || e.Name == W + "footnoteReference" || e.Name == W + "endnoteReference"
                || e.Name.LocalName.EndsWith("Change", StringComparison.Ordinal) || e.Name.LocalName.StartsWith("move", StringComparison.Ordinal)))
                throw new NotSupportedException("Review.Unsupported: Comments support ordinary text paragraphs only.");
            foreach (XElement p in comment.Elements(W + "p"))
            {
                commentNodes = checked(commentNodes + p.Descendants().LongCount() + 1); commentCharacters = checked(commentCharacters + p.Value.Length);
                if (commentNodes * 64 + commentCharacters * 2 > context.Options.MaximumExpandedBytes || commentCharacters > Math.Min(1024 * 1024, context.Options.MaximumCharacters))
                    throw new InvalidDataException("Review.ResourceLimit: Comment bodies exceed aggregate admission limits.");
                record.Body.Add(ReadParagraph(p));
            }
            review.Comments.Add(record.Id, record);
        }
    }
    private static ReviewRecord ReviewMetadata(XElement element)
    {
        string id = Integer(element, W + "id", 0, int.MaxValue).ToString(CultureInfo.InvariantCulture);
        string author = (string?)element.Attribute(W + "author") ?? "";
        string? date = (string?)element.Attribute(W + "date"); DateTimeOffset? timestamp = null;
        if (date != null)
        {
            if (!DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)
                || !(date.Length != 0 && date[date.Length - 1] == 'Z' || date.Length >= 6 && (date[date.Length - 6] is '+' or '-'))) throw new InvalidDataException("Review.Date: Invalid offset timestamp.");
            timestamp = parsed;
        }
        if (author.Length > 1024) throw new InvalidDataException("Review.AuthorLimit: Author exceeds its bound.");
        return new ReviewRecord { Id = id, Author = author, Date = timestamp, Initials = (string?)element.Attribute(W + "initials") ?? "", Deletion = element.Name == W + "del" };
    }
    private bool ReviewInline(XElement element, List<TextRun> runs, TextStyle inherited, string? link)
    {
        if (!context.PreserveReview) return false;
        if (element.Name == W + "ins" || element.Name == W + "del")
        {
            if (insideRevision || fields.Count != 0 || link != null || element.Ancestors().Any(e => e.Name == W + "sdt" || e.Name == W + "fldSimple" || e.Name == W + "txbxContent"
                || e.Name == W + "hdr" || e.Name == W + "ftr" || e.Name == W + "footnote" || e.Name == W + "endnote"))
                throw new NotSupportedException("Review.Unsupported: Nested, field, control or story revisions are unsupported.");
            if (element.Elements().Any(e => e.Name != W + "r") || element.Descendants().Any(e => e.Name != W + "r" && e.Name != W + "rPr"
                && e.Name != W + "t" && e.Name != W + "delText" && e.Name != W + "tab" && e.Name != W + "br" && e.Parent?.Name != W + "rPr"))
                throw new NotSupportedException("Review.Unsupported: Revisions support plain text runs only.");
            ReviewRecord record = ReviewMetadata(element);
            if (review.Revisions.ContainsKey(record.Id)) throw new InvalidDataException("Review.Id: Duplicate revision identifier.");
            var clone = new XElement(element);
            if (record.Deletion)
            {
                if (clone.Descendants(W + "t").Any()) throw new InvalidDataException("Review.DeletedText: Deletions require deleted-text markup.");
                foreach (XElement text in clone.Descendants(W + "delText")) text.Name = W + "t";
            }
            else if (clone.Descendants(W + "delText").Any()) throw new InvalidDataException("Review.DeletedText: Insertions cannot contain deleted-text markup.");
            var content = new List<TextRun>(); insideRevision = true;
            try { ReadInlines(clone, content, inherited, link); } finally { insideRevision = false; }
            if (content.Count == 0) throw new InvalidDataException("Review.Range: Empty revision.");
            foreach (TextRun run in content) { run.Revision = record.Id; runs.Add(run); }
            review.Revisions.Add(record.Id, record); return true;
        }
        if (element.Name == W + "commentRangeStart" || element.Name == W + "commentRangeEnd" || element.Name == W + "commentReference")
        {
            if (insideRevision || fields.Count > 0 || element.Ancestors().Any(e => e.Name == W + "sdt" || e.Name == W + "txbxContent"
                || e.Name == W + "hdr" || e.Name == W + "ftr" || e.Name == W + "footnote" || e.Name == W + "endnote")) throw new NotSupportedException("Review.Unsupported: Comment markers cannot intersect structured runs or unsupported stories.");
            string id = Integer(element, W + "id", 0, int.MaxValue).ToString(CultureInfo.InvariantCulture);
            runs.Add(new TextRun("") { Comment = id, CommentBoundary = element.Name == W + "commentRangeStart" ? 1 : element.Name == W + "commentRangeEnd" ? 2 : 3 }); return true;
        }
        return false;
    }
    private void ValidateReviewSource(XElement root)
    {
        if (!context.PreserveReview) return;
        if (root.Descendants(W + "delText").Any(e => !e.Ancestors(W + "del").Any())) throw new InvalidDataException("Review.DeletedText: Deleted text has no deletion owner.");
        foreach (XElement element in root.Descendants())
            if (element.Name.Namespace == W && (element.Name.LocalName.EndsWith("Change", StringComparison.Ordinal)
                || element.Name.LocalName.StartsWith("move", StringComparison.Ordinal) || element.Name.LocalName.StartsWith("customXml", StringComparison.Ordinal) && element.Name.LocalName.Contains("Range")))
                throw new NotSupportedException("Review.Unsupported: Move, property and structural history is outside the preservation profile.");
        if (root.Descendants(W + "ins").Concat(root.Descendants(W + "del")).Any(e => !e.Ancestors(W + "p").Any() || e.Parent?.Name == W + "rPr" || e.Parent?.Name == W + "trPr"))
            throw new NotSupportedException("Review.Unsupported: Structural revisions are unsupported.");
    }
}
