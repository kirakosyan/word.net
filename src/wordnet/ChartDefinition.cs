using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using WordNet.Internal;

namespace WordNet;

/// <summary>The admitted two-dimensional chart types.</summary>
public enum ChartKind
{
    /// <summary>Side-by-side columns with a zero baseline.</summary>
    ClusteredColumn,
    /// <summary>Ordinary non-stacked lines; missing values break the line.</summary>
    Line,
    /// <summary>Side-by-side horizontal bars with a zero baseline.</summary>
    ClusteredBar,
    /// <summary>Columns that stack positive and negative series independently.</summary>
    StackedColumn,
    /// <summary>Horizontal bars that stack positive and negative series independently.</summary>
    StackedBar,
    /// <summary>One series of non-negative pie slices with explicit colours.</summary>
    Pie,
    /// <summary>One series of non-negative annular slices with an unfilled centre.</summary>
    Doughnut,
}

/// <summary>An immutable category, nullable non-negative value and explicit RGB slice colour.</summary>
public sealed class ChartSlice
{
    /// <summary>Creates one bounded slice. Null is missing; zero has no area.</summary>
    public ChartSlice(string category, double? value, string color)
    {
        ObjectText.Check(category, nameof(category), 1024);
        if (value.HasValue && (double.IsNaN(value.Value) || double.IsInfinity(value.Value) || value.Value < 0))
            throw new ArgumentOutOfRangeException(nameof(value), "Chart.SliceValue: Values must be finite and non-negative.");
        if (color == null || color.Length != 6 || color.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Chart.Color: A six-digit RGB colour is required.", nameof(color));
        Category = category; Value = value; Color = color.ToUpperInvariant();
    }
    /// <summary>Gets the category text.</summary>
    public string Category { get; }
    /// <summary>Gets a finite non-negative value or null for a gap.</summary>
    public double? Value { get; }
    /// <summary>Gets the explicit uppercase RGB colour.</summary>
    public string Color { get; }
}

/// <summary>Plain, centred labels for admitted bar and column chart points.</summary>
public enum ChartDataLabels
{
    /// <summary>No point labels.</summary>
    None,
    /// <summary>The finite numeric value.</summary>
    Value,
    /// <summary>The category text.</summary>
    Category,
    /// <summary>The category, a comma and space, and the numeric value.</summary>
    CategoryAndValue,
}

/// <summary>One immutable, explicitly coloured series. Null values are gaps.</summary>
public sealed class ChartSeries
{
    /// <summary>Copies and admits a name, six-digit RGB colour and at most 10,000 finite values.</summary>
    public ChartSeries(string name, string color, IEnumerable<double?> values)
    {
        ObjectText.Check(name, nameof(name), 1024);
        if (color == null || color.Length != 6 || color.Any(c => !Uri.IsHexDigit(c))) throw new ArgumentException("Chart.Color: A six-digit RGB colour is required.", nameof(color));
        Guard.NotNull(values, nameof(values));
        var copy = new List<double?>();
        foreach (double? value in values)
        {
            if (copy.Count >= 10000 || value.HasValue && (double.IsNaN(value.Value) || double.IsInfinity(value.Value))) throw new ArgumentException("Chart.Values: Values must be finite and bounded.", nameof(values));
            copy.Add(value);
        }
        Name = name; Color = color.ToUpperInvariant(); Values = copy.AsReadOnly();
    }
    /// <summary>Gets the series name.</summary>
    public string Name { get; }
    /// <summary>Gets explicit uppercase RGB.</summary>
    public string Color { get; }
    /// <summary>Gets the private immutable value snapshot; null denotes a gap.</summary>
    public IReadOnlyList<double?> Values { get; }
}

/// <summary>Immutable chart data, independent of XML, spreadsheet and renderer types.</summary>
public sealed class ChartDefinition
{
    private ChartDefinition(ChartDefinition source, ChartDataLabels labels)
    {
        if (labels is not ChartDataLabels.None and not ChartDataLabels.Value and not ChartDataLabels.Category and not ChartDataLabels.CategoryAndValue)
            throw new ArgumentOutOfRangeException(nameof(labels));
        if (labels != ChartDataLabels.None && source.Kind is ChartKind.Line or ChartKind.Pie or ChartKind.Doughnut)
            throw new NotSupportedException("Chart.DataLabels: Point labels are supported on bars and columns only.");
        Kind = source.Kind; Categories = source.Categories; Series = source.Series; Title = source.Title;
        ShowLegend = source.ShowLegend; CategoryTitle = source.CategoryTitle; ValueTitle = source.ValueTitle; DataLabels = labels; SliceColors = source.SliceColors;
    }
    private ChartDefinition(ChartDefinition source, ChartKind kind, IReadOnlyList<string> colors)
    {
        Kind = kind; Categories = source.Categories; Series = source.Series; Title = source.Title; ShowLegend = source.ShowLegend;
        CategoryTitle = source.CategoryTitle; ValueTitle = source.ValueTitle; SliceColors = colors;
    }
    /// <summary>Copies one to 64 non-negative slices into an editable pie or doughnut chart.</summary>
    /// <remarks>Null and zero values have no area. At least one positive value is required for static rendering.</remarks>
    public static ChartDefinition CreateCircular(ChartKind kind, IEnumerable<ChartSlice> slices, string seriesName = "", string title = "", bool showLegend = true)
    {
        if (kind is not ChartKind.Pie and not ChartKind.Doughnut) throw new ArgumentOutOfRangeException(nameof(kind));
        Guard.NotNull(slices, nameof(slices));
        var labels = new List<string>(); var colors = new List<string>(); var values = new List<double?>();
        foreach (ChartSlice slice in slices)
        {
            if (slice == null || labels.Count >= 64) throw new ArgumentException("Chart.Slices: One to 64 non-null slices are required.", nameof(slices));
            labels.Add(slice.Category); colors.Add(slice.Color); values.Add(slice.Value);
        }
        if (labels.Count == 0) throw new ArgumentException("Chart.Slices: One to 64 non-null slices are required.", nameof(slices));
        var source = new ChartDefinition(ChartKind.ClusteredColumn, labels, new[] { new ChartSeries(seriesName, colors[0], values) }, title, showLegend);
        return new ChartDefinition(source, kind, colors.AsReadOnly());
    }
    /// <summary>Copies an admitted chart with 1–16 series, matching category counts and at most 10,000 total points.</summary>
    public ChartDefinition(ChartKind kind, IEnumerable<string> categories, IEnumerable<ChartSeries> series, string title = "", bool showLegend = true, string categoryTitle = "", string valueTitle = "")
    {
        if (kind is not ChartKind.ClusteredColumn and not ChartKind.Line and not ChartKind.ClusteredBar and not ChartKind.StackedColumn and not ChartKind.StackedBar)
            throw new ArgumentOutOfRangeException(nameof(kind));
        Guard.NotNull(categories, nameof(categories));
        Guard.NotNull(series, nameof(series));
        ObjectText.Check(title, nameof(title), 1024);
        ObjectText.Check(categoryTitle, nameof(categoryTitle), 1024); ObjectText.Check(valueTitle, nameof(valueTitle), 1024);
        var labels = new List<string>(); long text = title.Length + categoryTitle.Length + valueTitle.Length;
        foreach (string label in categories)
        {
            ObjectText.Check(label, nameof(categories), 1024); text += label.Length;
            if (labels.Count >= 10000 || text > 1048576) throw new ArgumentException("Chart.Categories: Category allowance exceeded.", nameof(categories));
            labels.Add(label);
        }
        var data = new List<ChartSeries>();
        foreach (ChartSeries item in series)
        {
            if (item == null || item.Values.Count != labels.Count || data.Count >= 16) throw new ArgumentException("Chart.Series: Series require matching category counts and at most sixteen entries.", nameof(series));
            text += item.Name.Length; data.Add(item);
        }
        if (labels.Count == 0 || data.Count == 0 || (long)labels.Count * data.Count > 10000) throw new ArgumentException("Chart.Points: One to ten thousand points are required.", nameof(categories));
        if (text > 1048576) throw new ArgumentException("Chart.Text: Aggregate label allowance exceeded.", nameof(series));
        Kind = kind; Categories = labels.AsReadOnly(); Series = data.AsReadOnly(); Title = title; ShowLegend = showLegend; CategoryTitle = categoryTitle; ValueTitle = valueTitle;
    }
    /// <summary>Gets the admitted chart type.</summary>
    public ChartKind Kind { get; }
    /// <summary>Gets the private immutable category snapshot.</summary>
    public IReadOnlyList<string> Categories { get; }
    /// <summary>Gets immutable series.</summary>
    public IReadOnlyList<ChartSeries> Series { get; }
    /// <summary>Gets the plain title.</summary>
    public string Title { get; }
    /// <summary>Gets whether the simple right-hand legend is rendered.</summary>
    public bool ShowLegend { get; }
    /// <summary>Gets the plain category-axis title.</summary>
    public string CategoryTitle { get; }
    /// <summary>Gets the plain value-axis title; the bounded renderer places it horizontally above the plot.</summary>
    public string ValueTitle { get; }
    /// <summary>Gets the plain, centred data-label content.</summary>
    public ChartDataLabels DataLabels { get; }
    /// <summary>Gets the immutable per-category colours of a circular chart, or an empty list for other chart types.</summary>
    public IReadOnlyList<string> SliceColors { get; } = Array.Empty<string>();
    /// <summary>Creates an immutable snapshot with the requested bounded data labels.</summary>
    public ChartDefinition WithDataLabels(ChartDataLabels labels) => new ChartDefinition(this, labels);
}

internal static class ObjectText
{
    internal static void Check(string value, string parameter, int maximum)
    {
        Guard.NotNull(value, parameter);
        if (value.Length > maximum) throw new ArgumentException("Object.Text: Text exceeds its allowance.", parameter);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsHighSurrogate(c)) { if (++i >= value.Length || !char.IsLowSurrogate(value[i])) throw new ArgumentException("Object.Text: Invalid UTF-16.", parameter); }
            else if (char.IsLowSurrogate(c) || char.IsControl(c)) throw new ArgumentException("Object.Text: Invalid UTF-16 or control character.", parameter);
        }
    }
}
