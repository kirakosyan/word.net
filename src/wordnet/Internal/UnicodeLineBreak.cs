using System;
using System.Collections.Generic;
using System.IO;
using B = WordNet.Internal.UnicodeLineBreakData.Break;
using C = WordNet.Internal.UnicodeLineBreakData.Category;
using E = WordNet.Internal.UnicodeLineBreakData.EastAsian;

namespace WordNet.Internal;

// UAX #14 default line-break opportunities, Unicode 17.0.0. No runtime Unicode data.
internal static class UnicodeLineBreak
{
    private sealed class Scalar
    {
        internal int Offset, Value, Previous, Next, BeforeSpaces, RegionalCount;
        internal B Original, Class;
        internal C Category;
        internal bool EastAsian, Pictographic, Ignored, NumericSequence;
    }

    // Offsets are UTF-16 positions at which a break is permitted, including end of text.
    internal static List<int> Find(string text, ConversionContext context, int maximumBreaks = 100000)
    {
        Guard.NotNull(text, nameof(text)); context.CheckCharacters(text.Length);
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBreaks);
#else
        if (maximumBreaks <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBreaks));
#endif
        if (text.Length * 80L > context.Options.MaximumExpandedBytes) throw new InvalidDataException("Layout.LineBreakLimit: Segmentation exceeds the resource allowance.");
        var scalars = new List<Scalar>(); int previous = -1, beforeSpaces = -1;
        for (int offset = 0; offset < text.Length; offset++)
        {
            context.CheckCancellation(); int start = offset, value = text[offset];
            if (char.IsHighSurrogate(text[offset]))
            {
                if (offset + 1 >= text.Length || !char.IsLowSurrogate(text[offset + 1])) throw new ArgumentException("Layout.Unicode: Malformed UTF-16.", nameof(text));
                value = char.ConvertToUtf32(text[offset], text[++offset]);
            }
            else if (char.IsLowSurrogate(text[offset])) throw new ArgumentException("Layout.Unicode: Malformed UTF-16.", nameof(text));
            B original = UnicodeLineBreakData.GetBreak(value); C category = UnicodeLineBreakData.GetCategory(value);
            B cls = original is B.AI or B.SG or B.XX ? B.AL : original == B.CJ ? B.NS : original == B.SA ? category is C.Mn or C.Mc ? B.CM : B.AL : original;
            var item = new Scalar
            {
                Offset = start,
                Value = value,
                Original = original,
                Class = cls,
                Category = category,
                EastAsian = UnicodeLineBreakData.GetEastAsian(value) == E.Wide,
                Pictographic = UnicodeBoundaryData.GetPictographic(value) != UnicodeBoundaryData.Pictographic.Other,
                Previous = previous,
                BeforeSpaces = beforeSpaces
            };
            if (cls is B.CM or B.ZWJ)
            {
                if (previous >= 0 && scalars[previous].Class is not (B.BK or B.CR or B.LF or B.NL or B.SP or B.ZW))
                    item.Ignored = true;
                else { item.Class = B.AL; item.Category = C.Other; item.EastAsian = false; item.Pictographic = false; }
            }
            if (!item.Ignored)
            {
                item.NumericSequence = item.Class == B.NU || item.Class is B.SY or B.IS && previous >= 0 && scalars[previous].NumericSequence;
                item.RegionalCount = item.Class == B.RI ? previous >= 0 ? scalars[previous].RegionalCount + 1 : 1 : 0;
                previous = scalars.Count;
                if (item.Class != B.SP) beforeSpaces = previous;
            }
            scalars.Add(item);
        }
        int next = -1;
        for (int i = scalars.Count - 1; i >= 0; i--)
        {
            context.CheckCancellation(); scalars[i].Next = next;
            if (!scalars[i].Ignored) next = i;
        }
        var result = new List<int>();
        for (int i = 1; i < scalars.Count; i++)
        {
            context.CheckCancellation();
            if (BreakAt(i)) Add(scalars[i].Offset);
        }
        Add(text.Length); return result;

        void Add(int offset)
        {
            if (result.Count >= maximumBreaks) throw new InvalidDataException("Layout.LineBreakLimit: Segmentation exceeds the break allowance.");
            result.Add(offset);
        }
        bool BreakAt(int i)
        {
            Scalar immediate = scalars[i - 1], right = scalars[i];
            B rawA = immediate.Original, rawB = right.Original;
            if (rawA == B.BK) return true; // LB4
            if (rawA == B.CR && rawB == B.LF) return false;
            if (rawA is B.CR or B.LF or B.NL) return true; // LB5
            if (rawB is B.BK or B.CR or B.LF or B.NL) return false; // LB6
            if (rawB is B.SP or B.ZW) return false; // LB7
            int nonspace = right.BeforeSpaces;
            if (nonspace >= 0 && scalars[nonspace].Class == B.ZW) return true; // LB8
            if (rawA == B.ZWJ) return false; // LB8a
            if (right.Ignored) return false; // LB9
            int leftIndex = right.Previous;
            if (leftIndex < 0) return false;
            Scalar left = scalars[leftIndex]; B a = left.Class, b = right.Class;
            int beforeIndex = left.Previous, afterIndex = right.Next;
            Scalar? before = beforeIndex < 0 ? null : scalars[beforeIndex];
            Scalar? after = afterIndex < 0 ? null : scalars[afterIndex];
            B beforeClass = before?.Class ?? B.XX, afterClass = after?.Class ?? B.XX;
            if (a == B.WJ || b == B.WJ) return false; // LB11
            if (a == B.GL) return false; // LB12
            if (b == B.GL && a is not (B.SP or B.BA or B.HY or B.HH)) return false; // LB12a
            if (b is B.CL or B.CP or B.EX or B.SY) return false; // LB13
            Scalar? lastNonspace = nonspace < 0 ? null : scalars[nonspace];
            if (lastNonspace?.Class == B.OP) return false; // LB14
            if (lastNonspace?.Class == B.QU && lastNonspace.Category == C.Pi)
            {
                int prior = lastNonspace.Previous;
                if (prior < 0 || scalars[prior].Class is B.BK or B.CR or B.LF or B.NL or B.OP or B.QU or B.GL or B.SP or B.ZW) return false; // LB15a
            }
            if (b == B.QU && right.Category == C.Pf && (after == null || afterClass is B.SP or B.GL or B.WJ or B.CL or B.QU or B.CP or B.EX or B.IS or B.SY or B.BK or B.CR or B.LF or B.NL or B.ZW)) return false; // LB15b
            if (a == B.SP && b == B.IS && afterClass == B.NU) return true; // LB15c
            if (b == B.IS) return false; // LB15d
            if (b == B.NS && lastNonspace?.Class is B.CL or B.CP) return false; // LB16
            if (b == B.B2 && lastNonspace?.Class == B.B2) return false; // LB17
            if (a == B.SP) return true; // LB18
            if (b == B.QU && right.Category != C.Pi || a == B.QU && left.Category != C.Pf) return false; // LB19
            if (b == B.QU && (!left.EastAsian || after == null || !after.EastAsian)
                || a == B.QU && (!right.EastAsian || before == null || !before.EastAsian)) return false; // LB19a
            if (a == B.CB || b == B.CB) return true; // LB20
            if (a is B.HY or B.HH && b is B.AL or B.HL && (before == null || beforeClass is B.BK or B.CR or B.LF or B.NL or B.SP or B.ZW or B.CB or B.GL)) return false; // LB20a
            if (b is B.BA or B.HH or B.HY or B.NS || a == B.BB) return false; // LB21
            if (beforeClass == B.HL && a is B.HY or B.HH && b != B.HL) return false; // LB21a
            if (a == B.SY && b == B.HL || b == B.IN) return false; // LB21b, LB22
            if (IsAlphabetic(a) && b == B.NU || a == B.NU && IsAlphabetic(b)) return false; // LB23
            if (a == B.PR && b is B.ID or B.EB or B.EM || a is B.ID or B.EB or B.EM && b == B.PO) return false; // LB23a
            if (a is B.PR or B.PO && IsAlphabetic(b) || IsAlphabetic(a) && b is B.PR or B.PO) return false; // LB24
            if (b is B.PO or B.PR && (left.NumericSequence || a is B.CL or B.CP && before?.NumericSequence == true)) return false;
            if (a is B.PO or B.PR && b == B.OP && (afterClass == B.NU || afterClass == B.IS && after?.Next >= 0 && scalars[after.Next].Class == B.NU)) return false;
            if (b == B.NU && (a is B.PO or B.PR or B.HY or B.IS || left.NumericSequence)) return false; // LB25
            if (a == B.JL && b is B.JL or B.JV or B.H2 or B.H3 || a is B.JV or B.H2 && b is B.JV or B.JT || a is B.JT or B.H3 && b == B.JT) return false; // LB26
            if (Hangul(a) && b == B.PO || a == B.PR && Hangul(b)) return false; // LB27
            if (IsAlphabetic(a) && IsAlphabetic(b)) return false; // LB28
            if (a == B.AP && Aksara(right) || Aksara(left) && b is B.VF or B.VI
                || a == B.VI && before != null && Aksara(before) && (b == B.AK || right.Value == 0x25cc)
                || Aksara(left) && Aksara(right) && afterClass == B.VF) return false; // LB28a
            if (a == B.IS && IsAlphabetic(b)) return false; // LB29
            if ((IsAlphabetic(a) || a == B.NU) && b == B.OP && !right.EastAsian
                || a == B.CP && !left.EastAsian && (IsAlphabetic(b) || b == B.NU)) return false; // LB30
            if (a == B.RI && b == B.RI && left.RegionalCount % 2 == 1) return false; // LB30a
            if ((a == B.EB || left.Pictographic && left.Category == C.Cn) && b == B.EM) return false; // LB30b
            return true; // LB31
        }
    }

    private static bool IsAlphabetic(B value) => value is B.AL or B.HL;
    private static bool Hangul(B value) => value is B.JL or B.JV or B.JT or B.H2 or B.H3;
    private static bool Aksara(Scalar value) => value.Class is B.AK or B.AS || value.Value == 0x25cc;
}
