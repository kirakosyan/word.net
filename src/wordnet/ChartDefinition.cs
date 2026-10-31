using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using WordNet.Internal;

namespace WordNet;

/// <summary>The admitted, two-dimensional, linear chart types.</summary>
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
