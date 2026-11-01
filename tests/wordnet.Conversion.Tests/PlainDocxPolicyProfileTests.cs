using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using WordNet.Internal;
using Xunit;

namespace WordNet.Tests;

public sealed class PlainDocxPolicyProfileTests
{
    [Theory]
    [InlineData("A")]
    [InlineData("Invoice123")]
    [InlineData("abcdefghijklmnopqrstuvwxyz12345678901234")]
    public void AdmitsOnlyTheNarrowOneRunProfileAndLeavesTheStreamOpen(string text)
    {
        using var source = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(TestDocuments.TextParagraph(text))));
        PlainDocxPolicyProfile.Validate(source, TestContext.Current.CancellationToken);
        Assert.True(source.CanRead);
    }

    [Fact]
    public void StructuredContentFormattingAndUnqualifiedTextReject()
    {
        string[] bodies =
        {
            "<w:p><w:pPr/><w:r><w:t>Alpha</w:t></w:r></w:p>",
            "<w:p><w:r><w:rPr/><w:t>Alpha</w:t></w:r></w:p>",
            "<w:p><w:r><w:t>Alpha</w:t></w:r></w:p><w:p><w:r><w:t>Bravo</w:t></w:r></w:p>",
            "<w:p><w:fldSimple w:instr='MERGEFIELD Name'><w:r><w:t>Alpha</w:t></w:r></w:fldSimple></w:p>",
            "<w:p><w:bookmarkStart w:id='1' w:name='Target'/><w:r><w:t>Alpha</w:t></w:r><w:bookmarkEnd w:id='1'/></w:p>",
            "<w:tbl><w:tr><w:tc><w:p><w:r><w:t>Alpha</w:t></w:r></w:p></w:tc></w:tr></w:tbl>",
            "<w:p><w:r><w:t xml:space='preserve'> Alpha</w:t></w:r></w:p>",
            "<w:p><w:r><w:t>Alpha Bravo</w:t></w:r></w:p>",
            "<w:p><w:r><w:t>Renée</w:t></w:r></w:p>",
            "<w:p><w:r><w:t>" + new string('A', 41) + "</w:t></w:r></w:p>",
            "<w:p><w:r><w:t/></w:r></w:p>",
            "<w:p><w:r><w:t>Alpha</w:t></w:r></w:p><w:sectPr/>",
        };
        Assert.All(bodies, body =>
        {
            using var source = new MemoryStream(TestDocuments.Docx(TestDocuments.Body(body)));
            NotSupportedException error = Assert.Throws<NotSupportedException>(() => PlainDocxPolicyProfile.Validate(source, TestContext.Current.CancellationToken));
            Assert.StartsWith("Plan.SourceProfile:", error.Message, StringComparison.Ordinal); Assert.True(source.CanRead);
        });
    }

    [Fact]
    public void ExtraPartsRelationshipsAndActiveXmlDoNotEnterTheProfile()
    {
        string body = TestDocuments.Body(TestDocuments.TextParagraph("Alpha"));
        string styles = "<w:styles xmlns:w='" + TestDocuments.W + "'/>";
        string link = TestDocuments.LinkRelationship("https://example.org");
        byte[][] packages =
        {
            TestDocuments.Docx(body, styles: styles),
            TestDocuments.Docx(body, relationships: link),
            TestDocuments.Docx(body, rootRelationships: "<Relationships xmlns='" + TestDocuments.PackageR + "'><Relationship Id='x' Type='" + TestDocuments.R + "/officeDocument' Target='https://example.org' TargetMode='External'/></Relationships>"),
            TestDocuments.Docx(body, rootRelationships: "<Relationships xmlns='" + TestDocuments.PackageR + "'><Relationship Id='9invalid' Type='" + TestDocuments.R + "/officeDocument' Target='word/document.xml'/></Relationships>"),
            TestDocuments.Docx(body, extra: new KeyValuePair<string, string>("word/other.xml", "<extra/>")),
        };
        Assert.All(packages, bytes =>
        {
            using var source = new MemoryStream(bytes);
            Assert.Throws<NotSupportedException>(() => PlainDocxPolicyProfile.Validate(source, TestContext.Current.CancellationToken));
            Assert.True(source.CanRead);
        });
        string dtd = "<!DOCTYPE x [<!ENTITY probe 'Alpha'>]>" + TestDocuments.Body("<w:p><w:r><w:t>&probe;</w:t></w:r></w:p>");
        using var prohibited = new MemoryStream(TestDocuments.Docx(dtd));
        Assert.Throws<InvalidDataException>(() => PlainDocxPolicyProfile.Validate(prohibited, TestContext.Current.CancellationToken));
        Assert.True(prohibited.CanRead);
    }

    [Fact]
    public void DuplicatePartsOversizedBuffersAndCancellationFailBeforeAdmission()
    {
        byte[] initial = TestDocuments.Docx(TestDocuments.Body(TestDocuments.TextParagraph("Alpha")));
        using var copy = new MemoryStream(); copy.Write(initial, 0, initial.Length);
        using (var zip = new ZipArchive(copy, ZipArchiveMode.Update, leaveOpen: true))
        using (var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open())) writer.Write("<duplicate/>");
        copy.Position = 0;
        Assert.Throws<InvalidDataException>(() => PlainDocxPolicyProfile.Validate(copy, TestContext.Current.CancellationToken));
        Assert.True(copy.CanRead);
        using var oversized = new MemoryStream(new byte[16 * 1024 * 1024 + 1]);
        InvalidDataException limit = Assert.Throws<InvalidDataException>(() => PlainDocxPolicyProfile.Validate(oversized, TestContext.Current.CancellationToken));
        Assert.StartsWith("Limit.CompatibilityPlan:", limit.Message, StringComparison.Ordinal); Assert.True(oversized.CanRead);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        using var source = new MemoryStream(initial);
        Assert.Throws<OperationCanceledException>(() => PlainDocxPolicyProfile.Validate(source, cancelled.Token));
        Assert.True(source.CanRead); Assert.Equal(0, source.Position);
        source.Position = 1;
        Assert.Throws<ArgumentException>(() => PlainDocxPolicyProfile.Validate(source, TestContext.Current.CancellationToken));
    }
}
