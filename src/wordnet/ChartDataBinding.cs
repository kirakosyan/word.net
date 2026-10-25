using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet;

/// <summary>Immutable paths for a chart series name at the report root and values relative to each row.</summary>
public sealed class ChartSeriesBinding
{
    /// <summary>Creates two required, case-sensitive dotted ASCII paths. No expression or external access is performed.</summary>
    public ChartSeriesBinding(string namePath, string valuePath)
    {
        ReportEngine.Path(namePath); ReportEngine.Path(valuePath);
        NamePath = namePath; ValuePath = valuePath;
    }
    /// <summary>Gets the report-root path to a string series name.</summary>
    public string NamePath { get; }
    /// <summary>Gets the row-relative path to a nullable number.</summary>
    public string ValuePath { get; }
}

/// <summary>A bounded chart-data projection from immutable report values, preserving placement and appearance.</summary>
public sealed class ChartDataBinding
{
    /// <summary>Copies one to sixteen series bindings. Rows is a root path; category is a row-relative string path.</summary>
    public ChartDataBinding(string rowsPath, string categoryPath, IEnumerable<ChartSeriesBinding> series)
    {
        ReportEngine.Path(rowsPath); ReportEngine.Path(categoryPath); Guard.NotNull(series, nameof(series));
        var copy = ReportEngine.Take(series, 16);
        if (copy.Count == 0 || copy.Any(s => s == null)) throw new ArgumentException("Chart.Binding: One to sixteen non-null series are required.", nameof(series));
        RowsPath = rowsPath; CategoryPath = categoryPath; Series = copy.AsReadOnly();
    }
    /// <summary>Gets the root path to the immutable report rows.</summary>
    public string RowsPath { get; }
    /// <summary>Gets the row-relative path to category strings.</summary>
    public string CategoryPath { get; }
    /// <summary>Gets the copied series bindings, in the existing chart's series order.</summary>
    public IReadOnlyList<ChartSeriesBinding> Series { get; }

    internal ChartDefinition Apply(ChartDefinition original, ReportData report, ConversionContext context)
    {
        if (Series.Count != original.Series.Count) throw new InvalidDataException("Chart.BindingSeries: Bindings must match the existing series count.");
        if (Get(report.Values, RowsPath) is not IReadOnlyList<object?> rows || rows.Count == 0 || (long)rows.Count * Series.Count > 10000)
            throw new InvalidDataException("Chart.BindingRows: A nonempty array with at most 10,000 total points is required.");
        var categories = new List<string>();
        var values = Series.Select(_ => new List<double?>()).ToArray();
        foreach (object? row in rows)
        {
            context.CheckCancellation(); categories.Add(Text(Get(row, CategoryPath)));
            for (int i = 0; i < Series.Count; i++)
            {
                object? value = Get(row, Series[i].ValuePath);
                if (value != null && !ReportEngine.Numeric(value) && value is not double and not float)
                    throw new InvalidDataException("Chart.BindingType: Values must be nullable finite numbers.");
                double? number = value == null ? null : Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (number.HasValue && (double.IsInfinity(number.Value) || double.IsNaN(number.Value)))
                    throw new InvalidDataException("Chart.BindingType: Values must be nullable finite numbers.");
                values[i].Add(number);
            }
        }
        var series = new List<ChartSeries>();
        for (int i = 0; i < Series.Count; i++)
        {
            context.CheckCancellation();
            series.Add(new ChartSeries(Text(Get(report.Values, Series[i].NamePath)), original.Series[i].Color, values[i]));
        }
        return new ChartDefinition(original.Kind, categories, series, original.Title, original.ShowLegend, original.CategoryTitle, original.ValueTitle);
    }
    private static object? Get(object? value, string path)
    {
        foreach (string part in path.Split('.'))
            if (value is not IReadOnlyDictionary<string, object?> dictionary || !dictionary.TryGetValue(part, out value))
                throw new InvalidDataException("Chart.BindingPath: Required report path is missing.");
        return value;
    }
    private static string Text(object? value) => value is string text ? text : throw new InvalidDataException("Chart.BindingType: Categories and series names must be strings.");
}

public sealed partial class WordDocument
{
    /// <summary>Binds immutable report rows to one stable chart, rebuilding semantic values, caches and workbook atomically.</summary>
    /// <remarks>Bindings retain type, colours, axes, title, description and dimensions. Object repeat/control owners remain unsupported.</remarks>
    public DocumentObject BindChart(DocumentObject handle, ReportData data, ChartDataBinding binding, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(data, nameof(data)); Guard.NotNull(binding, nameof(binding));
        lock (gate)
        {
            // Check identity before projecting any data; all work and admission are staged by ChangeObject.
            TextRun run = FindObject(model, handle);
            ParagraphBlock owner = model.BodyParagraphs.Single(p => p.Runs.Contains(run));
            if (owner.Control != null || owner.Runs.Any(r => r.Control != null)
                || model.Tables.Any(t => t.RowControls.Count != 0 && t.Rows.SelectMany(r => r).Any(c => c.Paragraphs.Contains(owner))))
                throw new NotSupportedException("Chart.BindingOwner: Chart owners in template controls or repeat rows are unsupported.");
            return ChangeObject(handle, (old, context) =>
            {
                if (old.Chart == null) throw new ArgumentException("Object.Kind: A chart handle is required.", nameof(handle));
                ChartDefinition definition = binding.Apply(old.Chart, data, context);
                return new DocumentObjectData(definition, Formats.ChartObjectFormat.Create(definition, context), old.Width, old.Height, old.Description, old.Id);
            }, cancellationToken);
        }
    }
}
