using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using WordNet.Internal;

namespace WordNet.Formats;

internal sealed partial class DocxReader
{
    private static readonly XNamespace GlossaryMath = "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private DocxReader? glossaryStyleReader;
    private bool glossaryScriptFormatting;

    // Preserve only a closed, inert style vocabulary. The retained bytes are never
    // executed, and no nested relationships or external resources are admitted.
    private void ValidateGlossaryStyles(XElement root)
    {
        var definitions = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var latentNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (XElement element in root.DescendantsAndSelf())
        {
            context.ReserveControlWork(1);
            if (element.Name.Namespace != W) throw new NotSupportedException("Control.Unsupported: Unknown glossary style namespace.");
            string name = element.Name.LocalName, parent = element.Parent?.Name.LocalName ?? "";
            bool admitted = parent switch
            {
                "" => name == "styles",
                "styles" => name is "docDefaults" or "latentStyles" or "style",
                "docDefaults" => name is "rPrDefault" or "pPrDefault",
                "rPrDefault" => name == "rPr",
                "style" => name is "rPr" or "name" or "basedOn" or "uiPriority" or "semiHidden" or "unhideWhenUsed" or "qFormat" or "rsid" or "tblPr" or "pPr",
                "pPrDefault" => name == "pPr",
                "latentStyles" => name == "lsdException",
                "rPr" => name is "rFonts" or "sz" or "szCs" or "lang" or "b" or "i" or "u" or "color",
                "pPr" => name == "spacing",
                "tblPr" => name is "tblInd" or "tblCellMar",
                "tblCellMar" => name is "top" or "bottom" or "left" or "right",
                _ => false,
            };
            if (!admitted) throw new NotSupportedException("Control.Unsupported: Unknown or active glossary style metadata.");
            string attributes = name switch
            {
                "style" => "type default styleId customStyle",
                "latentStyles" => "defLockedState defUIPriority defSemiHidden defUnhideWhenUsed defQFormat count",
                "lsdException" => "name locked uiPriority semiHidden unhideWhenUsed qFormat",
                "name" or "basedOn" or "uiPriority" or "rsid" or "sz" or "szCs" or "b" or "i" or "u" or "color" or "semiHidden" or "unhideWhenUsed" or "qFormat" => "val",
                "rFonts" => "ascii hAnsi cs eastAsia asciiTheme hAnsiTheme eastAsiaTheme cstheme hint",
                "lang" => "val eastAsia bidi",
                "spacing" => "before after line lineRule",
                "tblInd" or "top" or "bottom" or "left" or "right" => "w type",
                _ => "",
            };
            XName[] allowed = attributes.Length == 0 ? Array.Empty<XName>() : attributes.Split(' ').Select(a => W + a).ToArray();
            Attributes(element, allowed);
            foreach (XAttribute attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration))
            {
                context.ReserveControlWork(1);
                if (attribute.Value.Length is 0 or > 256) throw new InvalidDataException("Limit.Controls: Glossary style metadata exceeds its bound.");
            }
            if (name is not "styles" and not "latentStyles" && element.Elements().GroupBy(e => e.Name).Any(g => g.Count() > 1)
                || name == "styles" && element.Elements().Where(e => e.Name != W + "style").GroupBy(e => e.Name).Any(g => g.Count() > 1))
                throw new InvalidDataException("Control.Malformed: Duplicate glossary style metadata.");

            if (name == "style")
            {
                string id = DocxPackage.RequiredAttribute(element, W + "styleId");
                string type = DocxPackage.RequiredAttribute(element, W + "type");
                if (type is not ("paragraph" or "character" or "table" or "numbering") || definitions.ContainsKey(id))
                    throw new InvalidDataException("Control.Malformed: Invalid or duplicate glossary style identifier.");
                definitions.Add(id, element);
                foreach (string flag in new[] { "default", "customStyle" })
                    if ((string?)element.Attribute(W + flag) is string value) _ = ControlValues.Bool(value);
            }
            else if (name == "lsdException")
            {
                string id = DocxPackage.RequiredAttribute(element, W + "name");
                if (!latentNames.Add(id)) throw new InvalidDataException("Control.Malformed: Duplicate latent glossary style.");
            }
            else if (name is "name" or "basedOn") _ = DocxPackage.RequiredAttribute(element, W + "val");
            else if (name == "rsid")
            {
                string value = DocxPackage.RequiredAttribute(element, W + "val");
                if (value.Length != 8 || value.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("Control.Malformed: Invalid glossary style revision identifier.");
            }
            else if (name is "sz" or "szCs")
            {
                _ = Integer(element, W + "val", 2, 3168);
                if (name == "szCs") glossaryScriptFormatting = true;
            }
            else if (name == "color")
            {
                string value = DocxPackage.RequiredAttribute(element, W + "val");
                if (value != "auto" && (value.Length != 6 || value.Any(c => !Uri.IsHexDigit(c)))) throw new InvalidDataException("Control.Malformed: Invalid glossary style color.");
            }
            else if (name == "rFonts")
            {
                ValidateThemeFonts(element);
                glossaryScriptFormatting |= element.Attributes().Any(a => a.Name == W + "cs" || a.Name == W + "eastAsia" || a.Name == W + "cstheme"
                    || a.Name == W + "eastAsiaTheme" || a.Name == W + "hint" && a.Value != "default");
            }
            else if (name == "lang") glossaryScriptFormatting |= element.Attribute(W + "eastAsia") != null || element.Attribute(W + "bidi") != null;
            else if (name is "b" or "i" or "semiHidden" or "unhideWhenUsed" or "qFormat") _ = ControlValues.On(element, W);
            else if (name == "u" && (string?)element.Attribute(W + "val") is string underline && underline is not "single" and not "none")
                throw new NotSupportedException("Control.Unsupported: The glossary underline is outside the text profile.");
            else if (name == "spacing")
            {
                foreach (string dimension in new[] { "before", "after", "line" })
                    if ((string?)element.Attribute(W + dimension) is string value && (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) || number is < 0 or > 31680))
                        throw new InvalidDataException("Control.Malformed: Invalid glossary paragraph spacing.");
                if ((string?)element.Attribute(W + "lineRule") is string rule && rule is not "auto" and not "exact" and not "atLeast")
                    throw new InvalidDataException("Control.Malformed: Invalid glossary line-spacing rule.");
            }
            else if (name is "tblInd" or "top" or "bottom" or "left" or "right")
            {
                if ((string?)element.Attribute(W + "type") != "dxa" || !int.TryParse((string?)element.Attribute(W + "w"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int width) || width is < 0 or > 31680)
                    throw new InvalidDataException("Control.Malformed: Invalid glossary table measurement.");
            }
            if (name is "latentStyles" or "lsdException")
            {
                foreach (XAttribute attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration))
                {
                    string field = attribute.Name.LocalName;
                    if (field is "defLockedState" or "defSemiHidden" or "defUnhideWhenUsed" or "defQFormat" or "locked" or "semiHidden" or "unhideWhenUsed" or "qFormat") _ = ControlValues.Bool(attribute.Value);
                    else if (field is "defUIPriority" or "uiPriority" or "count")
                    { if (!int.TryParse(attribute.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number > 32767) throw new InvalidDataException("Control.Malformed: Invalid latent glossary style count or priority."); }
                }
            }
            if (name == "uiPriority") _ = Integer(element, W + "val", 0, 32767);
        }
        foreach (var pair in definitions)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal); XElement current = pair.Value;
            string type = DocxPackage.RequiredAttribute(current, W + "type");
            while (true)
            {
                context.ReserveControlWork(1);
                string id = DocxPackage.RequiredAttribute(current, W + "styleId");
                if (!visited.Add(id)) throw new InvalidDataException("Control.Malformed: Cyclic glossary styles are invalid.");
                if (visited.Count > context.Options.MaximumNestingDepth) throw new InvalidDataException("Limit.NestingDepth: Glossary style inheritance is too deep.");
                string? next = (string?)current.Element(W + "basedOn")?.Attribute(W + "val");
                if (next == null) break;
                if (!definitions.TryGetValue(next, out XElement? parent) || (string?)parent.Attribute(W + "type") != type)
                    throw new InvalidDataException("Control.Malformed: A glossary style has a missing or incompatible base.");
                current = parent;
            }
        }
    }

    private bool ValidateGlossarySetting(XElement element)
    {
        if (element.Name == W + "view")
        {
            Leaf(element, W + "val");
            if ((string?)element.Attribute(W + "val") is not ("none" or "print" or "outline" or "masterPages" or "normal" or "web"))
                throw new InvalidDataException("Control.Malformed: Invalid glossary view.");
        }
        else if (element.Name == W + "defaultTabStop") { Leaf(element, W + "val"); _ = Integer(element, W + "val", 0, 31680); }
        else if (element.Name == W + "characterSpacingControl")
        {
            Leaf(element, W + "val");
            if ((string?)element.Attribute(W + "val") is not ("doNotCompress" or "compressPunctuation" or "compressPunctuationAndJapaneseKana"))
                throw new InvalidDataException("Control.Malformed: Invalid glossary character spacing.");
        }
        else if (element.Name == W + "compat")
        {
            Attributes(element);
            if (element.Elements().Count() > 1 || element.Elements().Any(e => e.Name != W + "useFELayout"))
                throw new NotSupportedException("Control.Unsupported: Unknown glossary compatibility setting.");
            foreach (XElement child in element.Elements()) { context.ReserveControlWork(1); Leaf(child, W + "val"); _ = ControlValues.On(child, W); }
        }
        else if (element.Name == W + "themeFontLang")
        {
            Leaf(element, W + "val", W + "eastAsia", W + "bidi");
            if (element.Attributes().Any(a => !a.IsNamespaceDeclaration && a.Value.Length is 0 or > 85)) throw new InvalidDataException("Limit.Controls: Glossary language metadata exceeds its bound.");
        }
        else if (element.Name == W + "clrSchemeMapping")
        {
            XName[] names = { W + "bg1", W + "t1", W + "bg2", W + "t2", W + "accent1", W + "accent2", W + "accent3", W + "accent4", W + "accent5", W + "accent6", W + "hyperlink", W + "followedHyperlink" };
            Leaf(element, names);
            foreach (XAttribute attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration))
                if (attribute.Value is not ("dark1" or "light1" or "dark2" or "light2" or "accent1" or "accent2" or "accent3" or "accent4" or "accent5" or "accent6" or "hyperlink" or "followedHyperlink"))
                    throw new InvalidDataException("Control.Malformed: Invalid glossary color mapping.");
        }
        else if (element.Name == W + "decimalSymbol" || element.Name == W + "listSeparator")
        {
            Leaf(element, W + "val"); string value = DocxPackage.RequiredAttribute(element, W + "val");
            if (value.Length is 0 or > 2 || value.Any(char.IsControl)) throw new InvalidDataException("Control.Malformed: Invalid glossary separator.");
        }
        else if (element.Name == GlossaryMath + "mathPr")
        {
            Attributes(element); var names = new HashSet<XName>();
            foreach (XElement child in element.Elements())
            {
                context.ReserveControlWork(1);
                if (child.Name.Namespace != GlossaryMath || child.Name.LocalName is not ("mathFont" or "brkBin" or "brkBinSub" or "smallFrac" or "dispDef" or "lMargin" or "rMargin" or "defJc" or "wrapIndent" or "intLim" or "naryLim"))
                    throw new NotSupportedException("Control.Unsupported: Unknown glossary math setting.");
                if (!names.Add(child.Name)) throw new InvalidDataException("Control.Malformed: Duplicate glossary math setting.");
                Leaf(child, GlossaryMath + "val");
                if ((string?)child.Attribute(GlossaryMath + "val") is string value && value.Length is 0 or > 256)
                    throw new InvalidDataException("Limit.Controls: Glossary math metadata exceeds its bound.");
            }
        }
        else return false;
        return true;
    }
}
