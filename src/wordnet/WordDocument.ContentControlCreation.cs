using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet;

public sealed partial class WordDocument
{
    /// <summary>Inserts one typed inline control at a UTF-16 offset in an ordinary unstyled body or simple-cell paragraph and returns a fresh handle.</summary>
    /// <remarks>Offsets must be scalar boundaries. Existing controls, fields, links, bookmarks, objects, lists, nested tables and styled or protected owners are rejected. Bindings resolve only against an existing local XML store.</remarks>
    public DocumentContentControl InsertContentControl(int paragraphIndex, int offset, ContentControlDefinition definition, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(definition, nameof(definition));
        lock (gate)
        {
            int assigned = 0;
            _ = Edit((m, c) =>
            {
                ParagraphBlock? paragraph = null; int current = 0;
                if (paragraphIndex >= 0)
                    foreach (ParagraphBlock candidate in m.BodyParagraphs)
                    {
                        c.ReserveControlWork(1);
                        if (current++ == paragraphIndex) { paragraph = candidate; break; }
                    }
                if (paragraph == null) throw new ArgumentOutOfRangeException(nameof(paragraphIndex));
                foreach (TextRun existing in paragraph.Runs) c.ReserveControlWork(1);
                if (!AdmittedControlInsertionOwner(m, paragraph, c) || !ContentProfiles.OrdinaryParagraph(paragraph)
                    || paragraph.StyleId != null || paragraph.HeadingLevel != 0 || paragraph.IsCodeBlock
                    || paragraph.Layout.Left != 0 || paragraph.Layout.Right != 0 || paragraph.Layout.FirstLine != 0 || paragraph.Layout.Tabs.Count != 0
                    || paragraph.Runs.Any(run => run.Text.Contains('\f') || run.Text.Contains('\v')))
                    throw new NotSupportedException("Control.ProtectedOwner: Creation requires an ordinary unstyled body or simple-cell paragraph without protected content.");
                string text = paragraph.Text;
                if (offset < 0 || offset > text.Length) throw new ArgumentOutOfRangeException(nameof(offset));
                if (offset > 0 && offset < text.Length && char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]))
                    throw new ArgumentException("Control.Location: The offset cannot split a Unicode scalar.", nameof(offset));
                if (m.Controls.Items.Count == 1024) throw new InvalidDataException("Limit.Controls: At most 1024 typed controls are admitted.");
                assigned = 1; while (m.Controls.Items.ContainsKey(assigned)) assigned++;
                ControlData data = CreateControlData(definition, assigned, c);
                if (definition.Binding != null)
                {
                    XElement binding = ControlBindingElement(definition.Binding);
                    data.Binding = ControlBindings.Read(binding, c); _ = ControlBindings.Resolve(data.Binding, m.Controls, c);
                    data.Properties.Add(binding);
                }
                int runIndex = SplitControlInsertionRun(paragraph, offset, c);
                var run = new TextRun("") { TypedControlId = assigned };
                paragraph.Runs.Insert(runIndex, run); m.Controls.Items.Add(assigned, data);
                ControlOwner owner = ControlOwners.Index(m, c)[assigned];
                switch (data.Kind)
                {
                    case ContentControlKind.PlainText: ControlValues.SetPlain(data, owner, definition.InitialText ?? "", c); break;
                    case ContentControlKind.RichText:
                        string richText = definition.InitialText ?? ""; ControlValues.CheckText(richText, c);
                        owner.SetRuns(assigned, new[] { new TextRun(richText) }); data.State = richText.Length == 0 ? ContentControlValueState.Empty : ContentControlValueState.Value; break;
                    case ContentControlKind.Checkbox: ControlValues.SetChecked(data, owner, definition.IsChecked, c); break;
                    case ContentControlKind.DropDownList: ControlValues.SetSelection(data, owner, definition.SelectedValue, c); break;
                    case ContentControlKind.ComboBox:
                        if (definition.SelectedValue != null) ControlValues.SetSelection(data, owner, definition.SelectedValue, c);
                        else if (definition.InitialText == null) ControlValues.SetSelection(data, owner, null, c);
                        else ControlValues.SetCombo(data, owner, definition.InitialText, c);
                        break;
                    case ContentControlKind.Date:
                        _ = ControlValues.DateDisplay(data, m.Controls.DateMode == ContentControlDateMode.DateOnly ? ContentControlDateValue.FromDate(2000, 1, 1)
                            : ContentControlDateValue.FromInstant(new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero)));
                        ControlValues.SetDate(data, owner, definition.DateValue, m.Controls.DateMode, c); break;
                }
                if (data.Binding != null) data.BindingState = ContentControlBindingState.Unevaluated;
                data.Lock = definition.Lock;
                if (data.Lock != ContentControlLock.Unlocked) data.Properties.Add(new XElement(ControlValues.W + "lock", new XAttribute(ControlValues.W + "val", data.Lock switch
                { ContentControlLock.ControlLocked => "sdtLocked", ContentControlLock.ContentLocked => "contentLocked", _ => "sdtContentLocked" })));
                ControlOwners.Normalize(m); return 1;
            }, cancellationToken, controlAware: true);
            return ContentControls.Single(control => control.Id == assigned);
        }
    }

    private static ControlData CreateControlData(ContentControlDefinition definition, int id, ConversionContext c)
    {
        XNamespace w = ControlValues.W, w14 = ControlValues.W14;
        var data = new ControlData { Id = id, Inline = true, Kind = definition.Kind, InputStyle = new TextRun("") };
        data.Properties.Add(new XElement(w + "id", new XAttribute(w + "val", id)));
        if (definition.Tag != null) { ControlValues.CheckText(definition.Tag, c); data.Properties.Add(new XElement(w + "tag", new XAttribute(w + "val", definition.Tag))); }
        if (definition.Alias != null) { ControlValues.CheckText(definition.Alias, c); data.Properties.Add(new XElement(w + "alias", new XAttribute(w + "val", definition.Alias))); }
        if (definition.Kind == ContentControlKind.Date) ControlValues.CheckText(definition.DateFormat, c);
        foreach (ContentControlListItem item in definition.Items)
        { ControlValues.CheckText(item.Value, c); if (item.DisplayText != null) ControlValues.CheckText(item.DisplayText, c); data.Items.Add(item); }
        XElement type = definition.Kind switch
        {
            ContentControlKind.PlainText => new XElement(w + "text", definition.MultiLine ? new XAttribute(w + "multiLine", "1") : null),
            ContentControlKind.RichText => new XElement(w + "richText"),
            ContentControlKind.Checkbox => new XElement(w14 + "checkbox", new XElement(w14 + "checked", new XAttribute(w14 + "val", "0")),
                new XElement(w14 + "checkedState", new XAttribute(w14 + "val", "0058"), new XAttribute(w14 + "font", "Noto Sans")),
                new XElement(w14 + "uncheckedState", new XAttribute(w14 + "val", "002D"), new XAttribute(w14 + "font", "Noto Sans"))),
            ContentControlKind.DropDownList or ContentControlKind.ComboBox => new XElement(w + (definition.Kind == ContentControlKind.DropDownList ? "dropDownList" : "comboBox"),
                definition.Items.Select(item => new XElement(w + "listItem", item.DisplayText == null ? null : new XAttribute(w + "displayText", item.DisplayText), new XAttribute(w + "value", item.Value)))),
            _ => new XElement(w + "date", new XElement(w + "dateFormat", new XAttribute(w + "val", definition.DateFormat)),
                new XElement(w + "lid", new XAttribute(w + "val", "en-US")), new XElement(w + "calendar", new XAttribute(w + "val", "gregorian")))
        };
        data.Properties.Add(type); data.Validate(c); return data;
    }

    private static XElement ControlBindingElement(ContentControlBinding binding)
    {
        XNamespace w = ControlValues.W;
        string mappings = string.Join(" ", binding.NamespaceMappings.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item =>
        {
            string quote = item.Value.Contains('\'') ? "\"" : "'";
            return "xmlns:" + item.Key + "=" + quote + item.Value + quote;
        }));
        return new XElement(w + "dataBinding", new XAttribute(w + "storeItemID", binding.StoreItemId), new XAttribute(w + "xpath", binding.Path),
            mappings.Length == 0 ? null : new XAttribute(w + "prefixMappings", mappings));
    }

    private static int SplitControlInsertionRun(ParagraphBlock paragraph, int offset, ConversionContext c)
    {
        int position = 0;
        for (int index = 0; index < paragraph.Runs.Count; index++)
        {
            c.ReserveControlWork(1); TextRun run = paragraph.Runs[index];
            if (position == offset) return index;
            int end = checked(position + run.Text.Length);
            if (offset < end)
            {
                int split = offset - position;
                paragraph.Runs[index] = run.WithText(run.Text.Substring(0, split));
                paragraph.Runs.Insert(index + 1, run.WithText(run.Text.Substring(split)));
                return index + 1;
            }
            position = end;
        }
        return paragraph.Runs.Count;
    }

    private static bool AdmittedControlInsertionOwner(DocumentModel document, ParagraphBlock paragraph, ConversionContext c)
    {
        foreach (DocumentBlock block in document.Blocks)
        {
            c.ReserveControlWork(1);
            if (ReferenceEquals(block, paragraph)) return true;
            if (block is not TableBlock table) continue;
            bool valid = table.Control == null && table.NestedControl == null && table.RowControls.Count == 0 && table.TypedRowControls.Count == 0, found = false;
            foreach (List<TableCell> row in table.Rows)
            {
                c.ReserveControlWork(1);
                foreach (TableCell cell in row)
                {
                    c.ReserveControlWork(1);
                    if (cell.ColumnSpan != 1 || cell.Merge != VerticalMerge.None || cell.TypedControlId.HasValue) valid = false;
                    foreach (DocumentBlock child in cell.Blocks)
                    {
                        c.ReserveControlWork(1);
                        if (child is TableBlock) valid = false;
                        if (ReferenceEquals(child, paragraph)) found = true;
                    }
                }
            }
            if (found) return valid;
        }
        return false;
    }
}
