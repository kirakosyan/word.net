using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using WordNet.Internal;

namespace WordNet.Formats;

// Plain values and local rectangular references only: no spreadsheet evaluator.
internal sealed class ObjectWorkbook
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly Regex Reference = new Regex(@"^(?:'((?:[^']|'')+)'|([A-Za-z_][A-Za-z0-9_ ]*))!\$?([A-Z]{1,3})\$?([1-9][0-9]*)(?::\$?([A-Z]{1,3})\$?([1-9][0-9]*))?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private readonly Dictionary<(int Column, int Row), object?> values = new Dictionary<(int, int), object?>();
    internal string Sheet { get; private set; } = "";
    internal long Expansion { get; private set; }
    private static InvalidDataException Invalid(string part) => new InvalidDataException("Chart.Workbook" + part + ": Unsupported or invalid plain-value workbook.");
    internal static ObjectWorkbook Read(byte[] bytes, ConversionContext context)
    {
        var nested = new ConversionContext(context.Options, context.CancellationToken);
        using var stream = new MemoryStream(bytes, false); using var package = new DocxPackage(stream, nested);
        long expansion = nested.DeclaredExpandedBytes ?? throw Invalid("Size"); context.Objects.Workbook(0, expansion);
        if (package.PartNames.Count() > 32 || package.PartNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != package.PartNames.Count()) throw Invalid("Parts");
        var documents = new Dictionary<string, XDocument>(StringComparer.Ordinal); long characters = 0, nodes = 0;
        foreach (string part in package.PartNames)
        {
            context.CheckCancellation();
            if (!part.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && !part.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)) throw Invalid("PartType");
            byte[] data = package.ReadBytes(part); XDocument xml = PackageArchive.ParseXml(data, context); documents.Add(part, xml);
            foreach (XElement element in xml.Descendants())
            {
                context.CheckCancellation(); nodes++; characters = checked(characters + element.Attributes().Sum(a => (long)a.Value.Length));
                foreach (XText text in element.Nodes().OfType<XText>()) characters = checked(characters + text.Value.Length);
                if (characters > context.Options.MaximumCharacters) throw Invalid("ResourceLimit");
                context.Objects.Workbook(0, checked(expansion + nodes * 64 + characters * 2));
                if (element.Name.Namespace == S && element.Name.LocalName is "f" or "definedNames" or "externalReferences" or "connections" or "oleObjects" or "extLst"
                    || (string?)element.Attribute("hidden") is string hidden && hidden is not "0" and not "false") throw Invalid("ActiveContent");
            }
            if (part.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            {
                if (xml.Root?.Name != DocxPackage.Relationships + "Relationships") throw Invalid("Relationships");
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (XElement relation in xml.Root.Elements())
                {
                    string type = DocxPackage.RequiredAttribute(relation, "Type"), id = DocxPackage.RequiredAttribute(relation, "Id");
                    if (relation.Name != DocxPackage.Relationships + "Relationship" || !ids.Add(id) || (string?)relation.Attribute("TargetMode") is string mode && mode != "Internal"
                        || !(type == DocxPackage.DocumentRelationships.NamespaceName + "/officeDocument" || type == DocxPackage.DocumentRelationships.NamespaceName + "/worksheet"
                        || type == DocxPackage.DocumentRelationships.NamespaceName + "/styles" || type == DocxPackage.DocumentRelationships.NamespaceName + "/sharedStrings"
                        || type == DocxPackage.DocumentRelationships.NamespaceName + "/theme" || type == DocxPackage.DocumentRelationships.NamespaceName + "/extended-properties"
                        || type == "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties")) throw Invalid("Relationships");
                }
            }
        }
        XElement types = documents.TryGetValue("[Content_Types].xml", out XDocument? contentTypes) && contentTypes.Root?.Name == DocxPackage.ContentTypes + "Types" ? contentTypes.Root : throw Invalid("ContentTypes");
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "application/vnd.openxmlformats-package.relationships+xml", "application/xml", "text/xml",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml",
            "application/vnd.openxmlformats-officedocument.theme+xml", "application/vnd.openxmlformats-package.core-properties+xml",
            "application/vnd.openxmlformats-officedocument.extended-properties+xml"
        };
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal); var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement type in types.Elements())
        {
            string value = DocxPackage.RequiredAttribute(type, "ContentType"); if (!allowed.Contains(value)) throw Invalid("ContentTypes");
            if (type.Name == DocxPackage.ContentTypes + "Override")
            { string name = DocxPackage.RequiredAttribute(type, "PartName"); if (name.Length == 0 || name[0] != '/' || overrides.ContainsKey(name.Substring(1))) throw Invalid("ContentTypes"); overrides.Add(name.Substring(1), value); }
            else if (type.Name == DocxPackage.ContentTypes + "Default")
            { string extension = DocxPackage.RequiredAttribute(type, "Extension"); if (defaults.ContainsKey(extension)) throw Invalid("ContentTypes"); defaults.Add(extension, value); }
            else throw Invalid("ContentTypes");
        }
        string Type(string name) => overrides.TryGetValue(name, out string? exact) ? exact : defaults.TryGetValue(Path.GetExtension(name).TrimStart('.'), out string? fallback) ? fallback : throw Invalid("ContentTypes");
        Dictionary<string, XElement> Relations(string owner)
        {
            string name = owner.Length == 0 ? "_rels/.rels" : DocxPackage.RelationshipPartName(owner);
            return documents.TryGetValue(name, out XDocument? doc) ? doc.Root!.Elements().ToDictionary(e => DocxPackage.RequiredAttribute(e, "Id"), StringComparer.Ordinal) : new Dictionary<string, XElement>();
        }
        string Target(string owner, XElement relationship)
        {
            string target = DocxPackage.RequiredAttribute(relationship, "Target");
            string path = target.Length > 0 && target[0] == '/' ? PackageArchive.Resolve("", target.Substring(1)) : PackageArchive.Resolve(owner, target);
            if (!documents.ContainsKey(path)) throw Invalid("MissingPart"); return path;
        }
        Dictionary<string, XElement> root = Relations("");
        XElement[] office = root.Values.Where(e => (string?)e.Attribute("Type") == DocxPackage.DocumentRelationships.NamespaceName + "/officeDocument").ToArray();
        if (office.Length != 1) throw Invalid("Owner"); string workbook = Target("", office[0]);
        if (Type(workbook) != "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml") throw Invalid("ContentTypes");
        XDocument book = documents[workbook]; if (book.Root?.Name != S + "workbook") throw Invalid("Root");
        XElement[] sheets = book.Root.Element(S + "sheets")?.Elements(S + "sheet").ToArray() ?? Array.Empty<XElement>();
        if (sheets.Length != 1 || (string?)sheets[0].Attribute("state") is string state && state != "visible") throw Invalid("SheetCount");
        var result = new ObjectWorkbook { Sheet = DocxPackage.RequiredAttribute(sheets[0], "name"), Expansion = expansion };
        ObjectText.Check(result.Sheet, "sheet", 31);
        Dictionary<string, XElement> relations = Relations(workbook);
        if (relations.Values.Count(r => (string?)r.Attribute("Type") == DocxPackage.DocumentRelationships.NamespaceName + "/worksheet") != 1
            || relations.Values.GroupBy(r => (string?)r.Attribute("Type"), StringComparer.Ordinal).Any(g => g.Count() != 1)) throw Invalid("SheetOwner");
        string sheetId = DocxPackage.RequiredAttribute(sheets[0], DocxPackage.DocumentRelationships + "id");
        if (!relations.TryGetValue(sheetId, out XElement? sheetRelation) || (string?)sheetRelation.Attribute("Type") != DocxPackage.DocumentRelationships.NamespaceName + "/worksheet") throw Invalid("SheetOwner");
        string sheetName = Target(workbook, sheetRelation);
        if (Type(sheetName) != "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml") throw Invalid("ContentTypes");
        var reached = new HashSet<string>(StringComparer.Ordinal) { "[Content_Types].xml" }; var visiting = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string owner)
        {
            if (!visiting.Add(owner)) throw Invalid("Cycle");
            string relName = owner.Length == 0 ? "_rels/.rels" : DocxPackage.RelationshipPartName(owner); if (documents.ContainsKey(relName)) reached.Add(relName);
            foreach (XElement relation in Relations(owner).Values)
            {
                string target = Target(owner, relation); if (visiting.Contains(target)) throw Invalid("Cycle");
                string role = DocxPackage.RequiredAttribute(relation, "Type"); role = role.Substring(role.LastIndexOf('/') + 1);
                string expected = role switch
                {
                    "officeDocument" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml",
                    "worksheet" => "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml",
                    "styles" => "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml",
                    "sharedStrings" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml",
                    "theme" => "application/vnd.openxmlformats-officedocument.theme+xml",
                    "core-properties" => "application/vnd.openxmlformats-package.core-properties+xml",
                    "extended-properties" => "application/vnd.openxmlformats-officedocument.extended-properties+xml",
                    _ => throw Invalid("RelationshipType")
                };
                if (Type(target) != expected || owner.Length == 0 && role is not "officeDocument" and not "core-properties" and not "extended-properties"
                    || owner.Length != 0 && (owner != workbook || role is not "worksheet" and not "styles" and not "sharedStrings" and not "theme")) throw Invalid("Owner");
                XName rootName = role switch
                {
                    "officeDocument" => S + "workbook",
                    "worksheet" => S + "worksheet",
                    "styles" => S + "styleSheet",
                    "sharedStrings" => S + "sst",
                    "theme" => XName.Get("theme", "http://schemas.openxmlformats.org/drawingml/2006/main"),
                    "core-properties" => XName.Get("coreProperties", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties"),
                    _ => XName.Get("Properties", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties")
                };
                if (documents[target].Root?.Name != rootName) throw Invalid("PartRoot");
                if (reached.Add(target)) Visit(target);
            }
            visiting.Remove(owner);
        }
        Visit(""); if (!reached.SetEquals(documents.Keys)) throw Invalid("UnownedPart");
        foreach (string name in documents.Keys.Where(n => n != "[Content_Types].xml")) _ = Type(name);
        var strings = new List<string>();
        foreach (XElement shared in relations.Values.Where(e => (string?)e.Attribute("Type") == DocxPackage.DocumentRelationships.NamespaceName + "/sharedStrings"))
        {
            foreach (XElement item in documents[Target(workbook, shared)].Root?.Elements(S + "si") ?? Enumerable.Empty<XElement>())
            { if (strings.Count >= 100000) throw Invalid("StringLimit"); string value = string.Concat(item.Descendants(S + "t").Select(t => t.Value)); ObjectText.Check(value, "value", 1024); strings.Add(value); }
        }
        XElement sheet = documents[sheetName].Root ?? throw Invalid("SheetRoot"); if (sheet.Name != S + "worksheet") throw Invalid("SheetRoot");
        if (sheet.Element(S + "dimension")?.Attribute("ref") is XAttribute dimension) _ = Rectangle(dimension.Value, 100000);
        foreach (XElement cell in sheet.Descendants(S + "c"))
        {
            context.CheckCancellation(); string reference = DocxPackage.RequiredAttribute(cell, "r");
            var rectangle = Rectangle(reference, 1); var coordinate = (rectangle.Left, rectangle.Top);
            if (result.values.ContainsKey(coordinate) || result.values.Count >= 100000) throw Invalid("CellCount");
            string? type = (string?)cell.Attribute("t"), value = cell.Element(S + "v")?.Value; object? admitted;
            if (type == "inlineStr") admitted = string.Concat(cell.Element(S + "is")?.Descendants(S + "t").Select(t => t.Value) ?? Enumerable.Empty<string>());
            else if (type == "s")
            { if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index < 0 || index >= strings.Count) throw Invalid("SharedString"); admitted = strings[index]; }
            else if (type is null or "n")
            { if (value == null) admitted = null; else { if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || double.IsNaN(number) || double.IsInfinity(number)) throw Invalid("Number"); admitted = number; } }
            else throw Invalid("CellType");
            if (admitted is string text) ObjectText.Check(text, "value", 1024);
            result.values.Add(coordinate, admitted);
        }
        result.Expansion = checked(expansion + nodes * 64 + characters * 2);
        context.Objects.Workbook(result.values.Count, result.Expansion); return result;
    }
    internal object?[] Range(string formula)
    {
        Match match = Reference.Match(formula);
        if (!match.Success || (match.Groups[1].Success ? match.Groups[1].Value.Replace("''", "'") : match.Groups[2].Value) != Sheet) throw Invalid("Range");
        string range = match.Groups[3].Value + match.Groups[4].Value + (match.Groups[5].Success ? ":" + match.Groups[5].Value + match.Groups[6].Value : "");
        var rectangle = Rectangle(range, 10000);
        if (rectangle.Right != rectangle.Left && rectangle.Top != rectangle.Bottom) throw Invalid("RangeShape");
        var data = new List<object?>();
        for (int row = rectangle.Top; row <= rectangle.Bottom; row++) for (int column = rectangle.Left; column <= rectangle.Right; column++)
            data.Add(values.TryGetValue((column, row), out object? value) ? value : null);
        return data.ToArray();
    }
    internal static string Column(int column)
    {
        if (column is < 1 or > 16384) throw Invalid("Range");
        string name = "";
        while (column > 0) { column--; name = (char)('A' + column % 26) + name; column /= 26; }
        return name;
    }
    internal static byte[] Write(ChartDefinition chart, ConversionContext context)
    {
        using var output = new BoundedMemoryStream(context.Options.MaximumExpandedBytes);
        using var zip = new StoredZipWriter(output, context);
        XNamespace rel = DocxPackage.Relationships, types = DocxPackage.ContentTypes, r = DocxPackage.DocumentRelationships;
        var contentTypes = new XElement(types + "Types",
            new XElement(types + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(types + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            new XElement(types + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
            new XElement(types + "Override", new XAttribute("PartName", "/xl/worksheets/sheet1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
        Xml("[Content_Types].xml", contentTypes);
        XElement Relationship(string id, string role, string target) => new XElement(rel + "Relationship", new XAttribute("Id", id), new XAttribute("Type", r.NamespaceName + "/" + role), new XAttribute("Target", target));
        Xml("_rels/.rels", new XElement(rel + "Relationships", Relationship("workbook", "officeDocument", "xl/workbook.xml")));
        Xml("xl/workbook.xml", new XElement(S + "workbook", new XAttribute(XNamespace.Xmlns + "r", r),
            new XElement(S + "sheets", new XElement(S + "sheet", new XAttribute("name", "Data"), new XAttribute("sheetId", 1), new XAttribute(r + "id", "sheet")))));
        Xml("xl/_rels/workbook.xml.rels", new XElement(rel + "Relationships", Relationship("sheet", "worksheet", "worksheets/sheet1.xml")));
        var rows = new XElement(S + "sheetData");
        var header = new XElement(S + "row", new XAttribute("r", 1));
        header.Add(StringCell("A1", "Category"));
        for (int series = 0; series < chart.Series.Count; series++) header.Add(StringCell(Column(series + 2) + "1", chart.Series[series].Name));
        rows.Add(header);
        for (int index = 0; index < chart.Categories.Count; index++)
        {
            context.CheckCancellation(); string position = (index + 2).ToString(CultureInfo.InvariantCulture);
            var row = new XElement(S + "row", new XAttribute("r", position), StringCell("A" + position, chart.Categories[index]));
            for (int series = 0; series < chart.Series.Count; series++)
                if (chart.Series[series].Values[index] is double value) row.Add(new XElement(S + "c", new XAttribute("r", Column(series + 2) + position),
                    new XElement(S + "v", value.ToString("R", CultureInfo.InvariantCulture))));
            rows.Add(row);
        }
        Xml("xl/worksheets/sheet1.xml", new XElement(S + "worksheet", new XElement(S + "dimension", new XAttribute("ref", "A1:" + Column(chart.Series.Count + 1) + (chart.Categories.Count + 1).ToString(CultureInfo.InvariantCulture))), rows));
        zip.Complete(); return output.ToArray();
        void Xml(string name, XElement value) => zip.Write(name, Encoding.UTF8.GetBytes(value.ToString(SaveOptions.DisableFormatting)));
        XElement StringCell(string reference, string text) => new XElement(S + "c", new XAttribute("r", reference), new XAttribute("t", "inlineStr"), new XElement(S + "is", new XElement(S + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text)));
    }
    private static (int Left, int Top, int Right, int Bottom) Rectangle(string reference, int maximum)
    {
        string[] ends = reference.Split(':'); if (ends.Length is < 1 or > 2) throw Invalid("Range");
        (int Column, int Row) Cell(string text)
        {
            int position = 0, column = 0;
            while (position < text.Length && text[position] is >= 'A' and <= 'Z') { if (position >= 3) throw Invalid("Range"); column = checked(column * 26 + text[position++] - 'A' + 1); }
#if NET8_0_OR_GREATER
            bool parsed = int.TryParse(text.AsSpan(position), NumberStyles.None, CultureInfo.InvariantCulture, out int row);
#else
            bool parsed = int.TryParse(text.Substring(position), NumberStyles.None, CultureInfo.InvariantCulture, out int row);
#endif
            if (position == 0 || !parsed || row is < 1 or > 1048576 || column is < 1 or > 16384) throw Invalid("Range");
            return (column, row);
        }
        var a = Cell(ends[0]); var b = Cell(ends[ends.Length - 1]);
        if (b.Column < a.Column || b.Row < a.Row || (long)(b.Column - a.Column + 1) * (b.Row - a.Row + 1) > maximum) throw Invalid("RangeLimit");
        return (a.Column, a.Row, b.Column, b.Row);
    }
}
