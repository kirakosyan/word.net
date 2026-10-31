using System.Collections.Generic;
using System.Linq;
using WordNet.Model;

namespace WordNet.Layout;

internal sealed class FixedShape
{
    internal FixedShape(double x, double y, double width, double height, string color, double stroke = 0, string style = "single", string? geometry = null, bool foreground = false, TextRun? sourceRun = null, LinearGradient? gradient = null)
    { X = x; Y = y; Width = width; Height = height; Color = color; Stroke = stroke; Style = style; Geometry = geometry; Foreground = foreground; SourceRun = sourceRun; Gradient = gradient; }
    internal double X { get; }
    internal double Y { get; }
    internal double Width { get; }
    internal double Height { get; }
    internal string Color { get; }
    internal double Stroke { get; }
    internal string Style { get; }
    internal string? Geometry { get; }
    internal bool Foreground { get; }
    internal TextRun? SourceRun { get; }
    internal LinearGradient? Gradient { get; }
    internal FixedShape At(double x, double y) => new FixedShape(x, y, Width, Height, Color, Stroke, Style, Geometry, Foreground, SourceRun, Gradient);
    internal static IEnumerable<FixedShape> PaintOrder(IEnumerable<FixedShape> shapes)
    {
        FixedShape[] all = shapes.ToArray();
        foreach (FixedShape vector in all.Where(s => s.Geometry != null)) yield return vector;
        foreach (FixedShape fill in all.Where(s => s.Geometry == null && s.Stroke == 0)) yield return fill;
        foreach (FixedShape diagonal in all.Where(s => s.Geometry == null && s.Stroke > 0 && s.Width != 0 && s.Height != 0)) yield return diagonal;
        // Sweep shared edges, including partial edges of spanning cells. The active
        // set is ordered by border weight/style/source order, with logarithmic updates.
        foreach (var group in all.Where(s => s.Geometry == null && s.Stroke > 0 && (s.Width == 0 || s.Height == 0)).GroupBy(s => (Vertical: s.Width == 0, Coordinate: s.Width == 0 ? s.X : s.Y)))
        {
            var borders = group.Select((shape, index) => (Shape: shape, Index: index)).ToArray();
            var events = borders.SelectMany(b => new[] { (Position: group.Key.Vertical ? b.Shape.Y : b.Shape.X, Start: true, b.Index),
                (Position: group.Key.Vertical ? b.Shape.Y + b.Shape.Height : b.Shape.X + b.Shape.Width, Start: false, b.Index) }).OrderBy(e => e.Position).ToArray();
            var active = new SortedSet<int>(Comparer<int>.Create((a, b) =>
            {
                int order = borders[b].Shape.Stroke.CompareTo(borders[a].Shape.Stroke);
                if (order == 0) order = Rank(borders[a].Shape.Style).CompareTo(Rank(borders[b].Shape.Style));
                return order == 0 ? a.CompareTo(b) : order;
            }));
            int offset = 0;
            while (offset < events.Length)
            {
                double start = events[offset].Position;
                while (offset < events.Length && events[offset].Position == start)
                { var item = events[offset++]; if (item.Start) active.Add(item.Index); else active.Remove(item.Index); }
                if (offset == events.Length || active.Count == 0) continue;
                double length = events[offset].Position - start;
                FixedShape winner = borders[active.Min].Shape;
                yield return new FixedShape(group.Key.Vertical ? group.Key.Coordinate : start, group.Key.Vertical ? start : group.Key.Coordinate,
                    group.Key.Vertical ? 0 : length, group.Key.Vertical ? length : 0, winner.Color, winner.Stroke, winner.Style);
            }
        }
    }

    private static int Rank(string style) => style == "double" ? 0 : style == "single" ? 1 : style == "dashed" ? 2 : 3;
}
