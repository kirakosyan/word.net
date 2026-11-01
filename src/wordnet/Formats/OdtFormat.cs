using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Formats;

internal static partial class OdtFormat
{
    private static readonly XNamespace O = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private static readonly XNamespace T = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private static readonly XNamespace S = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    private static readonly XNamespace F = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
    private static readonly XNamespace B = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private static readonly XNamespace D = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";
    private static readonly XNamespace V = "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0";
    private static readonly XNamespace L = "http://www.w3.org/1999/xlink";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static InvalidDataException Invalid(string code) => new InvalidDataException("Odt." + code + ": Invalid or unsupported ODT content.");
    private static int Count(XElement element, XName attribute, int fallback = 1)
    {
        string? text = (string?)element.Attribute(attribute);
        if (text == null) return fallback;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < 1 || value > 100000) throw Invalid("Count");
        return value;
    }
    private static int Start(XElement element, int fallback = 1)
    {
        string? text = (string?)element.Attribute(T + "start-value");
        if (text == null) return fallback;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < 0 || value == int.MaxValue) throw Invalid("ListStart"); return value;
    }
    private static double Length(string text)
    {
        if (text.Length < 3) throw Invalid("Unit");
        string unit = text.Substring(text.Length - 2);
#if NET8_0_OR_GREATER
        bool parsed = double.TryParse(text.AsSpan(0, text.Length - 2), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out double amount);
#else
        bool parsed = double.TryParse(text.Substring(0, text.Length - 2), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out double amount);
#endif
        if (!parsed
            || double.IsNaN(amount) || double.IsInfinity(amount)) throw Invalid("Unit");
        double result = amount * (unit switch { "pt" => 1, "in" => 72, "cm" => 72 / 2.54, "mm" => 72 / 25.4, "pc" => 12, _ => throw Invalid("Unit") });
        if (Math.Abs(result) > 1584) throw Invalid("Geometry"); return result;
    }
    private static string Points(double value) => value.ToString("0.#####", CultureInfo.InvariantCulture) + "pt";
    private static string? Color(string? color)
    {
        if (color == null || color == "transparent") return null;
        if (color.Length != 7 || color[0] != '#' || color.Skip(1).Any(c => !Uri.IsHexDigit(c))) throw Invalid("Color");
        return color.Substring(1).ToUpperInvariant();
    }
    internal static DocumentModel Read(Stream input, ConversionContext context, bool template = false)
    {
        using var package = new OdfPackage(input, context, template); return new Reader(package, context).Read();
    }
    private sealed class Reader
    {
        private readonly OdfPackage package;
        private readonly ConversionContext context;
        private readonly Dictionary<string, XElement> styles = new Dictionary<string, XElement>(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<XName, string>> resolved = new Dictionary<string, Dictionary<XName, string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, XElement> listStyles = new Dictionary<string, XElement>(StringComparer.Ordinal);
        private readonly Dictionary<string, PageSettings> masters = new Dictionary<string, PageSettings>(StringComparer.Ordinal);
        private readonly Dictionary<string, DocumentImage> images = new Dictionary<string, DocumentImage>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> listIds = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<XElement, string> listElements = new Dictionary<XElement, string>();
        private readonly Dictionary<string, string> previousLists = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> bookmarkNames = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> bookmarkStarts = new HashSet<string>(StringComparer.Ordinal), bookmarkEnds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> fontFamilies = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly HashSet<XName> SupportedProperties = new HashSet<XName>
        {
            F + "font-weight", F + "font-style", F + "font-size", F + "font-family", F + "color", F + "text-align",
            F + "margin-top", F + "margin-bottom", F + "margin-left", F + "margin-right", F + "text-indent", F + "break-before",
            S + "text-underline-style", S + "font-name", S + "master-page-name", S + "column-width", S + "width", S + "rel-width",
            F + "background-color", F + "padding", F + "padding-left", F + "padding-right", F + "padding-top", F + "padding-bottom",
            F + "border", F + "border-left", F + "border-right", F + "border-top", F + "border-bottom", S + "vertical-align",
        };
        private PageSettings? active;
        private long characters, nodes, cells;
        internal Reader(OdfPackage package, ConversionContext context) { this.package = package; this.context = context; }
        private void Admit(long count = 1, long text = 0)
        {
            context.CheckCancellation(); nodes = checked(nodes + count); characters = checked(characters + text);
            if (characters > context.Options.MaximumCharacters || nodes > context.Options.MaximumCharacters
                || characters * 2 + nodes * 128 > context.Options.MaximumExpandedBytes) throw Invalid("ResourceLimit");
        }
        internal DocumentModel Read()
        {
            XDocument content = package.Xml("content.xml");
            if (content.Root?.Name != O + "document-content" || (string?)content.Root.Attribute(O + "version") != "1.3") throw Invalid("Version");
            foreach (XElement marker in content.Descendants().Where(e => e.Name == T + "bookmark-start" || e.Name == T + "bookmark"))
            {
                string name = (string?)marker.Attribute(T + "name") ?? throw Invalid("Bookmark");
                if (name.Length is < 1 or > 256 || bookmarkNames.ContainsKey(name)) throw Invalid("Bookmark");
                bookmarkNames.Add(name, "odtb" + bookmarkNames.Count.ToString(CultureInfo.InvariantCulture));
            }
            var sources = new List<XDocument>(); if (package.Contains("styles.xml")) sources.Add(package.Xml("styles.xml")); sources.Add(content);
            foreach (XElement face in sources.SelectMany(x => x.Descendants(S + "font-face")))
            {
                string? name = (string?)face.Attribute(S + "name"), family = (string?)face.Attribute(V + "font-family");
                if (name != null && family != null) fontFamilies[name] = family;
                if (face.HasElements) Loss("FontLoss");
            }
            foreach (XElement element in sources.SelectMany(x => x.Descendants()).Where(e => e.Name == S + "style" || e.Name == S + "default-style"))
            {
                Admit();
                string family = (string?)element.Attribute(S + "family") ?? throw Invalid("Style");
                string name = (string?)element.Attribute(S + "name") ?? ""; string key = family + ":" + name;
                if (styles.Count >= 4096 || styles.ContainsKey(key)) throw Invalid("Style"); styles.Add(key, element);
                if (name.Length != 0 && element.Parent?.Name == O + "styles") Loss("StyleIdentityFlattened");
            }
            foreach (string key in styles.Keys) _ = Resolve(key, new HashSet<string>(StringComparer.Ordinal), 0);
            foreach (XElement element in sources.SelectMany(x => x.Descendants(T + "list-style")))
            {
                string name = (string?)element.Attribute(S + "name") ?? throw Invalid("ListStyle");
                if (listStyles.ContainsKey(name) || listStyles.Count >= 4096) throw Invalid("ListStyle"); listStyles.Add(name, element);
            }
            if (sources.Count > 1) ReadMasters(sources[0]);
            var document = new DocumentModel();
            XElement body = content.Root.Element(O + "body")?.Element(O + "text") ?? throw Invalid("Body");
            Blocks(body, document.Blocks, 0, bodyFlow: true);
            if (!bookmarkStarts.SetEquals(bookmarkEnds)) throw Invalid("Bookmark");
            document.FinalSection = active;
            if (package.Contains("meta.xml"))
            {
                XElement? meta = package.Xml("meta.xml").Root?.Element(O + "meta");
                document.Title = meta?.Element(Dc + "title")?.Value ?? ""; document.Author = meta?.Element(Dc + "creator")?.Value ?? "";
                Admit(text: document.Title.Length + document.Author.Length);
                if (meta?.Elements().Any(e => e.Name != Dc + "title" && e.Name != Dc + "creator") == true) Loss("MetadataLoss");
            }
            if (package.Contains("settings.xml") || package.Names.Any(n => n.StartsWith("Thumbnails/", StringComparison.Ordinal))) context.Report("Odt.SettingsIgnored", "Safe editor settings and thumbnails are omitted.");
            return document;
        }
        private Dictionary<XName, string> Resolve(string key, HashSet<string> visiting, int depth)
        {
            context.CheckCancellation();
            if (resolved.TryGetValue(key, out Dictionary<XName, string>? cached)) return cached;
            if (!styles.TryGetValue(key, out XElement? element) || depth > Math.Min(32, context.Options.MaximumNestingDepth) || !visiting.Add(key)) throw Invalid("StyleChain");
            string family = key.Split(':')[0]; var result = new Dictionary<XName, string>();
            string? parent = (string?)element.Attribute(S + "parent-style-name");
            string baseKey = parent != null ? family + ":" + parent : family + ":";
            if (baseKey != key && (parent != null || styles.ContainsKey(baseKey))) foreach (var pair in Resolve(baseKey, visiting, depth + 1)) result[pair.Key] = pair.Value;
            foreach (XAttribute attribute in element.Attributes().Where(a => a.Name == S + "master-page-name")) result[attribute.Name] = attribute.Value;
            foreach (XElement properties in element.Elements())
            {
                if (properties.Name.LocalName.EndsWith("-properties", StringComparison.Ordinal))
                    foreach (XAttribute attribute in properties.Attributes())
                        if (!attribute.IsNamespaceDeclaration)
                        {
                            if (!SupportedProperties.Contains(attribute.Name)) { Loss("StyleLoss"); continue; }
                            if (attribute.Name == S + "font-name")
                            {
                                if (fontFamilies.TryGetValue(attribute.Value, out string? familyName)) result[F + "font-family"] = familyName;
                                else Loss("FontLoss");
                            }
                            else result[attribute.Name] = attribute.Value;
                        }
                        else Loss("StyleLoss");
            }
            visiting.Remove(key); resolved.Add(key, result); return result;
        }
        private Dictionary<XName, string> Style(XElement element, string family)
        {
            string key = family + ":" + ((string?)element.Attribute(T + "style-name") ?? (string?)element.Attribute(B + "style-name") ?? "");
            if (resolved.TryGetValue(key, out Dictionary<XName, string>? result)) return result;
            if (key[key.Length - 1] == ':') return new Dictionary<XName, string>();
            throw Invalid("MissingStyle");
        }
        private void ReadMasters(XDocument source)
        {
            var layouts = new Dictionary<string, XElement>(StringComparer.Ordinal);
            foreach (XElement layout in source.Descendants(S + "page-layout"))
            {
                string name = (string?)layout.Attribute(S + "name") ?? throw Invalid("Master");
                if (layouts.Count >= 4096 || layouts.ContainsKey(name)) throw Invalid("Master"); layouts.Add(name, layout);
            }
            foreach (XElement master in source.Descendants(S + "master-page"))
            {
                Admit(); if (masters.Count >= context.Options.MaximumPages) throw Invalid("MasterLimit");
                string name = (string?)master.Attribute(S + "name") ?? throw Invalid("Master");
                string layoutName = (string?)master.Attribute(S + "page-layout-name") ?? throw Invalid("Master");
                if (masters.ContainsKey(name) || !layouts.TryGetValue(layoutName, out XElement? layout)) throw Invalid("Master");
                XElement properties = layout.Element(S + "page-layout-properties") ?? throw Invalid("Master");
                var page = new PageSettings();
                foreach (XAttribute attribute in properties.Attributes())
                {
                    if (attribute.Name == F + "page-width") page.Width = Length(attribute.Value);
                    else if (attribute.Name == F + "page-height") page.Height = Length(attribute.Value);
                    else if (attribute.Name == F + "margin-left") page.Left = Length(attribute.Value);
                    else if (attribute.Name == F + "margin-right") page.Right = Length(attribute.Value);
                    else if (attribute.Name == F + "margin-top") page.Top = Length(attribute.Value);
                    else if (attribute.Name == F + "margin-bottom") page.Bottom = Length(attribute.Value);
                    else if (attribute.Name == F + "margin") page.Left = page.Right = page.Top = page.Bottom = Length(attribute.Value);
                    else if (attribute.Name == S + "print-orientation") page.Landscape = attribute.Value == "landscape";
                    else if (!attribute.IsNamespaceDeclaration) Loss("SectionLoss");
                }
                if (properties.HasElements || master.Attribute(S + "next-style-name") != null) Loss("SectionLoss");
                page.Validate();
                foreach (string kind in new[] { "header", "footer" }) if (master.Element(S + kind) is XElement story)
                {
                    var blocks = new List<DocumentBlock>(); Blocks(story, blocks, 1);
                    page.Stories.Add(kind + "-default", blocks);
                }
                masters.Add(name, page);
            }
            active = masters.Values.FirstOrDefault();
        }
        private void Blocks(XElement container, List<DocumentBlock> output, int depth, ListItem? item = null, bool bodyFlow = false)
        {
            if (depth > context.Options.MaximumNestingDepth) throw Invalid("NestingLimit");
            foreach (XElement element in container.Elements())
            {
                Admit();
                if (element.Name == T + "p" || element.Name == T + "h")
                {
                    Dictionary<XName, string> appearance = Style(element, "paragraph");
                    if (bodyFlow && appearance.TryGetValue(S + "master-page-name", out string? master))
                    {
                        if (!masters.TryGetValue(master, out PageSettings? page)) throw Invalid("Master"); active = page;
                    }
                    var paragraph = new ParagraphBlock { Section = active, List = item?.Copy() };
                    if (element.Name == T + "h") paragraph.HeadingLevel = Math.Min(6, Count(element, T + "outline-level"));
                    foreach (var property in appearance)
                    {
                        if (property.Key == F + "text-align") paragraph.Alignment = property.Value switch { "left" or "start" => ParagraphAlignment.Left, "right" or "end" => ParagraphAlignment.Right, "center" => ParagraphAlignment.Center, "justify" => ParagraphAlignment.Justified, _ => throw Invalid("Alignment") };
                        else if (property.Key == F + "margin-top") paragraph.Layout.Before = Length(property.Value);
                        else if (property.Key == F + "margin-bottom") paragraph.Layout.After = Length(property.Value);
                        else if (property.Key == F + "margin-left") paragraph.Layout.Left = Length(property.Value);
                        else if (property.Key == F + "margin-right") paragraph.Layout.Right = Length(property.Value);
                        else if (property.Key == F + "text-indent") paragraph.Layout.FirstLine = Length(property.Value);
                        else if (property.Key == F + "break-before")
                        {
                            if (property.Value is not "auto" and not "page") Loss("SectionLoss");
                            paragraph.Layout.PageBreakBefore = property.Value == "page";
                        }
                    }
                    Inlines(element, paragraph.Runs, appearance); output.Add(paragraph); item = null;
                }
                else if (element.Name == T + "list") ReadList(element, output, depth, bodyFlow);
                else if (element.Name == B + "table")
                {
                    if (!bodyFlow) { Loss("NestedTableLoss"); foreach (XElement cell in element.Descendants(B + "table-cell")) Blocks(cell, output, depth + 1); }
                    else output.Add(Table(element, 0));
                }
                else if (element.Name == T + "sequence-decls" || element.Name == T + "variable-decls" || element.Name == T + "user-field-decls" || element.Name == T + "soft-page-break") { }
                else if (element.Name == T + "section") { Blocks(element, output, depth + 1, item, bodyFlow); item = null; }
                else if (element.Name == T + "tracked-changes") Loss("RevisionLoss");
                else if (element.Name == O + "annotation") Loss("AnnotationLoss");
                else if (element.Name == T + "note") Loss("NotesLoss");
                else Loss("FeatureLoss");
            }
        }
        private sealed class InlineSpace
        {
            internal bool Visible;
            internal TextRun? Pending;
        }
        private void Inlines(XElement element, List<TextRun> output, Dictionary<XName, string> inherited, string? link = null, InlineSpace? space = null)
        {
            space ??= new InlineSpace();
            Dictionary<XName, string> style = new Dictionary<XName, string>(inherited);
            if (element.Attribute(T + "style-name") != null && element.Name != T + "p" && element.Name != T + "h") foreach (var pair in Style(element, "text")) style[pair.Key] = pair.Value;
            foreach (XNode node in element.Nodes())
            {
                Admit();
                if (node is XText text) { Add(text.Value, ordinary: true); continue; }
                if (node is not XElement child) continue;
                if (child.Name == T + "s") { int count = Count(child, T + "c"); Admit(text: count); Add(new string(' ', count), false); }
                else if (child.Name == T + "tab") Add("\t");
                else if (child.Name == T + "line-break") Add("\n");
                else if (child.Name == T + "span") Inlines(child, output, style, link, space);
                else if (child.Name == T + "a")
                {
                    string? target = (string?)child.Attribute(L + "href");
                    if (target != null && target.Length > 0 && target[0] == '#')
                    {
                        if (!bookmarkNames.TryGetValue(target.Substring(1), out string? mapped)) throw Invalid("BookmarkLink");
                        Inlines(child, output, style, "#" + mapped, space);
                    }
                    else Inlines(child, output, style, context.SafeLink(target), space);
                }
                else if (child.Name == T + "bookmark-start" || child.Name == T + "bookmark-end" || child.Name == T + "bookmark")
                {
                    string name = (string?)child.Attribute(T + "name") ?? throw Invalid("Bookmark");
                    bool end = child.Name == T + "bookmark-end";
                    if (!bookmarkNames.TryGetValue(name, out string? mapped) || !(end ? bookmarkEnds : bookmarkStarts).Add(name)
                        || end && !bookmarkStarts.Contains(name)) throw Invalid("Bookmark");
                    output.Add(new TextRun("", bookmark: (end ? "/" : "") + mapped));
                    if (child.Name == T + "bookmark") { bookmarkEnds.Add(name); output.Add(new TextRun("", bookmark: "/" + mapped)); }
                }
                else if (child.Name == D + "frame")
                {
                    if ((string?)child.Attribute(T + "anchor-type") != "as-char" || child.Attribute(D + "transform") != null) Loss("DrawingLoss");
                    XElement? image = child.Element(D + "image");
                    if (image == null) { Loss("DrawingLoss"); Add(child.Element(V + "desc")?.Value ?? ""); continue; }
                    string path = OdfPackage.Resolve((string?)image.Attribute(L + "href") ?? throw Invalid("Image"));
                    string mediaType = package.ContentType(path);
                    if (mediaType is not "image/png" and not "image/jpeg")
                    {
                        if (mediaType == "image/svg+xml") _ = package.Xml(path);
                        Loss("DrawingLoss"); Add(child.Element(V + "desc")?.Value ?? ""); continue;
                    }
                    double width = Length((string?)child.Attribute(V + "width") ?? throw Invalid("Image")), height = Length((string?)child.Attribute(V + "height") ?? throw Invalid("Image"));
                    if (!images.TryGetValue(path, out DocumentImage? admitted))
                    {
                        byte[] bytes = package.Read(path); admitted = DocumentImage.Read(bytes, width, height, child.Element(V + "desc")?.Value ?? "", context);
                        if (admitted.Extension is not "png" and not "jpg") throw Invalid("Image"); package.RequireType(path, admitted.MediaType); images.Add(path, admitted);
                    }
                    FlushSpace(); space.Visible = true;
                    output.Add(new TextRun("", image: DocumentImage.Read(admitted.Bytes, width, height, child.Element(V + "desc")?.Value ?? "", context, admitted.PixelsWide, admitted.PixelsHigh)));
                }
                else if (child.Name == T + "soft-page-break") { }
                else if (child.Name == O + "annotation" || child.Name == O + "annotation-end") Loss("AnnotationLoss");
                else if (child.Name == T + "note") Loss("NotesLoss");
                else if (child.Name == T + "change" || child.Name == T + "change-start" || child.Name == T + "change-end") Loss("RevisionLoss");
                else if (child.Name.Namespace == T && child.Name.LocalName is "page-number" or "page-count" or "date" or "time" or "sequence" or "variable-get" or "user-field-get" or "text-input" or "file-name")
                { Loss("FieldLoss"); Inlines(child, output, style, link, space); }
                else Loss("FeatureLoss");
            }
            void FlushSpace()
            { if (space.Pending != null) { output.Add(space.Pending); space.Pending = null; } }
            void Add(string text, bool charge = true, bool ordinary = false)
            {
                if (charge) Admit(text: text.Length);
                bool bold = style.TryGetValue(F + "font-weight", out string? weight) && (weight == "bold" || weight == "700");
                bool italic = style.TryGetValue(F + "font-style", out string? slant) && slant == "italic";
                double? size = style.TryGetValue(F + "font-size", out string? points) ? Length(points) : null;
                if (size.HasValue && size <= 0) throw Invalid("FontSize");
                var run = new TextRun(text, bold, italic, link: link, fontSize: size, color: Color(style.TryGetValue(F + "color", out string? color) ? color : null));
                if (style.TryGetValue(F + "font-family", out string? family))
                {
                    if (family.Length > 256) throw Invalid("FontLimit"); Admit(text: family.Length);
                    run.Appearance.FontFamily = family.Trim('\'', '"');
                }
                if (style.TryGetValue(S + "text-underline-style", out string? underline)) run.Appearance.Underline = underline != "none";
                if (!ordinary) { FlushSpace(); output.Add(run); if (text.Length != 0) space.Visible = true; return; }
                var value = new StringBuilder(); TextRun gap = run.WithText(" ");
                foreach (char character in text)
                {
                    if (character is ' ' or '\t' or '\r' or '\n')
                    {
                        if (space.Visible && space.Pending == null) space.Pending = gap;
                    }
                    else
                    {
                        if (space.Pending != null)
                        {
                            if (ReferenceEquals(space.Pending, gap)) { value.Append(' '); space.Pending = null; }
                            else FlushSpace();
                        }
                        value.Append(character); space.Visible = true;
                    }
                }
                if (value.Length != 0) output.Add(run.WithText(value.ToString()));
            }
        }
        private void ReadList(XElement element, List<DocumentBlock> output, int depth, bool bodyFlow)
        {
            if (listElements.Count >= 4096) throw Invalid("ListLimit");
            int listLevel = element.Ancestors(T + "list").Count();
            if (listLevel >= 9) throw Invalid("ListDepth");
            string name = (string?)element.Attribute(T + "style-name") ?? element.Ancestors(T + "list").Select(e => (string?)e.Attribute(T + "style-name")).FirstOrDefault(n => n != null) ?? throw Invalid("ListStyle");
            if (!listStyles.TryGetValue(name, out XElement? style)) throw Invalid("ListStyle");
            var levels = Enumerable.Range(0, 9).Select(i => new ListLevel { Pattern = "%" + (i + 1).ToString(CultureInfo.InvariantCulture) + ".", RestartAfter = i - 1 }).ToArray();
            foreach (XElement definition in style.Elements())
            {
                int index = Count(definition, T + "level") - 1; if (index > 8) throw Invalid("ListDepth");
                if (definition.Name == T + "list-level-style-image") { Loss("ListLoss"); continue; }
                levels[index].Format = definition.Name == T + "list-level-style-bullet" ? "bullet" : (string?)definition.Attribute(S + "num-format") switch
                { "1" => "decimal", "a" => "lowerLetter", "A" => "upperLetter", "i" => "lowerRoman", "I" => "upperRoman", _ => "unsupported" };
                if (levels[index].Format == "unsupported") { Loss("ListLoss"); levels[index].Format = "decimal"; }
                levels[index].Pattern = levels[index].Format == "bullet" ? (string?)definition.Attribute(T + "bullet-char") ?? "•"
                    : ((string?)definition.Attribute(S + "num-prefix") ?? "") + "%" + (index + 1).ToString(CultureInfo.InvariantCulture) + ((string?)definition.Attribute(S + "num-suffix") ?? ".");
                levels[index].Start = Start(definition);
                if (levels[index].Start < 1) throw Invalid("ListStart");
                if (levels[index].Pattern.Length > 1024) throw Invalid("ListLabelLimit");
            }
            Admit(9, levels.Sum(level => (long)level.Pattern.Length));
            XElement? parentList = element.Ancestors(T + "list").FirstOrDefault();
            string id = parentList != null && listElements.TryGetValue(parentList, out string? parentId) ? parentId : context.NextListId();
            if (element.Attribute(T + "continue-list") is XAttribute continuation)
            { if (!listIds.TryGetValue(continuation.Value, out id!)) throw Invalid("ListContinuation"); }
            else if ((string?)element.Attribute(T + "continue-numbering") == "true")
            { if (!previousLists.TryGetValue(name, out id!)) throw Invalid("ListContinuation"); }
            if (element.Attribute(XNamespace.Xml + "id") is XAttribute xmlId)
            { if (listIds.ContainsKey(xmlId.Value)) throw Invalid("ListId"); listIds.Add(xmlId.Value, id); }
            foreach (XElement child in element.Elements())
            {
                if (child.Name != T + "list-item" && child.Name != T + "list-header") throw Invalid("List");
                if (child.Attribute(T + "start-value") != null)
                {
                    levels = levels.Select(level =>
                    {
                        var copy = new ListLevel
                        {
                            Format = level.Format,
                            Pattern = level.Pattern,
                            Start = level.Start,
                            RestartAfter = level.RestartAfter,
                            Legal = level.Legal,
                            Suffix = level.Suffix,
                            Layout = level.Layout.Copy(),
                            Appearance = level.Appearance
                        };
                        copy.LayoutProperties.UnionWith(level.LayoutProperties); return copy;
                    }).ToArray();
                    levels[listLevel].Start = Start(child); id = context.NextListId();
                }
                var item = new ListItem { Id = id, Level = listLevel, Ordered = levels[listLevel].Format != "bullet", Definition = levels, Start = Start(child, levels[listLevel].Start) };
                listElements[element] = id;
                Blocks(child, output, depth + 1, child.Name == T + "list-header" ? null : item, bodyFlow);
            }
            previousLists[name] = id;
            if (element.Attribute(XNamespace.Xml + "id") is XAttribute finalId) listIds[finalId.Value] = id;
        }
        private TableBlock Table(XElement element, int depth)
        {
            var table = new TableBlock { Section = active };
            var tableProperties = Style(element, "table");
            if (depth == 0 && tableProperties.TryGetValue(S + "master-page-name", out string? masterName))
            { if (!masters.TryGetValue(masterName, out PageSettings? page)) throw Invalid("Master"); active = page; table.Section = page; }
            if (tableProperties.TryGetValue(S + "width", out string? tableWidth)) table.Width = Length(tableWidth);
            if (table.Width.HasValue && table.Width.Value <= 0) throw Invalid("TableWidth");
            if (tableProperties.TryGetValue(S + "rel-width", out string? relative))
            {
                if (relative.Length < 2 || relative[relative.Length - 1] != '%' || !double.TryParse(relative.TrimEnd('%'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double percent)
                    || percent <= 0 || percent > 100) throw Invalid("TableWidth"); table.WidthPercent = percent;
            }
            foreach (XElement col in element.Elements(B + "table-column"))
            {
                int repeat = Count(col, B + "number-columns-repeated"); var properties = Style(col, "table-column");
                double width = properties.TryGetValue(S + "column-width", out string? value) ? Length(value) : 0;
                if (value != null && width <= 0) throw Invalid("TableWidth");
                if (repeat > 1024 - table.ColumnWidths.Count) throw Invalid("TableLimit");
                for (int i = 0; i < repeat; i++) { Admit(); table.ColumnWidths.Add(width); }
            }
            var rows = new List<XElement>(); var headers = new HashSet<XElement>();
            foreach (XElement row in element.Elements())
                if (row.Name == B + "table-row") rows.Add(row);
                else if (row.Name == B + "table-header-rows")
                {
                    if (rows.Count != 0) throw Invalid("TableHeaders");
                    rows.AddRange(row.Elements(B + "table-row")); headers.UnionWith(rows);
                }
                else if (row.Name != B + "table-column") Loss("TableLoss");
            var spans = new Dictionary<int, (int Remaining, int Width)>();
            foreach (XElement row in rows)
            {
                int rowRepeat = Count(row, B + "number-rows-repeated");
                long declaredCells = row.Elements().Sum(cell => (long)Count(cell, B + "number-columns-repeated"));
                if (declaredCells == 0) throw Invalid("TableCell");
                if (checked(declaredCells * rowRepeat) > Math.Min(100000 - cells, context.Options.MaximumExpandedBytes / 128)) throw Invalid("TableLimit");
                if (headers.Contains(row)) table.HeaderRowCount = checked(table.HeaderRowCount + rowRepeat);
                for (int repetition = 0; repetition < rowRepeat; repetition++)
                {
                    Admit();
                    var output = new List<TableCell>(); int column = 0;
                    var expected = new Dictionary<int, (int Remaining, int Width)>();
                    foreach (XElement cell in row.Elements())
                        for (int repeated = 0; repeated < Count(cell, B + "number-columns-repeated"); repeated++)
                        {
                            Admit(); if (++cells > 100000 || column >= 1024) throw Invalid("TableLimit");
                            if (cell.Name == B + "covered-table-cell" && (cell.HasElements || !string.IsNullOrWhiteSpace(cell.Value))) throw Invalid("CoveredCellContent");
                            if (spans.TryGetValue(column, out var above))
                            {
                                if (cell.Name != B + "covered-table-cell") throw Invalid("CoveredCell");
                                var continued = new TableCell(new ParagraphBlock()) { ColumnSpan = above.Width, Merge = VerticalMerge.Continue };
                                output.Add(continued); if (above.Remaining > 1) expected[column] = (above.Remaining - 1, above.Width);
                                column++; continue;
                            }
                            if (cell.Name == B + "covered-table-cell")
                            { if (output.Count == 0 || column >= output.Sum(c => c.ColumnSpan)) throw Invalid("CoveredCell"); column++; continue; }
                            if (cell.Name != B + "table-cell") throw Invalid("TableCell");
                            if (cell.Attribute(B + "formula") != null) Loss("FormulaLoss");
                            int width = Count(cell, B + "number-columns-spanned"), height = Count(cell, B + "number-rows-spanned");
                            if (width > 1024 - column || height > 100000 - table.Rows.Count) throw Invalid("TableLimit");
                            var target = new TableCell { ColumnSpan = width, Merge = height > 1 ? VerticalMerge.Start : VerticalMerge.None };
                            var appearance = Style(cell, "table-cell");
                            if (appearance.TryGetValue(F + "background-color", out string? background)) target.Background = Color(background);
                            if (appearance.TryGetValue(F + "padding", out string? padding)) target.Padding = new CellPadding { Left = Length(padding), Right = Length(padding), Top = Length(padding), Bottom = Length(padding) };
                            foreach (string side in new[] { "left", "right", "top", "bottom" })
                            {
                                if (appearance.TryGetValue(F + "padding-" + side, out string? amount))
                                {
                                    target.Padding ??= new CellPadding(); double value = Length(amount); if (value < 0) throw Invalid("CellPadding");
                                    switch (side) { case "left": target.Padding.Left = value; break; case "right": target.Padding.Right = value; break; case "top": target.Padding.Top = value; break; default: target.Padding.Bottom = value; break; }
                                }
                                string? border = appearance.TryGetValue(F + "border-" + side, out string? specific) ? specific : appearance.TryGetValue(F + "border", out string? all) ? all : null;
                                if (border != null && border != "none")
                                {
                                    string[] values = border.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                                    if (values.Length != 3) throw Invalid("CellBorder");
                                    double widthPoints = Length(values[0]); if (widthPoints <= 0) throw Invalid("CellBorder");
                                    if (widthPoints < 0.25 || widthPoints > 12) { Loss("TableLoss"); widthPoints = Math.Max(0.25, Math.Min(12, widthPoints)); }
                                    string style = values[1] switch { "solid" => "single", "double" => "double", "dotted" => "dotted", "dashed" => "dashed", _ => "unsupported" };
                                    if (style == "unsupported") { Loss("TableLoss"); style = "single"; }
                                    target.Borders.Add(side, new CellBorder { Width = widthPoints, Style = style, Color = Color(values[2]) ?? "000000" });
                                }
                            }
                            if (appearance.TryGetValue(S + "vertical-align", out string? alignment)) target.VerticalAlignment = alignment switch { "top" => "top", "middle" => "center", "bottom" => "bottom", _ => throw Invalid("CellAlignment") };
                            if (target.Padding != null && (target.Padding.Left < 0 || target.Padding.Right < 0 || target.Padding.Top < 0 || target.Padding.Bottom < 0)) throw Invalid("CellPadding");
                            Blocks(cell, target.Content, depth + 1); target.Paragraphs.AddRange(DocumentModel.CellParagraphs(target.Content));
                            if (target.Paragraphs.Count == 0)
                            {
                                var p = new ParagraphBlock();
                                string? cached = (string?)cell.Attribute(O + "string-value") ?? (string?)cell.Attribute(O + "value") ?? (string?)cell.Attribute(O + "boolean-value") ?? (string?)cell.Attribute(O + "date-value");
                                if (cached != null) { Loss("FieldLoss"); Admit(text: cached.Length); p.Runs.Add(new TextRun(cached)); }
                                target.Content.Add(p); target.Paragraphs.Add(p);
                            }
                            output.Add(target); if (height > 1) expected[column] = (height - 1, width); column++;
                        }
                    if (output.Sum(c => c.ColumnSpan) != column || spans.Keys.Any(c => c >= column)) throw Invalid("CoveredCell");
                    table.Rows.Add(output); spans = expected;
                }
            }
            if (spans.Count != 0) throw Invalid("TableSpan"); TableValidation.Validate(table, context); return table;
        }
        private void Loss(string code) => context.Loss("Odt." + code, "The ODT exchange omits unsupported content or semantics and retains only the admitted visible representation.");
    }
}
