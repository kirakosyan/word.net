using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed partial class ReviewWorkflowTests
{
    private const string OldParagraphProperties = "<w:pPr><w:keepNext w:val='0'/><w:spacing w:before='0' w:after='120'/><w:ind w:left='0' w:right='0' w:firstLine='0'/><w:jc w:val='left'/></w:pPr>";
    private const string NewParagraphProperties = "<w:pPr><w:keepNext/><w:spacing w:before='120' w:after='240'/><w:ind w:left='480' w:right='120' w:firstLine='240'/><w:jc w:val='center'/></w:pPr>";
    private static string ParagraphWith(string properties, string text) => "<w:p>" + properties + "<w:r><w:t>" + text + "</w:t></w:r></w:p>";

    [Fact]
    public void ParagraphFormattingHistoryRoundTripsAndBothDecisionsMatchIndependentGeometry()
    {
        WordDocument original = Load(Input(ParagraphWith(OldParagraphProperties, "Terms")));
        WordDocument revised = Load(Input(ParagraphWith(NewParagraphProperties, "Terms")));
        byte[] before = Save(original), after = Save(revised);
        WordDocument result = DocumentComparison.Compare(original, revised, new ComparisonOptions("Reviewer", Time), Policy(), TestContext.Current.CancellationToken);
        DocumentReviewItem revision = Assert.Single(result.Revisions); Assert.Equal("paragraph-format", revision.Structure); Assert.Equal("Terms", revision.Text);
        byte[] reviewed = Save(result); Schema(reviewed);
        var change = Part(reviewed).Descendants(W + "pPrChange").Single();
        Assert.Equal("Reviewer", (string?)change.Attribute(W + "author"));
        Assert.Equal("left", (string?)change.Element(W + "pPr")!.Element(W + "jc")!.Attribute(W + "val"));
        Assert.Equal("120", (string?)change.Element(W + "pPr")!.Element(W + "spacing")!.Attribute(W + "after"));
        WordDocument reloaded = Load(reviewed); Assert.Single(reloaded.Revisions);
        WordDocument accepted = reloaded.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken);
        WordDocument rejected = reloaded.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken);
        byte[] acceptedWord = Save(accepted), rejectedWord = Save(rejected); Schema(acceptedWord); Schema(rejectedWord);
        Assert.Equal("center", (string?)Part(acceptedWord).Descendants(W + "pPr").First().Element(W + "jc")!.Attribute(W + "val"));
        Assert.Equal("left", (string?)Part(rejectedWord).Descendants(W + "pPr").First().Element(W + "jc")!.Attribute(W + "val"));
        Assert.Empty(Part(acceptedWord).Descendants(W + "pPrChange")); Assert.Empty(Part(rejectedWord).Descendants(W + "pPrChange"));
        using PdfDocument oldPdf = PdfDocument.Open(Save(original, DocumentFormat.Pdf));
        using PdfDocument newPdf = PdfDocument.Open(Save(revised, DocumentFormat.Pdf));
        using PdfDocument rejectedPdf = PdfDocument.Open(Save(rejected, DocumentFormat.Pdf));
        using PdfDocument acceptedPdf = PdfDocument.Open(Save(accepted, DocumentFormat.Pdf));
        Assert.Equal(oldPdf.GetPage(1).Letters[0].BoundingBox.Left, rejectedPdf.GetPage(1).Letters[0].BoundingBox.Left, 3);
        Assert.Equal(newPdf.GetPage(1).Letters[0].BoundingBox.Left, acceptedPdf.GetPage(1).Letters[0].BoundingBox.Left, 3);
        Assert.Equal(oldPdf.GetPage(1).Letters[0].BoundingBox.Bottom, rejectedPdf.GetPage(1).Letters[0].BoundingBox.Bottom, 3);
        Assert.Equal(newPdf.GetPage(1).Letters[0].BoundingBox.Bottom, acceptedPdf.GetPage(1).Letters[0].BoundingBox.Bottom, 3);
        Assert.Equal(before, Save(original)); Assert.Equal(after, Save(revised));
    }

    [Fact]
    public void ParagraphAndTextChangesHaveIndependentAttributedDecisions()
    {
        WordDocument original = Load(Input(ParagraphWith(OldParagraphProperties, "Old")));
        WordDocument revised = Load(Input(ParagraphWith(NewParagraphProperties, "New")));
        WordDocument result = DocumentComparison.Compare(original, revised, new ComparisonOptions("Reviewer", Time), Policy(), TestContext.Current.CancellationToken);
        Assert.Equal(3, result.Revisions.Count); Assert.Single(result.Revisions, r => r.Structure == "paragraph-format");
        byte[] bytes = Save(result); Schema(bytes); WordDocument reload = Load(bytes);
        WordDocument accepted = reload.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken);
        WordDocument rejected = reload.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("New", accepted.Paragraphs[0].Text); Assert.Equal("Old", rejected.Paragraphs[0].Text);
        Assert.Equal("center", (string?)Part(Save(accepted)).Descendants(W + "jc").Single().Attribute(W + "val"));
        Assert.Equal("left", (string?)Part(Save(rejected)).Descendants(W + "jc").Single().Attribute(W + "val"));
        WordDocument selected = Load(bytes); DocumentReviewItem format = Assert.Single(selected.Revisions, r => r.Structure == "paragraph-format");
        selected.ApplyRevisions(new[] { format }, ReviewDecision.Reject, TestContext.Current.CancellationToken);
        Assert.Equal(2, selected.Revisions.Count); Assert.Empty(Part(Save(selected)).Descendants(W + "pPrChange"));
        Assert.Throws<ArgumentException>(() => selected.ApplyRevisions(new[] { format }, ReviewDecision.Accept, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("auto", "240", "exact", "300")]
    [InlineData("atLeast", "260", "auto", "360")]
    public void ParagraphLineSpacingAndHangingIndentPreservePriorAndCurrentValues(string oldRule, string oldLine, string newRule, string newLine)
    {
        string old = "<w:pPr><w:spacing w:line='" + oldLine + "' w:lineRule='" + oldRule + "'/><w:ind w:hanging='120'/></w:pPr>";
        string current = "<w:pPr><w:spacing w:line='" + newLine + "' w:lineRule='" + newRule + "'/><w:ind w:firstLine='240'/></w:pPr>";
        WordDocument result = DocumentComparison.Compare(Load(Input(ParagraphWith(old, "Spacing"))), Load(Input(ParagraphWith(current, "Spacing"))),
            new ComparisonOptions("Reviewer", Time), Policy(), TestContext.Current.CancellationToken);
        byte[] reviewed = Save(result); Schema(reviewed); WordDocument reload = Load(reviewed);
        byte[] rejected = Save(reload.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken));
        byte[] accepted = Save(reload.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken));
        var before = Part(rejected).Descendants(W + "pPr").First(); var after = Part(accepted).Descendants(W + "pPr").First();
        Assert.Equal(oldLine, (string?)before.Element(W + "spacing")!.Attribute(W + "line"));
        Assert.Equal(oldRule, (string?)before.Element(W + "spacing")!.Attribute(W + "lineRule"));
        Assert.Equal("120", (string?)before.Element(W + "ind")!.Attribute(W + "hanging"));
        Assert.Equal(newLine, (string?)after.Element(W + "spacing")!.Attribute(W + "line"));
        Assert.Equal(newRule, (string?)after.Element(W + "spacing")!.Attribute(W + "lineRule"));
        Assert.Equal("240", (string?)after.Element(W + "ind")!.Attribute(W + "firstLine"));
    }

    [Fact]
    public void ParagraphFormattingPreservesInheritedExplicitResets()
    {
        string styles = "<w:styles xmlns:w='" + W + "'><w:style w:type='paragraph' w:styleId='Emphasis'><w:name w:val='Emphasis'/><w:pPr><w:keepNext/><w:ind w:left='720'/><w:jc w:val='center'/></w:pPr></w:style></w:styles>";
        byte[] Fixture(bool reset)
        {
            string properties = "<w:pPr><w:pStyle w:val='Emphasis'/>" + (reset ? "<w:keepNext w:val='0'/><w:ind w:left='0'/><w:jc w:val='left'/>" : "") + "</w:pPr>";
            return TestDocuments.Docx(TestDocuments.Body(ParagraphWith(properties, "Inherited")), "<Relationship Id='styles' Type='" + TestDocuments.R + "/styles' Target='styles.xml'/>",
                extra: new KeyValuePair<string, string>("word/styles.xml", styles));
        }
        WordDocument original = Load(Fixture(true)), revised = Load(Fixture(false));
        WordDocument result = DocumentComparison.Compare(original, revised, new ComparisonOptions("Reviewer", Time), Policy(), TestContext.Current.CancellationToken);
        byte[] reviewed = Save(result); Schema(reviewed); WordDocument reloaded = Load(reviewed);
        byte[] rejected = Save(reloaded.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken)); Schema(rejected);
        var properties = Part(rejected).Descendants(W + "pPr").First();
        Assert.Equal("0", (string?)properties.Element(W + "keepNext")!.Attribute(W + "val"));
        Assert.Equal("0", (string?)properties.Element(W + "ind")!.Attribute(W + "left"));
        Assert.Equal("left", (string?)properties.Element(W + "jc")!.Attribute(W + "val"));
        byte[] accepted = Save(reloaded.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("center", (string?)Part(accepted).Descendants(W + "jc").Single().Attribute(W + "val"));
        WordDocument ignored = DocumentComparison.Compare(original, revised, new ComparisonOptions("Reviewer", Time) { CompareParagraphFormatting = false }, Policy(), TestContext.Current.CancellationToken);
        byte[] ignoredWord = Save(ignored); Schema(ignoredWord); Assert.Empty(ignored.Revisions);
        var ignoredProperties = Part(ignoredWord).Descendants(W + "pPr").First();
        Assert.Equal("0", (string?)ignoredProperties.Element(W + "keepNext")!.Attribute(W + "val"));
        Assert.Equal("0", (string?)ignoredProperties.Element(W + "ind")!.Attribute(W + "left"));
        Assert.Equal("left", (string?)ignoredProperties.Element(W + "jc")!.Attribute(W + "val"));
    }

    [Fact]
    public void ParagraphFormattingCanBeIgnoredWithoutDiscardingTextChanges()
    {
        WordDocument original = Load(Input(ParagraphWith(OldParagraphProperties, "Old")));
        WordDocument revised = Load(Input(ParagraphWith(NewParagraphProperties, "New")));
        var options = new ComparisonOptions("Reviewer", Time) { CompareParagraphFormatting = false };
        WordDocument result = DocumentComparison.Compare(original, revised, options, Policy(), TestContext.Current.CancellationToken);
        byte[] bytes = Save(result); Schema(bytes); Assert.Empty(Part(bytes).Descendants(W + "pPrChange"));
        byte[] accepted = Save(result.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("New", result.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken).Paragraphs[0].Text);
        Assert.Equal("left", (string?)Part(accepted).Descendants(W + "jc").Single().Attribute(W + "val"));
    }

    [Fact]
    public void ImportedParagraphFormattingHistoryUsesPriorAndCurrentProperties()
    {
        string history = "<w:pPr><w:jc w:val='center'/><w:pPrChange w:id='42' w:author='Word'><w:pPr><w:jc w:val='left'/></w:pPr></w:pPrChange></w:pPr>";
        WordDocument document = Load(Input(ParagraphWith(history, "Imported")));
        Assert.Equal("Word", Assert.Single(document.Revisions).Author);
        byte[] saved = Save(document); Schema(saved); Assert.Single(Load(saved).Revisions);
        byte[] old = Save(document.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("left", (string?)Part(old).Descendants(W + "jc").Single().Attribute(W + "val"));
        string malformed = history.Replace("<w:jc w:val='left'/>", "<w:tabs><w:tab w:val='left' w:pos='100'/></w:tabs>");
        Assert.Throws<NotSupportedException>(() => Load(Input(ParagraphWith(malformed, "Imported"))));
    }

    [Fact]
    public void OrdinaryConversionDiagnosesDiscardedParagraphHistoryAndKeepsStrictOutputUntouched()
    {
        string history = "<w:pPr><w:jc w:val='center'/><w:pPrChange w:id='42' w:author='Word'><w:pPr><w:jc w:val='left'/></w:pPr></w:pPrChange></w:pPr>";
        using var input = new MemoryStream(Input(ParagraphWith(history, "Imported"))); using var output = new MemoryStream();
        output.WriteByte(42); output.Position = 0;
        Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, new ConversionOptions(), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
        input.Position = 0; using var lenientOutput = new MemoryStream();
        ConversionResult result = new WordConverter().Convert(input, lenientOutput, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "Revisions.Accepted");
        using var pdf = PdfDocument.Open(lenientOutput.ToArray()); Assert.Equal("Imported", pdf.GetPage(1).Text);
    }

    [Fact]
    public void ParagraphFormattingInsideSimpleCellsAndAtSectionBoundariesIsSchemaValid()
    {
        string Cell(string properties) => "<w:tbl><w:tr><w:tc>" + ParagraphWith(properties, "Cell") + "</w:tc></w:tr></w:tbl>";
        WordDocument result = DocumentComparison.Compare(Load(Input(Cell(OldParagraphProperties))), Load(Input(Cell(NewParagraphProperties))),
            new ComparisonOptions("Reviewer", Time), Policy(), TestContext.Current.CancellationToken);
        byte[] word = Save(result); Schema(word); Assert.Single(Load(word).Revisions);
        string section = "<w:sectPr><w:type w:val='nextPage'/></w:sectPr>";
        string old = ParagraphWith(OldParagraphProperties.Replace("</w:pPr>", section + "</w:pPr>"), "Section") + P("Next");
        string current = ParagraphWith(NewParagraphProperties.Replace("</w:pPr>", section + "</w:pPr>"), "Section") + P("Next");
        WordDocument sectionResult = DocumentComparison.Compare(Load(Input(old)), Load(Input(current)), new ComparisonOptions("Reviewer", Time), Policy(), TestContext.Current.CancellationToken);
        byte[] sections = Save(sectionResult); Schema(sections); Assert.Single(Load(sections).Revisions);
        var change = Part(sections).Descendants(W + "pPrChange").Single();
        Assert.NotNull(change.Parent!.Element(W + "sectPr")); Assert.Null(change.Element(W + "pPr")!.Element(W + "sectPr"));
    }

    [Fact]
    public void ParagraphMarkFormattingRemainsUnchangedAcrossPropertyHistory()
    {
        string mark = "<w:rPr><w:b/></w:rPr>";
        string old = OldParagraphProperties.Replace("</w:pPr>", mark + "</w:pPr>");
        string current = NewParagraphProperties.Replace("</w:pPr>", mark + "</w:pPr>");
        WordDocument result = DocumentComparison.Compare(Load(Input(ParagraphWith(old, "Marked"))), Load(Input(ParagraphWith(current, "Marked"))),
            new ComparisonOptions("Reviewer", Time), Policy(), TestContext.Current.CancellationToken);
        byte[] word = Save(result); Schema(word); Assert.Single(Load(word).Revisions);
        byte[] rejected = Save(Load(word).CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken));
        Assert.NotNull(Part(rejected).Descendants(W + "pPr").First().Element(W + "rPr")!.Element(W + "b"));
    }

    [Fact]
    public void ParagraphFormattingRetainsExistingStructuralAndDisplayBoundaries()
    {
        WordDocument original = Load(Input(ParagraphWith(OldParagraphProperties, "A") + P("B")));
        WordDocument revised = Load(Input(ParagraphWith(NewParagraphProperties, "A") + P("Inserted") + P("B")));
        byte[] before = Save(original);
        Assert.Throws<NotSupportedException>(() => DocumentComparison.Compare(original, revised, new ComparisonOptions("Reviewer", Time), Policy(), TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(original));
        WordDocument result = DocumentComparison.Compare(Load(Input(ParagraphWith(OldParagraphProperties, "A"))), Load(Input(ParagraphWith(NewParagraphProperties, "A"))),
            new ComparisonOptions("Reviewer", Time), Policy(), TestContext.Current.CancellationToken);
        Assert.Throws<NotSupportedException>(() => result.CreateReviewDisplay(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Throws<NotSupportedException>(() => result.CreateReviewDisplay(new ReviewDisplayOptions { AnnotationPolicy = ReviewAnnotationPolicy.NumberedEndNotes }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ParagraphFormattingHistoryComposesWithDistinctIdsAndRejectsRestyling()
    {
        WordDocument original = Load(Input(ParagraphWith(OldParagraphProperties, "A")));
        WordDocument revised = Load(Input(ParagraphWith(NewParagraphProperties, "A")));
        WordDocument source = DocumentComparison.Compare(original, revised, new ComparisonOptions("Reviewer", Time), Policy(), TestContext.Current.CancellationToken);
        WordDocument target = Load(Input(P("Prefix")));
        target.Append(source, cancellationToken: TestContext.Current.CancellationToken);
        target.Append(source, cancellationToken: TestContext.Current.CancellationToken);
        byte[] output = Save(target); Schema(output);
        Assert.Equal(2, Part(output).Descendants(W + "pPrChange").Select(e => (string?)e.Attribute(W + "id")).Distinct().Count());
        Assert.Equal(2, Load(output).Revisions.Count);
        WordDocument rejected = Load(output).CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, Part(Save(rejected)).Descendants(W + "jc").Count(e => (string?)e.Attribute(W + "val") == "left"));
        byte[] before = Save(target);
        Assert.Throws<NotSupportedException>(() => target.Append(source, CompositionStyle.UseDestination, TestContext.Current.CancellationToken));
        Assert.Equal(before, Save(target));
    }

    [Fact]
    public void ParagraphFormattingUsesTheComparisonBudgetAndCancellationAtomically()
    {
        WordDocument original = Load(Input(ParagraphWith(OldParagraphProperties, "Same")));
        WordDocument revised = Load(Input(ParagraphWith(NewParagraphProperties, "Same")));
        byte[] before = Save(original), after = Save(revised);
        Assert.Throws<InvalidDataException>(() => DocumentComparison.Compare(original, revised, new ComparisonOptions("Reviewer", Time) { MaximumSteps = 1 }, Policy(), TestContext.Current.CancellationToken));
        Assert.Throws<OperationCanceledException>(() => DocumentComparison.Compare(original, revised, new ComparisonOptions("Reviewer", Time), Policy(), new System.Threading.CancellationToken(true)));
        Assert.Equal(before, Save(original)); Assert.Equal(after, Save(revised));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportedParagraphHistoryRejectsUnsupportedOwners(bool header)
    {
        string history = "<w:pPr><w:jc w:val='center'/><w:pPrChange w:id='1' w:author='Word'><w:pPr><w:jc w:val='left'/></w:pPr></w:pPrChange></w:pPr>";
        byte[] input;
        if (header)
        {
            string section = "<w:sectPr><w:headerReference w:type='default' r:id='header'/></w:sectPr>";
            input = TestDocuments.Docx(TestDocuments.Body(P("Body") + section),
                "<Relationship Id='header' Type='" + TestDocuments.R + "/header' Target='header.xml'/>",
                extra: new KeyValuePair<string, string>("word/header.xml", "<w:hdr xmlns:w='" + W + "'>" + ParagraphWith(history, "Header") + "</w:hdr>"));
        }
        else input = Input("<w:tbl><w:tr><w:tc><w:tcPr><w:gridSpan w:val='2'/></w:tcPr>" + ParagraphWith(history, "Merged") + "</w:tc></w:tr></w:tbl>");
        Assert.Throws<NotSupportedException>(() => Load(input));
    }

    [Fact]
    public void ImportedParagraphHistoryCannotHideASectionChange()
    {
        string history = "<w:pPr><w:jc w:val='center'/><w:sectPr/><w:pPrChange w:id='1' w:author='Word'><w:pPr><w:jc w:val='left'/><w:sectPr/></w:pPr></w:pPrChange></w:pPr>";
        Assert.Throws<NotSupportedException>(() => Load(Input(ParagraphWith(history, "Boundary"))));
    }

    [Theory]
    [InlineData("<w:ind w:firstLine='20' w:hanging='20'/>", "")]
    [InlineData("<w:pStyle/>", "<w:pStyle/>")]
    public void ImportedParagraphHistoryRejectsInvalidIndentAndStyleMetadata(string oldProperties, string currentProperties)
    {
        string history = "<w:pPr>" + currentProperties + "<w:pPrChange w:id='1' w:author='Word'><w:pPr>" + oldProperties + "</w:pPr></w:pPrChange></w:pPr>";
        Assert.Throws<InvalidDataException>(() => Load(Input(ParagraphWith(history, "Invalid"))));
    }

    [Fact]
    public void ParagraphHistoryCannotBeHiddenInsideAStyleDefinition()
    {
        string history = "<w:pPr><w:pPrChange w:id='1' w:author='Word'><w:pPr><w:jc w:val='left'/></w:pPr></w:pPrChange></w:pPr>";
        string styles = "<w:styles xmlns:w='" + W + "'><w:style w:type='paragraph' w:styleId='Bad'><w:name w:val='Bad'/>" + history + "</w:style></w:styles>";
        byte[] input = TestDocuments.Docx(TestDocuments.Body(ParagraphWith("<w:pPr><w:pStyle w:val='Bad'/></w:pPr>", "Text")),
            "<Relationship Id='styles' Type='" + TestDocuments.R + "/styles' Target='styles.xml'/>",
            extra: new KeyValuePair<string, string>("word/styles.xml", styles));
        Assert.Throws<NotSupportedException>(() => Load(input));
        byte[] unused = TestDocuments.Docx(TestDocuments.Body(P("Text")),
            "<Relationship Id='styles' Type='" + TestDocuments.R + "/styles' Target='styles.xml'/>",
            extra: new KeyValuePair<string, string>("word/styles.xml", styles));
        Assert.Throws<NotSupportedException>(() => Load(unused));
    }
}
