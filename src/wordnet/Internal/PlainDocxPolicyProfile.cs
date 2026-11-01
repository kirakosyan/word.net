using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using WordNet.Formats;

namespace WordNet.Internal;

// The source admission check for the intentionally narrow policy execution profile.
// It does not compile a plan, prove output or authorize conversion.
internal static class PlainDocxPolicyProfile
{
    private static readonly XNamespace Word = DocxPackage.Word;
    private static readonly XNamespace Types = DocxPackage.ContentTypes;
    private static readonly XNamespace Relationships = DocxPackage.Relationships;
    private const string OfficeRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    private const string RelationshipsContentType = "application/vnd.openxmlformats-package.relationships+xml";
    private const string XmlContentType = "application/xml";
    private static readonly string[] RequiredParts = { "[Content_Types].xml", "_rels/.rels", "word/document.xml" };
    private static readonly string[] DefaultAttributes = { "Extension", "ContentType" };
    private static readonly string[] OverrideAttributes = { "PartName", "ContentType" };
    private static readonly string[] RelationshipAttributes = { "Id", "Type", "Target" };

    internal static void Validate(Stream input, CancellationToken cancellationToken = default) => _ = ReadText(input, cancellationToken);

    internal static string ReadText(Stream input, CancellationToken cancellationToken = default)
        => Read(input, allowBestEffort: false, cancellationToken).Text;

    internal static (string Text, PlainDocxPolicyInput Kind) Read(Stream input, bool allowBestEffort, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(input, nameof(input));
        if (!input.CanRead || !input.CanSeek || input.Position != 0) throw new ArgumentException("The source must be a readable seekable buffer positioned at zero.", nameof(input));
        if (input.Length > 16 * 1024 * 1024) throw new InvalidDataException("Limit.CompatibilityPlan: Source bytes exceed the bound.");
        cancellationToken.ThrowIfCancellationRequested();
        var context = new ConversionContext(new ConversionOptions(maximumInputBytes: 16 * 1024 * 1024), cancellationToken);
        using var package = new DocxPackage(input, context);
        var names = new HashSet<string>(package.PartNames, StringComparer.Ordinal);
        bool hasDocumentRelationships = names.Remove("word/_rels/document.xml.rels");
        if (!names.SetEquals(RequiredParts)) throw Unsupported();

        XDocument contentTypes = package.ReadXml("[Content_Types].xml"); XElement types = Root(contentTypes, Types + "Types");
        XElement[] declarations = Children(types, 3);
        if (declarations.Count(element => element.Name == Types + "Default") != 2 || declarations.Count(element => element.Name == Types + "Override") != 1
            || declarations.Any(element => !OnlyAttributes(element, element.Name == Types + "Default" ? DefaultAttributes : OverrideAttributes))) throw Unsupported();
        foreach (XElement declaration in declarations) _ = Children(declaration, 0);
        if (DocxPackage.ContentTypeFor(types, "word/document.xml", context) != DocxPackage.MainContentType
            || DocxPackage.ContentTypeFor(types, "_rels/.rels", context) != RelationshipsContentType
            || DocxPackage.ContentTypeFor(types, "unused.xml", context) != XmlContentType) throw Unsupported();

        XDocument rootRelationships = package.ReadXml("_rels/.rels"); XElement relationshipRoot = Root(rootRelationships, Relationships + "Relationships");
        XElement relation = Children(relationshipRoot, 1)[0];
        string? id = (string?)relation.Attribute("Id");
        if (relation.Name != Relationships + "Relationship" || !OnlyAttributes(relation, RelationshipAttributes)
            || id == null || id.Length is < 1 or > 64 || id[0] is not (>= 'A' and <= 'Z') and not (>= 'a' and <= 'z')
            || id.Skip(1).Any(character => character is not (>= 'A' and <= 'Z') and not (>= 'a' and <= 'z') and not (>= '0' and <= '9'))
            || (string?)relation.Attribute("Type") != OfficeRelationship
            || (string?)relation.Attribute("Target") != "word/document.xml" || Children(relation, 0).Length != 0) throw Unsupported();
        if (hasDocumentRelationships)
        {
            XElement empty = Root(package.ReadXml("word/_rels/document.xml.rels"), Relationships + "Relationships");
            if (Children(empty, 0).Length != 0) throw Unsupported();
        }

        XElement document = Root(package.ReadXml("word/document.xml"), Word + "document");
        XElement body = Child(document, Word + "body"); XElement paragraph = Child(body, Word + "p");
        XElement owner = Children(paragraph, 1)[0]; XElement run;
        PlainDocxPolicyInput kind = PlainDocxPolicyInput.Plain;
        if (owner.Name == Word + "hyperlink" && allowBestEffort)
        {
            XAttribute[] attributes = owner.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration).ToArray();
            string? anchor = (string?)owner.Attribute(Word + "anchor");
            if (attributes.Length != 1 || attributes[0].Name != Word + "anchor" || anchor == null || anchor.Length is < 1 or > 40
                || anchor.Any(character => character is not (>= 'A' and <= 'Z') and not (>= 'a' and <= 'z') and not (>= '0' and <= '9')))
                throw Unsupported();
            run = Child(owner, Word + "r"); kind = PlainDocxPolicyInput.UncheckedLink;
        }
        else
        {
            if (owner.Name != Word + "r" || !OnlyAttributes(owner)) throw Unsupported();
            run = owner;
        }
        XElement[] runChildren = run.Elements().Take(3).ToArray(); XElement text;
        if (allowBestEffort && kind == PlainDocxPolicyInput.Plain && runChildren.Length == 2 && runChildren[0].Name == Word + "rPr")
        {
            _ = Children(run, 2); XElement properties = runChildren[0];
            if (!OnlyAttributes(properties)) throw Unsupported();
            XElement font = Children(properties, 1)[0]; if (font.Name != Word + "rFonts") throw Unsupported();
            XAttribute[] attributes = font.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration).ToArray();
            if (attributes.Length != 2 || (string?)font.Attribute(Word + "asciiTheme") != "minorHAnsi"
                || (string?)font.Attribute(Word + "hAnsiTheme") != "minorHAnsi" || Children(font, 0).Length != 0) throw Unsupported();
            text = runChildren[1]; if (text.Name != Word + "t") throw Unsupported();
            kind = PlainDocxPolicyInput.ThemeFont;
        }
        else text = Child(run, Word + "t");
        if (!OnlyAttributes(text) || text.Nodes().Count() != 1 || text.FirstNode is not XText value || value.GetType() != typeof(XText)
            || value.Value.Length is < 1 or > 40 || value.Value.Any(character => character is not (>= 'A' and <= 'Z') and not (>= 'a' and <= 'z') and not (>= '0' and <= '9')))
            throw Unsupported();
        cancellationToken.ThrowIfCancellationRequested(); return (value.Value, kind);
    }

    private static XElement Root(XDocument document, XName expected)
    {
        XElement? root = document.Root;
        if (root == null || root.Name != expected || !OnlyAttributes(root)
            || document.Nodes().Any(node => !ReferenceEquals(node, root) && (node is not XText whitespace || !string.IsNullOrWhiteSpace(whitespace.Value)))) throw Unsupported();
        return root;
    }

    private static XElement Child(XElement parent, XName expected)
    {
        XElement child = Children(parent, 1)[0]; if (child.Name != expected || !OnlyAttributes(child)) throw Unsupported(); return child;
    }

    private static XElement[] Children(XElement parent, int expected)
    {
        if (parent.Nodes().Any(node => node is not XElement && (node is not XText whitespace || !string.IsNullOrWhiteSpace(whitespace.Value)))) throw Unsupported();
        XElement[] children = parent.Elements().ToArray(); if (children.Length != expected) throw Unsupported(); return children;
    }

    private static bool OnlyAttributes(XElement element, params string[] names)
    {
        XAttribute[] attributes = element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration).ToArray();
        return attributes.Length == names.Length && attributes.All(attribute => attribute.Name.NamespaceName.Length == 0 && names.Contains(attribute.Name.LocalName, StringComparer.Ordinal));
    }

    private static NotSupportedException Unsupported() => new NotSupportedException("Plan.SourceProfile: This source is outside the narrow plain-DOCX policy profile.");
}

internal enum PlainDocxPolicyInput
{
    Plain,
    ThemeFont,
    UncheckedLink,
}
