using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using WordNet.Internal;

namespace WordNet.Formats;

// ODF-specific admission: intentionally does not relax DOCX package path policy.
internal sealed class OdfPackage : IDisposable
{
    internal const string MediaType = "application/vnd.oasis.opendocument.text";
    internal const string TemplateMediaType = "application/vnd.oasis.opendocument.text-template";
    internal static readonly XNamespace Manifest = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";
    private readonly ZipArchive archive;
    private readonly Dictionary<string, ZipArchiveEntry> entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> cache = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    private readonly Dictionary<string, string> types = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly ConversionContext context;
    private long expanded, xmlCharacters, xmlNodes;
    internal IEnumerable<string> Names => entries.Keys;
    internal bool Contains(string name) => entries.ContainsKey(name);
    internal OdfPackage(Stream input, ConversionContext context, bool template = false)
    {
        this.context = context; context.CheckCancellation();
        string mediaType = template ? TemplateMediaType : MediaType;
        // Validate the actual first local header, independently of central-directory ordering.
        long origin = input.Position;
        using (var reader = new BinaryReader(input, Encoding.UTF8, true))
        {
            if (input.Length - origin < 38 || reader.ReadUInt32() != 0x04034b50) throw Invalid("Mimetype");
            _ = reader.ReadUInt16(); ushort flags = reader.ReadUInt16(), method = reader.ReadUInt16();
            input.Position += 16; ushort nameLength = reader.ReadUInt16(), extraLength = reader.ReadUInt16();
            if ((flags & 9) != 0 || method != 0 || nameLength != 8 || extraLength != 0
                || Encoding.ASCII.GetString(reader.ReadBytes(8)) != "mimetype") throw Invalid("Mimetype");
            byte[] mime = reader.ReadBytes(mediaType.Length);
            if (Encoding.ASCII.GetString(mime) != mediaType) throw Invalid("Mimetype");
        }
        input.Position = origin;
        archive = new ZipArchive(input, ZipArchiveMode.Read, true);
        try
        {
            var archiveEntries = PackageArchive.ReadEntries(archive);
            if (archiveEntries.Count > context.Options.MaximumArchiveEntries) throw Invalid("EntryLimit");
            var insensitive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long declared = 0;
            foreach (ZipArchiveEntry entry in archiveEntries)
            {
                context.CheckCancellation(); declared = checked(declared + entry.Length);
                if (declared > context.Options.MaximumExpandedBytes) throw Invalid("ExpansionLimit");
                string name = entry.FullName; ValidatePath(name, true);
                if (!insensitive.Add(name)) throw Invalid("Duplicate");
                if (name.StartsWith("Scripts/", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Basic/", StringComparison.OrdinalIgnoreCase)
                    || ContainsText(name, "signature") || name.StartsWith("Object", StringComparison.OrdinalIgnoreCase)) throw Invalid("ActiveContent");
                entries.Add(name, entry);
            }
            if (archiveEntries.Count == 0 || archiveEntries[0].FullName != "mimetype" || Read("mimetype").Length != mediaType.Length) throw Invalid("Mimetype");
            XDocument manifest = Xml("META-INF/manifest.xml");
            if (manifest.Root?.Name != Manifest + "manifest" || (string?)manifest.Root.Attribute(Manifest + "version") != "1.3") throw Invalid("Manifest");
            foreach (XElement entry in manifest.Root.Elements())
            {
                context.CheckCancellation();
                if (entry.Name != Manifest + "file-entry" || entry.HasElements) throw Invalid("Manifest");
                string path = (string?)entry.Attribute(Manifest + "full-path") ?? throw Invalid("Manifest");
                string type = (string?)entry.Attribute(Manifest + "media-type") ?? throw Invalid("Manifest");
                if (path != "/") ValidatePath(path, true);
                if (types.ContainsKey(path) || ContainsText(type, "script") || ContainsText(type, "ole")) throw Invalid("Manifest");
                types.Add(path, type);
            }
            if (!types.TryGetValue("/", out string? rootType) || rootType != mediaType) throw Invalid("Manifest");
            foreach (var entry in types.Where(e => e.Key != "/")) if (!entries.ContainsKey(entry.Key)) throw Invalid("Manifest");
            foreach (string name in entries.Keys)
                if (name != "mimetype" && name != "META-INF/manifest.xml" && name[name.Length - 1] != '/' && !types.ContainsKey(name)) throw Invalid("Manifest");
            RequireType("content.xml", "text/xml");
            foreach (string name in new[] { "styles.xml", "meta.xml", "settings.xml" }) if (Contains(name)) RequireType(name, "text/xml");
            // Admit every actual decompression once. Unused resources cannot evade the operation budget.
            foreach (string name in entries.Keys)
            {
                _ = Read(name);
                if (name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) _ = Xml(name);
            }
        }
        catch { archive.Dispose(); throw; }
    }
    private static InvalidDataException Invalid(string code) => new InvalidDataException("Odt.Package" + code + ": Invalid or unsupported ODF text package.");
    private static bool ContainsText(string value, string part)
    {
#if NET8_0_OR_GREATER
        return value.Contains(part, StringComparison.OrdinalIgnoreCase);
#else
        return value.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
#endif
    }
    private static void ValidatePath(string name, bool directories = false)
    {
        if (directories && name.Length > 1 && name[name.Length - 1] == '/') name = name.Substring(0, name.Length - 1);
        if (string.IsNullOrEmpty(name) || name.Any(c => c is ':' or '\\' or '%' or '?' or '#' || char.IsControl(c))
            || name.Split('/').Any(s => s.Length == 0 || s is "." or "..")) throw Invalid("Path");
    }
    internal static string Resolve(string target)
    {
        if (target.StartsWith("./", StringComparison.Ordinal)) target = target.Substring(2);
        ValidatePath(target); return target;
    }
    internal void RequireType(string name, string type)
    { if (!types.TryGetValue(name, out string? actual) || actual != type) throw Invalid("MediaType"); }
    internal string ContentType(string name) => types.TryGetValue(name, out string? type) ? type : throw Invalid("MediaType");
    internal byte[] Read(string name)
    {
        context.CheckCancellation();
        if (cache.TryGetValue(name, out byte[]? existing)) return existing;
        if (!entries.TryGetValue(name, out ZipArchiveEntry? entry)) throw Invalid("MissingPart");
        using Stream source = entry.Open(); using var output = new BoundedMemoryStream(context.Options.MaximumExpandedBytes - expanded);
        byte[] buffer = new byte[8192]; int count;
        while ((count = source.Read(buffer, 0, buffer.Length)) != 0)
        {
            context.CheckCancellation(); expanded = checked(expanded + count);
            if (expanded > context.Options.MaximumExpandedBytes) throw Invalid("ExpansionLimit"); output.Write(buffer, 0, count);
        }
        if (output.Length != entry.Length) throw Invalid("Length");
        byte[] result = output.ToArray(); cache.Add(name, result); return result;
    }
    private readonly Dictionary<string, XDocument> xml = new Dictionary<string, XDocument>(StringComparer.Ordinal);
    internal XDocument Xml(string name)
    {
        if (xml.TryGetValue(name, out XDocument? cached)) return cached;
        byte[] bytes = Read(name);
        XDocument document = PackageArchive.ParseXml(bytes, context);
        foreach (XNode node in document.DescendantNodes())
        {
            context.CheckCancellation(); xmlNodes++;
            if (node is XText text) xmlCharacters += text.Value.Length;
            if (node is XElement element) foreach (XAttribute attribute in element.Attributes()) xmlCharacters += attribute.Value.Length;
            if (xmlCharacters > context.Options.MaximumCharacters || xmlNodes * 64 + xmlCharacters * 2 + expanded > context.Options.MaximumExpandedBytes) throw Invalid("XmlLimit");
        }
        if (document.Descendants().Any(e => e.Name.NamespaceName.Contains("script") || e.Name.LocalName is "object" or "object-ole" or "script"
            || e.Name.LocalName is "scripts" or "event-listeners" && (e.HasElements || !string.IsNullOrWhiteSpace(e.Value)))) throw Invalid("ActiveContent");
        xml.Add(name, document); return document;
    }
    public void Dispose() => archive.Dispose();
}
