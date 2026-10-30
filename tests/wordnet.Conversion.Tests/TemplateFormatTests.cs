using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using OpenMcdf;
using UglyToad.PdfPig;
using Xunit;
using Wp = DocumentFormat.OpenXml.Wordprocessing;

namespace WordNet.Tests;

public sealed class TemplateFormatTests
{
    private const string OdtMime = "application/vnd.oasis.opendocument.text";
    private const string OttMime = "application/vnd.oasis.opendocument.text-template";
    private static readonly XNamespace Ct = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace Manifest = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";
    private static readonly XNamespace W = TestDocuments.W;

    private static byte[] Word(WordprocessingDocumentType kind)
    {
        using var output = new MemoryStream();
        using (WordprocessingDocument package = WordprocessingDocument.Create(output, kind, true))
        {
            MainDocumentPart main = package.AddMainDocumentPart();
            main.Document = new Wp.Document(new Wp.Body(new Wp.Paragraph(new Wp.Run(new Wp.Text("Independent template text")))));
            main.Document.Save();
        }
        return output.ToArray();
    }
    private static byte[] Odf(bool template, bool active = false, bool encrypted = false, bool inlineScript = false, bool manyXml = false)
    {
        string mime = template ? OttMime : OdtMime;
        string script = inlineScript ? "<office:scripts xmlns:script='urn:oasis:names:tc:opendocument:xmlns:script:1.0'><script:event-listener script:macro-name='Ignored'/></office:scripts>" : "";
        var parts = new List<(string Name, string Content)>
        {
            ("mimetype", mime),
            ("content.xml", "<office:document-content xmlns:office='urn:oasis:names:tc:opendocument:xmlns:office:1.0' xmlns:text='urn:oasis:names:tc:opendocument:xmlns:text:1.0' office:version='1.3'>" + script + "<office:body><office:text><text:p>Independent ODF text</text:p></office:text></office:body></office:document-content>"),
            ("META-INF/manifest.xml", "<manifest:manifest xmlns:manifest='" + Manifest + "' manifest:version='1.3'>"
                + "<manifest:file-entry manifest:full-path='/' manifest:media-type='" + mime + "'/>"
                + "<manifest:file-entry manifest:full-path='content.xml' manifest:media-type='text/xml'>"
                + (encrypted ? "<manifest:encryption-data/>" : "") + "</manifest:file-entry></manifest:manifest>"),
        };
        if (active) parts.Add(("Basic/script.xml", "<script/>"));
        if (manyXml) { parts.Add(("extra1.xml", "<x>" + new string('a', 400) + "</x>")); parts.Add(("extra2.xml", "<x>" + new string('b', 400) + "</x>")); }
        return StoredFixture(parts);
    }

    // Independently author stored ZIP records. Framework ZipArchive's
    // NoCompression still emits Deflate, which is invalid for ODF mimetype.
    private static byte[] StoredFixture(IEnumerable<(string Name, string Content)> parts)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        var entries = new List<(byte[] Name, byte[] Data, uint Crc, uint Offset)>();
        foreach (var part in parts)
        {
            byte[] name = Encoding.ASCII.GetBytes(part.Name), data = Encoding.UTF8.GetBytes(part.Content);
            uint crc = uint.MaxValue;
            foreach (byte value in data)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? crc >> 1 ^ 0xedb88320u : crc >> 1;
            }
            crc = ~crc; uint offset = checked((uint)output.Position), length = checked((uint)data.Length);
            entries.Add((name, data, crc, offset));
            writer.Write(0x04034b50u); writer.Write((ushort)20); writer.Write((ushort)0); writer.Write((ushort)0);
            writer.Write((ushort)0); writer.Write((ushort)33); writer.Write(crc); writer.Write(length); writer.Write(length);
            writer.Write(checked((ushort)name.Length)); writer.Write((ushort)0); writer.Write(name); writer.Write(data);
        }
        uint directory = checked((uint)output.Position);
        foreach (var entry in entries)
        {
            uint length = checked((uint)entry.Data.Length);
            writer.Write(0x02014b50u); writer.Write((ushort)20); writer.Write((ushort)20); writer.Write((ushort)0); writer.Write((ushort)0);
            writer.Write((ushort)0); writer.Write((ushort)33); writer.Write(entry.Crc); writer.Write(length); writer.Write(length);
            writer.Write(checked((ushort)entry.Name.Length)); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)0);
            writer.Write((ushort)0); writer.Write(0u); writer.Write(entry.Offset); writer.Write(entry.Name);
        }
        uint size = checked((uint)output.Position - directory); ushort count = checked((ushort)entries.Count);
        writer.Write(0x06054b50u); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write(count); writer.Write(count);
        writer.Write(size); writer.Write(directory); writer.Write((ushort)0); writer.Flush();
        return output.ToArray();
    }
    private static void Put(ZipArchive zip, string name, string content, CompressionLevel compression = CompressionLevel.Optimal)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name, compression).Open(), new UTF8Encoding(false)); writer.Write(content);
    }
    private static byte[] Edit(byte[] source, Action<ZipArchive> change)
    {
        using var output = new MemoryStream(); output.Write(source, 0, source.Length);
        using (var zip = new ZipArchive(output, ZipArchiveMode.Update, true)) change(zip);
        return output.ToArray();
    }
    private static byte[] MarkLaterZipEntryEncrypted(byte[] source, byte flag = 1)
    {
        byte[] bytes = (byte[])source.Clone(); int end = ZipEnd(bytes);
        int U16(int offset) => bytes[offset] | bytes[offset + 1] << 8;
        int U32(int offset) => U16(offset) | U16(offset + 2) << 16;
        int position = U32(end + 16), count = U16(end + 10);
        for (int i = 0; i < count; i++)
        {
            int local = U32(position + 42);
            if (local > 0) { bytes[position + 8] |= flag; bytes[local + 6] |= flag; return bytes; }
            position += 46 + U16(position + 28) + U16(position + 30) + U16(position + 32);
        }
        throw new InvalidDataException("The ZIP fixture requires a later local entry.");
    }
    private static int ZipEnd(byte[] bytes)
    {
        int end = bytes.Length - 22;
        while (end >= 0 && !(bytes[end] == 0x50 && bytes[end + 1] == 0x4b && bytes[end + 2] == 5 && bytes[end + 3] == 6)) end--;
        if (end < 0) throw new InvalidDataException("Missing ZIP end record.");
        return end;
    }
    private static byte[] ControlCharacterInEntryName(byte[] source)
    {
        byte[] bytes = (byte[])source.Clone(); int end = ZipEnd(bytes);
        int U16(int offset) => bytes[offset] | bytes[offset + 1] << 8;
        int U32(int offset) => U16(offset) | U16(offset + 2) << 16;
        int position = U32(end + 16), count = U16(end + 10);
        const string expected = "word/evil-name.xml";
        for (int i = 0; i < count; i++)
        {
            int nameLength = U16(position + 28), local = U32(position + 42);
            if (Encoding.UTF8.GetString(bytes, position + 46, nameLength) == expected)
            {
                int localLength = U16(local + 26), change = expected.IndexOf('-');
                if (Encoding.UTF8.GetString(bytes, local + 30, localLength) != expected) throw new InvalidDataException("The ZIP fixture has inconsistent entry names.");
                bytes[position + 46 + change] = (byte)'\n'; bytes[local + 30 + change] = (byte)'\n'; return bytes;
            }
            position += 46 + nameLength + U16(position + 30) + U16(position + 32);
        }
        throw new InvalidDataException("The ZIP fixture lacks its named test entry.");
    }
    private static XDocument Xml(ZipArchive zip, string name)
    { using Stream input = zip.GetEntry(name)!.Open(); return XDocument.Load(input); }
    private static void ReplaceXml(ZipArchive zip, string name, XDocument xml)
    { zip.GetEntry(name)!.Delete(); using Stream output = zip.CreateEntry(name).Open(); xml.Save(output); }
    private static byte[] AsTemplate(byte[] document) => Edit(document, zip =>
    {
        XDocument types = Xml(zip, "[Content_Types].xml");
        XElement main = types.Root!.Elements(Ct + "Override").Single(e => (string?)e.Attribute("PartName") == "/word/document.xml");
        main.SetAttributeValue("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml");
        ReplaceXml(zip, "[Content_Types].xml", types);
    });
    private static void ValidateWord(byte[] bytes, WordprocessingDocumentType kind)
    {
        using var package = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        Assert.Equal(kind, package.DocumentType); Assert.Empty(new OpenXmlValidator().Validate(package));
    }
    private static void ValidateOdf(byte[] bytes, string mime, string expected = "Independent ODF text")
    {
        Assert.Equal(0, BitConverter.ToUInt16(bytes, 8)); // ODF requires an uncompressed first local entry.
        Assert.Equal(0, BitConverter.ToUInt16(bytes, 28)); // Its mimetype entry must not have local extra data.
        using var zip = new ZipArchive(new MemoryStream(bytes));
        Assert.Equal("mimetype", zip.Entries[0].FullName);
        using (var reader = new StreamReader(zip.GetEntry("mimetype")!.Open(), Encoding.ASCII)) Assert.Equal(mime, reader.ReadToEnd());
        XElement root = Xml(zip, "META-INF/manifest.xml").Root!;
        Assert.Equal(mime, (string?)root.Elements(Manifest + "file-entry").Single(e => (string?)e.Attribute(Manifest + "full-path") == "/").Attribute(Manifest + "media-type"));
        string text = string.Concat(Xml(zip, "content.xml").Root!.Value.Where(c => !char.IsWhiteSpace(c)));
        Assert.Contains(string.Concat(expected.Where(c => !char.IsWhiteSpace(c))), text, StringComparison.Ordinal);
    }
    private static byte[] Convert(byte[] source, DocumentFormat from, DocumentFormat to)
    {
        using var input = new MemoryStream(source); using var output = new MemoryStream();
        new WordConverter().Convert(input, output, from, to, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        Assert.True(input.CanRead); Assert.True(output.CanWrite); return output.ToArray();
    }
    private static DocumentFormatInspection Detect(byte[] bytes)
    { using var source = new MemoryStream(bytes); return WordConverter.DetectFormat(source, cancellationToken: TestContext.Current.CancellationToken); }

    [Fact]
    public void DotxUsesTemplateIdentityThroughConversionEditingAndFixedOutput()
    {
        byte[] source = Word(WordprocessingDocumentType.Template); ValidateWord(source, WordprocessingDocumentType.Template);
        byte[] document = Convert(source, DocumentFormat.Dotx, DocumentFormat.Docx); ValidateWord(document, WordprocessingDocumentType.Document);
        byte[] template = Convert(document, DocumentFormat.Docx, DocumentFormat.Dotx); ValidateWord(template, WordprocessingDocumentType.Template);
        using var input = new MemoryStream(source);
        WordDocument editing = WordDocument.Load(input, DocumentFormat.Dotx, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        editing.AddParagraph("Edited text", cancellationToken: TestContext.Current.CancellationToken); using var output = new MemoryStream(); editing.Save(output, DocumentFormat.Dotx, cancellationToken: TestContext.Current.CancellationToken);
        ValidateWord(output.ToArray(), WordprocessingDocumentType.Template);
        using PdfDocument pdf = PdfDocument.Open(Convert(source, DocumentFormat.Dotx, DocumentFormat.Pdf));
        Assert.Contains("Independent template text", pdf.GetPage(1).Text, StringComparison.Ordinal);
        using (var xps = new ZipArchive(new MemoryStream(Convert(source, DocumentFormat.Dotx, DocumentFormat.Xps))))
        {
            string text = string.Concat(Xml(xps, "Documents/1/Pages/1.fpage").Descendants().Where(e => e.Name.LocalName == "Glyphs").Select(e => (string?)e.Attribute("UnicodeString")));
            Assert.Contains("Independent template text", text, StringComparison.Ordinal);
        }
        using var wrong = new MemoryStream(source); using var destination = new MemoryStream(); destination.WriteByte(42);
        Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(wrong, destination, DocumentFormat.Docx, DocumentFormat.Dotx,
            new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, destination.ToArray()); Assert.True(wrong.CanRead); Assert.True(destination.CanWrite);
    }

    [Fact]
    public void OttUsesTemplateMimeAndManifestThroughConversionEditingAndPdf()
    {
        byte[] source = Odf(template: true); ValidateOdf(source, OttMime);
        byte[] document = Convert(source, DocumentFormat.Ott, DocumentFormat.Odt); ValidateOdf(document, OdtMime);
        byte[] template = Convert(document, DocumentFormat.Odt, DocumentFormat.Ott); ValidateOdf(template, OttMime);
        byte[] word = Convert(source, DocumentFormat.Ott, DocumentFormat.Docx); ValidateWord(word, WordprocessingDocumentType.Document);
        using var input = new MemoryStream(source);
        WordDocument editing = WordDocument.Load(input, DocumentFormat.Ott, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        editing.AddParagraph("Edited text", cancellationToken: TestContext.Current.CancellationToken); using var output = new MemoryStream(); editing.Save(output, DocumentFormat.Ott, cancellationToken: TestContext.Current.CancellationToken);
        ValidateOdf(output.ToArray(), OttMime);
        using PdfDocument pdf = PdfDocument.Open(Convert(source, DocumentFormat.Ott, DocumentFormat.Pdf));
        Assert.Contains("Independent ODF text", pdf.GetPage(1).Text, StringComparison.Ordinal);
        using var wrong = new MemoryStream(source); using var destination = new MemoryStream(); destination.WriteByte(42);
        Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(wrong, destination, DocumentFormat.Odt, DocumentFormat.Ott,
            new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, destination.ToArray());
    }

    [Fact]
    public void DotxRetainsAdmittedReviewAndTypedControlSemantics()
    {
        string revision = "<w:p><w:ins w:id='1' w:author='Reviewer'><w:r><w:t>Inserted</w:t></w:r></w:ins></w:p>";
        byte[] reviewed = AsTemplate(TestDocuments.Docx(TestDocuments.Body(revision))); ValidateWord(reviewed, WordprocessingDocumentType.Template);
        using var reviewInput = new MemoryStream(reviewed);
        WordDocument review = WordDocument.Load(reviewInput, DocumentFormat.Dotx, new ReviewLoadOptions(), out _,
            new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        Assert.Single(review.Revisions); using var reviewOutput = new MemoryStream(); review.Save(reviewOutput, DocumentFormat.Dotx, cancellationToken: TestContext.Current.CancellationToken);
        ValidateWord(reviewOutput.ToArray(), WordprocessingDocumentType.Template);
        reviewOutput.Position = 0;
        Assert.Single(WordDocument.Load(reviewOutput, DocumentFormat.Dotx, new ReviewLoadOptions(), out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken).Revisions);
        string control = "<w:p><w:sdt><w:sdtPr><w:id w:val='1'/><w:text/></w:sdtPr><w:sdtContent><w:r><w:t>Old</w:t></w:r></w:sdtContent></w:sdt></w:p>";
        byte[] controlled = AsTemplate(TestDocuments.Docx(TestDocuments.Body(control))); ValidateWord(controlled, WordprocessingDocumentType.Template);
        using var controlInput = new MemoryStream(controlled);
        WordDocument document = WordDocument.LoadWithControls(controlInput, DocumentFormat.Dotx, new ContentControlLoadOptions(), out _,
            new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        document.SetControlText(Assert.Single(document.ContentControls), "Changed", TestContext.Current.CancellationToken);
        using var controlOutput = new MemoryStream(); document.Save(controlOutput, DocumentFormat.Dotx, cancellationToken: TestContext.Current.CancellationToken);
        ValidateWord(controlOutput.ToArray(), WordprocessingDocumentType.Template); controlOutput.Position = 0;
        Assert.Equal("Changed", Assert.Single(WordDocument.LoadWithControls(controlOutput, DocumentFormat.Dotx, new ContentControlLoadOptions(), out _,
            new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken).ContentControls).DisplayText);
        using var controlConversion = new MemoryStream(controlled); using var flattened = new MemoryStream();
        ConversionResult loss = new WordConverter().Convert(controlConversion, flattened, DocumentFormat.Dotx, DocumentFormat.Docx,
            new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        Assert.Contains(loss.Diagnostics, d => d.Code == "Template.UnsupportedControl" && d.Severity == DiagnosticSeverity.Warning);
        using var strictInput = new MemoryStream(controlled); using var strictOutput = new MemoryStream(); strictOutput.WriteByte(42);
        Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(strictInput, strictOutput, DocumentFormat.Dotx, DocumentFormat.Docx,
            new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, strictOutput.ToArray());
        string tagged = "<w:p><w:sdt><w:sdtPr><w:tag w:val='merge:Customer'/></w:sdtPr><w:sdtContent><w:r><w:t>Cached</w:t></w:r></w:sdtContent></w:sdt></w:p>";
        byte[] taggedTemplate = AsTemplate(TestDocuments.Docx(TestDocuments.Body(tagged)));
        byte[] taggedDocument = Convert(taggedTemplate, DocumentFormat.Dotx, DocumentFormat.Docx); ValidateWord(taggedDocument, WordprocessingDocumentType.Document);
        using var taggedZip = new ZipArchive(new MemoryStream(taggedDocument));
        Assert.Contains(Xml(taggedZip, "word/document.xml").Descendants(W + "tag"), e => (string?)e.Attribute(W + "val") == "merge:Customer");
        string field = "<w:p><w:fldSimple w:instr=' MERGEFIELD Customer '><w:r><w:t>Cached</w:t></w:r></w:fldSimple></w:p>";
        byte[] templatedField = AsTemplate(TestDocuments.Docx(TestDocuments.Body(field))); ValidateWord(templatedField, WordprocessingDocumentType.Template);
        byte[] savedField = Convert(templatedField, DocumentFormat.Dotx, DocumentFormat.Docx); ValidateWord(savedField, WordprocessingDocumentType.Document);
        using var zip = new ZipArchive(new MemoryStream(savedField));
        Assert.Contains(Xml(zip, "word/document.xml").Descendants(W + "fldSimple"), e => ((string?)e.Attribute(W + "instr"))?.Contains("MERGEFIELD") == true && e.Value == "Cached");
        byte[] reverse = Convert(TestDocuments.Docx(TestDocuments.Body(field)), DocumentFormat.Docx, DocumentFormat.Dotx); ValidateWord(reverse, WordprocessingDocumentType.Template);
        using var reverseZip = new ZipArchive(new MemoryStream(reverse));
        Assert.Contains(Xml(reverseZip, "word/document.xml").Descendants(W + "fldSimple"), e => ((string?)e.Attribute(W + "instr"))?.Contains("MERGEFIELD") == true && e.Value == "Cached");
    }

    [Theory]
    [InlineData(DocumentFormat.Dotx, DocumentFormat.Odt)]
    [InlineData(DocumentFormat.Dotx, DocumentFormat.Ott)]
    [InlineData(DocumentFormat.Odt, DocumentFormat.Dotx)]
    [InlineData(DocumentFormat.Ott, DocumentFormat.Dotx)]
    [InlineData(DocumentFormat.Docx, DocumentFormat.Ott)]
    public void TemplateCrossFormatDirectionsRetainIndependentPackageIdentityAndText(DocumentFormat from, DocumentFormat to)
    {
        bool word = from is DocumentFormat.Docx or DocumentFormat.Dotx;
        byte[] source = word ? Word(from == DocumentFormat.Dotx ? WordprocessingDocumentType.Template : WordprocessingDocumentType.Document) : Odf(from == DocumentFormat.Ott);
        string expected = word ? "Independent template text" : "Independent ODF text";
        byte[] converted = Convert(source, from, to);
        if (to == DocumentFormat.Dotx)
        {
            ValidateWord(converted, WordprocessingDocumentType.Template);
            using var package = WordprocessingDocument.Open(new MemoryStream(converted), false);
            Assert.Contains(expected, package.MainDocumentPart!.Document!.InnerText, StringComparison.Ordinal);
        }
        else ValidateOdf(converted, to == DocumentFormat.Ott ? OttMime : OdtMime, expected);
    }

    [Fact]
    public void DetectionDistinguishesPackageFamilyAndTemplateIdentity()
    {
        foreach (var item in new[] { (Word(WordprocessingDocumentType.Document), DocumentFormat.Docx, FormatContainerFamily.OfficeOpenXml, false),
            (Word(WordprocessingDocumentType.Template), DocumentFormat.Dotx, FormatContainerFamily.OfficeOpenXml, true),
            (Odf(false), DocumentFormat.Odt, FormatContainerFamily.OpenDocument, false), (Odf(true), DocumentFormat.Ott, FormatContainerFamily.OpenDocument, true) })
        {
            DocumentFormatInspection result = Detect(item.Item1);
            Assert.Equal(FormatDetectionStatus.Recognized, result.Status); Assert.Equal(item.Item2, result.Format);
            Assert.Equal(item.Item3, result.Family); Assert.Equal(item.Item4, result.IsTemplate);
            Assert.Equal(FormatIdentityConfidence.PackageIdentity, result.Confidence); Assert.Equal(item.Item1.Length, result.BytesRead);
        }
        DocumentFormatInspection pdf = Detect(Encoding.ASCII.GetBytes("%PDF-1.7\n"));
        Assert.Equal(DocumentFormat.Pdf, pdf.Format); Assert.Equal(FormatIdentityConfidence.Signature, pdf.Confidence);
        Assert.Equal(FormatDetectionStatus.Unknown, Detect(Encoding.UTF8.GetBytes("ambiguous text")).Status);
        byte[] binary = File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "plain.doc"));
        Assert.Equal(DocumentFormat.Doc, Detect(binary).Format);
    }

    [Fact]
    public void DetectionRespectsPositionsOwnershipLimitsAndCancellation()
    {
        byte[] word = Word(WordprocessingDocumentType.Template), prefixed = new byte[word.Length + 4];
        Array.Copy(word, 0, prefixed, 4, word.Length); using var seekable = new MemoryStream(prefixed); seekable.Position = 4;
        DocumentFormatInspection result = WordConverter.DetectFormat(seekable, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(DocumentFormat.Dotx, result.Format); Assert.Equal(word.Length, result.BytesRead); Assert.Equal(4, seekable.Position); Assert.True(seekable.CanRead);
        using var nonseekable = new NonSeekable(word);
        Assert.Equal(DocumentFormat.Dotx, WordConverter.DetectFormat(nonseekable, cancellationToken: TestContext.Current.CancellationToken).Format);
        Assert.Equal(word.Length, nonseekable.BytesRead); Assert.True(nonseekable.CanRead);
        using var limited = new NonSeekable(word);
        Assert.Throws<InvalidDataException>(() => WordConverter.DetectFormat(limited, new ConversionOptions(maximumInputBytes: 16), TestContext.Current.CancellationToken));
        Assert.Equal(17, limited.BytesRead); Assert.True(limited.CanRead);
        seekable.Position = 4;
        Assert.Throws<InvalidDataException>(() => WordConverter.DetectFormat(seekable, new ConversionOptions(maximumArchiveEntries: 1), TestContext.Current.CancellationToken));
        Assert.Equal(4, seekable.Position);
        Assert.Throws<InvalidDataException>(() => WordConverter.DetectFormat(seekable, new ConversionOptions(maximumExpandedBytes: 32), TestContext.Current.CancellationToken));
        Assert.Equal(4, seekable.Position);
        InvalidDataException perPart = Assert.Throws<InvalidDataException>(() => WordConverter.DetectFormat(seekable,
            new ConversionOptions(maximumCharacters: 1), TestContext.Current.CancellationToken));
        Assert.StartsWith("Limit.Characters", perPart.Message, StringComparison.Ordinal); Assert.Equal(4, seekable.Position);
        byte[] manyXml = Odf(true, manyXml: true);
        using var aggregate = new MemoryStream(manyXml);
        InvalidDataException limit = Assert.Throws<InvalidDataException>(() => WordConverter.DetectFormat(aggregate,
            new ConversionOptions(maximumCharacters: 700), TestContext.Current.CancellationToken));
        Assert.StartsWith("Limit.Characters", limit.Message, StringComparison.Ordinal); Assert.Equal(0, aggregate.Position);
        using var binary = new MemoryStream(File.ReadAllBytes(Path.Combine(TestDocuments.FixtureDirectory, "plain.doc")));
        InvalidDataException compoundLimit = Assert.Throws<InvalidDataException>(() => WordConverter.DetectFormat(binary,
            new ConversionOptions(maximumExpandedBytes: 8), TestContext.Current.CancellationToken));
        Assert.StartsWith("Limit.ExpandedBytes", compoundLimit.Message, StringComparison.Ordinal); Assert.Equal(0, binary.Position);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => WordConverter.DetectFormat(seekable, cancellationToken: cancelled.Token));
        Assert.Equal(4, seekable.Position);
    }

    [Fact]
    public void DetectionRejectsConflictingTruncatedAndUnsafePackageIdentities()
    {
        byte[] word = Word(WordprocessingDocumentType.Template);
        byte[] duplicate = Edit(word, zip =>
        {
            XDocument types = Xml(zip, "[Content_Types].xml");
            XNamespace relations = "http://schemas.openxmlformats.org/package/2006/relationships";
            string target = (string)Xml(zip, "_rels/.rels").Root!.Elements(relations + "Relationship").Single().Attribute("Target")!;
            string part = "/" + target.TrimStart('/');
            types.Root!.Add(new XElement(Ct + "Override", new XAttribute("PartName", part),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml")));
            types.Root.Add(new XElement(Ct + "Override", new XAttribute("PartName", part),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")));
            ReplaceXml(zip, "[Content_Types].xml", types);
        });
        Assert.Equal(FormatDetectionStatus.Invalid, Detect(duplicate).Status);
        using var source = new MemoryStream(duplicate); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(source, output, DocumentFormat.Dotx, DocumentFormat.Docx,
            new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
        Assert.Equal(FormatDetectionStatus.Invalid, Detect(word.Take(word.Length / 2).ToArray()).Status);
        byte[] executablePrefix = new byte[word.Length + 2]; executablePrefix[0] = (byte)'M'; executablePrefix[1] = (byte)'Z';
        Array.Copy(word, 0, executablePrefix, 2, word.Length);
        using var prefixedInput = new MemoryStream(executablePrefix); using var prefixedOutput = new MemoryStream(); prefixedOutput.WriteByte(42);
        Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(prefixedInput, prefixedOutput, DocumentFormat.Dotx, DocumentFormat.Docx,
            new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, prefixedOutput.ToArray());
        byte[] zip64 = (byte[])word.Clone(); int end = ZipEnd(zip64); zip64[end + 8] = 255; zip64[end + 9] = 255; zip64[end + 10] = 255; zip64[end + 11] = 255;
        Assert.Equal(FormatDetectionStatus.Unknown, Detect(zip64).Status);
        using var zip64Input = new MemoryStream(zip64); using var zip64Output = new MemoryStream(); zip64Output.WriteByte(42);
        Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(zip64Input, zip64Output, DocumentFormat.Dotx, DocumentFormat.Docx,
            new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, zip64Output.ToArray());
        Assert.Equal(FormatDetectionStatus.Invalid, Detect(Edit(word, zip => Put(zip, "../outside.xml", "<x/>"))).Status);
        byte[] controlName = Edit(word, zip => Put(zip, "word/evil-name.xml", "<x/>"));
        controlName = ControlCharacterInEntryName(controlName);
        Assert.Equal(FormatDetectionStatus.Invalid, Detect(controlName).Status);
        using var controlInput = new MemoryStream(controlName); using var controlOutput = new MemoryStream(); controlOutput.WriteByte(42);
        Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(controlInput, controlOutput, DocumentFormat.Dotx, DocumentFormat.Docx,
            new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, controlOutput.ToArray());
        byte[] caseConflict = Edit(word, zip => Put(zip, "[content_types].xml", "<x/>"));
        Assert.Equal(FormatDetectionStatus.Invalid, Detect(caseConflict).Status);
        using var conflictInput = new MemoryStream(caseConflict); using var conflictOutput = new MemoryStream(); conflictOutput.WriteByte(42);
        Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(conflictInput, conflictOutput, DocumentFormat.Dotx, DocumentFormat.Docx,
            new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, conflictOutput.ToArray());
        Assert.Equal(FormatDetectionStatus.Invalid, Detect(Edit(word, zip => Put(zip, "mimetype", OdtMime, CompressionLevel.NoCompression))).Status);
        byte[] dtd = Edit(word, zip =>
        {
            zip.GetEntry("[Content_Types].xml")!.Delete();
            Put(zip, "[Content_Types].xml", "<!DOCTYPE Types [<!ENTITY x SYSTEM 'file:///nonexistent/never-read'>]><Types xmlns='" + Ct + "'><Default Extension='xml' ContentType='&x;'/></Types>");
        });
        Assert.Equal(FormatDetectionStatus.Invalid, Detect(dtd).Status);
        byte[] external = Edit(word, zip =>
        {
            XNamespace relations = "http://schemas.openxmlformats.org/package/2006/relationships";
            XDocument rels = Xml(zip, "_rels/.rels"); XElement relation = rels.Root!.Elements(relations + "Relationship").Single();
            relation.SetAttributeValue("Target", "https://example.invalid/document.xml"); relation.SetAttributeValue("TargetMode", "External");
            ReplaceXml(zip, "_rels/.rels", rels);
        });
        Assert.Equal(FormatDetectionStatus.Invalid, Detect(external).Status);
        Assert.Equal(FormatDetectionStatus.Invalid, Detect(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }).Status);
    }

    [Fact]
    public void DetectionDistinguishesMacrosAndProtectedContainersWithoutExecutingThem()
    {
        byte[] macro = Word(WordprocessingDocumentType.MacroEnabledTemplate);
        DocumentFormatInspection observed = Detect(macro);
        Assert.Equal(FormatDetectionStatus.UnsupportedMacro, observed.Status); Assert.Equal(FormatContainerFamily.OfficeOpenXml, observed.Family); Assert.True(observed.IsTemplate);
        byte[] disguised = Edit(Word(WordprocessingDocumentType.Template), zip => Put(zip, "word/vbaProject.bin", "not executable"));
        Assert.Equal(FormatDetectionStatus.UnsupportedMacro, Detect(disguised).Status);
        byte[] macroRelation = Edit(Word(WordprocessingDocumentType.Template), zip =>
        {
            XNamespace r = "http://schemas.openxmlformats.org/package/2006/relationships";
            string main = ((string)Xml(zip, "_rels/.rels").Root!.Elements(r + "Relationship").Single().Attribute("Target")!).TrimStart('/');
            int slash = main.LastIndexOf('/'); string name = main.Insert(slash + 1, "_rels/") + ".rels";
            zip.GetEntry(name)?.Delete();
            Put(zip, name, "<Relationships xmlns='" + r + "'><Relationship Id='vba' Type='http://schemas.microsoft.com/office/2006/relationships/vbaProject' Target='missing.bin'/></Relationships>");
        });
        Assert.Equal(FormatDetectionStatus.UnsupportedMacro, Detect(macroRelation).Status);
        using var input = new MemoryStream(disguised); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Dotx, DocumentFormat.Docx,
            new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
        byte[] disguisedDocument = Edit(Word(WordprocessingDocumentType.Document), zip => Put(zip, "word/vbaProject.bin", "not executable"));
        using var documentInput = new MemoryStream(disguisedDocument); using var templateOutput = new MemoryStream(); templateOutput.WriteByte(42);
        Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(documentInput, templateOutput, DocumentFormat.Docx, DocumentFormat.Dotx,
            new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, templateOutput.ToArray());
        Assert.Equal(FormatDetectionStatus.UnsupportedMacro, Detect(Odf(true, active: true)).Status);
        byte[] inlineScript = Odf(true, inlineScript: true);
        Assert.Equal(FormatDetectionStatus.UnsupportedMacro, Detect(inlineScript).Status);
        Assert.Equal(FormatDetectionStatus.UnsupportedProtected, Detect(Odf(true, encrypted: true)).Status);
        byte[] zipProtected = Word(WordprocessingDocumentType.Template); zipProtected[6] |= 1;
        Assert.Equal(FormatDetectionStatus.UnsupportedProtected, Detect(zipProtected).Status);
        byte[] laterProtected = MarkLaterZipEntryEncrypted(Word(WordprocessingDocumentType.Template));
        Assert.Equal(FormatDetectionStatus.UnsupportedProtected, Detect(laterProtected).Status);
        Assert.Equal(FormatDetectionStatus.UnsupportedProtected, Detect(MarkLaterZipEntryEncrypted(Word(WordprocessingDocumentType.Template), 0x40)).Status);
        using var protectedInput = new MemoryStream(laterProtected); using var protectedOutput = new MemoryStream(); protectedOutput.WriteByte(42);
        Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(protectedInput, protectedOutput, DocumentFormat.Dotx, DocumentFormat.Docx,
            new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, protectedOutput.ToArray());
        using var compound = new MemoryStream();
        using (RootStorage root = RootStorage.Create(compound, OpenMcdf.Version.V3, StorageModeFlags.LeaveOpen))
        {
            using CfbStream encryption = root.CreateStream("EncryptionInfo"); encryption.WriteByte(1);
        }
        Assert.Equal(FormatDetectionStatus.UnsupportedProtected, Detect(compound.ToArray()).Status);
    }

    private sealed class NonSeekable : Stream
    {
        private readonly MemoryStream input;
        internal NonSeekable(byte[] bytes) { input = new MemoryStream(bytes, false); }
        internal long BytesRead => input.Position;
        public override bool CanRead => input.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => input.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) input.Dispose(); base.Dispose(disposing); }
    }
}
