using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet;

/// <summary>The bounded DOCX content-control kind.</summary>
public enum ContentControlKind
{
    /// <summary>An Office 2010 checkbox.</summary>
    Checkbox = 0,
    /// <summary>A list requiring an exact stored-value selection.</summary>
    DropDownList = 1,
    /// <summary>A list with bounded literal free text.</summary>
    ComboBox = 2,
    /// <summary>A Gregorian date with an explicit interpretation.</summary>
    Date = 3,
    /// <summary>Formatted inline text or ordinary paragraphs.</summary>
    RichText = 4,
    /// <summary>One formatting set of literal text.</summary>
    PlainText = 5,
}

/// <summary>Content-control editing restrictions, not encryption or confidentiality.</summary>
public enum ContentControlLock
{
    /// <summary>Content and wrapper are unlocked.</summary>
    Unlocked = 0,
    /// <summary>The wrapper cannot be deleted; its content can be edited.</summary>
    ControlLocked = 1,
    /// <summary>The content cannot be edited.</summary>
    ContentLocked = 2,
    /// <summary>Neither the content nor the wrapper can be edited/deleted.</summary>
    ControlAndContentLocked = 3,
}

/// <summary>How explicitly typed date-control values are interpreted.</summary>
public enum ContentControlDateMode
{
    /// <summary>Require an explicit offset and display the instant in UTC.</summary>
    UtcInstant = 0,
    /// <summary>Use Gregorian calendar components, represented as midnight UTC in DOCX.</summary>
    DateOnly = 1,
}

/// <summary>The current visible/value state, independent of the control kind.</summary>
public enum ContentControlValueState
{
    /// <summary>A concrete typed value.</summary>
    Value = 0,
    /// <summary>No selection or empty text.</summary>
    Empty = 1,
    /// <summary>Authored placeholder content, not a typed value.</summary>
    Placeholder = 2,
    /// <summary>An intact source cache without a resolved typed value.</summary>
    Unresolved = 3,
    /// <summary>Literal combo-box text outside its item labels.</summary>
    FreeText = 4,
}

/// <summary>The explicit local XML synchronization state.</summary>
public enum ContentControlBindingState
{
    /// <summary>No data binding.</summary>
    None = 0,
    /// <summary>Preserved binding, not explicitly applied by this session.</summary>
    Unevaluated = 1,
    /// <summary>The control and its local XML target have been explicitly synchronized.</summary>
    Synchronized = 2,
    /// <summary>A bound control was edited; DOCX save needs apply, write-back or detach.</summary>
    Dirty = 3,
}

/// <summary>Configure before loading. Values are captured once; existing Load behavior is unchanged.</summary>
public sealed class ContentControlLoadOptions
{
    /// <summary>Gets or sets the common date interpretation for this session. Default UTC instant.</summary>
    public ContentControlDateMode DateMode { get; set; }
}

/// <summary>An immutable date-control value; no text, host-zone or clock inference is performed.</summary>
public sealed class ContentControlDateValue
{
    private ContentControlDateValue(DateTimeOffset value, ContentControlDateMode mode) { Value = value; Mode = mode; }
    /// <summary>Creates an explicit Gregorian calendar date in years 1 through 9999.</summary>
    public static ContentControlDateValue FromDate(int year, int month, int day) =>
        new ContentControlDateValue(new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero), ContentControlDateMode.DateOnly);
    /// <summary>Creates an explicit instant, normalized to UTC.</summary>
    public static ContentControlDateValue FromInstant(DateTimeOffset value) => new ContentControlDateValue(value.ToUniversalTime(), ContentControlDateMode.UtcInstant);
    /// <summary>Gets the calendar midnight or UTC instant.</summary>
    public DateTimeOffset Value { get; }
    /// <summary>Gets the explicit interpretation.</summary>
    public ContentControlDateMode Mode { get; }
}

/// <summary>An immutable source list item. A missing display label uses its stored value.</summary>
public sealed class ContentControlListItem
{
    /// <summary>Creates an item with an optional display label and a non-null stored value.</summary>
    public ContentControlListItem(string? displayText, string value) { Guard.NotNull(value, nameof(value)); DisplayText = displayText; Value = value; }
    /// <summary>Gets the optional source display label.</summary>
    public string? DisplayText { get; }
    /// <summary>Gets the ordinal stored value, which can be empty.</summary>
    public string Value { get; }
    /// <summary>Gets the effective nonempty display label.</summary>
    public string Label => DisplayText ?? Value;
}

/// <summary>The supported ordinary paragraph alignment.</summary>
public enum ContentControlAlignment
{
    /// <summary>Align left.</summary>
    Left = 0,
    /// <summary>Center.</summary>
    Center = 1,
    /// <summary>Align right.</summary>
    Right = 2,
    /// <summary>Justify.</summary>
    Justified = 3,
    /// <summary>Distribute.</summary>
    Distributed = 4,
}

/// <summary>A detached paragraph input/snapshot. Formatting and input runs are copied.</summary>
public sealed class ContentControlParagraph
{
    internal readonly ParagraphBlock Data;
    /// <summary>Creates an ordinary paragraph with at most 65536 runs and 4194304 UTF-16 units. Spacing is in points; heading level is zero through six.</summary>
    public ContentControlParagraph(IEnumerable<DocumentText> runs, ContentControlAlignment alignment = ContentControlAlignment.Left,
        int headingLevel = 0, double? spaceBefore = null, double? spaceAfter = null)
    {
        Guard.NotNull(runs, nameof(runs));
        if (alignment is < ContentControlAlignment.Left or > ContentControlAlignment.Distributed) throw new ArgumentOutOfRangeException(nameof(alignment));
        if (headingLevel is < 0 or > 6) throw new ArgumentOutOfRangeException(nameof(headingLevel));
        if (spaceBefore.HasValue && !(spaceBefore >= 0 && spaceBefore <= 1584)) throw new ArgumentOutOfRangeException(nameof(spaceBefore));
        if (spaceAfter.HasValue && !(spaceAfter >= 0 && spaceAfter <= 1584)) throw new ArgumentOutOfRangeException(nameof(spaceAfter));
        var copied = new List<DocumentText>(); long characters = 0;
        foreach (DocumentText run in runs)
        {
            if (run == null) throw new ArgumentException("Control.Content: Runs cannot be null.", nameof(runs));
            characters = checked(characters + run.Text.Length + (run.Formatting.FontFamily?.Length ?? 0));
            if (copied.Count == 65536 || characters > 4 * 1024 * 1024) throw new InvalidDataException("Limit.Controls: A detached paragraph input exceeds 65536 runs or 4194304 UTF-16 units.");
            copied.Add(run);
        }
        Runs = new ReadOnlyCollection<DocumentText>(copied); Data = new ParagraphBlock { Alignment = (ParagraphAlignment)alignment, HeadingLevel = headingLevel };
        Data.Layout.Before = spaceBefore; Data.Layout.After = spaceAfter;
    }
    internal ContentControlParagraph(ParagraphBlock paragraph, IReadOnlyList<DocumentText> runs) { Data = paragraph; Runs = runs; }
    internal Guid Origin { get; set; }
    /// <summary>Gets immutable copied text values. Replacement also enforces aggregate session resource limits.</summary>
    public IReadOnlyList<DocumentText> Runs { get; } = Array.Empty<DocumentText>();
    /// <summary>Gets the paragraph's alignment.</summary>
    public ContentControlAlignment Alignment => (ContentControlAlignment)Data.Alignment;
    /// <summary>Gets the heading role, zero for ordinary text.</summary>
    public int HeadingLevel => Data.HeadingLevel;
    /// <summary>Gets before spacing in points, or null when absent.</summary>
    public double? SpaceBefore => Data.Layout.Before;
    /// <summary>Gets after spacing in points, or null when absent.</summary>
    public double? SpaceAfter => Data.Layout.After;
    /// <summary>Gets the retained line-spacing amount (points for exact/atLeast, multiplier for auto).</summary>
    public double? LineSpacing => Data.Layout.LineSpacing;
    /// <summary>Gets the retained line rule: auto, exact or atLeast.</summary>
    public string LineRule => Data.Layout.MultipleLineSpacing ? "auto" : Data.Layout.ExactLineSpacing ? "exact" : "atLeast";
    /// <summary>Gets whether this source paragraph stays with its successor.</summary>
    public bool KeepWithNext => Data.Layout.KeepWithNext;
}

/// <summary>Detached local XML binding metadata. It never exposes or executes a general XPath engine.</summary>
public sealed class ContentControlBinding
{
    /// <summary>Creates detached binding metadata. The target must resolve in an existing local store when attached; no general XPath is executed.</summary>
    public ContentControlBinding(string storeItemId, string path, IReadOnlyDictionary<string, string>? namespaceMappings = null)
    {
        Guard.NotNull(storeItemId, nameof(storeItemId)); Guard.NotNull(path, nameof(path));
        StoreItemId = ControlBindings.StoreId(storeItemId); Path = path;
        if (path.Length is 0 or > 1024) throw new ArgumentException("Binding.Path: Path length must be between 1 and 1024.", nameof(path));
        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        if (namespaceMappings != null)
            foreach (var mapping in namespaceMappings)
            {
                if (copy.Count == 16) throw new InvalidDataException("Limit.Controls: Too many namespace mappings.");
                if (mapping.Key == null || mapping.Value == null || mapping.Key.Length is 0 or > 64 || mapping.Value.Length is 0 or > 1024
                    || mapping.Value.Contains('\'') && mapping.Value.Contains('"'))
                    throw new ArgumentException("Binding.Path: Namespace mappings must be bounded and representable as quoted declarations.", nameof(namespaceMappings));
                copy.Add(mapping.Key, mapping.Value);
            }
        NamespaceMappings = new ReadOnlyDictionary<string, string>(copy);
    }
    internal ContentControlBinding(ControlBinding data)
    { StoreItemId = data.StoreId; Path = data.Path; NamespaceMappings = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(data.Namespaces, StringComparer.Ordinal)); }
    /// <summary>Gets the local store GUID.</summary>
    public string StoreItemId { get; }
    /// <summary>Gets the bounded singleton child/attribute path.</summary>
    public string Path { get; }
    /// <summary>Gets copied prefix mappings; unprefixed names mean the empty namespace.</summary>
    public IReadOnlyDictionary<string, string> NamespaceMappings { get; }
}

/// <summary>An immutable definition for inserting one bounded inline typed control.</summary>
public sealed class ContentControlDefinition
{
    /// <summary>Copies a definition. Text is literal; list values and labels must be unique; optional binding targets must already exist locally.</summary>
    public ContentControlDefinition(ContentControlKind kind, string? initialText = null, IEnumerable<ContentControlListItem>? items = null,
        string? selectedValue = null, bool isChecked = false, ContentControlDateValue? dateValue = null, bool multiLine = false,
        string? dateFormat = null, string? tag = null, string? alias = null, ContentControlLock editLock = ContentControlLock.Unlocked,
        ContentControlBinding? binding = null)
    {
        if (kind is < ContentControlKind.Checkbox or > ContentControlKind.PlainText) throw new ArgumentOutOfRangeException(nameof(kind));
        if (editLock is < ContentControlLock.Unlocked or > ContentControlLock.ControlAndContentLocked) throw new ArgumentOutOfRangeException(nameof(editLock));
        if (kind is not ContentControlKind.RichText and not ContentControlKind.PlainText and not ContentControlKind.ComboBox && initialText != null
            || kind is not ContentControlKind.DropDownList and not ContentControlKind.ComboBox && (items != null || selectedValue != null)
            || kind != ContentControlKind.Checkbox && isChecked || kind != ContentControlKind.Date && (dateValue != null || dateFormat != null)
            || kind != ContentControlKind.PlainText && multiLine || kind == ContentControlKind.RichText && binding != null
            || kind == ContentControlKind.ComboBox && selectedValue != null && initialText != null)
            throw new ArgumentException("Control.Definition: Values are incompatible with the selected kind.");
        if ((tag?.Length ?? 0) > 256 || (alias?.Length ?? 0) > 256 || (dateFormat?.Length ?? 0) > 256
            || (initialText?.Length ?? 0) > 4 * 1024 * 1024 || (selectedValue?.Length ?? 0) > 1024)
            throw new InvalidDataException("Limit.Controls: Definition metadata exceeds its bound.");
        var copied = new List<ContentControlListItem>();
        if (items != null)
            foreach (ContentControlListItem item in items)
            {
                if (item == null) throw new ArgumentException("Control.Definition: List items cannot be null.", nameof(items));
                if (copied.Count == 256) throw new InvalidDataException("Limit.Controls: Too many list items.");
                copied.Add(item);
            }
        if (copied.Any(item => item.Value.Length > 1024 || item.Label.Length is 0 or > 1024)
            || copied.Select(item => item.Value).Distinct(StringComparer.Ordinal).Count() != copied.Count
            || copied.Select(item => item.Label).Distinct(StringComparer.Ordinal).Count() != copied.Count)
            throw new ArgumentException("Control.Definition: List values and effective labels must be unique and bounded.", nameof(items));
        if (selectedValue != null && copied.All(item => item.Value != selectedValue))
            throw new ArgumentException("Control.Definition: The initial selection must name a list item.", nameof(selectedValue));
        Kind = kind; InitialText = initialText; Items = new ReadOnlyCollection<ContentControlListItem>(copied); SelectedValue = selectedValue;
        IsChecked = isChecked; DateValue = dateValue; MultiLine = multiLine; DateFormat = dateFormat ?? "yyyy-MM-dd";
        Tag = tag; Alias = alias; Lock = editLock; Binding = binding;
    }
    /// <summary>Gets the requested supported kind.</summary>
    public ContentControlKind Kind { get; }
    /// <summary>Gets the initial literal text, or null.</summary>
    public string? InitialText { get; }
    /// <summary>Gets the copied list items.</summary>
    public IReadOnlyList<ContentControlListItem> Items { get; }
    /// <summary>Gets the initial stored selection, or null.</summary>
    public string? SelectedValue { get; }
    /// <summary>Gets the initial checkbox state.</summary>
    public bool IsChecked { get; }
    /// <summary>Gets the initial date, or null.</summary>
    public ContentControlDateValue? DateValue { get; }
    /// <summary>Gets whether plain text allows LF soft breaks.</summary>
    public bool MultiLine { get; }
    /// <summary>Gets the explicit bounded date picture.</summary>
    public string DateFormat { get; }
    /// <summary>Gets literal tag metadata.</summary>
    public string? Tag { get; }
    /// <summary>Gets optional alias metadata.</summary>
    public string? Alias { get; }
    /// <summary>Gets the initial lock, applied after the initial value.</summary>
    public ContentControlLock Lock { get; }
    /// <summary>Gets the optional explicit local binding.</summary>
    public ContentControlBinding? Binding { get; }
}

/// <summary>An immutable, session/version-scoped control snapshot. Reacquire after every committed edit.</summary>
public sealed class DocumentContentControl
{
    internal DocumentContentControl(Guid session, long version, ControlData data, int? parentId, bool story, ControlStructure structure, IReadOnlyList<ContentControlParagraph> content)
    {
        Session = session; Version = version; Identity = data.Identity; Id = data.Id; Kind = data.Kind;
        Tag = data.Tag; Alias = data.Alias; ParentId = parentId;
        Owner = structure == ControlStructure.Row ? "body table row" : structure == ControlStructure.Cell ? "body table cell"
            : story ? (data.Inline ? "inline header/footer paragraph" : "block header/footer paragraphs")
            : (data.Inline ? "inline body/cell paragraph" : "block body/cell paragraphs");
        Lock = data.Lock; ShowingPlaceholder = data.Placeholder; State = data.State;
        IsChecked = data.Kind == ContentControlKind.Checkbox && !data.Placeholder ? data.Checked : null;
        SelectedValue = data.SelectedValue; DateValue = data.Placeholder ? null : data.DateValue; FreeText = data.FreeText;
        Items = new ReadOnlyCollection<ContentControlListItem>(data.Items.ToList()); Content = content;
        Binding = data.Binding == null ? null : new ContentControlBinding(data.Binding); BindingState = data.BindingState;
        DisplayText = string.Join("\n", content.Select(p => string.Concat(p.Runs.Select(r => r.Text))));
    }
    internal Guid Session { get; }
    internal long Version { get; }
    internal Guid Identity { get; }
    /// <summary>Gets the stable source OOXML ID; IDs are unique in a preserved session.</summary>
    public int Id { get; }
    /// <summary>Gets the enclosing typed control's source ID, or null for a top-level control in this snapshot.</summary>
    public int? ParentId { get; }
    /// <summary>Gets the typed kind.</summary>
    public ContentControlKind Kind { get; }
    /// <summary>Gets literal tag metadata, never implicitly executed.</summary>
    public string? Tag { get; }
    /// <summary>Gets the optional alias.</summary>
    public string? Alias { get; }
    /// <summary>Gets the bounded owner description.</summary>
    public string Owner { get; }
    /// <summary>Gets the editing lock.</summary>
    public ContentControlLock Lock { get; }
    /// <summary>Gets whether display content is an authored placeholder.</summary>
    public bool ShowingPlaceholder { get; }
    /// <summary>Gets the current value state.</summary>
    public ContentControlValueState State { get; }
    /// <summary>Gets the visible copied text, with paragraph separators.</summary>
    public string DisplayText { get; }
    /// <summary>Gets checkbox state, or null for a placeholder/other kind.</summary>
    public bool? IsChecked { get; }
    /// <summary>Gets the exact selected stored value; null means no resolved selection.</summary>
    public string? SelectedValue { get; }
    /// <summary>Gets combo-box free text, or null.</summary>
    public string? FreeText { get; }
    /// <summary>Gets the immutable date, or null.</summary>
    public ContentControlDateValue? DateValue { get; }
    /// <summary>Gets immutable source items in their original order.</summary>
    public IReadOnlyList<ContentControlListItem> Items { get; }
    /// <summary>Gets copied content and paragraph appearance.</summary>
    public IReadOnlyList<ContentControlParagraph> Content { get; }
    /// <summary>Gets preserved local binding metadata, or null.</summary>
    public ContentControlBinding? Binding { get; }
    /// <summary>Gets the explicit XML synchronization state.</summary>
    public ContentControlBindingState BindingState { get; }
}
