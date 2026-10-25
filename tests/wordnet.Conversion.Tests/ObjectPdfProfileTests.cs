using System;
using System.IO;
using System.Linq;
using System.Text;
using WordNet.Formats;
using Xunit;

namespace WordNet.Tests;

public sealed class ObjectPdfProfileTests
{
    private static readonly string[] Categories = { "North", "South" };
    private static WordDocument Document(ConversionOptions options, bool description = true)
    {
        options.Fonts.MathFamily = "STIX Two Math";
        options.Fonts.Sources.Add(FontSource.FromFile("STIX Two Math", Path.Combine(TestDocuments.FixtureDirectory, "P2", "STIXTwoMath.ttf")));
        WordDocument document = WordDocument.Create(options); document.Title = "Accessible revenue report";
        document.AddParagraph("Revenue report", headingLevel: 1, cancellationToken: TestContext.Current.CancellationToken);
        document.AddChart(new ChartDefinition(ChartKind.ClusteredColumn, Categories, new[] { new ChartSeries("Revenue", "2468AC", new double?[] { 0, null }) }),
            description: description ? "Revenue in euros by region" : "", cancellationToken: TestContext.Current.CancellationToken);
        document.AddEquation(EquationExpression.Fraction(EquationExpression.Text("x"), EquationExpression.Text("2")),
            description: description ? "Half of x" : "", cancellationToken: TestContext.Current.CancellationToken);
        return document;
    }

    [Theory]
    [InlineData(PdfAccessibility.Tagged)]
    [InlineData(PdfAccessibility.PdfUa1)]
    public void TaggedObjectsHaveOneOwnedDataAlternativeAndFormulaMeaning(PdfAccessibility accessibility)
    {
        var options = new ConversionOptions(FidelityMode.BestEffort); options.Pdf.Accessibility = accessibility; options.Pdf.Language = "en"; options.Pdf.Compress = false;
        WordDocument document = Document(options); using var output = new MemoryStream(); document.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        byte[] bytes = output.ToArray(); string raw = Encoding.ASCII.GetString(bytes);
        Assert.Contains("/Figure", raw, StringComparison.Ordinal); Assert.Contains("/Formula", raw, StringComparison.Ordinal);
        Assert.Contains("/Alt", raw, StringComparison.Ordinal);
        string chart = "Revenue in euros by region. North; Revenue = 0. South; Revenue = missing";
        string equation = "Half of x. Equation: (x)/(2)";
        foreach (string alternative in new[] { chart, equation })
        {
            string token = string.Concat(Encoding.BigEndianUnicode.GetBytes(alternative).Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));
            Assert.Contains(token, raw, StringComparison.OrdinalIgnoreCase);
        }
        Evidence(accessibility == PdfAccessibility.PdfUa1 ? "objects-ua" : "objects-tagged", bytes);
    }

    [Fact]
    public void ArchivalObjectsRetainRealEmbeddedFontsAndManagedVectors()
    {
        var options = new ConversionOptions(FidelityMode.BestEffort); options.Pdf.Conformance = PdfConformance.PdfA2b;
        WordDocument document = Document(options); using var output = new MemoryStream(); document.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(output.ToArray()); Assert.Equal(1, pdf.NumberOfPages);
        Assert.Contains("Revenue report", pdf.GetPage(1).Text, StringComparison.Ordinal);
        Evidence("objects-pdfa", output.ToArray());
    }

    [Theory]
    [InlineData("description")]
    [InlineData("language")]
    [InlineData("combined")]
    [InlineData("native")]
    public void IncompleteOrUnqualifiedObjectSemanticsNeverBypassChecks(string failure)
    {
        var options = new ConversionOptions(FidelityMode.Lenient); options.Pdf.Accessibility = PdfAccessibility.PdfUa1;
        options.Pdf.Language = failure == "language" ? null : "en";
        if (failure == "combined") options.Pdf.Conformance = PdfConformance.PdfA2b;
        if (failure == "native") { options.Typography.Enabled = true; options.Typography.ExtendedBidi = true; }
        WordDocument document = Document(options, failure != "description"); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<NotSupportedException>(() => document.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }
    private static void Evidence(string name, byte[] bytes)
    {
        string? folder = Environment.GetEnvironmentVariable("WORDNET_ROADMAP_EVIDENCE");
        if (folder != null) { Directory.CreateDirectory(folder); File.WriteAllBytes(Path.Combine(folder, name + ".pdf"), bytes); }
    }
}
