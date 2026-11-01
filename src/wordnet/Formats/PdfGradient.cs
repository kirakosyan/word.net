using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UglyToad.PdfPig.Tokens;
using WordNet.Internal;
using WordNet.Layout;
using WordNet.Model;

namespace WordNet.Formats;

internal static class PdfGradient
{
    private static readonly double[] EncodePair = { 0, 1 };
    internal static (Dictionary<FixedShape, NameToken> Names, DictionaryToken? Resources) Create(IReadOnlyList<FixedPage> pages, ConversionContext context)
    {
        var names = new Dictionary<FixedShape, NameToken>();
        var resources = new Dictionary<NameToken, IToken>();
        foreach (FixedPage page in pages)
            foreach (FixedShape shape in page.Shapes.Where(shape => shape.Gradient != null))
            {
                context.CheckCancellation();
                if (names.ContainsKey(shape)) throw new InvalidDataException("Shape.Gradient: A paint is shared by multiple physical pages.");
                if ((long)(names.Count + 1) * pages.Count > 4096 || (long)(names.Count + 1) * pages.Count * 4096 > context.Options.MaximumExpandedBytes)
                    throw new InvalidDataException("Limit.GradientPaint: Expanded PDF shading resources exceed the document budget.");
                NameToken name = N("WNGrad" + names.Count.ToString(CultureInfo.InvariantCulture));
                names.Add(shape, name); resources.Add(name, Shading(shape, page.Settings.Height));
            }
        return (names, resources.Count == 0 ? null : new DictionaryToken(resources));
    }

    private static DictionaryToken Shading(FixedShape shape, double pageHeight)
    {
        LinearGradient gradient = shape.Gradient!;
        var ends = gradient.Endpoints(shape.X, shape.Y, shape.Width, shape.Height);
        var stops = new List<GradientStop>(gradient.Stops);
        if (stops[0].Position != 0) stops.Insert(0, new GradientStop(0, stops[0].Color));
        if (stops[stops.Count - 1].Position != 100000) stops.Add(new GradientStop(100000, stops[stops.Count - 1].Color));
        var functions = new List<IToken>();
        for (int i = 1; i < stops.Count; i++)
            functions.Add(D(("FunctionType", new NumericToken(2)), ("Domain", Numbers(0, 1)), ("C0", Color(stops[i - 1].Color)),
                ("C1", Color(stops[i].Color)), ("N", new NumericToken(1))));
        IToken function = functions.Count == 1 ? functions[0] : D(("FunctionType", new NumericToken(3)), ("Domain", Numbers(0, 1)),
            ("Functions", new ArrayToken(functions)), ("Bounds", Numbers(stops.Skip(1).Take(stops.Count - 2).Select(stop => stop.Position / 100000.0).ToArray())),
            ("Encode", Numbers(Enumerable.Range(0, functions.Count).SelectMany(_ => EncodePair).ToArray())));
        return D(("ShadingType", new NumericToken(2)), ("ColorSpace", N("DeviceRGB")),
            ("Coords", Numbers(ends.X1, pageHeight - ends.Y1, ends.X2, pageHeight - ends.Y2)),
            ("Function", function), ("Extend", new ArrayToken(new IToken[] { BooleanToken.True, BooleanToken.True })));
    }

    private static ArrayToken Color(string color) => Numbers(Enumerable.Range(0, 3).Select(i => Convert.ToInt32(color.Substring(i * 2, 2), 16) / 255.0).ToArray());
    private static ArrayToken Numbers(params double[] values) => new ArrayToken(values.Select(value => (IToken)new NumericToken(value)).ToArray());
    private static NameToken N(string value) => NameToken.Create(value);
    private static DictionaryToken D(params (string Key, IToken Value)[] values) => new DictionaryToken(values.ToDictionary(value => N(value.Key), value => value.Value));
}
