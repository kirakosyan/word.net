using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using WordNet.Model;

namespace WordNet.Internal;

internal static class ControlValues
{
    internal static readonly XNamespace W = Formats.DocxPackage.Word;
    internal static readonly XNamespace W14 = "http://schemas.microsoft.com/office/word/2010/wordml";
    internal static bool On(XElement element, XNamespace ns, bool required = false)
    {
        string? value = (string?)element.Attribute(ns + "val");
        if (value == null && !required) return true;
        return value switch { "true" or "1" or "on" => true, "false" or "0" or "off" => false, _ => throw new InvalidDataException("Control.Malformed: Invalid Boolean metadata.") };
    }
    internal static void CheckText(string text, ConversionContext c)
    {
        c.CheckCancellation(); c.CheckCharacters(text.Length); c.ReserveControlWork(text.Length);
        try { _ = XmlConvert.VerifyXmlChars(text); }
        catch (XmlException e) { throw new ArgumentException("Control.Malformed: Text must contain complete XML Unicode characters.", nameof(text), e); }
    }
    internal static void Infer(ControlData data, ControlOwner owner, ConversionContext c)
    {
        owner.Refresh(data.Id, c); data.SelectedValue = null; data.FreeText = null;
        if (data.Placeholder) { data.State = ContentControlValueState.Placeholder; return; }
        string text = owner.Text;
        if (data.Kind == ContentControlKind.Checkbox)
        {
            if (text.Length != 1) throw new InvalidDataException("Control.Malformed: A checkbox must contain one character.");
            string glyph = Glyph(data).Text;
            if (glyph != text) { SetText(data, owner, glyph, c); c.Report("Control.CacheRefreshed", "A typed control's non-placeholder cache was refreshed from its declared state."); }
            data.State = ContentControlValueState.Value;
        }
        else if (data.Kind is ContentControlKind.DropDownList or ContentControlKind.ComboBox)
        {
            ContentControlListItem? item = data.Items.FirstOrDefault(i => i.Label == text);
            if (item != null) { data.SelectedValue = item.Value; data.State = ContentControlValueState.Value; }
            else if (text.Length == 0) data.State = ContentControlValueState.Empty;
            else if (data.Kind == ContentControlKind.ComboBox) { data.FreeText = text; data.State = ContentControlValueState.FreeText; }
            else { data.State = ContentControlValueState.Unresolved; c.Report("Control.ChoiceUnresolved", "A nonmember dropdown cache is preserved without an inferred selection."); }
        }
        else if (data.Kind == ContentControlKind.Date)
        {
            if (data.DateValue == null)
            {
                data.State = text.Length == 0 ? ContentControlValueState.Empty : ContentControlValueState.Unresolved;
                if (text.Length != 0) c.Report("Control.DateUnresolved", "Date display text without an explicit date value is preserved without inference.");
            }
            else
            {
                string display = DateDisplay(data, data.DateValue);
                if (display != text) { SetText(data, owner, display, c); c.Report("Control.CacheRefreshed", "A typed control's non-placeholder cache was refreshed from its declared state."); }
                data.State = ContentControlValueState.Value;
            }
        }
        else data.State = text.Length == 0 ? ContentControlValueState.Empty : ContentControlValueState.Value;
    }
    internal static (string Text, string Font) Glyph(ControlData data)
    {
        XElement? state = data.Properties.Element(W14 + "checkbox")?.Element(W14 + (data.Checked ? "checkedState" : "uncheckedState"));
        string code = (string?)state?.Attribute(W14 + "val") ?? (data.Checked ? "2612" : "2610");
        return (((char)int.Parse(code, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString(), (string?)state?.Attribute(W14 + "font") ?? "MS Gothic");
    }
    internal static void SetChecked(ControlData data, ControlOwner owner, bool value, ConversionContext c)
    {
        Require(data, ContentControlKind.Checkbox); data.Checked = value; ClearPlaceholder(data);
        XElement checkbox = data.Properties.Element(W14 + "checkbox")!;
        XElement? check = checkbox.Element(W14 + "checked");
        if (check == null) { check = new XElement(W14 + "checked"); checkbox.AddFirst(check); }
        check.SetAttributeValue(W14 + "val", value ? "1" : "0");
        SetText(data, owner, Glyph(data).Text, c); data.State = ContentControlValueState.Value; Dirty(data);
    }
    internal static void SetSelection(ControlData data, ControlOwner owner, string? value, ConversionContext c)
    {
        if (data.Kind is not ContentControlKind.DropDownList and not ContentControlKind.ComboBox) throw new ArgumentException("Control.Kind: The handle must select a list control.");
        ContentControlListItem? item = value == null ? null : data.Items.FirstOrDefault(i => i.Value == value);
        if (value != null && item == null) throw new ArgumentException("Control.Selection: The value must be an exact ordinal list member.", nameof(value));
        ClearPlaceholder(data); data.SelectedValue = value; data.FreeText = null; data.State = value == null ? ContentControlValueState.Empty : ContentControlValueState.Value;
        SetText(data, owner, item?.Label ?? "", c); Dirty(data);
    }
    internal static void SetCombo(ControlData data, ControlOwner owner, string text, ConversionContext c)
    {
        Require(data, ContentControlKind.ComboBox); CheckText(text, c);
        if (data.Items.Any(i => i.Label == text || data.Binding != null && i.Value == text))
            throw new ArgumentException("Control.Selection: Use the selection operation for text colliding with an item identity.", nameof(text));
        ClearPlaceholder(data); data.SelectedValue = null; data.FreeText = text.Length == 0 ? null : text;
        data.State = text.Length == 0 ? ContentControlValueState.Empty : ContentControlValueState.FreeText; SetText(data, owner, text, c); Dirty(data);
    }
    internal static void SetDate(ControlData data, ControlOwner owner, ContentControlDateValue? value, ContentControlDateMode mode, ConversionContext c)
    {
        Require(data, ContentControlKind.Date);
        if (value != null && value.Mode != mode) throw new ArgumentException("Control.DateMode: The date value must match the session's explicit date interpretation.", nameof(value));
        string display = value == null ? "" : DateDisplay(data, value);
        ClearPlaceholder(data); data.DateValue = value; data.Properties.Element(W + "date")!.SetAttributeValue(W + "fullDate", value == null ? null : DateLexical(value));
        SetText(data, owner, display, c); data.State = value == null ? ContentControlValueState.Empty : ContentControlValueState.Value; Dirty(data);
    }
    internal static string DateLexical(ContentControlDateValue value) => value.Value.ToString(value.Mode == ContentControlDateMode.DateOnly ? "yyyy-MM-dd'T'00:00:00'Z'" : "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
    internal static string DateDisplay(ControlData data, ContentControlDateValue value)
    {
        string pattern = (string?)data.Properties.Element(W + "date")?.Element(W + "dateFormat")?.Attribute(W + "val") ?? throw new NotSupportedException("Control.DateFormat: An explicit date picture is required.");
        try
        {
            if (value.Mode == ContentControlDateMode.DateOnly)
            {
                bool literal = false;
                foreach (char ch in pattern) { if (ch == '\'') literal = !literal; else if (!literal && "Hhmst".Contains(ch)) throw new FormatException(); }
            }
            return FieldPictures.Format(value.Value, "\\@", pattern);
        }
        catch (FormatException e) { throw new NotSupportedException("Control.DateFormat: The date picture is outside the bounded F2 profile.", e); }
    }
    internal static ContentControlDateValue ParseDate(string lexical, ContentControlDateMode mode)
    {
        if (!Regex.IsMatch(lexical, "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\\.[0-9]{1,7})?(?:Z|[+-][0-9]{2}:[0-9]{2})$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            throw new InvalidDataException("Control.DateValue: An explicit offset XML dateTime is required.");
        try
        {
            DateTimeOffset value = XmlConvert.ToDateTimeOffset(lexical);
            if (mode == ContentControlDateMode.DateOnly)
            {
                if (lexical != value.ToString("yyyy-MM-dd'T'00:00:00'Z'", CultureInfo.InvariantCulture) || value.Offset != TimeSpan.Zero || value.TimeOfDay != TimeSpan.Zero)
                    throw new InvalidDataException("Control.DateMode: DateOnly input must be canonical midnight UTC; no time/offset is discarded.");
                return ContentControlDateValue.FromDate(value.Year, value.Month, value.Day);
            }
            return ContentControlDateValue.FromInstant(value);
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        { throw new InvalidDataException("Control.DateValue: Invalid or out-of-range date.", e); }
    }
    internal static void SetPlain(ControlData data, ControlOwner owner, string text, ConversionContext c)
    {
        Require(data, ContentControlKind.PlainText); CheckText(text, c);
        XElement type = data.Properties.Element(W + "text")!;
        bool multiline = type.Attribute(W + "multiLine") is XAttribute a && Bool(a.Value);
        if ((!multiline && (text.Contains('\n') || text.Contains('\r'))) || text.Contains('\r'))
            throw new ArgumentException("Control.PlainText: Use LF soft breaks only, with explicit multiLine enabled.", nameof(text));
        ClearPlaceholder(data); SetText(data, owner, text, c); data.State = text.Length == 0 ? ContentControlValueState.Empty : ContentControlValueState.Value; Dirty(data);
    }
    internal static bool Bool(string value) => value switch { "true" or "1" or "on" => true, "false" or "0" or "off" => false, _ => throw new InvalidDataException("Control.Malformed: Invalid Boolean metadata.") };
    internal static void ClearPlaceholder(ControlData data) { data.Placeholder = false; data.Properties.Elements(W + "showingPlcHdr").Remove(); }
    internal static void Dirty(ControlData data) { if (data.Binding != null) data.BindingState = ContentControlBindingState.Dirty; }
    internal static void Require(ControlData data, ContentControlKind kind) { if (data.Kind != kind) throw new ArgumentException("Control.Kind: The handle has the wrong typed-control kind."); }
    internal static void SetText(ControlData data, ControlOwner owner, string text, ConversionContext c, string? fontOverride = null)
    {
        owner.Refresh(data.Id, c); CheckText(text, c);
        TextRun prototype = data.InputStyle ?? owner.Runs.FirstOrDefault() ?? new TextRun("");
        RunAppearance appearance = prototype.Appearance.Copy();
        if (data.Kind == ContentControlKind.Checkbox) appearance.FontFamily = Glyph(data).Font;
        appearance.FontFamily = fontOverride ?? appearance.FontFamily;
        owner.SetRuns(data.Id, new[] { new TextRun(text, prototype.Bold, prototype.Italic, prototype.Code, fontSize: prototype.FontSize, color: prototype.Color, appearance: appearance) });
    }
}
