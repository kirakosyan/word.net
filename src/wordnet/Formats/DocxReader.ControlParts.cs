using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxReader
{
    private const string CustomXmlPropertiesType = "application/vnd.openxmlformats-officedocument.customXmlProperties+xml";
    private const string GlossaryType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.glossary+xml";
    private void ReadTypedParts()
    {
        if (!typedControls.HasControls) return;
        XElement contentTypes = package.ReadXml("[Content_Types].xml").Root!;
        var requested = new HashSet<string>(typedControls.Items.Values.Where(d => d.Binding != null).Select(d => d.Binding!.StoreId), StringComparer.OrdinalIgnoreCase);
        var storeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        XElement[] stores = relationships.Values.Where(r => (string?)r.Attribute("Type") == DocxPackage.DocumentRelationships.NamespaceName + "/customXml").ToArray();
        if (stores.Length > 64) throw new InvalidDataException("Limit.Controls: Too many local custom XML stores.");
        foreach (XElement relation in stores)
        {
            context.CheckCancellation();
            string part = ControlPart(mainPart, relation);
            RequireControlPart(part, "application/xml", contentTypes);
            var relations = package.ReadRelationships(part);
            if (relations.Count != 1 || (string?)relations.Values.Single().Attribute("Type") != DocxPackage.DocumentRelationships.NamespaceName + "/customXmlProps")
                throw new NotSupportedException("Binding.Store: Custom XML requires exactly one internal properties relationship.");
            string props = ControlPart(part, relations.Values.Single());
            RequireControlPart(props, CustomXmlPropertiesType, contentTypes);
            if (props == part || package.ReadRelationships(props).Count != 0) throw new InvalidDataException("Binding.Store: Invalid properties graph.");
            XElement properties = TypedXml(props);
            if (properties.Name != Cx + "datastoreItem") throw new InvalidDataException("Binding.Store: Invalid custom XML properties root.");
            Attributes(properties, Cx + "itemID");
            string lexicalId = DocxPackage.RequiredAttribute(properties, Cx + "itemID");
            string id = ControlBindings.StoreId(lexicalId);
            if (lexicalId != id)
            { properties.SetAttributeValue(Cx + "itemID", id); context.Report("Control.GuidNormalized", "A local store GUID uses the schema-required uppercase lexical form; its identity is unchanged."); }
            if (!storeIds.Add(id)) throw new InvalidDataException("Binding.Store: Duplicate local XML store GUID.");
            if (properties.Elements(Cx + "schemaRefs").Count() > 1 || properties.Elements().Any(e => e.Name != Cx + "schemaRefs")) throw new NotSupportedException("Binding.Store: Unknown custom XML properties.");
            foreach (XElement refs in properties.Elements())
            {
                Attributes(refs);
                foreach (XElement schema in refs.Elements())
                { if (schema.Name != Cx + "schemaRef") throw new NotSupportedException("Binding.Store: Unknown schema metadata."); Leaf(schema, Cx + "uri"); _ = DocxPackage.RequiredAttribute(schema, Cx + "uri"); }
            }
            // Retain admitted local stores even after explicit detachment; removing a binding must not delete caller data.
            XElement xml = TypedXml(part);
            typedControls.XmlStores.Add(id, new ControlXmlStore { Id = id, Part = part, PropertiesPart = props, Xml = xml, Properties = properties });
            knownParts.Add(part); knownParts.Add(props); knownParts.Add(DocxPackage.RelationshipPartName(part)); knownParts.Add(DocxPackage.RelationshipPartName(props));
        }
        if (requested.Any(id => !typedControls.XmlStores.ContainsKey(id))) throw new InvalidDataException("Binding.Store: A binding references a missing local XML store.");
        XElement[] glossary = relationships.Values.Where(r => (string?)r.Attribute("Type") == DocxPackage.DocumentRelationships.NamespaceName + "/glossaryDocument").ToArray();
        if (glossary.Length > 1) throw new InvalidDataException("Control.Malformed: Duplicate glossary relationships.");
        if (glossary.Length == 0) return;
        string glossaryPart = ControlPart(mainPart, glossary[0]);
        RequireControlPart(glossaryPart, GlossaryType, contentTypes);
        ReadGlossaryResources(glossaryPart, contentTypes);
        XElement root = TypedXml(glossaryPart);
        if (root.Name != W + "glossaryDocument") throw new InvalidDataException("Control.Malformed: Invalid glossary root.");
        Attributes(root);
        XElement? parts = root.Element(W + "docParts");
        if (root.Elements().Count() != 1 || parts == null) throw new NotSupportedException("Control.Unsupported: Glossaries contain only admitted named placeholder entries.");
        Attributes(parts);
        foreach (XElement entry in parts.Elements())
        {
            context.ReserveControlWork(1); Attributes(entry);
            if (entry.Name != W + "docPart" || entry.Elements(W + "docPartPr").Count() != 1 || entry.Elements(W + "docPartBody").Count() != 1 || entry.Elements().Count() != 2)
                throw new InvalidDataException("Control.Malformed: Invalid glossary entry.");
            XElement properties = entry.Element(W + "docPartPr")!, body = entry.Element(W + "docPartBody")!;
            Attributes(properties); Attributes(body);
            if (properties.Elements().GroupBy(e => e.Name).Any(g => g.Count() > 1)) throw new InvalidDataException("Control.Malformed: Duplicate glossary metadata.");
            foreach (XElement property in properties.Elements())
            {
                if (property.Name == W + "name" || property.Name == W + "description" || property.Name == W + "guid")
                {
                    Leaf(property, W + "val"); string value = DocxPackage.RequiredAttribute(property, W + "val");
                    if (value.Length is 0 or > 256) throw new InvalidDataException("Limit.Controls: Glossary metadata exceeds its bound.");
                    if (property.Name == W + "guid") property.SetAttributeValue(W + "val", ControlBindings.StoreId(value));
                }
                else if (property.Name == W + "types" || property.Name == W + "behaviors")
                {
                    Attributes(property); string leaf = property.Name == W + "types" ? "type" : "behavior";
                    if (property.Elements().Count() != 1 || property.Element(W + leaf) is not XElement value) throw new NotSupportedException("Control.Unsupported: Only static placeholder glossary behavior is admitted.");
                    Leaf(value, W + "val"); if ((string?)value.Attribute(W + "val") != (leaf == "type" ? "bbPlcHdr" : "content")) throw new NotSupportedException("Control.Unsupported: Only static placeholder glossary behavior is admitted.");
                }
                else if (property.Name == W + "category")
                {
                    Attributes(property);
                    if (property.Elements().Count() != 2 || property.Element(W + "name") is not XElement categoryName || property.Element(W + "gallery") is not XElement gallery)
                        throw new InvalidDataException("Control.Malformed: Glossary categories require exactly one name and gallery.");
                    Leaf(categoryName, W + "val"); Leaf(gallery, W + "val");
                    string nameValue = DocxPackage.RequiredAttribute(categoryName, W + "val");
                    string galleryValue = DocxPackage.RequiredAttribute(gallery, W + "val");
                    if (nameValue.Length is 0 or > 256) throw new InvalidDataException("Limit.Controls: Glossary category name exceeds its bound.");
                    if (galleryValue is not "default" and not "placeholder") throw new NotSupportedException("Control.Unsupported: Only static placeholder glossary categories are admitted.");
                }
                else throw new NotSupportedException("Control.Unsupported: Unknown glossary entry behavior.");
            }
            string name = (string?)properties.Element(W + "name")?.Attribute(W + "val") ?? throw new InvalidDataException("Control.Malformed: A glossary entry requires a name.");
            if (typedControls.GlossaryContent.Count == 128) throw new InvalidDataException("Limit.Controls: Too many glossary entries.");
            if (typedControls.GlossaryContent.ContainsKey(name)) throw new InvalidDataException("Control.Malformed: Duplicate glossary entry names.");
            if (body.Elements().Count() != 1 || body.Element(W + "p") is not XElement paragraph) throw new NotSupportedException("Control.Unsupported: A placeholder glossary entry must contain one ordinary paragraph.");
            var validation = new ControlData { Kind = ContentControlKind.RichText, Inline = false };
            ParagraphBlock content;
            if (glossaryStyleReader == null)
            {
                ValidateTypedContent(body, validation);
                content = ReadParagraph(paragraph);
            }
            else
            {
                // Validate the ordinary content independently, allowing only a
                // resolved character-style reference from the local glossary.
                var checkedBody = new XElement(body);
                foreach (XElement style in checkedBody.Descendants(W + "rStyle").ToArray())
                {
                    context.ReserveControlWork(1);
                    if (style.Parent?.Name != W + "rPr" || style.Parent.Parent?.Name != W + "r")
                        throw new NotSupportedException("Control.Unsupported: A glossary style reference must belong to an ordinary run.");
                    Leaf(style, W + "val"); string id = DocxPackage.RequiredAttribute(style, W + "val");
                    if (!glossaryStyleReader.styles.TryGetValue(id, out XElement? definition) || (string?)definition.Attribute(W + "type") != "character")
                        throw new NotSupportedException("Control.Unsupported: A placeholder requires a local character style.");
                    if (style.Parent.Elements(W + "rStyle").Count() != 1) throw new InvalidDataException("Control.Malformed: Duplicate glossary character styles.");
                    style.Remove();
                }
                ValidateTypedContent(checkedBody, validation);
                if (glossaryScriptFormatting) { RequireTypedDefaultScript(paragraph); ReportTypedEditorMetadata(); }
                content = glossaryStyleReader.ReadParagraph(paragraph);
                // Local style identities do not belong to the main document's
                // style graph. Materialize their admitted resolved appearance.
                content.StyleId = null;
                IEnumerable<TextRun> styledRuns = content.Mark == null ? content.Runs : content.Runs.Concat(new[] { content.Mark });
                foreach (TextRun run in styledRuns)
                {
                    if (run.Appearance.ThemeFontAscii != null || run.Appearance.ThemeFontHighAnsi != null || run.Appearance.ThemeColor != null)
                    {
                        context.Loss("Control.GlossaryTheme", "Glossary-local theme references use their resolved appearance without retaining editable theme identity.");
                        run.Appearance.ThemeFontAscii = null; run.Appearance.ThemeFontHighAnsi = null;
                        run.Appearance.ThemeColor = null; run.Appearance.ThemeTint = null; run.Appearance.ThemeShade = null;
                    }
                }
            }
            ValidateTypedRuns(validation, content.Runs);
            typedControls.GlossaryContent.Add(name, content);
        }
        typedControls.GlossaryPart = glossaryPart; typedControls.Glossary = new XElement(root);
        knownParts.Add(glossaryPart); knownParts.Add(DocxPackage.RelationshipPartName(glossaryPart));
    }
    private XElement TypedXml(string part)
    {
        XDocument document = package.ReadXml(part);
        if (document.DescendantNodes().Any(n => n is XProcessingInstruction || n is XDocumentType)) throw new InvalidDataException("Binding.Store: Processing instructions and DTDs are prohibited.");
        return document.Root ?? throw new InvalidDataException("Binding.Store: Missing XML root.");
    }
    private void RequireControlPart(string part, string expectedType, XElement types)
    {
        if (knownParts.Contains(part) || part.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Binding.Store: A control resource cannot alias another recognized part.");
        XElement[] overrides = types.Elements(DocxPackage.ContentTypes + "Override").Where(e => (string?)e.Attribute("PartName") == "/" + part).ToArray();
        string? type = overrides.Length == 1 ? (string?)overrides[0].Attribute("ContentType") : null;
        if (overrides.Length > 1) throw new InvalidDataException("Binding.Store: Ambiguous part content type.");
        if (overrides.Length == 0)
        {
            string extension = part.Substring(part.LastIndexOf('.') + 1);
            XElement[] defaults = types.Elements(DocxPackage.ContentTypes + "Default").Where(e => (string?)e.Attribute("Extension") == extension).ToArray();
            if (defaults.Length == 1) type = (string?)defaults[0].Attribute("ContentType");
        }
        if (type != expectedType) throw new InvalidDataException("Binding.Store: A control part has an unexpected content type.");
    }
    private static string ControlPart(string owner, XElement relation)
    {
        string? mode = (string?)relation.Attribute("TargetMode");
        if (mode != null && mode != "Internal") throw new InvalidDataException("Binding.Store: External or unknown relationship modes are prohibited.");
        string target = DocxPackage.RequiredAttribute(relation, "Target");
        return target.Length > 0 && target[0] == '/' ? PackageArchive.Resolve("", target.Substring(1)) : PackageArchive.Resolve(owner, target);
    }
}
