using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxReader
{
    private void ReadGlossaryResources(string glossaryPart, XElement contentTypes)
    {
        Dictionary<string, XElement> relations = package.ReadRelationships(glossaryPart);
        if (relations.Count > 4) throw new InvalidDataException("Limit.Controls: Too many glossary resource relationships.");
        foreach (XElement relation in relations.Values)
        {
            context.ReserveControlWork(1);
            Leaf(relation, "Id", "Type", "Target", "TargetMode");
            string id = DocxPackage.RequiredAttribute(relation, "Id");
            if (id.Length is 0 or > 256) throw new InvalidDataException("Limit.Controls: Glossary relationship identifiers must be bounded.");
            try { _ = XmlConvert.VerifyNCName(id); }
            catch (XmlException error) { throw new InvalidDataException("Control.Malformed: Invalid glossary relationship identifier.", error); }
            string prefix = DocxPackage.DocumentRelationships.NamespaceName + "/";
            string type = DocxPackage.RequiredAttribute(relation, "Type");
            if (!type.StartsWith(prefix, StringComparison.Ordinal)) throw new NotSupportedException("Control.Unsupported: Unknown glossary relationship type.");
            string kind = type.Substring(prefix.Length);
            if (kind is not "styles" and not "settings" and not "fontTable" and not "webSettings")
                throw new NotSupportedException("Control.Unsupported: Unknown or active glossary resource relationship.");
            if (typedControls.GlossaryResources.ContainsKey(kind)) throw new InvalidDataException("Control.Malformed: Duplicate glossary resource type.");
            string part = ControlPart(glossaryPart, relation);
            if (part != "word/glossary/" + kind + ".xml" || part == glossaryPart)
                throw new NotSupportedException("Control.Unsupported: Glossary resources must use their dedicated local package parts.");
            string contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml." + kind + "+xml";
            RequireControlPart(part, contentType, contentTypes);
            if (package.Contains(DocxPackage.RelationshipPartName(part)))
                throw new NotSupportedException("Control.Unsupported: Nested glossary resource relationships are not admitted.");
            XElement root = TypedXml(part);
            ValidateGlossaryResource(kind, root);
            typedControls.GlossaryResources.Add(kind, new ControlGlossaryResource
            {
                Part = part,
                ContentType = contentType,
                Relationship = new XElement(relation),
                Xml = root,
                Bytes = package.ReadBytes(part)
            });
            knownParts.Add(part);
        }
    }

    private void ValidateGlossaryResource(string kind, XElement root)
    {
        XName expected = W + (kind == "fontTable" ? "fonts" : kind);
        if (root.Name != expected) throw new InvalidDataException("Control.Malformed: Invalid glossary resource root.");
        Attributes(root);
        if (kind == "styles")
        {
            if (root.HasElements) throw new NotSupportedException("Control.Unsupported: Glossary style definitions require separate qualification.");
            return;
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (XElement child in root.Elements())
        {
            context.ReserveControlWork(1);
            if (kind != "fontTable" && !names.Add(child.Name.LocalName)) throw new InvalidDataException("Control.Malformed: Duplicate glossary resource metadata.");
            if (kind == "settings" && child.Name == W + "proofState")
            {
                Leaf(child, W + "spelling", W + "grammar");
                if (child.Attributes().Any(a => !a.IsNamespaceDeclaration && a.Value is not "clean" and not "dirty"))
                    throw new InvalidDataException("Control.Malformed: Invalid proofing state.");
            }
            else if (kind == "settings" && child.Name == W + "zoom")
            {
                Leaf(child, W + "percent", W + "val");
                if (child.Attribute(W + "percent") is XAttribute percent && (!int.TryParse(percent.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value is < 1 or > 500))
                    throw new InvalidDataException("Control.Malformed: Invalid glossary zoom percentage.");
                if ((string?)child.Attribute(W + "val") is string zoom && zoom is not "none" and not "fullPage" and not "bestFit" and not "textFit")
                    throw new InvalidDataException("Control.Malformed: Invalid glossary zoom mode.");
            }
            else if (kind == "settings" && child.Name == W + "rsids")
            {
                Attributes(child); int roots = 0;
                foreach (XElement rsid in child.Elements())
                {
                    context.ReserveControlWork(1);
                    if (rsid.Name != W + "rsid" && rsid.Name != W + "rsidRoot") throw new NotSupportedException("Control.Unsupported: Unknown glossary revision-session metadata.");
                    if (rsid.Name == W + "rsidRoot" && ++roots > 1) throw new InvalidDataException("Control.Malformed: Duplicate glossary revision-session root.");
                    Leaf(rsid, W + "val"); Hex(DocxPackage.RequiredAttribute(rsid, W + "val"), 8);
                }
            }
            else if (kind == "webSettings" && (child.Name == W + "allowPNG" || child.Name == W + "optimizeForBrowser"))
            { Leaf(child, W + "val"); _ = ControlValues.On(child, W); }
            else if (kind == "fontTable" && child.Name == W + "font")
            {
                Attributes(child, W + "name");
                string font = DocxPackage.RequiredAttribute(child, W + "name");
                if (font.Length is 0 or > 256 || !names.Add(font)) throw new InvalidDataException("Control.Malformed: Duplicate or invalid glossary font name.");
                var properties = new HashSet<XName>();
                foreach (XElement property in child.Elements())
                {
                    context.ReserveControlWork(1);
                    if (!properties.Add(property.Name)) throw new InvalidDataException("Control.Malformed: Duplicate glossary font metadata.");
                    if (property.Name == W + "family" || property.Name == W + "pitch")
                    {
                        Leaf(property, W + "val"); string value = DocxPackage.RequiredAttribute(property, W + "val");
                        if (property.Name == W + "family" && value is not "auto" and not "roman" and not "swiss" and not "modern" and not "script" and not "decorative"
                            || property.Name == W + "pitch" && value is not "default" and not "fixed" and not "variable")
                            throw new InvalidDataException("Control.Malformed: Invalid glossary font metadata.");
                    }
                    else if (property.Name == W + "charset" || property.Name == W + "panose1")
                    { Leaf(property, W + "val"); Hex(DocxPackage.RequiredAttribute(property, W + "val"), property.Name == W + "charset" ? 2 : 20); }
                    else if (property.Name == W + "sig")
                    {
                        XName[] signature = { W + "usb0", W + "usb1", W + "usb2", W + "usb3", W + "csb0", W + "csb1" };
                        Leaf(property, signature);
                        foreach (XName name in signature) Hex(DocxPackage.RequiredAttribute(property, name), 8);
                    }
                    else throw new NotSupportedException("Control.Unsupported: Unknown or embedded glossary font metadata.");
                }
            }
            else throw new NotSupportedException("Control.Unsupported: Unknown or active glossary resource metadata.");
        }
        static void Hex(string value, int length)
        {
            if (value.Length != length || value.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("Control.Malformed: Invalid hexadecimal glossary metadata.");
        }
    }
}
