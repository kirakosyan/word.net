using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace WordNet.Tests;

public sealed partial class ReviewWorkflowTests
{
    private static readonly ReviewDecision[] ComparisonDecisions = { ReviewDecision.Accept, ReviewDecision.Reject };
    [Fact]
    public void IgnoredRunFormattingRetainsOriginalAppearanceAndDoesNotChangeInputs()
    {
        WordDocument original = Load(Input("<w:p><w:r><w:rPr><w:i/><w:color w:val='0000FF'/><w:sz w:val='20'/></w:rPr><w:t>Same</w:t></w:r></w:p>"));
        WordDocument revised = Load(Input("<w:p><w:r><w:rPr><w:b/><w:color w:val='FF0000'/><w:sz w:val='28'/></w:rPr><w:t>Same</w:t></w:r></w:p>"));
        byte[] before = Save(original), revisedBefore = Save(revised);
        WordDocument tracked = DocumentComparison.Compare(original, revised, new ComparisonOptions("Reviewer", Time), Policy(), TestContext.Current.CancellationToken);
        Assert.Single(tracked.Revisions);
        WordDocument ignored = DocumentComparison.Compare(original, revised, new ComparisonOptions("Reviewer", Time) { CompareRunFormatting = false }, Policy(), TestContext.Current.CancellationToken);
        byte[] result = Save(ignored); Schema(result); Assert.Empty(ignored.Revisions);
        var properties = Part(result).Descendants(W + "r").Single(r => r.Element(W + "t")?.Value == "Same").Element(W + "rPr")!;
        Assert.NotNull(properties.Element(W + "i")); Assert.Equal("0000FF", (string?)properties.Element(W + "color")!.Attribute(W + "val"));
        Assert.Equal("20", (string?)properties.Element(W + "sz")!.Attribute(W + "val"));
        Assert.Equal(before, Save(original)); Assert.Equal(revisedBefore, Save(revised));
    }

    [Fact]
    public void IgnoredFormattingKeepsExplicitResetAgainstInheritedBold()
    {
        string styles = "<w:styles xmlns:w='" + W + "'><w:style w:type='paragraph' w:styleId='Emphasis'><w:name w:val='Emphasis'/><w:rPr><w:b/></w:rPr></w:style></w:styles>";
        byte[] Fixture(bool reset)
        {
            string text = "<w:p><w:pPr><w:pStyle w:val='Emphasis'/></w:pPr><w:r>" + (reset ? "<w:rPr><w:b w:val='0'/></w:rPr>" : "") + "<w:t>Same</w:t></w:r></w:p>";
            return TestDocuments.Docx(TestDocuments.Body(text), "<Relationship Id='styles' Type='" + TestDocuments.R + "/styles' Target='styles.xml'/>",
                extra: new KeyValuePair<string, string>("word/styles.xml", styles));
        }
        WordDocument original = Load(Fixture(true)), revised = Load(Fixture(false));
        WordDocument result = DocumentComparison.Compare(original, revised, new ComparisonOptions("Reviewer", Time) { CompareRunFormatting = false }, Policy(), TestContext.Current.CancellationToken);
        byte[] bytes = Save(result); Schema(bytes); Assert.Empty(result.Revisions);
        Assert.False(Load(bytes).Paragraphs[0].Runs[0].Formatting.Bold);
    }

    [Fact]
    public void IgnoredFormattingStillTracksChangedTextAndPreservesOriginalUnchangedText()
    {
        WordDocument original = Load(Input("<w:p><w:r><w:t>Alpha old</w:t></w:r></w:p>"));
        WordDocument revised = Load(Input("<w:p><w:r><w:rPr><w:b/></w:rPr><w:t>Alpha new</w:t></w:r></w:p>"));
        WordDocument result = DocumentComparison.Compare(original, revised, new ComparisonOptions("Reviewer", Time) { CompareRunFormatting = false }, Policy(), TestContext.Current.CancellationToken);
        byte[] bytes = Save(result); Schema(bytes); Assert.Equal(2, result.Revisions.Count);
        Assert.Empty(Part(bytes).Descendants(W + "rPrChange"));
        Assert.DoesNotContain(Part(bytes).Descendants(W + "r").Where(r => r.Element(W + "t")?.Value == "Alpha "), r => r.Element(W + "rPr")?.Element(W + "b") != null);
        Assert.Equal("Alpha new", result.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken).Paragraphs[0].Text);
        Assert.Equal("Alpha old", result.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken).Paragraphs[0].Text);
    }

    [Fact]
    public void IgnoredRunFormattingDoesNotAdmitUnsupportedProperties()
    {
        WordDocument original = Load(Input(P("Same")));
        WordDocument revised = Load(Input("<w:p><w:r><w:rPr><w:u w:val='single'/></w:rPr><w:t>Same</w:t></w:r></w:p>"));
        byte[] before = Save(original);
        Assert.Throws<NotSupportedException>(() => DocumentComparison.Compare(original, revised,
            new ComparisonOptions("Reviewer", Time) { CompareRunFormatting = false }, Policy(), TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(original));
    }

    [Fact]
    public void IgnoredFormattingCanCoexistWithStructuralInsertion()
    {
        WordDocument original = Load(Input(P("First") + P("Last")));
        WordDocument revised = Load(Input("<w:p><w:r><w:rPr><w:b/></w:rPr><w:t>First</w:t></w:r></w:p>" + P("Added") + P("Last")));
        WordDocument result = DocumentComparison.Compare(original, revised, new ComparisonOptions("Reviewer", Time) { CompareRunFormatting = false }, Policy(), TestContext.Current.CancellationToken);
        byte[] bytes = Save(result); Schema(bytes);
        Assert.Empty(Part(bytes).Descendants(W + "rPrChange"));
        Assert.Equal("First|Added|Last", string.Join("|", result.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken).Paragraphs.Select(p => p.Text)));
        Assert.Equal("First|Last", string.Join("|", result.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken).Paragraphs.Select(p => p.Text)));
        Assert.DoesNotContain(Part(bytes).Descendants(W + "r").Where(r => r.Element(W + "t")?.Value == "First"), r => r.Element(W + "rPr")?.Element(W + "b") != null);
    }

    [Fact]
    public void IgnoredCellFormattingCanCoexistWithRowInsertion()
    {
        string Row(string run) => "<w:tr><w:tc><w:p>" + run + "</w:p></w:tc></w:tr>";
        string first = "<w:r><w:t>First</w:t></w:r>", boldFirst = "<w:r><w:rPr><w:b/></w:rPr><w:t>First</w:t></w:r>";
        string last = "<w:r><w:t>Last</w:t></w:r>", added = "<w:r><w:t>Added</w:t></w:r>";
        WordDocument original = Load(Input("<w:tbl>" + Row(first) + Row(last) + "</w:tbl>"));
        WordDocument revised = Load(Input("<w:tbl>" + Row(boldFirst) + Row(added) + Row(last) + "</w:tbl>"));
        WordDocument result = DocumentComparison.Compare(original, revised, new ComparisonOptions("Reviewer", Time) { CompareRunFormatting = false }, Policy(), TestContext.Current.CancellationToken);
        byte[] bytes = Save(result); Schema(bytes);
        Assert.Empty(Part(bytes).Descendants(W + "rPrChange"));
        Assert.Equal("First|Added|Last", string.Join("|", result.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken).Paragraphs.Select(p => p.Text)));
        Assert.Equal("First|Last", string.Join("|", result.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken).Paragraphs.Select(p => p.Text)));
        Assert.DoesNotContain(Part(bytes).Descendants(W + "r").Where(r => r.Element(W + "t")?.Value == "First"), r => r.Element(W + "rPr")?.Element(W + "b") != null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IgnoredSharedStoryTextKeepsTheOriginalThroughBodyEdits(bool structural)
    {
        string section = "<w:sectPr><w:headerReference w:type='default' r:id='header'/></w:sectPr>";
        byte[] Fixture(string body, string header) => TestDocuments.Docx(TestDocuments.Body(body + section),
            "<Relationship Id='header' Type='" + TestDocuments.R + "/header' Target='header.xml'/>",
            extra: new KeyValuePair<string, string>("word/header.xml", "<w:hdr xmlns:w='" + W + "'>" + P(header) + "</w:hdr>"));
        WordDocument original = Load(Fixture(P("First") + P("Last"), "Old header"));
        WordDocument revised = Load(Fixture(P("First") + (structural ? P("Inserted") : "") + P("New last"), "New header"));
        var options = new ComparisonOptions("Reviewer", Time) { CompareStoryText = false };
        WordDocument result = DocumentComparison.Compare(original, revised, options, Policy(), TestContext.Current.CancellationToken);
        byte[] reviewed = Save(result); Schema(reviewed);
        foreach (ReviewDecision decision in ComparisonDecisions)
        {
            WordDocument view = result.CreateReviewView(decision, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal("Old header", Part(Save(view), "word/header1.xml").Root!.Value);
        }
        Assert.Contains(result.Revisions, item => item.Text.StartsWith("New", StringComparison.Ordinal) || item.Text.StartsWith("Inserted", StringComparison.Ordinal));
    }

    [Fact]
    public void SelectedStoryTextCanIgnoreItsRunFormatting()
    {
        string section = "<w:sectPr><w:headerReference w:type='default' r:id='header'/></w:sectPr>";
        byte[] Fixture(string header) => TestDocuments.Docx(TestDocuments.Body(P("Body") + section),
            "<Relationship Id='header' Type='" + TestDocuments.R + "/header' Target='header.xml'/>",
            extra: new KeyValuePair<string, string>("word/header.xml", "<w:hdr xmlns:w='" + W + "'><w:p>" + header + "</w:p></w:hdr>"));
        WordDocument original = Load(Fixture("<w:r><w:t>Old header</w:t></w:r>"));
        WordDocument revised = Load(Fixture("<w:r><w:rPr><w:b/></w:rPr><w:t>New header</w:t></w:r>"));
        WordDocument result = DocumentComparison.Compare(original, revised, new ComparisonOptions("Reviewer", Time) { CompareRunFormatting = false }, Policy(), TestContext.Current.CancellationToken);
        byte[] bytes = Save(result); Schema(bytes); Assert.Empty(Part(bytes, "word/header1.xml").Descendants(W + "rPrChange"));
        Assert.Equal("New header", Part(Save(result.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken)), "word/header1.xml").Root!.Value);
        Assert.Equal("Old header", Part(Save(result.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken)), "word/header1.xml").Root!.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IgnoredFormattingNormalizationsUseTheCombinedWorkBudget(bool story)
    {
        WordDocument original, revised;
        if (story)
        {
            string section = "<w:sectPr><w:headerReference w:type='default' r:id='header'/></w:sectPr>";
            byte[] Fixture(string header) => TestDocuments.Docx(TestDocuments.Body(P("Body") + section),
                "<Relationship Id='header' Type='" + TestDocuments.R + "/header' Target='header.xml'/>",
                extra: new KeyValuePair<string, string>("word/header.xml", "<w:hdr xmlns:w='" + W + "'>" + P(header) + "</w:hdr>"));
            original = Load(Fixture("Old")); revised = Load(Fixture("New"));
        }
        else
        {
            original = Load(Input(P("First") + P("Last")));
            revised = Load(Input(P("First") + P("Inserted") + P("Last")));
        }
        byte[] oldBytes = Save(original), newBytes = Save(revised);
        var options = new ComparisonOptions("Reviewer", Time) { CompareRunFormatting = false, MaximumSteps = 1 };
        Assert.Throws<System.IO.InvalidDataException>(() => DocumentComparison.Compare(original, revised, options, Policy(), TestContext.Current.CancellationToken));
        Assert.Equal(oldBytes, Save(original)); Assert.Equal(newBytes, Save(revised));
    }
}
