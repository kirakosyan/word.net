using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace WordNet.Internal;

internal static class TemplateData
{
    private static readonly Regex JsonNumber = new Regex("\\G-?(?:0|[1-9][0-9]*)(?:\\.[0-9]+)?(?:[eE][+-]?[0-9]+)?", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    internal static Dictionary<string, object?> Snapshot(object value, ConversionContext context, int maximumRows)
    {
        int nodes = 0; long characters = 0;
        object? root = Copy(value, 0);
        return root as Dictionary<string, object?> ?? throw new ArgumentException("Template data must be a named object.", nameof(value));
        object? Copy(object? item, int depth)
        {
            context.CheckCancellation();
            if (++nodes > context.Options.MaximumCharacters || depth > context.Options.MaximumNestingDepth) throw new InvalidDataException("Limit.TemplateData: Data structure exceeds its configured bounds.");
            if (nodes * 64L + characters * 2 > context.Options.MaximumExpandedBytes) throw new InvalidDataException("Limit.TemplateData: Data exceeds the aggregate resource bound.");
            if (item == null || item is bool or DateTime or DateTimeOffset or Guid or TemplateImage) return item;
            if (item is string text)
            {
                characters = checked(characters + text.Length); if (characters > context.Options.MaximumCharacters) throw new InvalidDataException("Limit.TemplateData: Data text exceeds the character bound.");
                return text;
            }
            if (item is byte or sbyte or short or ushort or int or uint or long or ulong or decimal) return item;
            if (item is double d) { if (double.IsNaN(d) || double.IsInfinity(d)) throw new ArgumentException("Template numbers must be finite.", nameof(value)); return item; }
            if (item is float f) { if (float.IsNaN(f) || float.IsInfinity(f)) throw new ArgumentException("Template numbers must be finite.", nameof(value)); return item; }
            var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (item is IDictionary source)
            {
                foreach (DictionaryEntry entry in source)
                {
                    if (entry.Key is not string key) throw new ArgumentException("Template keys must be strings.", nameof(value));
                    Add(key, entry.Value);
                }
                return dictionary;
            }
            if (item is IEnumerable<KeyValuePair<string, object?>> pairs)
            { foreach (var pair in pairs) Add(pair.Key, pair.Value); return dictionary; }
            if (item is IEnumerable sequence)
            {
                var list = new List<object?>();
                foreach (object? child in sequence)
                {
                    context.CheckCancellation();
                    if (list.Count >= maximumRows) throw new InvalidDataException("Limit.TemplateData: Data collection exceeds the row limit.");
                    list.Add(Copy(child, depth + 1));
                }
                return list;
            }
            if (item.GetType().IsValueType) throw new ArgumentException("Template.DataType: This scalar type is outside the supported data profile.", nameof(value));
            PropertyInfo[] properties = item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (PropertyInfo property in properties)
            {
                if (property.GetIndexParameters().Length != 0 || property.GetMethod?.IsPublic != true) continue;
                object? propertyValue;
                try { propertyValue = property.GetValue(item, null); }
                catch (TargetInvocationException) { throw new InvalidDataException("Template.DataAccess: A caller property getter failed."); }
                Add(property.Name, propertyValue);
            }
            return dictionary;
            void Add(string key, object? child)
            {
                if (!Identifier(key) || dictionary.ContainsKey(key)) throw new ArgumentException("Template keys must be unique supported identifiers.", nameof(value));
                dictionary.Add(key, Copy(child, depth + 1));
            }
        }
    }
    internal static bool Identifier(string name) => name.Length is > 0 and <= 64 && (name[0] == '_' || name[0] is >= 'a' and <= 'z' or >= 'A' and <= 'Z')
        && name.All(c => c == '_' || c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9');

    internal static object Json(string json, ConversionContext context)
    {
        Guard.NotNull(json, nameof(json)); context.CheckCharacters(json.Length);
        int position = 0, nodes = 0;
        object? root = Read(0); Space();
        if (position != json.Length || root is not Dictionary<string, object?>) throw Invalid();
        return root;
        object? Read(int depth)
        {
            context.CheckCancellation();
            if (depth > context.Options.MaximumNestingDepth || ++nodes > context.Options.MaximumCharacters) throw new InvalidDataException("Limit.TemplateData: JSON exceeds its structure bound.");
            if (nodes * 64L + json.Length * 2L > context.Options.MaximumExpandedBytes) throw new InvalidDataException("Limit.TemplateData: JSON exceeds the aggregate resource bound.");
            Space(); if (position == json.Length) throw Invalid(); char current = json[position++];
            if (current == '"') return String();
            if (current == '{')
            {
                var result = new Dictionary<string, object?>(StringComparer.Ordinal); Space();
                if (Take('}')) return result;
                do
                {
                    Space(); if (!Take('"')) throw Invalid(); string key = String(); Space();
                    if (!Take(':') || result.ContainsKey(key)) throw Invalid(); result.Add(key, Read(depth + 1)); Space();
                    if (Take('}')) return result;
                } while (Take(','));
                throw Invalid();
            }
            if (current == '[')
            {
                var result = new List<object?>(); Space(); if (Take(']')) return result;
                do { result.Add(Read(depth + 1)); Space(); if (Take(']')) return result; } while (Take(','));
                throw Invalid();
            }
            position--;
            foreach (string literal in new[] { "true", "false", "null" })
                if (position <= json.Length - literal.Length && string.CompareOrdinal(json, position, literal, 0, literal.Length) == 0)
                { position += literal.Length; return literal == "null" ? null : literal == "true"; }
            Match number = JsonNumber.Match(json, position);
            if (!number.Success) throw Invalid(); position += number.Length;
            if (decimal.TryParse(number.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value)) return value;
            if (double.TryParse(number.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double wide) && !double.IsInfinity(wide)) return wide;
            throw Invalid();
        }
        string String()
        {
            var text = new StringBuilder();
            while (position < json.Length)
            {
                context.CheckCancellation(); char current = json[position++];
                if (current == '"') return text.ToString();
                if (current < ' ') throw Invalid();
                if (current != '\\') { text.Append(current); continue; }
                if (position == json.Length) throw Invalid(); char escaped = json[position++];
                if (escaped == 'u')
                {
                    if (position > json.Length - 4) throw Invalid();
                    int code = 0;
                    for (int i = 0; i < 4; i++)
                    {
                        char digit = json[position++];
                        int value = digit is >= '0' and <= '9' ? digit - '0' : digit is >= 'a' and <= 'f' ? digit - 'a' + 10 : digit is >= 'A' and <= 'F' ? digit - 'A' + 10 : -1;
                        if (value < 0) throw Invalid(); code = code * 16 + value;
                    }
                    text.Append((char)code);
                }
                else text.Append(escaped switch { '"' => '"', '\\' => '\\', '/' => '/', 'b' => '\b', 'f' => '\f', 'n' => '\n', 'r' => '\r', 't' => '\t', _ => throw Invalid() });
            }
            throw Invalid();
        }
        bool Take(char c) { if (position == json.Length || json[position] != c) return false; position++; return true; }
        void Space() { while (position < json.Length && json[position] is ' ' or '\r' or '\n' or '\t') position++; }
        static InvalidDataException Invalid() => new InvalidDataException("Template.Json: Invalid, duplicate-key or unsupported JSON input.");
    }
}
