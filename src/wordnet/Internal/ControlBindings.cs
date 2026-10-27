using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using WordNet.Model;

namespace WordNet.Internal;

internal static class ControlBindings
{
    internal static string StoreId(string value)
    {
        if (!Guid.TryParseExact(value, "B", out Guid guid)) throw new InvalidDataException("Binding.Store: A braced local store GUID is required.");
        return guid.ToString("B", CultureInfo.InvariantCulture).ToUpperInvariant();
    }
    internal static ControlBinding Read(XElement element, ConversionContext c)
    {
        var data = new ControlBinding { StoreId = StoreId(Required(element, "storeItemID")), Path = Required(element, "xpath") };
        string mappings = (string?)element.Attribute(ControlValues.W + "prefixMappings") ?? "";
        if (mappings.Length > 16 * 1100) throw new InvalidDataException("Limit.Controls: Namespace mappings exceed their bound.");
        int at = 0;
        while (at < mappings.Length)
        {
            while (at < mappings.Length && char.IsWhiteSpace(mappings[at])) at++;
            if (at == mappings.Length) break;
            Match match = Regex.Match(mappings.Substring(at), "^xmlns:([^=\\s]+)\\s*=\\s*(?:'([^']*)'|\"([^\"]*)\")(?=\\s|$)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (!match.Success) throw new InvalidDataException("Binding.Path: Namespace mappings must be explicit prefix declarations.");
            string prefix = match.Groups[1].Value, uri = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
            Name(prefix);
            if (prefix is "xml" or "xmlns" || uri.Length is 0 or > 1024 || uri == XNamespace.Xml.NamespaceName || uri == XNamespace.Xmlns.NamespaceName || data.Namespaces.ContainsKey(prefix))
                throw new InvalidDataException("Binding.Path: Invalid, duplicate or reserved namespace mapping.");
            if (data.Namespaces.Count == 16) throw new InvalidDataException("Limit.Controls: Too many namespace mappings.");
            data.Namespaces.Add(prefix, uri); at += match.Length; c.ReserveControlWork(match.Length);
        }
        _ = Steps(data, c); return data;
    }
    private static string Required(XElement e, string name) => (string?)e.Attribute(ControlValues.W + name) ?? throw new InvalidDataException("Binding.Path: Missing binding metadata.");
    private static void Name(string value)
    {
        if (value.Length is 0 or > 64) throw new InvalidDataException("Binding.Path: QName components must be bounded names.");
        try { _ = XmlConvert.VerifyNCName(value); }
        catch (XmlException e) { throw new InvalidDataException("Binding.Path: Invalid QName component.", e); }
    }
    private static (XName Name, bool Attribute)[] Steps(ControlBinding data, ConversionContext c)
    {
        string path = data.Path;
        if (path.Length is 0 or > 1024 || path[0] != '/') throw new InvalidDataException("Binding.Path: An absolute bounded child path is required.");
        string[] steps = path.Substring(1).Split('/');
        if (steps.Length > 17) throw new InvalidDataException("Limit.Controls: Binding path exceeds the depth limit.");
        var result = new List<(XName, bool)>();
        for (int i = 0; i < steps.Length; i++)
        {
            string step = steps[i]; bool attribute = step.Length > 0 && step[0] == '@';
            if (attribute) { if (i == 0 || i != steps.Length - 1) throw new InvalidDataException("Binding.Path: An attribute is permitted only as the final step."); step = step.Substring(1); }
            else if (step.EndsWith("[1]", StringComparison.Ordinal)) step = step.Substring(0, step.Length - 3);
            if (i >= 16 && !attribute) throw new InvalidDataException("Limit.Controls: Binding child depth exceeds sixteen.");
            string[] names = step.Split(':'); if (names.Length is < 1 or > 2) throw new InvalidDataException("Binding.Path: Invalid QName.");
            foreach (string n in names) Name(n);
            string local = names[names.Length - 1], ns = "";
            if (names.Length == 2 && !data.Namespaces.TryGetValue(names[0], out ns!)) throw new InvalidDataException("Binding.Path: Undeclared prefix.");
            if (attribute && (local == "xmlns" || ns == XNamespace.Xmlns.NamespaceName || ns == XNamespace.Xml.NamespaceName))
                throw new InvalidDataException("Binding.Path: Reserved namespace attributes cannot be updated.");
            result.Add((XName.Get(local, ns), attribute)); c.ReserveControlWork(step.Length + 1);
        }
        return result.ToArray();
    }
    internal static XObject Resolve(ControlBinding data, ContentControlStore store, ConversionContext c)
    {
        if (!store.XmlStores.TryGetValue(data.StoreId, out ControlXmlStore? part)) throw new InvalidDataException("Binding.Store: The referenced local XML store is missing.");
        var steps = Steps(data, c); XElement element = part.Xml;
        if (element.Name != steps[0].Name) throw new InvalidDataException("Binding.Path: The path does not resolve to exactly one target.");
        for (int i = 1; i < steps.Length; i++)
        {
            c.CheckCancellation();
            if (steps[i].Attribute) return element.Attribute(steps[i].Name) ?? throw new InvalidDataException("Binding.Path: The target attribute is missing.");
            XElement? next = null;
            foreach (XElement child in element.Elements())
            {
                c.ReserveControlWork(1);
                if (child.Name != steps[i].Name) continue;
                if (next != null) throw new InvalidDataException("Binding.Path: A path step has multiple matches; [1] cannot hide ambiguity.");
                next = child;
            }
            element = next ?? throw new InvalidDataException("Binding.Path: A path step has no match.");
        }
        if (element.HasElements) throw new InvalidDataException("Binding.Path: Element targets must contain only literal text.");
        return element;
    }
    internal static string Value(XObject target) => target is XAttribute a ? a.Value : ((XElement)target).Value;
    private static void Write(XObject target, string value) { if (target is XAttribute a) a.Value = value; else ((XElement)target).Value = value; }
    internal static void Apply(ControlData data, ControlOwner owner, ContentControlStore store, ConversionContext c)
    {
        if (data.Binding == null) throw new ArgumentException("Binding.Store: The selected control has no binding.");
        string text = Value(Resolve(data.Binding, store, c)); ControlValues.CheckText(text, c);
        switch (data.Kind)
        {
            case ContentControlKind.Checkbox:
                bool flag = text switch { "true" or "1" => true, "false" or "0" => false, _ => throw new InvalidDataException("Binding.Type: Invalid Boolean XML value.") };
                ControlValues.SetChecked(data, owner, flag, c); break;
            case ContentControlKind.DropDownList: ControlValues.SetSelection(data, owner, text, c); break;
            case ContentControlKind.ComboBox:
                if (data.Items.Any(i => i.Value == text)) ControlValues.SetSelection(data, owner, text, c);
                else ControlValues.SetCombo(data, owner, text, c);
                break;
            case ContentControlKind.Date:
                string? type = (string?)data.Properties.Element(ControlValues.W + "date")?.Element(ControlValues.W + "storeMappedDataAs")?.Attribute(ControlValues.W + "val");
                ContentControlDateValue date;
                if (store.DateMode == ContentControlDateMode.DateOnly && type == "date")
                {
                    if (!Regex.IsMatch(text, "^[0-9]{4}-[0-9]{2}-[0-9]{2}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) throw new InvalidDataException("Binding.Type: A canonical calendar date is required.");
                    date = ControlValues.ParseDate(text + "T00:00:00Z", ContentControlDateMode.DateOnly);
                }
                else if (store.DateMode == ContentControlDateMode.UtcInstant && type == "dateTime") date = ControlValues.ParseDate(text, ContentControlDateMode.UtcInstant);
                else throw new NotSupportedException("Binding.Type: Date bindings need matching explicit date/dateTime storage; text inference is unsupported.");
                ControlValues.SetDate(data, owner, date, store.DateMode, c); break;
            case ContentControlKind.PlainText: ControlValues.SetPlain(data, owner, text, c); break;
            default: throw new NotSupportedException("Binding.Type: Rich-text XML binding is not supported by OOXML or this profile.");
        }
        SetLastValue(data, text); data.BindingState = ContentControlBindingState.Synchronized;
    }
    private static string Serialize(ControlData data, ControlOwner owner, ContentControlStore store)
    {
        if (data.Placeholder || data.State == ContentControlValueState.Unresolved) throw new InvalidOperationException("Binding.Type: Placeholder/unresolved values cannot be written back.");
        switch (data.Kind)
        {
            case ContentControlKind.Checkbox: return data.Checked ? "true" : "false";
            case ContentControlKind.DropDownList:
            case ContentControlKind.ComboBox: return data.SelectedValue ?? data.FreeText ?? throw new InvalidOperationException("Binding.Type: Choose a concrete list value before write-back.");
            case ContentControlKind.PlainText: return owner.Text;
            case ContentControlKind.Date:
                if (data.DateValue == null) throw new InvalidOperationException("Binding.Type: Choose a date before write-back.");
                string? type = (string?)data.Properties.Element(ControlValues.W + "date")?.Element(ControlValues.W + "storeMappedDataAs")?.Attribute(ControlValues.W + "val");
                if (store.DateMode == ContentControlDateMode.DateOnly && type == "date") return data.DateValue.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (store.DateMode == ContentControlDateMode.UtcInstant && type == "dateTime") return ControlValues.DateLexical(data.DateValue);
                throw new NotSupportedException("Binding.Type: Explicit matching date/dateTime storage is required.");
            default: throw new NotSupportedException("Binding.Type: This control kind cannot be written back.");
        }
    }
    internal static void WriteBack(DocumentModel model, IReadOnlyList<ControlData> selected, Dictionary<int, ControlOwner> owners, ConversionContext c)
    {
        var groups = new Dictionary<XObject, List<ControlData>>();
        foreach (ControlData data in model.Controls.Items.Values.Where(d => d.Binding != null))
        {
            XObject target = Resolve(data.Binding!, model.Controls, c);
            if (!groups.TryGetValue(target, out List<ControlData>? list)) { list = new List<ControlData>(); groups.Add(target, list); }
            list.Add(data);
        }
        var ids = new HashSet<int>(selected.Select(d => d.Id));
        foreach (var group in groups.Where(g => g.Value.Any(d => ids.Contains(d.Id))))
        {
            if (group.Value.Any(d => !ids.Contains(d.Id))) throw new InvalidOperationException("Binding.Conflict: Write-back must include every control sharing the selected XML target.");
            string? value = null;
            foreach (ControlData data in group.Value)
            {
                owners[data.Id].Refresh(data.Id, c);
                string next = Serialize(data, owners[data.Id], model.Controls); ControlValues.CheckText(next, c);
                if (value != null && value != next) throw new InvalidOperationException("Binding.Conflict: Controls sharing a target have divergent values.");
                value = next;
            }
            Write(group.Key, value!);
            foreach (ControlData data in group.Value) { data.BindingState = ContentControlBindingState.Synchronized; SetLastValue(data, value!); }
        }
    }
    private static void SetLastValue(ControlData data, string value)
    {
        if (data.Kind is ContentControlKind.DropDownList or ContentControlKind.ComboBox)
            data.Properties.Element(ControlValues.W + (data.Kind == ContentControlKind.DropDownList ? "dropDownList" : "comboBox"))!.SetAttributeValue(ControlValues.W + "lastValue", value);
    }
}
