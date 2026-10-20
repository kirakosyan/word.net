using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using WordNet.Internal;

namespace WordNet.Formats;

internal static class MathObjectFormat
{
    internal static readonly XNamespace M = "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private static readonly XNamespace W = DocxPackage.Word;
    private static InvalidDataException Invalid(string code) => new InvalidDataException("Equation." + code + ": Invalid OMML expression.");
    internal static (EquationExpression Expression, bool Display, double Size, bool Renderable) Read(XElement source, ConversionContext context)
    {
        try { return ReadCore(source, context); }
        catch (ArgumentException error) { throw new NotSupportedException("Equation.Profile: Valid OMML text or delimiters exceed the supported expression profile.", error); }
    }
    private static (EquationExpression Expression, bool Display, double Size, bool Renderable) ReadCore(XElement source, ConversionContext context)
    {
        int nodes = 0; long text = 0;
        foreach (XElement element in source.DescendantsAndSelf())
        {
            context.CheckCancellation(); text = checked(text + element.Attributes().Sum(a => (long)a.Value.Length) + element.Nodes().OfType<XText>().Sum(t => (long)t.Value.Length));
            if (++nodes > 10000 || text > context.Options.MaximumCharacters || nodes * 128 + text * 2 > context.Options.MaximumExpandedBytes
                || element.Ancestors().TakeWhile(p => p != source.Parent).Count() > Math.Min(32, context.Options.MaximumNestingDepth)) throw Invalid("Limit");
            if (element.Attributes().Any(a => !a.IsNamespaceDeclaration && a.Name.Namespace == DocxPackage.DocumentRelationships)) throw Invalid("Relationship");
            foreach (XAttribute attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration))
            {
                bool ordinary = attribute.Name == M + "val" || attribute.Name == XNamespace.Xml + "space"
                    || element.Name.Namespace == W && attribute.Name.Namespace == W && attribute.Name.LocalName is "val" or "ascii" or "hAnsi" or "eastAsia" or "cs" or "bidi" or "hint" or "asciiTheme" or "hAnsiTheme" or "eastAsiaTheme" or "cstheme" or "rsidRPr";
                if (!ordinary || attribute.Value.Length > 256) throw Invalid("Properties");
            }
        }
        bool display = source.Name == M + "oMathPara";
        if (!display && source.Name != M + "oMath") throw Invalid("Root");
        XElement math = source;
        if (display)
        {
            if (source.Elements().Any(e => e.Name != M + "oMathParaPr" && e.Name != M + "oMath") || source.Elements(M + "oMath").Count() != 1) throw Invalid("Display");
            Properties(source.Element(M + "oMathParaPr"), "jc"); math = source.Element(M + "oMath")!;
        }
        bool renderable = (string?)source.Element(M + "oMathParaPr")?.Element(M + "jc")?.Attribute(M + "val") is null or "center" or "centerGroup";
        double size = 12; var sizes = new HashSet<double>();
        foreach (XElement properties in source.Descendants(W + "rPr").Where(p => !p.Ancestors(M + "ctrlPr").Any()))
            foreach (XElement property in properties.Elements())
            {
                if (property.Name == W + "sz")
                { if (!int.TryParse((string?)property.Attribute(W + "val"), NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value is < 8 or > 288) throw Invalid("Size"); sizes.Add(value / 2.0); }
                else if (property.Name == W + "color") { if ((string?)property.Attribute(W + "val") is not null and not "000000" and not "auto") renderable = false; }
                else if (property.Name == W + "b" || property.Name == W + "i") { if ((string?)property.Attribute(W + "val") is not "0" and not "false" and not "off") renderable = false; }
                else if (property.Name != W + "rFonts" && property.Name != W + "lang") renderable = false;
                if (property.Descendants().Any() || property.Attributes().Any(a => a.Value.Length > 256)) throw Invalid("Properties");
            }
        if (sizes.Count == 1) size = sizes.Single(); else if (sizes.Count > 1) renderable = false;
        EquationExpression ParseRow(XElement container)
        {
            EquationExpression[] children = container.Elements().Select(Parse).ToArray();
            if (children.Length == 0) throw Invalid("Empty"); return children.Length == 1 ? children[0] : EquationExpression.Row(children);
        }
        XElement Child(XElement parent, string name)
        { XElement[] children = parent.Elements(M + name).ToArray(); return children.Length == 1 ? children[0] : throw Invalid("Children"); }
        EquationExpression Parse(XElement node)
        {
            context.CheckCancellation();
            if (node.Name == M + "r")
            {
                if (node.Elements().Any(e => e.Name != M + "rPr" && e.Name != W + "rPr" && e.Name != M + "t") || node.Elements(M + "t").Count() != 1) throw Invalid("Run");
                Properties(node.Element(M + "rPr"), "sty", "nor");
                string style = (string?)node.Element(M + "rPr")?.Element(M + "sty")?.Attribute(M + "val") ?? "i";
                if (style is not "p" and not "i") renderable = false;
                bool italic = style != "p" && (string?)node.Element(M + "rPr")?.Element(M + "nor")?.Attribute(M + "val") is not "1" and not "true";
                return EquationExpression.Text(node.Element(M + "t")!.Value, italic);
            }
            string kind = node.Name.LocalName;
            if (node.Name.Namespace != M || kind is not "f" and not "sSub" and not "sSup" and not "sSubSup" and not "rad" and not "d") throw new NotSupportedException("Equation.Type: This OMML construction is outside the admitted fraction/script/root/delimiter profile.");
            string[] expected = kind switch
            {
                "f" => new[] { "num", "den" },
                "sSub" => new[] { "e", "sub" },
                "sSup" => new[] { "e", "sup" },
                "sSubSup" => new[] { "e", "sub", "sup" },
                "rad" => new[] { "deg", "e" },
                _ => new[] { "e" }
            };
            XElement? properties = node.Element(M + kind + "Pr");
            Properties(properties, kind switch { "f" => "type", "rad" => "degHide", "sSubSup" => "alnScr", "d" => "begChr", _ => "" }, kind == "d" ? "endChr" : "", kind == "d" ? "grow" : "");
            if (node.Elements().Any(e => e.Name != M + kind + "Pr" && (e.Name.Namespace != M || !expected.Contains(e.Name.LocalName)))) throw Invalid("Children");
            if (kind == "f")
            {
                string type = (string?)properties?.Element(M + "type")?.Attribute(M + "val") ?? "bar";
                if (type != "bar") throw new NotSupportedException("Equation.FractionType: Only stacked barred fractions are admitted.");
                return EquationExpression.Fraction(ParseRow(Child(node, "num")), ParseRow(Child(node, "den")));
            }
            if (kind == "rad")
            {
                XElement degree = Child(node, "deg"); bool hidden = (string?)properties?.Element(M + "degHide")?.Attribute(M + "val") is "1" or "true";
                if (hidden && degree.HasElements) throw Invalid("HiddenDegree");
                return EquationExpression.Root(ParseRow(Child(node, "e")), degree.HasElements ? ParseRow(degree) : null);
            }
            if (kind == "d" && node.Elements(M + "e").Count() > 1) throw new NotSupportedException("Equation.DelimiterArguments: Multiple delimiter arguments are outside the supported profile.");
            if (kind == "d") return EquationExpression.Delimiter(ParseRow(Child(node, "e")),
                (string?)properties?.Element(M + "begChr")?.Attribute(M + "val") ?? "(", (string?)properties?.Element(M + "endChr")?.Attribute(M + "val") ?? ")");
            if (properties?.Element(M + "alnScr") != null) renderable = false;
            EquationExpression basis = ParseRow(Child(node, "e"));
            return kind switch
            {
                "sSub" => EquationExpression.Subscript(basis, ParseRow(Child(node, "sub"))),
                "sSup" => EquationExpression.Superscript(basis, ParseRow(Child(node, "sup"))),
                _ => EquationExpression.SubSuperscript(basis, ParseRow(Child(node, "sub")), ParseRow(Child(node, "sup")))
            };
        }
        EquationExpression expression = ParseRow(math); return (expression, display, size, renderable);
    }
    private static void Properties(XElement? properties, params string[] admitted)
    {
        if (properties == null) return;
        var found = new HashSet<XName>();
        foreach (XElement item in properties.Elements())
        {
            if (!found.Add(item.Name)) throw Invalid("Properties");
            if (item.Name == M + "ctrlPr")
            { if (item.Elements().Any(e => e.Name != W + "rPr")) throw Invalid("Properties"); continue; }
            if (item.Name.Namespace != M || !admitted.Contains(item.Name.LocalName) || item.HasElements) throw new NotSupportedException("Equation.Properties: An unsupported OMML property is outside the preservation profile.");
        }
    }
    internal static string Description(EquationExpression node) => node.Kind switch
    {
        EquationKind.Text => node.Value,
        EquationKind.Row => string.Concat(node.Children.Select(Description)),
        EquationKind.Fraction => "(" + Description(node.Children[0]) + ")/(" + Description(node.Children[1]) + ")",
        EquationKind.Subscript => Description(node.Children[0]) + "_(" + Description(node.Children[1]) + ")",
        EquationKind.Superscript => Description(node.Children[0]) + "^(" + Description(node.Children[1]) + ")",
        EquationKind.SubSuperscript => Description(node.Children[0]) + "_(" + Description(node.Children[1]) + ")^(" + Description(node.Children[2]) + ")",
        EquationKind.Radical => (node.Children.Count == 1 ? "sqrt" : "root[" + Description(node.Children[1]) + "]") + "(" + Description(node.Children[0]) + ")",
        _ => node.Opening + Description(node.Children[0]) + node.Closing
    };
    internal static XElement Write(EquationExpression expression, bool display, double size)
    {
        IEnumerable<XElement> Nodes(EquationExpression node) => node.Kind == EquationKind.Row ? node.Children.SelectMany(Nodes) : new[] { Node(node) };
        XElement Row(string name, EquationExpression node) => new XElement(M + name, Nodes(node));
        XElement Node(EquationExpression node)
        {
            if (node.Kind == EquationKind.Text) return new XElement(M + "r", new XElement(M + "rPr", new XElement(M + "sty", new XAttribute(M + "val", node.Italic ? "i" : "p"))),
                new XElement(W + "rPr", new XElement(W + "sz", new XAttribute(W + "val", checked((int)Math.Round(size * 2))))), new XElement(M + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), node.Value));
            if (node.Kind == EquationKind.Fraction) return new XElement(M + "f", Row("num", node.Children[0]), Row("den", node.Children[1]));
            if (node.Kind == EquationKind.Radical) return new XElement(M + "rad", new XElement(M + "radPr", new XElement(M + "degHide", new XAttribute(M + "val", node.Children.Count == 1 ? "1" : "0"))), node.Children.Count == 1 ? new XElement(M + "deg") : Row("deg", node.Children[1]), Row("e", node.Children[0]));
            if (node.Kind == EquationKind.Delimiter) return new XElement(M + "d", new XElement(M + "dPr", new XElement(M + "begChr", new XAttribute(M + "val", node.Opening)), new XElement(M + "endChr", new XAttribute(M + "val", node.Closing))), Row("e", node.Children[0]));
            string kind = node.Kind == EquationKind.Subscript ? "sSub" : node.Kind == EquationKind.Superscript ? "sSup" : "sSubSup";
            return new XElement(M + kind, Row("e", node.Children[0]), node.Kind != EquationKind.Superscript ? Row("sub", node.Children[1]) : null,
                node.Kind == EquationKind.Subscript ? null : Row("sup", node.Children[node.Kind == EquationKind.Superscript ? 1 : 2]));
        }
        XElement math = Row("oMath", expression); return display ? new XElement(M + "oMathPara", new XElement(M + "oMathParaPr", new XElement(M + "jc", new XAttribute(M + "val", "center"))), math) : math;
    }
}
