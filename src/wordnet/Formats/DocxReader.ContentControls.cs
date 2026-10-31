using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxReader
{
    private readonly ContentControlStore typedControls = new ContentControlStore();
    private Dictionary<XElement, ControlData> typedNodes = new Dictionary<XElement, ControlData>();
    private readonly Dictionary<string, XDocument> typedStorySources = new Dictionary<string, XDocument>(StringComparer.Ordinal);
    private static readonly XNamespace Cx = "http://schemas.openxmlformats.org/officeDocument/2006/customXml";
    private void PrepareTypedControls(XDocument source)
    {
        if (!context.PreserveControls) return;
        typedControls.DateMode = context.ControlDateMode;
        var sources = new List<(XDocument Document, string Part, bool Story)> { (source, mainPart, false) };
        var storyKinds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (XElement reference in source.Descendants(W + "sectPr").Elements().Where(element => element.Name == W + "headerReference" || element.Name == W + "footerReference"))
        {
            context.ReserveControlWork(1);
            string id = DocxPackage.RequiredAttribute(reference, DocxPackage.DocumentRelationships + "id");
            string kind = reference.Name == W + "headerReference" ? "header" : "footer";
            if (!relationships.TryGetValue(id, out XElement? relation)
                || (string?)relation.Attribute("Type") != DocxPackage.DocumentRelationships.NamespaceName + "/" + kind)
                throw new InvalidDataException("Docx.HeaderFooter: A header/footer relationship is missing or has the wrong type.");
            string part = DocxPackage.ResolvePart(mainPart, relation);
            if (storyKinds.TryGetValue(part, out string? previous))
            {
                if (previous != kind) throw new InvalidDataException("Docx.HeaderFooter: A story part cannot be both header and footer.");
                continue;
            }
            XDocument story = package.ReadXml(part);
            if (story.Root?.Name != W + (kind == "header" ? "hdr" : "ftr")) throw new InvalidDataException("Docx.HeaderFooter: Invalid story root.");
            storyKinds.Add(part, kind); typedStorySources.Add(part, story); sources.Add((story, part, true));
        }
        var nodes = sources.SelectMany(item => item.Document.Descendants(W + "sdt").Select(node => (Node: node, item.Part, item.Story))).ToArray();
        if (nodes.Length == 0) return;
        if (nodes.Length > 1024) throw new InvalidDataException("Limit.Controls: At most 1024 typed controls are admitted.");
        var ids = new HashSet<int>();
        foreach (var entry in nodes)
        {
            XElement node = entry.Node;
            context.CheckCancellation(); Attributes(node);
            XElement[] props = node.Elements(W + "sdtPr").ToArray();
            if (props.Length != 1 || node.Elements(W + "sdtContent").Count() != 1 || node.Elements(W + "sdtEndPr").Count() > 1
                || node.Elements().Any(e => e.Name != W + "sdtPr" && e.Name != W + "sdtContent" && e.Name != W + "sdtEndPr"))
                throw new InvalidDataException("Control.Malformed: A control requires unique properties/content and optional end properties.");
            XElement[] identity = props[0].Elements(W + "id").ToArray();
            if (identity.Length > 1 || identity.Length == 1 && !ids.Add(Integer(identity[0], W + "val", int.MinValue, int.MaxValue)))
                throw new InvalidDataException("Control.DuplicateId: Control IDs must be unique signed integers.");
        }
        int next = 1;
        foreach (var entry in nodes)
        {
            XElement node = entry.Node;
            using var location = context.At(XmlSourceLocation.Create(node, entry.Part));
            XElement properties = node.Element(W + "sdtPr")!;
            int id;
            if (properties.Element(W + "id") is XElement identity) id = Integer(identity, W + "val", int.MinValue, int.MaxValue);
            else
            {
                while (ids.Contains(next)) next = checked(next + 1);
                id = next; ids.Add(id); context.Report("Control.IdAssigned", "A missing content-control ID was assigned deterministically in document order.");
            }
            bool inline = node.Parent?.Name == W + "p";
            XElement[] parents = node.Ancestors(W + "sdt").ToArray();
            if (parents.Length + 1 > context.Options.MaximumNestingDepth)
                throw new InvalidDataException("Limit.NestingDepth: Typed controls exceed the nesting limit.");
            if (parents.Length != 0 && (parents.Length != 1 || !inline || node.Parent?.Parent != parents[0].Element(W + "sdtContent")
                || !typedNodes.TryGetValue(parents[0], out ControlData? parent) || parent.Inline || parent.Kind != ContentControlKind.RichText
                || parent.Placeholder || parent.Binding != null))
                throw new NotSupportedException("Control.ProtectedOwner: Nested controls require a non-placeholder, unbound block rich-text parent.");
            if (entry.Story && (parents.Length != 0 || (inline ? node.Parent?.Parent != node.Document?.Root : node.Parent != node.Document?.Root))
                || !entry.Story && (!inline && node.Parent?.Name != W + "body" && node.Parent?.Name != W + "tc" || !node.Ancestors(W + "body").Any())
                || node.Ancestors().Any(a => a.Name == W + "fldSimple" || a.Name == W + "txbxContent"
                    || a.Name == W + "ins" || a.Name == W + "del" || a.Name == W + "moveFrom" || a.Name == W + "moveTo")
                )
                throw new NotSupportedException("Control.ProtectedOwner: Typed controls require admitted ordinary body, cell or plain header/footer owners.");
            var data = ReadTypedProperties(properties, id, inline);
            if (node.Element(W + "sdtEndPr") is XElement end)
            {
                Attributes(end); if (end.Elements().Count() != 1 || end.Element(W + "rPr") is not XElement rpr) throw new NotSupportedException("Control.Unsupported: End properties require one admitted run property set.");
                ValidateTypedRunProperties(rpr); data.EndProperties = new XElement(end);
            }
            ValidateTypedContent(node.Element(W + "sdtContent")!, data);
            typedControls.Items.Add(id, data); typedNodes.Add(node, data);
        }
        // A control may not intersect any source field/bookmark range, even when its marker is outside the wrapper.
        foreach (var item in sources)
        {
            var bookmarksOpen = new HashSet<string>(StringComparer.Ordinal); int fieldDepth = 0;
            foreach (XElement element in item.Document.Descendants())
            {
                context.ReserveControlWork(1);
                if (element.Name == W + "sdt" && (fieldDepth != 0 || bookmarksOpen.Count != 0)) throw new NotSupportedException("Control.ProtectedOwner: Field/bookmark ranges cannot enclose or cross a control.");
                if (element.Name == W + "bookmarkStart") bookmarksOpen.Add(DocxPackage.RequiredAttribute(element, W + "id"));
                else if (element.Name == W + "bookmarkEnd") bookmarksOpen.Remove(DocxPackage.RequiredAttribute(element, W + "id"));
                else if (element.Name == W + "fldChar")
                { string? kind = (string?)element.Attribute(W + "fldCharType"); if (kind == "begin") fieldDepth++; else if (kind == "end") fieldDepth--; }
            }
        }
        ReadTypedParts();
    }
    private ControlData ReadTypedProperties(XElement properties, int id, bool inline)
    {
        Attributes(properties);
        if (properties.Element(W + "rPr") is XElement defaults) ValidateTypedRunProperties(defaults);
        if (properties.Elements().GroupBy(e => e.Name).Any(g => g.Count() != 1)) throw new InvalidDataException("Control.Malformed: Duplicate control properties.");
        XElement[] types = properties.Elements().Where(e => e.Name == W + "richText" || e.Name == W + "text" || e.Name == W + "dropDownList"
            || e.Name == W + "comboBox" || e.Name == W + "date" || e.Name == ControlValues.W14 + "checkbox").ToArray();
        if (types.Length > 1) throw new InvalidDataException("Control.Malformed: Multiple typed-control kind declarations.");
        var data = new ControlData
        {
            Id = id,
            Inline = inline,
            Properties = new XElement(properties),
            Kind = types.Length == 0 ? ContentControlKind.RichText : types[0].Name.LocalName switch
            { "checkbox" => ContentControlKind.Checkbox, "dropDownList" => ContentControlKind.DropDownList, "comboBox" => ContentControlKind.ComboBox, "date" => ContentControlKind.Date, "text" => ContentControlKind.PlainText, _ => ContentControlKind.RichText }
        };
        if (data.Properties.Element(W + "id") == null) data.Properties.Add(new XElement(W + "id", new XAttribute(W + "val", id)));
        foreach (XElement property in properties.Elements())
        {
            context.ReserveControlWork(1);
            if (types.Contains(property)) continue;
            if (property.Name == W + "rPr") { ValidateTypedRunProperties(property); continue; }
            if (property.Name == W + "placeholder")
            {
                Attributes(property); XElement? part = property.Element(W + "docPart");
                if (property.Elements().Count() != 1 || part == null) throw new InvalidDataException("Control.Malformed: A placeholder requires one local document-part reference.");
                Leaf(part, W + "val"); if (DocxPackage.RequiredAttribute(part, W + "val").Length is 0 or > 256) throw new InvalidDataException("Limit.Controls: Placeholder names must be bounded.");
                continue;
            }
            if (property.Name == W + "dataBinding")
            {
                Leaf(property, W + "storeItemID", W + "xpath", W + "prefixMappings");
                if (data.Kind == ContentControlKind.RichText) throw new NotSupportedException("Binding.Type: OOXML ignores rich-text data binding; typed preservation rejects it.");
                data.Binding = ControlBindings.Read(property, context); data.BindingState = ContentControlBindingState.Unevaluated; continue;
            }
            if (property.Name != W + "tag" && property.Name != W + "alias" && property.Name != W + "id" && property.Name != W + "lock" && property.Name != W + "showingPlcHdr")
                throw new NotSupportedException("Control.Unsupported: Unknown or behavior-bearing control properties are outside the admitted profile.");
            Leaf(property, W + "val");
            if (property.Name == W + "showingPlcHdr") data.Placeholder = ControlValues.On(property, W);
            else if (property.Name == W + "lock") data.Lock = DocxPackage.RequiredAttribute(property, W + "val") switch
            { "unlocked" => ContentControlLock.Unlocked, "sdtLocked" => ContentControlLock.ControlLocked, "contentLocked" => ContentControlLock.ContentLocked, "sdtContentLocked" => ContentControlLock.ControlAndContentLocked, _ => throw new InvalidDataException("Control.Malformed: Invalid lock setting.") };
            else _ = DocxPackage.RequiredAttribute(property, W + "val");
        }
        if (types.Length != 0)
        {
            XElement type = types[0];
            if (data.Kind == ContentControlKind.Checkbox)
            {
                Attributes(type);
                if (type.Elements().GroupBy(e => e.Name).Any(g => g.Count() > 1)) throw new InvalidDataException("Control.Malformed: Duplicate checkbox state metadata.");
                foreach (XElement e in type.Elements())
                {
                    if (e.Name == ControlValues.W14 + "checked") { Leaf(e, ControlValues.W14 + "val"); data.Checked = ControlValues.On(e, ControlValues.W14, required: true); }
                    else if (e.Name == ControlValues.W14 + "checkedState" || e.Name == ControlValues.W14 + "uncheckedState")
                    {
                        Leaf(e, ControlValues.W14 + "val", ControlValues.W14 + "font"); string value = DocxPackage.RequiredAttribute(e, ControlValues.W14 + "val");
                        if (value.Length != 4 || value.Any(c => !Uri.IsHexDigit(c)) || !int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int scalar) || !System.Xml.XmlConvert.IsXmlChar((char)scalar) || char.IsControl((char)scalar) || char.IsSurrogate((char)scalar))
                            throw new InvalidDataException("Control.Malformed: Checkbox glyphs must be printable BMP scalars in four hexadecimal digits.");
                        string font = DocxPackage.RequiredAttribute(e, ControlValues.W14 + "font");
                        if (font.Length is 0 or > 256) throw new InvalidDataException("Limit.Controls: Checkbox font names exceed their bound.");
                    }
                    else throw new NotSupportedException("Control.Unsupported: Unknown checkbox state metadata.");
                }
            }
            else if (data.Kind is ContentControlKind.DropDownList or ContentControlKind.ComboBox)
            {
                Attributes(type, W + "lastValue");
                if (((string?)type.Attribute(W + "lastValue"))?.Length > 1024) throw new InvalidDataException("Limit.Controls: List cache metadata exceeds its bound.");
                foreach (XElement e in type.Elements())
                {
                    if (e.Name != W + "listItem") throw new NotSupportedException("Control.Unsupported: Unsupported list metadata.");
                    Leaf(e, W + "displayText", W + "value"); data.Items.Add(new ContentControlListItem((string?)e.Attribute(W + "displayText"), DocxPackage.RequiredAttribute(e, W + "value")));
                    if (data.Items.Count > 256) throw new InvalidDataException("Limit.Controls: Too many list items.");
                }
            }
            else if (data.Kind == ContentControlKind.Date)
            {
                Attributes(type, W + "fullDate");
                if (type.Elements().GroupBy(e => e.Name).Any(g => g.Count() > 1)) throw new InvalidDataException("Control.Malformed: Duplicate date metadata.");
                foreach (XElement e in type.Elements())
                {
                    if (e.Name != W + "dateFormat" && e.Name != W + "lid" && e.Name != W + "calendar" && e.Name != W + "storeMappedDataAs") throw new NotSupportedException("Control.Unsupported: Unknown date metadata.");
                    Leaf(e, W + "val"); string value = DocxPackage.RequiredAttribute(e, W + "val");
                    if (e.Name == W + "lid" && !value.Equals("en-US", StringComparison.OrdinalIgnoreCase) && !value.Equals("en-GB", StringComparison.OrdinalIgnoreCase)
                        || e.Name == W + "calendar" && value != "gregorian"
                        || e.Name == W + "storeMappedDataAs" && value is not "text" and not "date" and not "dateTime")
                        throw new NotSupportedException("Control.DateFormat: Unsupported locale, calendar or storage mode.");
                }
                _ = ControlValues.DateDisplay(data, context.ControlDateMode == ContentControlDateMode.DateOnly ? ContentControlDateValue.FromDate(2000, 1, 1) : ContentControlDateValue.FromInstant(new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero)));
                if (type.Attribute(W + "fullDate") is XAttribute fullDate) data.DateValue = ControlValues.ParseDate(fullDate.Value, context.ControlDateMode);
            }
            else if (data.Kind == ContentControlKind.PlainText) { Leaf(type, W + "multiLine"); if (type.Attribute(W + "multiLine") is XAttribute multiline) _ = ControlValues.Bool(multiline.Value); }
            else Leaf(type);
        }
        data.Validate(context); return data;
    }
    private static void Attributes(XElement element, params XName[] allowed)
    {
        if (element.Name != W + "t" && element.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)) || element.Nodes().Any(n => n is XProcessingInstruction))
            throw new InvalidDataException("Control.Malformed: Text must be inside text elements and processing instructions are prohibited.");
        if (element.Attributes().Any(a => !a.IsNamespaceDeclaration && !allowed.Contains(a.Name))) throw new NotSupportedException("Control.Unsupported: An attribute is outside the admitted control profile.");
    }
    private static void Leaf(XElement element, params XName[] allowed)
    { Attributes(element, allowed); if (element.HasElements || !string.IsNullOrWhiteSpace(element.Value)) throw new InvalidDataException("Control.Malformed: Metadata must be a leaf property."); }
    private void ValidateTypedRunProperties(XElement rpr)
    {
        Attributes(rpr);
        if (rpr.Elements().GroupBy(e => e.Name).Any(g => g.Count() > 1)) throw new InvalidDataException("Control.Malformed: Duplicate run properties.");
        NormalizeTypedEditorRunProperties(rpr);
        foreach (XElement e in rpr.Elements())
        {
            if (e.Name == W + "rFonts")
            {
                Leaf(e, W + "ascii", W + "hAnsi");
                string? a = (string?)e.Attribute(W + "ascii"), h = (string?)e.Attribute(W + "hAnsi");
                if (a?.Length > 256 || h?.Length > 256 || a != null && h != null && a != h) throw new NotSupportedException("Control.Unsupported: A control run needs one bounded font family.");
            }
            else if (e.Name == W + "b" || e.Name == W + "i") { Leaf(e, W + "val"); _ = ControlValues.On(e, W); }
            else if (e.Name == W + "u") { Leaf(e, W + "val"); if ((string?)e.Attribute(W + "val") is not "single" and not "none") throw new NotSupportedException("Control.Unsupported: Only single/none underline is admitted."); }
            else if (e.Name == W + "sz") { Leaf(e, W + "val"); _ = Integer(e, W + "val", 1, 3168); }
            else if (e.Name == W + "color")
            { Leaf(e, W + "val"); string value = DocxPackage.RequiredAttribute(e, W + "val"); if (value != "auto" && (value.Length != 6 || value.Any(c => !Uri.IsHexDigit(c)) || !uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))) throw new InvalidDataException("Control.Malformed: Invalid RGB color."); }
            else throw new NotSupportedException("Control.Unsupported: The run properties are outside the admitted control text profile.");
        }
    }
    private void ValidateTypedContent(XElement content, ControlData data)
    {
        Attributes(content);
        if (data.Inline && content.Elements().Any(e => e.Name != W + "r" && e.Name != W + "proofErr") || !data.Inline && content.Elements().Any(e => e.Name != W + "p"))
            throw new NotSupportedException("Control.ProtectedOwner: Controls admit formatted runs or ordinary paragraphs only.");
        if (!data.Inline && data.Kind != ContentControlKind.RichText && content.Elements(W + "p").Count() > 1)
            throw new NotSupportedException("Control.ProtectedOwner: Scalar controls admit at most one paragraph.");
        var renderedBreaks = new List<XElement>();
        foreach (XElement e in content.Descendants())
        {
            context.ReserveControlWork(1);
            if (e.Ancestors(W + "sdt").FirstOrDefault() != content.Parent) continue;
            if (e.Name == W + "sdt")
            {
                if (data.Inline || data.Kind != ContentControlKind.RichText || data.Placeholder || data.Binding != null
                    || e.Parent?.Name != W + "p" || e.Parent.Parent != content)
                    throw new NotSupportedException("Control.ProtectedOwner: Nested controls require a non-placeholder, unbound block rich-text parent.");
                continue;
            }
            if (e.Ancestors(W + "rPr").Any()) continue;
            if (e.Name == W + "rPr") { ValidateTypedRunProperties(e); continue; }
            if (e.Name == W + "pPr") { ValidateTypedParagraphProperties(e); continue; }
            if (e.Ancestors(W + "pPr").Any()) continue;
            if (e.Name == W + "p" || e.Name == W + "r")
            {
                ValidateTypedTextOwnerAttributes(e);
                if (e.Elements().Any(child => e.Name == W + "p" ? child.Name != W + "pPr" && child.Name != W + "r" && child.Name != W + "proofErr" && child.Name != W + "sdt" : child.Name != W + "rPr" && child.Name != W + "t" && child.Name != W + "tab" && child.Name != W + "br" && child.Name != W + "lastRenderedPageBreak")) throw new NotSupportedException("Control.ProtectedOwner: Unsupported nested text structure.");
                if (e.Elements(W + (e.Name == W + "p" ? "pPr" : "rPr")).Count() > 1) throw new InvalidDataException("Control.Malformed: Duplicate text property containers.");
            }
            else if (e.Name == W + "t") { Attributes(e, XNamespace.Xml + "space"); if (e.HasElements) throw new InvalidDataException("Control.Malformed: Text cannot contain XML markup."); }
            else if (e.Name == W + "tab") Leaf(e);
            else if (e.Name == W + "br") { Leaf(e, W + "type"); if ((string?)e.Attribute(W + "type") is string type && type != "textWrapping") throw new NotSupportedException("Control.ProtectedOwner: Page/column breaks cannot occur inside controls."); }
            else if (e.Name == W + "proofErr")
            {
                Leaf(e, W + "type");
                if ((string?)e.Attribute(W + "type") is not "spellStart" and not "spellEnd" and not "gramStart" and not "gramEnd") throw new InvalidDataException("Control.Malformed: Invalid proofing marker.");
                ReportTypedEditorMetadata();
            }
            else if (e.Name == W + "lastRenderedPageBreak") { Leaf(e); renderedBreaks.Add(e); ReportTypedEditorMetadata(); }
            else throw new NotSupportedException("Control.ProtectedOwner: Controls cannot contain fields, bookmarks, tables, revisions, objects or unsupported nested controls.");
        }
        foreach (XElement renderedBreak in renderedBreaks) renderedBreak.Remove();
    }
    private void ValidateTypedParagraphProperties(XElement ppr)
    {
        Attributes(ppr);
        if (ppr.Elements().GroupBy(e => e.Name).Any(g => g.Count() > 1)) throw new InvalidDataException("Control.Malformed: Duplicate paragraph properties.");
        foreach (XElement e in ppr.Elements())
        {
            if (e.Name == W + "rPr") ValidateTypedRunProperties(e);
            else if (e.Name == W + "spacing") Leaf(e, W + "before", W + "after", W + "line", W + "lineRule");
            else if (e.Name == W + "pStyle" || e.Name == W + "jc" || e.Name == W + "outlineLvl") Leaf(e, W + "val");
            else if (e.Name == W + "keepNext" || e.Name == W + "keepLines" || e.Name == W + "pageBreakBefore" || e.Name == W + "bidi") { Leaf(e, W + "val"); _ = ControlValues.On(e, W); }
            else throw new NotSupportedException("Control.Unsupported: The paragraph properties are outside the admitted rich-text profile.");
        }
    }
    private bool ReadTypedBlock(XElement element, List<DocumentBlock> blocks)
    {
        if (!typedNodes.TryGetValue(element, out ControlData? data)) throw new NotSupportedException("Control.ProtectedOwner: Typed controls in this story/owner are unsupported.");
        int start = blocks.Count;
        ReadBlocks(element.Element(W + "sdtContent")!, blocks);
        if (blocks.Count == start) blocks.Add(new ParagraphBlock { FallbackFontSize = 11, FallbackAfter = 0 });
        foreach (ParagraphBlock p in blocks.Skip(start).Cast<ParagraphBlock>()) { ValidateTypedParagraph(p); p.TypedControlId = data.Id; }
        ValidateTypedRuns(data, blocks.Skip(start).Cast<ParagraphBlock>().SelectMany(p => p.Runs));
        return true;
    }
    private bool ReadTypedInline(XElement element, List<TextRun> runs, TextStyle inherited, string? link)
    {
        if (fields.Count != 0 || link != null || !typedNodes.TryGetValue(element, out ControlData? data)) throw new NotSupportedException("Control.ProtectedOwner: Controls cannot be embedded in fields, hyperlinks or unsupported stories.");
        var cached = new List<TextRun>(); ReadInlines(element.Element(W + "sdtContent")!, cached, inherited, link);
        if (cached.Count == 0) cached.Add(new TextRun("", inherited.Bold, inherited.Italic, inherited.Code, fontSize: inherited.FontSize, color: inherited.Color, appearance: inherited.Appearance.Copy()));
        ValidateTypedRuns(data, cached);
        foreach (TextRun r in cached) { r.TypedControlId = data.Id; runs.Add(r); }
        return true;
    }
    private static void ValidateTypedParagraph(ParagraphBlock paragraph)
    {
        if (paragraph.List != null || paragraph.IsCodeBlock || paragraph.HeadingLevel > 6 || paragraph.Layout.Left != 0 || paragraph.Layout.Right != 0 || paragraph.Layout.FirstLine != 0 || paragraph.Layout.Tabs.Count != 0)
            throw new NotSupportedException("Control.Unsupported: Inherited paragraph structure is outside the ordinary rich-control profile.");
    }
    private static void ValidateTypedRuns(ControlData data, IEnumerable<TextRun> runs)
    {
        TextRun? first = null;
        foreach (TextRun run in runs)
        {
            if (run.Image != null || run.FieldInstruction != null || run.Bookmark != null || run.Control != null || run.Object != null || run.Note != null || run.Box != null || run.Revision != null || run.Comment != null)
                throw new NotSupportedException("Control.ProtectedOwner: Only ordinary formatted text is admitted inside a control.");
            if (run.Color != null && (run.Color.Length != 6 || run.Color.Any(c => !Uri.IsHexDigit(c)))) throw new InvalidDataException("Control.Malformed: Inherited color metadata must contain six RGB hex digits.");
            if (run.Appearance.ThemeFontAscii != null || run.Appearance.ThemeFontHighAnsi != null || run.Appearance.ThemeColor != null)
                throw new NotSupportedException("Control.Unsupported: Editable theme references are outside the admitted typed-control text profile.");
            if (run.Code || run.Appearance.Strike || run.Appearance.Background != null || run.Appearance.VerticalAlignment != null)
                throw new NotSupportedException("Control.Unsupported: Inherited text properties exceed the control formatting profile.");
            if (first != null && data.Kind != ContentControlKind.RichText && !SameStyle(first, run)) throw new NotSupportedException("Control.Unsupported: Scalar controls require a single formatting set.");
            first ??= run;
        }
        static bool SameStyle(TextRun a, TextRun b) => a.Bold == b.Bold && a.Italic == b.Italic && a.Code == b.Code && a.FontSize == b.FontSize && a.Color == b.Color
            && a.Appearance.FontFamily == b.Appearance.FontFamily && a.Appearance.Underline == b.Appearance.Underline;
    }
    private void FinishTypedControls(DocumentModel document)
    {
        if (!context.PreserveControls) return;
        document.Controls = typedControls;
        if (!typedControls.HasControls) return;
        var owners = ControlOwners.Index(document, context);
        foreach (ControlData data in typedControls.Items.Values)
        {
            ControlOwner owner = owners[data.Id]; owner.Refresh(data.Id, context);
            using var location = context.At(owner.Content.First().Location);
            // ReadBlocks can attach pending body/cell bookmark markers after the initial content validation.
            ValidateTypedRuns(data, owner.Runs);
            foreach (ParagraphBlock p in owner.Content) ValidateTypedParagraph(p);
            TextRun prototype = owner.Runs.FirstOrDefault() ?? new TextRun("");
            var style = new TextStyle { Bold = prototype.Bold, Italic = prototype.Italic, Code = prototype.Code, FontSize = prototype.FontSize, Color = prototype.Color, Appearance = prototype.Appearance.Copy() };
            ReadRunProperties(data.Properties.Element(W + "rPr"), style);
            data.InputStyle = new TextRun("", style.Bold, style.Italic, style.Code, fontSize: style.FontSize, color: style.Color, appearance: style.Appearance.Copy());
            string? placeholder = (string?)data.Properties.Element(W + "placeholder")?.Element(W + "docPart")?.Attribute(W + "val");
            if (placeholder != null)
            {
                if (!typedControls.GlossaryContent.TryGetValue(placeholder, out ParagraphBlock? p)) throw new NotSupportedException("Control.Unsupported: The authored placeholder reference does not resolve to an admitted local glossary entry.");
                data.PlaceholderContent = new[] { p };
            }
            else if (data.Placeholder)
            {
                var cloner = new DocumentCloner(context);
                var content = owner.Paragraph == null ? owner.Content.Select(p => (ParagraphBlock)cloner.Block(p)).ToArray() : new[] { new ParagraphBlock() };
                if (owner.Paragraph != null) content[0].Runs.AddRange(owner.Runs.Select(r => r.WithText(r.Text)));
                foreach (ParagraphBlock p in content) { p.TypedControlId = null; foreach (TextRun r in p.Runs) r.TypedControlId = null; }
                data.PlaceholderContent = content;
            }
            foreach (ParagraphBlock placeholderParagraph in data.PlaceholderContent) { ValidateTypedParagraph(placeholderParagraph); ValidateTypedRuns(data, placeholderParagraph.Runs); }
            if (data.Kind == ContentControlKind.Checkbox && data.PlaceholderContent.Count != 0 && string.Join("\n", data.PlaceholderContent.Select(p => p.Text)).Length != 1)
                throw new NotSupportedException("Control.Placeholder: Authored checkbox placeholders must contain one character.");
            if (data.Kind == ContentControlKind.Checkbox && owner.Text.Length != 1) throw new InvalidDataException("Control.Malformed: A checkbox cache/placeholder must contain exactly one character.");
            if (data.Kind == ContentControlKind.PlainText)
            {
                bool multiline = data.Properties.Element(W + "text")?.Attribute(W + "multiLine") is XAttribute a && ControlValues.Bool(a.Value);
                if (!multiline && (owner.Text.Contains('\n') || data.PlaceholderContent.Any(p => p.Text.Contains('\n')))) throw new NotSupportedException("Control.Unsupported: Single-line controls cannot contain line breaks.");
            }
            ControlValues.Infer(data, owner, context);
            if (data.Binding != null) _ = ControlBindings.Resolve(data.Binding, typedControls, context);
        }
        foreach (int parent in owners.Values.Where(owner => owner.ParentId.HasValue).Select(owner => owner.ParentId!.Value).Distinct())
            ControlValues.Infer(typedControls.Items[parent], owners[parent], context);
        ControlOwners.Normalize(document);
    }
}
