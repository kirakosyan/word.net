using System;
using System.Linq;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed partial class EditingTemplateTests
{
    [Fact]
    public void ReportFieldsAndReviewDecisionsComposeInTheSameDocument()
    {
        string report = Region("<w:p>" + ReportFieldXml("Name") + "</w:p>" + MixedTable(ReportFieldXml("Value")));
        string tail = "<w:p>" + Field("PAGE", "9") + "</w:p>" + Page;
        WordDocument old = Load(Docx(P("Original") + report + tail));
        WordDocument revised = Load(Docx(P("Revised") + report + tail));
        var data = new { Items = new[] { new { Name = "Ada", Value = "One" }, new { Name = "Bea", Value = "Two" } } };
        old.Merge(data, cancellationToken: TestContext.Current.CancellationToken);
        revised.Merge(data, cancellationToken: TestContext.Current.CancellationToken);
        WordDocument result = DocumentComparison.Compare(old, revised,
            new ComparisonOptions("Reviewer", new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero)), cancellationToken: TestContext.Current.CancellationToken);
        WordDocument accepted = result.CreateReviewView(ReviewDecision.Accept, cancellationToken: TestContext.Current.CancellationToken);
        WordDocument rejected = result.CreateReviewView(ReviewDecision.Reject, cancellationToken: TestContext.Current.CancellationToken);
        Schema(Save(accepted)); Schema(Save(rejected));
        foreach (var sample in new[] { (Document: accepted, Present: "Revised", Absent: "Original"), (Document: rejected, Present: "Original", Absent: "Revised") })
        {
            using PdfDocument pdf = PdfDocument.Open(Save(sample.Document, DocumentFormat.Pdf));
            string text = string.Concat(pdf.GetPages().Select(p => p.Text));
            Assert.Contains(sample.Present, text, StringComparison.Ordinal); Assert.DoesNotContain(sample.Absent, text, StringComparison.Ordinal);
            foreach (string value in new[] { "Ada", "Bea", "One", "Two", "1" }) Assert.Contains(value, text, StringComparison.Ordinal);
            Assert.DoesNotContain("9", text, StringComparison.Ordinal);
        }
    }
}
