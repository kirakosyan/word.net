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
        string tail = "<w:p>" + Field("PAGE", "1") + "</w:p>" + Page;
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
        using PdfDocument pdf = PdfDocument.Open(Save(accepted, DocumentFormat.Pdf));
        string text = string.Concat(pdf.GetPages().Select(p => p.Text));
        Assert.Contains("Revised", text, StringComparison.Ordinal);
        Assert.Contains("Ada", text, StringComparison.Ordinal);
        Assert.Contains("Bea", text, StringComparison.Ordinal);
        Assert.Contains("1", text, StringComparison.Ordinal);
        using PdfDocument original = PdfDocument.Open(Save(rejected, DocumentFormat.Pdf));
        Assert.Contains("Original", string.Concat(original.GetPages().Select(p => p.Text)), StringComparison.Ordinal);
    }
}
