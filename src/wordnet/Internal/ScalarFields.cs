using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WordNet.Model;

namespace WordNet.Internal;

// A closed, bounded field grammar. Never delegates to a scripting or expression engine.
internal static class ScalarFields
{
    internal static bool Candidate(string instruction) => DocumentFields.Kind(instruction) is "DOCPROPERTY" or "IF" or "=";

    internal static string Render(TextRun run, DocumentModel model, ConversionContext context)
    {
        try
        {
            int nodes = 0;
            object value = Evaluate(run, model, context, new HashSet<TextRun>(), 0, ref nodes);
            string result = Display(value);
            context.CheckCharacters(result.Length);
            return result;
        }
        catch (Exception error) when (error is FormatException or OverflowException or DivideByZeroException)
        {
            context.Report("Field.UnsupportedUsedCachedResult", "A field outside the bounded typed scalar grammar, missing property or arithmetic error retains its complete cached result.");
            return run.Text;
        }
    }

    // Table dependencies consume the unformatted decimal, not a rounded or
    // decorated cached display value. The caller stages every result before commit.
    internal static (decimal Number, string Display) EvaluateTable(TextRun run, DocumentModel model, ConversionContext context,
        Func<string, string, decimal> reference)
    {
        int nodes = 0; decimal? number = null;
        object value = Evaluate(run, model, context, new HashSet<TextRun>(), 0, ref nodes, reference, n => number = n);
        if (!number.HasValue) throw new FormatException();
        return (number.Value, Display(value));
    }

    private static string Display(object value) => value switch
    {
        string text => text,
        decimal number => number.ToString("0.############################", CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        DateTimeOffset date => date.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture),
        _ => throw new FormatException()
    };

    private static object Evaluate(TextRun run, DocumentModel model, ConversionContext context, HashSet<TextRun> visiting, int depth, ref int nodes,
        Func<string, string, decimal>? reference = null, Action<decimal>? numericResult = null)
    {
        context.CheckCancellation();
        if (depth >= Math.Min(16, context.Options.MaximumNestingDepth) || ++nodes > 256 || !visiting.Add(run)) throw new FormatException();
        var children = new Dictionary<int, object>();
        if (run.FieldChildren != null)
            foreach (var child in run.FieldChildren.OrderBy(p => p.Key))
                children.Add(child.Key, Evaluate(child.Value, model, context, visiting, depth + 1, ref nodes, reference));
        var parser = new Parser(run.FieldInstruction!, context, children, reference);
        string kind = parser.Take().Value.ToUpperInvariant();
        object result;
        if (kind == "DOCPROPERTY")
        {
            string name = parser.Take().Value;
            result = name.ToUpperInvariant() switch
            {
                "TITLE" => model.Title,
                "AUTHOR" => model.Author,
                _ => model.CustomProperties.TryGetValue(name, out DocumentProperty? property) ? property.Value : throw new FormatException()
            };
        }
        else if (kind == "IF")
        {
            Token left = parser.Operand(); string comparison = parser.Take().Value; Token right = parser.Operand();
            Token yes = parser.Take(), no = parser.Take();
            if (!yes.Quoted && yes.Typed == null || !no.Quoted && no.Typed == null) throw new FormatException();
            object l = left.Typed ?? (left.Quoted ? (object)left.Value : Number(left.Value));
            object r = right.Typed ?? (right.Quoted ? (object)right.Value : Number(right.Value));
            int order = l switch
            {
                string text when r is string other && !other.Contains('*') && !other.Contains('?') => StringComparer.Ordinal.Compare(text, other),
                decimal number when r is decimal other => number.CompareTo(other),
                bool flag when r is bool other => flag.CompareTo(other),
                DateTimeOffset date when r is DateTimeOffset other => date.CompareTo(other),
                _ => throw new FormatException()
            };
            bool condition = comparison switch { "=" => order == 0, "<>" => order != 0, "<" => order < 0, ">" => order > 0, "<=" => order <= 0, ">=" => order >= 0, _ => throw new FormatException() };
            Token chosen = condition ? yes : no; result = chosen.Typed ?? chosen.Value;
        }
        else if (kind == "=")
        {
            decimal number = decimal.Parse(parser.Expression(0).ToString("G15", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
            numericResult?.Invoke(number); result = number;
        }
        else throw new FormatException();
        if (parser.Peek is "\\#" or "\\@")
        {
            string picture = parser.Take().Value;
            Token pattern = parser.Take(); if (!pattern.Quoted) throw new FormatException();
            result = FieldPictures.Format(result, picture, pattern.Value);
        }
        if (parser.Peek == "\\*")
        {
            parser.Take();
            if (!parser.Take().Value.Equals("MERGEFORMAT", StringComparison.OrdinalIgnoreCase)) throw new FormatException();
        }
        if (parser.Peek != null) throw new FormatException();
        context.CheckCharacters(Display(result).Length);
        visiting.Remove(run);
        return result;
    }

    private static decimal Number(string value) => decimal.Parse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    private readonly struct Token
    {
        internal Token(string value, bool quoted = false, object? typed = null) { Value = value; Quoted = quoted; Typed = typed; }
        internal object? Typed { get; }
        internal string Value { get; }
        internal bool Quoted { get; }
    }
    private sealed class Parser
    {
        private readonly List<Token> tokens = new List<Token>();
        private readonly ConversionContext context;
        private readonly Func<string, string, decimal>? reference;
        private int position;
        internal Parser(string instruction, ConversionContext context, Dictionary<int, object> children, Func<string, string, decimal>? reference)
        {
            this.context = context;
            this.reference = reference;
            if (instruction.Length > 4096) throw new FormatException();
            context.ReserveFieldWork(instruction.Length);
            for (int i = 0; i < instruction.Length;)
            {
                context.CheckCancellation();
                if (char.IsWhiteSpace(instruction[i])) { i++; continue; }
                if (tokens.Count == 256) throw new FormatException();
                int start = i; char c = instruction[i++];
                if (c == '\ufffc')
                {
                    if (!children.TryGetValue(start, out object? child)) throw new FormatException();
                    tokens.Add(new Token(Display(child), typed: child)); continue;
                }
                if (c == '"')
                {
                    start = i;
                    var text = new System.Text.StringBuilder();
                    bool closed = false;
                    while (i < instruction.Length)
                    {
                        char next = instruction[i++];
                        if (next == '"') { closed = true; break; }
                        if (next == '\\' && i < instruction.Length && (instruction[i] == '"' || instruction[i] == '\\')) text.Append(instruction[i++]);
                        else if (next == '\ufffc')
                        { if (!children.TryGetValue(i - 1, out object? child)) throw new FormatException(); text.Append(Display(child)); }
                        else text.Append(next);
                        context.CheckCharacters(text.Length);
                    }
                    if (!closed) throw new FormatException();
                    tokens.Add(new Token(text.ToString(), true));
                }
                else if (c is '=' or '<' or '>' or '+' or '-' or '*' or '/' or '(' or ')')
                {
                    if (i < instruction.Length && (c is '<' or '>') && (instruction[i] == '=' || c == '<' && instruction[i] == '>')) i++;
                    tokens.Add(new Token(instruction.Substring(start, i - start)));
                }
                else
                {
                    if (c is '{' or '}') throw new FormatException();
                    while (i < instruction.Length && instruction[i] != '\ufffc' && !char.IsWhiteSpace(instruction[i]) && "=<>+-*/(){}\"".IndexOf(instruction[i]) < 0) i++;
                    // A Word formatting switch is a single token, including its '*' character.
                    if (c == '\\' && i == start + 1 && i < instruction.Length && instruction[i] == '*') i++;
                    tokens.Add(new Token(instruction.Substring(start, i - start)));
                }
            }
        }
        internal string? Peek => position < tokens.Count ? tokens[position].Value : null;
        internal Token Take()
        { context.CheckCancellation(); return position < tokens.Count ? tokens[position++] : throw new FormatException(); }
        internal Token Operand()
        {
            Token token = Take();
            if (token.Typed == null && !token.Quoted && token.Value is "+" or "-")
            {
                Token number = Take(); if (number.Quoted) throw new FormatException();
                return new Token(token.Value + number.Value);
            }
            return token;
        }
        internal decimal Expression(int depth)
        {
            decimal value = Product(depth);
            while (Peek is "+" or "-")
            { string operation = Take().Value; decimal right = Product(depth); value = operation == "+" ? checked(value + right) : checked(value - right); }
            return value;
        }
        private decimal Product(int depth)
        {
            decimal value = Primary(depth);
            while (Peek is "*" or "/")
            { string operation = Take().Value; decimal right = Primary(depth); value = operation == "*" ? checked(value * right) : value / right; }
            return value;
        }
        private decimal Primary(int depth)
        {
            if (depth >= Math.Min(16, context.Options.MaximumNestingDepth)) throw new FormatException();
            Token token = Take();
            if (token.Typed != null) return token.Typed is decimal numeric ? numeric : throw new FormatException();
            if (token.Quoted) throw new FormatException();
            if (token.Value is "+" or "-") return token.Value == "-" ? checked(-Primary(depth + 1)) : Primary(depth + 1);
            if (reference != null && token.Value.Equals("SUM", StringComparison.OrdinalIgnoreCase))
            {
                if (Take().Value != "(") throw new FormatException();
                Token argument = Take();
                if (argument.Quoted || argument.Typed != null || Take().Value != ")") throw new FormatException();
                return reference("SUM", argument.Value);
            }
            if (reference != null && token.Value.Length > 0 && char.IsLetter(token.Value[0])) return reference("CELL", token.Value);
            if (token.Value != "(") return Number(token.Value);
            decimal value = Expression(depth + 1);
            if (Take().Value != ")") throw new FormatException();
            return value;
        }
    }
}
