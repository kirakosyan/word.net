using System;
using System.IO;
using System.Threading;
using WordNet;

namespace WordNet.Examples;

// Executable examples use the same public APIs as callers; fixed synthetic attribution.
internal static class P1Recipes
{
    internal static void Run(string directory, string templates, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var plan = new ReportPlan(new[] { new ReportCollectionOperation("Groups", "Items", "Category", "Category", aggregates: new[] {
            new ReportAggregate("Sum", ReportAggregateKind.Sum, "Amount"), new ReportAggregate("Count", ReportAggregateKind.Count) }) },
            new[] {
                new ReportField("Tax", ReportExpression.Round(ReportExpression.Binary(ReportOperation.Multiply, ReportExpression.Path("Net"), ReportExpression.Literal(0.2m)), 2, MidpointRounding.AwayFromZero)),
                new ReportField("Total", ReportExpression.Binary(ReportOperation.Add, ReportExpression.Path("Net"), ReportExpression.Path("Tax"))),
                new ReportField("ShowTerms", ReportExpression.Binary(ReportOperation.Greater, ReportExpression.Path("Total"), ReportExpression.Literal(110m))) });
        ReportData prepared = ReportData.Prepare(new { Net = 100m, Items = new[] { new { Category = "Beta", Amount = 30m }, new { Category = "Alpha", Amount = 20m }, new { Category = "Beta", Amount = 50m } } }, plan, cancellationToken: cancellationToken);
        using var template = File.OpenRead(Path.Combine(templates, "p1-report.docx"));
        WordDocument report = WordDocument.Load(template, DocumentFormat.Docx, out _, AutomationRecipes.Options(), cancellationToken);
        report.Merge(prepared.Values, cancellationToken: cancellationToken);
        Save(report, "p1-report.docx", DocumentFormat.Docx); Save(report, "p1-report.pdf", DocumentFormat.Pdf);
        WordDocument original = WordDocument.Create(AutomationRecipes.Options()); original.AddParagraph("Total 10", cancellationToken: cancellationToken);
        WordDocument revised = WordDocument.Create(AutomationRecipes.Options()); revised.AddParagraph("Total 20", cancellationToken: cancellationToken);
        DateTimeOffset timestamp = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        WordDocument compared = DocumentComparison.Compare(original, revised, new ComparisonOptions("Example Reviewer", timestamp), AutomationRecipes.Options(), cancellationToken);
        Save(compared, "p1-compared.docx", DocumentFormat.Docx);
        DocumentReviewRange range = original.GetReviewRange(0, 6, 2, cancellationToken);
        original.RecordReplacement(range, "20", "Example Reviewer", timestamp, cancellationToken);
        Save(original, "p1-recorded.docx", DocumentFormat.Docx);
        Save(original.CreateReviewView(ReviewDecision.Accept, cancellationToken: cancellationToken), "p1-accepted.pdf", DocumentFormat.Pdf);
        Save(original.CreateReviewView(ReviewDecision.Reject, cancellationToken: cancellationToken), "p1-rejected.pdf", DocumentFormat.Pdf);
        using var word = new MemoryStream(); report.Save(word, DocumentFormat.Docx, cancellationToken: cancellationToken); word.Position = 0;
        using var odt = new MemoryStream(); new WordConverter().Convert(word, odt, DocumentFormat.Docx, DocumentFormat.Odt, AutomationRecipes.Options(), cancellationToken);
        File.WriteAllBytes(Path.Combine(directory, "p1-report.odt"), odt.ToArray());
        void Save(WordDocument document, string name, DocumentFormat format)
        {
            using var output = new MemoryStream(); document.Save(output, format, cancellationToken: cancellationToken);
            File.WriteAllBytes(Path.Combine(directory, name), output.ToArray());
        }
    }
}
