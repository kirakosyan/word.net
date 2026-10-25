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
            context.CheckCancellation();
            var parser = new Parser(run.FieldInstruction!, context);
            string kind = parser.Take().Value.ToUpperInvariant();
            string result;
            if (kind == "DOCPROPERTY")
            {
                string name = parser.Take().Value;
                result = name.ToUpperInvariant() switch { "TITLE" => model.Title, "AUTHOR" => model.Author, _ => throw new FormatException() };
            }
            else if (kind == "IF")
            {
                Token left = parser.Operand(); string comparison = parser.Take().Value; Token right = parser.Operand();
                Token yes = parser.Take(), no = parser.Take();
                if (!yes.Quoted || !no.Quoted) throw new FormatException();
                int order;
                if (left.Quoted && right.Quoted) order = StringComparer.OrdinalIgnoreCase.Compare(left.Value, right.Value);
                else if (!left.Quoted && !right.Quoted) order = Number(left.Value).CompareTo(Number(right.Value));
                else throw new FormatException();
                bool condition = comparison switch { "=" => order == 0, "<>" => order != 0, "<" => order < 0, ">" => order > 0, "<=" => order <= 0, ">=" => order >= 0, _ => throw new FormatException() };
                result = condition ? yes.Value : no.Value;
            }
            else if (kind == "=") result = parser.Expression(0).ToString(CultureInfo.InvariantCulture);
            else throw new FormatException();
            if (parser.Peek == "\\*")
            {
                parser.Take();
                if (!parser.Take().Value.Equals("MERGEFORMAT", StringComparison.OrdinalIgnoreCase)) throw new FormatException();
            }
            if (parser.Peek != null) throw new FormatException();
            context.CheckCharacters(result.Length);
            return result;
        }
        catch (Exception error) when (error is FormatException or OverflowException or DivideByZeroException)
        {
            context.Loss("Field.UnsupportedUsedCachedResult", "A field outside the bounded scalar grammar, missing property or arithmetic error retains its cached result.");
            return run.Text;
        }
    }

    private static decimal Number(string value) => decimal.Parse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    private readonly struct Token
    {
        internal Token(string value, bool quoted = false) { Value = value; Quoted = quoted; }
        internal string Value { get; }
        internal bool Quoted { get; }
    }
    private sealed class Parser
    {
        private readonly List<Token> tokens = new List<Token>();
        private readonly ConversionContext context;
        private int position;
        internal Parser(string instruction, ConversionContext context)
        {
            this.context = context;
            if (instruction.Length > 4096) throw new FormatException();
            for (int i = 0; i < instruction.Length;)
            {
                context.CheckCancellation();
                if (char.IsWhiteSpace(instruction[i])) { i++; continue; }
                if (tokens.Count == 256) throw new FormatException();
                int start = i; char c = instruction[i++];
                if (c == '"')
                {
                    start = i;
                    while (i < instruction.Length && instruction[i] != '"') i++;
                    if (i == instruction.Length) throw new FormatException();
                    tokens.Add(new Token(instruction.Substring(start, i++ - start), true));
                }
                else if (c is '=' or '<' or '>' or '+' or '-' or '*' or '/' or '(' or ')')
                {
                    if (i < instruction.Length && (c is '<' or '>') && (instruction[i] == '=' || c == '<' && instruction[i] == '>')) i++;
                    tokens.Add(new Token(instruction.Substring(start, i - start)));
                }
                else
                {
                    if (c is '{' or '}') throw new FormatException();
                    while (i < instruction.Length && !char.IsWhiteSpace(instruction[i]) && "=<>+-*/(){}\"".IndexOf(instruction[i]) < 0) i++;
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
            if (!token.Quoted && token.Value is "+" or "-")
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
            if (token.Quoted) throw new FormatException();
            if (token.Value is "+" or "-") return token.Value == "-" ? checked(-Primary(depth + 1)) : Primary(depth + 1);
            if (token.Value != "(") return Number(token.Value);
            decimal value = Expression(depth + 1);
            if (Take().Value != ")") throw new FormatException();
            return value;
        }
    }
}
