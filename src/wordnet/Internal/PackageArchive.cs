using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace WordNet.Internal;

internal sealed class PackageArchive : IDisposable
{
    private readonly ZipArchive archive;
    private readonly Dictionary<string, ZipArchiveEntry> entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
    private readonly ConversionContext context;

    internal PackageArchive(Stream input, ConversionContext context)
    {
        this.context = context;
        archive = new ZipArchive(input, ZipArchiveMode.Read, true);
        try
        {
            if (archive.Entries.Count > context.Options.MaximumArchiveEntries) throw new InvalidDataException("Limit.ArchiveEntries: Too many package entries.");
            long total = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                context.CheckCancellation();
                total = checked(total + entry.Length);
                if (total > context.Options.MaximumExpandedBytes) throw new InvalidDataException("Limit.ExpandedBytes: Package expansion exceeds the limit.");
                string name = entry.FullName;
                if (name.Length > 0 && name[name.Length - 1] == '/') continue;
                if (name.Length == 0 || name.Any(c => c is '\\' or ':' or '%' or '?' or '#') || name.Split('/').Any(p => p.Length == 0 || p == "." || p == ".."))
                    throw new InvalidDataException("Package.Path: Unsafe package entry.");
                if (entries.ContainsKey(name)) throw new InvalidDataException("Package.Duplicate: Duplicate entry.");
                entries.Add(name, entry);
            }
        }
        catch { archive.Dispose(); throw; }
    }

    internal bool Contains(string name) => entries.ContainsKey(name);

    internal byte[] Read(string name)
    {
        if (!entries.TryGetValue(name, out ZipArchiveEntry? entry)) throw new InvalidDataException("Package.MissingPart: Required entry is missing.");
        using Stream source = entry.Open();
        using var output = new BoundedMemoryStream(Math.Min(entry.Length, context.Options.MaximumExpandedBytes));
        byte[] bytes = new byte[8192];
        int count;
        while ((count = source.Read(bytes, 0, bytes.Length)) > 0) { context.CheckCancellation(); output.Write(bytes, 0, count); }
        return output.ToArray();
    }

    internal XDocument ReadXml(string name) => ParseXml(Read(name), context);

    internal static XDocument ParseXml(byte[] bytes, ConversionContext context)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = context.Options.MaximumCharacters };
        try
        {
            using var stream = new MemoryStream(bytes, false);
            using (XmlReader reader = XmlReader.Create(stream, settings))
            {
                while (reader.Read())
                {
                    context.CheckCancellation();
                    if (reader.Depth > context.Options.MaximumNestingDepth) throw new InvalidDataException("Limit.NestingDepth: XML nesting exceeds the limit.");
                }
            }
            stream.Position = 0;
            using XmlReader parsed = XmlReader.Create(stream, settings);
            return XDocument.Load(parsed, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException error) { throw new InvalidDataException("Package.Xml: Invalid or prohibited XML.", error); }
    }

    internal static string Resolve(string owner, string target)
    {
        if (target.Length == 0 || target.Any(c => c is '\\' or ':' or '%' or '?' or '#') || target[0] == '/')
            throw new InvalidDataException("Package.Path: External or noncanonical reference.");
        var parts = new List<string>(owner.Split('/').Take(owner.Split('/').Length - 1));
        foreach (string part in target.Split('/'))
        {
            if (part == ".") continue;
            if (part == "..") { if (parts.Count == 0) throw new InvalidDataException("Package.Path: Reference leaves the package."); parts.RemoveAt(parts.Count - 1); }
            else if (part.Length == 0) throw new InvalidDataException("Package.Path: Empty path segment.");
            else parts.Add(part);
        }
        return string.Join("/", parts);
    }

    internal static void Write(ZipArchive archive, string name, byte[] bytes, CompressionLevel compression = CompressionLevel.Optimal)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, compression);
        entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using Stream stream = entry.Open();
        stream.Write(bytes, 0, bytes.Length);
    }

    public void Dispose() => archive.Dispose();
}
