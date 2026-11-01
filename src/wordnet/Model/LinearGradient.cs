namespace WordNet.Model;

// A deliberately finite, opaque DrawingML linear-gradient profile. Stops are
// stored as integer DrawingML percentages so editable output retains their values.
internal sealed class LinearGradient
{
    internal LinearGradient(int angle, bool scaled, GradientStop[] stops)
    { Angle = angle; Scaled = scaled; Stops = stops; }
    internal int Angle { get; }
    internal bool Scaled { get; }
    internal GradientStop[] Stops { get; }

    internal (double X1, double Y1, double X2, double Y2) Endpoints(double x, double y, double width, double height) => Angle switch
    {
        0 => (x, y + height / 2, x + width, y + height / 2),
        5400000 => (x + width / 2, y, x + width / 2, y + height),
        10800000 => (x + width, y + height / 2, x, y + height / 2),
        16200000 => (x + width / 2, y + height, x + width / 2, y),
        _ => throw new System.InvalidOperationException("Shape.Gradient: Unqualified gradient angle.")
    };
}

internal sealed class GradientStop
{
    internal GradientStop(int position, string color, string? theme = null, bool mappingDifference = false)
    { Position = position; Color = color; Theme = theme; MappingDifference = mappingDifference; }
    internal int Position { get; }
    internal string Color { get; }
    internal string? Theme { get; }
    internal bool MappingDifference { get; }
}
