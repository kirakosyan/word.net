using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Xunit;

namespace WordNet.Tests;

public sealed class TemplateInspectionTests
{
    [Theory]
    [InlineData(PdfConformance.PdfA2a, PdfAccessibility.None, false, "Plan.PdfTaggedArchiveConflict", "Pdf.TaggedArchive")]
    [InlineData(PdfConformance.PdfA2b, PdfAccessibility.Tagged, false, "Plan.PdfTaggedArchiveConflict", "Pdf.TagsConformance")]
    [InlineData(PdfConformance.PdfA2b, PdfAccessibility.None, true, "Plan.PdfNativeArchiveConflict", "Typography.Conformance")]
    public void PdfProfileConflictsPredictStagedOutputRejection(PdfConformance conformance, PdfAccessibility accessibility, bool native, string findingCode, string errorCode)
    {
        byte[] bytes = Document("<w:p><w:r><w:t>text</w:t></w:r></w:p>");
        var options = new ConversionOptions(FidelityMode.Lenient);
        options.Pdf.Conformance = conformance; options.Pdf.Accessibility = accessibility; options.Typography.Enabled = native;
        using var source = new MemoryStream(bytes);
        TemplateInspection report = new WordConverter().InspectTemplate(source, new TemplateDataSchema(), DocumentFormat.Pdf, options, TestContext.Current.CancellationToken);
        TemplateInspectionFinding finding = Assert.Single(report.Findings, f => f.Code == findingCode);
        Assert.False(finding.Unknown); Assert.Null(finding.PartId); Assert.Null(finding.SourcePhase);
        Assert.Contains(report.Findings, f => f.Code == "Plan.UninspectedCoverage" && f.Unknown); Assert.False(report.IsReusable);
        source.Position = 0;
        var document = WordDocument.Load(source, DocumentFormat.Docx, out _, options, TestContext.Current.CancellationToken);
        using var output = new MemoryStream(); output.WriteByte(42);
        NotSupportedException error = Assert.Throws<NotSupportedException>(() => document.Save(output, DocumentFormat.Pdf, options, TestContext.Current.CancellationToken));
        Assert.StartsWith(errorCode, error.Message, StringComparison.Ordinal); Assert.Equal(new byte[] { 42 }, output.ToArray()); Assert.True(output.CanWrite);
    }

    [Fact]
    public void PdfOptionChecksAreBoundedToTheRequestedDestinationAndNeverEvaluateTheClock()
    {
        byte[] bytes = Document("<w:p><w:r><w:t>text</w:t></w:r></w:p>");
        var options = new ConversionOptions(FidelityMode.Lenient) { Clock = () => throw new InvalidOperationException("Inspection must not run the clock.") };
        options.Pdf.Conformance = (PdfConformance)99;
        using var pdf = new MemoryStream(bytes);
        TemplateInspection invalid = new WordConverter().InspectTemplate(pdf, new TemplateDataSchema(), DocumentFormat.Pdf, options, TestContext.Current.CancellationToken);
        Assert.Contains(invalid.Findings, f => f.Code == "Plan.PdfOptionsInvalid" && !f.Unknown);
        using var docx = new MemoryStream(bytes);
        TemplateInspection other = new WordConverter().InspectTemplate(docx, new TemplateDataSchema(), DocumentFormat.Docx, options, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(other.Findings, f => f.Code.StartsWith("Plan.Pdf", StringComparison.Ordinal));
        options.Pdf = null!;
        using var missing = new MemoryStream(bytes);
        TemplateInspection absent = new WordConverter().InspectTemplate(missing, new TemplateDataSchema(), DocumentFormat.Pdf, options, TestContext.Current.CancellationToken);
        Assert.Contains(absent.Findings, f => f.Code == "Plan.PdfOptionsMissing" && !f.Unknown);
    }

    [Fact]
    public void SimpleCellControlRequirementsMatchActualMerge()
    {
        byte[] bytes = Document(InlineControl("merge:Customer.Name") + Table(InlineControl("merge:Customer.Code")));
        var schema = new TemplateDataSchema(new TemplateSchemaPath("Customer", TemplatePathType.Mapping),
            new TemplateSchemaPath("Customer.Name", TemplatePathType.Scalar));
        using var source = new MemoryStream(bytes);
        TemplateInspection report = new WordConverter().InspectTemplate(source, schema, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        TemplateInspectionFinding missing = Assert.Single(report.Findings, f => f.Code == "Plan.MissingPath");
        Assert.Equal("Customer.Code", report.GetSensitivePathAliases()[missing.PathId!]); Assert.Equal("1,1,2,3,1,2,1", string.Join(",", missing.SourceElementIndices!));
        Assert.Contains(report.Findings, f => f.Code == "Plan.ControlCoverage" && f.Unknown);
        Assert.Contains(report.Findings, f => f.Code == "Plan.OwnerCoverage" && f.Unknown);
        source.Position = 0;
        var document = WordDocument.Load(source, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        Assert.Throws<InvalidDataException>(() => document.MergeJson("{\"Customer\":{\"Name\":\"Ada\"}}", cancellationToken: TestContext.Current.CancellationToken));
        Assert.All(document.Paragraphs, paragraph => Assert.Equal("cached", paragraph.Text));
        Assert.Equal(2, document.MergeJson("{\"Customer\":{\"Name\":\"Ada\",\"Code\":\"Q42\"}}", cancellationToken: TestContext.Current.CancellationToken).Changes);
        Assert.Collection(document.Paragraphs, paragraph => Assert.Equal("Ada", paragraph.Text), paragraph => Assert.Equal("Q42", paragraph.Text));
    }

    [Fact]
    public void InvalidSimpleControlPathsIdentifyTheirSourceWithoutExposingNames()
    {
        string repeat = RootRepeat("Bad-Name", "<w:p><w:r><w:t>cached</w:t></w:r></w:p>");
        byte[] bytes = Document(InlineControl("merge:Bad-Name") + repeat);
        using var source = new MemoryStream(bytes);
        TemplateInspection report = new WordConverter().InspectTemplate(source, new TemplateDataSchema(), DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        TemplateInspectionFinding[] findings = report.Findings.Where(f => f.Code == "Plan.InvalidControlPath").ToArray();
        Assert.Equal(2, findings.Length);
        Assert.Contains(findings, f => string.Join(",", f.SourceElementIndices!) == "1,1,1,1");
        Assert.Contains(findings, f => string.Join(",", f.SourceElementIndices!) == "1,1,2");
        Assert.All(findings, f => { Assert.Null(f.PathId); Assert.Equal("word/document.xml", report.GetSensitivePartAliases()[f.PartId!]); });
        string json = JsonSerializer.Serialize(report); Assert.DoesNotContain("Bad-Name", json, StringComparison.Ordinal); Assert.DoesNotContain("word/document.xml", json, StringComparison.Ordinal);
        source.Position = 0;
        var document = WordDocument.Load(source, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        Assert.Throws<InvalidDataException>(() => document.MergeJson("{}", cancellationToken: TestContext.Current.CancellationToken));
        Assert.All(document.Paragraphs, paragraph => Assert.Equal("cached", paragraph.Text));
    }

    [Fact]
    public void SimpleRootRepeatChecksCollectionPathWithoutInspectingItsContents()
    {
        byte[] bytes = Document(RootRepeat("Items", "<w:p><w:r><w:t>cached</w:t></w:r></w:p>"));
        using var source = new MemoryStream(bytes);
        var converter = new WordConverter();
        TemplateInspection missing = converter.InspectTemplate(source, new TemplateDataSchema(), DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        TemplateInspectionFinding finding = Assert.Single(missing.Findings, f => f.Code == "Plan.MissingPath");
        Assert.Equal("Items", missing.GetSensitivePathAliases()[finding.PathId!]); Assert.Equal("1,1,1", string.Join(",", finding.SourceElementIndices!));
        Assert.Contains(missing.Findings, f => f.Code == "Plan.RepeatScope" && f.Unknown);
        source.Position = 0;
        var schema = new TemplateDataSchema(new TemplateSchemaPath("Items", TemplatePathType.Collection));
        TemplateInspection valid = converter.InspectTemplate(source, schema, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        TemplateInspectionRequirement requirement = Assert.Single(valid.Requirements);
        Assert.False(requirement.ScopeUnknown); Assert.Equal(TemplatePathType.Collection, requirement.DeclaredType);
        Assert.DoesNotContain(valid.Findings, f => f.Code is "Plan.MissingPath" or "Plan.PathType");
        Assert.DoesNotContain("Items", JsonSerializer.Serialize(valid), StringComparison.Ordinal);
        source.Position = 0;
        var document = WordDocument.Load(source, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        Assert.Throws<InvalidDataException>(() => document.MergeJson("{}", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("cached", Assert.Single(document.Paragraphs).Text);
        Assert.Equal(1, document.MergeJson("{\"Items\":[{},{}]}", cancellationToken: TestContext.Current.CancellationToken).Changes);
        Assert.Collection(document.Paragraphs, paragraph => Assert.Equal("cached", paragraph.Text), paragraph => Assert.Equal("cached", paragraph.Text));
    }

    [Fact]
    public void RootRepeatTypeMismatchMatchesMergeAndOutsideFieldUsesRootScope()
    {
        string repeat = RootRepeat("Items", "<w:p><w:r><w:t>cached</w:t></w:r></w:p>");
        var schema = new TemplateDataSchema(new TemplateSchemaPath("Items", TemplatePathType.Scalar));
        using var source = new MemoryStream(Document(repeat));
        TemplateInspection report = new WordConverter().InspectTemplate(source, schema, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(report.Findings, f => f.Code == "Plan.PathType" && report.GetSensitivePathAliases()[f.PathId!] == "Items");
        source.Position = 0;
        var document = WordDocument.Load(source, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => document.MergeJson("{\"Items\":1}", cancellationToken: TestContext.Current.CancellationToken));
        Assert.StartsWith("Template.Collection:", error.Message, StringComparison.Ordinal); Assert.Equal("cached", Assert.Single(document.Paragraphs).Text);
        using var mixed = new MemoryStream(Document(repeat + Field("MERGEFIELD Items")));
        TemplateInspection mixedReport = new WordConverter().InspectTemplate(mixed, schema, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        TemplateInspectionRequirement mixedRequirement = Assert.Single(mixedReport.Requirements);
        Assert.Equal(2, mixedRequirement.Count); Assert.False(mixedRequirement.ScopeUnknown); Assert.Equal(TemplatePathType.Scalar, mixedRequirement.DeclaredType);
        string multiple = RootRepeat("Other", "<w:p><w:r><w:t>a</w:t></w:r></w:p><w:p><w:r><w:t>b</w:t></w:r></w:p>");
        using var unsupported = new MemoryStream(Document(multiple));
        TemplateInspection unknown = new WordConverter().InspectTemplate(unsupported, new TemplateDataSchema(), DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(unknown.Requirements); Assert.DoesNotContain(unknown.Findings, f => f.Code == "Plan.MissingPath");
        Assert.Contains(unknown.Findings, f => f.Code == "Plan.RepeatScope" && f.Unknown);
    }

    [Fact]
    public void IndependentRootFieldsAndControlsRemainInspectableWhenARepeatExists()
    {
        string repeat = RootRepeat("Items", Field("MERGEFIELD Inside"));
        byte[] bytes = Document(repeat + Field("MERGEFIELD Outside") + Table(InlineControl("merge:Cell")) + InlineControl("merge:Body"));
        using var source = new MemoryStream(bytes);
        TemplateInspection report = new WordConverter().InspectTemplate(source, new TemplateDataSchema(), DocumentFormat.Docx,
            cancellationToken: TestContext.Current.CancellationToken);
        string[] missing = report.Findings.Where(f => f.Code == "Plan.MissingPath").Select(f => report.GetSensitivePathAliases()[f.PathId!]).OrderBy(s => s).ToArray();
        Assert.Equal("Body,Cell,Items,Outside", string.Join(",", missing));
        TemplateInspectionRequirement inside = Assert.Single(report.Requirements, requirement => requirement.ScopeUnknown);
        Assert.Equal("Inside", report.GetSensitivePathAliases()[inside.PathId]);
        Assert.Null(inside.DeclaredType); Assert.Null(inside.Required);
        Assert.Contains(report.Findings, f => f.Code == "Plan.RepeatFieldScopeUnknown" && f.Unknown && f.PathId == inside.PathId);
        Assert.Contains(report.Findings, f => f.Code == "Plan.RepeatScope" && f.Unknown);
        Assert.Contains(report.Findings, f => f.Code == "Plan.OwnerCoverage" && f.Unknown);
        string json = JsonSerializer.Serialize(report);
        Assert.DoesNotContain("Outside", json, StringComparison.Ordinal); Assert.DoesNotContain("Inside", json, StringComparison.Ordinal);
        Assert.DoesNotContain("word/document.xml", json, StringComparison.Ordinal);
        source.Position = 0;
        var document = WordDocument.Load(source, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        Assert.Throws<InvalidDataException>(() => document.MergeJson("{\"Items\":[{\"Inside\":\"inner\"}]}", cancellationToken: TestContext.Current.CancellationToken));
        Assert.All(document.Paragraphs, paragraph => Assert.Equal("cached", paragraph.Text));
        document.MergeJson("{\"Items\":[{\"Inside\":\"inner\"}],\"Outside\":\"outer\",\"Cell\":\"cell\",\"Body\":\"body\"}", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("inner,outer,cell,body", string.Join(",", document.Paragraphs.Select(paragraph => paragraph.Text)));
    }

    [Fact]
    public void RepeatedFieldAndRootFieldKeepSeparateScopeEvidence()
    {
        byte[] bytes = Document(Field("MERGEFIELD Name") + RootRepeat("Items", Field("MERGEFIELD Name")));
        var schema = new TemplateDataSchema(new TemplateSchemaPath("Items", TemplatePathType.Collection), new TemplateSchemaPath("Name", TemplatePathType.Scalar));
        using var source = new MemoryStream(bytes);
        TemplateInspection report = new WordConverter().InspectTemplate(source, schema, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        TemplateInspectionRequirement[] names = report.Requirements.Where(requirement => report.GetSensitivePathAliases()[requirement.PathId] == "Name").ToArray();
        Assert.Equal(2, names.Length); Assert.Equal(names[0].PathId, names[1].PathId);
        TemplateInspectionRequirement root = Assert.Single(names, requirement => !requirement.ScopeUnknown);
        TemplateInspectionRequirement repeated = Assert.Single(names, requirement => requirement.ScopeUnknown);
        Assert.Equal(TemplatePathType.Scalar, root.DeclaredType); Assert.True(root.Required); Assert.Equal(1, root.Count);
        Assert.Null(repeated.DeclaredType); Assert.Null(repeated.Required); Assert.Equal(1, repeated.Count);
        TemplateInspectionFinding finding = Assert.Single(report.Findings, item => item.Code == "Plan.RepeatFieldScopeUnknown");
        Assert.True(finding.Unknown); Assert.Equal(repeated.PathId, finding.PathId);
        Assert.Equal("1,1,2,2,1,1", string.Join(",", finding.SourceElementIndices!));
        Assert.DoesNotContain(report.Findings, item => item.Code is "Plan.MissingPath" or "Plan.PathType");
        string json = JsonSerializer.Serialize(report);
        Assert.DoesNotContain("Name", json, StringComparison.Ordinal); Assert.DoesNotContain("Items", json, StringComparison.Ordinal);
        Assert.DoesNotContain("word/document.xml", json, StringComparison.Ordinal);
        source.Position = 0;
        WordDocument document = WordDocument.Load(source, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        document.MergeJson("{\"Name\":\"root\",\"Items\":[{\"Name\":\"child\"},{\"Other\":1}]}", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("root,child,root", string.Join(",", document.Paragraphs.Select(paragraph => paragraph.Text)));
    }

    [Fact]
    public void RequiredRepeatItemDeclarationResolvesItsOwnScopeWithoutConflatingRoot()
    {
        byte[] bytes = Document(Field("MERGEFIELD Name") + RootRepeat("Items", Field("MERGEFIELD Name")));
        var schema = new TemplateDataSchema(new TemplateSchemaPath("Items", TemplatePathType.Collection),
            new TemplateSchemaPath("Name", TemplatePathType.Scalar), TemplateSchemaPath.ForRepeatItem("Items", "Name", TemplatePathType.Scalar));
        using var source = new MemoryStream(bytes);
        TemplateInspection report = new WordConverter().InspectTemplate(source, schema, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        TemplateInspectionRequirement[] names = report.Requirements.Where(requirement => report.GetSensitivePathAliases()[requirement.PathId] == "Name").ToArray();
        Assert.Equal(2, names.Length); Assert.Equal(names[0].PathId, names[1].PathId);
        TemplateInspectionRequirement root = Assert.Single(names, requirement => requirement.ScopePathId == null);
        TemplateInspectionRequirement item = Assert.Single(names, requirement => requirement.ScopePathId != null);
        Assert.False(root.ScopeUnknown); Assert.False(item.ScopeUnknown); Assert.True(item.Required);
        Assert.Equal(TemplatePathType.Scalar, item.DeclaredType); Assert.Equal(1, item.Count);
        Assert.Equal("Items", report.GetSensitivePathAliases()[item.ScopePathId!]);
        Assert.DoesNotContain(report.Findings, finding => finding.Code == "Plan.RepeatFieldScopeUnknown");
        Assert.Contains(report.Findings, finding => finding.Code == "Plan.RepeatScope" && finding.Unknown);
        string json = JsonSerializer.Serialize(report);
        Assert.DoesNotContain("Name", json, StringComparison.Ordinal); Assert.DoesNotContain("Items", json, StringComparison.Ordinal);
        source.Position = 0;
        WordDocument document = WordDocument.Load(source, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        document.MergeJson("{\"Name\":\"root\",\"Items\":[{\"Name\":\"first\"},{\"Name\":\"second\"}]}", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("root,first,second", string.Join(",", document.Paragraphs.Select(paragraph => paragraph.Text)));
    }

    [Fact]
    public void OptionalRepeatItemStillHasUnknownFallbackScope()
    {
        byte[] bytes = Document(RootRepeat("Items", Field("MERGEFIELD Name")));
        var schema = new TemplateDataSchema(new TemplateSchemaPath("Items", TemplatePathType.Collection),
            new TemplateSchemaPath("Name", TemplatePathType.Scalar), TemplateSchemaPath.ForRepeatItem("Items", "Name", TemplatePathType.Scalar, required: false));
        using var source = new MemoryStream(bytes);
        TemplateInspection report = new WordConverter().InspectTemplate(source, schema, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        TemplateInspectionRequirement item = Assert.Single(report.Requirements, requirement => report.GetSensitivePathAliases()[requirement.PathId] == "Name");
        Assert.True(item.ScopeUnknown); Assert.Null(item.DeclaredType); Assert.Null(item.Required);
        Assert.Equal("Items", report.GetSensitivePathAliases()[item.ScopePathId!]);
        Assert.Contains(report.Findings, finding => finding.Code == "Plan.RepeatFieldScopeUnknown" && finding.Unknown && finding.ScopePathId == item.ScopePathId);
        source.Position = 0;
        WordDocument document = WordDocument.Load(source, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        document.MergeJson("{\"Name\":\"root\",\"Items\":[{\"Name\":\"child\"},{\"Other\":1}]}", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("child,root", string.Join(",", document.Paragraphs.Select(paragraph => paragraph.Text)));
    }

    [Fact]
    public void RepeatsWithTheSameFieldKeepIndependentCollectionScopes()
    {
        byte[] bytes = Document(RootRepeat("Items", Field("MERGEFIELD Name")) + RootRepeat("Orders", Field("MERGEFIELD Name")));
        var schema = new TemplateDataSchema(new TemplateSchemaPath("Items", TemplatePathType.Collection), new TemplateSchemaPath("Orders", TemplatePathType.Collection),
            new TemplateSchemaPath("Name", TemplatePathType.Scalar), TemplateSchemaPath.ForRepeatItem("Items", "Name", TemplatePathType.Scalar),
            TemplateSchemaPath.ForRepeatItem("Orders", "Name", TemplatePathType.Scalar, required: false));
        using var source = new MemoryStream(bytes);
        TemplateInspection report = new WordConverter().InspectTemplate(source, schema, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        TemplateInspectionRequirement[] names = report.Requirements.Where(requirement => report.GetSensitivePathAliases()[requirement.PathId] == "Name").ToArray();
        Assert.Equal(2, names.Length); Assert.Equal(names[0].PathId, names[1].PathId);
        TemplateInspectionRequirement items = Assert.Single(names, requirement => report.GetSensitivePathAliases()[requirement.ScopePathId!] == "Items");
        TemplateInspectionRequirement orders = Assert.Single(names, requirement => report.GetSensitivePathAliases()[requirement.ScopePathId!] == "Orders");
        Assert.False(items.ScopeUnknown); Assert.True(orders.ScopeUnknown); Assert.Equal(1, items.Count); Assert.Equal(1, orders.Count);
        Assert.Contains(report.Findings, finding => finding.Code == "Plan.RepeatFieldScopeUnknown" && finding.ScopePathId == orders.ScopePathId);
        source.Position = 0;
        WordDocument document = WordDocument.Load(source, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        document.MergeJson("{\"Name\":\"root\",\"Items\":[{\"Name\":\"first\"}],\"Orders\":[{\"Name\":\"second\"},{\"Other\":1}]}", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("first,second,root", string.Join(",", document.Paragraphs.Select(paragraph => paragraph.Text)));
    }

    [Fact]
    public void RepeatItemTypeMismatchIsUncertainWhenTheCollectionMayBeEmpty()
    {
        byte[] bytes = Document(RootRepeat("Items", Field("MERGEFIELD Amount")));
        var schema = new TemplateDataSchema(new TemplateSchemaPath("Items", TemplatePathType.Collection),
            TemplateSchemaPath.ForRepeatItem("Items", "Amount", TemplatePathType.Mapping));
        using var source = new MemoryStream(bytes);
        TemplateInspection report = new WordConverter().InspectTemplate(source, schema, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        TemplateInspectionFinding finding = Assert.Single(report.Findings, item => item.Code == "Plan.RepeatFieldTypeMismatch");
        Assert.True(finding.Unknown); Assert.Equal("Items", report.GetSensitivePathAliases()[finding.ScopePathId!]);
        Assert.DoesNotContain(report.Findings, item => item.Code == "Plan.RepeatFieldScopeUnknown");
        source.Position = 0;
        WordDocument document = WordDocument.Load(source, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        document.MergeJson("{\"Items\":[]}", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(document.Paragraphs);
        source.Position = 0;
        WordDocument populated = WordDocument.Load(source, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => populated.MergeJson("{\"Items\":[{\"Amount\":{}}]}",
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.StartsWith("Template.Scalar:", error.Message, StringComparison.Ordinal);
        Assert.Equal("cached", Assert.Single(populated.Paragraphs).Text);
        Assert.Throws<ArgumentException>(() => TemplateSchemaPath.ForRepeatItem("Items", "Nested.Value", TemplatePathType.Scalar));
        var duplicate = TemplateSchemaPath.ForRepeatItem("Items", "Amount", TemplatePathType.Mapping);
        Assert.Throws<ArgumentException>(() => new TemplateDataSchema(duplicate, duplicate));
    }

    [Fact]
    public void UnparseableRepeatedFieldsRemainUnknownWhenTheirCollectionMayBeEmpty()
    {
        byte[] bytes = Document(RootRepeat("Items", Field("MERGEFIELD Bad-Name")));
        var schema = new TemplateDataSchema(new TemplateSchemaPath("Items", TemplatePathType.Collection));
        using var source = new MemoryStream(bytes);
        TemplateInspection report = new WordConverter().InspectTemplate(source, schema, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(report.Findings, finding => finding.Code == "Plan.FieldCoverage" && finding.Unknown && finding.SourceElementIndices != null);
        Assert.DoesNotContain(report.Findings, finding => finding.Code == "Plan.InvalidField");
        Assert.DoesNotContain(report.GetSensitivePathAliases().Values, path => path == "Bad-Name");
        source.Position = 0;
        WordDocument document = WordDocument.Load(source, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        document.MergeJson("{\"Items\":[]}", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(document.Paragraphs);
    }

    [Fact]
    public void FlattenedWrapperFieldsDoNotGainAnAssumedRootScope()
    {
        string repeat = RootRepeat("Items", "<w:p><w:r><w:t>cached</w:t></w:r></w:p>");
        string wrapped = "<w:customXml w:element='value'>" + Field("MERGEFIELD Wrapped") + "</w:customXml>";
        using var source = new MemoryStream(Document(repeat + wrapped));
        TemplateInspection report = new WordConverter().InspectTemplate(source, new TemplateDataSchema(), DocumentFormat.Docx,
            new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        TemplateInspectionRequirement wrappedRequirement = Assert.Single(report.Requirements, r => report.GetSensitivePathAliases()[r.PathId] == "Wrapped");
        Assert.True(wrappedRequirement.ScopeUnknown); Assert.Null(wrappedRequirement.DeclaredType);
        Assert.DoesNotContain(report.Findings, f => f.Code == "Plan.MissingPath" && f.PathId != null && report.GetSensitivePathAliases()[f.PathId] == "Wrapped");
    }

    [Fact]
    public void SimpleInlineControlAndFieldRequirementsMatchMergeWithoutExposingNames()
    {
        byte[] bytes = Document(InlineControl("merge:Customer.Name") + Field("MERGEFIELD Customer.Name"));
        var schema = new TemplateDataSchema(new TemplateSchemaPath("Customer", TemplatePathType.Mapping));
        using var source = new MemoryStream(bytes);
        TemplateInspection report = new WordConverter().InspectTemplate(source, schema, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        TemplateInspectionRequirement requirement = Assert.Single(report.Requirements);
        Assert.Equal(2, requirement.Count); Assert.Equal("Customer.Name", report.GetSensitivePathAliases()[requirement.PathId]);
        TemplateInspectionFinding[] missing = report.Findings.Where(f => f.Code == "Plan.MissingPath").ToArray();
        Assert.Equal(2, missing.Length); Assert.Equal("1,1,1,1", string.Join(",", missing[0].SourceElementIndices!));
        Assert.Contains(report.Findings, f => f.Code == "Plan.ControlCoverage" && f.Unknown);
        Assert.DoesNotContain("Customer", JsonSerializer.Serialize(report), StringComparison.Ordinal);
        source.Position = 0;
        var document = WordDocument.Load(source, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        Assert.Throws<InvalidDataException>(() => document.MergeJson("{\"Customer\":{}}", cancellationToken: TestContext.Current.CancellationToken));
        Assert.All(document.Paragraphs, paragraph => Assert.Equal("cached", paragraph.Text));
        Assert.Equal(2, document.MergeJson("{\"Customer\":{\"Name\":\"Ada\"}}", cancellationToken: TestContext.Current.CancellationToken).Changes);
        Assert.All(document.Paragraphs, paragraph => Assert.Equal("Ada", paragraph.Text));
    }

    [Theory]
    [InlineData("merge:Value", TemplatePathType.Collection, "{\"Value\":[1]}", "Template.Scalar")]
    [InlineData("image:Value", TemplatePathType.Scalar, "{\"Value\":\"not an image\"}", "Template.Image")]
    public void SimpleInlineControlTypeFindingsMatchMergeFailures(string tag, TemplatePathType type, string data, string error)
    {
        byte[] bytes = Document(InlineControl(tag));
        using var source = new MemoryStream(bytes);
        var schema = new TemplateDataSchema(new TemplateSchemaPath("Value", type));
        TemplateInspection report = new WordConverter().InspectTemplate(source, schema, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        TemplateInspectionFinding finding = Assert.Single(report.Findings, f => f.Code == "Plan.PathType");
        Assert.Equal("Value", report.GetSensitivePathAliases()[finding.PathId!]); Assert.Equal("1,1,1,1", string.Join(",", finding.SourceElementIndices!));
        source.Position = 0;
        var document = WordDocument.Load(source, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => document.MergeJson(data, cancellationToken: TestContext.Current.CancellationToken));
        Assert.StartsWith(error, exception.Message, StringComparison.Ordinal); Assert.Equal("cached", Assert.Single(document.Paragraphs).Text);
    }

    [Fact]
    public void DeclaredImageControlCanBindCallerImageBytes()
    {
        byte[] bytes = Document(InlineControl("image:Logo"));
        using var source = new MemoryStream(bytes);
        var schema = new TemplateDataSchema(new TemplateSchemaPath("Logo", TemplatePathType.Image));
        TemplateInspection report = new WordConverter().InspectTemplate(source, schema, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        Assert.DoesNotContain(report.Findings, f => f.Code is "Plan.MissingPath" or "Plan.PathType");
        Assert.Equal(TemplatePathType.Image, Assert.Single(report.Requirements).DeclaredType);
        source.Position = 0;
        var document = WordDocument.Load(source, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        byte[] pixels = FidelityDocuments.Fixture("color-grid.png");
        var image = new TemplateImage(pixels, 60, 30, "Synthetic logo");
        Assert.Equal(1, document.Merge(new { Logo = image }, cancellationToken: TestContext.Current.CancellationToken).Changes);
        using var output = new MemoryStream(); document.Save(output, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        output.Position = 0; using var package = new ZipArchive(output, ZipArchiveMode.Read, true);
        ZipArchiveEntry media = Assert.Single(package.Entries, entry => entry.FullName.StartsWith("word/media/", StringComparison.Ordinal));
        using var embedded = media.Open(); using var copied = new MemoryStream(); embedded.CopyTo(copied);
        Assert.Equal(pixels, copied.ToArray());
        using var xml = new StreamReader(package.GetEntry("word/document.xml")!.Open());
        Assert.Contains("drawing", xml.ReadToEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void InlineConditionsUseTheDeclaredPathButComplexControlsStayUnknown()
    {
        byte[] bytes = Document(InlineControl("if:!Show"));
        using var source = new MemoryStream(bytes);
        var schema = new TemplateDataSchema(new TemplateSchemaPath("Show", TemplatePathType.Collection));
        TemplateInspection report = new WordConverter().InspectTemplate(source, schema, DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("Show", report.GetSensitivePathAliases()[Assert.Single(report.Requirements).PathId]);
        Assert.DoesNotContain(report.Findings, f => f.Code is "Plan.MissingPath" or "Plan.PathType");
        source.Position = 0;
        var document = WordDocument.Load(source, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        Assert.Equal(1, document.MergeJson("{\"Show\":[]}", cancellationToken: TestContext.Current.CancellationToken).Changes);
        Assert.Equal("cached", Assert.Single(document.Paragraphs).Text);
        string formatted = "<w:p><w:sdt><w:sdtPr><w:tag w:val='merge:Hidden'/></w:sdtPr><w:sdtContent><w:r><w:rPr><w:b/></w:rPr><w:t>cached</w:t></w:r></w:sdtContent></w:sdt></w:p>";
        using var complex = new MemoryStream(Document(formatted));
        TemplateInspection unknown = new WordConverter().InspectTemplate(complex, new TemplateDataSchema(), DocumentFormat.Docx, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(unknown.Requirements); Assert.DoesNotContain(unknown.Findings, f => f.Code == "Plan.MissingPath");
        Assert.Contains(unknown.Findings, f => f.Code == "Plan.ControlCoverage" && f.Unknown);
    }

    [Fact]
    public void NestedAndMergedCellInlineControlsRemainUninspected()
    {
        string nested = "<w:p><w:sdt><w:sdtPr><w:tag w:val='if:Outer'/></w:sdtPr><w:sdtContent>"
            + InlineControl("merge:Inner").Replace("<w:p>", "").Replace("</w:p>", "") + "</w:sdtContent></w:sdt></w:p>";
        string merged = "<w:tbl><w:tblGrid><w:gridCol w:w='1500'/><w:gridCol w:w='1500'/></w:tblGrid><w:tr><w:tc><w:tcPr><w:gridSpan w:val='2'/></w:tcPr>"
            + InlineControl("merge:Cell") + "</w:tc></w:tr></w:tbl>";
        using var source = new MemoryStream(Document(nested + merged));
        TemplateInspection report = new WordConverter().InspectTemplate(source, new TemplateDataSchema(), DocumentFormat.Docx,
            new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        Assert.Empty(report.Requirements); Assert.DoesNotContain(report.Findings, f => f.Code == "Plan.MissingPath");
        Assert.Contains(report.Findings, f => f.Code == "Plan.ControlCoverage" && f.Unknown);
        Assert.Contains(report.Findings, f => f.Code == "Plan.OwnerCoverage" && f.Unknown);
    }

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
        using var source = new MemoryStream(Document(repeat + InlineControl("merge:Outside") + "<w:tbl><w:tr><w:tc><w:p/></w:tc></w:tr></w:tbl>"));
        TemplateInspection report = new WordConverter().InspectTemplate(source, new TemplateDataSchema(), DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(report.Findings, f => f.Code == "Plan.RepeatScope" && f.Unknown);
        Assert.Contains(report.Findings, f => f.Code == "Plan.OwnerCoverage" && f.Unknown);
        string[] missing = report.Findings.Where(f => f.Code == "Plan.MissingPath").Select(f => report.GetSensitivePathAliases()[f.PathId!]).OrderBy(path => path).ToArray();
        Assert.Equal("Items,Outside", string.Join(",", missing));
        Assert.Contains(report.Requirements, requirement => requirement.ScopeUnknown && report.GetSensitivePathAliases()[requirement.PathId] == "Name");
        Assert.Contains(report.Findings, finding => finding.Code == "Plan.RepeatFieldScopeUnknown" && finding.Unknown);
        TemplateInspectionRequirement requirement = Assert.Single(report.Requirements, item => report.GetSensitivePathAliases()[item.PathId] == "Outside");
        Assert.False(requirement.ScopeUnknown);
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
    private static string RootRepeat(string path, string content) => "<w:sdt><w:sdtPr><w:tag w:val='repeat:" + path + "'/></w:sdtPr><w:sdtContent>" + content + "</w:sdtContent></w:sdt>";
    private static string InlineControl(string tag) => "<w:p><w:sdt><w:sdtPr><w:tag w:val='" + tag + "'/></w:sdtPr><w:sdtContent><w:r><w:t>cached</w:t></w:r></w:sdtContent></w:sdt></w:p>";
    private static string Field(string instruction) => "<w:p><w:fldSimple w:instr='" + instruction + "'><w:r><w:t>cached</w:t></w:r></w:fldSimple></w:p>";
    private static string Table(string content) => "<w:tbl><w:tblPr><w:tblW w:w='3000' w:type='dxa'/><w:tblLayout w:type='fixed'/></w:tblPr>"
        + "<w:tblGrid><w:gridCol w:w='3000'/></w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:w='3000' w:type='dxa'/></w:tcPr>" + content + "</w:tc></w:tr></w:tbl>";
}
