using System;
using System.Globalization;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxWriter
{
    private XElement TypedControl(int id, XElement? content)
    {
        ControlData data = model.Controls.Items[id];
        var properties = new XElement(W + "sdtPr");
        foreach (string name in new[] { "rPr", "alias", "lock", "placeholder", "showingPlcHdr", "dataBinding", "id", "tag" })
            if (data.Properties.Element(W + name) is XElement p) properties.Add(name == "rPr" ? TypedRunProperties(p) : new XElement(p));
        XElement? type = data.Kind switch
        {
            ContentControlKind.Checkbox => data.Properties.Element(ControlValues.W14 + "checkbox"),
            ContentControlKind.DropDownList => data.Properties.Element(W + "dropDownList"),
            ContentControlKind.ComboBox => data.Properties.Element(W + "comboBox"),
            ContentControlKind.Date => data.Properties.Element(W + "date"),
            ContentControlKind.PlainText => data.Properties.Element(W + "text"),
            _ => data.Properties.Element(W + "richText")
        };
        if (type != null)
        {
            var clone = new XElement(type.Name, type.Attributes());
            if (data.Kind == ContentControlKind.Checkbox)
            {
                foreach (string checkboxName in new[] { "checked", "checkedState", "uncheckedState" })
                    if (type.Element(ControlValues.W14 + checkboxName) is XElement checkProperty) clone.Add(new XElement(checkProperty));
            }
            else if (data.Kind == ContentControlKind.Date)
            {
                foreach (string dateName in new[] { "dateFormat", "lid", "storeMappedDataAs", "calendar" })
                    if (type.Element(W + dateName) is XElement dateProperty) clone.Add(new XElement(dateProperty));
            }
            else clone.Add(type.Nodes());
            properties.Add(clone);
        }
        XElement? end = data.EndProperties == null ? null : new XElement(W + "sdtEndPr", TypedRunProperties(data.EndProperties.Element(W + "rPr")!));
        return new XElement(W + "sdt", properties, end, new XElement(W + "sdtContent", content));
    }
    private static XElement TypedRunProperties(XElement source)
    {
        var result = new XElement(W + "rPr");
        foreach (string name in new[] { "rFonts", "b", "i", "color", "sz", "u" })
            if (source.Element(W + name) is XElement p) result.Add(new XElement(p));
        return result;
    }
    private static bool HasControl(DocumentBlock block) => block.Control != null || block.TypedControlId.HasValue;
    private static bool SameControl(DocumentBlock a, DocumentBlock b) => a.TypedControlId == b.TypedControlId && ReferenceEquals(a.Control, b.Control);
    private XElement BlockControl(DocumentBlock block, XElement? element) => block.TypedControlId is int id ? TypedControl(id, element) : Control(block.Control!, element);
    private void WriteTypedParts(ZipArchive archive, XElement types)
    {
        int index = 0;
        foreach (ControlXmlStore store in model.Controls.XmlStores.Values.OrderBy(s => s.Part, StringComparer.Ordinal))
        {
            context.CheckCancellation();
            relationships.Add(Relationship("rControlXml" + (index++).ToString(CultureInfo.InvariantCulture), "customXml", "/" + store.Part));
            types.Add(new XElement(DocxPackage.ContentTypes + "Override", new XAttribute("PartName", "/" + store.Part), new XAttribute("ContentType", "application/xml")),
                new XElement(DocxPackage.ContentTypes + "Override", new XAttribute("PartName", "/" + store.PropertiesPart), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.customXmlProperties+xml")));
            WritePart(archive, store.Part, new XElement(store.Xml)); WritePart(archive, store.PropertiesPart, new XElement(store.Properties));
            WritePart(archive, DocxPackage.RelationshipPartName(store.Part), new XElement(DocxPackage.Relationships + "Relationships", Relationship("rProperties", "customXmlProps", "/" + store.PropertiesPart)));
        }
        if (model.Controls.Glossary != null)
        {
            string part = model.Controls.GlossaryPart!;
            var root = new XElement(W + "glossaryDocument", new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName));
            var entries = new XElement(W + "docParts"); root.Add(entries);
            foreach (XElement entry in model.Controls.Glossary.Element(W + "docParts")!.Elements(W + "docPart"))
            {
                XElement original = entry.Element(W + "docPartPr")!; var props = new XElement(W + "docPartPr");
                foreach (string name in new[] { "name", "types", "behaviors", "description", "guid" })
                    if (original.Element(W + name) is XElement p) props.Add(new XElement(p));
                string key = (string)original.Element(W + "name")!.Attribute(W + "val")!;
                entries.Add(new XElement(W + "docPart", props, new XElement(W + "docPartBody", Paragraph(model.Controls.GlossaryContent[key]))));
            }
            relationships.Add(Relationship("rControlGlossary", "glossaryDocument", "/" + part));
            types.Add(new XElement(DocxPackage.ContentTypes + "Override", new XAttribute("PartName", "/" + part), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.glossary+xml")));
            WritePart(archive, part, root);
        }
    }
}
