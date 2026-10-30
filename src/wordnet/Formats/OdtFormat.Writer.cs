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

internal static partial class OdtFormat
{
    internal static void Write(DocumentModel document, Stream output, ConversionContext context) => new Writer(document, context).Write(output);
    internal static void InspectDestination(DocumentModel document, ConversionContext context) => _ = new Writer(document, context).Content();
    private sealed class Writer
    {
        private readonly DocumentModel model;
        private readonly ConversionContext context;
        private readonly XElement automatic = new XElement(O + "automatic-styles");
        private readonly XElement pageLayouts = new XElement(O + "automatic-styles");
        private readonly XElement masterStyles = new XElement(O + "master-styles");
        private readonly Dictionary<PageSettings, string> masters = new Dictionary<PageSettings, string>();
        private readonly Dictionary<string, string> appearances = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<(string Name, DocumentImage Image)> images = new List<(string, DocumentImage)>();
        private readonly Dictionary<string, string> lists = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> listInstances = new Dictionary<string, string>(StringComparer.Ordinal);
        private int listInstanceCount;
        private bool inMaster;
        private readonly HashSet<string> bookmarks = new HashSet<string>(StringComparer.Ordinal);
        internal Writer(DocumentModel model, ConversionContext context) { this.model = model; this.context = context; }
        internal XElement Content()
        {
            foreach (PageSettings settings in model.Blocks.Select(b => b.Section).Concat(new[] { model.FinalSection }).Where(s => s != null).Distinct().Cast<PageSettings>()) Master(settings);
            var body = new XElement(O + "text"); Blocks(model.Blocks, body);
            if (model.Notes.Count != 0)
            {
                Loss("NotesLoss");
                foreach (DocumentNote note in model.Notes) Blocks(note.Blocks, body, false);
            }
            if (bookmarks.Count != 0) throw Invalid("Bookmark");
            return body;
        }
        internal void Write(Stream output)
        {
            XElement body = Content();
            using var archive = new StoredZipWriter(output, context);
            string mediaType = context.Destination == DocumentFormat.Ott ? OdfPackage.TemplateMediaType : OdfPackage.MediaType;
            archive.Write("mimetype", Encoding.ASCII.GetBytes(mediaType), stored: true);
            var manifest = new XElement(OdfPackage.Manifest + "manifest", new XAttribute(OdfPackage.Manifest + "version", "1.3"),
                Declaration("/", mediaType), Declaration("content.xml", "text/xml"), Declaration("styles.xml", "text/xml"), Declaration("meta.xml", "text/xml"));
            Xml(archive, "content.xml", Root("document-content", automatic, new XElement(O + "body", body)));
            Xml(archive, "styles.xml", Root("document-styles", new XElement(O + "styles"), pageLayouts, masterStyles));
            Xml(archive, "meta.xml", Root("document-meta", new XElement(O + "meta", new XElement(Dc + "title", model.Title), new XElement(Dc + "creator", model.Author))));
            foreach (var image in images)
            { context.CheckCancellation(); archive.Write(image.Name, image.Image.Bytes); manifest.Add(Declaration(image.Name, image.Image.MediaType)); }
            Xml(archive, "META-INF/manifest.xml", manifest);
            archive.Complete();
        }
        private static XElement Root(string kind, params object[] children) => new XElement(O + kind,
            new XAttribute(XNamespace.Xmlns + "office", O), new XAttribute(XNamespace.Xmlns + "text", T), new XAttribute(XNamespace.Xmlns + "style", S),
            new XAttribute(XNamespace.Xmlns + "fo", F), new XAttribute(XNamespace.Xmlns + "table", B), new XAttribute(XNamespace.Xmlns + "draw", D),
            new XAttribute(XNamespace.Xmlns + "svg", V), new XAttribute(XNamespace.Xmlns + "xlink", L), new XAttribute(XNamespace.Xmlns + "dc", Dc), new XAttribute(O + "version", "1.3"), children);
        private static XElement Declaration(string path, string type) => new XElement(OdfPackage.Manifest + "file-entry",
            new XAttribute(OdfPackage.Manifest + "full-path", path), new XAttribute(OdfPackage.Manifest + "media-type", type));
        private void Xml(StoredZipWriter archive, string name, XElement root)
        {
            context.CheckCancellation(); byte[] bytes = Encoding.UTF8.GetBytes(new XDocument(root).ToString(SaveOptions.DisableFormatting));
            if (bytes.Length > context.Options.MaximumExpandedBytes) throw Invalid("ResourceLimit"); archive.Write(name, bytes);
        }
        private string Appearance(string family, XElement properties, string? master = null)
        {
            string key = (inMaster ? "master:" : "body:") + family + ":" + master + ":" + properties.ToString(SaveOptions.DisableFormatting);
            if (appearances.TryGetValue(key, out string? name)) return name;
            if (appearances.Count >= 4096) throw Invalid("StyleLimit");
            name = "s" + (appearances.Count + 1).ToString(CultureInfo.InvariantCulture); appearances.Add(key, name);
            (inMaster ? pageLayouts : automatic).Add(new XElement(S + "style", new XAttribute(S + "name", name), new XAttribute(S + "family", family),
                master == null ? null : new XAttribute(S + "master-page-name", master), properties)); return name;
        }
        private void Master(PageSettings page)
        {
            context.CheckCancellation(); page.Validate(); string name = "m" + (masters.Count + 1).ToString(CultureInfo.InvariantCulture); masters.Add(page, name);
            if (page.Columns != 1 || page.BreakType != "nextPage" || page.DifferentFirstPage || page.EvenAndOdd || page.PageNumberStart.HasValue) Loss("SectionLoss");
            pageLayouts.Add(new XElement(S + "page-layout", new XAttribute(S + "name", name), new XElement(S + "page-layout-properties",
                new XAttribute(F + "page-width", Points(page.Width)), new XAttribute(F + "page-height", Points(page.Height)),
                new XAttribute(F + "margin-left", Points(page.Left)), new XAttribute(F + "margin-right", Points(page.Right)),
                new XAttribute(F + "margin-top", Points(page.Top)), new XAttribute(F + "margin-bottom", Points(page.Bottom)),
                new XAttribute(S + "print-orientation", page.Landscape ? "landscape" : "portrait"))));
            var master = new XElement(S + "master-page", new XAttribute(S + "name", name), new XAttribute(S + "page-layout-name", name));
            foreach (var story in page.Stories)
            {
                if (story.Key is not "header-default" and not "footer-default") { Loss("SectionLoss"); continue; }
                var content = new XElement(S + (story.Key == "header-default" ? "header" : "footer"));
                inMaster = true;
                try { Blocks(story.Value, content, false); } finally { inMaster = false; }
                master.Add(content);
            }
            masterStyles.Add(master);
        }
        private void Blocks(IEnumerable<DocumentBlock> blocks, XElement parent, bool allowMaster = true)
        {
            var levels = new List<XElement>(); string? currentList = null; PageSettings? previous = null;
            foreach (DocumentBlock block in blocks)
            {
                context.CheckCancellation();
                string? master = allowMaster && block.Section != null && !ReferenceEquals(previous, block.Section) ? masters[block.Section] : null; previous = block.Section;
                if (block.Control != null) Loss("TemplateLoss");
                if (block is TableBlock table) { currentList = null; levels.Clear(); parent.Add(Table(table, master)); continue; }
                var paragraph = (ParagraphBlock)block;
                XElement element = Paragraph(paragraph, master);
                if (paragraph.List == null) { currentList = null; levels.Clear(); parent.Add(element); continue; }
                ListItem item = paragraph.List;
                if (currentList != item.Id) { currentList = item.Id; levels.Clear(); }
                while (levels.Count > item.Level + 1) levels.RemoveAt(levels.Count - 1);
                while (levels.Count <= item.Level)
                {
                    var list = new XElement(T + "list", new XAttribute(T + "style-name", ListStyle(item)));
                    string identity = (inMaster ? "master:" : "body:") + item.Id + ":" + levels.Count.ToString(CultureInfo.InvariantCulture);
                    string instance = "list" + (++listInstanceCount).ToString(CultureInfo.InvariantCulture);
                    list.Add(new XAttribute(XNamespace.Xml + "id", instance));
                    if (listInstances.TryGetValue(identity, out string? previousInstance)) list.Add(new XAttribute(T + "continue-list", previousInstance));
                    listInstances[identity] = instance;
                    if (levels.Count == 0) parent.Add(list);
                    else
                    {
                        XElement upper = levels[levels.Count - 1].Elements(T + "list-item").LastOrDefault() ?? new XElement(T + "list-item", new XElement(T + "p"));
                        if (upper.Parent == null) levels[levels.Count - 1].Add(upper); upper.Add(list);
                    }
                    levels.Add(list);
                }
                XElement destination = levels[item.Level];
                destination.Add(new XElement(T + "list-item", !destination.HasElements && item.Start != 1 ? new XAttribute(T + "start-value", item.Start) : null, element));
            }
        }
        private string ListStyle(ListItem item)
        {
            string key = (inMaster ? "master:" : "body:") + item.Id;
            if (lists.TryGetValue(key, out string? existing)) return existing;
            string name = "l" + (lists.Count + 1).ToString(CultureInfo.InvariantCulture); lists.Add(key, name);
            var list = new XElement(T + "list-style", new XAttribute(S + "name", name));
            for (int i = 0; i < 9; i++)
            {
                ListLevel? level = item.Definition?[i]; string format = level?.Format ?? (item.Ordered ? "decimal" : "bullet");
                var definition = new XElement(T + (format == "bullet" ? "list-level-style-bullet" : "list-level-style-number"), new XAttribute(T + "level", i + 1));
                if (format == "bullet") definition.Add(new XAttribute(T + "bullet-char", level?.Pattern ?? "•"));
                else
                {
                    string numbering = format switch { "decimal" => "1", "lowerLetter" => "a", "upperLetter" => "A", "lowerRoman" => "i", "upperRoman" => "I", _ => "1" };
                    if (format is not "decimal" and not "lowerLetter" and not "upperLetter" and not "lowerRoman" and not "upperRoman") Loss("ListLoss");
                    string marker = "%" + (i + 1).ToString(CultureInfo.InvariantCulture), pattern = level?.Pattern ?? marker + ".";
                    int position = pattern.IndexOf(marker, StringComparison.Ordinal);
                    if (position < 0 || pattern.Count(c => c == '%') != 1) { Loss("ListLoss"); pattern = marker + "."; position = 0; }
                    definition.Add(new XAttribute(S + "num-format", numbering), new XAttribute(S + "num-prefix", pattern.Substring(0, position)),
                        new XAttribute(S + "num-suffix", pattern.Substring(position + marker.Length)), new XAttribute(T + "start-value", Math.Max(1, level?.Start ?? 1)));
                }
                list.Add(definition);
            }
            (inMaster ? pageLayouts : automatic).Add(list); return name;
        }
        private XElement Paragraph(ParagraphBlock paragraph, string? master)
        {
            var properties = new XElement(S + "paragraph-properties", new XAttribute(F + "text-align", paragraph.Alignment switch
            { ParagraphAlignment.Left => "left", ParagraphAlignment.Right => "right", ParagraphAlignment.Center => "center", _ => "justify" }));
            if (paragraph.Layout.Before.HasValue) properties.Add(new XAttribute(F + "margin-top", Points(paragraph.Layout.Before.Value)));
            if (paragraph.Layout.After.HasValue) properties.Add(new XAttribute(F + "margin-bottom", Points(paragraph.Layout.After.Value)));
            properties.Add(new XAttribute(F + "margin-left", Points(paragraph.Layout.Left)), new XAttribute(F + "margin-right", Points(paragraph.Layout.Right)), new XAttribute(F + "text-indent", Points(paragraph.Layout.FirstLine)));
            if (paragraph.Layout.PageBreakBefore) properties.Add(new XAttribute(F + "break-before", "page"));
            if (paragraph.Layout.KeepTogether || paragraph.Layout.KeepWithNext || paragraph.Layout.LineSpacing.HasValue || paragraph.Layout.Tabs.Count > 0) Loss("ParagraphLoss");
            var element = new XElement(T + (paragraph.HeadingLevel == 0 ? "p" : "h"), new XAttribute(T + "style-name", Appearance("paragraph", properties, master)));
            if (paragraph.HeadingLevel != 0) element.Add(new XAttribute(T + "outline-level", paragraph.HeadingLevel));
            var runs = new List<TextRun>();
            foreach (TextRun run in paragraph.Runs)
                if (run.Box != null) { Loss("DrawingLoss"); runs.AddRange(FidelityChecks.FlattenBox(run.Box)); } else runs.Add(run);
            foreach (TextRun run in runs)
            {
                context.CheckCancellation();
                if (run.Bookmark != null)
                {
                    bool end = run.Bookmark.Length != 0 && run.Bookmark[0] == '/'; string name = end ? run.Bookmark.Substring(1) : run.Bookmark;
                    if (end ? !bookmarks.Remove(name) : !bookmarks.Add(name)) throw Invalid("Bookmark");
                    element.Add(new XElement(T + (end ? "bookmark-end" : "bookmark-start"), new XAttribute(T + "name", name))); continue;
                }
                if (run.Image != null)
                {
                    DocumentImage image = run.Image;
                    if (image.Transformed || image.Anchor != null) Loss("DrawingLoss");
                    string path = "Pictures/image" + (images.Count + 1).ToString(CultureInfo.InvariantCulture) + "." + image.Extension; images.Add((path, image));
                    element.Add(new XElement(D + "frame", new XAttribute(T + "anchor-type", "as-char"), new XAttribute(V + "width", Points(image.Width)), new XAttribute(V + "height", Points(image.Height)),
                        new XElement(D + "image", new XAttribute(L + "href", path), new XAttribute(L + "type", "simple"), new XAttribute(L + "show", "embed"), new XAttribute(L + "actuate", "onLoad")), new XElement(V + "desc", image.AlternativeText))); continue;
                }
                if (run.Box != null || run.Note != null) Loss("DrawingLoss");
                if (run.Control != null) Loss("TemplateLoss"); if (run.FieldInstruction != null) Loss("FieldLoss");
                var textProperties = new XElement(S + "text-properties", new XAttribute(F + "font-weight", run.Bold ? "bold" : "normal"), new XAttribute(F + "font-style", run.Italic ? "italic" : "normal"));
                if (run.FontSize.HasValue) textProperties.Add(new XAttribute(F + "font-size", Points(run.FontSize.Value)));
                if (run.Color != null) textProperties.Add(new XAttribute(F + "color", "#" + run.Color));
                if (run.Appearance.FontFamily != null) textProperties.Add(new XAttribute(F + "font-family", run.Appearance.FontFamily));
                if (run.Appearance.Underline) textProperties.Add(new XAttribute(S + "text-underline-style", "solid"));
                if (run.Appearance.Strike || run.Appearance.Background != null || run.Appearance.VerticalAlignment != null) Loss("StyleLoss");
                var span = new XElement(T + "span", new XAttribute(T + "style-name", Appearance("text", textProperties)));
                var plain = new StringBuilder();
                foreach (char c in run.Text)
                {
                    if (c is ' ' or '\t' or '\n' or '\f' or '\v')
                    {
                        if (plain.Length > 0) { span.Add(new XText(plain.ToString())); plain.Clear(); }
                        if (c is '\f' or '\v') Loss("BreakLoss");
                        span.Add(new XElement(T + (c == ' ' ? "s" : c == '\t' ? "tab" : "line-break")));
                    }
                    else plain.Append(c);
                }
                if (plain.Length > 0) span.Add(new XText(plain.ToString()));
                if (run.Link == null) element.Add(span);
                else element.Add(new XElement(T + "a", new XAttribute(L + "href", run.Link.Length > 0 && run.Link[0] == '#' ? run.Link : context.SafeLink(run.Link) ?? ""), new XAttribute(L + "type", "simple"), span));
            }
            return element;
        }
        private XElement Table(TableBlock table, string? master)
        {
            TableValidation.Validate(table, context);
            var properties = new XElement(S + "table-properties");
            if (table.Width.HasValue) properties.Add(new XAttribute(S + "width", Points(table.Width.Value)));
            if (table.WidthPercent.HasValue) properties.Add(new XAttribute(S + "rel-width", table.WidthPercent.Value.ToString("0.#####", CultureInfo.InvariantCulture) + "%"));
            if (table.AutoFit || table.LeftIndent != 0 || table.NonSplittingRows.Count != 0) Loss("TableLoss");
            var element = new XElement(B + "table", new XAttribute(B + "name", "Table" + (appearances.Count + 1).ToString(CultureInfo.InvariantCulture)), new XAttribute(B + "style-name", Appearance("table", properties, master)));
            foreach (double width in table.ColumnWidths) element.Add(new XElement(B + "table-column", new XAttribute(B + "style-name", Appearance("table-column", new XElement(S + "table-column-properties", new XAttribute(S + "column-width", Points(width)))))));
            var headers = new XElement(B + "table-header-rows"); if (table.HeaderRowCount > 0) element.Add(headers);
            for (int r = 0; r < table.Rows.Count; r++)
            {
                var row = new XElement(B + "table-row"); (r < table.HeaderRowCount ? headers : element).Add(row); int column = 0;
                foreach (TableCell cell in table.Rows[r])
                {
                    if (cell.Merge == VerticalMerge.Continue)
                    { for (int i = 0; i < cell.ColumnSpan; i++) row.Add(new XElement(B + "covered-table-cell")); column += cell.ColumnSpan; continue; }
                    var target = new XElement(B + "table-cell");
                    if (cell.ColumnSpan > 1) target.Add(new XAttribute(B + "number-columns-spanned", cell.ColumnSpan));
                    if (cell.Merge == VerticalMerge.Start)
                    {
                        int height = 1;
                        for (int i = r + 1; i < table.Rows.Count; i++)
                        {
                            int c = 0; TableCell? below = null;
                            foreach (TableCell candidate in table.Rows[i]) { if (c == column) below = candidate; c += candidate.ColumnSpan; }
                            if (below?.Merge != VerticalMerge.Continue) break; height++;
                        }
                        target.Add(new XAttribute(B + "number-rows-spanned", height));
                    }
                    var appearance = new XElement(S + "table-cell-properties");
                    if (cell.Background != null) appearance.Add(new XAttribute(F + "background-color", "#" + cell.Background));
                    if (cell.Padding != null)
                    {
                        appearance.Add(new XAttribute(F + "padding-left", Points(cell.Padding.Left)), new XAttribute(F + "padding-right", Points(cell.Padding.Right)),
                            new XAttribute(F + "padding-top", Points(cell.Padding.Top)), new XAttribute(F + "padding-bottom", Points(cell.Padding.Bottom)));
                    }
                    foreach (var border in cell.Borders)
                        appearance.Add(new XAttribute(F + "border-" + border.Key, border.Value.Width == 0 ? "none" : Points(border.Value.Width) + " " + (border.Value.Style == "single" ? "solid" : border.Value.Style) + " #" + border.Value.Color));
                    if (cell.VerticalAlignment != "top") appearance.Add(new XAttribute(S + "vertical-align", cell.VerticalAlignment == "center" ? "middle" : cell.VerticalAlignment));
                    target.Add(new XAttribute(B + "style-name", Appearance("table-cell", appearance)));
                    if (cell.Content.OfType<TableBlock>().Any()) Loss("NestedTableLoss");
                    Blocks(cell.Paragraphs, target, false);
                    row.Add(target); for (int i = 1; i < cell.ColumnSpan; i++) row.Add(new XElement(B + "covered-table-cell")); column += cell.ColumnSpan;
                }
            }
            return element;
        }
        private void Loss(string code) => context.Loss("Odt." + code, "The ODT exchange omits an unsupported feature while retaining its supported content.");
    }
}
