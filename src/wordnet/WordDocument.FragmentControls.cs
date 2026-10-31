using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet;

public sealed partial class WordDocument
{
    private static bool FragmentHasReportControls(DocumentModel model, ConversionContext c)
    {
        foreach (DocumentBlock block in DocumentModel.Expand(model.AllBlocks).Distinct())
        {
            c.ReserveControlWork(1);
            if (block.Control != null || block.NestedControl != null || block is TableBlock table && table.RowControls.Count != 0) return true;
            if (block is ParagraphBlock paragraph)
                foreach (TextRun run in paragraph.Runs)
                { c.ReserveControlWork(1); if (run.Control != null) return true; }
        }
        return false;
    }

    private static void MergeFragmentControls(DocumentModel destination, DocumentModel source, HashSet<int> usedIds, ref int nextId, ConversionContext c)
    {
        ContentControlStore incoming = source.Controls, target = destination.Controls;
        if (!incoming.HasControls) return;
        if (incoming.Items.Values.Any(item => item.Kind == ContentControlKind.Date) && incoming.DateMode != target.DateMode)
            throw new NotSupportedException("Fragment.ControlDateMode: Source and destination date policies must match for date controls.");
        if (incoming.Items.Count + target.Items.Count > 1024 || incoming.XmlStores.Count + target.XmlStores.Count > 64)
            throw new InvalidDataException("Limit.Controls: Combined control and local XML store counts exceed their limits.");

        var parts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ControlXmlStore store in target.XmlStores.Values) { parts.Add(store.Part); parts.Add(store.PropertiesPart); }
        if (target.GlossaryPart != null) parts.Add(target.GlossaryPart);
        foreach (ControlGlossaryResource resource in target.GlossaryResources.Values) parts.Add(resource.Part);
        MergeFragmentGlossary(target, incoming, parts, c);
        var stores = new HashSet<string>(target.XmlStores.Keys, StringComparer.OrdinalIgnoreCase);
        var storeIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        ControlXmlStore[] incomingStores = incoming.XmlStores.Values.ToArray(); incoming.XmlStores.Clear();
        stores.UnionWith(incomingStores.Select(store => store.Id));
        int partIndex = 1; XNamespace custom = "http://schemas.openxmlformats.org/officeDocument/2006/customXml";
        foreach (ControlXmlStore store in incomingStores)
        {
            c.ReserveControlWork(1); string id; int nonce = 1;
            do
            {
                c.ReserveControlWork(1);
                byte[] hash = FragmentStoreHash(Encoding.UTF8.GetBytes("wordnet-fragment-store\n" + store.Id + "\n" + (nonce++).ToString(CultureInfo.InvariantCulture)));
                var bytes = new byte[16]; Array.Copy(hash, bytes, bytes.Length);
                id = new Guid(bytes).ToString("B").ToUpperInvariant();
            } while (!stores.Add(id));
            storeIds.Add(store.Id, id); store.Id = id;
            string part, properties;
            do
            {
                c.ReserveControlWork(1); string suffix = (partIndex++).ToString(CultureInfo.InvariantCulture);
                part = "customXml/wordnet-fragment-" + suffix + ".xml";
                properties = "customXml/wordnet-fragment-props-" + suffix + ".xml";
            } while (parts.Contains(part) || parts.Contains(properties));
            parts.Add(part); parts.Add(properties); store.Part = part; store.PropertiesPart = properties;
            store.Properties.SetAttributeValue(custom + "itemID", id);
            incoming.XmlStores.Add(id, store);
        }

        var ids = new Dictionary<int, int>(); ControlData[] controls = incoming.Items.Values.ToArray(); incoming.Items.Clear();
        foreach (ControlData data in controls)
        {
            c.ReserveControlWork(1);
            while (usedIds.Contains(nextId)) { c.ReserveControlWork(1); nextId = checked(nextId + 1); }
            int id = nextId; usedIds.Add(id); nextId = checked(nextId + 1); ids.Add(data.Id, id);
            data.Id = id; data.Identity = Guid.NewGuid();
            XElement identity = data.Properties.Element(ControlValues.W + "id") ?? throw new InvalidDataException("Control.Malformed: A retained control requires an identity.");
            identity.SetAttributeValue(ControlValues.W + "val", id);
            if (data.Binding != null)
            {
                if (!storeIds.TryGetValue(data.Binding.StoreId, out string? storeId))
                    throw new InvalidDataException("Binding.Store: A fragment binding references an unavailable local XML store.");
                data.Binding.StoreId = storeId;
                XElement binding = data.Properties.Element(ControlValues.W + "dataBinding") ?? throw new InvalidDataException("Binding.Store: A fragment binding is missing its retained properties.");
                binding.SetAttributeValue(ControlValues.W + "storeItemID", storeId);
            }
            incoming.Items.Add(id, data);
        }
        foreach (DocumentBlock block in DocumentModel.Expand(source.AllBlocks).Distinct())
        {
            c.ReserveControlWork(1);
            if (block.TypedControlId is int blockId) block.TypedControlId = ids[blockId];
            if (block is TableBlock table)
            {
                foreach (int row in table.TypedRowControls.Keys.ToArray()) { c.ReserveControlWork(1); table.TypedRowControls[row] = ids[table.TypedRowControls[row]]; }
                foreach (TableCell cell in table.Rows.SelectMany(row => row))
                { c.ReserveControlWork(1); if (cell.TypedControlId is int cellId) cell.TypedControlId = ids[cellId]; }
            }
            if (block is ParagraphBlock paragraph)
                foreach (TextRun run in paragraph.Runs)
                { c.ReserveControlWork(1); if (run.TypedControlId is int runId) run.TypedControlId = ids[runId]; }
        }
        foreach (ControlXmlStore store in incoming.XmlStores.Values) target.XmlStores.Add(store.Id, store);
        foreach (ControlData data in incoming.Items.Values) target.Items.Add(data.Id, data);
    }

    private static void MergeFragmentGlossary(ContentControlStore target, ContentControlStore incoming, HashSet<string> parts, ConversionContext c)
    {
        if (incoming.Glossary == null)
        {
            if (incoming.GlossaryContent.Count != 0 || incoming.GlossaryResources.Count != 0) throw new InvalidDataException("Control.Malformed: Fragment glossary entries have no glossary part.");
            return;
        }
        if (target.GlossaryContent.Count + incoming.GlossaryContent.Count > 128)
            throw new InvalidDataException("Limit.Controls: Combined glossary entries exceed their limit.");
        XNamespace w = ControlValues.W;
        XElement sourceEntries = incoming.Glossary.Element(w + "docParts") ?? throw new InvalidDataException("Control.Malformed: Missing fragment glossary entries.");
        if (target.Glossary == null)
        {
            int index = 1; string part;
            do { c.ReserveControlWork(1); part = "word/glossary/wordnet-fragment-" + (index++).ToString(CultureInfo.InvariantCulture) + ".xml"; }
            while (!parts.Add(part));
            target.GlossaryPart = part;
            target.Glossary = new XElement(w + "glossaryDocument", new XElement(w + "docParts"));
        }
        XElement destinationEntries = target.Glossary.Element(w + "docParts") ?? throw new InvalidDataException("Control.Malformed: Missing destination glossary entries.");
        var relationIds = new HashSet<string>(target.GlossaryResources.Values.Select(resource => (string?)resource.Relationship.Attribute("Id") ?? ""), StringComparer.Ordinal);
        int nextRelation = 1;
        foreach (var item in incoming.GlossaryResources.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            c.ReserveControlWork(1); ControlGlossaryResource source = item.Value;
            if (target.GlossaryResources.TryGetValue(item.Key, out ControlGlossaryResource? existing))
            {
                if (existing.ContentType != source.ContentType || !FragmentResourceBytesEqual(existing.Bytes, source.Bytes, c))
                    throw new NotSupportedException("Fragment.ControlResources: Conflicting glossary resources require an explicit merge.");
                continue;
            }
            if (!parts.Add(source.Part)) throw new NotSupportedException("Fragment.ControlResources: A glossary resource conflicts with another retained part.");
            string id; do { c.ReserveControlWork(1); id = "rFragmentGlossary" + (nextRelation++).ToString(CultureInfo.InvariantCulture); } while (!relationIds.Add(id));
            ControlGlossaryResource copy = source.Copy(); copy.Relationship.SetAttributeValue("Id", id);
            copy.Relationship.SetAttributeValue("Target", "/" + copy.Part); target.GlossaryResources.Add(item.Key, copy);
        }
        var names = new HashSet<string>(target.GlossaryContent.Keys, StringComparer.OrdinalIgnoreCase);
        names.UnionWith(incoming.GlossaryContent.Keys);
        var guids = new HashSet<string>(destinationEntries.Elements(w + "docPart").Select(entry => (string?)entry.Element(w + "docPartPr")?.Element(w + "guid")?.Attribute(w + "val"))
            .Where(value => value != null).Select(value => value!), StringComparer.OrdinalIgnoreCase);
        var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
        int nextName = 1;
        foreach (XElement entry in sourceEntries.Elements(w + "docPart"))
        {
            c.ReserveControlWork(1); var copy = new XElement(entry);
            XElement properties = copy.Element(w + "docPartPr") ?? throw new InvalidDataException("Control.Malformed: A glossary entry lacks properties.");
            XElement nameElement = properties.Element(w + "name") ?? throw new InvalidDataException("Control.Malformed: A glossary entry lacks a name.");
            string oldName = (string?)nameElement.Attribute(w + "val") ?? throw new InvalidDataException("Control.Malformed: A glossary entry lacks a name value.");
            if (!incoming.GlossaryContent.TryGetValue(oldName, out ParagraphBlock? content)) throw new InvalidDataException("Control.Malformed: A glossary entry lacks retained content.");
            string name; do { c.ReserveControlWork(1); name = "wordnet-fragment-" + (nextName++).ToString(CultureInfo.InvariantCulture); } while (!names.Add(name));
            renamed.Add(oldName, name); nameElement.SetAttributeValue(w + "val", name);
            if (properties.Element(w + "guid") is XElement guid)
            {
                string oldGuid = (string?)guid.Attribute(w + "val") ?? throw new InvalidDataException("Control.Malformed: A glossary GUID lacks a value.");
                int nonce = 1; string id;
                do
                {
                    c.ReserveControlWork(1);
                    byte[] hash = FragmentStoreHash(Encoding.UTF8.GetBytes("wordnet-fragment-glossary\n" + oldGuid + "\n" + (nonce++).ToString(CultureInfo.InvariantCulture)));
                    var bytes = new byte[16]; Array.Copy(hash, bytes, bytes.Length); id = new Guid(bytes).ToString("B").ToUpperInvariant();
                } while (!guids.Add(id));
                guid.SetAttributeValue(w + "val", id);
            }
            destinationEntries.Add(copy); target.GlossaryContent.Add(name, content);
        }
        if (renamed.Count != incoming.GlossaryContent.Count) throw new InvalidDataException("Control.Malformed: Fragment glossary metadata and content disagree.");
        foreach (ControlData data in incoming.Items.Values)
            if (data.Properties.Element(w + "placeholder")?.Element(w + "docPart") is XElement reference)
            {
                c.ReserveControlWork(1); string old = (string?)reference.Attribute(w + "val") ?? throw new InvalidDataException("Control.Malformed: A placeholder reference lacks a name.");
                if (!renamed.TryGetValue(old, out string? name)) throw new InvalidDataException("Control.Malformed: A fragment placeholder does not resolve locally.");
                reference.SetAttributeValue(w + "val", name);
            }
    }

    private static bool FragmentResourceBytesEqual(byte[] first, byte[] second, ConversionContext c)
    {
        if (first.Length != second.Length) return false;
        for (int i = 0; i < first.Length; i++)
        {
            if ((i & 4095) == 0) { c.CheckCancellation(); c.ReserveControlWork(1); }
            if (first[i] != second[i]) return false;
        }
        return true;
    }

    private static byte[] FragmentStoreHash(byte[] value)
    {
#if NET8_0_OR_GREATER
        return SHA256.HashData(value);
#else
        using var hash = SHA256.Create(); return hash.ComputeHash(value);
#endif
    }
}
