using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using WordNet.Layout;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxReader
{
    private readonly ReviewStore review = new ReviewStore();
    private bool insideRevision;
    private readonly Dictionary<string, string> movePartners = new Dictionary<string, string>(StringComparer.Ordinal);
    private long commentNodes, commentCharacters;
    private void ReadReviewComments()
    {
        if (!context.PreserveReview) return;
        foreach (XElement relation in relationships.Values)
        {
            string type = (string?)relation.Attribute("Type") ?? "";
            string role = type.Substring(type.LastIndexOf('/') + 1);
            if (role.StartsWith("comments", StringComparison.Ordinal) && role != "comments")
                throw new NotSupportedException("Review.Unsupported: Modern review metadata is outside the preservation profile.");
            if (role is "people" or "person")
            {
                string people = DocxPackage.ResolvePart(mainPart, relation); _ = package.ReadXml(people); knownParts.Add(people);
                context.Report("Review.AuthorIdentitiesIgnored", "Extended author identities are omitted; revision/comment author metadata is retained.");
            }
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
        string author = (string?)element.Attribute(W + "author") ?? throw new InvalidDataException("Review.Author: Missing required author metadata.");
        string? date = (string?)element.Attribute(W + "date"); DateTimeOffset? timestamp = null;
        if (date != null)
        {
            if (!DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)
                || !(date.Length != 0 && date[date.Length - 1] == 'Z' || date.Length >= 6 && (date[date.Length - 6] is '+' or '-'))) throw new InvalidDataException("Review.Date: Invalid offset timestamp.");
            timestamp = parsed;
        }
        if (author.Length > 1024) throw new InvalidDataException("Review.AuthorLimit: Author exceeds its bound.");
        return new ReviewRecord
        {
            Id = id,
            Author = author,
            Date = timestamp,
            Initials = (string?)element.Attribute(W + "initials") ?? "",
            Deletion = element.Name == W + "del" || element.Name == W + "moveFrom",
            MoveSide = element.Name == W + "moveFrom" ? "from" : element.Name == W + "moveTo" ? "to" : ""
        };
    }
    private bool ReviewInline(XElement element, List<TextRun> runs, TextStyle inherited, string? link)
    {
        if (!context.PreserveReview) return false;
        if (element.Name == W + "ins" || element.Name == W + "del" || element.Name == W + "moveFrom" || element.Name == W + "moveTo")
        {
            if (insideRevision || fields.Count != 0 || link != null || element.Ancestors().Any(e => e.Name == W + "sdt" || e.Name == W + "fldSimple" || e.Name == W + "txbxContent"
                || e.Name == W + "footnote" || e.Name == W + "endnote"))
                throw new NotSupportedException("Review.Unsupported: Nested, field, control, text-box and note revisions are unsupported.");
            if (element.Elements().Any(e => e.Name != W + "r") || element.Descendants().Any(e => e.Name != W + "r" && e.Name != W + "rPr"
                && e.Name != W + "t" && e.Name != W + "delText" && e.Name != W + "tab" && e.Name != W + "br" && e.Parent?.Name != W + "rPr"))
                throw new NotSupportedException("Review.Unsupported: Revisions support plain text runs only.");
            ReviewRecord record = ReviewMetadata(element);
            if (review.Revisions.ContainsKey(record.Id)) throw new InvalidDataException("Review.Id: Duplicate revision identifier.");
            var clone = new XElement(element);
            if (record.Deletion && record.MoveSide.Length == 0)
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
    private void ReadFormatRevision(XElement run, TextStyle inherited, string? link, List<TextRun> runs, int first)
    {
        if (!context.PreserveReview) return;
        XElement[] changes = run.Element(W + "rPr")?.Elements(W + "rPrChange").ToArray() ?? Array.Empty<XElement>();
        if (changes.Length == 0) return;
        if (changes.Length != 1 || insideRevision || fields.Count != 0 || link != null || runs.Count != first + 1
            || run.Ancestors().Any(e => e.Name == W + "sdt" || e.Name == W + "txbxContent" || e.Name == W + "hdr" || e.Name == W + "ftr" || e.Name == W + "footnote" || e.Name == W + "endnote")
            || run.Elements().Any(e => e.Name != W + "rPr" && e.Name != W + "t"))
            throw new NotSupportedException("Review.Format: Formatting history requires one ordinary body/cell text run.");
        XElement change = changes[0]; XElement? old = change.Element(W + "rPr");
        XElement properties = run.Element(W + "rPr")!;
        if (properties.Elements().GroupBy(e => e.Name).Any(g => g.Count() != 1)
            || properties.Elements().Any(e => e.Name != W + "rPrChange" && e.Name != W + "b" && e.Name != W + "i" && e.Name != W + "sz" && e.Name != W + "color"
                && e.Name != W + "rFonts" && e.Name != W + "rStyle" && e.Name != W + "strike" && e.Name != W + "u" && e.Name != W + "shd" && e.Name != W + "vertAlign"))
            throw new NotSupportedException("Review.Format: Unsupported current run properties in formatting history.");
        if (old == null || change.Elements().Count() != 1 || old.Elements().GroupBy(e => e.Name).Any(g => g.Count() != 1)
            || old.Elements().Any(e => e.Name != W + "b" && e.Name != W + "i" && e.Name != W + "sz" && e.Name != W + "color"
                && e.Name != W + "rFonts" && e.Name != W + "rStyle" && e.Name != W + "strike" && e.Name != W + "u" && e.Name != W + "shd" && e.Name != W + "vertAlign"))
            throw new NotSupportedException("Review.Format: Only bold, italic, size and color history is supported.");
        ReviewRecord record = ReviewMetadata(change); record.Structure = "format";
        if (review.Revisions.ContainsKey(record.Id)) throw new InvalidDataException("Review.Id: Duplicate revision identifier.");
        TextStyle previous = inherited.Copy(); ReadRunProperties(old, previous);
        TextRun owner = runs[first]; owner.FormatRevision = record.Id;
        owner.PreviousFormat = new TextRun(owner.Text, previous.Bold, previous.Italic, previous.Code, fontSize: previous.FontSize, color: previous.Color, appearance: previous.Appearance.Copy());
        TextRun currentOther = new TextRun(owner.Text, code: owner.Code, appearance: owner.Appearance);
        TextRun previousOther = new TextRun(owner.Text, code: previous.Code, appearance: previous.Appearance);
        if (!RunCoalescer.Same(currentOther, previousOther)) throw new NotSupportedException("Review.Format: Unsupported non-admitted run property change.");
        review.Revisions.Add(record.Id, record);
    }
    private string? ReadStructuralRevision(XElement? properties, string kind)
    {
        XElement[] markers = properties?.Elements().Where(e => e.Name == W + "ins" || e.Name == W + "del" || e.Name == W + "moveFrom" || e.Name == W + "moveTo").ToArray() ?? Array.Empty<XElement>();
        if (markers.Length == 0) return null;
        if (!context.PreserveReview)
        {
            context.Loss("Revisions.Accepted", "Structural revision marks are omitted in the accepted-state conversion profile.");
            return null;
        }
        XElement? owner = kind == "paragraph" ? properties?.Parent?.Parent : properties?.Parent;
        bool bodyOwner = kind == "paragraph" ? owner?.Parent?.Name == W + "body" : owner?.Parent?.Parent?.Name == W + "body";
        if (!bodyOwner) throw new NotSupportedException("Review.Unsupported: Structural revisions require top-level body paragraphs or simple body table rows; story and nested owners are unsupported.");
        if (markers.Length != 1 || markers[0].HasElements) throw new InvalidDataException("Review.Structure: Invalid structural marker.");
        ReviewRecord record = ReviewMetadata(markers[0]); record.Structure = kind;
        if (record.MoveSide.Length != 0)
        {
            if (kind != "paragraph" || !movePartners.TryGetValue(record.Id, out string? partner)) throw new InvalidDataException("Review.Move: Missing validated range pair.");
            record.MovePartner = partner;
        }
        if (review.Revisions.ContainsKey(record.Id)) throw new InvalidDataException("Review.Id: Duplicate structural revision identifier.");
        review.Revisions.Add(record.Id, record); return record.Id;
    }
    private void ValidateReviewSource(XElement root)
    {
        if (!context.PreserveReview) return;
        ValidateMoveRanges(root);
        if (root.Descendants(W + "delText").Any(e => !e.Ancestors(W + "del").Any())) throw new InvalidDataException("Review.DeletedText: Deleted text has no deletion owner.");
        foreach (XElement element in root.Descendants())
            if (element.Name.Namespace == W && (element.Name.LocalName.EndsWith("Change", StringComparison.Ordinal) && element.Name != W + "rPrChange"
                || element.Name.LocalName.StartsWith("move", StringComparison.Ordinal) && element.Name != W + "moveFrom" && element.Name != W + "moveTo"
                    && element.Name != W + "moveFromRangeStart" && element.Name != W + "moveFromRangeEnd" && element.Name != W + "moveToRangeStart" && element.Name != W + "moveToRangeEnd"
                || element.Name.LocalName.StartsWith("customXml", StringComparison.Ordinal) && element.Name.LocalName.Contains("Range")))
                throw new NotSupportedException("Review.Unsupported: Move, property and structural history is outside the preservation profile.");
        foreach (XElement change in root.Descendants(W + "rPrChange"))
            if (change.Parent?.Name != W + "rPr" || change.Parent.Parent?.Name != W + "r")
                throw new NotSupportedException("Review.Format: Run property history requires an ordinary run owner.");
        foreach (XElement marker in root.Descendants(W + "ins").Concat(root.Descendants(W + "del")))
        {
            bool paragraphMark = marker.Parent?.Name == W + "rPr" && marker.Parent.Parent?.Name == W + "pPr" && marker.Parent.Parent.Parent?.Parent?.Name == W + "body";
            bool rowMark = marker.Parent?.Name == W + "trPr" && marker.Parent.Parent?.Parent?.Parent?.Name == W + "body";
            if (paragraphMark || rowMark) continue;
            if (marker.Parent?.Name != W + "p") throw new NotSupportedException("Review.Unsupported: Only plain paragraph marks and simple body rows support structural revisions.");
        }
    }
    private void ValidateMoveRanges(XElement root)
    {
        var moves = new Dictionary<string, (string? From, string? To)>(StringComparer.Ordinal);
        var rangeIds = new HashSet<string>(StringComparer.Ordinal); XElement[] blocks = root.Elements().ToArray();
        var owners = new HashSet<XElement>();
        int seen = 0;
        for (int i = 0; i < blocks.Length; i++)
        {
            context.CheckCancellation(); XElement start = blocks[i];
            bool from = start.Name == W + "moveFromRangeStart", to = start.Name == W + "moveToRangeStart";
            if (!from && !to) continue;
            if (++seen > 10000 || i + 2 >= blocks.Length) throw new InvalidDataException("Review.Move: Invalid or excessive move ranges.");
            string id = Integer(start, W + "id", 0, int.MaxValue).ToString(CultureInfo.InvariantCulture);
            string name = DocxPackage.RequiredAttribute(start, W + "name"); XElement paragraph = blocks[++i], end = blocks[++i];
            if (name.Length == 0 || name.Length > 256 || !rangeIds.Add(id) || paragraph.Name != W + "p"
                || end.Name != W + (from ? "moveFromRangeEnd" : "moveToRangeEnd")
                || Integer(end, W + "id", 0, int.MaxValue).ToString(CultureInfo.InvariantCulture) != id
                || start.Attributes().Any(a => !a.IsNamespaceDeclaration && a.Name != W + "id" && a.Name != W + "name" && a.Name != W + "author" && a.Name != W + "date")
                || end.Attributes().Any(a => !a.IsNamespaceDeclaration && a.Name != W + "id"))
                throw new InvalidDataException("Review.Move: Invalid or unsupported range boundaries.");
            XElement? mark = paragraph.Element(W + "pPr")?.Element(W + "rPr")?.Element(W + (from ? "moveFrom" : "moveTo"));
            XElement[] content = paragraph.Elements().Where(e => e.Name != W + "pPr").ToArray();
            if (mark == null || paragraph.Descendants(W + "sectPr").Any() || content.Length != 1 || content[0].Name != W + (from ? "moveFrom" : "moveTo"))
                throw new NotSupportedException("Review.Move: Only a whole plain paragraph inside each range is supported.");
            ReviewRecord rangeMetadata = ReviewMetadata(start), markMetadata = ReviewMetadata(mark);
            if (!rangeMetadata.Date.HasValue || rangeMetadata.Date != markMetadata.Date || rangeMetadata.Author != markMetadata.Author)
                throw new NotSupportedException("Review.Move: Range and paragraph attribution must match and include a timestamp.");
            owners.Add(paragraph);
            string markId = Integer(mark, W + "id", 0, int.MaxValue).ToString(CultureInfo.InvariantCulture);
            moves.TryGetValue(name, out var pair);
            if (from ? pair.From != null : pair.To != null) throw new InvalidDataException("Review.Move: Duplicate move range name.");
            moves[name] = from ? (markId, pair.To) : (pair.From, markId);
        }
        int markerCount = 0;
        foreach (XElement element in root.Descendants())
        {
            context.CheckCancellation();
            if (element.Name == W + "moveFromRangeStart" || element.Name == W + "moveToRangeStart" || element.Name == W + "moveFromRangeEnd" || element.Name == W + "moveToRangeEnd") markerCount++;
        }
        if (markerCount != seen * 2)
            throw new NotSupportedException("Review.Move: Nested or unpaired move ranges are unsupported.");
        foreach (var pair in moves.Values)
        {
            if (pair.From == null || pair.To == null || pair.From == pair.To || movePartners.ContainsKey(pair.From) || movePartners.ContainsKey(pair.To))
                throw new InvalidDataException("Review.Move: Missing or duplicate move pair.");
            movePartners.Add(pair.From, pair.To); movePartners.Add(pair.To, pair.From);
        }
        foreach (XElement move in root.Descendants().Where(e => e.Name == W + "moveFrom" || e.Name == W + "moveTo"))
            if (!(move.Parent?.Name == W + "p" && owners.Contains(move.Parent) || move.Parent?.Name == W + "rPr" && move.Parent.Parent?.Name == W + "pPr"
                && move.Parent.Parent.Parent is XElement paragraph && owners.Contains(paragraph)))
                throw new NotSupportedException("Review.Move: Moves are limited to whole body paragraphs.");
    }
}
