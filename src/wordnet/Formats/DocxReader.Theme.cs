using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace WordNet.Formats;

internal sealed partial class DocxReader
{
    private static readonly XNamespace Drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private ThemeAppearance? theme;

    private void ReadTheme(DocxPackage source, string owner)
    {
        XElement[] parts = relationships.Values.Where(r => (string?)r.Attribute("Type") == DocxPackage.DocumentRelationships.NamespaceName + "/theme").ToArray();
        if (parts.Length > 1) throw new InvalidDataException("Docx.Theme: Multiple document theme parts are invalid.");
        if (parts.Length == 0) return;
        string path = DocxPackage.ResolvePart(owner, parts[0]);
        XElement root = source.ReadXml(path).Root ?? throw new InvalidDataException("Docx.Theme: Missing theme root.");
        if (root.Name != Drawing + "theme") throw new InvalidDataException("Docx.Theme: Invalid theme root.");
        theme = new ThemeAppearance();
        XElement? elements = Single(root, Drawing + "themeElements");
        XElement? fonts = elements == null ? null : Single(elements, Drawing + "fontScheme");
        if (fonts != null)
            foreach (string group in new[] { "major", "minor" })
            {
                context.CheckCancellation();
                XElement? family = Single(fonts, Drawing + (group + "Font"));
                string? latin = family == null ? null : (string?)Single(family, Drawing + "latin")?.Attribute("typeface");
                if (!string.IsNullOrWhiteSpace(latin) && latin!.Length <= 256) theme.Fonts.Add(group, latin);
            }
        XElement? colors = elements == null ? null : Single(elements, Drawing + "clrScheme");
        if (colors != null)
            foreach (string slot in ThemeAppearance.Slots)
            {
                context.CheckCancellation(); XElement? value = Single(colors, Drawing + slot);
                if (value == null) continue;
                XElement[] children = value.Elements().ToArray();
                if (children.Length != 1 || children[0].HasElements) continue;
                XElement child = children[0];
                string? rgb = child.Name == Drawing + "srgbClr" ? (string?)child.Attribute("val")
                    : child.Name == Drawing + "sysClr" ? (string?)child.Attribute("lastClr") : null;
                if (rgb != null)
                {
                    if (rgb.Length != 6 || !rgb.All(Uri.IsHexDigit)) throw new InvalidDataException("Docx.Theme: Invalid scheme color.");
                    theme.Colors.Add(slot, rgb.ToUpperInvariant());
                }
                else if (child.Name == Drawing + "srgbClr") throw new InvalidDataException("Docx.Theme: Missing scheme color.");
            }

        XElement[] settings = relationships.Values.Where(r => (string?)r.Attribute("Type") == DocxPackage.DocumentRelationships.NamespaceName + "/settings").ToArray();
        if (settings.Length > 1) throw new InvalidDataException("Docx.Settings: Multiple settings parts are invalid.");
        if (settings.Length == 1)
        {
            XElement settingsRoot = source.ReadXml(DocxPackage.ResolvePart(owner, settings[0])).Root ?? throw new InvalidDataException("Docx.Settings: Missing settings root.");
            if (settingsRoot.Name != W + "settings") throw new InvalidDataException("Docx.Settings: Invalid settings root.");
            XElement? mapping = Single(settingsRoot, W + "clrSchemeMapping");
            if (mapping != null) theme.Map(mapping);
        }
    }

    private static XElement? Single(XElement parent, XName name)
    {
        XElement[] matches = parent.Elements(name).ToArray();
        if (matches.Length > 1) throw new InvalidDataException("Docx.Theme: Duplicate theme scheme entries are invalid.");
        return matches.Length == 0 ? null : matches[0];
    }

    private string? ThemeReference(XElement element, string colorName, string tintName, string shadeName, string? fallback)
    {
        string? token = (string?)element.Attribute(W + colorName), tint = (string?)element.Attribute(W + tintName), shade = (string?)element.Attribute(W + shadeName);
        if (token != null && !ThemeAppearance.ValidColorToken(token) || !ThemeAppearance.ValidTransform(tint) || !ThemeAppearance.ValidTransform(shade))
            throw new InvalidDataException("Docx.ThemeColor: Invalid theme color reference or transform.");
        if (token == null && tint == null && shade == null) return fallback;
        string? resolved = token == null ? null : theme?.Color(token, tint, shade);
        context.Loss("Docx.ThemeColor", "Theme colors use the bounded local scheme or stored RGB fallback; editable theme semantics are not retained.");
        return resolved ?? fallback;
    }

    private static bool HasThemeFont(XElement property) => property.Attributes().Any(a => !a.IsNamespaceDeclaration
        && (a.Name.LocalName.EndsWith("Theme", StringComparison.Ordinal) || a.Name == W + "cstheme"));
    private static void ValidateThemeFonts(XElement property)
    {
        if (property.Attributes().Any(a => (a.Name == W + "asciiTheme" || a.Name == W + "hAnsiTheme" || a.Name == W + "eastAsiaTheme" || a.Name == W + "cstheme") && !ThemeAppearance.ValidFontToken(a.Value)))
            throw new InvalidDataException("Docx.ThemeFont: Invalid theme font reference.");
    }

    private sealed class ThemeAppearance
    {
        internal static readonly string[] Slots = { "dk1", "lt1", "dk2", "lt2", "accent1", "accent2", "accent3", "accent4", "accent5", "accent6", "hlink", "folHlink" };
        internal readonly Dictionary<string, string> Fonts = new Dictionary<string, string>(StringComparer.Ordinal);
        internal readonly Dictionary<string, string> Colors = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> mapping = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dark1"] = "dk1",
            ["light1"] = "lt1",
            ["dark2"] = "dk2",
            ["light2"] = "lt2",
            ["background1"] = "lt1",
            ["text1"] = "dk1",
            ["background2"] = "lt2",
            ["text2"] = "dk2",
            ["accent1"] = "accent1",
            ["accent2"] = "accent2",
            ["accent3"] = "accent3",
            ["accent4"] = "accent4",
            ["accent5"] = "accent5",
            ["accent6"] = "accent6",
            ["hyperlink"] = "hlink",
            ["followedHyperlink"] = "folHlink",
        };

        internal void Map(XElement source)
        {
            foreach (var pair in new[] { ("bg1", "background1"), ("t1", "text1"), ("bg2", "background2"), ("t2", "text2"),
                ("accent1", "accent1"), ("accent2", "accent2"), ("accent3", "accent3"), ("accent4", "accent4"), ("accent5", "accent5"), ("accent6", "accent6"),
                ("hyperlink", "hyperlink"), ("followedHyperlink", "followedHyperlink") })
                if ((string?)source.Attribute(W + pair.Item1) is string value)
                    mapping[pair.Item2] = Slot(value) ?? throw new InvalidDataException("Docx.Theme: Invalid color-scheme mapping.");
        }
        internal string? Font(string value)
        {
            string? group = value is "majorAscii" or "majorHAnsi" ? "major" : value is "minorAscii" or "minorHAnsi" ? "minor" : null;
            return group != null && Fonts.TryGetValue(group, out string? font) ? font : null;
        }
        internal static bool ValidFontToken(string value) => value is "majorAscii" or "majorHAnsi" or "majorEastAsia" or "majorBidi"
            or "minorAscii" or "minorHAnsi" or "minorEastAsia" or "minorBidi";
        internal static bool ValidColorToken(string value) => value is "dark1" or "light1" or "dark2" or "light2"
            or "background1" or "text1" or "background2" or "text2" or "accent1" or "accent2" or "accent3" or "accent4" or "accent5" or "accent6"
            or "hyperlink" or "followedHyperlink" or "none";
        internal static bool ValidTransform(string? value) => value == null || value.Length == 2 && value.All(Uri.IsHexDigit);
        internal string? Color(string value, string? tint, string? shade)
        {
            if (tint != null && shade != null || !mapping.TryGetValue(value, out string? slot) || !Colors.TryGetValue(slot, out string? rgb)) return null;
            string? transform = tint ?? shade;
            if (transform == null) return rgb;
            if (transform.Length != 2 || !byte.TryParse(transform, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte factor)) return null;
            var result = new System.Text.StringBuilder(6);
            for (int index = 0; index < 6; index += 2)
            {
                int component = Hex(rgb[index]) * 16 + Hex(rgb[index + 1]);
                int product = shade != null ? component * factor : (255 - component) * factor;
                int scaled = (product + 127) / 255;
                result.Append((shade != null ? scaled : 255 - scaled).ToString("X2", CultureInfo.InvariantCulture));
            }
            return result.ToString();
        }
        private static int Hex(char value) => value <= '9' ? value - '0' : value - 'A' + 10;
        private static string? Slot(string value) => value switch
        {
            "dark1" => "dk1",
            "light1" => "lt1",
            "dark2" => "dk2",
            "light2" => "lt2",
            "accent1" or "accent2" or "accent3" or "accent4" or "accent5" or "accent6" => value,
            "hyperlink" => "hlink",
            "followedHyperlink" => "folHlink",
            _ => null,
        };
    }
}
