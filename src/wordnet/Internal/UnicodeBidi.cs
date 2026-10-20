using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using K = WordNet.Internal.UnicodeBidiData.Kind;

namespace WordNet.Internal;

// UAX #9, Unicode 17. Logical scalar indices are retained independently of L2 order.
// The converter uses balanced controls and a smaller nesting policy; conformance
// exercises the specification's unmatched/overflow behavior separately.
internal static class UnicodeBidi
{
    internal sealed class Result
    {
        internal int[] Scalars { get; set; } = Array.Empty<int>();
        internal int[] Offsets { get; set; } = Array.Empty<int>();
        internal int[] Levels { get; set; } = Array.Empty<int>();
        internal int[] Order { get; set; } = Array.Empty<int>();
        internal int BaseLevel { get; set; }
    }
    internal static bool Format(int c) => c is >= 0x202a and <= 0x202e or >= 0x2066 and <= 0x2069 or 0x200e or 0x200f or 0x061c;
    internal static Result Resolve(string text, int direction, ConversionContext context, bool balanced = true, int maximumDepth = 32)
    {
        if (text.Length > 131072 || text.Length * 256L > context.Options.MaximumExpandedBytes) throw new InvalidDataException("Typography.BidiBudget: Paragraph bidi work exceeds its allowance.");
        var scalars = new List<int>(); var offsets = new List<int>();
        for (int i = 0; i < text.Length; i++)
        { context.CheckCancellation(); offsets.Add(i); int c = char.ConvertToUtf32(text, i); scalars.Add(c); if (c > 0xffff) i++; }
        var original = scalars.Select(UnicodeBidiData.Class).ToArray(); var types = (K[])original.Clone(); int count = types.Length;
        int[] matching = Enumerable.Repeat(-1, count).ToArray(); var isolates = new Stack<int>();
        for (int i = 0; i < count; i++)
        {
            if (Initiator(types[i])) isolates.Push(i);
            else if (types[i] == K.PDI && isolates.Count > 0) { int open = isolates.Pop(); matching[open] = i; matching[i] = open; }
            else if (types[i] == K.PDI && balanced) throw Controls();
        }
        if (balanced && isolates.Count > 0) throw Controls();
        int FirstStrong(int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                if (original[i] is K.L or K.R or K.AL) return original[i] == K.L ? 0 : 1;
                if (Initiator(original[i])) { if (matching[i] < 0) break; i = matching[i]; }
            }
            return -1;
        }
        int basis = direction is 0 or 1 ? direction : Math.Max(0, FirstStrong(0, count));
        var levels = new int[count]; var stack = new List<(int Level, K Override, bool Isolate)> { (basis, K.ON, false) };
        int overflowIsolate = 0, overflowEmbedding = 0, validIsolate = 0;
        for (int i = 0; i < count; i++)
        {
            context.CheckCancellation(); K kind = types[i]; var current = stack[stack.Count - 1]; levels[i] = current.Level;
            if (kind is K.RLE or K.LRE or K.RLO or K.LRO || Initiator(kind))
            {
                bool isolate = Initiator(kind); bool rtl = kind is K.RLE or K.RLO or K.RLI || kind == K.FSI && FirstStrong(i + 1, matching[i] < 0 ? count : matching[i]) == 1;
                int next = rtl ? (current.Level + 1) | 1 : (current.Level + 2) & ~1;
                if (isolate && current.Override is K.L or K.R) types[i] = current.Override;
                if (balanced && stack.Count >= maximumDepth + 1) throw new NotSupportedException("Typography.BidiDepth: Directional nesting exceeds the configured bound.");
                if (next <= 125 && overflowIsolate == 0 && overflowEmbedding == 0)
                { stack.Add((next, kind == K.RLO ? K.R : kind == K.LRO ? K.L : K.ON, isolate)); if (isolate) validIsolate++; }
                else if (isolate) overflowIsolate++; else if (overflowIsolate == 0) overflowEmbedding++;
            }
            else if (kind == K.PDI)
            {
                if (overflowIsolate > 0) overflowIsolate--;
                else if (validIsolate > 0)
                {
                    overflowEmbedding = 0;
                    while (!stack[stack.Count - 1].Isolate) stack.RemoveAt(stack.Count - 1);
                    stack.RemoveAt(stack.Count - 1); validIsolate--;
                }
                current = stack[stack.Count - 1]; levels[i] = current.Level;
                if (current.Override is K.L or K.R) types[i] = current.Override;
            }
            else if (kind == K.PDF)
            {
                if (overflowIsolate == 0)
                {
                    if (overflowEmbedding > 0) overflowEmbedding--;
                    else if (stack.Count > 1 && !current.Isolate) stack.RemoveAt(stack.Count - 1);
                    else if (balanced) throw Controls();
                }
            }
            else if (kind == K.B)
            {
                if (balanced && stack.Count > 1) throw Controls();
                levels[i] = basis; stack.RemoveRange(1, stack.Count - 1); overflowEmbedding = overflowIsolate = validIsolate = 0;
            }
            else if (current.Override is K.L or K.R && kind != K.BN) types[i] = current.Override;
        }
        if (balanced && stack.Count > 1) throw Controls();
        int[] active = Enumerable.Range(0, count).Where(i => !Removed(original[i])).ToArray();
        var runs = new List<List<int>>(); var runAt = new int[count];
        foreach (int index in active)
        {
            if (runs.Count == 0 || levels[runs[runs.Count - 1][0]] != levels[index]) runs.Add(new List<int>());
            runAt[index] = runs.Count - 1; runs[runs.Count - 1].Add(index);
        }
        var nextRun = Enumerable.Repeat(-1, runs.Count).ToArray(); var incoming = new bool[runs.Count];
        for (int i = 0; i < runs.Count; i++)
        {
            int last = runs[i][runs[i].Count - 1];
            if (Initiator(original[last]) && matching[last] >= 0)
            { int next = runAt[matching[last]]; nextRun[i] = next; incoming[next] = true; }
        }
        int[] embeddingLevels = (int[])levels.Clone();
        var positions = new int[count]; for (int i = 0; i < active.Length; i++) positions[active[i]] = i;
        for (int r = 0; r < runs.Count; r++)
        {
            if (incoming[r]) continue;
            var sequence = new List<int>(); int next = r;
            while (next >= 0) { sequence.AddRange(runs[next]); next = nextRun[next]; }
            int first = sequence[0], last = sequence[sequence.Count - 1]; int before = positions[first] - 1, after = positions[last] + 1;
            K sos = Direction(Math.Max(embeddingLevels[first], before >= 0 ? embeddingLevels[active[before]] : basis));
            K eos = Direction(Math.Max(embeddingLevels[last], after < active.Length && !Initiator(original[last]) ? embeddingLevels[active[after]] : basis));
            ResolveSequence(sequence, types, original, scalars, levels, sos, eos, context);
        }
        // L1 resets line/paragraph separators and whitespace before them and at end.
        for (int i = 0; i < count; i++) if (original[i] is K.B or K.S)
        { levels[i] = basis; for (int j = i - 1; j >= 0 && Whitespace(original[j]); j--) levels[j] = basis; }
        for (int i = count - 1; i >= 0 && Whitespace(original[i]); i--) levels[i] = basis;
        foreach (int i in Enumerable.Range(0, count).Where(i => Removed(original[i]))) levels[i] = -1;
        int[] order = Reorder(active, levels);
        return new Result { Scalars = scalars.ToArray(), Offsets = offsets.ToArray(), Levels = levels, Order = order, BaseLevel = basis };
    }
    internal static int[] Reorder(int[] indices, int[] levels)
    {
        int[] order = (int[])indices.Clone(); int maximum = indices.Select(i => levels[i]).DefaultIfEmpty(0).Max();
        int minimumOdd = indices.Where(i => (levels[i] & 1) != 0).Select(i => levels[i]).DefaultIfEmpty(maximum + 1).Min();
        for (int level = maximum; level >= minimumOdd; level--)
            for (int i = 0; i < order.Length; i++) if (levels[order[i]] >= level)
            { int end = i + 1; while (end < order.Length && levels[order[end]] >= level) end++; Array.Reverse(order, i, end - i); i = end - 1; }
        return order;
    }
    private static void ResolveSequence(List<int> sequence, K[] types, K[] original, List<int> scalars, int[] levels, K sos, K eos, ConversionContext context)
    {
        K previous = sos;
        // W1.
        foreach (int i in sequence) { if (types[i] == K.NSM) types[i] = previous is K.LRI or K.RLI or K.FSI or K.PDI ? K.ON : previous; previous = types[i]; }
        // W2, W3.
        K strong = sos;
        foreach (int i in sequence)
        { if (types[i] == K.EN && strong == K.AL) types[i] = K.AN; if (types[i] is K.R or K.L or K.AL) strong = types[i]; if (types[i] == K.AL) types[i] = K.R; }
        // W4.
        for (int j = 1; j + 1 < sequence.Count; j++)
        {
            int i = sequence[j]; K a = types[sequence[j - 1]], b = types[sequence[j + 1]];
            if (types[i] == K.ES && a == K.EN && b == K.EN || types[i] == K.CS && a == b && a is K.EN or K.AN) types[i] = a;
        }
        // W5.
        for (int j = 0; j < sequence.Count; j++) if (types[sequence[j]] == K.ET)
        {
            int end = j + 1; while (end < sequence.Count && types[sequence[end]] == K.ET) end++;
            if (j > 0 && types[sequence[j - 1]] == K.EN || end < sequence.Count && types[sequence[end]] == K.EN)
                for (int k = j; k < end; k++) types[sequence[k]] = K.EN;
            j = end - 1;
        }
        // W6, W7.
        foreach (int i in sequence) if (types[i] is K.ES or K.ET or K.CS) types[i] = K.ON;
        strong = sos;
        foreach (int i in sequence) { if (types[i] == K.EN && strong == K.L) types[i] = K.L; if (types[i] is K.L or K.R) strong = types[i]; }
        // N0 paired bracket discovery; canonical-equivalent angle brackets share pairs.
        int Canonical(int c) => c == 0x2329 ? 0x3008 : c == 0x232a ? 0x3009 : c;
        var opens = new List<(int Position, int Close)>(); var pairs = new List<(int Open, int Close)>();
        for (int j = 0; j < sequence.Count; j++)
        {
            context.CheckCancellation(); int i = sequence[j]; if (types[i] != K.ON) continue;
            var bracket = UnicodeBidiData.Bracket(scalars[i]); if (!bracket.HasValue) continue;
            if (bracket.Value.Open)
            { if (opens.Count >= 63) break; opens.Add((j, Canonical(bracket.Value.Pair))); }
            else
                for (int k = opens.Count - 1; k >= 0; k--) if (opens[k].Close == Canonical(scalars[i]))
                { pairs.Add((opens[k].Position, j)); opens.RemoveRange(k, opens.Count - k); break; }
        }
        foreach (var pair in pairs.OrderBy(p => p.Open))
        {
            K embedding = Direction(levels[sequence[pair.Open]]), opposite = embedding == K.L ? K.R : K.L;
            bool oppositeFound = false, embeddingFound = false;
            for (int j = pair.Open + 1; j < pair.Close; j++)
            { K t = Strong(types[sequence[j]]); if (t == embedding) { embeddingFound = true; break; } if (t == opposite) oppositeFound = true; }
            K resolved;
            if (embeddingFound) resolved = embedding;
            else if (oppositeFound)
            {
                K preceding = sos;
                for (int j = pair.Open - 1; j >= 0; j--) { K t = Strong(types[sequence[j]]); if (t is K.L or K.R) { preceding = t; break; } }
                resolved = preceding == opposite ? opposite : embedding;
            }
            else continue;
            foreach (int at in new[] { pair.Open, pair.Close })
            { types[sequence[at]] = resolved; for (int j = at + 1; j < sequence.Count && original[sequence[j]] == K.NSM; j++) types[sequence[j]] = resolved; }
        }
        // N1/N2.
        for (int j = 0; j < sequence.Count; j++) if (Neutral(types[sequence[j]]))
        {
            int end = j + 1; while (end < sequence.Count && Neutral(types[sequence[end]])) end++;
            K a = j == 0 ? sos : Strong(types[sequence[j - 1]]), b = end == sequence.Count ? eos : Strong(types[sequence[end]]);
            K resolved = a == b ? a : Direction(levels[sequence[j]]);
            for (int k = j; k < end; k++) types[sequence[k]] = resolved;
            j = end - 1;
        }
        // I1/I2.
        foreach (int i in sequence)
        { if ((levels[i] & 1) == 0) { if (types[i] == K.R) levels[i]++; else if (types[i] is K.EN or K.AN) levels[i] += 2; } else if (types[i] is K.L or K.EN or K.AN) levels[i]++; }
    }
    private static bool Initiator(K kind) => kind is K.LRI or K.RLI or K.FSI;
    private static bool Removed(K kind) => kind is K.RLE or K.LRE or K.RLO or K.LRO or K.PDF or K.BN;
    private static bool Whitespace(K kind) => kind == K.WS || Initiator(kind) || kind == K.PDI || Removed(kind);
    private static bool Neutral(K kind) => kind is K.B or K.S or K.WS or K.ON or K.LRI or K.RLI or K.FSI or K.PDI;
    private static K Strong(K kind) => kind is K.EN or K.AN ? K.R : kind;
    private static K Direction(int level) => (level & 1) == 0 ? K.L : K.R;
    private static NotSupportedException Controls() => new NotSupportedException("Typography.BidiControls: Directional controls must be balanced within each ordinary paragraph.");
}
