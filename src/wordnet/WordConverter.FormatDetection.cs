using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using OpenMcdf;
using WordNet.Formats;
using WordNet.Internal;

namespace WordNet;

public sealed partial class WordConverter
{
    private static readonly byte[] CompoundSignature = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
    /// <summary>Inspects bounded input bytes for a format identity without converting or executing content.</summary>
    /// <param name="source">Readable input at its starting position; the caller retains ownership.</param>
    /// <param name="options">Input, archive, XML and expansion limits; null uses the normal defaults.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>A format-family, template and confidence observation, not a capability or safety guarantee.</returns>
    /// <remarks>Seekable streams are restored to their starting position, including on failure. Nonseekable
    /// streams are consumed from their current position through EOF, or through at most one byte beyond
    /// the input limit on failure. No extension or remote resource is consulted, and no document content is executed.</remarks>
    public static DocumentFormatInspection DetectFormat(Stream source, ConversionOptions? options = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(source, nameof(source));
        if (!source.CanRead) throw new ArgumentException("The source must be readable.", nameof(source));
        options ??= new ConversionOptions();
        long? position = source.CanSeek ? source.Position : null;
        try
        {
            var context = new ConversionContext(options, cancellationToken); context.CheckCancellation();
            using var input = new BoundedMemoryStream(options.MaximumInputBytes);
            Copy(source, input, context, options.MaximumInputBytes); input.Position = 0;
            return DetectCore(input, context);
        }
        finally { if (position.HasValue) source.Position = position.Value; }
    }

    private static DocumentFormatInspection DetectCore(MemoryStream input, ConversionContext context)
    {
        long size = input.Length;
        DocumentFormatInspection Result(FormatDetectionStatus status, FormatContainerFamily family = FormatContainerFamily.Unknown,
            FormatIdentityConfidence confidence = FormatIdentityConfidence.Unknown, DocumentFormat format = DocumentFormat.Unknown, bool? template = null)
            => new DocumentFormatInspection(status, family, confidence, format, template, size);
        byte[] header = new byte[8]; int read = input.Read(header, 0, header.Length); input.Position = 0;
        if (read >= 5 && header[0] == '%' && header[1] == 'P' && header[2] == 'D' && header[3] == 'F' && header[4] == '-')
            return Result(FormatDetectionStatus.Recognized, FormatContainerFamily.Pdf, FormatIdentityConfidence.Signature, DocumentFormat.Pdf, false);
        if (read >= 8 && header.SequenceEqual(CompoundSignature))
        {
            try
            {
                using var root = RootStorage.Open(input, StorageModeFlags.LeaveOpen | StorageModeFlags.StrictValidation);
                if (root.ContainsEntry("EncryptionInfo") || root.ContainsEntry("EncryptedPackage"))
                    return Result(FormatDetectionStatus.UnsupportedProtected, FormatContainerFamily.CompoundFile, FormatIdentityConfidence.PackageIdentity);
                if (!root.TryOpenStream("WordDocument", out CfbStream? word) || word == null)
                    return Result(FormatDetectionStatus.Unknown, FormatContainerFamily.CompoundFile, FormatIdentityConfidence.Signature);
                using (word)
                {
                    if (word.Length > context.Options.MaximumExpandedBytes)
                        throw new InvalidDataException("Limit.ExpandedBytes: Compound Word stream exceeds the inspection limit.");
                    if (word.Length < 12) return Result(FormatDetectionStatus.Invalid, FormatContainerFamily.CompoundFile);
                    var fib = new byte[12]; int offset = 0;
                    while (offset < fib.Length) { context.CheckCancellation(); int count = word.Read(fib, offset, fib.Length - offset); if (count == 0) return Result(FormatDetectionStatus.Invalid, FormatContainerFamily.CompoundFile); offset += count; }
                    if (fib[0] != 0xEC || fib[1] != 0xA5) return Result(FormatDetectionStatus.Unknown, FormatContainerFamily.CompoundFile, FormatIdentityConfidence.Signature);
                    int flags = fib[10] | fib[11] << 8; bool template = (flags & 1) != 0;
                    if ((flags & 0x8100) != 0) return Result(FormatDetectionStatus.UnsupportedProtected, FormatContainerFamily.CompoundFile, FormatIdentityConfidence.PackageIdentity, template: template);
                    if (root.ContainsEntry("Macros") || root.ContainsEntry("_VBA_PROJECT_CUR"))
                        return Result(FormatDetectionStatus.UnsupportedMacro, FormatContainerFamily.CompoundFile, FormatIdentityConfidence.PackageIdentity, template: template);
                    return template ? Result(FormatDetectionStatus.Unknown, FormatContainerFamily.CompoundFile, FormatIdentityConfidence.PackageIdentity, template: true)
                        : Result(FormatDetectionStatus.Recognized, FormatContainerFamily.CompoundFile, FormatIdentityConfidence.PackageIdentity, DocumentFormat.Doc, false);
                }
            }
            catch (Exception error) when (error is FileFormatException or EndOfStreamException or FileNotFoundException or ArgumentException or OverflowException)
            { return Result(FormatDetectionStatus.Invalid, FormatContainerFamily.CompoundFile); }
        }
        if (read >= 4 && header[0] == 'P' && header[1] == 'K' && header[2] == 3 && header[3] == 4)
        {
            ZipSecurity.Result security = ZipSecurity.Examine(input, context);
            if (security == ZipSecurity.Result.Protected)
                return Result(FormatDetectionStatus.UnsupportedProtected, FormatContainerFamily.Unknown, FormatIdentityConfidence.Signature);
            if (security == ZipSecurity.Result.Invalid) return Result(FormatDetectionStatus.Invalid);
            if (security == ZipSecurity.Result.Unsupported) return Result(FormatDetectionStatus.Unknown, confidence: FormatIdentityConfidence.Signature);
            try { return DetectZip(input, context); }
            catch (InvalidDataException error) when (!error.Message.StartsWith("Limit.", StringComparison.Ordinal))
            { return Result(FormatDetectionStatus.Invalid); }
            catch (Exception error) when (error is EndOfStreamException or FileFormatException)
            { return Result(FormatDetectionStatus.Invalid); }
        }
        return Result(FormatDetectionStatus.Unknown);
    }

    private static DocumentFormatInspection DetectZip(MemoryStream input, ConversionContext context)
    {
        DocumentFormatInspection Result(FormatDetectionStatus status, FormatContainerFamily family = FormatContainerFamily.Unknown,
            FormatIdentityConfidence confidence = FormatIdentityConfidence.Unknown, DocumentFormat format = DocumentFormat.Unknown, bool? template = null)
            => new DocumentFormatInspection(status, family, confidence, format, template, input.Length);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, true);
        System.Collections.ObjectModel.ReadOnlyCollection<ZipArchiveEntry> archiveEntries;
        // Framework validates entry names while loading the central directory and can
        // throw before our own path checks see a malformed name.
        try { archiveEntries = archive.Entries; }
        catch (ArgumentException) { return Result(FormatDetectionStatus.Invalid); }
        if (archiveEntries.Count > context.Options.MaximumArchiveEntries) throw new InvalidDataException("Limit.ArchiveEntries: Too many entries for format inspection.");
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        var insensitive = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long expanded = 0;
        foreach (ZipArchiveEntry entry in archiveEntries)
        {
            context.CheckCancellation();
            if (entry.Length > context.Options.MaximumExpandedBytes - expanded) throw new InvalidDataException("Limit.ExpandedBytes: Package expansion exceeds the inspection limit.");
            expanded += entry.Length;
            string name = entry.FullName; bool directory = name.Length != 0 && name[name.Length - 1] == '/';
            string path = directory ? name.Substring(0, name.Length - 1) : name;
            if (path.Length == 0 || path.Any(c => c is '\\' or ':' or '%' or '?' or '#' || char.IsControl(c))
                || path.Split('/').Any(part => part.Length == 0 || part is "." or "..") || !insensitive.Add(name))
                return Result(FormatDetectionStatus.Invalid);
            if (!directory) entries.Add(name, entry);
        }
        byte[] Read(string name, long limit)
        {
            if (!entries.TryGetValue(name, out ZipArchiveEntry? entry)) throw new InvalidDataException("Format.MissingPart: Identity metadata is missing.");
            if (entry.Length > limit) throw new InvalidDataException("Limit.ExpandedBytes: Identity metadata exceeds the inspection limit.");
            using Stream source = entry.Open(); using var output = new BoundedMemoryStream(limit); byte[] buffer = new byte[8192]; int count;
            while ((count = source.Read(buffer, 0, buffer.Length)) != 0) { context.CheckCancellation(); output.Write(buffer, 0, count); }
            if (output.Length != entry.Length) throw new InvalidDataException("Format.Length: Identity metadata length is inconsistent.");
            return output.ToArray();
        }
        long xmlCharacters = 0, xmlNodes = 0;
        XDocument Xml(string name)
        {
            byte[] bytes = Read(name, context.Options.MaximumExpandedBytes);
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = context.Options.MaximumExpandedBytes };
            using var stream = new MemoryStream(bytes, false);
            try
            {
                using (XmlReader reader = XmlReader.Create(stream, settings))
                    while (reader.Read())
                    {
                        context.CheckCancellation();
                        if (reader.Depth > context.Options.MaximumNestingDepth) throw new InvalidDataException("Limit.NestingDepth: Package identity XML exceeds the nesting limit.");
                        xmlNodes++; xmlCharacters += reader.Name.Length + (reader.HasValue ? reader.Value.Length : 0);
                        if (reader.NodeType == XmlNodeType.Element && reader.HasAttributes)
                        {
                            while (reader.MoveToNextAttribute()) xmlCharacters += reader.Name.Length + reader.Value.Length;
                            reader.MoveToElement();
                        }
                        if (xmlCharacters > context.Options.MaximumCharacters) throw new InvalidDataException("Limit.Characters: Package identity XML exceeds the aggregate inspection limit.");
                        if (expanded + xmlNodes * 64 + xmlCharacters * 2 > context.Options.MaximumExpandedBytes)
                            throw new InvalidDataException("Limit.ExpandedBytes: Package identity XML exceeds the aggregate inspection limit.");
                    }
                context.CheckCancellation(); stream.Position = 0;
                using XmlReader parsed = XmlReader.Create(stream, settings);
                XDocument document = XDocument.Load(parsed, LoadOptions.PreserveWhitespace);
                context.CheckCancellation(); return document;
            }
            catch (XmlException error) { throw new InvalidDataException("Format.Xml: Invalid or prohibited package identity XML.", error); }
        }
        bool opc = entries.ContainsKey("[Content_Types].xml") || entries.ContainsKey("_rels/.rels");
        if (entries.TryGetValue("mimetype", out ZipArchiveEntry? mimeEntry) && mimeEntry.Length > 256) return Result(FormatDetectionStatus.Invalid);
        string? mime = mimeEntry == null ? null : Encoding.ASCII.GetString(Read("mimetype", 256));
        bool odf = mime is OdfPackage.MediaType or OdfPackage.TemplateMediaType;
        if (opc && odf) return Result(FormatDetectionStatus.Invalid);
        if (odf)
        {
            bool template = mime == OdfPackage.TemplateMediaType;
            if (archiveEntries.Count == 0 || archiveEntries[0].FullName != "mimetype" || !OdfHeader(input, mime!)
                || !entries.ContainsKey("META-INF/manifest.xml") || !entries.ContainsKey("content.xml"))
                return Result(FormatDetectionStatus.Invalid, FormatContainerFamily.OpenDocument);
            XDocument manifest = Xml("META-INF/manifest.xml"); XElement? root = manifest.Root;
            if (root?.Name != OdfPackage.Manifest + "manifest" || (string?)root.Attribute(OdfPackage.Manifest + "version") != "1.3")
                return Result(FormatDetectionStatus.Invalid, FormatContainerFamily.OpenDocument);
            XElement[] roots = root.Elements(OdfPackage.Manifest + "file-entry").Where(e => (string?)e.Attribute(OdfPackage.Manifest + "full-path") == "/").ToArray();
            if (roots.Length != 1 || (string?)roots[0].Attribute(OdfPackage.Manifest + "media-type") != mime)
                return Result(FormatDetectionStatus.Invalid, FormatContainerFamily.OpenDocument);
            if (root.Descendants(OdfPackage.Manifest + "encryption-data").Any())
                return Result(FormatDetectionStatus.UnsupportedProtected, FormatContainerFamily.OpenDocument, FormatIdentityConfidence.PackageIdentity, template: template);
            if (entries.Keys.Any(n => n.StartsWith("Scripts/", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Basic/", StringComparison.OrdinalIgnoreCase))
                || root.Descendants(OdfPackage.Manifest + "file-entry").Any(e => DocxPackage.ContainsIgnoreCase((string?)e.Attribute(OdfPackage.Manifest + "media-type") ?? "", "script")))
                return Result(FormatDetectionStatus.UnsupportedMacro, FormatContainerFamily.OpenDocument, FormatIdentityConfidence.PackageIdentity, template: template);
            XDocument content = Xml("content.xml"); XNamespace office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
            if (content.Root?.Name != office + "document-content" || (string?)content.Root.Attribute(office + "version") != "1.3")
                return Result(FormatDetectionStatus.Invalid, FormatContainerFamily.OpenDocument);
            foreach (string name in entries.Keys.Where(n => n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && n != "META-INF/manifest.xml"))
            {
                XDocument part = name == "content.xml" ? content : Xml(name);
                if (part.Descendants().Any(e => DocxPackage.ContainsIgnoreCase(e.Name.NamespaceName, "script") || e.Name.LocalName is "script" or "event-listener"
                    || e.Name.LocalName is "scripts" or "event-listeners" && (e.HasElements || e.Attributes().Any(a => !a.IsNamespaceDeclaration) || !string.IsNullOrWhiteSpace(e.Value))))
                    return Result(FormatDetectionStatus.UnsupportedMacro, FormatContainerFamily.OpenDocument, FormatIdentityConfidence.PackageIdentity, template: template);
            }
            return Result(FormatDetectionStatus.Recognized, FormatContainerFamily.OpenDocument, FormatIdentityConfidence.PackageIdentity, template ? DocumentFormat.Ott : DocumentFormat.Odt, template);
        }
        if (!opc) return Result(FormatDetectionStatus.Unknown);
        if (!entries.ContainsKey("[Content_Types].xml") || !entries.ContainsKey("_rels/.rels")) return Result(FormatDetectionStatus.Invalid, FormatContainerFamily.OfficeOpenXml);
        XDocument types = Xml("[Content_Types].xml"), relations = Xml("_rels/.rels");
        if (types.Root?.Name != DocxPackage.ContentTypes + "Types" || relations.Root?.Name != DocxPackage.Relationships + "Relationships")
            return Result(FormatDetectionStatus.Invalid, FormatContainerFamily.OfficeOpenXml);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (XElement relation in relations.Root.Elements())
        {
            string? id = (string?)relation.Attribute("Id"), type = (string?)relation.Attribute("Type"), target = (string?)relation.Attribute("Target");
            if (relation.Name != DocxPackage.Relationships + "Relationship" || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(type) || string.IsNullOrEmpty(target) || !ids.Add(id!))
                return Result(FormatDetectionStatus.Invalid, FormatContainerFamily.OfficeOpenXml);
        }
        XElement[] officeRelations = relations.Root.Elements(DocxPackage.Relationships + "Relationship")
            .Where(r => (string?)r.Attribute("Type") == DocxPackage.DocumentRelationships.NamespaceName + "/officeDocument").ToArray();
        if (officeRelations.Length != 1) return Result(FormatDetectionStatus.Invalid, FormatContainerFamily.OfficeOpenXml);
        string main = DocxPackage.ResolvePart("", officeRelations[0]);
        string? declaredType = DocxPackage.ContentTypeFor(types.Root, main, context);
        if (declaredType == null || !entries.ContainsKey(main)) return Result(FormatDetectionStatus.Invalid, FormatContainerFamily.OfficeOpenXml);
        string contentType = declaredType;
        bool document = contentType == DocxPackage.MainContentType, templateType = contentType == DocxPackage.TemplateContentType;
        bool macro = DocxPackage.ContainsIgnoreCase(contentType, "macroEnabled")
            || entries.Keys.Any(n => n.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase))
            || types.Root.Elements().Any(e => DocxPackage.ContainsIgnoreCase((string?)e.Attribute("ContentType") ?? "", "vbaProject"));
        string mainRelationships = DocxPackage.RelationshipPartName(main);
        if (entries.ContainsKey(mainRelationships))
        {
            XDocument own = Xml(mainRelationships);
            if (own.Root?.Name != DocxPackage.Relationships + "Relationships") return Result(FormatDetectionStatus.Invalid, FormatContainerFamily.OfficeOpenXml);
            macro |= own.Root.Elements(DocxPackage.Relationships + "Relationship")
                .Any(e => DocxPackage.ContainsIgnoreCase((string?)e.Attribute("Type") ?? "", "vbaProject"));
        }
        bool? isTemplate = document ? false : templateType ? true : DocxPackage.ContainsIgnoreCase(contentType, "template") ? true
            : DocxPackage.ContainsIgnoreCase(contentType, "document") ? false : null;
        if (macro) return Result(FormatDetectionStatus.UnsupportedMacro, FormatContainerFamily.OfficeOpenXml, FormatIdentityConfidence.PackageIdentity, template: isTemplate);
        if (!document && !templateType) return Result(FormatDetectionStatus.Unknown, FormatContainerFamily.OfficeOpenXml, FormatIdentityConfidence.PackageIdentity);
        return Result(FormatDetectionStatus.Recognized, FormatContainerFamily.OfficeOpenXml, FormatIdentityConfidence.PackageIdentity,
            templateType ? DocumentFormat.Dotx : DocumentFormat.Docx, templateType);
    }

    private static bool OdfHeader(MemoryStream input, string mediaType)
    {
        input.Position = 0;
        using var reader = new BinaryReader(input, Encoding.UTF8, true);
        if (input.Length < 38 || reader.ReadUInt32() != 0x04034b50) return false;
        _ = reader.ReadUInt16(); ushort flags = reader.ReadUInt16(), method = reader.ReadUInt16();
        input.Position += 16; ushort nameLength = reader.ReadUInt16(), extraLength = reader.ReadUInt16();
        return (flags & 9) == 0 && method == 0 && nameLength == 8 && extraLength == 0
            && Encoding.ASCII.GetString(reader.ReadBytes(8)) == "mimetype" && Encoding.ASCII.GetString(reader.ReadBytes(mediaType.Length)) == mediaType;
    }
}
