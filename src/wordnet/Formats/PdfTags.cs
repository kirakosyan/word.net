using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UglyToad.PdfPig.Graphics.Operations.MarkedContent;
using UglyToad.PdfPig.Tokens;
using UglyToad.PdfPig.Writer;
using WordNet.Internal;
using WordNet.Layout;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed class PdfTags
{
    internal sealed class Node
    {
        internal Node(string role, Node? parent) { Role = role; Parent = parent; parent?.Children.Add(this); }
        internal string Role { get; }
        internal Node? Parent { get; }
        internal string? Alt { get; set; }
        internal string? LinkDescription { get; set; }
        internal int Span { get; set; } = 1;
        internal List<Node> Children { get; } = new List<Node>();
        internal List<(int Page, int Mcid)> Marks { get; } = new List<(int, int)>();
        internal List<(int Page, int Index)> Links { get; } = new List<(int, int)>();
        internal bool Used => Marks.Count > 0 || Links.Count > 0 || Children.Any(c => c.Used);
    }
    internal Node Root { get; } = new Node("Document", null);
    internal List<List<Node>> Pages { get; } = new List<List<Node>>();
    internal List<List<Node>> Annotations { get; } = new List<List<Node>>();
    private readonly Dictionary<ParagraphBlock, Node> paragraphs = new Dictionary<ParagraphBlock, Node>();
    private readonly Dictionary<TextRun, Node> runs = new Dictionary<TextRun, Node>();
    private readonly Dictionary<ParagraphBlock, Node> labels = new Dictionary<ParagraphBlock, Node>();
    private readonly HashSet<ParagraphBlock> headers = new HashSet<ParagraphBlock>();
    private readonly Dictionary<ParagraphBlock, int> firstHeaderPage = new Dictionary<ParagraphBlock, int>();
    private readonly ConversionContext context;
    internal PdfOptions Options { get; }
    private int nodes;
    private int previousHeading;
    internal PdfTags(DocumentModel model, PdfOptions options, ConversionContext context)
    {
        this.context = context; Options = options;
        if (options.Accessibility is < PdfAccessibility.None or > PdfAccessibility.PdfUa1) throw new ArgumentOutOfRangeException(nameof(options));
        if (options.Conformance != PdfConformance.Pdf17) throw new NotSupportedException("Pdf.TagsConformance: Tagged PDF/A is outside the independently qualified profile.");
        if (context.Typography.Enabled) throw new NotSupportedException("Pdf.TagsTypography: Combining native word carriers with PDF/UA is outside this initial profile.");
        if (options.Language == null || options.Language.Length > 64 || !Regex.IsMatch(options.Language, @"\A[A-Za-z]{2,3}(-[A-Za-z]{4})?(-[A-Za-z]{2}|-[0-9]{3})?\z", RegexOptions.CultureInvariant)) Missing("Pdf.Language", "Set Pdf.Language to a supported BCP 47 language[-Script][-Region] tag.");
        if (string.IsNullOrWhiteSpace(model.Title)) Missing("Pdf.Title", "Supply the document title before requesting accessible PDF.");
        if (model.Notes.Count > 0 || model.Paragraphs.Any(p => p.Runs.Any(r => r.Box != null || r.Note != null))) Missing("Pdf.SemanticFeature", "Footnote relationships and positioned text boxes are outside the initial accessibility profile; use ordinary PDF or simplify the source.");
        Blocks(model.Blocks, Root);
    }
    private Node Add(string role, Node parent)
    {
        context.CheckCancellation(); if (++nodes > Math.Min(100000, context.Options.MaximumExpandedBytes / 256)) throw new InvalidDataException("Limit.PdfTags: Semantic tree exceeds its node budget."); return new Node(role, parent);
    }
    private void Missing(string code, string message)
    {
        if (Options.Accessibility == PdfAccessibility.PdfUa1) throw new NotSupportedException(code + ": " + message);
        context.Loss(code, message);
    }
    private void Blocks(IEnumerable<DocumentBlock> blocks, Node parent)
    {
        var lists = new List<(string Id, Node List, Node? LastBody)>();
        foreach (DocumentBlock block in blocks)
        {
            if (block is ParagraphBlock paragraph)
            {
                Node container = parent;
                if (paragraph.List is ListItem item)
                {
                    int level = item.Level;
                    if (level < 0 || level > 8) throw new InvalidDataException("Pdf.List: Invalid list level.");
                    while (lists.Count > level + 1) lists.RemoveAt(lists.Count - 1);
                    if (lists.Count > level && lists[level].Id != item.Id) lists.RemoveRange(level, lists.Count - level);
                    if (level > lists.Count) { Missing("Pdf.ListNesting", "A nested list requires a preceding parent item."); level = lists.Count; }
                    if (lists.Count == level)
                    {
                        Node listParent = level == 0 ? parent : lists[level - 1].LastBody ?? parent;
                        lists.Add((item.Id, Add("L", listParent), null));
                    }
                    Node li = Add("LI", lists[level].List); labels.Add(paragraph, Add("Lbl", li)); container = Add("LBody", li); lists[level] = (item.Id, lists[level].List, container);
                }
                else lists.Clear();
                if (paragraph.HeadingLevel > 0) { if (paragraph.HeadingLevel > previousHeading + 1) Missing("Pdf.HeadingOrder", "Heading levels must start at one and increase by at most one level."); previousHeading = paragraph.HeadingLevel; }
                Node p = Add(paragraph.HeadingLevel > 0 ? "H" + paragraph.HeadingLevel.ToString(System.Globalization.CultureInfo.InvariantCulture) : "P", container); paragraphs.Add(paragraph, p);
                foreach (TextRun run in paragraph.Runs)
                {
                    if (run.Bookmark != null || run.Image?.Decorative == true) continue;
                    string role = run.Image != null ? "Figure" : run.Link != null ? "Link" : "Span";
                    Node owner = run.Image != null && run.Link != null ? Add("Link", p) : p;
                    Node node = Add(role, owner); runs.Add(run, node); if (run.Link != null) (owner == p ? node : owner).LinkDescription = run.Image?.AlternativeText ?? run.Text;
                    if (run.Image != null) { node.Alt = run.Image.AlternativeText; if (string.IsNullOrWhiteSpace(node.Alt)) Missing("Pdf.ImageAlt", "Provide meaningful image alternative text; the initial profile does not infer whether an image is decorative."); }
                }
            }
            else if (block is TableBlock table)
            {
                lists.Clear(); Node t = Add("Table", parent);
                if (table.HeaderRowCount == 0) Missing("Pdf.TableHeaders", "Mark the column header row in each data table.");
                if (table.Rows.SelectMany(r => r).Any(c => c.Merge != VerticalMerge.None)) Missing("Pdf.TableSemantics", "Vertical cell merges require explicit complex header associations outside this initial accessibility profile.");
                for (int r = 0; r < table.Rows.Count; r++)
                {
                    Node row = Add("TR", t);
                    foreach (TableCell cell in table.Rows[r])
                    {
                        if (cell.Merge == VerticalMerge.Continue) continue;
                        Node td = Add(r < table.HeaderRowCount ? "TH" : "TD", row); td.Span = cell.ColumnSpan;
                        if (r < table.HeaderRowCount) foreach (ParagraphBlock p in DocumentModel.CellParagraphs(cell.Blocks)) headers.Add(p);
                        Blocks(cell.Blocks, td);
                    }
                }
            }
        }
    }
    internal void NewPage() { Pages.Add(new List<Node>()); Annotations.Add(new List<Node>()); }
    internal Node? Begin(PdfPageBuilder target, PositionedText text)
    {
        Node? node = null;
        if ((text.Text.Length > 0 || text.Image != null) && text.SourceParagraph is ParagraphBlock p && paragraphs.TryGetValue(p, out Node? paragraph))
        {
            int page = Pages.Count - 1;
            bool repeated = false;
            if (headers.Contains(p)) { if (!firstHeaderPage.TryGetValue(p, out int first)) firstHeaderPage.Add(p, page); else repeated = first != page; }
            if (!repeated && text.Image?.Decorative != true) node = text.IsLabel && labels.TryGetValue(p, out Node? label) ? label : text.SourceRun != null && runs.TryGetValue(text.SourceRun, out Node? run) ? run : paragraph;
        }
        if (node == null) target.CurrentStream.Operations.Add(new BeginMarkedContent(NameToken.Create("Artifact")));
        else
        {
            int page = Pages.Count - 1, mcid = Pages[page].Count; Pages[page].Add(node); node.Marks.Add((page, mcid));
            target.CurrentStream.Operations.Add(new BeginMarkedContentWithProperties(NameToken.Create(node.Role), new DictionaryToken(new Dictionary<NameToken, IToken> { [NameToken.Create("MCID")] = new NumericToken(mcid) })));
        }
        return node;
    }
    internal bool Link(Node? node)
    {
        if (node?.Role == "Figure" && node.Parent?.Role == "Link") node = node.Parent;
        if (node?.Role != "Link") { Missing("Pdf.LinkSemantics", "A generated or artifact hyperlink lacks a source link relationship; its annotation is omitted."); return false; }
        int page = Pages.Count - 1; node.Links.Add((page, Annotations[page].Count)); Annotations[page].Add(node); return true;
    }
    internal byte[] Xmp(string title)
    {
        XNamespace x = "adobe:ns:meta/", rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#", dc = "http://purl.org/dc/elements/1.1/", ua = "http://www.aiim.org/pdfua/ns/id/";
        return Encoding.UTF8.GetBytes(new XElement(x + "xmpmeta", new XAttribute(XNamespace.Xmlns + "x", x), new XElement(rdf + "RDF", new XAttribute(XNamespace.Xmlns + "rdf", rdf), new XElement(rdf + "Description", new XAttribute(rdf + "about", ""), new XAttribute(XNamespace.Xmlns + "pdfuaid", ua), new XAttribute(XNamespace.Xmlns + "dc", dc), Options.Accessibility == PdfAccessibility.PdfUa1 ? new XElement(ua + "part", "1") : null, new XElement(dc + "title", new XElement(rdf + "Alt", new XElement(rdf + "li", new XAttribute(XNamespace.Xml + "lang", "x-default"), title)))))).ToString(SaveOptions.DisableFormatting));
    }
}
