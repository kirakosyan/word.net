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
        if (shape.Geometry == "sector")
        {
            double cx = x + w / 2, cy = y + h / 2, rx = w / 2, ry = h / 2;
            double start = shape.SectorStart, sweep = shape.SectorSweep;
            if (double.IsNaN(start) || double.IsInfinity(start) || double.IsNaN(sweep) || double.IsInfinity(sweep)
                || sweep <= 0 || sweep > 2 * Math.PI + 1e-10 || double.IsNaN(shape.SectorHole) || double.IsInfinity(shape.SectorHole) || shape.SectorHole < 0 || shape.SectorHole >= 1)
                throw new InvalidOperationException("Chart.Sector: Invalid bounded sector geometry.");
            void Arc(double radius, double begin, double delta)
            {
                int count = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2)));
                double step = delta / count;
                for (int i = 0; i < count; i++)
                {
                    double a = begin + i * step, b = a + step, kappa = 4d / 3 * Math.Tan(step / 4);
                    double ax = cx + radius * rx * Math.Cos(a), ay = cy + radius * ry * Math.Sin(a);
                    double bx = cx + radius * rx * Math.Cos(b), by = cy + radius * ry * Math.Sin(b);
                    Curve(ax - kappa * radius * rx * Math.Sin(a), ay + kappa * radius * ry * Math.Cos(a),
                        bx + kappa * radius * rx * Math.Sin(b), by - kappa * radius * ry * Math.Cos(b), bx, by);
                }
            }
            Move(cx + rx * Math.Cos(start), cy + ry * Math.Sin(start));
            Arc(1, start, sweep);
            if (shape.SectorHole == 0) Line(cx, cy);
            else
            {
                double end = start + sweep;
                Line(cx + shape.SectorHole * rx * Math.Cos(end), cy + shape.SectorHole * ry * Math.Sin(end));
                Arc(shape.SectorHole, end, -sweep);
            }
            result.Add(new Command('Z')); return result;
        }
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
        else if (shape.Geometry == "rect")
        {
            Move(x, y); Line(x + w, y); Line(x + w, y + h); Line(x, y + h);
        }
        else if (shape.Geometry == "triangle")
        {
            Move(x + w / 2, y); Line(x + w, y + h); Line(x, y + h);
        }
        else if (shape.Geometry == "diamond")
        {
            Move(x + w / 2, y); Line(x + w, y + h / 2); Line(x + w / 2, y + h); Line(x, y + h / 2);
        }
        else throw new InvalidOperationException("Shape.Vector: Unknown vector geometry.");
        result.Add(new Command('Z')); return result;
    }
}
