using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using AngleSharp.Dom;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Formats;

internal static partial class EbookContent
{
    private static string Points(double value) => value.ToString("0.###", CultureInfo.InvariantCulture) + "pt";
    private static XElement WriteTableCell(TableCell cell, bool header, Func<DocumentImage, string>? imageWriter, ListCounter counter, ConversionContext context)
    {
        var element = new XElement(Html + (header ? "th" : "td"), new XAttribute("style", (header ? "font-weight:normal;" : "") + CellStyle(cell)));
        var lists = new List<(string, XElement)>();
        foreach (ParagraphBlock p in cell.Paragraphs)
        {
            XElement paragraph = Paragraph(p, imageWriter);
            if (p.List == null) { lists.Clear(); element.Add(paragraph); }
            else AppendList(element, lists, p.List, paragraph, counter.Label(p.List), context);
        }
        if (cell.ColumnSpan > 1) element.SetAttributeValue("colspan", cell.ColumnSpan);
        return element;
    }
    private static string CellStyle(TableCell cell)
    {
        string style = cell.CellWidth.HasValue ? "width:" + Points(cell.CellWidth.Value) + ";" : "";
        if (cell.Padding is CellPadding p) style += "padding-top:" + Points(p.Top) + ";padding-right:" + Points(p.Right)
            + ";padding-bottom:" + Points(p.Bottom) + ";padding-left:" + Points(p.Left) + ";";
        return style;
    }

    private static string? Css(IElement element, string name, bool inherit = false)
    {
        for (IElement? current = element; current != null; current = inherit ? current.ParentElement : null)
        {
            string? result = null;
            foreach (string declaration in (current.GetAttribute("style") ?? "").Split(';'))
            {
                int colon = declaration.IndexOf(':');
                if (colon >= 0 && declaration.Substring(0, colon).Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) result = declaration.Substring(colon + 1).Trim();
            }
            if (result != null) return result;
        }
        return null;
    }

    private static bool SupportedCss(IElement element, string declaration)
    {
        int colon = declaration.IndexOf(':');
        if (colon < 0) return false;
        string name = declaration.Substring(0, colon).Trim().ToLowerInvariant(), value = declaration.Substring(colon + 1).Trim();
        return name switch
        {
            "text-align" => IsAlignmentBlock(element),
            "font-size" => Length(value).HasValue,
            "font-weight" => value is "normal" or "bold" or "400" or "700",
            "color" => IsRgb(value),
            "width" => element.LocalName is "img" or "table" or "col" or "td" or "th" && Length(value, element.LocalName == "img").HasValue,
            "height" => element.LocalName == "img" && Length(value, true).HasValue,
            "padding-top" or "padding-right" or "padding-bottom" or "padding-left" => element.LocalName is "td" or "th" && Length(value).HasValue,
            _ => false,
        };
    }

    private static double? Length(string value, bool image = false)
    {
        double scale;
        if (value.EndsWith("pt", StringComparison.OrdinalIgnoreCase)) scale = 1;
        else if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase)) scale = 0.75;
        else if (value == "0") return 0;
        else return null;
#if NET8_0_OR_GREATER
        if (!double.TryParse(value.AsSpan(0, value.Length - 2), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double points)
#else
        if (!double.TryParse(value.Substring(0, value.Length - 2), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double points)
#endif
            || points < 0 || double.IsInfinity(points) || double.IsNaN(points) || !image && points * scale > 1638) return null;
        return points * scale;
    }

    internal static double? CssLength(IElement element, string name, ConversionContext context)
    {
        string? value = Css(element, name, name == "font-size");
        if (value == null) return null;
        double? result = Length(value, element.LocalName == "img" && name is "width" or "height");
        if (!result.HasValue || result == 0 && (name == "font-size" || name is "width" or "height"))
        { context.Loss("Ebook.CssLength", "Only positive absolute point/pixel dimensions and nonnegative margins are supported."); return null; }
        return result;
    }

    private static string? CssColor(IElement element, ConversionContext context)
    {
        string? value = Css(element, "color", true);
        if (value == null) return null;
        if (IsRgb(value)) return value.Substring(1).ToUpperInvariant();
        context.Loss("Ebook.CssColor", "Only explicit six-digit RGB colors are supported."); return null;
    }

    private static bool IsRgb(string value) => value.Length == 7 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);

    private static CellPadding? CssPadding(IElement element, ConversionContext context)
    {
        double? top = CssLength(element, "padding-top", context), right = CssLength(element, "padding-right", context), bottom = CssLength(element, "padding-bottom", context), left = CssLength(element, "padding-left", context);
        return top.HasValue || right.HasValue || bottom.HasValue || left.HasValue ? new CellPadding { Top = top ?? 0, Right = right ?? 0, Bottom = bottom ?? 0, Left = left ?? 0 } : null;
    }

    private static void AppendList(XElement body, List<(string Id, XElement Element)> stack, ListItem item, XElement paragraph, string label, ConversionContext context)
    {
        int level = item.Level;
        if (level > stack.Count) { context.Loss("Ebook.ListLevel", "A list starting at a missing ancestor level is moved to the nearest available level."); level = stack.Count; }
        if (stack.Count > level + 1) stack.RemoveRange(level + 1, stack.Count - level - 1);
        if (stack.Count > level && (stack[level].Id != item.Id || stack[level].Element.Name.LocalName != (item.Ordered ? "ol" : "ul"))) stack.RemoveAt(level);
        if (stack.Count == level)
        {
            var list = new XElement(Html + (item.Ordered ? "ol" : "ul"));
            if (item.Ordered) list.SetAttributeValue("start", label.TrimEnd('.'));
            XElement parent = level == 0 ? body : stack[level - 1].Element.Elements(Html + "li").Last();
            parent.Add(list); stack.Add((item.Id, list));
        }
        stack[level].Element.Add(new XElement(Html + "li", paragraph));
    }

    internal static double? ImageDimension(IElement element, string name, ConversionContext context)
    {
        double? css = CssLength(element, name, context);
        if (css.HasValue) return css;
        string? attribute = element.GetAttribute(name);
        if (attribute == null) return null;
        if (!double.TryParse(attribute, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double value) || value <= 0 || double.IsInfinity(value) || double.IsNaN(value))
            throw new InvalidDataException("Epub.ImageSize: Invalid image dimension.");
        return value * 0.75;
    }
}
