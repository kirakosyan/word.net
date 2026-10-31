using System;
using System.Collections.Generic;
using System.IO;

namespace WordNet;

/// <summary>An immutable caller-supplied list of English-word hyphenation points.</summary>
public sealed class HyphenationLexicon
{
    private readonly Dictionary<string, int[]> words = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a local dictionary from entries such as <c>hy-phen-a-tion</c>. No resource is fetched or discovered.</summary>
    /// <param name="language">Explicit dictionary language, currently <c>en</c> or <c>en-US</c>.</param>
    /// <param name="entries">ASCII words with one or more internal hyphens at permitted break points.</param>
    public HyphenationLexicon(string language, IEnumerable<string> entries)
    {
        if (language is not "en" and not "en-US") throw new ArgumentException("Hyphenation.Language: Supply en or en-US.", nameof(language));
        Language = language; int bytes = 0;
        foreach (string entry in entries ?? throw new ArgumentNullException(nameof(entries)))
        {
            if (entry == null || entry.Length is < 3 or > 128) throw new ArgumentException("Hyphenation.Dictionary: Invalid entry length.", nameof(entries));
            bytes = checked(bytes + entry.Length);
            if (words.Count >= 25000 || bytes > 1024 * 1024) throw new InvalidDataException("Limit.HyphenationDictionary: Dictionary size exceeds its bound.");
            var text = new System.Text.StringBuilder(entry.Length); var points = new List<int>(); bool previousBreak = false;
            foreach (char c in entry)
            {
                if (c == '-')
                {
                    if (text.Length == 0 || previousBreak) throw new ArgumentException("Hyphenation.Dictionary: Breaks must separate letters.", nameof(entries));
                    points.Add(text.Length); previousBreak = true;
                }
                else
                {
                    if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z')) throw new ArgumentException("Hyphenation.Dictionary: Only ASCII letters and break markers are admitted.", nameof(entries));
                    text.Append(c); previousBreak = false;
                }
            }
            if (previousBreak || points.Count == 0 || words.ContainsKey(text.ToString()))
                throw new ArgumentException("Hyphenation.Dictionary: Entries need unique words and internal break points.", nameof(entries));
            words.Add(text.ToString(), points.ToArray());
        }
        if (words.Count == 0) throw new ArgumentException("Hyphenation.Dictionary: At least one entry is required.", nameof(entries));
    }

    /// <summary>Gets the explicitly selected language.</summary>
    public string Language { get; }
    internal bool TryGetBreaks(string word, out int[] points) => words.TryGetValue(word, out points!);
}

/// <summary>Explicit local dictionary-hyphenation options. Configure before concurrent conversions.</summary>
public sealed class HyphenationOptions
{
    /// <summary>Gets or sets the immutable dictionary; null leaves the existing line layout unchanged.</summary>
    public HyphenationLexicon? Dictionary { get; set; }
    /// <summary>Gets or sets the minimum number of letters before a break, from 2 to 16.</summary>
    public int MinimumPrefix { get; set; } = 2;
    /// <summary>Gets or sets the minimum number of letters after a break, from 2 to 16.</summary>
    public int MinimumSuffix { get; set; } = 2;
    /// <summary>Gets or sets the maximum number of consecutive hyphenated lines, from 1 to 8.</summary>
    public int MaximumConsecutiveLines { get; set; } = 2;
    /// <summary>Gets or sets the aggregate lookup and candidate-work bound, from 1 to 1,000,000.</summary>
    public int MaximumWork { get; set; } = 100000;

    internal HyphenationOptions Snapshot()
    {
        if (MinimumPrefix is < 2 or > 16) throw new ArgumentOutOfRangeException(nameof(MinimumPrefix));
        if (MinimumSuffix is < 2 or > 16) throw new ArgumentOutOfRangeException(nameof(MinimumSuffix));
        if (MaximumConsecutiveLines is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(MaximumConsecutiveLines));
        if (MaximumWork is < 1 or > 1000000) throw new ArgumentOutOfRangeException(nameof(MaximumWork));
        return (HyphenationOptions)MemberwiseClone();
    }
}
