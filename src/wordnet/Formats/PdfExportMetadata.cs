using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using UglyToad.PdfPig.Outline;
using UglyToad.PdfPig.Outline.Destinations;
using WordNet.Layout;
using WordNet.Model;

namespace WordNet.Formats;

internal static class PdfExportMetadata
{
    internal static string Producer => "wordnet " + (typeof(WordConverter).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion.Split('+')[0] ?? typeof(WordConverter).Assembly.GetName().Version?.ToString() ?? "unknown");

    internal static XDocument Xmp(DocumentModel model, DateTimeOffset time)
    {
        XNamespace x = "adobe:ns:meta/", rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#",
            dc = "http://purl.org/dc/elements/1.1/", pdf = "http://ns.adobe.com/pdf/1.3/",
            xmp = "http://ns.adobe.com/xap/1.0/", aid = "http://www.aiim.org/pdfa/ns/id/";
        string instant = time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        return new XDocument(new XElement(x + "xmpmeta", new XAttribute(XNamespace.Xmlns + "x", x),
            new XElement(rdf + "RDF", new XAttribute(XNamespace.Xmlns + "rdf", rdf),
                new XElement(rdf + "Description", new XAttribute(rdf + "about", ""),
                    new XAttribute(XNamespace.Xmlns + "pdfaid", aid), new XAttribute(aid + "part", "2"), new XAttribute(aid + "conformance", "B"),
                    new XAttribute(XNamespace.Xmlns + "pdf", pdf), new XAttribute(pdf + "Producer", Producer),
                    new XAttribute(XNamespace.Xmlns + "xmp", xmp), new XAttribute(xmp + "CreateDate", instant),
                    new XAttribute(xmp + "ModifyDate", instant), new XAttribute(xmp + "MetadataDate", instant),
                    new XAttribute(XNamespace.Xmlns + "dc", dc),
                    new XElement(dc + "format", "application/pdf"),
                    new XElement(dc + "title", new XElement(rdf + "Alt", new XElement(rdf + "li", new XAttribute(XNamespace.Xml + "lang", "x-default"), model.Title))),
                    new XElement(dc + "creator", new XElement(rdf + "Seq", new XElement(rdf + "li", model.Author)))))));
    }

    internal static Bookmarks Outlines(List<FixedPage> pages)
    {
        var roots = new List<OutlineNode>();
        var stack = new Stack<OutlineNode>();
        for (int page = 0; page < pages.Count; page++)
        {
            foreach (FixedHeading heading in pages[page].Headings)
            {
                var node = new OutlineNode(heading, page + 1);
                while (stack.Count > 0 && stack.Peek().Heading.Level >= heading.Level) stack.Pop();
                if (stack.Count == 0) roots.Add(node); else stack.Peek().Children.Add(node);
                stack.Push(node);
            }
        }
        return new Bookmarks(roots.Select(n => n.Build(0)).ToList());
    }

    private sealed class OutlineNode
    {
        internal OutlineNode(FixedHeading heading, int page) { Heading = heading; Page = page; }
        internal FixedHeading Heading { get; }
        private int Page { get; }
        internal List<OutlineNode> Children { get; } = new List<OutlineNode>();
        internal DocumentBookmarkNode Build(int depth) => new DocumentBookmarkNode(Heading.Title, depth,
            new ExplicitDestination(Page, ExplicitDestinationType.XyzCoordinates, new ExplicitDestinationCoordinates(Heading.X, Heading.Y)),
            Children.Select(c => c.Build(depth + 1)).ToList());
    }
}
