using System;
using System.Globalization;
using System.Text;
using WordNet.Formats;
using WordNet.Model;

namespace WordNet.Internal;

internal static class ObjectAlternatives
{
    internal static string Describe(DocumentObjectData item, ConversionContext context)
    {
        if (string.IsNullOrWhiteSpace(item.Description))
            throw new NotSupportedException("Object.Alternative: Tagged objects require a caller-supplied meaningful description.");
        var text = new StringBuilder(item.Description);
        if (item.Chart is ChartDefinition chart)
        {
            if (chart.Title.Length != 0) { Add(". Chart: "); Add(chart.Title); }
            if (chart.CategoryTitle.Length != 0) { Add(". Categories: "); Add(chart.CategoryTitle); }
            if (chart.ValueTitle.Length != 0) { Add(". Values: "); Add(chart.ValueTitle); }
            for (int i = 0; i < chart.Categories.Count; i++)
            {
                Add(". "); Add(chart.Categories[i]);
                foreach (ChartSeries series in chart.Series)
                {
                    Add("; "); Add(series.Name); Add(" = ");
                    Add(series.Values[i]?.ToString("R", CultureInfo.InvariantCulture) ?? "missing");
                }
            }
        }
        else
        {
            if (item.Equation!.NodeCount > 256 || item.Equation.Characters > 2048)
                throw new NotSupportedException("Equation.Alternative: Tagged equation alternatives are bounded to 256 nodes and 2,048 text characters.");
            Add(". Equation: "); Add(MathObjectFormat.Description(item.Equation));
        }
        return text.ToString();

        void Add(string value)
        {
            context.CheckCancellation();
            if ((long)text.Length + value.Length > Math.Min(65536, context.Options.MaximumCharacters))
                throw new NotSupportedException("Object.AlternativeLimit: The logical alternative exceeds its character allowance.");
            text.Append(value);
        }
    }
}
