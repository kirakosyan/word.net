using System.Collections.Generic;
using System.IO;
using WordNet.Layout;
using WordNet.Model;

namespace WordNet.Internal;

internal static class GradientBudget
{
    internal static void Check(DocumentModel model, ConversionContext context)
    {
        long stops = 0;
        foreach (ParagraphBlock paragraph in model.Paragraphs)
            foreach (TextRun run in paragraph.Runs)
                if (run.Box?.Gradient is LinearGradient gradient)
                {
                    context.CheckCancellation(); context.HasGradient = true;
                    stops = Add(stops, gradient, context);
                }
    }

    internal static void CheckPages(IEnumerable<FixedPage> pages, ConversionContext context)
    {
        long stops = 0;
        foreach (FixedPage page in pages)
            foreach (FixedShape shape in page.Shapes)
                if (shape.Gradient is LinearGradient gradient)
                {
                    context.CheckCancellation(); stops = Add(stops, gradient, context);
                }
    }

    private static long Add(long stops, LinearGradient gradient, ConversionContext context)
    {
        stops = checked(stops + gradient.Stops.Length);
        if (stops > 4096 || stops * 512 > context.Options.MaximumExpandedBytes)
            throw new InvalidDataException("Limit.GradientPaint: Aggregate gradient paint resources exceed the document budget.");
        return stops;
    }
}
