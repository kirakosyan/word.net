using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Model;
using Xunit;

namespace WordNet.Tests;

public sealed partial class OdtTests
{
    private static readonly XNamespace Ot = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private static readonly XNamespace Oo = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private static readonly XNamespace Os = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    private static readonly XNamespace Of = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
    private static readonly string[] ListParagraphTexts = { "Intro", "One", "details", "Two" };
    private static byte[] EditOdt(string name, Action<XDocument> edit, bool stripStyles = false)
    {
        byte[] fixture = Fixture(name); using var stream = new MemoryStream(); stream.Write(fixture, 0, fixture.Length);
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, true))
        {
            ZipArchiveEntry entry = zip.GetEntry("content.xml")!; XDocument content;
            using (Stream input = entry.Open()) content = XDocument.Load(input, LoadOptions.PreserveWhitespace);
            edit(content); entry.Delete();
            if (stripStyles) content.Descendants().Where(e => e.Name == Os + "style" || e.Name == Os + "default-style").Remove();
            using (var writer = new StreamWriter(zip.CreateEntry("content.xml").Open(), new UTF8Encoding(false))) writer.Write(content.ToString(SaveOptions.DisableFormatting));
            if (stripStyles)
            {
                ZipArchiveEntry styleEntry = zip.GetEntry("styles.xml")!; XDocument styles;
                using (Stream input = styleEntry.Open()) styles = XDocument.Load(input);
                styles.Descendants().Where(e => e.Name == Os + "style" || e.Name == Os + "default-style").Remove(); styleEntry.Delete();
                using var writer = new StreamWriter(zip.CreateEntry("styles.xml").Open(), new UTF8Encoding(false)); writer.Write(styles.ToString(SaveOptions.DisableFormatting));
            }
        }
        return stream.ToArray();
    }
    private static DocumentModel OdtBody(string body, ConversionContext context)
    {
        byte[] bytes = EditOdt("text", content => content.Descendants(Oo + "text").Single().ReplaceNodes(XElement.Parse("<root xmlns:text='" + Ot + "' xmlns:office='" + Oo + "' xmlns:dc='http://purl.org/dc/elements/1.1/'>" + body + "</root>").Nodes()), stripStyles: true);
        using var stream = new MemoryStream(bytes); return OdtFormat.Read(stream, context);
    }
    [Fact]
    public void NonFlowHistoryAnnotationsAndNotesNeverBecomeBodyText()
    {
        ConversionContext context = Context();
        DocumentModel model = OdtBody("<text:tracked-changes><text:changed-region><text:deletion><text:p>Deleted clause</text:p></text:deletion></text:changed-region></text:tracked-changes>"
            + "<office:forms><text:p>Hidden form metadata</text:p></office:forms><text:p>Hello<office:annotation><dc:creator>Jane</dc:creator><dc:date>2026-05-01T10:00:00</dc:date><text:p>Check this</text:p></office:annotation>"
            + "<text:note><text:note-citation>1</text:note-citation><text:note-body><text:p>Note body</text:p></text:note-body></text:note> world</text:p>", context);
        Assert.Equal("Hello world", Assert.Single(model.BodyParagraphs).Text);
        Assert.Contains(context.Result(0).Diagnostics, d => d.Code == "Odt.RevisionLoss");
        Assert.Contains(context.Result(0).Diagnostics, d => d.Code == "Odt.AnnotationLoss");
        Assert.Contains(context.Result(0).Diagnostics, d => d.Code == "Odt.NotesLoss");
    }
    [Theory]
    [InlineData("\n    Hello\n    world\n  ", "Hello world")]
    [InlineData("  Hello<text:span> \n world </text:span>  again  ", "Hello world again")]
    [InlineData("<text:s text:c='2'/>Hello<text:tab/>world<text:line-break/>next<text:s/>", "  Hello\tworld\nnext ")]
    [InlineData("A&#160;  B", "A\u00a0 B")]
    public void OdfWhitespaceCollapsesAcrossInlineBoundariesAndKeepsExplicitWhitespace(string content, string expected)
    { Assert.Equal(expected, Assert.Single(OdtBody("<text:p>" + content + "</text:p>", Context()).BodyParagraphs).Text); }
    [Fact]
    public void EmptyDeclarationsAndSoftPageBreaksAreStrictNoOps()
    {
        var context = new ConversionContext(new ConversionOptions(), TestContext.Current.CancellationToken);
        DocumentModel model = OdtBody("<text:sequence-decls><text:sequence-decl text:name='Figure'/></text:sequence-decls><text:soft-page-break/><text:p>A<text:soft-page-break/>B</text:p>", context);
        Assert.Equal("AB", Assert.Single(model.BodyParagraphs).Text);
        Assert.DoesNotContain(context.Result(0).Diagnostics, d => d.Severity == DiagnosticSeverity.Warning);
    }
    [Fact]
    public void ListHeadersAndContinuationParagraphsNeverConsumeNumbers()
    {
        byte[] bytes = EditOdt("list", content =>
        {
            XElement list = content.Descendants(Ot + "list").First();
            list.ReplaceNodes(new XElement(Ot + "list-header", new XElement(Ot + "p", "Intro")),
                new XElement(Ot + "list-item", new XAttribute(Ot + "start-value", "3"), new XElement(Ot + "p", "One"), new XElement(Ot + "p", "details")),
                new XElement(Ot + "list-item", new XElement(Ot + "p", "Two")));
        });
        using var source = new MemoryStream(bytes); ParagraphBlock[] paragraphs = OdtFormat.Read(source, Context()).BodyParagraphs.ToArray();
        Assert.Equal(ListParagraphTexts, paragraphs.Select(p => p.Text));
        Assert.Null(paragraphs[0].List); Assert.NotNull(paragraphs[1].List); Assert.Null(paragraphs[2].List); Assert.NotNull(paragraphs[3].List);
        var counter = new ListCounter(); Assert.Equal("3.", counter.Label(paragraphs[1].List!)); Assert.Equal("4.", counter.Label(paragraphs[3].List!));
    }
    [Fact]
    public void StyledMasterStoriesAndCellListsUseCorrectOdfScopesAndSectionFlow()
    {
        using var source = new MemoryStream(Fixture("masters")); DocumentModel model = OdtFormat.Read(source, Context());
        ParagraphBlock header = (ParagraphBlock)model.Blocks[0].Section!.Stories["header-default"][0]; header.Alignment = ParagraphAlignment.Right;
        header.Runs.Clear(); header.Runs.Add(new TextRun("First header", bold: true, fontSize: 18, color: "2468AC"));
        ((ParagraphBlock)model.Blocks[1]).List = new ListItem { Id = "section-list", Ordered = true };
        var table = new TableBlock { Section = model.Blocks[1].Section }; table.ColumnWidths.Add(120);
        var cell = new TableCell();
        foreach (string text in new[] { "Cell one", "Cell two" })
        { var p = new ParagraphBlock { List = new ListItem { Id = "cell-list", Ordered = true } }; p.Runs.Add(new TextRun(text)); cell.Paragraphs.Add(p); }
        table.Rows.Add(new List<TableCell> { cell }); model.Blocks.Add(table);
        using var output = new MemoryStream(); OdtFormat.Write(model, output, Context());
        output.Position = 0; DocumentModel restored = OdtFormat.Read(output, Context());
        Assert.Equal(500, restored.Blocks[1].Section!.Width); Assert.All(Assert.Single(restored.Tables).Rows[0][0].Paragraphs, p => Assert.NotNull(p.List));
        output.Position = 0; using (var zip = new ZipArchive(output, ZipArchiveMode.Read, true))
        {
            using Stream part = zip.GetEntry("styles.xml")!.Open(); XDocument styles = XDocument.Load(part);
            XElement p = styles.Descendants(Os + "header").First().Element(Ot + "p")!;
            XElement paragraphStyle = styles.Descendants(Os + "style").Single(s => (string?)s.Attribute(Os + "name") == (string?)p.Attribute(Ot + "style-name"));
            Assert.Equal("right", (string?)paragraphStyle.Element(Os + "paragraph-properties")!.Attribute(Of + "text-align"));
            string name = (string)p.Element(Ot + "span")!.Attribute(Ot + "style-name")!;
            Assert.Equal("bold", (string?)styles.Descendants(Os + "style").Single(s => (string?)s.Attribute(Os + "name") == name).Element(Os + "text-properties")!.Attribute(Of + "font-weight"));
        }
        string? evidence = Environment.GetEnvironmentVariable("WORDNET_ODT_EVIDENCE");
        if (evidence != null) { Directory.CreateDirectory(evidence); File.WriteAllBytes(Path.Combine(evidence, "review-exchange.odt"), output.ToArray()); }
    }
    [Fact]
    public void EpubMimetypeIsActuallyStoredOnEveryTargetFramework()
    {
        var model = new DocumentModel(); var p = new ParagraphBlock(); p.Runs.Add(new TextRun("EPUB content")); model.Blocks.Add(p);
        using var output = new MemoryStream(); EpubFormat.Write(model, output, Context()); byte[] bytes = output.ToArray();
        Assert.Equal(0x04034b50u, BitConverter.ToUInt32(bytes, 0)); Assert.Equal(0, BitConverter.ToUInt16(bytes, 8)); Assert.Equal(0, BitConverter.ToUInt16(bytes, 28));
        Assert.Equal("mimetypeapplication/epub+zip", Encoding.ASCII.GetString(bytes, 30, 8 + "application/epub+zip".Length));
        output.Position = 0; Assert.Equal("EPUB content", Assert.Single(EpubFormat.Read(output, Context()).BodyParagraphs).Text);
    }
}
