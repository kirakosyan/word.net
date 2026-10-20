using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Formats;

internal static class ChartObjectFormat
{
    internal static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace R = DocxPackage.DocumentRelationships;
    internal const string ChartType = "application/vnd.openxmlformats-officedocument.drawingml.chart+xml";
    internal const string WorkbookType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    internal static ObjectGraph Create(ChartDefinition definition, ConversionContext context)
    {
        context.CheckCancellation();
        long repeatedLabels = definition.Categories.Sum(s => (long)s.Length) * definition.Series.Count + definition.Series.Sum(s => (long)s.Name.Length) + definition.Title.Length;
        if (repeatedLabels > context.Options.MaximumCharacters) throw Invalid("XmlTextLimit");
        XElement V(string name, object value) => new XElement(C + name, new XAttribute("val", value));
        XElement Title(string text) => new XElement(C + "title", new XElement(C + "tx", new XElement(C + "rich", new XElement(A + "bodyPr"), new XElement(A + "lstStyle"),
            new XElement(A + "p", new XElement(A + "r", new XElement(A + "t", text))))));
        XElement Strings(IEnumerable<string> strings)
        {
            string[] list = strings.ToArray(); return new XElement(C + "strCache", V("ptCount", list.Length), list.Select((s, i) => new XElement(C + "pt", new XAttribute("idx", i), new XElement(C + "v", s))));
        }
        XElement Ref(string name, string range, IEnumerable<string> strings) => new XElement(C + name, new XElement(C + "strRef", new XElement(C + "f", "Data!" + range), Strings(strings)));
        int end = definition.Categories.Count + 1;
        XElement data = new XElement(C + (definition.Kind == ChartKind.Line ? "lineChart" : "barChart"),
            definition.Kind == ChartKind.ClusteredColumn ? V("barDir", "col") : null,
            V("grouping", definition.Kind == ChartKind.Line ? "standard" : "clustered"));
        for (int i = 0; i < definition.Series.Count; i++)
        {
            context.CheckCancellation(); ChartSeries series = definition.Series[i]; string column = ObjectWorkbook.Column(i + 2);
            var fill = new XElement(A + "solidFill", new XElement(A + "srgbClr", new XAttribute("val", series.Color)));
            data.Add(new XElement(C + "ser", V("idx", i), V("order", i), Ref("tx", "$" + column + "$1", new[] { series.Name }),
                new XElement(C + "spPr", definition.Kind == ChartKind.Line ? new XElement(A + "ln", new XAttribute("w", 12700), fill) : fill),
                Ref("cat", "$A$2:$A$" + end.ToString(CultureInfo.InvariantCulture), definition.Categories),
                new XElement(C + "val", new XElement(C + "numRef", new XElement(C + "f", "Data!$" + column + "$2:$" + column + "$" + end.ToString(CultureInfo.InvariantCulture)),
                    new XElement(C + "numCache", new XElement(C + "formatCode", "General"), V("ptCount", series.Values.Count),
                        series.Values.Select((v, p) => v.HasValue ? new XElement(C + "pt", new XAttribute("idx", p), new XElement(C + "v", v.Value.ToString("R", CultureInfo.InvariantCulture))) : null))))));
        }
        data.Add(V("axId", 1), V("axId", 2));
        XElement Axis(string kind, int id, int cross, string position) => new XElement(C + kind, V("axId", id),
            new XElement(C + "scaling", V("orientation", "minMax")), V("axPos", position),
            kind == "valAx" ? new XElement(C + "majorGridlines") : null,
            (kind == "catAx" ? definition.CategoryTitle : definition.ValueTitle) is string title && title.Length > 0 ? Title(title) : null,
            new XElement(C + "numFmt", new XAttribute("formatCode", "General"), new XAttribute("sourceLinked", 0)),
            V("majorTickMark", "none"), V("minorTickMark", "none"), V("tickLblPos", "nextTo"), V("crossAx", cross), V("crosses", "autoZero"),
            kind == "catAx" ? new object[] { V("auto", 1), V("lblAlgn", "ctr"), V("lblOffset", 100) } : new object[] { V("crossBetween", "between") });
        XElement chart = new XElement(C + "chart",
            definition.Title.Length == 0 ? null : Title(definition.Title),
            new XElement(C + "plotArea", new XElement(C + "layout"), data, Axis("catAx", 1, 2, "b"), Axis("valAx", 2, 1, "l")),
            definition.ShowLegend ? new XElement(C + "legend", V("legendPos", "r"), new XElement(C + "layout")) : null, V("plotVisOnly", 1), V("dispBlanksAs", "gap"));
        XElement root = new XElement(C + "chartSpace", new XAttribute(XNamespace.Xmlns + "c", C), new XAttribute(XNamespace.Xmlns + "a", A), new XAttribute(XNamespace.Xmlns + "r", R),
            chart, new XElement(C + "externalData", new XAttribute(R + "id", "data"), V("autoUpdate", 0)));
        const string name = "word/charts/chart.xml", book = "word/embeddings/data.xlsx";
        byte[] workbook = ObjectWorkbook.Write(definition, context); ObjectWorkbook admitted = ObjectWorkbook.Read(workbook, context);
        // Use the independent reader path for cache/range agreement before the edit can commit.
        _ = Parse(root, admitted, context);
        XElement rels = new XElement(DocxPackage.Relationships + "Relationships", new XElement(DocxPackage.Relationships + "Relationship",
            new XAttribute("Id", "data"), new XAttribute("Type", R.NamespaceName + "/package"), new XAttribute("Target", "../embeddings/data.xlsx")));
        byte[] chartXml = TextFormat.Utf8.GetBytes(root.ToString(SaveOptions.DisableFormatting));
        _ = PackageArchive.ParseXml(chartXml, context, reader => { _ = context.Objects.ScanXml(reader); });
        return new ObjectGraph(name, new[] { new ObjectPart(name, ChartType, chartXml),
            new ObjectPart(DocxPackage.RelationshipPartName(name), "application/vnd.openxmlformats-package.relationships+xml", TextFormat.Utf8.GetBytes(rels.ToString(SaveOptions.DisableFormatting))),
            new ObjectPart(book, WorkbookType, workbook) }, admitted.Expansion, admitted.Entries, admitted.Characters);
    }
    private static InvalidDataException Invalid(string code) => new InvalidDataException("Chart." + code + ": Invalid or unsupported chart graph/data.");
    internal static string Resolve(string owner, XElement relation)
    {
        if ((string?)relation.Attribute("TargetMode") is string mode && mode != "Internal") throw Invalid("ExternalRelationship");
        string target = DocxPackage.RequiredAttribute(relation, "Target");
        return target.Length > 0 && target[0] == '/' ? PackageArchive.Resolve("", target.Substring(1)) : PackageArchive.Resolve(owner, target);
    }
    internal static (ChartDefinition Definition, ObjectGraph Graph, bool Renderable) Read(DocxPackage package, string root, ConversionContext context)
    {
        try { return ReadCore(package, root, context); }
        catch (ArgumentException error) { throw new NotSupportedException("Chart.Profile: Chart text is outside the supported immutable chart profile.", error); }
    }
    private static (ChartDefinition Definition, ObjectGraph Graph, bool Renderable) ReadCore(DocxPackage package, string root, ConversionContext context)
    {
        XElement types = package.ReadXml("[Content_Types].xml").Root ?? throw Invalid("ContentTypes");
        if (types.Name != DocxPackage.ContentTypes + "Types" || package.PartNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != package.PartNames.Count()) throw Invalid("ContentTypes");
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal); var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement type in types.Elements())
        {
            if (type.Name == DocxPackage.ContentTypes + "Override")
            {
                string name = DocxPackage.RequiredAttribute(type, "PartName"), value = DocxPackage.RequiredAttribute(type, "ContentType");
                if (name.Length < 2 || name[0] != '/' || overrides.ContainsKey(name.Substring(1))) throw Invalid("ContentTypes"); overrides.Add(name.Substring(1), value);
            }
            else if (type.Name == DocxPackage.ContentTypes + "Default")
            {
                string extension = DocxPackage.RequiredAttribute(type, "Extension"), value = DocxPackage.RequiredAttribute(type, "ContentType");
                if (defaults.ContainsKey(extension)) throw Invalid("ContentTypes"); defaults.Add(extension, value);
            }
            else throw Invalid("ContentTypes");
        }
        string Type(string name) => overrides.TryGetValue(name, out string? exact) ? exact : defaults.TryGetValue(Path.GetExtension(name).TrimStart('.'), out string? fallback) ? fallback : throw Invalid("ContentTypes");
        var parts = new Dictionary<string, ObjectPart>(StringComparer.Ordinal); var documents = new Dictionary<string, XDocument>(StringComparer.Ordinal);
        var relations = new Dictionary<string, Dictionary<string, XElement>>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal); var insensitive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? workbookName = null; ObjectWorkbook? workbook = null; bool unsupportedNamespace = false;
        void Add(string name, string contentType, byte[] bytes)
        {
            if (parts.Count >= 32 || !insensitive.Add(name)) throw Invalid("PartLimit"); parts.Add(name, new ObjectPart(name, contentType, bytes));
        }
        void Visit(string name, string expected)
        {
            context.CheckCancellation(); if (!visiting.Add(name)) throw Invalid("Cycle");
            if (parts.ContainsKey(name)) { visiting.Remove(name); return; }
            if (Type(name) != expected) throw Invalid("ContentType");
            byte[] bytes = package.ReadBytes(name); Add(name, expected, bytes);
            if (expected == WorkbookType)
            {
                if (workbookName != null) throw Invalid("WorkbookCount"); workbookName = name; workbook = ObjectWorkbook.Read(bytes, context);
            }
            else if (expected is "image/png" or "image/jpeg")
            { DocumentImage image = DocumentImage.Read(bytes, null, null, "", context); if (image.MediaType != expected) throw Invalid("ImageType"); }
            else
            {
                XDocument xml = PackageArchive.ParseXml(bytes, context, reader => { _ = context.Objects.ScanXml(reader); }); documents.Add(name, xml);
                if (expected == ChartType && xml.Root?.Name != C + "chartSpace") throw Invalid("Root");
                XNamespace style = "http://schemas.microsoft.com/office/drawing/2012/chartStyle";
                if (expected == "application/vnd.ms-office.chartstyle+xml" && xml.Root?.Name != style + "chartStyle"
                    || expected == "application/vnd.ms-office.chartcolorstyle+xml" && xml.Root?.Name != style + "colorStyle") throw Invalid("Root");
                if (xml.Root == null) throw Invalid("Root");
                unsupportedNamespace |= xml.Descendants().Any(e => e.Name.Namespace != C && e.Name.Namespace != A && e.Name.Namespace != style);
            }
            string relName = DocxPackage.RelationshipPartName(name); var local = new Dictionary<string, XElement>(StringComparer.Ordinal); relations.Add(name, local);
            if (package.Contains(relName))
            {
                if (expected == WorkbookType || expected is "image/png" or "image/jpeg") throw Invalid("Owner");
                byte[] data = package.ReadBytes(relName); XDocument rels = PackageArchive.ParseXml(data, context, reader => { _ = context.Objects.ScanXml(reader); });
                if (rels.Root?.Name != DocxPackage.Relationships + "Relationships" || rels.Root.Elements().Take(33).Count() > 32) throw Invalid("Relationships");
                Add(relName, "application/vnd.openxmlformats-package.relationships+xml", data);
                foreach (XElement relation in rels.Root.Elements())
                {
                    if (relation.Name != DocxPackage.Relationships + "Relationship") throw Invalid("Relationships");
                    string id = DocxPackage.RequiredAttribute(relation, "Id"), role = DocxPackage.RequiredAttribute(relation, "Type");
                    if (local.ContainsKey(id)) throw Invalid("RelationshipId"); local.Add(id, relation);
                    string target = Resolve(name, relation); if (visiting.Contains(target)) throw Invalid("Cycle");
                    string nextType;
                    if (role == R.NamespaceName + "/package" && name == root) nextType = WorkbookType;
                    else if (role == "http://schemas.microsoft.com/office/2011/relationships/chartStyle" && name == root) nextType = "application/vnd.ms-office.chartstyle+xml";
                    else if (role == "http://schemas.microsoft.com/office/2011/relationships/chartColorStyle" && name == root) nextType = "application/vnd.ms-office.chartcolorstyle+xml";
                    else if (role == R.NamespaceName + "/image") { nextType = Type(target); if (nextType is not "image/png" and not "image/jpeg") throw Invalid("ImageType"); }
                    else throw Invalid("RelationshipType");
                    Visit(target, nextType);
                }
            }
            if (documents.TryGetValue(name, out XDocument? doc))
                foreach (XAttribute reference in doc.Descendants().Attributes().Where(a => a.Name.Namespace == R))
                    if (reference.Name.LocalName is not "id" and not "embed" || !local.ContainsKey(reference.Value)) throw Invalid("UnresolvedReference");
            visiting.Remove(name);
        }
        Visit(root, ChartType);
        XElement space = documents[root].Root!;
        XElement[] external = space.Elements(C + "externalData").ToArray();
        if (external.Length > 1 || (workbook != null) != (external.Length == 1)) throw Invalid("WorkbookOwner");
        if (external.Length == 1)
        {
            string id = DocxPackage.RequiredAttribute(external[0], R + "id");
            if (!relations[root].TryGetValue(id, out XElement? edge) || Resolve(root, edge) != workbookName) throw Invalid("WorkbookOwner");
        }
        if (unsupportedNamespace) throw new NotSupportedException("Chart.Namespace: Chart extensions are outside the supported preservation profile.");
        var parsed = Parse(space, workbook, context);
        bool simpleParts = parts.Values.All(p => p.ContentType is ChartType or WorkbookType or "application/vnd.openxmlformats-package.relationships+xml");
        return (parsed.Definition, new ObjectGraph(root, parts.Values, workbook?.Expansion ?? 0, workbook?.Entries ?? 0, workbook?.Characters ?? 0), parsed.Renderable && simpleParts);
    }
    private static (ChartDefinition Definition, bool Renderable) Parse(XElement root, ObjectWorkbook? workbook, ConversionContext context)
    {
        XElement chart = root.Element(C + "chart") ?? throw Invalid("Chart"); XElement plot = chart.Element(C + "plotArea") ?? throw Invalid("PlotArea");
        XElement[] kinds = plot.Elements().Where(e => e.Name.Namespace == C && e.Name.LocalName.EndsWith("Chart", StringComparison.Ordinal)).ToArray();
        if (kinds.Length != 1 || kinds[0].Name != C + "barChart" && kinds[0].Name != C + "lineChart") throw new NotSupportedException("Chart.Type: Only clustered columns and ordinary non-stacked lines are admitted.");
        XElement data = kinds[0]; ChartKind kind = data.Name == C + "barChart" ? ChartKind.ClusteredColumn : ChartKind.Line;
        string grouping = (string?)data.Element(C + "grouping")?.Attribute("val") ?? (kind == ChartKind.Line ? "standard" : "clustered");
        if (grouping != (kind == ChartKind.Line ? "standard" : "clustered") || kind == ChartKind.ClusteredColumn && (string?)data.Element(C + "barDir")?.Attribute("val") != "col") throw new NotSupportedException("Chart.Type: Stacked and horizontal charts are outside the profile.");
        XElement[] categories = plot.Elements(C + "catAx").ToArray(), values = plot.Elements(C + "valAx").ToArray();
        if (categories.Length != 1 || values.Length != 1 || plot.Elements().Count(e => e.Name.LocalName.EndsWith("Ax", StringComparison.Ordinal)) != 2
            || plot.Descendants(C + "logBase").Any() || data.Elements(C + "axId").Count() != 2) throw new NotSupportedException("Chart.Axes: One linear category/value axis pair is required.");
        string catId = DocxPackage.RequiredAttribute(categories[0].Element(C + "axId") ?? throw Invalid("Axes"), "val");
        string valId = DocxPackage.RequiredAttribute(values[0].Element(C + "axId") ?? throw Invalid("Axes"), "val");
        if (catId == valId || (string?)categories[0].Element(C + "crossAx")?.Attribute("val") != valId || (string?)values[0].Element(C + "crossAx")?.Attribute("val") != catId
            || !new HashSet<string>(data.Elements(C + "axId").Select(e => DocxPackage.RequiredAttribute(e, "val")), StringComparer.Ordinal).SetEquals(new[] { catId, valId })) throw Invalid("AxisReferences");
        XElement[] series = data.Elements(C + "ser").ToArray(); if (series.Length is < 1 or > 16) throw Invalid("SeriesLimit");
        XElement One(IEnumerable<XElement> elements) { XElement[] found = elements.Take(2).ToArray(); return found.Length == 1 ? found[0] : throw Invalid("Cardinality"); }
        long pointCount = series.Sum(s => (long)Count(One(s.Element(C + "val")?.Descendants(C + "ptCount") ?? Enumerable.Empty<XElement>())));
        if (pointCount > 10000) throw Invalid("PointLimit");
        bool renderable = !root.Descendants(C + "dLbls").Any() && !root.Descendants(C + "trendline").Any() && !root.Descendants(C + "errBars").Any()
            && !root.Descendants(C + "smooth").Any(e => (string?)e.Attribute("val") is not "0" and not "false")
            && !plot.Descendants(C + "scaling").Elements().Any(e => e.Name == C + "min" || e.Name == C + "max")
            && ((string?)chart.Element(C + "dispBlanksAs")?.Attribute("val") is null or "gap")
            && ((string?)chart.Element(C + "legend")?.Element(C + "legendPos")?.Attribute("val") is null or "r");
        renderable &= !plot.Descendants(C + "orientation").Any(e => (string?)e.Attribute("val") is not null and not "minMax")
            && !plot.Descendants(C + "crossesAt").Any() && !plot.Descendants(C + "delete").Any(e => (string?)e.Attribute("val") is "1" or "true")
            && !plot.Descendants(C + "tickLblPos").Any(e => (string?)e.Attribute("val") is not null and not "nextTo")
            && ((string?)categories[0].Element(C + "axPos")?.Attribute("val") is null or "b") && ((string?)values[0].Element(C + "axPos")?.Attribute("val") is null or "l")
            && !root.Descendants(C + "dTable").Any() && !root.Descendants(C + "dPt").Any() && !root.Descendants(C + "extLst").Any()
            && !root.Descendants(C + "symbol").Any(e => (string?)e.Attribute("val") is not null and not "none")
            && !root.Descendants(A + "gradFill").Any() && !root.Descendants(A + "blipFill").Any() && !root.Descendants(A + "effectLst").Any()
            && !root.Descendants(A + "srgbClr").Any(e => e.HasElements)
            && !root.Descendants(A + "rPr").Attributes().Any(a => !a.IsNamespaceDeclaration && a.Name.LocalName != "lang")
            && !root.Descendants(A + "defRPr").Attributes().Any(a => !a.IsNamespaceDeclaration && a.Name.LocalName != "lang");
        renderable &= !plot.Descendants(C + "majorUnit").Any() && !plot.Descendants(C + "minorUnit").Any() && !plot.Descendants(C + "minorGridlines").Any()
            && !categories[0].Elements().Concat(values[0].Elements()).Any(e => e.Name == C + "spPr" || e.Name == C + "txPr")
            && root.Element(C + "spPr") == null && root.Element(C + "txPr") == null
            && !root.Descendants(A + "prstDash").Any(e => (string?)e.Attribute("val") != "solid") && !root.Descendants(A + "custDash").Any()
            && !root.Descendants(A + "ln").Attributes().Any(a => !a.IsNamespaceDeclaration && a.Name.LocalName != "w")
            && !root.Descendants(C + "style").Any() && !root.Descendants(C + "manualLayout").Any() && !root.Descendants(A + "blip").Any()
            && (kind != ChartKind.ClusteredColumn || !series.Any(s => s.Element(C + "spPr")?.Element(A + "ln") is XElement line && (line.Element(A + "noFill") == null || line.Elements().Count() != 1)));
        var admitted = new List<ChartSeries>(); string[]? labels = null; var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (XElement item in series)
        {
            context.CheckCancellation(); string id = DocxPackage.RequiredAttribute(item.Element(C + "idx") ?? throw Invalid("SeriesId"), "val");
            if (!uint.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out _) || !identifiers.Add(id)) throw Invalid("SeriesId");
            XElement category = item.Element(C + "cat") ?? throw Invalid("CategoryCache");
            string[] current = Strings(category, workbook); if (labels != null && !labels.SequenceEqual(current, StringComparer.Ordinal)) throw Invalid("Categories"); labels = current;
            double?[] points = Numbers(item.Element(C + "val") ?? throw Invalid("ValueCache"), workbook);
            if (points.Length != labels.Length) throw Invalid("CacheCount");
            string name = ""; XElement? tx = item.Element(C + "tx");
            if (tx?.Element(C + "v") is XElement text) name = text.Value;
            else if (tx?.Element(C + "strRef") != null) { string[] names = Strings(tx, workbook); if (names.Length != 1) throw Invalid("SeriesName"); name = names[0]; }
            ObjectText.Check(name, "series", 1024);
            XElement? paint = kind == ChartKind.Line ? item.Element(C + "spPr")?.Element(A + "ln") : item.Element(C + "spPr");
            string? color = (string?)paint?.Element(A + "solidFill")?.Element(A + "srgbClr")?.Attribute("val");
            if (color == null) { renderable = false; color = "000000"; }
            if (color.Length != 6 || color.Any(c => !Uri.IsHexDigit(c))) throw Invalid("Color");
            if (kind == ChartKind.Line && paint?.Attribute("w") is XAttribute weight && weight.Value != "12700") renderable = false;
            admitted.Add(new ChartSeries(name, color, points));
        }
        string title = string.Concat(chart.Element(C + "title")?.Descendants(A + "t").Select(t => t.Value) ?? Enumerable.Empty<string>());
        ObjectText.Check(title, "title", 1024);
        string AxisTitle(XElement axis) => string.Concat(axis.Element(C + "title")?.Descendants(A + "t").Select(t => t.Value) ?? Enumerable.Empty<string>());
        return (new ChartDefinition(kind, labels!, admitted, title, chart.Element(C + "legend") != null, AxisTitle(categories[0]), AxisTitle(values[0])), renderable);
    }
    private static int Count(XElement count)
    { if (!int.TryParse((string?)count.Attribute("val"), NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value is < 1 or > 10000) throw Invalid("PointLimit"); return value; }
    private static XElement Cache(XElement holder, string literal, string reference, string cache, ObjectWorkbook? workbook, out string? formula)
    {
        XElement[] children = holder.Elements().ToArray(); if (children.Length != 1) throw Invalid("Cache"); XElement source = children[0]; formula = null;
        if (source.Name == C + literal) return source;
        if (source.Name != C + reference || workbook == null) throw Invalid("CacheOwner");
        formula = source.Element(C + "f")?.Value ?? throw Invalid("Range");
        if (formula.Length > 1024) throw Invalid("Range"); return source.Element(C + cache) ?? throw Invalid("Cache");
    }
    private static string[] Strings(XElement holder, ObjectWorkbook? workbook)
    {
        if (holder.Elements().Any(e => e.Name == C + "numRef" || e.Name == C + "numLit" || e.Name == C + "multiLvlStrRef"))
            throw new NotSupportedException("Chart.Categories: Numeric and multilevel category caches are outside the supported profile.");
        XElement cache = Cache(holder, "strLit", "strRef", "strCache", workbook, out string? formula);
        int count = Count(cache.Element(C + "ptCount") ?? throw Invalid("CacheCount")); var result = Enumerable.Repeat("", count).ToArray(); var seen = new HashSet<int>();
        foreach (XElement point in cache.Elements(C + "pt"))
        {
            int index = Index(point, count); if (!seen.Add(index)) throw Invalid("CacheIndex"); string value = point.Element(C + "v")?.Value ?? throw Invalid("CacheValue");
            ObjectText.Check(value, "label", 1024); result[index] = value;
        }
        if (formula != null)
        {
            object?[] cells = workbook!.Range(formula); if (cells.Length != count) throw Invalid("CacheRangeCount");
            for (int i = 0; i < count; i++) if (cells[i] is not null and not string || !string.Equals(cells[i] as string ?? "", result[i], StringComparison.Ordinal)) throw Invalid("CacheMismatch");
        }
        return result;
    }
    private static double?[] Numbers(XElement holder, ObjectWorkbook? workbook)
    {
        XElement cache = Cache(holder, "numLit", "numRef", "numCache", workbook, out string? formula);
        int count = Count(cache.Element(C + "ptCount") ?? throw Invalid("CacheCount")); var result = new double?[count]; var seen = new HashSet<int>();
        foreach (XElement point in cache.Elements(C + "pt"))
        {
            int index = Index(point, count); if (!seen.Add(index) || !double.TryParse(point.Element(C + "v")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || double.IsNaN(value) || double.IsInfinity(value)) throw Invalid("CacheValue");
            result[index] = value;
        }
        if (formula != null)
        {
            object?[] cells = workbook!.Range(formula); if (cells.Length != count) throw Invalid("CacheRangeCount");
            for (int i = 0; i < count; i++) if (cells[i] is double number ? result[i] != number : cells[i] != null || result[i] != null) throw Invalid("CacheMismatch");
        }
        return result;
    }
    private static int Index(XElement point, int count)
    { if (!int.TryParse((string?)point.Attribute("idx"), NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index < 0 || index >= count) throw Invalid("CacheIndex"); return index; }
}
