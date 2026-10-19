using System;
using System.Collections.Generic;
using System.IO;
using C = WordNet.Internal.UnicodeBoundaryData.Conjunct;
using G = WordNet.Internal.UnicodeBoundaryData.Grapheme;
using W = WordNet.Internal.UnicodeBoundaryData.Word;

namespace WordNet.Internal;

// UAX #29 default extended grapheme clusters and default word boundaries, Unicode 17.
// No runtime Unicode data, normalization, case folding or language tailoring.
internal static class UnicodeBoundaries
{
    private sealed class Scalar
    {
        internal int Offset;
        internal G Grapheme;
        internal W Word;
        internal C Conjunct;
        internal bool Pictographic;
    }
    internal static List<int> Find(string text, bool words, ConversionContext context, int maximumTokens = 100000)
    {
        Guard.NotNull(text, nameof(text)); context.CheckCharacters(text.Length);
        if (text.Length * 64L > context.Options.MaximumExpandedBytes) throw new InvalidDataException("Comparison.ResourceLimit: Segmentation exceeds the resource allowance.");
        var scalars = new List<Scalar>();
        for (int offset = 0; offset < text.Length; offset++)
        {
            context.CheckCancellation(); int start = offset, value = text[offset];
            if (char.IsHighSurrogate(text[offset]))
            {
                if (offset + 1 >= text.Length || !char.IsLowSurrogate(text[offset + 1])) throw new ArgumentException("Review.Unicode: Malformed UTF-16.", nameof(text));
                value = char.ConvertToUtf32(text[offset], text[++offset]);
            }
            else if (char.IsLowSurrogate(text[offset])) throw new ArgumentException("Review.Unicode: Malformed UTF-16.", nameof(text));
            scalars.Add(new Scalar
            {
                Offset = start,
                Grapheme = UnicodeBoundaryData.GetGrapheme(value),
                Word = UnicodeBoundaryData.GetWord(value),
                Conjunct = UnicodeBoundaryData.GetConjunct(value),
                Pictographic = UnicodeBoundaryData.GetPictographic(value) != UnicodeBoundaryData.Pictographic.Other
            });
        }
        var output = new List<int> { 0 };
        int lastSignificant = -1, beforeSignificant = -1, regionalWords = 0, regionalGraphemes = 0;
        bool extendedPictographic = false, zwjAfterPictographic = false, conjunctConsonant = false, conjunctLinker = false;
        for (int i = 0; i < scalars.Count; i++)
        {
            context.CheckCancellation(); Scalar current = scalars[i];
            if (i > 0)
            {
                Scalar previous = scalars[i - 1];
                bool boundary = words ? WordBreak(i) : GraphemeBreak(previous, current);
                if (boundary)
                {
                    if (output.Count >= maximumTokens) throw new InvalidDataException("Comparison.TokenLimit: Segmentation exceeds the token allowance.");
                    output.Add(current.Offset);
                }
            }
            if (!Ignored(current.Word))
            {
                beforeSignificant = lastSignificant; lastSignificant = i;
                regionalWords = current.Word == W.Regional_Indicator ? regionalWords + 1 : 0;
            }
            regionalGraphemes = current.Grapheme == G.Regional_Indicator ? regionalGraphemes + 1 : 0;
            zwjAfterPictographic = current.Grapheme == G.ZWJ && extendedPictographic;
            if (current.Pictographic) extendedPictographic = true;
            else if (current.Grapheme != G.Extend) extendedPictographic = false;
            if (current.Conjunct == C.Consonant) { conjunctConsonant = true; conjunctLinker = false; }
            else if (current.Conjunct == C.Linker) conjunctLinker |= conjunctConsonant;
            else if (current.Conjunct != C.Extend) { conjunctConsonant = false; conjunctLinker = false; }
        }
        if (text.Length != 0) output.Add(text.Length); return output;

        bool GraphemeBreak(Scalar previous, Scalar current)
        {
            G a = previous.Grapheme, b = current.Grapheme;
            if (a == G.CR && b == G.LF) return false; // GB3
            if (Control(a) || Control(b)) return true; // GB4/5
            if (a == G.L && b is G.L or G.V or G.LV or G.LVT) return false; // GB6
            if (a is G.LV or G.V && b is G.V or G.T) return false; // GB7
            if (a is G.LVT or G.T && b == G.T) return false; // GB8
            if (b is G.Extend or G.ZWJ or G.SpacingMark || a == G.Prepend) return false; // GB9/9a/9b
            if (current.Conjunct == C.Consonant && conjunctConsonant && conjunctLinker) return false; // GB9c
            if (current.Pictographic && zwjAfterPictographic) return false; // GB11
            if (a == G.Regional_Indicator && b == G.Regional_Indicator && regionalGraphemes % 2 == 1) return false; // GB12/13
            return true;
        }
        bool WordBreak(int index)
        {
            Scalar current = scalars[index], previous = scalars[index - 1]; W immediate = previous.Word, b = current.Word;
            if (immediate == W.CR && b == W.LF) return false; // WB3
            if (Newline(immediate) || Newline(b)) return true; // WB3a/b
            if (immediate == W.ZWJ && current.Pictographic) return false; // WB3c
            if (immediate == W.WSegSpace && b == W.WSegSpace) return false; // WB3d
            if (Ignored(b)) return false; // WB4
            if (lastSignificant < 0) return true;
            W a = scalars[lastSignificant].Word;
            W before = beforeSignificant < 0 ? W.Other : scalars[beforeSignificant].Word;
            // Only mid-letter/number rules need a forward look; each such node is visited at most once.
            W after = W.Other;
            if (MidLetter(b) || MidNumber(b) || b == W.Double_Quote)
                for (int next = index + 1; next < scalars.Count; next++)
                { context.CheckCancellation(); if (!Ignored(scalars[next].Word)) { after = scalars[next].Word; break; } }
            if (Letter(a) && Letter(b)) return false; // WB5
            if (Letter(a) && MidLetter(b) && Letter(after)) return false; // WB6
            if (Letter(before) && MidLetter(a) && Letter(b)) return false; // WB7
            if (a == W.Hebrew_Letter && b == W.Single_Quote) return false; // WB7a
            if (a == W.Hebrew_Letter && b == W.Double_Quote && after == W.Hebrew_Letter) return false; // WB7b
            if (before == W.Hebrew_Letter && a == W.Double_Quote && b == W.Hebrew_Letter) return false; // WB7c
            if (a == W.Numeric && b == W.Numeric || Letter(a) && b == W.Numeric || a == W.Numeric && Letter(b)) return false; // WB8/9/10
            if (before == W.Numeric && MidNumber(a) && b == W.Numeric || a == W.Numeric && MidNumber(b) && after == W.Numeric) return false; // WB11/12
            if (a == W.Katakana && b == W.Katakana) return false; // WB13
            if ((Letter(a) || a is W.Numeric or W.Katakana or W.ExtendNumLet) && b == W.ExtendNumLet) return false; // WB13a
            if (a == W.ExtendNumLet && (Letter(b) || b is W.Numeric or W.Katakana)) return false; // WB13b
            if (a == W.Regional_Indicator && b == W.Regional_Indicator && regionalWords % 2 == 1) return false; // WB15/16
            return true;
        }
    }
    private static bool Control(G value) => value is G.Control or G.CR or G.LF;
    private static bool Newline(W value) => value is W.Newline or W.CR or W.LF;
    private static bool Ignored(W value) => value is W.Extend or W.Format or W.ZWJ;
    private static bool Letter(W value) => value is W.ALetter or W.Hebrew_Letter;
    private static bool MidLetter(W value) => value is W.MidLetter or W.MidNumLet or W.Single_Quote;
    private static bool MidNumber(W value) => value is W.MidNum or W.MidNumLet or W.Single_Quote;
}
