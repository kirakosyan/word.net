using System;
using System.IO;
using System.Linq;
using System.Threading;
using WordNet;

namespace WordNet.Examples;

// Caller font paths are explicit. No math/Indic font or native provider is fetched at runtime.
internal static class P2Recipes
{
    private static readonly string[] Quarters = { "Q1", "Q2", "Q3", "Q4" };
    private static readonly byte[] Sentinel = { 7, 8, 9 };
    internal static void Run(WordDocument report, string directory, string mathFont, string hindiFont, string arabicFont, CancellationToken token)
    {
        var options = AutomationRecipes.Options(); options.Fonts.MathFamily = "STIX Two Math";
        options.Fonts.Sources.Add(FontSource.FromFile("STIX Two Math", mathFont));
        var objects = WordDocument.Create(options); objects.Title = "Quarterly values";
        DocumentObject chart = objects.AddChart(new ChartDefinition(ChartKind.ClusteredColumn, Quarters,
            new[] { new ChartSeries("Revenue", "2468AC", new double?[] { 10, 20, null, -5 }) }, "Quarterly report", categoryTitle: "Quarter", valueTitle: "Amount"),
            width: 360, height: 220, description: "Quarterly revenue including a gap and a negative value", cancellationToken: token);
        objects.UpdateChart(chart, new ChartDefinition(ChartKind.ClusteredColumn, Quarters,
            new[] { new ChartSeries("Revenue", "2468AC", new double?[] { 12, 20, null, -5 }) }, "Quarterly report", categoryTitle: "Quarter", valueTitle: "Amount"), token);
        DocumentObject equation = objects.AddEquation(EquationExpression.Fraction(EquationExpression.Text("x"), EquationExpression.Text("2")),
            description: "x divided by two", cancellationToken: token);
        objects.UpdateEquation(equation, EquationExpression.Fraction(EquationExpression.Text("x"), EquationExpression.Text("3")), description: "x divided by three", cancellationToken: token);
        report.Append(objects, cancellationToken: token);
        Save(report, "p2-report.docx", DocumentFormat.Docx, options); Save(report, "p2-report.pdf", DocumentFormat.Pdf, options); Save(report, "p2-report.xps", DocumentFormat.Xps, options);
        using var source = new MemoryStream(); report.Save(source, DocumentFormat.Docx, options, token); source.Position = 0;
        DocumentPreview preview = new WordConverter().RenderPages(source, DocumentFormat.Docx, options: options, cancellationToken: token);
        foreach (PagePreview page in preview.Pages) File.WriteAllBytes(Path.Combine(directory, "p2-report-" + page.SourcePage + ".svg"), page.GetBytes());
        Native("hi-IN", "Hindi reading sample", "Hindi हिन्दी कर्म क्षि र्कि क्‍ष क्‌ष ज़िंदा end", hindiFont, "Noto Sans Devanagari", true);
        Native("ar", "Arabic reading sample", "Arabic \u2067مَرْحَبًا (123 [45])\u2069 end", arabicFont, "Noto Sans Arabic", false);
        // Matrix failures are executable: a generic switch cannot combine objects and native/UA output.
        var incompatible = new ConversionOptions(FidelityMode.Lenient); incompatible.Typography.Enabled = true; incompatible.Typography.ExtendedBidi = true;
        incompatible.Pdf.Accessibility = PdfAccessibility.PdfUa1; incompatible.Pdf.Language = "en-US";
        using var sentinel = new MemoryStream(Sentinel, true);
        try { report.Save(sentinel, DocumentFormat.Pdf, incompatible, token); throw new InvalidOperationException("An unsupported object/native combination unexpectedly succeeded."); }
        catch (NotSupportedException) { if (!sentinel.ToArray().SequenceEqual(Sentinel)) throw new InvalidOperationException("A rejected export modified the destination."); }

        void Native(string language, string title, string text, string path, string family, bool hindi)
        {
            var native = new ConversionOptions(); native.Typography.Enabled = true; native.Typography.ExtendedBidi = true; native.Typography.Devanagari = hindi;
            native.Fonts.Sources.Add(FontSource.FromFile(family, path)); native.Pdf.Accessibility = PdfAccessibility.PdfUa1; native.Pdf.Language = language;
            WordDocument chapter = WordDocument.Create(native); chapter.Title = title; chapter.AddParagraph(title, headingLevel: 1, cancellationToken: token); chapter.AddParagraph(text, cancellationToken: token);
            Save(chapter, "p2-" + language + "-ua.pdf", DocumentFormat.Pdf, native);
        }
        void Save(WordDocument document, string name, DocumentFormat format, ConversionOptions policy)
        {
            using var output = new MemoryStream(); document.Save(output, format, policy, token); File.WriteAllBytes(Path.Combine(directory, name), output.ToArray());
        }
    }
}
