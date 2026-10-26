using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using WordNet.Internal;

namespace WordNet.Model;

internal sealed class ContentControlStore
{
    internal ContentControlDateMode DateMode { get; set; }
    internal Dictionary<int, ControlData> Items { get; } = new Dictionary<int, ControlData>();
    internal Dictionary<string, ControlXmlStore> XmlStores { get; } = new Dictionary<string, ControlXmlStore>(StringComparer.OrdinalIgnoreCase);
    internal string? GlossaryPart { get; set; }
    internal XElement? Glossary { get; set; }
    internal Dictionary<string, ParagraphBlock> GlossaryContent { get; } = new Dictionary<string, ParagraphBlock>(StringComparer.Ordinal);
    internal bool HasControls => Items.Count != 0;
    internal ContentControlStore Copy(ConversionContext c)
    {
        var copy = new ContentControlStore { DateMode = DateMode, GlossaryPart = GlossaryPart, Glossary = Glossary == null ? null : new XElement(Glossary) };
        foreach (var entry in GlossaryContent) copy.GlossaryContent.Add(entry.Key, entry.Value);
        foreach (var item in Items) { c.CheckCancellation(); copy.Items.Add(item.Key, item.Value.Copy(c)); }
        foreach (var item in XmlStores) { c.CheckCancellation(); copy.XmlStores.Add(item.Key, item.Value.Copy()); }
        return copy;
    }
    internal long Admit(DocumentModel model, ConversionContext c)
    {
        if (!HasControls) return 0;
        if (Items.Count > 1024 || XmlStores.Count > 64) throw new InvalidDataException("Limit.Controls: Too many controls or local XML stores.");
        if (model.Review.HasHistory || model.ObjectCount != 0) throw new NotSupportedException("Control.ProtectedOwner: Retained review/objects cannot be combined with typed controls.");
        long chars = 0, nodes = 0; int listItems = 0; var placeholderCopies = new HashSet<ParagraphBlock>();
        foreach (ControlData item in Items.Values)
        {
            c.CheckCancellation(); item.Validate(c); listItems = checked(listItems + item.Items.Count);
            if (listItems > 4096) throw new InvalidDataException("Limit.Controls: Aggregate list items exceed the supported bound.");
            Count(item.Properties); if (item.EndProperties != null) Count(item.EndProperties);
            foreach (ParagraphBlock p in item.PlaceholderContent)
            {
                if (!placeholderCopies.Add(p)) { nodes++; continue; }
                chars = checked(chars + p.Text.Length); nodes = checked(nodes + p.Runs.Count + 1);
                foreach (TextRun r in p.Runs) chars = checked(chars + (r.Appearance.FontFamily?.Length ?? 0));
            }
        }
        if (Glossary != null) Count(Glossary);
        foreach (ControlXmlStore store in XmlStores.Values) { Count(store.Xml); Count(store.Properties); nodes += 3; chars += store.Part.Length + store.PropertiesPart.Length + store.Id.Length; }
        if (nodes > Math.Min(100000L, c.Options.MaximumCharacters) || chars > c.Options.MaximumCharacters || chars * 2 + nodes * 64 > c.Options.MaximumExpandedBytes)
            throw new InvalidDataException("Limit.Controls: Retained control/XML metadata exceed aggregate resource limits.");
        _ = ControlOwners.Index(model, c);
        return checked(chars + nodes * 32);

        void Count(XElement root)
        {
            foreach (XElement element in root.DescendantsAndSelf())
            {
                c.CheckCancellation(); c.ReserveControlWork(1); nodes++; chars = checked(chars + element.Name.LocalName.Length + element.Name.NamespaceName.Length);
                foreach (XAttribute a in element.Attributes()) { nodes++; chars = checked(chars + a.Value.Length + a.Name.LocalName.Length + a.Name.NamespaceName.Length); }
                foreach (XNode node in element.Nodes())
                {
                    if (node is XText t) { nodes++; chars = checked(chars + t.Value.Length); }
                    else if (node is XComment comment) { nodes++; chars = checked(chars + comment.Value.Length); }
                    else if (node is not XElement) throw new InvalidDataException("Binding.Store: Processing instructions and unsupported XML nodes are prohibited.");
                }
                if (nodes > Math.Min(100000L, c.Options.MaximumCharacters) || chars > c.Options.MaximumCharacters)
                    throw new InvalidDataException("Limit.Controls: Aggregate XML/control metadata exceed admission limits.");
            }
        }
    }
}

internal sealed class ControlData
{
    internal int Id { get; set; }
    internal Guid Identity { get; set; } = Guid.NewGuid();
    internal ContentControlKind Kind { get; set; }
    internal bool Inline { get; set; }
    internal XElement Properties { get; set; } = new XElement(ControlValues.W + "sdtPr");
    internal XElement? EndProperties { get; set; }
    internal string? Tag => (string?)Properties.Element(ControlValues.W + "tag")?.Attribute(ControlValues.W + "val");
    internal string? Alias => (string?)Properties.Element(ControlValues.W + "alias")?.Attribute(ControlValues.W + "val");
    internal ContentControlLock Lock { get; set; }
    internal bool Placeholder { get; set; }
    internal TextRun? InputStyle { get; set; }
    internal IReadOnlyList<ParagraphBlock> PlaceholderContent { get; set; } = Array.Empty<ParagraphBlock>();
    internal List<ContentControlListItem> Items { get; } = new List<ContentControlListItem>();
    internal bool Checked { get; set; }
    internal string? SelectedValue { get; set; }
    internal string? FreeText { get; set; }
    internal ContentControlDateValue? DateValue { get; set; }
    internal ContentControlValueState State { get; set; }
    internal ControlBinding? Binding { get; set; }
    internal ContentControlBindingState BindingState { get; set; }
    internal ControlData Copy(ConversionContext c)
    {
        var copy = new ControlData
        {
            Id = Id,
            Identity = Identity,
            Kind = Kind,
            Inline = Inline,
            Properties = new XElement(Properties),
            EndProperties = EndProperties == null ? null : new XElement(EndProperties),
            Lock = Lock,
            Placeholder = Placeholder,
            Checked = Checked,
            SelectedValue = SelectedValue,
            FreeText = FreeText,
            DateValue = DateValue,
            State = State,
            Binding = Binding?.Copy(),
            BindingState = BindingState,
            InputStyle = InputStyle == null ? null : new TextRun("", InputStyle.Bold, InputStyle.Italic, InputStyle.Code, fontSize: InputStyle.FontSize, color: InputStyle.Color, appearance: InputStyle.Appearance.Copy())
        };
        copy.Items.AddRange(Items);
        copy.PlaceholderContent = PlaceholderContent;
        return copy;
    }
    internal void Validate(ConversionContext c)
    {
        if (Tag?.Length > 256 || Alias?.Length > 256 || Items.Count > 256)
            throw new InvalidDataException("Limit.Controls: Tag, alias or item collection exceeds the supported bound.");
        if (Items.Any(i => i.Value.Length > 1024 || i.Label.Length is 0 or > 1024)
            || Items.Select(i => i.Value).Distinct(StringComparer.Ordinal).Count() != Items.Count
            || Items.Select(i => i.Label).Distinct(StringComparer.Ordinal).Count() != Items.Count)
            throw new InvalidDataException("Control.Malformed: List values and nonempty effective labels must be unique and bounded.");
        c.ReserveControlWork(Items.Count + 1);
    }
}

internal sealed class ControlXmlStore
{
    internal string Id { get; set; } = "";
    internal string Part { get; set; } = "";
    internal string PropertiesPart { get; set; } = "";
    internal XElement Xml { get; set; } = null!;
    internal XElement Properties { get; set; } = null!;
    internal ControlXmlStore Copy() => new ControlXmlStore { Id = Id, Part = Part, PropertiesPart = PropertiesPart, Xml = new XElement(Xml), Properties = new XElement(Properties) };
}

internal sealed class ControlBinding
{
    internal string StoreId { get; set; } = "";
    internal string Path { get; set; } = "";
    internal Dictionary<string, string> Namespaces { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
    internal ControlBinding Copy()
    { var copy = new ControlBinding { StoreId = StoreId, Path = Path }; foreach (var n in Namespaces) copy.Namespaces.Add(n.Key, n.Value); return copy; }
}
