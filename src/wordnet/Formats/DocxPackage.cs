using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using WordNet.Internal;

namespace WordNet.Formats;

internal sealed class DocxPackage : IDisposable
{
    internal static readonly XNamespace Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    internal static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    internal static readonly XNamespace DocumentRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    internal static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
    internal const string MainContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
    internal const string TemplateContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml";
    internal static bool ContainsIgnoreCase(string value, string part)
    {
#if NET8_0_OR_GREATER
        return value.Contains(part, StringComparison.OrdinalIgnoreCase);
#else
        return value.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
#endif
    }
    internal static string? ContentTypeFor(XElement root, string name, ConversionContext context)
    {
        if (root.Name != ContentTypes + "Types") throw new InvalidDataException("Docx.ContentType: Invalid content-type root.");
        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement item in root.Elements())
        {
            context.CheckCancellation();
            string type = RequiredAttribute(item, "ContentType");
            if (type.Length == 0) throw new InvalidDataException("Docx.ContentType: Empty content type.");
            if (item.Name == ContentTypes + "Default")
            {
                string extension = RequiredAttribute(item, "Extension");
                if (extension.Length == 0 || extension.Any(c => c is '/' or '\\' or '.' || char.IsControl(c)) || defaults.ContainsKey(extension))
                    throw new InvalidDataException("Docx.ContentType: Invalid or duplicate default declaration.");
                defaults.Add(extension, type);
            }
            else if (item.Name == ContentTypes + "Override")
            {
                string part = RequiredAttribute(item, "PartName");
                if (part.Length < 2 || part[0] != '/') throw new InvalidDataException("Docx.ContentType: Invalid override part name.");
                ValidatePartName(part.Substring(1));
                if (overrides.ContainsKey(part)) throw new InvalidDataException("Docx.ContentType: Duplicate override declaration.");
                overrides.Add(part, type);
            }
            else throw new InvalidDataException("Docx.ContentType: Unknown content-type declaration.");
        }
        if (overrides.TryGetValue("/" + name, out string? declared)) return declared;
        int dot = name.LastIndexOf('.'), slash = name.LastIndexOf('/');
        return dot > slash && dot + 1 < name.Length && defaults.TryGetValue(name.Substring(dot + 1), out string? fallback) ? fallback : null;
    }
    private readonly ZipArchive archive;
    private readonly Dictionary<string, ZipArchiveEntry> entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
    private readonly ConversionContext context;

    internal DocxPackage(Stream input, ConversionContext context)
    {
        this.context = context;
        archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        try
        {
            long expanded = 0;
            var insensitive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var archiveEntries = PackageArchive.ReadEntries(archive);
            if (archiveEntries.Count > context.Options.MaximumArchiveEntries)
            {
                throw new InvalidDataException("Limit.ArchiveEntries: DOCX contains too many entries.");
            }

            foreach (ZipArchiveEntry entry in archiveEntries)
            {
                context.CheckCancellation();
                expanded = checked(expanded + entry.Length);
                if (expanded > context.Options.MaximumExpandedBytes)
                {
                    throw new InvalidDataException("Limit.ExpandedBytes: DOCX exceeds the uncompressed byte limit.");
                }

                bool directory = entry.FullName.Length > 0 && entry.FullName[entry.FullName.Length - 1] == '/';
                ValidatePartName(directory ? entry.FullName.Substring(0, entry.FullName.Length - 1) : entry.FullName);
                if (directory) continue;
                if (!insensitive.Add(entry.FullName))
                {
                    throw new InvalidDataException("Docx.DuplicatePart: DOCX contains duplicate or case-ambiguous part names.");
                }

                entries.Add(entry.FullName, entry);
            }
            context.ArchiveEntryCount = archiveEntries.Count;
            context.DeclaredExpandedBytes = expanded;
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    internal IEnumerable<string> PartNames => entries.Keys;

    internal bool Contains(string name) => entries.ContainsKey(name);

    internal byte[] ReadBytes(string name)
    {
        if (!entries.TryGetValue(name, out ZipArchiveEntry? entry)) throw new InvalidDataException("Docx.MissingPart: Missing embedded resource.");
        using var buffer = new BoundedMemoryStream(Math.Min(context.Options.MaximumExpandedBytes, entry.Length));
        using Stream stream = entry.Open();
        byte[] chunk = new byte[81920];
        int count;
        while ((count = stream.Read(chunk, 0, chunk.Length)) != 0) { context.CheckCancellation(); buffer.Write(chunk, 0, count); }
        return buffer.ToArray();
    }

    internal XDocument ReadXml(string name)
    {
        if (!entries.TryGetValue(name, out ZipArchiveEntry? entry))
        {
            throw new InvalidDataException("Docx.MissingPart: A required package part is missing.");
        }

        using var buffer = new BoundedMemoryStream(Math.Min(context.Options.MaximumExpandedBytes, entry.Length));
        using (Stream stream = entry.Open())
        {
            byte[] chunk = new byte[81920];
            int count;
            while ((count = stream.Read(chunk, 0, chunk.Length)) != 0)
            {
                context.CheckCancellation();
                buffer.Write(chunk, 0, count);
            }
        }

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = context.Options.MaximumCharacters,
            IgnoreWhitespace = false,
        };
        try
        {
            buffer.Position = 0;
            using (XmlReader reader = XmlReader.Create(buffer, settings))
            {
                while (reader.Read())
                {
                    context.CheckCancellation();
                    if (reader.Depth > context.Options.MaximumNestingDepth)
                    {
                        throw new InvalidDataException("Limit.NestingDepth: XML nesting exceeds the configured limit.");
                    }
                }
            }

            buffer.Position = 0;
            using XmlReader parsed = XmlReader.Create(buffer, settings);
            return XDocument.Load(parsed, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException("Docx.Xml: Invalid or prohibited XML in a package part.", exception);
        }
    }

    internal Dictionary<string, XElement> ReadRelationships(string partName)
    {
        string name = RelationshipPartName(partName);
        var result = new Dictionary<string, XElement>(StringComparer.Ordinal);
        if (!Contains(name))
        {
            return result;
        }

        XElement root = ReadXml(name).Root ?? throw new InvalidDataException("Docx.Relationships: Missing XML root.");
        if (root.Name != Relationships + "Relationships")
        {
            throw new InvalidDataException("Docx.Relationships: Invalid relationship namespace.");
        }

        foreach (XElement relation in root.Elements())
        {
            if (relation.Name != Relationships + "Relationship")
            {
                throw new InvalidDataException("Docx.Relationships: Invalid relationship element.");
            }

            string id = RequiredAttribute(relation, "Id");
            _ = RequiredAttribute(relation, "Type");
            _ = RequiredAttribute(relation, "Target");
            if (result.ContainsKey(id))
            {
                throw new InvalidDataException("Docx.Relationships: Duplicate relationship identifier.");
            }

            result.Add(id, relation);
        }

        return result;
    }

    internal static string RequiredAttribute(XElement element, XName name) =>
        (string?)element.Attribute(name) ?? throw new InvalidDataException("Docx.Attribute: A required XML attribute is missing.");

    internal static string ResolvePart(string owner, XElement relation)
    {
        if (string.Equals((string?)relation.Attribute("TargetMode"), "External", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Docx.ExternalPart: Document and style parts must be inside the package.");
        }

        string target = RequiredAttribute(relation, "Target");
        string prefix = owner.IndexOf('/') < 0 ? "" : owner.Substring(0, owner.LastIndexOf('/') + 1);
        string name = target.Length > 0 && target[0] == '/' ? target.Substring(1) : prefix + target;
        ValidatePartName(name);
        return name;
    }

    internal static string RelationshipPartName(string owner)
    {
        int slash = owner.LastIndexOf('/');
        return owner.Insert(slash + 1, "_rels/") + ".rels";
    }

    private static void ValidatePartName(string name)
    {
        if (name.Length == 0 || name.Any(character => character is '\\' or ':' or '?' or '#' or '%' || char.IsControl(character))
            || name.Split('/').Any(segment => segment.Length == 0 || segment == "." || segment == ".."))
        {
            throw new InvalidDataException("Docx.PartName: Noncanonical or unsafe package part name.");
        }
    }

    public void Dispose() => archive.Dispose();
}
