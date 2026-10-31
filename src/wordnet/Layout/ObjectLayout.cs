using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Layout;

internal sealed class ObjectVisual
{
    internal double Width { get; set; }
    internal double Ascent { get; set; }
    internal double Descent { get; set; }
    // Coordinates are relative to the object's baseline, with downward-positive Y.
    internal List<PositionedText> Text { get; } = new List<PositionedText>();
    internal List<FixedShape> Shapes { get; } = new List<FixedShape>();
}

internal static class ChartLayout
{
    internal static ObjectVisual Create(DocumentObjectData item, int font, Func<string, int, double, double> measure, ConversionContext context)
    {
        if (!item.Renderable) throw new NotSupportedException("Chart.Appearance: The retained chart appearance is outside the static rendering profile.");
        ChartDefinition chart = item.Chart!;
        if (chart.Kind == ChartKind.ClusteredBar && (context.Typography.Enabled
            || context.Destination == DocumentFormat.Pdf && !context.PreviewIntermediate
                && (context.Options.Pdf.Accessibility != PdfAccessibility.None || context.Options.Pdf.Conformance != PdfConformance.Pdf17)))
            throw new NotSupportedException("Chart.BarProfile: Horizontal bars are qualified only for ordinary managed output.");
        if (chart.Kind == ChartKind.ClusteredBar)
            context.Loss("Chart.BarLayoutApproximation", "The bounded horizontal-bar plot area differs from independent reference geometry.");
        context.Objects.Render(chart.Title.Length + chart.CategoryTitle.Length + chart.ValueTitle.Length + chart.Categories.Sum(s => (long)s.Length)
            + chart.Series.Sum(s => (long)s.Name.Length) + 128, (long)chart.Categories.Count * chart.Series.Count + chart.Categories.Count + chart.Series.Count + 32);
        double[] finite = chart.Series.SelectMany(s => s.Values).Where(v => v.HasValue).Select(v => v!.Value).ToArray();
        if (finite.Length == 0) throw new NotSupportedException("Chart.EmptyData: At least one real value is required for rendering.");
        double min = Math.Min(0, finite.Min()), max = Math.Max(0, finite.Max());
        if (min == 0 && max == 0) { min = -1; max = 1; }
        double span = max - min;
        if (double.IsInfinity(span) || span < 1e-200 || span > 1e200) throw new NotSupportedException("Chart.Range: The finite range cannot be resolved by this profile.");
        double power = Math.Pow(10, Math.Floor(Math.Log10(span / 5))), ratio = span / (5 * power);
        double step = (ratio <= 1 ? 1 : ratio <= 2 ? 2 : ratio <= 5 ? 5 : 10) * power;
        min = Math.Floor(min / step) * step; max = Math.Ceiling(max / step) * step;
        int ticks = checked((int)Math.Round((max - min) / step));
        if (ticks is < 1 or > 7 || !Finite(min) || !Finite(max) || max <= min) throw new NotSupportedException("Chart.Range: Axis rounding exceeds the finite tick allowance.");
        var result = new ObjectVisual { Width = item.Width, Ascent = item.Height };
        const double size = 8, padding = 8;
        string Label(double value) => value == 0 ? "0" : value.ToString("G6", CultureInfo.InvariantCulture);
        string[] tickLabels = Enumerable.Range(0, ticks + 1).Select(i => Label(min + i * step)).ToArray();
        if (chart.Kind == ChartKind.ClusteredBar) return Horizontal(item, chart, font, measure, context, min, max, step, tickLabels);
        double left = padding + tickLabels.Max(t => measure(t, font, size)) + 6;
        double legend = chart.ShowLegend ? chart.Series.Max(s => measure(s.Name, font, size)) + 26 : 0;
        double top = -item.Height + padding + (chart.Title.Length > 0 ? 18 : 0) + (chart.ValueTitle.Length > 0 ? 12 : 0), bottom = chart.CategoryTitle.Length > 0 ? -38 : -24;
        double right = item.Width - padding - legend;
        if (right - left < 24 || bottom - top < 24) throw new NotSupportedException("Chart.LabelFit: Labels leave insufficient plot space.");
        double plotWidth = right - left, plotHeight = bottom - top, slot = plotWidth / chart.Categories.Count;
        double Y(double v) => bottom - (v - min) / (max - min) * plotHeight;
        void Text(string value, double x, double y, double available, bool center = false)
        {
            context.CheckCancellation(); double width = measure(value, font, size);
            if (!Finite(width) || width > available) throw new NotSupportedException("Chart.LabelFit: A chart label exceeds its slot; labels are never silently dropped or clipped.");
            if (value.Length != 0) result.Text.Add(new PositionedText(value, center ? x - width / 2 : x, y, size, font));
        }
        if (chart.Title.Length > 0) Text(chart.Title, item.Width / 2, -item.Height + 16, item.Width - 16, true);
        if (chart.ValueTitle.Length > 0) Text(chart.ValueTitle, left, top - 4, plotWidth);
        if (chart.CategoryTitle.Length > 0) Text(chart.CategoryTitle, left + plotWidth / 2, bottom + 30, plotWidth, true);
        for (int i = 0; i <= ticks; i++)
        {
            double at = Y(min + i * step);
            result.Shapes.Add(new FixedShape(left, at, plotWidth, 0, "D0D0D0", 0.5));
            double width = measure(tickLabels[i], font, size); Text(tickLabels[i], left - width - 5, at + size / 3, left - 5);
        }
        result.Shapes.Add(new FixedShape(left, top, 0, plotHeight, "000000", 0.75));
        result.Shapes.Add(new FixedShape(left, Y(0), plotWidth, 0, "000000", 0.75));
        for (int c = 0; c < chart.Categories.Count; c++) Text(chart.Categories[c], left + (c + 0.5) * slot, bottom + 14, slot - 4, true);
        for (int s = 0; s < chart.Series.Count; s++)
        {
            context.CheckCancellation(); ChartSeries series = chart.Series[s]; double? previousX = null, previousY = null;
            for (int c = 0; c < series.Values.Count; c++)
            {
                context.CheckCancellation(); double? value = series.Values[c];
                if (!value.HasValue) { previousX = previousY = null; continue; }
                double x = left + (c + 0.5) * slot, y = Y(value.Value);
                if (!Finite(x) || !Finite(y)) throw new InvalidDataException("Chart.Geometry: A plot coordinate is not finite.");
                if (chart.Kind == ChartKind.ClusteredColumn)
                {
                    double width = slot * 0.8 / chart.Series.Count, baseline = Y(0);
                    if (width < 0.25) throw new NotSupportedException("Chart.PointSpacing: Column width is below the qualified geometry bound.");
                    // Zero is a point on the baseline; a gap creates no primitive.
                    result.Shapes.Add(new FixedShape(left + c * slot + slot * 0.1 + s * width, Math.Min(y, baseline), width, Math.Abs(y - baseline), series.Color));
                }
                else
                {
                    if (previousX.HasValue) result.Shapes.Add(new FixedShape(previousX.Value, previousY!.Value, x - previousX.Value, y - previousY.Value, series.Color, 1));
                    previousX = x; previousY = y;
                }
            }
            if (chart.ShowLegend)
            {
                double y = top + 10 + s * 14;
                if (y + 4 > bottom) throw new NotSupportedException("Chart.LabelFit: The legend cannot fit the frame.");
                result.Shapes.Add(new FixedShape(right + 8, y - 6, 8, 6, series.Color)); Text(series.Name, right + 20, y, legend - 22);
            }
        }
        return result;
    }

    private static ObjectVisual Horizontal(DocumentObjectData item, ChartDefinition chart, int font, Func<string, int, double, double> measure,
        ConversionContext context, double min, double max, double step, string[] tickLabels)
    {
        const double size = 8, padding = 8;
        var result = new ObjectVisual { Width = item.Width, Ascent = item.Height };
        double halfTick = tickLabels.Max(label => measure(label, font, size)) / 2;
        double left = padding + Math.Max(chart.Categories.Max(label => measure(label, font, size)) + 6, halfTick);
        double legend = chart.ShowLegend ? chart.Series.Max(series => measure(series.Name, font, size)) + 26 : 0;
        double top = -item.Height + padding + (chart.Title.Length > 0 ? 18 : 0) + (chart.ValueTitle.Length > 0 ? 12 : 0);
        double bottom = chart.CategoryTitle.Length > 0 ? -38 : -24, right = item.Width - padding - legend - halfTick;
        if (!Finite(left) || right - left < 24 || bottom - top < 24) throw new NotSupportedException("Chart.LabelFit: Labels leave insufficient plot space.");
        double plotWidth = right - left, plotHeight = bottom - top, slot = plotHeight / chart.Categories.Count;
        if (slot < size + 2) throw new NotSupportedException("Chart.LabelFit: Category labels do not fit the available height.");
        double X(double value) => left + (value - min) / (max - min) * plotWidth;
        void Text(string value, double x, double y, double available, bool center = false)
        {
            context.CheckCancellation(); double width = measure(value, font, size);
            if (!Finite(width) || width > available) throw new NotSupportedException("Chart.LabelFit: A chart label exceeds its slot; labels are never silently dropped or clipped.");
            if (value.Length != 0) result.Text.Add(new PositionedText(value, center ? x - width / 2 : x, y, size, font));
        }
        if (chart.Title.Length > 0) Text(chart.Title, item.Width / 2, -item.Height + 16, item.Width - 16, true);
        if (chart.ValueTitle.Length > 0) Text(chart.ValueTitle, left, top - 4, plotWidth);
        if (chart.CategoryTitle.Length > 0) Text(chart.CategoryTitle, left + plotWidth / 2, bottom + 30, plotWidth, true);
        double tickSlot = plotWidth / (tickLabels.Length - 1);
        for (int i = 0; i < tickLabels.Length; i++)
        {
            double x = X(min + i * step), width = measure(tickLabels[i], font, size);
            if (!Finite(x) || !Finite(width) || x - width / 2 < padding || x + width / 2 > item.Width - padding)
                throw new NotSupportedException("Chart.LabelFit: A value-axis label exceeds the chart frame.");
            result.Shapes.Add(new FixedShape(x, top, 0, plotHeight, "D0D0D0", 0.5));
            Text(tickLabels[i], x, bottom + 14, tickSlot - 4, true);
        }
        result.Shapes.Add(new FixedShape(left, bottom, plotWidth, 0, "000000", 0.75));
        result.Shapes.Add(new FixedShape(X(0), top, 0, plotHeight, "000000", 0.75));
        for (int c = 0; c < chart.Categories.Count; c++)
        {
            double width = measure(chart.Categories[c], font, size);
            double axis = X(0);
            Text(chart.Categories[c], axis - width - 5, bottom - (c + 0.5) * slot + size / 3, axis - padding - 5);
        }
        // OOXML's default gap is 150 percent of one bar's thickness.
        double height = slot / (chart.Series.Count + 1.5), inset = (slot - chart.Series.Count * height) / 2;
        if (height < 0.25) throw new NotSupportedException("Chart.PointSpacing: Bar height is below the qualified geometry bound.");
        for (int s = 0; s < chart.Series.Count; s++)
        {
            context.CheckCancellation(); ChartSeries series = chart.Series[s];
            for (int c = 0; c < series.Values.Count; c++)
            {
                context.CheckCancellation(); double? value = series.Values[c]; if (!value.HasValue) continue;
                double x = X(value.Value), zero = X(0), y = bottom - (c + 1) * slot + inset + (chart.Series.Count - 1 - s) * height;
                if (!Finite(x) || !Finite(y)) throw new InvalidDataException("Chart.Geometry: A plot coordinate is not finite.");
                result.Shapes.Add(new FixedShape(Math.Min(x, zero), y, Math.Abs(x - zero), height, series.Color));
            }
            if (chart.ShowLegend)
            {
                double y = top + 10 + (chart.Series.Count - 1 - s) * 14;
                if (y + 4 > bottom) throw new NotSupportedException("Chart.LabelFit: The legend cannot fit the frame.");
                result.Shapes.Add(new FixedShape(right + 8, y - 6, 8, 6, series.Color)); Text(series.Name, right + 20, y, legend - 22);
            }
        }
        return result;
    }
    private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
}
