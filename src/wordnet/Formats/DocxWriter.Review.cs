using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxWriter
{
    private readonly Dictionary<string, int> reviewIds = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly Dictionary<string, int> commentIds = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly Dictionary<string, int> moveRangeIds = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly Dictionary<string, int> moveNames = new Dictionary<string, int>(StringComparer.Ordinal);
    private static int ReviewId(Dictionary<string, int> ids, string key)
    { if (!ids.TryGetValue(key, out int id)) ids.Add(key, id = ids.Count); return id; }
    private XElement Revision(string id)
    {
        ReviewRecord record = model.Review.Revisions[id];
        string name = record.MoveSide == "from" ? "moveFrom" : record.MoveSide == "to" ? "moveTo" : record.Deletion ? "del" : "ins";
        var element = new XElement(W + name, new XAttribute(W + "id", ReviewId(reviewIds, id)), new XAttribute(W + "author", record.Author));
        if (record.Date.HasValue) element.Add(new XAttribute(W + "date", record.Date.Value.ToString("o", CultureInfo.InvariantCulture))); return element;
    }
    private XElement MoveRange(ReviewRecord record, bool start)
    {
        string name = record.MoveSide == "from" ? "moveFromRange" : "moveToRange";
        string from = record.MoveSide == "from" ? record.Id : record.MovePartner!;
        return new XElement(W + (name + (start ? "Start" : "End")),
            new XAttribute(W + "id", ReviewId(moveRangeIds, record.Id)),
            start ? new XAttribute(W + "name", "wnMove" + ReviewId(moveNames, from).ToString(CultureInfo.InvariantCulture)) : null,
            start ? new XAttribute(W + "author", record.Author) : null,
            start ? new XAttribute(W + "date", (record.Date ?? throw new InvalidDataException("Review.Move: A range timestamp is required.")).ToString("o", CultureInfo.InvariantCulture)) : null);
    }
    private void WriteReview(ZipArchive archive, XElement types)
    {
        if (model.Review.Comments.Count == 0) return;
        var comments = new XElement(W + "comments", new XAttribute(XNamespace.Xmlns + "w", W));
        foreach (ReviewRecord record in model.Review.Comments.Values)
        {
            context.CheckCancellation();
            var element = new XElement(W + "comment", new XAttribute(W + "id", ReviewId(commentIds, record.Id)), new XAttribute(W + "author", record.Author));
            if (record.Initials.Length != 0) element.Add(new XAttribute(W + "initials", record.Initials));
            if (record.Date.HasValue) element.Add(new XAttribute(W + "date", record.Date.Value.ToString("o", CultureInfo.InvariantCulture)));
            foreach (ParagraphBlock paragraph in record.Body) element.Add(Paragraph(paragraph));
            if (!element.HasElements) element.Add(new XElement(W + "p")); comments.Add(element);
        }
        WritePart(archive, "word/comments.xml", comments);
        types.Add(new XElement(DocxPackage.ContentTypes + "Override", new XAttribute("PartName", "/word/comments.xml"),
            new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml")));
        relationships.Add(Relationship("rReviewComments", "comments", "comments.xml"));
    }
}
