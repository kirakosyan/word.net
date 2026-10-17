using System;
using System.Collections.Generic;
using System.Linq;

namespace WordNet.Internal;

internal static class PageSelection
{
    internal static int[] Resolve(IEnumerable<PageRange> ranges, int total)
    {
        var pages = new List<int>(); int previous = 0;
        foreach (PageRange range in ranges)
        {
            if (range == null || range.First <= previous || range.Last > total) throw new ArgumentException("Page ranges must be ascending, nonoverlapping and within the full physical page count.", nameof(ranges));
            for (int p = range.First; p <= range.Last; p++) pages.Add(p);
            previous = range.Last;
        }
        return previous == 0 ? Enumerable.Range(1, total).ToArray() : pages.ToArray();
    }
}
