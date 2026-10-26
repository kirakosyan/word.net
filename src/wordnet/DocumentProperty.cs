using System;
using System.Xml;

namespace WordNet;

/// <summary>The admitted, explicitly typed custom-property values.</summary>
public enum DocumentPropertyType
{
    /// <summary>Unicode text.</summary>
    Text,
    /// <summary>A Boolean value.</summary>
    Boolean,
    /// <summary>A checked decimal number.</summary>
    Number,
    /// <summary>An instant normalized to UTC, without host time-zone inference.</summary>
    Date
}

/// <summary>An immutable named custom property. Names match ordinally ignoring case.</summary>
public sealed class DocumentProperty
{
    /// <summary>Creates a Unicode text property.</summary>
    public DocumentProperty(string name, string value) : this(name, DocumentPropertyType.Text, value ?? throw new ArgumentNullException(nameof(value))) { }
    /// <summary>Creates a Boolean property.</summary>
    public DocumentProperty(string name, bool value) : this(name, DocumentPropertyType.Boolean, value) { }
    /// <summary>Creates a decimal property; floating-point and text coercion are excluded.</summary>
    public DocumentProperty(string name, decimal value) : this(name, DocumentPropertyType.Number, value) { }
    /// <summary>Creates a UTC date property from an explicitly supplied offset.</summary>
    public DocumentProperty(string name, DateTimeOffset value) : this(name, DocumentPropertyType.Date, value.ToUniversalTime()) { }
    private DocumentProperty(string name, DocumentPropertyType type, object value)
    {
        Internal.Guard.NotNull(name, nameof(name));
        if (name.Length == 0 || name.Length > 255 || string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Property names must contain 1–255 characters.", nameof(name));
        XmlConvert.VerifyXmlChars(name);
        if (value is string text) XmlConvert.VerifyXmlChars(text);
        Name = name; Type = type; Value = value;
    }
    /// <summary>Gets the original property name.</summary>
    public string Name { get; }
    /// <summary>Gets the explicit value type.</summary>
    public DocumentPropertyType Type { get; }
    /// <summary>Gets an immutable string, Boolean, decimal or UTC DateTimeOffset.</summary>
    public object Value { get; }
}
