using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxReader
{
    private DocumentObjectData? ReadObjectForConversion(Func<DocumentObjectData?> read)
    {
        try { return read(); }
        catch (NotSupportedException) when (!context.Editing && !context.PreserveReview && context.EffectiveMode != FidelityMode.Strict)
        {
            context.Loss("Object.UnsupportedLoss", "An out-of-profile chart or equation is omitted during conversion; its content and appearance are not retained.");
            return null;
        }
    }
    private readonly Dictionary<string, (ChartDefinition Definition, ObjectGraph Graph, bool Renderable)> chartGraphs = new Dictionary<string, (ChartDefinition, ObjectGraph, bool)>(StringComparer.Ordinal);
    private DocumentObjectData? ReadRetainedDrawing(XElement drawing)
    {
        XElement[] charts = drawing.Descendants(ChartObjectFormat.C + "chart").ToArray(); if (charts.Length == 0) return null;
        if (charts.Length != 1) throw new InvalidDataException("Chart.Owner: A drawing must own exactly one chart.");
        ValidateObjectOwner(drawing);
        XElement inline = drawing.Element(Wp + "inline") ?? throw new NotSupportedException("Chart.Anchor: Only inline body/cell charts are admitted.");
        if (drawing.Elements().Count() != 1 || inline.Elements().GroupBy(e => e.Name).Any(g => g.Count() > 1)
            || inline.Elements().Any(e => e.Name != Wp + "extent" && e.Name != Wp + "effectExtent" && e.Name != Wp + "docPr" && e.Name != Wp + "cNvGraphicFramePr" && e.Name != A + "graphic")
            || inline.Attributes().Any(a => !a.IsNamespaceDeclaration
                && !(a.Name.NamespaceName == "http://schemas.microsoft.com/office/word/2010/wordprocessingDrawing" && a.Name.LocalName is "anchorId" or "editId" && a.Value.Length == 8 && a.Value.All(Uri.IsHexDigit))
                && (a.Name.Namespace != XNamespace.None || a.Name.LocalName is not "distT" and not "distB" and not "distL" and not "distR" || a.Value != "0"))
            || inline.Element(Wp + "effectExtent")?.Attributes().Any(a => !a.IsNamespaceDeclaration && a.Value != "0") == true)
            throw new NotSupportedException("Chart.Placement: Unsupported inline spacing, effects or wrapper metadata.");
        XElement? properties = inline.Element(Wp + "docPr");
        if (properties?.HasElements == true || (string?)properties?.Attribute("hidden") is not null and not "0" and not "false"
            || !string.IsNullOrEmpty((string?)properties?.Attribute("title")))
            throw new NotSupportedException("Chart.Placement: Hyperlinked, hidden or separately titled chart wrappers require explicit qualification.");
        XElement graphic = inline.Element(A + "graphic")?.Element(A + "graphicData") ?? throw new InvalidDataException("Chart.Owner: Missing graphic owner.");
        if ((string?)graphic.Attribute("uri") != ChartObjectFormat.C.NamespaceName || graphic.Elements().Count() != 1 || graphic.Elements().First() != charts[0]) throw new InvalidDataException("Chart.Owner: Ambiguous graphic owner.");
        XElement extent = inline.Element(Wp + "extent") ?? throw new InvalidDataException("Chart.Extent: Missing chart extent.");
        double width = Integer(extent, "cx", 1, 20116800) / 12700.0, height = Integer(extent, "cy", 1, 20116800) / 12700.0;
        string id = DocxPackage.RequiredAttribute(charts[0], DocxPackage.DocumentRelationships + "id");
        if (!relationships.TryGetValue(id, out XElement? relation) || (string?)relation.Attribute("Type") != DocxPackage.DocumentRelationships.NamespaceName + "/chart") throw new InvalidDataException("Chart.Owner: Missing chart relationship.");
        string root = ChartObjectFormat.Resolve(mainPart, relation);
        if (!chartGraphs.TryGetValue(root, out var chart))
        { chart = ChartObjectFormat.Read(package, root, context); chartGraphs.Add(root, chart); foreach (ObjectPart part in chart.Graph.Parts) knownParts.Add(part.Name); }
        string description = (string?)inline.Element(Wp + "docPr")?.Attribute("descr") ?? "";
        var item = new DocumentObjectData(chart.Definition, chart.Graph, width, height, description, renderable: chart.Renderable); context.Objects.Place(item); return item;
    }
    private void ValidateObjectOwner(XElement element)
    {
        if (fields.Count != 0 || element.Ancestors().Any(e => e.Name == W + "sdt" || e.Name == W + "fldSimple" || e.Name == W + "hyperlink"
            || e.Name == W + "ins" || e.Name == W + "del" || e.Name == W + "txbxContent" || e.Name == W + "hdr" || e.Name == W + "ftr" || e.Name == W + "footnote" || e.Name == W + "endnote" || e.Name == W + "comment"))
            throw new NotSupportedException("Object.Owner: Objects in unsupported stories, controls, fields, links or history are rejected.");
    }
    private DocumentObjectData ReadRetainedMath(XElement source)
    {
        ValidateObjectOwner(source); var parsed = MathObjectFormat.Read(source, context);
        string description = parsed.Expression.NodeCount <= 256 && parsed.Expression.Characters <= 2048 ? MathObjectFormat.Description(parsed.Expression) : "";
        if (description.Length > 4096) description = "";
        var item = new DocumentObjectData(parsed.Expression, parsed.Display, parsed.Size, description,
            retainedXml: source.ToString(SaveOptions.DisableFormatting), renderable: parsed.Renderable);
        context.Objects.Place(item); return item;
    }
}
