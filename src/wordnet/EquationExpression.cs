using System;
using System.Collections.Generic;
using System.Linq;
using WordNet.Internal;

namespace WordNet;

/// <summary>Admitted immutable OMML expression forms.</summary>
public enum EquationKind
{
    /// <summary>Plain mathematical text.</summary>
    Text,
    /// <summary>A horizontal expression row.</summary>
    Row,
    /// <summary>A stacked numerator and denominator.</summary>
    Fraction,
    /// <summary>A base with a subscript.</summary>
    Subscript,
    /// <summary>A base with a superscript.</summary>
    Superscript,
    /// <summary>A base with both scripts.</summary>
    SubSuperscript,
    /// <summary>A square or indexed radical.</summary>
    Radical,
    /// <summary>Simple paired delimiters around one expression.</summary>
    Delimiter,
}

/// <summary>A closed, immutable expression tree; no XML, executable formula or provider object is accepted.</summary>
public sealed class EquationExpression
{
    private EquationExpression(EquationKind kind, string text, bool italic, string opening, string closing, params EquationExpression[] children)
    {
        if (children.Any(c => c == null)) throw new ArgumentException("Equation.Child: Expression children cannot be null.", nameof(children));
        int count = checked(1 + children.Sum(c => c.NodeCount)), depth = 1 + children.Select(c => c.Depth).DefaultIfEmpty(0).Max();
        long characters = text.Length + children.Sum(c => c.Characters);
        if (count > 10000 || depth > 32 || characters > 1048576) throw new ArgumentException("Equation.Limit: Expression tree exceeds its bounded profile.", nameof(children));
        Kind = kind; Value = text; Italic = italic; Opening = opening; Closing = closing;
        Children = Array.AsReadOnly((EquationExpression[])children.Clone()); NodeCount = count; Depth = depth; Characters = characters;
    }
    /// <summary>Gets the expression form.</summary>
    public EquationKind Kind { get; }
    /// <summary>Gets the text of a text node, otherwise empty.</summary>
    public string Value { get; }
    /// <summary>Gets whether Latin variable letters use mathematical italic glyphs.</summary>
    public bool Italic { get; }
    /// <summary>Gets immutable child nodes in the documented mathematical order.</summary>
    public IReadOnlyList<EquationExpression> Children { get; }
    /// <summary>Gets the opening delimiter, otherwise empty.</summary>
    public string Opening { get; }
    /// <summary>Gets the closing delimiter, otherwise empty.</summary>
    public string Closing { get; }
    internal int NodeCount { get; }
    internal int Depth { get; }
    internal long Characters { get; }
    /// <summary>Creates mathematical text; Latin letters are italic by default.</summary>
    public static EquationExpression Text(string value, bool italic = true)
    { ObjectText.Check(value, nameof(value), 1024); return new EquationExpression(EquationKind.Text, value, italic, "", ""); }
    /// <summary>Creates a nonempty horizontal row from a copied array.</summary>
    public static EquationExpression Row(params EquationExpression[] children)
    {
        Guard.NotNull(children, nameof(children));
        if (children.Length is < 1 or > 10000) throw new ArgumentException("Equation.Row: A bounded nonempty row is required.", nameof(children));
        return new EquationExpression(EquationKind.Row, "", false, "", "", children);
    }
    /// <summary>Creates numerator over denominator.</summary>
    public static EquationExpression Fraction(EquationExpression numerator, EquationExpression denominator) => new EquationExpression(EquationKind.Fraction, "", false, "", "", numerator, denominator);
    /// <summary>Creates a base with its subscript.</summary>
    public static EquationExpression Subscript(EquationExpression basis, EquationExpression subscript) => new EquationExpression(EquationKind.Subscript, "", false, "", "", basis, subscript);
    /// <summary>Creates a base with its superscript.</summary>
    public static EquationExpression Superscript(EquationExpression basis, EquationExpression superscript) => new EquationExpression(EquationKind.Superscript, "", false, "", "", basis, superscript);
    /// <summary>Creates a base, subscript and superscript, in that child order.</summary>
    public static EquationExpression SubSuperscript(EquationExpression basis, EquationExpression subscript, EquationExpression superscript) => new EquationExpression(EquationKind.SubSuperscript, "", false, "", "", basis, subscript, superscript);
    /// <summary>Creates a square root, or an indexed root with degree as the second child.</summary>
    public static EquationExpression Root(EquationExpression radicand, EquationExpression? degree = null) => degree == null
        ? new EquationExpression(EquationKind.Radical, "", false, "", "", radicand)
        : new EquationExpression(EquationKind.Radical, "", false, "", "", radicand, degree);
    /// <summary>Creates simple parentheses, brackets, braces, single bars or double bars.</summary>
    public static EquationExpression Delimiter(EquationExpression expression, string opening = "(", string closing = ")")
    {
        if ((opening, closing) is not ("(", ")") and not ("[", "]") and not ("{", "}") and not ("|", "|") and not ("‖", "‖")) throw new ArgumentException("Equation.Delimiter: Unsupported delimiter pair.", nameof(opening));
        return new EquationExpression(EquationKind.Delimiter, "", false, opening, closing, expression);
    }
}
