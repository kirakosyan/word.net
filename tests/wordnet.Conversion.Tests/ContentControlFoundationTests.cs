using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;
using static WordNet.Tests.ControlTestDocuments;

namespace WordNet.Tests;

public sealed class ContentControlFoundationTests
{
    private static readonly int[] ExpectedIds = { 2, 1, 3 };
    [Fact]
    public void CheckboxPreservesIdentityStateGlyphMetadataAndSelectableDisplay()
    {
        byte[] source = Package(Fixture("checkbox.xml")); Validate(source);
        WordDocument document = Load(source); DocumentContentControl handle = Assert.Single(document.ContentControls);
        Assert.Equal(42, handle.Id); Assert.Equal(ContentControlKind.Checkbox, handle.Kind); Assert.False(handle.IsChecked); Assert.Equal("merge:Ignored", handle.Tag); Assert.Equal("Approval", handle.Alias);
        Assert.Equal("-", handle.DisplayText); document.SetControlChecked(handle, true, TestContext.Current.CancellationToken);
        Assert.False(handle.IsChecked); Assert.Equal("-", handle.DisplayText); Assert.True(Assert.Single(document.ContentControls).IsChecked);
        byte[] word = Save(document); Validate(word);
        XElement wrapper = Assert.Single(Xml(word, "word/document.xml").Descendants(W + "sdt"));
        Assert.Equal("42", (string?)wrapper.Element(W + "sdtPr")!.Element(W + "id")!.Attribute(W + "val"));
        Assert.Equal("1", (string?)wrapper.Descendants(W14 + "checked").Single().Attribute(W14 + "val"));
        Assert.Equal("0058", (string?)wrapper.Descendants(W14 + "checkedState").Single().Attribute(W14 + "val"));
        Assert.Equal("Noto Sans", (string?)wrapper.Descendants(W14 + "checkedState").Single().Attribute(W14 + "font"));
        Assert.Equal("X", wrapper.Element(W + "sdtContent")!.Value);
        Assert.Equal("BeforeXafter", Compact(Pdf(document))); Assert.Equal("BeforeXafter", Compact(Xps(document)));
        using var pdf = PdfDocument.Open(Save(document, DocumentFormat.Pdf));
        var glyph = Assert.Single(pdf.GetPage(1).Letters, l => l.Value == "X"); Assert.Equal(12, glyph.FontSize, 3);
        Assert.True(glyph.StartBaseLine.X > pdf.GetPage(1).Letters[0].StartBaseLine.X);
        Assert.True(glyph.StartBaseLine.X < pdf.GetPage(1).Letters[pdf.GetPage(1).Letters.Count - 1].StartBaseLine.X);
        Assert.True(Assert.Single(Load(word).ContentControls).IsChecked);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingGlyphUsesAsciiOnlyInFixedDisplay(bool state)
    {
        string control = Control(1, Checkbox(state ? "1" : "0", "E000", "Missing Authored Font"), state ? "\ue000" : "-", block: true);
        WordDocument document = LoadBody(control); byte[] before = Save(document);
        using var output = new MemoryStream(); ConversionResult result = document.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "Control.GlyphFallback" && d.Severity == DiagnosticSeverity.Info);
        Assert.Contains(result.Diagnostics, d => d.Code == "Control.StaticDisplay");
        using var pdf = PdfDocument.Open(output.ToArray()); Assert.Equal(state ? "[x]" : "[]", Compact(pdf.GetPage(1).Text));
        Assert.Equal(state ? "[x]" : "[]", Compact(Xps(document)));
        Assert.Equal(Xml(before, "word/document.xml").ToString(), Xml(Save(document), "word/document.xml").ToString());
        Assert.Equal("Missing Authored Font", (string?)Xml(Save(document), "word/document.xml").Descendants(W14 + "checkedState").Single().Attribute(W14 + "font"));
    }
    [Fact]
    public void OmittedCheckboxStateAndGlyphDeclarationsKeepTheirAbsence()
    {
        WordDocument document = LoadBody("<w:p>" + Control(-7, "<w14:checkbox/>", "\u2610") + "</w:p>");
        Assert.False(Assert.Single(document.ContentControls).IsChecked);
        byte[] word = Save(document); Validate(word); Assert.Empty(Xml(word, "word/document.xml").Descendants(W14 + "checked"));
        Assert.Equal(-7, Assert.Single(Load(word).ContentControls).Id); Assert.Equal("[]", Compact(Pdf(document)));
    }
    [Fact]
    public void MissingIdsAreAssignedAfterReservingAllExplicitIdsAndTagsNeedNotBeUnique()
    {
        string metadata = "<w:tag w:val='same'/><w:alias w:val='same'/>";
        WordDocument document = LoadBody("<w:p>" + Control(null, Checkbox(), "-", metadata) + Control(1, Checkbox(), "-", metadata) + Control(null, Checkbox(), "-", metadata) + "</w:p>");
        Assert.Equal(ExpectedIds, document.ContentControls.Select(c => c.Id));
        Assert.Equal(3, document.ContentControls.Count(c => c.Tag == "same")); Assert.Contains(document.Diagnostics, d => d.Code == "Control.IdAssigned");
        byte[] word = Save(document); Validate(word); Assert.Equal(ExpectedIds, Load(word).ContentControls.Select(c => c.Id));
    }
    [Theory]
    [InlineData("unlocked", true)]
    [InlineData("sdtLocked", true)]
    [InlineData("contentLocked", false)]
    [InlineData("sdtContentLocked", false)]
    public void LocksEnforceEditingWithoutChangingTheSource(string lockValue, bool allowed)
    {
        WordDocument document = LoadBody(Control(1, Checkbox(), "-", "<w:lock w:val='" + lockValue + "'/>", block: true));
        DocumentContentControl handle = Assert.Single(document.ContentControls); string before = Xml(Save(document), "word/document.xml").ToString();
        if (allowed) { document.SetControlChecked(handle, true, TestContext.Current.CancellationToken); Assert.True(Assert.Single(document.ContentControls).IsChecked); }
        else
        {
            Assert.Throws<InvalidOperationException>(() => document.SetControlChecked(handle, true, TestContext.Current.CancellationToken));
            Assert.Equal(before, Xml(Save(document), "word/document.xml").ToString()); Assert.False(Assert.Single(document.ContentControls).IsChecked);
        }
        Validate(Save(document));
        WordDocument view = document.CreateControlDisplayView(out ConversionResult result, TestContext.Current.CancellationToken);
        Assert.Empty(view.ContentControls); Assert.Contains(result.Diagnostics, d => d.Code == "Control.OutputLoss"); Assert.Single(document.ContentControls);
    }
    [Fact]
    public void ForeignStaleAndWrongKindHandlesFailAndMetadataCommitsInvalidateSnapshots()
    {
        WordDocument document = Load(Package(Fixture("checkbox.xml"))); var original = Assert.Single(document.ContentControls);
        WordDocument other = Load(Package(Fixture("checkbox.xml")));
        Assert.Throws<ArgumentException>(() => other.SetControlChecked(original, true, TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(() => document.SetControlSelection(original, "a", TestContext.Current.CancellationToken));
        document.SetControlChecked(original, true, TestContext.Current.CancellationToken); var live = Assert.Single(document.ContentControls);
        Assert.Throws<ArgumentException>(() => document.SetControlChecked(original, false, TestContext.Current.CancellationToken));
        _ = Save(document); document.SetControlChecked(live, false, TestContext.Current.CancellationToken);
        var metadataHandle = Assert.Single(document.ContentControls); document.Title = "Title";
        Assert.Throws<ArgumentException>(() => document.SetControlChecked(metadataHandle, true, TestContext.Current.CancellationToken));
        var noOpHandle = Assert.Single(document.ContentControls); Assert.False(document.RemoveCustomProperty("Absent", TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(() => document.SetControlChecked(noOpHandle, true, TestContext.Current.CancellationToken));
        var canceled = Assert.Single(document.ContentControls);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => document.SetControlChecked(canceled, true, cancellation.Token));
        document.SetControlChecked(canceled, true, TestContext.Current.CancellationToken);
        Assert.False(original.IsChecked); Assert.Equal("-", original.DisplayText);
    }
    [Fact]
    public void TypedTagsNeverExecuteAndOrdinaryOwnerRewritesRejectBeforeCallerGetters()
    {
        WordDocument document = Load(Package(Fixture("checkbox.xml"))); var data = new GetterProbe(); var handle = Assert.Single(document.ContentControls);
        Assert.Throws<NotSupportedException>(() => document.Merge(data, cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(0, data.Reads);
        Assert.Throws<NotSupportedException>(() => document.MergeJson("invalid json", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Throws<NotSupportedException>(() => document.ReplaceParagraph(0, new[] { new DocumentText("Lost") }, TestContext.Current.CancellationToken));
        Assert.Throws<NotSupportedException>(() => document.AddParagraph("Lost", cancellationToken: TestContext.Current.CancellationToken));
        WordDocument destination = WordDocument.Create(); destination.AddParagraph("Keep", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Throws<NotSupportedException>(() => destination.Append(document, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("Keep", Assert.Single(destination.Paragraphs).Text);
        Assert.Throws<NotSupportedException>(() => document.Append(destination, cancellationToken: TestContext.Current.CancellationToken));
        document.SetControlChecked(handle, true, TestContext.Current.CancellationToken);
        WordDocument view = document.CreateControlDisplayView(out _, TestContext.Current.CancellationToken); view.Merge(data, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("BeforeXafter", Compact(Pdf(view))); Assert.Equal("merge:Ignored", Assert.Single(document.ContentControls).Tag);
        Assert.Empty(Xml(Save(view), "word/document.xml").Descendants(W + "sdt"));
    }
    private sealed class GetterProbe { public int Reads { get; private set; } public string Ignored { get { Reads++; return "EXECUTED"; } } }
    [Theory]
    [InlineData(DocumentFormat.Doc)]
    [InlineData(DocumentFormat.Html)]
    [InlineData(DocumentFormat.Text)]
    public void OtherDestinationsRejectStrictBeforeCopyAndDiscloseExplicitFlattening(DocumentFormat format)
    {
        WordDocument document = Load(Package(Fixture("checkbox.xml"))); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<NotSupportedException>(() => document.Save(output, format, cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(new byte[] { 42 }, output.ToArray());
        using var best = new MemoryStream(); ConversionResult result = document.Save(best, format, new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "Control.OutputLoss" && d.Severity == DiagnosticSeverity.Warning);
        if (format == DocumentFormat.Text) Assert.Equal("Before-after", Compact(System.Text.Encoding.UTF8.GetString(best.ToArray())));
        Assert.Single(document.ContentControls);
    }
    [Theory]
    [InlineData("contentLocked")]
    [InlineData("sdtContentLocked")]
    public void LockedPlaceholderResetCannotRewriteContent(string lockValue)
    {
        string properties = "<w:showingPlcHdr/><w:lock w:val='" + lockValue + "'/>";
        WordDocument document = LoadBody("<w:p>" + Control(1, "<w:text/>", "Prompt", properties) + "</w:p>");
        var handle = Assert.Single(document.ContentControls); Assert.True(handle.ShowingPlaceholder);
        Assert.Throws<InvalidOperationException>(() => document.ShowControlPlaceholder(handle, TestContext.Current.CancellationToken)); Assert.Equal("Prompt", Assert.Single(document.ContentControls).DisplayText);
    }
    [Fact]
    public void ReadAndWriteLeaveCallerStreamsOpenAndPreCopyFailureUntouched()
    {
        using var input = new MemoryStream(Package(Fixture("checkbox.xml"))); WordDocument document = WordDocument.LoadWithControls(input, out _, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(input.CanRead); Assert.Equal(input.Length, input.Position);
        using var output = new MemoryStream(); document.Save(output, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken); Assert.True(output.CanWrite); Validate(output.ToArray());
        using var small = new MemoryStream(); small.WriteByte(7);
        Assert.Throws<InvalidDataException>(() => document.Save(small, DocumentFormat.Docx, new ConversionOptions(maximumOutputBytes: 8), TestContext.Current.CancellationToken)); Assert.Equal(new byte[] { 7 }, small.ToArray());
    }
    [Fact]
    public void OrdinaryCachedConversionRemainsIndependentOfTypedTemplateTags()
    {
        using var source = new MemoryStream(Package(Fixture("checkbox.xml"))); using var pdf = new MemoryStream();
        new WordConverter().Convert(source, pdf, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(), TestContext.Current.CancellationToken);
        using var read = PdfDocument.Open(pdf.ToArray()); Assert.Equal("Before-after", Compact(read.GetPage(1).Text));
    }
}
