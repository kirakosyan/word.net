using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace WordNet.Internal;

// Deliberately closed picture profiles; never pass an unchecked picture to the runtime.
internal static class FieldPictures
{
    internal static string Format(object value, string kind, string picture)
    {
        if (picture.Length == 0 || picture.Length > 128) throw new FormatException();
        string result;
        if (kind == "\\#" && value is decimal number)
        {
            if (!Regex.IsMatch(picture, "^(?:'[^'\\r\\n]*')?(?:0|#,##0)(?:\\.(?:0{1,10}|#{1,10}))?(?:'[^'\\r\\n]*')?%?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) throw new FormatException();
            bool literal = false; int precision = 0; bool fraction = false;
            foreach (char c in picture)
            {
                if (c == '\'') literal = !literal;
                else if (!literal && c == '.') fraction = true;
                else if (!literal && fraction && (c == '0' || c == '#')) precision++;
            }
            // Word's numeric picture shows a percent sign; callers multiply by
            // 100 explicitly when formatting a fractional percentage. .NET's
            // unquoted percent token instead scales by 100, so quote it here.
            string normalized = picture;
            if (picture[picture.Length - 1] == '%')
            {
                var escaped = new StringBuilder(picture); escaped.Remove(escaped.Length - 1, 1); escaped.Append("'%'"); normalized = escaped.ToString();
            }
            result = decimal.Round(number, precision, MidpointRounding.AwayFromZero).ToString(normalized, CultureInfo.InvariantCulture);
        }
        else if (kind == "\\@" && value is DateTimeOffset instant)
        {
            var normalized = new StringBuilder(); int tokens = 0;
            for (int i = 0; i < picture.Length;)
            {
                if (++tokens > 64) throw new FormatException();
                char c = picture[i];
                if (c == '\'')
                {
                    int end = picture.IndexOf('\'', i + 1); if (end < 0) throw new FormatException();
                    normalized.Append(picture, i, end - i + 1); i = end + 1;
                }
                else if (char.IsLetter(c))
                {
                    int start = i; while (i < picture.Length && picture[i] == c) i++;
                    string token = picture.Substring(start, i - start);
                    if (token is not "yyyy" and not "yy" and not "M" and not "MM" and not "MMM" and not "MMMM"
                        and not "d" and not "dd" and not "ddd" and not "dddd" and not "H" and not "HH" and not "h" and not "hh"
                        and not "m" and not "mm" and not "s" and not "ss" and not "tt") throw new FormatException();
                    normalized.Append(token);
                }
                else if ("-/ :,.".Contains(c)) { normalized.Append(c); i++; }
                else throw new FormatException();
            }
            result = instant.ToUniversalTime().ToString(normalized.Length == 1 ? "%" + normalized : normalized.ToString(), CultureInfo.InvariantCulture);
        }
        else throw new FormatException();
        if (result.Length > 1024) throw new FormatException();
        return result;
    }
}
