using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using WordNet.Internal;

namespace WordNet;

/// <summary>Explicit report expression operations; no text is evaluated as code.</summary>
public enum ReportOperation
{
    /// <summary>Checked decimal addition.</summary>
    Add,
    /// <summary>Checked decimal subtraction.</summary>
    Subtract,
    /// <summary>Checked decimal multiplication.</summary>
    Multiply,
    /// <summary>Decimal division; zero divisors fail.</summary>
    Divide,
    /// <summary>Typed scalar equality.</summary>
    Equal,
    /// <summary>Typed scalar inequality.</summary>
    NotEqual,
    /// <summary>Numeric or offset-date ordering.</summary>
    Less,
    /// <summary>Numeric or offset-date ordering.</summary>
    LessOrEqual,
    /// <summary>Numeric or offset-date ordering.</summary>
    Greater,
    /// <summary>Numeric or offset-date ordering.</summary>
    GreaterOrEqual,
    /// <summary>Left-to-right short-circuit conjunction.</summary>
    And,
    /// <summary>Left-to-right short-circuit disjunction.</summary>
    Or,
}

/// <summary>An immutable literal, path, arithmetic or predicate node.</summary>
public sealed class ReportExpression
{
    private ReportExpression(string kind, object? value, ReportExpression? left = null, ReportExpression? right = null,
        ReportOperation operation = default, int scale = 0, MidpointRounding rounding = default)
    { Kind = kind; Value = value; Left = left; Right = right; Operation = operation; Scale = scale; Rounding = rounding; }
    internal string Kind { get; }
    internal object? Value { get; }
    internal ReportExpression? Left { get; }
    internal ReportExpression? Right { get; }
    internal ReportOperation Operation { get; }
    internal int Scale { get; }
    internal MidpointRounding Rounding { get; }
    /// <summary>Creates a null, Boolean, string, decimal/integer or offset-date literal.</summary>
    public static ReportExpression Literal(object? value)
    {
        if (value != null && value is not bool and not string and not DateTimeOffset && !ReportEngine.Numeric(value))
            throw new ArgumentException("Report.Type: Unsupported literal type.", nameof(value));
        return new ReportExpression("literal", value);
    }
    /// <summary>References an ASCII data path or another computed output.</summary>
    public static ReportExpression Path(string path) { ReportEngine.Path(path); return new ReportExpression("path", path); }
    /// <summary>Combines two expressions with an explicit operation.</summary>
    public static ReportExpression Binary(ReportOperation operation, ReportExpression left, ReportExpression right)
    {
        Guard.NotNull(left, nameof(left)); Guard.NotNull(right, nameof(right));
        if (operation < ReportOperation.Add || operation > ReportOperation.Or) throw new ArgumentOutOfRangeException(nameof(operation));
        return new ReportExpression("binary", null, left, right, operation);
    }
    /// <summary>Rounds a decimal using an explicit scale and midpoint policy.</summary>
    public static ReportExpression Round(ReportExpression value, int scale, MidpointRounding rounding)
    {
        Guard.NotNull(value, nameof(value));
        if (scale < 0 || scale > 28) throw new ArgumentOutOfRangeException(nameof(scale));
        if (rounding is not MidpointRounding.ToEven and not MidpointRounding.AwayFromZero) throw new ArgumentOutOfRangeException(nameof(rounding));
        return new ReportExpression("round", null, value, scale: scale, rounding: rounding);
    }
    /// <summary>Negates a Boolean expression.</summary>
    public static ReportExpression Not(ReportExpression value) { Guard.NotNull(value, nameof(value)); return new ReportExpression("not", null, value); }
}

/// <summary>An immutable named computed value or predicate.</summary>
public sealed class ReportField
{
    /// <summary>Declares one new root-level merge-data value.</summary>
    public ReportField(string name, ReportExpression expression)
    { ReportEngine.Name(name); Name = name; Expression = expression ?? throw new ArgumentNullException(nameof(expression)); }
    /// <summary>Gets the output identifier.</summary>
    public string Name { get; }
    /// <summary>Gets the expression.</summary>
    public ReportExpression Expression { get; }
}

/// <summary>Aggregate rules; null values are excluded except by Count.</summary>
public enum ReportAggregateKind
{
    /// <summary>Counts all rows.</summary>
    Count,
    /// <summary>Checked decimal sum; empty yields zero.</summary>
    Sum,
    /// <summary>Decimal minimum; empty yields null.</summary>
    Minimum,
    /// <summary>Decimal maximum; empty yields null.</summary>
    Maximum,
}

/// <summary>An immutable aggregate declaration.</summary>
public sealed class ReportAggregate
{
    /// <summary>Creates an aggregate; Count requires no path.</summary>
    public ReportAggregate(string name, ReportAggregateKind kind, string? path = null)
    {
        ReportEngine.Name(name);
        if (kind < ReportAggregateKind.Count || kind > ReportAggregateKind.Maximum) throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind != ReportAggregateKind.Count) ReportEngine.Path(path!);
        else if (path != null) throw new ArgumentException("Report.Path: Count has no value path.", nameof(path));
        Name = name; Kind = kind; Path = path;
    }
    /// <summary>Gets the result identifier.</summary>
    public string Name { get; }
    /// <summary>Gets the operation.</summary>
    public ReportAggregateKind Kind { get; }
    /// <summary>Gets the row-relative path, or null for Count.</summary>
    public string? Path { get; }
}

/// <summary>An immutable collection sort/group operation.</summary>
public sealed class ReportCollectionOperation
{
    /// <summary>Declares a derived collection. Without grouping, aggregates are not permitted.</summary>
    public ReportCollectionOperation(string name, string source, string? groupBy = null, string? sortBy = null,
        bool descending = false, IEnumerable<ReportAggregate>? aggregates = null)
    {
        ReportEngine.Name(name); ReportEngine.Path(source);
        if (groupBy != null) ReportEngine.Path(groupBy); if (sortBy != null) ReportEngine.Path(sortBy);
        var items = ReportEngine.Take(aggregates ?? Array.Empty<ReportAggregate>(), 256);
        if (items.Any(a => a == null)) throw new ArgumentException("Report.Plan: Null aggregate.", nameof(aggregates));
        if (groupBy == null && items.Count != 0) throw new ArgumentException("Report.Plan: Aggregates require grouping.", nameof(aggregates));
        var names = new HashSet<string>(StringComparer.Ordinal) { "Key", "Rows" };
        if (items.Any(a => !names.Add(a.Name))) throw new ArgumentException("Report.Name: Duplicate aggregate output.", nameof(aggregates));
        Name = name; Source = source; GroupBy = groupBy; SortBy = sortBy; Descending = descending; Aggregates = items.AsReadOnly();
    }
    /// <summary>Gets the new root identifier.</summary>
    public string Name { get; }
    /// <summary>Gets the source collection path.</summary>
    public string Source { get; }
    /// <summary>Gets the row-relative group key.</summary>
    public string? GroupBy { get; }
    /// <summary>Gets the row-relative sort key.</summary>
    public string? SortBy { get; }
    /// <summary>Gets the sort direction.</summary>
    public bool Descending { get; }
    /// <summary>Gets the copied aggregate declarations.</summary>
    public IReadOnlyList<ReportAggregate> Aggregates { get; }
}

/// <summary>An immutable preprocessing plan. Collection operations precede computed fields.</summary>
public sealed class ReportPlan
{
    /// <summary>Copies at most 1,024 collection and 1,024 field declarations.</summary>
    public ReportPlan(IEnumerable<ReportCollectionOperation>? collections = null, IEnumerable<ReportField>? fields = null)
    {
        Collections = ReportEngine.Take(collections ?? Array.Empty<ReportCollectionOperation>(), 1024).AsReadOnly();
        Fields = ReportEngine.Take(fields ?? Array.Empty<ReportField>(), 1024).AsReadOnly();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? name in Collections.Select(c => c?.Name).Concat(Fields.Select(f => f?.Name)))
            if (name == null || !names.Add(name)) throw new ArgumentException("Report.Name: Null or duplicate plan output.");
    }
    /// <summary>Gets collection declarations in execution order.</summary>
    public IReadOnlyList<ReportCollectionOperation> Collections { get; }
    /// <summary>Gets computed declarations, evaluated in dependency order.</summary>
    public IReadOnlyList<ReportField> Fields { get; }
}

/// <summary>Finite preprocessing limits, also constrained by conversion resource limits.</summary>
public sealed class ReportOptions
{
    /// <summary>Gets or sets admitted and generated row limit, default 10,000.</summary>
    public int MaximumRows { get; set; } = 10000;
    /// <summary>Gets or sets generated group limit, default 1,000.</summary>
    public int MaximumGroups { get; set; } = 1000;
    /// <summary>Gets or sets string-key character limit, default 1,024.</summary>
    public int MaximumKeyLength { get; set; } = 1024;
    /// <summary>Gets or sets expression node limit, default 10,000.</summary>
    public int MaximumNodes { get; set; } = 10000;
    /// <summary>Gets or sets expression/dependency depth limit, default 32.</summary>
    public int MaximumDepth { get; set; } = 32;
    /// <summary>Gets or sets total comparison, aggregate and expression work limit, default 1,000,000.</summary>
    public int MaximumSteps { get; set; } = 1000000;
}

/// <summary>An immutable staged report-data snapshot, consumable by ordinary Merge.</summary>
public sealed class ReportData
{
    private ReportData(IReadOnlyDictionary<string, object?> values) { Values = values; }
    /// <summary>Gets deeply read-only data, with caller values and new outputs.</summary>
    public IReadOnlyDictionary<string, object?> Values { get; }
    /// <summary>Snapshots once and executes a bounded plan without mutating input or any session.</summary>
    public static ReportData Prepare(object data, ReportPlan plan, ReportOptions? reportOptions = null,
        ConversionOptions? options = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(data, nameof(data)); Guard.NotNull(plan, nameof(plan));
        var engine = new ReportEngine(reportOptions ?? new ReportOptions(), new ConversionContext(options ?? new ConversionOptions(), cancellationToken));
        return new ReportData(engine.Execute(data, plan));
    }
}

internal sealed class ReportEngine
{
    private readonly int maximumRows, maximumGroups, maximumKey, maximumNodes, maximumDepth, maximumSteps;
    private readonly ConversionContext context;
    private int steps, nodes, rows, groups;
    private long sourceCharacters, expressionCharacters;
    internal ReportEngine(ReportOptions policy, ConversionContext context)
    {
        if (policy.MaximumRows is < 1 or > 100000 || policy.MaximumGroups is < 1 or > 100000 || policy.MaximumKeyLength is < 1 or > 65536
            || policy.MaximumNodes is < 1 or > 100000 || policy.MaximumDepth is < 1 or > 64 || policy.MaximumSteps is < 1 or > 10000000)
            throw new ArgumentOutOfRangeException(nameof(policy));
        maximumRows = policy.MaximumRows; maximumGroups = policy.MaximumGroups; maximumKey = policy.MaximumKeyLength;
        maximumNodes = policy.MaximumNodes; maximumDepth = Math.Min(policy.MaximumDepth, context.Options.MaximumNestingDepth);
        maximumSteps = policy.MaximumSteps; this.context = context;
    }
    internal static List<T> Take<T>(IEnumerable<T> source, int maximum)
    {
        var result = new List<T>();
        foreach (T item in source) { if (result.Count >= maximum) throw new ArgumentException("Report.Plan: Declaration limit exceeded."); result.Add(item); }
        return result;
    }
    internal static void Name(string name) { if (name == null || !TemplateData.Identifier(name)) throw new ArgumentException("Report.Name: Invalid identifier."); }
    internal static void Path(string path)
    { if (path == null || path.Length > 256 || !path.Split('.').All(TemplateData.Identifier)) throw new ArgumentException("Report.Path: Invalid ASCII path."); }
    internal static bool Numeric(object value) => value is byte or sbyte or short or ushort or int or uint or long or ulong or decimal;
    private static decimal Number(object? value) => value != null && Numeric(value) ? Convert.ToDecimal(value, CultureInfo.InvariantCulture) : throw Invalid("Type");
    private static bool Boolean(object? value) => value is bool b ? b : throw Invalid("Type");
    private static InvalidDataException Invalid(string code) => new InvalidDataException("Report." + code + ": Invalid report operation or data.");
    private void Step()
    {
        context.CheckCancellation();
        if (++steps > maximumSteps) throw Invalid("WorkLimit");
    }
    private void ReserveWork(int amount)
    {
        context.CheckCancellation();
        if (amount > maximumSteps - steps) throw Invalid("WorkLimit"); steps += amount;
    }
    private void Depth(int depth) { Step(); if (depth > maximumDepth) throw Invalid("DepthLimit"); }
    private object? Key(object? value)
    {
        Step();
        if (value is string s && s.Length > maximumKey) throw Invalid("KeyLimit");
        if (value is string key) ReserveWork(key.Length);
        if (value != null && value is not string and not bool and not DateTimeOffset && !Numeric(value)) throw Invalid("Type");
        return value != null && Numeric(value) ? Number(value) : value;
    }
    private int Compare(object? left, object? right, bool nulls = true)
    {
        Step();
        if (left != null && left is not string and not bool and not DateTimeOffset && !Numeric(left)
            || right != null && right is not string and not bool and not DateTimeOffset && !Numeric(right)) throw Invalid("Type");
        if (left == null || right == null)
        { if (!nulls) throw Invalid("Null"); return left == null ? right == null ? 0 : -1 : 1; }
        if (Numeric(left) && Numeric(right)) return Number(left).CompareTo(Number(right));
        if (left is DateTimeOffset a && right is DateTimeOffset b) return a.CompareTo(b);
        if (left is string x && right is string y) { ReserveWork(Math.Min(x.Length, y.Length)); return string.CompareOrdinal(x, y); }
        if (left is bool p && right is bool q) return p.CompareTo(q);
        throw Invalid("Type");
    }
    private object? Get(object? scope, string path)
    {
        foreach (string part in path.Split('.'))
        { Step(); if (scope is not Dictionary<string, object?> dictionary || !dictionary.TryGetValue(part, out scope)) throw Invalid("MissingPath"); }
        return scope;
    }
    internal IReadOnlyDictionary<string, object?> Execute(object data, ReportPlan plan)
    {
        Dictionary<string, object?> root = TemplateData.Snapshot(data, context, maximumRows);
        int admittedRows = 0; CountInput(root);
        var fields = plan.Fields.ToDictionary(f => f.Name, StringComparer.Ordinal);
        foreach (string name in plan.Collections.Select(c => c.Name).Concat(fields.Keys)) if (root.ContainsKey(name)) throw Invalid("DuplicateName");
        // Validate every declared node, including short-circuited operands, before evaluating.
        foreach (ReportField field in plan.Fields) Validate(field.Expression, 0);
        var dependencies = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (ReportField field in plan.Fields)
        {
            var references = new List<string>(); References(field.Expression, references); dependencies.Add(field.Name, references);
        }
        HashSet<string> complete = new HashSet<string>(StringComparer.Ordinal), visiting = new HashSet<string>(StringComparer.Ordinal);
        foreach (ReportField field in plan.Fields) Graph(field.Name, 0);
        foreach (ReportCollectionOperation collection in plan.Collections)
        {
            Step();
            if (Get(root, collection.Source) is not List<object?> source) throw Invalid("Collection");
            var sorted = new List<(object? Row, object? Key)>();
            object? sortType = null, groupType = null;
            foreach (object? row in source)
            {
                Step(); if (++rows > maximumRows) throw Invalid("RowLimit");
                object? key = collection.SortBy == null ? null : Key(Get(row, collection.SortBy));
                if (key != null) { if (sortType != null) _ = Compare(sortType, key); else sortType = key; }
                sorted.Add((row, key));
            }
            if (collection.SortBy != null) sorted = Sort(sorted, collection.Descending);
            if (collection.GroupBy == null) { root.Add(collection.Name, sorted.Select(r => r.Row).ToList()); continue; }
            var output = new List<object?>();
            // Dictionary hashing is ordinal and all numeric keys have already been normalized.
            var indexes = new Dictionary<object, int>(); int nullIndex = -1;
            foreach (var row in sorted)
            {
                object? key = Key(Get(row.Row, collection.GroupBy));
                if (key != null) { if (groupType != null) _ = Compare(groupType, key); else groupType = key; }
                int index;
                bool found = key == null ? (index = nullIndex) >= 0 : indexes.TryGetValue(key, out index);
                if (!found)
                {
                    if (++groups > maximumGroups) throw Invalid("GroupLimit");
                    index = output.Count; if (key == null) nullIndex = index; else indexes.Add(key, index);
                    output.Add(new Dictionary<string, object?>(StringComparer.Ordinal) { ["Key"] = key, ["Rows"] = new List<object?>() });
                }
                ((List<object?>)((Dictionary<string, object?>)output[index]!)["Rows"]!).Add(row.Row);
            }
            foreach (Dictionary<string, object?> group in output.Cast<Dictionary<string, object?>>())
            {
                var members = (List<object?>)group["Rows"]!;
                foreach (ReportAggregate aggregate in collection.Aggregates)
                {
                    Step(); object? result = aggregate.Kind == ReportAggregateKind.Count ? members.Count : aggregate.Kind == ReportAggregateKind.Sum ? 0m : null;
                    if (aggregate.Kind != ReportAggregateKind.Count) foreach (object? row in members)
                    {
                        Step(); object? value = Get(row, aggregate.Path!); if (value == null) continue; decimal number = Number(value);
                        result = aggregate.Kind switch
                        {
                            ReportAggregateKind.Sum => checked((decimal)result! + number),
                            ReportAggregateKind.Minimum => result == null ? number : Math.Min((decimal)result, number),
                            _ => result == null ? number : Math.Max((decimal)result, number),
                        };
                    }
                    group.Add(aggregate.Name, result);
                }
            }
            root.Add(collection.Name, output);
        }
        var active = new HashSet<string>(StringComparer.Ordinal);
        foreach (ReportField field in plan.Fields) _ = Field(field.Name, 0);
        // Admit the entire generated result with the original aggregate bounds, then freeze it.
        root = TemplateData.Snapshot(root, context, maximumRows);
        return (IReadOnlyDictionary<string, object?>)Freeze(root)!;

        void References(ReportExpression expression, List<string> references)
        {
            Step();
            if (expression.Kind == "path")
            {
                string first = ((string)expression.Value!).Split('.')[0]; if (fields.ContainsKey(first)) references.Add(first);
            }
            if (expression.Left != null) References(expression.Left, references); if (expression.Right != null) References(expression.Right, references);
        }
        void Graph(string name, int depth)
        {
            Depth(depth); if (complete.Contains(name)) return;
            if (!visiting.Add(name)) throw Invalid("Cycle");
            foreach (string dependency in dependencies[name]) Graph(dependency, depth + 1);
            visiting.Remove(name); complete.Add(name);
        }

        void CountInput(object? value)
        {
            Step();
            if (value is string text) sourceCharacters = checked(sourceCharacters + text.Length);
            else if (value is Dictionary<string, object?> dictionary)
                foreach (var pair in dictionary) { sourceCharacters = checked(sourceCharacters + pair.Key.Length); CountInput(pair.Value); }
            else if (value is List<object?> list)
            {
                admittedRows = checked(admittedRows + list.Count); if (admittedRows > maximumRows) throw Invalid("RowLimit");
                foreach (object? item in list) CountInput(item);
            }
            if (sourceCharacters > context.Options.MaximumCharacters || sourceCharacters * 2 > context.Options.MaximumExpandedBytes) throw Invalid("ResourceLimit");
        }

        object? Field(string name, int depth)
        {
            Depth(depth);
            if (root.TryGetValue(name, out object? existing)) return existing;
            if (!fields.TryGetValue(name, out ReportField? field)) throw Invalid("MissingPath");
            if (!active.Add(name)) throw Invalid("Cycle");
            object? value = Evaluate(field.Expression, depth + 1); active.Remove(name); root.Add(name, value); return value;
        }
        object? Evaluate(ReportExpression expression, int depth)
        {
            Depth(depth);
            if (expression.Kind == "literal") return expression.Value;
            if (expression.Kind == "path")
            {
                string path = (string)expression.Value!, first = path.Split('.')[0];
                if (fields.ContainsKey(first)) _ = Field(first, depth + 1);
                return Get(root, path);
            }
            object? left = Evaluate(expression.Left!, depth + 1);
            if (expression.Kind == "not") return !Boolean(left);
            if (expression.Kind == "round") return Math.Round(Number(left), expression.Scale, expression.Rounding);
            if (expression.Operation == ReportOperation.And && !Boolean(left)) return false;
            if (expression.Operation == ReportOperation.Or && Boolean(left)) return true;
            object? right = Evaluate(expression.Right!, depth + 1);
            return expression.Operation switch
            {
                ReportOperation.Add => checked(Number(left) + Number(right)),
                ReportOperation.Subtract => checked(Number(left) - Number(right)),
                ReportOperation.Multiply => checked(Number(left) * Number(right)),
                ReportOperation.Divide => Number(left) / Number(right),
                ReportOperation.And => Boolean(right),
                ReportOperation.Or => Boolean(right),
                ReportOperation.Equal => Compare(left, right) == 0,
                ReportOperation.NotEqual => Compare(left, right) != 0,
                _ => Order(expression.Operation, left, right),
            };
        }
    }
    private bool Order(ReportOperation operation, object? left, object? right)
    {
        if (!(left is DateTimeOffset && right is DateTimeOffset) && !(left != null && right != null && Numeric(left) && Numeric(right))) throw Invalid("Type");
        int order = Compare(left, right, false);
        return operation switch { ReportOperation.Less => order < 0, ReportOperation.LessOrEqual => order <= 0, ReportOperation.Greater => order > 0, _ => order >= 0 };
    }
    private void Validate(ReportExpression expression, int depth)
    {
        Depth(depth); if (++nodes > maximumNodes || nodes * 64L > context.Options.MaximumExpandedBytes || nodes > context.Options.MaximumCharacters) throw Invalid("NodeLimit");
        if (expression.Value is string text) expressionCharacters = checked(expressionCharacters + text.Length);
        if (sourceCharacters + expressionCharacters > context.Options.MaximumCharacters
            || (sourceCharacters + expressionCharacters) * 2 + nodes * 64L > context.Options.MaximumExpandedBytes) throw Invalid("ResourceLimit");
        if (expression.Left != null) Validate(expression.Left, depth + 1); if (expression.Right != null) Validate(expression.Right, depth + 1);
    }
    private List<(object? Row, object? Key)> Sort(List<(object? Row, object? Key)> source, bool descending)
    {
        // Iterative stable merge sort checks cancellation/work inside every comparison.
        var input = source.ToArray(); var output = new (object? Row, object? Key)[input.Length];
        for (int width = 1; width < input.Length; width *= 2)
        {
            for (int start = 0; start < input.Length; start += width * 2)
            {
                int middle = Math.Min(start + width, input.Length), end = Math.Min(start + width * 2, input.Length), a = start, b = middle;
                for (int i = start; i < end; i++)
                {
                    Step();
                    bool takeLeft = b == end || a < middle && (descending ? Compare(input[a].Key, input[b].Key) >= 0 : Compare(input[a].Key, input[b].Key) <= 0);
                    output[i] = takeLeft ? input[a++] : input[b++];
                }
            }
            var swap = input; input = output; output = swap;
        }
        return input.ToList();
    }
    private object? Freeze(object? value)
    {
        Step();
        if (value is Dictionary<string, object?> dictionary)
            return new ReadOnlyDictionary<string, object?>(dictionary.ToDictionary(p => p.Key, p => Freeze(p.Value), StringComparer.Ordinal));
        if (value is List<object?> list) return list.Select(Freeze).ToList().AsReadOnly();
        return value;
    }
}
