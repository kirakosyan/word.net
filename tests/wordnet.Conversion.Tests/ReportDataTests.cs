using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using WordNet;
using Xunit;

namespace WordNet.Tests;

public sealed class ReportDataTests
{
    private static readonly int[] ReportExpected0 = new[] { 2, 4 };
    private static readonly int[] ReportExpected1 = new[] { 1, 2, 3, 4 };
    private static readonly string[] ReportExpected2 = new[] { "a", "c", "d", "b" };
    private static readonly string[] ReportExpected3 = new[] { "b", "d", "a", "c" };
    private static ReportExpression N(decimal value) => ReportExpression.Literal(value);
    private static ReportExpression P(string path) => ReportExpression.Path(path);
    private static ReportExpression B(ReportOperation operation, ReportExpression left, ReportExpression right) => ReportExpression.Binary(operation, left, right);
    private static IReadOnlyDictionary<string, object?> Run(object data, params ReportField[] fields) => ReportData.Prepare(data, new ReportPlan(fields: fields), cancellationToken: TestContext.Current.CancellationToken).Values;

    [Fact]
    public void StableOrdinalGroupsHaveIndependentSubtotalsAndNeverMutateInput()
    {
        var input = new[] { new { Category = "b", Amount = (decimal?)3, Id = 1 }, new { Category = "A", Amount = (decimal?)2, Id = 2 },
            new { Category = "b", Amount = (decimal?)null, Id = 3 }, new { Category = "A", Amount = (decimal?)-1, Id = 4 } };
        var operation = new ReportCollectionOperation("Groups", "Items", "Category", "Category", aggregates: new[] {
            new ReportAggregate("Count", ReportAggregateKind.Count), new ReportAggregate("Sum", ReportAggregateKind.Sum, "Amount"),
            new ReportAggregate("Min", ReportAggregateKind.Minimum, "Amount"), new ReportAggregate("Max", ReportAggregateKind.Maximum, "Amount") });
        ReportData result = ReportData.Prepare(new { Items = input }, new ReportPlan(new[] { operation }), cancellationToken: TestContext.Current.CancellationToken);
        var groups = (IReadOnlyList<object?>)result.Values["Groups"]!;
        var first = (IReadOnlyDictionary<string, object?>)groups[0]!;
        Assert.Equal("A", first["Key"]); Assert.Equal(2, first["Count"]); Assert.Equal(1m, first["Sum"]);
        Assert.Equal(-1m, first["Min"]); Assert.Equal(2m, first["Max"]);
        Assert.Equal(ReportExpected0, ((IReadOnlyList<object?>)first["Rows"]!).Cast<IReadOnlyDictionary<string, object?>>().Select(r => (int)r["Id"]!));
        Assert.Equal(ReportExpected1, input.Select(r => r.Id));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, object?>)first).Add("Unexpected", 1));
    }

    [Fact]
    public void NullGroupKeysAndAllNullAggregatesAreExplicit()
    {
        var operation = new ReportCollectionOperation("Groups", "Rows", "Key", aggregates: new[] {
            new ReportAggregate("Sum", ReportAggregateKind.Sum, "Value"), new ReportAggregate("Min", ReportAggregateKind.Minimum, "Value") });
        var plan = new ReportPlan(new[] { operation });
        var empty = ReportData.Prepare(new { Rows = Array.Empty<object>() }, plan, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty((IReadOnlyList<object?>)empty.Values["Groups"]!);
        var result = ReportData.Prepare(new { Rows = new[] { new { Key = (string?)null, Value = (decimal?)null } } }, plan, cancellationToken: TestContext.Current.CancellationToken);
        var group = (IReadOnlyDictionary<string, object?>)((IReadOnlyList<object?>)result.Values["Groups"]!)[0]!;
        Assert.Null(group["Key"]); Assert.Equal(0m, group["Sum"]); Assert.Null(group["Min"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SortingKeepsEqualKeysInInputOrder(bool descending)
    {
        var result = ReportData.Prepare(new { Rows = new[] { new { Key = (int?)2, Id = "a" }, new { Key = (int?)null, Id = "b" }, new { Key = (int?)2, Id = "c" }, new { Key = (int?)1, Id = "d" } } },
            new ReportPlan(new[] { new ReportCollectionOperation("Sorted", "Rows", sortBy: "Key", descending: descending) }), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(descending ? ReportExpected2 : ReportExpected3,
            ((IReadOnlyList<object?>)result.Values["Sorted"]!).Cast<IReadOnlyDictionary<string, object?>>().Select(r => (string)r["Id"]!));
    }

    [Theory]
    [InlineData(100, 7.25, 107.25)]
    [InlineData(-100, -7.25, -107.25)]
    [InlineData(0, 0, 0)]
    public void InvoiceComputationsResolveForwardDependencies(double net, double tax, double total)
    {
        var values = Run(new { Net = (decimal)net }, new ReportField("Total", B(ReportOperation.Add, P("Net"), P("Tax"))),
            new ReportField("Tax", ReportExpression.Round(B(ReportOperation.Multiply, P("Net"), N(0.0725m)), 2, MidpointRounding.AwayFromZero)));
        Assert.Equal((decimal)tax, values["Tax"]); Assert.Equal((decimal)total, values["Total"]);
    }

    [Theory]
    [InlineData(MidpointRounding.ToEven, 1.22)]
    [InlineData(MidpointRounding.AwayFromZero, 1.23)]
    public void MidpointPolicyIsExplicit(MidpointRounding rounding, double expected)
    { Assert.Equal((decimal)expected, Run(new { }, new ReportField("Rounded", ReportExpression.Round(N(1.225m), 2, rounding)))["Rounded"]); }

    [Theory]
    [InlineData(ReportOperation.Add)]
    [InlineData(ReportOperation.Multiply)]
    public void CheckedOverflowDoesNotPublishPartialResults(ReportOperation operation)
    {
        var data = new Dictionary<string, object?> { ["Value"] = decimal.MaxValue };
        Assert.Throws<OverflowException>(() => Run(data, new ReportField("Good", N(1)), new ReportField("Bad", B(operation, P("Value"), N(2)))));
        Assert.Single(data); Assert.Equal(decimal.MaxValue, data["Value"]);
    }

    [Fact]
    public void CyclesMissingPathsNullsAndZeroDivisorsFailWithoutCallerChanges()
    {
        var input = new Dictionary<string, object?> { ["Value"] = null };
        Assert.Throws<InvalidDataException>(() => Run(input, new ReportField("A", P("B")), new ReportField("B", P("A"))));
        Assert.Throws<InvalidDataException>(() => Run(input, new ReportField("A", P("Missing"))));
        Assert.Throws<InvalidDataException>(() => Run(input, new ReportField("A", B(ReportOperation.Add, P("Value"), N(1)))));
        Assert.Throws<DivideByZeroException>(() => Run(input, new ReportField("A", B(ReportOperation.Divide, N(1), N(0)))));
        Assert.Single(input); Assert.Null(input["Value"]);
        Assert.Throws<ArgumentException>(() => new ReportPlan(fields: new[] { new ReportField("A", N(1)), new ReportField("A", N(2)) }));
    }

    [Theory]
    [InlineData("Rows.[0]")]
    [InlineData("Rows.GetType()")]
    [InlineData("Rows.Å")]
    [InlineData("")]
    public void InvalidPathsAreRejected(string path) => Assert.Throws<ArgumentException>(() => P(path));

    [Fact]
    public void MixedKeysFloatingPointAndLocalDatesAreRejected()
    {
        var plan = new ReportPlan(new[] { new ReportCollectionOperation("Sorted", "Rows", sortBy: "Key") });
        foreach (object key in new object[] { "1", 1.0, DateTime.Now })
            Assert.Throws<InvalidDataException>(() => ReportData.Prepare(new { Rows = new[] { new { Key = (object)1 }, new { Key = key } } }, plan, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(() => ReportExpression.Literal(1.0));
    }

    [Fact]
    public void TypedPredicatesHandleOrdinalNullAndOffsetDatesAndShortCircuit()
    {
        var date = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(2));
        var values = Run(new { },
            new ReportField("NullEqual", B(ReportOperation.Equal, ReportExpression.Literal(null), ReportExpression.Literal(null))),
            new ReportField("Ordinal", B(ReportOperation.Equal, ReportExpression.Literal("a"), ReportExpression.Literal("A"))),
            new ReportField("DateEqual", B(ReportOperation.Equal, ReportExpression.Literal(date), ReportExpression.Literal(date.ToUniversalTime()))),
            new ReportField("DateLess", B(ReportOperation.Less, ReportExpression.Literal(date), ReportExpression.Literal(date.AddMinutes(1)))),
            new ReportField("Short", B(ReportOperation.Or, ReportExpression.Literal(true), P("Missing"))),
            new ReportField("Nested", ReportExpression.Not(B(ReportOperation.And, ReportExpression.Literal(false), P("Missing")))));
        Assert.Equal(true, values["NullEqual"]); Assert.Equal(false, values["Ordinal"]); Assert.Equal(true, values["DateEqual"]);
        Assert.Equal(true, values["DateLess"]); Assert.Equal(true, values["Short"]); Assert.Equal(true, values["Nested"]);
        Assert.Throws<InvalidDataException>(() => Run(new { }, new ReportField("Bad", B(ReportOperation.Equal, N(1), ReportExpression.Literal("1")))));
    }

    [Fact]
    public void SnapshotInvokesEachGetterOnceAndResultsMergeWithExistingGrammar()
    {
        var input = new GetterData();
        var result = ReportData.Prepare(input, new ReportPlan(fields: new[] { new ReportField("Total", B(ReportOperation.Add, P("Amount"), P("Amount"))) }), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, input.Calls);
        byte[] template = TestDocuments.Docx(TestDocuments.Body("<w:p><w:fldSimple w:instr='MERGEFIELD Total'><w:r><w:t>cached</w:t></w:r></w:fldSimple></w:p>"));
        using var stream = new MemoryStream(template);
        WordDocument doc = WordDocument.Load(stream, DocumentFormat.Docx, out _, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
        doc.Merge(result.Values, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("24", doc.Paragraphs[0].Text);
    }
    private sealed class GetterData
    {
        internal int Calls;
        public decimal Amount { get { Calls++; return 12m; } }
    }

    [Fact]
    public void RowGroupNodeWorkAndCancellationLimitsAreAtomic()
    {
        var data = new { Rows = new[] { new { Key = "b" }, new { Key = "a" } } };
        var plan = new ReportPlan(new[] { new ReportCollectionOperation("Groups", "Rows", "Key", "Key") });
        Assert.Throws<InvalidDataException>(() => ReportData.Prepare(data, plan, new ReportOptions { MaximumRows = 1 }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Throws<InvalidDataException>(() => ReportData.Prepare(data, plan, new ReportOptions { MaximumGroups = 1 }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Throws<InvalidDataException>(() => ReportData.Prepare(data, plan, new ReportOptions { MaximumSteps = 3 }, cancellationToken: TestContext.Current.CancellationToken));
        var fields = new ReportPlan(fields: new[] { new ReportField("A", B(ReportOperation.Add, N(1), N(2))) });
        Assert.Throws<InvalidDataException>(() => ReportData.Prepare(new { }, fields, new ReportOptions { MaximumNodes = 2 }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(3m, ReportData.Prepare(new { }, fields, new ReportOptions { MaximumNodes = 3 }, cancellationToken: TestContext.Current.CancellationToken).Values["A"]);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ReportData.Prepare(data, plan, cancellationToken: cancellation.Token));
        Assert.Equal("b", data.Rows[0].Key);
    }
}
