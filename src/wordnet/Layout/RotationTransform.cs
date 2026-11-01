using System;

namespace WordNet.Layout;

internal static class RotationTransform
{
    internal static (double Cosine, double Sine) Components(double degrees)
    {
        if (degrees == 0) return (1, 0);
        if (degrees == 90) return (0, 1);
        if (degrees == 180) return (-1, 0);
        if (degrees == 270) return (0, -1);
        double radians = degrees * Math.PI / 180;
        return (Math.Cos(radians), Math.Sin(radians));
    }

    internal static (double X, double Y) Point(double x, double y, double centerX, double centerY, double degrees)
    {
        var (c, s) = Components(degrees); double dx = x - centerX, dy = y - centerY;
        return (centerX + c * dx - s * dy, centerY + s * dx + c * dy);
    }
}
