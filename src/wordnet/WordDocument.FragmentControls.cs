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
        if (incoming.Glossary != null || incoming.GlossaryResources.Count != 0 || incoming.GlossaryContent.Count != 0)
            throw new NotSupportedException("Fragment.ControlResources: Glossary-backed control fragments require a separate resource merge.");
        if (incoming.Items.Values.Any(item => item.Kind == ContentControlKind.Date) && incoming.DateMode != target.DateMode)
            throw new NotSupportedException("Fragment.ControlDateMode: Source and destination date policies must match for date controls.");
        if (incoming.Items.Count + target.Items.Count > 1024 || incoming.XmlStores.Count + target.XmlStores.Count > 64)
            throw new InvalidDataException("Limit.Controls: Combined control and local XML store counts exceed their limits.");

        var parts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ControlXmlStore store in target.XmlStores.Values) { parts.Add(store.Part); parts.Add(store.PropertiesPart); }
        if (target.GlossaryPart != null) parts.Add(target.GlossaryPart);
        foreach (ControlGlossaryResource resource in target.GlossaryResources.Values) parts.Add(resource.Part);
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

    private static byte[] FragmentStoreHash(byte[] value)
    {
#if NET8_0_OR_GREATER
        return SHA256.HashData(value);
#else
        using var hash = SHA256.Create(); return hash.ComputeHash(value);
#endif
    }
}
