using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Formats;

// A parser and semantic mapper, never a browsing context or a resource loader.
internal static class HtmlInput
{
    private sealed class Rule
    {
        internal string Selector = "";
        internal int Specificity;
        internal List<(string Name, string Value, bool Important)> Values = new List<(string, string, bool)>();
    }

    internal static DocumentModel Read(byte[] bytes, ConversionContext context)
    {
        var resources = (context.Options.HtmlInput ?? throw new ArgumentException("HTML input options must not be null.")).Snapshot(context.Options);
        string text = TextFormat.Decode(bytes, context);
        using var document = new HtmlParser(new HtmlParserOptions { IsScripting = false }).ParseDocument(text);
        var nodes = new List<IElement>();
        var stack = new Stack<(INode Node, int Depth)>(); stack.Push((document, 0));
        int count = 0;
        while (stack.Count > 0)
        {
            context.CheckCancellation(); var item = stack.Pop();
            if (item.Depth > context.Options.MaximumNestingDepth || ++count > context.Options.MaximumCharacters)
                throw new InvalidDataException("Limit.HtmlTree: HTML tree exceeds depth or node bounds.");
            if (item.Node is IElement e) nodes.Add(e);
            for (int i = item.Node.ChildNodes.Length - 1; i >= 0; i--) stack.Push((item.Node.ChildNodes[i], item.Depth + 1));
        }
        var rules = new List<Rule>();
        foreach (IElement style in nodes.Where(e => e.LocalName == "style"))
        {
            string css = Comments(style.TextContent, context);
            foreach (string piece in css.Split('}'))
            {
                if (string.IsNullOrWhiteSpace(piece)) continue;
                int brace = piece.IndexOf('{');
                if (brace < 0 || piece.IndexOf('{', brace + 1) >= 0 || piece.Contains('@')) { CssLoss(context); continue; }
                var values = Declarations(piece.Substring(brace + 1), context);
                foreach (string selector in piece.Substring(0, brace).Split(','))
                {
                    string key = selector.Trim();
                    if (!Selector(key, null, out int specificity)) { CssLoss(context); continue; }
                    if (rules.Count >= 1024) throw new InvalidDataException("Limit.HtmlCss: Too many CSS rules.");
                    rules.Add(new Rule { Selector = key, Specificity = specificity, Values = values });
                }
            }
        }
        if ((long)rules.Count * nodes.Count > 2000000) throw new InvalidDataException("Limit.HtmlCss: CSS matching work exceeds its bound.");
        var computed = new Dictionary<IElement, Dictionary<string, string>>();
        foreach (IElement element in nodes)
        {
            context.CheckCancellation();
            var inherited = element.ParentElement != null && computed.TryGetValue(element.ParentElement, out var parent) ? parent : new Dictionary<string, string>(StringComparer.Ordinal);
            var winners = new Dictionary<string, (string Value, bool Important, int Specificity)>();
            foreach (Rule rule in rules)
                if (Selector(rule.Selector, element, out _)) Apply(rule.Values, rule.Specificity);
            Apply(Declarations(Comments(element.GetAttribute("style") ?? "", context), context), 1000000);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in inherited)
                if (Inherited(pair.Key)) values[pair.Key] = pair.Value;
            if (element.LocalName is "b" or "strong") values["font-weight"] = "bold";
            if (element.LocalName is "i" or "em") values["font-style"] = "italic";
            if (element.LocalName == "u") values["text-decoration"] = "underline";
            if (element.LocalName == "pre") values["white-space"] = "pre";
            double parentSize = inherited.TryGetValue("font-size", out string? previous) ? Points(previous) ?? 12 : 12;
            foreach (var pair in winners.OrderBy(p => p.Key == "font-size" ? 0 : 1))
            {
                string value = pair.Value.Value.Trim();
                if ((pair.Key == "width" && element.LocalName is not "table" and not "td" and not "th" and not "col" and not "img")
                    || (pair.Key == "height" && element.LocalName != "img")
                    || (pair.Key.StartsWith("padding-", StringComparison.Ordinal) && element.LocalName is not "td" and not "th"))
                { CssLoss(context); continue; }
                if (value is "inherit" or "unset") { if (inherited.TryGetValue(pair.Key, out string? v)) values[pair.Key] = v; else values.Remove(pair.Key); continue; }
                string? normalized = Normalize(pair.Key, value, pair.Key == "font-size" ? parentSize : values.TryGetValue("font-size", out string? size) ? Points(size) ?? 12 : parentSize);
                if (normalized == null) { CssLoss(context); continue; }
                values[pair.Key] = normalized;
            }
            computed.Add(element, values);
            element.RemoveAttribute("class"); element.SetAttribute("style", string.Join(";", values.Select(p => p.Key + ":" + p.Value)));
            foreach (var attribute in element.Attributes.Where(a => a.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase)).ToArray())
            { context.Loss("Html.ActiveContent", "HTML event handlers are omitted without execution."); element.RemoveAttribute(attribute.Name); }
            if (element.LocalName is "script" or "iframe" or "object" or "embed" or "svg" or "math" or "link")
            { context.Loss("Html.ActiveContent", "Active content and external resources are omitted without execution or fetching."); element.Remove(); }
            if (element.LocalName == "style") element.Remove();

            void Apply(IEnumerable<(string Name, string Value, bool Important)> declarations, int specificity)
            {
                foreach (var d in declarations)
                    if (!winners.TryGetValue(d.Name, out var winner) || d.Important && !winner.Important || d.Important == winner.Important && specificity >= winner.Specificity)
                        winners[d.Name] = (d.Value, d.Important, specificity);
            }
        }
        context.HtmlImport = true;
        long imageBytes = 0;
        DocumentModel result = EbookContent.Read(TextFormat.Utf8.GetBytes(document.DocumentElement!.OuterHtml), context, Image);
        result.Title = document.Title ?? "";
        return result;

        DocumentImage? Image(IElement element)
        {
            string source = element.GetAttribute("src") ?? ""; byte[] data; double? width = EbookContent.ImageDimension(element, "width", context), height = EbookContent.ImageDimension(element, "height", context);
            string alt = element.GetAttribute("alt") ?? "";
            if (resources.TryGetValue(source, out TemplateImage? supplied))
            { data = supplied.Bytes; width ??= supplied.Width; height ??= supplied.Height; if (!element.HasAttribute("alt")) alt = supplied.AlternativeText; }
            else if (source.StartsWith("data:image/png;base64,", StringComparison.OrdinalIgnoreCase) || source.StartsWith("data:image/jpeg;base64,", StringComparison.OrdinalIgnoreCase))
            {
                int comma = source.IndexOf(','); long maximum = (long)(source.Length - comma - 1) * 3 / 4;
                if (maximum > context.Options.MaximumExpandedBytes - imageBytes) throw new InvalidDataException("Limit.HtmlImages: Data images exceed expansion bounds.");
                try { data = Convert.FromBase64String(source.Substring(comma + 1)); }
                catch (FormatException e) { throw new InvalidDataException("Html.Image: Invalid base64 image data.", e); }
            }
            else { context.Loss("Html.ImageResource", "Only caller-supplied resources and PNG/JPEG base64 data images are resolved; no URI is fetched."); return null; }
            if ((imageBytes = checked(imageBytes + data.Length)) > context.Options.MaximumExpandedBytes) throw new InvalidDataException("Limit.HtmlImages: Images exceed expansion bounds.");
            if (ManagedRaster.Identify(data).Extension is not "png" and not "jpg" and not "jpeg")
            { context.Loss("Html.ImageFormat", "Only PNG/JPEG images are within the HTML input profile."); return null; }
            DocumentImage image = DocumentImage.Read(data, width, height, alt, context);
            image.Decorative = alt.Length == 0 && (element.HasAttribute("alt") || supplied?.Decorative == true);
            return image;
        }
    }

    private static string Comments(string value, ConversionContext context)
    {
        int start = 0; var result = new System.Text.StringBuilder(value.Length);
        while (start < value.Length)
        {
            int comment = value.IndexOf("/*", start, StringComparison.Ordinal);
            if (comment < 0) { result.Append(value, start, value.Length - start); break; }
            result.Append(value, start, comment - start); int end = value.IndexOf("*/", comment + 2, StringComparison.Ordinal);
            if (end < 0) { CssLoss(context); break; }
            start = end + 2;
        }
        return result.ToString();
    }

    private static List<(string Name, string Value, bool Important)> Declarations(string value, ConversionContext context)
    {
        var result = new List<(string, string, bool)>();
        foreach (string declaration in value.Split(';'))
        {
            if (string.IsNullOrWhiteSpace(declaration)) continue;
            int colon = declaration.IndexOf(':'); if (colon < 1) { CssLoss(context); continue; }
            string name = declaration.Substring(0, colon).Trim().ToLowerInvariant(), text = declaration.Substring(colon + 1).Trim();
            bool important = text.EndsWith("!important", StringComparison.OrdinalIgnoreCase);
            if (important) text = text.Substring(0, text.Length - 10).TrimEnd();
            if (text.Length > 4096 || HasAny(text, "(){}@\\")) { CssLoss(context); continue; }
            if (result.Count >= 256) throw new InvalidDataException("Limit.HtmlCss: Too many declarations in a rule.");
            result.Add((name, text, important));
        }
        return result;
    }

    private static bool Selector(string selector, IElement? element, out int specificity)
    {
        specificity = 0;
        if (selector.Length == 0 || selector.Length > 128) return false;
        int offset = 0; bool matches = true;
        if (selector[0] != '.' && selector[0] != '#')
        {
            while (offset < selector.Length && selector[offset] != '.' && selector[offset] != '#') offset++;
            string tag = selector.Substring(0, offset);
            if (tag != "*" && !tag.All(c => char.IsLetterOrDigit(c) || c == '-')) return false;
            matches = element == null || tag == "*" || tag.Equals(element.LocalName, StringComparison.OrdinalIgnoreCase); specificity += tag == "*" ? 0 : 1;
        }
        while (offset < selector.Length)
        {
            char kind = selector[offset++]; int start = offset;
            while (offset < selector.Length && selector[offset] != '.' && selector[offset] != '#') offset++;
            string name = selector.Substring(start, offset - start);
            if (name.Length == 0 || !name.All(c => char.IsLetterOrDigit(c) || c is '-' or '_')) return false;
            specificity += kind == '#' ? 10000 : 100;
            matches &= element == null || (kind == '#' ? element.Id == name : element.ClassList.Contains(name));
        }
        return matches;
    }

    private static bool HasAny(string value, string characters) => value.Any(c => characters.Contains(c));

    private static bool Inherited(string name) => name is "font-size" or "font-family" or "font-weight" or "font-style" or "color" or "text-align" or "white-space" or "text-decoration";
    private static void CssLoss(ConversionContext context) => context.Loss("Html.Css", "CSS outside simple selectors and the documented font, whitespace, alignment and table/image dimension profile is not retained.");

    private static string? Normalize(string name, string value, double em)
    {
        string lower = value.ToLowerInvariant();
        if (lower.Length == 0) return null;
        if (name == "font-family") return value.Length is > 0 and <= 256 && !HasAny(value, ",;\"'") ? value : null;
        if (name == "color" || name == "background-color")
        {
            if (name == "background-color" && lower == "transparent") return lower;
            if (lower.Length == 4 && lower[0] == '#' && lower.Skip(1).All(Uri.IsHexDigit)) return "#" + string.Concat(lower.Skip(1).SelectMany(c => new[] { c, c }));
            if (lower.Length == 7 && lower[0] == '#' && lower.Skip(1).All(Uri.IsHexDigit)) return lower;
            return lower switch { "black" => "#000000", "white" => "#ffffff", "red" => "#ff0000", "blue" => "#0000ff", "green" => "#008000", "yellow" => "#ffff00", _ => null };
        }
        if (name == "font-weight") return lower is "normal" or "bold" or "400" or "700" ? lower : null;
        if (name == "font-style") return lower is "normal" or "italic" ? lower : null;
        if (name == "text-decoration") return lower is "none" or "underline" or "line-through" ? lower : null;
        if (name == "white-space") return lower is "normal" or "pre" or "pre-wrap" ? lower : null;
        if (name == "text-align") return lower is "left" or "center" or "right" or "justify" ? lower : null;
        if (name is not "font-size" and not "width" and not "height" and not "padding-top" and not "padding-right" and not "padding-bottom" and not "padding-left") return null;
        if (lower[lower.Length - 1] == '%')
        {
            double? percentage = Number(lower.Substring(0, lower.Length - 1));
            if (percentage is not > 0 or > 100) return null;
            return name == "width" ? lower : name == "font-size" ? PointString(em * percentage.Value / 100) : null;
        }
        if (lower == "0" && name.StartsWith("padding-", StringComparison.Ordinal)) return "0pt";
        if (lower.Length < 3) return null;
        double scale = lower.Substring(lower.Length - 2) switch { "pt" => 1, "px" => .75, "in" => 72, "cm" => 72 / 2.54, "mm" => 72 / 25.4, "em" => em, _ => 0 };
        double? number = Number(lower.Substring(0, lower.Length - 2));
        if (!number.HasValue || scale == 0 || number * scale > 1638 || number == 0 && !name.StartsWith("padding-", StringComparison.Ordinal)) return null;
        return PointString(number.Value * scale);
    }
    private static double? Number(string text) => double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double n) && n >= 0 && !double.IsInfinity(n) && !double.IsNaN(n) ? n : null;
    private static string PointString(double points) => points.ToString("0.######", CultureInfo.InvariantCulture) + "pt";
    private static double? Points(string value) => value.EndsWith("pt", StringComparison.Ordinal) ? Number(value.Substring(0, value.Length - 2)) : null;

    internal static string? Css(IElement e, string name) => (e.GetAttribute("style") ?? "").Split(';').Select(d => d.Split(':')).Where(p => p.Length == 2 && p[0] == name).Select(p => p[1]).LastOrDefault();
    internal static double? Percent(IElement e) { string? value = Css(e, "width"); return value?.Length > 0 && value[value.Length - 1] == '%' ? Number(value.Substring(0, value.Length - 1)) : null; }

    internal static void Trim(ParagraphBlock paragraph)
    {
        bool start = true, space = false;
        for (int i = 0; i < paragraph.Runs.Count; i++)
        {
            TextRun run = paragraph.Runs[i]; string value = run.Text;
            if (run.Image != null) { start = false; space = false; continue; }
            if (start || space) value = value.TrimStart(' ');
            if (value.Length == 0) { paragraph.Runs[i] = run.WithText(value); continue; }
            start = false; space = value[value.Length - 1] == ' '; paragraph.Runs[i] = run.WithText(value);
        }
        for (int i = paragraph.Runs.Count - 1; i >= 0; i--)
        {
            TextRun run = paragraph.Runs[i]; if (run.Image != null) break;
            string value = run.Text.TrimEnd(' '); paragraph.Runs[i] = run.WithText(value); if (value.Length > 0) break;
        }
    }
}
