using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Xunit;

namespace WordNet.Tests;

public sealed class TemplateInspectionTests
{
    [Fact]
    public void SimpleFieldFindingsExposeImmutableNumericSourcePositions()
    {
        byte[] bytes = Document(Field("MERGEFIELD Customer.Name") + Table(Field("MERGEFIELD Customer.Code")));
        using var source = new MemoryStream(bytes);
        TemplateInspection report = new WordConverter().InspectTemplate(source, new TemplateDataSchema(), DocumentFormat.Docx,
            cancellationToken: TestContext.Current.CancellationToken);
        TemplateInspectionFinding[] findings = report.Findings.Where(f => f.Code == "Plan.MissingPath").ToArray();
        Assert.Equal(2, findings.Length);
        TemplateInspectionFinding body = Assert.Single(findings, f => report.GetSensitivePathAliases()[f.PathId!] == "Customer.Name");
        TemplateInspectionFinding cell = Assert.Single(findings, f => report.GetSensitivePathAliases()[f.PathId!] == "Customer.Code");
        Assert.Equal("1,1,1,1", string.Join(",", body.SourceElementIndices!));
        Assert.Equal("1,1,2,3,1,2,1", string.Join(",", cell.SourceElementIndices!));
        var positions = Assert.IsAssignableFrom<System.Collections.Generic.IList<int>>(cell.SourceElementIndices);
        Assert.Throws<NotSupportedException>(() => positions[0] = 42);
        Assert.Null(Assert.Single(report.Findings, f => f.Code == "Plan.OwnerCoverage").SourceElementIndices);
        string json = JsonSerializer.Serialize(report);
        Assert.DoesNotContain("Customer", json, StringComparison.Ordinal); Assert.DoesNotContain("word/document.xml", json, StringComparison.Ordinal);
        Assert.DoesNotContain("fldSimple", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ComplexFieldFindingsDoNotInventAnElementPosition()
    {
        const string body = "<w:p><w:r><w:fldChar w:fldCharType='begin'/></w:r><w:r><w:instrText> MERGEFIELD Secret </w:instrText></w:r>"
            + "<w:r><w:fldChar w:fldCharType='separate'/></w:r><w:r><w:t>cached</w:t></w:r><w:r><w:fldChar w:fldCharType='end'/></w:r></w:p>";
        using var source = new MemoryStream(Document(body));
        TemplateInspection report = new WordConverter().InspectTemplate(source, new TemplateDataSchema(), DocumentFormat.Docx,
            cancellationToken: TestContext.Current.CancellationToken);
        TemplateInspectionFinding finding = Assert.Single(report.Findings, f => f.Code == "Plan.MissingPath");
        Assert.Equal("Secret", report.GetSensitivePathAliases()[finding.PathId!]); Assert.NotNull(finding.PartId);
        Assert.Null(finding.SourceElementIndices);
    }

    [Fact]
    public void SourcePositionWorkIsBoundedAndOnlyComputedForFindings()
    {
        string body = "<w:p>" + string.Concat(Enumerable.Repeat("<w:r/>", 50000))
            + "<w:fldSimple w:instr='MERGEFIELD Name'><w:r><w:t>cached</w:t></w:r></w:fldSimple></w:p>";
        byte[] bytes = Document(body); using var source = new MemoryStream(bytes);
        var converter = new WordConverter();
        var schema = new TemplateDataSchema(new TemplateSchemaPath("Name", TemplatePathType.Scalar));
        TemplateInspection complete = converter.InspectTemplate(source, schema, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(complete.Requirements); Assert.DoesNotContain(complete.Findings, f => f.Code == "Plan.MissingPath");
        source.Position = 0;
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => converter.InspectTemplate(source, new TemplateDataSchema(), DocumentFormat.Docx,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.StartsWith("Limit.TemplateInspection:", error.Message, StringComparison.Ordinal); Assert.True(source.CanRead);
    }

    [Fact]
    public void SimpleTableCellFieldsMatchActualMergeAndKeepOwnerCoverageUnknown()
    {
        byte[] bytes = Document(Field("MERGEFIELD Customer.Name") + Table(Field("MERGEFIELD Customer.Code")));
        var schema = new TemplateDataSchema(new TemplateSchemaPath("Customer", TemplatePathType.Mapping),
            new TemplateSchemaPath("Customer.Name", TemplatePathType.Scalar));
        using var source = new MemoryStream(bytes);
        TemplateInspection report = new WordConverter().InspectTemplate(source, schema, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(report.IsReusable); Assert.Equal(2, report.Requirements.Count);
        TemplateInspectionFinding missing = Assert.Single(report.Findings, finding => finding.Code == "Plan.MissingPath");
        Assert.Equal("Customer.Code", report.GetSensitivePathAliases()[missing.PathId!]);
        Assert.Equal("word/document.xml", report.GetSensitivePartAliases()[missing.PartId!]);
        Assert.Contains(report.Findings, finding => finding.Code == "Plan.OwnerCoverage" && finding.Unknown);
        string json = JsonSerializer.Serialize(report);
        Assert.DoesNotContain("Customer", json, StringComparison.Ordinal); Assert.DoesNotContain("word/document.xml", json, StringComparison.Ordinal);
        source.Position = 0;
        var document = WordDocument.Load(source, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        Assert.Throws<InvalidDataException>(() => document.MergeJson("{\"Customer\":{\"Name\":\"Ada\"}}", cancellationToken: TestContext.Current.CancellationToken));
        Assert.All(document.Paragraphs, paragraph => Assert.Equal("cached", paragraph.Text));
        Assert.Equal(2, document.MergeJson("{\"Customer\":{\"Name\":\"Ada\",\"Code\":\"Q42\"}}", cancellationToken: TestContext.Current.CancellationToken).Changes);
        Assert.Collection(document.Paragraphs, paragraph => Assert.Equal("Ada", paragraph.Text), paragraph => Assert.Equal("Q42", paragraph.Text));
    }

    [Fact]
    public void MergedNestedAndWrappedTableOwnersRemainUninspected()
    {
        string field = Field("MERGEFIELD Hidden");
        string merged = "<w:tbl><w:tblGrid><w:gridCol w:w='1500'/><w:gridCol w:w='1500'/></w:tblGrid><w:tr><w:tc><w:tcPr><w:gridSpan w:val='2'/></w:tcPr>" + field + "</w:tc></w:tr></w:tbl>";
        string nested = "<w:tbl><w:tr><w:tc>" + Table(field) + "<w:p/></w:tc></w:tr></w:tbl>";
        string wrapped = "<w:tbl><w:sdt><w:sdtPr><w:tag w:val='repeat:Items'/></w:sdtPr><w:sdtContent><w:tr><w:tc>" + field + "</w:tc></w:tr></w:sdtContent></w:sdt></w:tbl>";
        foreach (string body in new[] { merged, nested, wrapped })
        {
            using var source = new MemoryStream(Document(body));
            TemplateInspection report = new WordConverter().InspectTemplate(source, new TemplateDataSchema(), DocumentFormat.Docx,
                new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
            Assert.Contains(report.Findings, finding => finding.Code == "Plan.OwnerCoverage" && finding.Unknown);
            Assert.Empty(report.Requirements); Assert.DoesNotContain(report.Findings, finding => finding.Code == "Plan.MissingPath");
        }
    }

    [Fact]
    public void DeclaredBodyFieldsMatchActualMergeWithoutDisclosingNames()
    {
        byte[] bytes = Document(Field("MERGEFIELD Customer.Name"));
        var schema = new TemplateDataSchema(new TemplateSchemaPath("Customer", TemplatePathType.Mapping),
            new TemplateSchemaPath("Customer.Name", TemplatePathType.Scalar));
        using var source = new MemoryStream(bytes);
        TemplateInspection report = new WordConverter().InspectTemplate(source, schema, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(source.CanRead); Assert.False(report.IsReusable);
        TemplateInspectionRequirement requirement = Assert.Single(report.Requirements);
        Assert.Equal("Customer.Name", report.GetSensitivePathAliases()[requirement.PathId]);
        Assert.Equal(TemplatePathType.Scalar, requirement.DeclaredType); Assert.True(requirement.Required);
        Assert.DoesNotContain(report.Findings, f => f.Code is "Plan.MissingPath" or "Plan.PathType" or "Plan.ParentTypeUnknown");
        Assert.Contains(report.Findings, f => f.Code == "Plan.UninspectedCoverage" && f.Unknown);
        string json = JsonSerializer.Serialize(report);
        Assert.DoesNotContain("Customer", json, StringComparison.Ordinal);
        Assert.DoesNotContain("word/document.xml", json, StringComparison.Ordinal);
        source.Position = 0;
        var document = WordDocument.Load(source, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        Assert.Equal(1, document.MergeJson("{\"Customer\":{\"Name\":\"Ada\"}}", cancellationToken: TestContext.Current.CancellationToken).Changes);
        Assert.Equal("Ada", Assert.Single(document.Paragraphs).Text);
    }

    [Fact]
    public void MissingPathPredictionMatchesTheActualMergeFailure()
    {
        byte[] bytes = Document(Field("MERGEFIELD PrivateName"));
        using var source = new MemoryStream(bytes);
        TemplateInspection report = new WordConverter().InspectTemplate(source, new TemplateDataSchema(), DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        TemplateInspectionFinding finding = Assert.Single(report.Findings, f => f.Code == "Plan.MissingPath");
        Assert.Equal("PrivateName", report.GetSensitivePathAliases()[finding.PathId!]);
        Assert.NotNull(finding.PartId); Assert.Equal("word/document.xml", report.GetSensitivePartAliases()[finding.PartId!]);
        source.Position = 0;
        var document = WordDocument.Load(source, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        Assert.Throws<InvalidDataException>(() => document.MergeJson("{}", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("cached", Assert.Single(document.Paragraphs).Text);
    }

    [Theory]
    [InlineData(TemplatePathType.Number, null)]
    [InlineData(TemplatePathType.Date, "Plan.PathType")]
    [InlineData(TemplatePathType.Scalar, "Plan.PathTypeUnknown")]
    public void FormattingSwitchChecksOnlyWhatTheSchemaEstablishes(TemplatePathType type, string? expected)
    {
        using var source = new MemoryStream(Document(Field("MERGEFIELD Total \\# &quot;N2&quot;")));
        var report = new WordConverter().InspectTemplate(source, new TemplateDataSchema(new TemplateSchemaPath("Total", type, required: false)), DocumentFormat.Pdf,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(report.Findings, f => f.Code == "Plan.OptionalPath" && f.Unknown);
        if (expected == null) Assert.DoesNotContain(report.Findings, f => f.Code is "Plan.PathType" or "Plan.PathTypeUnknown");
        else Assert.Contains(report.Findings, f => f.Code == expected);
    }

    [Fact]
    public void NestedPathsNeedDeclaredRequiredMappingParents()
    {
        using var source = new MemoryStream(Document(Field("MERGEFIELD Client.Address.City")));
        var schema = new TemplateDataSchema(new TemplateSchemaPath("Client", TemplatePathType.Scalar),
            new TemplateSchemaPath("Client.Address.City", TemplatePathType.Scalar));
        TemplateInspection report = new WordConverter().InspectTemplate(source, schema, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(report.Findings, f => f.Code == "Plan.ParentType" && report.GetSensitivePathAliases()[f.PathId!] == "Client");
        Assert.Contains(report.Findings, f => f.Code == "Plan.ParentTypeUnknown" && report.GetSensitivePathAliases()[f.PathId!] == "Client.Address");
    }

    [Fact]
    public void InvalidAndNonMergeFieldsAreReportedWithoutEvaluatingTheClock()
    {
        byte[] bytes = Document(Field("MERGEFIELD Invalid-Name") + Field("DATE"));
        using var source = new MemoryStream(bytes);
        var options = new ConversionOptions(FidelityMode.Lenient) { Clock = () => throw new InvalidOperationException("A clock must not run during this inspection.") };
        TemplateInspection report = new WordConverter().InspectTemplate(source, new TemplateDataSchema(), DocumentFormat.Docx, options, TestContext.Current.CancellationToken);
        Assert.Contains(report.Findings, f => f.Code == "Plan.InvalidField" && f.PartId != null);
        Assert.Contains(report.Findings, f => f.Code == "Plan.FieldCoverage" && f.Unknown);
        Assert.DoesNotContain("Invalid-Name", JsonSerializer.Serialize(report), StringComparison.Ordinal);
    }

    [Fact]
    public void SourceDiagnosticRetainsSeverityCountAndKnownPhase()
    {
        byte[] bytes = Document("<w:p><w:r><w:rPr><w:color w:val='FF0000' w:themeColor='accent1'/></w:rPr><w:t>hello</w:t></w:r></w:p>");
        var options = new ConversionOptions(FidelityMode.Lenient);
        using var inspected = new MemoryStream(bytes);
        TemplateInspection report = new WordConverter().InspectTemplate(inspected, new TemplateDataSchema(), DocumentFormat.Docx, options, TestContext.Current.CancellationToken);
        using var loaded = new MemoryStream(bytes);
        _ = WordDocument.Load(loaded, DocumentFormat.Docx, out ConversionResult original, options, TestContext.Current.CancellationToken);
        ConversionDiagnostic diagnostic = Assert.Single(original.Diagnostics, d => d.Code == "Docx.ThemeColor");
        TemplateInspectionFinding finding = Assert.Single(report.Findings, f => f.Code == diagnostic.Code);
        Assert.Equal(diagnostic.Severity, finding.Severity); Assert.Equal(diagnostic.Count, finding.Count);
        Assert.Equal(DiagnosticPhase.Parse, finding.SourcePhase);
        Assert.NotNull(finding.PartId); Assert.Equal("word/document.xml", report.GetSensitivePartAliases()[finding.PartId!]);
    }

    [Fact]
    public void TruncatedSourceLocationsKeepTheirCountAndUncertainty()
    {
        string paragraph = "<w:p><w:r><w:rPr><w:color w:val='FF0000' w:themeColor='accent1'/></w:rPr><w:t>x</w:t></w:r></w:p>";
        using var source = new MemoryStream(Document(string.Concat(Enumerable.Repeat(paragraph, 150))));
        TemplateInspection report = new WordConverter().InspectTemplate(source, new TemplateDataSchema(), DocumentFormat.Docx,
            new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        TemplateInspectionFinding finding = Assert.Single(report.Findings, f => f.Code == "Docx.ThemeColor");
        Assert.Equal(150, finding.Count); Assert.True(finding.LocationsTruncated);
        Assert.Contains(report.Findings, f => f.Code == "Plan.LocationCoverage" && f.Unknown);
    }

    [Fact]
    public void RequestedDestinationDoesNotChangeTemplatePathInspection()
    {
        byte[] bytes = Document(Field("MERGEFIELD Customer.Name") + Field("TOC \\o &quot;1-3&quot;"));
        var schema = new TemplateDataSchema(new TemplateSchemaPath("Customer", TemplatePathType.Mapping),
            new TemplateSchemaPath("Customer.Name", TemplatePathType.Scalar));
        TemplateInspection Inspect(DocumentFormat destination)
        {
            using var input = new MemoryStream(bytes);
            return new WordConverter().InspectTemplate(input, schema, destination, cancellationToken: TestContext.Current.CancellationToken);
        }
        TemplateInspection word = Inspect(DocumentFormat.Docx), pdf = Inspect(DocumentFormat.Pdf), xps = Inspect(DocumentFormat.Xps);
        Assert.Equal(JsonSerializer.Serialize(word.Requirements), JsonSerializer.Serialize(pdf.Requirements));
        Assert.Equal(JsonSerializer.Serialize(word.Requirements), JsonSerializer.Serialize(xps.Requirements));
        Assert.Equal(JsonSerializer.Serialize(word.Findings), JsonSerializer.Serialize(pdf.Findings));
        Assert.Equal(JsonSerializer.Serialize(word.Findings), JsonSerializer.Serialize(xps.Findings));
    }

    [Fact]
    public void RepeatsAndUninspectedOwnersStayUnknown()
    {
        string repeat = "<w:sdt><w:sdtPr><w:tag w:val='repeat:Items'/></w:sdtPr><w:sdtContent>" + Field("MERGEFIELD Name") + "</w:sdtContent></w:sdt>";
        using var source = new MemoryStream(Document(repeat + "<w:tbl><w:tr><w:tc><w:p/></w:tc></w:tr></w:tbl>"));
        TemplateInspection report = new WordConverter().InspectTemplate(source, new TemplateDataSchema(), DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(report.Findings, f => f.Code == "Plan.RepeatScope" && f.Unknown);
        Assert.Contains(report.Findings, f => f.Code == "Plan.OwnerCoverage" && f.Unknown);
        Assert.DoesNotContain(report.Findings, f => f.Code == "Plan.MissingPath");
    }

    [Fact]
    public void CurrentPositionBoundsCancellationAndSchemaSnapshotsAreEnforced()
    {
        byte[] bytes = Document(Field("MERGEFIELD Name"));
        var entries = new[] { new TemplateSchemaPath("Name", TemplatePathType.Scalar) };
        var schema = new TemplateDataSchema(entries); entries[0] = new TemplateSchemaPath("Other", TemplatePathType.Number);
        byte[] prefixed = new byte[bytes.Length + 1]; Array.Copy(bytes, 0, prefixed, 1, bytes.Length);
        using var source = new MemoryStream(prefixed) { Position = 1 };
        var report = new WordConverter().InspectTemplate(source, schema, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("Name", report.GetSensitivePathAliases()[Assert.Single(report.Requirements).PathId]);
        using var plain = new MemoryStream(bytes);
        var same = new WordConverter().InspectTemplate(plain, schema, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(report.InputSha256, same.InputSha256); Assert.Equal(JsonSerializer.Serialize(report), JsonSerializer.Serialize(same));
        using var limited = new MemoryStream(bytes);
        Assert.Throws<InvalidDataException>(() => new WordConverter().InspectTemplate(limited, schema, DocumentFormat.Docx,
            new ConversionOptions(maximumInputBytes: 16), TestContext.Current.CancellationToken));
        using var cancelled = new MemoryStream(bytes); using var token = new CancellationTokenSource(); token.Cancel();
        Assert.Throws<OperationCanceledException>(() => new WordConverter().InspectTemplate(cancelled, schema, DocumentFormat.Docx, cancellationToken: token.Token));
        Assert.Equal(0, cancelled.Position); Assert.True(cancelled.CanRead);
        Assert.Throws<ArgumentException>(() => new TemplateDataSchema(new TemplateSchemaPath("Name", TemplatePathType.Scalar), new TemplateSchemaPath("Name", TemplatePathType.Number)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TemplateDataSchema(Enumerable.Repeat(new TemplateSchemaPath("Name", TemplatePathType.Scalar), 4097).ToArray()));
    }

    private static byte[] Document(string body) => TestDocuments.Docx(TestDocuments.Body(body));
    private static string Field(string instruction) => "<w:p><w:fldSimple w:instr='" + instruction + "'><w:r><w:t>cached</w:t></w:r></w:fldSimple></w:p>";
    private static string Table(string content) => "<w:tbl><w:tblPr><w:tblW w:w='3000' w:type='dxa'/><w:tblLayout w:type='fixed'/></w:tblPr>"
        + "<w:tblGrid><w:gridCol w:w='3000'/></w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:w='3000' w:type='dxa'/></w:tcPr>" + content + "</w:tc></w:tr></w:tbl>";
}
