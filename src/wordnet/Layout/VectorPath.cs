using System;
using System.Collections.Generic;

namespace WordNet.Layout;

internal static class VectorPath
{
    internal readonly struct Command
    {
        internal Command(char kind, params double[] values) { Kind = kind; Values = values; }
        internal char Kind { get; }
        internal double[] Values { get; }
    }

    internal static List<Command> Create(FixedShape shape)
    {
        double x = shape.X, y = shape.Y, w = shape.Width, h = shape.Height;
        var result = new List<Command>();
        void Move(double a, double b) => result.Add(new Command('M', a, b));
        void Line(double a, double b) => result.Add(new Command('L', a, b));
        void Curve(double a, double b, double c, double d, double e, double f) => result.Add(new Command('C', a, b, c, d, e, f));
        if (shape.Geometry == "line") { Move(x, y); Line(x + w, y + h); return result; }
        const double k = 0.5522847498307936;
        if (shape.Geometry == "ellipse")
        {
            double rx = w / 2, ry = h / 2, cx = x + rx, cy = y + ry;
            Move(cx, y); Curve(cx + k * rx, y, x + w, cy - k * ry, x + w, cy);
            Curve(x + w, cy + k * ry, cx + k * rx, y + h, cx, y + h);
            Curve(cx - k * rx, y + h, x, cy + k * ry, x, cy);
            Curve(x, cy - k * ry, cx - k * rx, y, cx, y);
        }
        else if (shape.Geometry == "roundRect")
        {
            double r = Math.Min(w, h) / 6;
            Move(x + r, y); Line(x + w - r, y); Curve(x + w - r + k * r, y, x + w, y + r - k * r, x + w, y + r);
            Line(x + w, y + h - r); Curve(x + w, y + h - r + k * r, x + w - r + k * r, y + h, x + w - r, y + h);
            Line(x + r, y + h); Curve(x + r - k * r, y + h, x, y + h - r + k * r, x, y + h - r);
            Line(x, y + r); Curve(x, y + r - k * r, x + r - k * r, y, x + r, y);
        }
        else throw new InvalidOperationException("Shape.Vector: Unknown vector geometry.");
        result.Add(new Command('Z')); return result;
    }
}
