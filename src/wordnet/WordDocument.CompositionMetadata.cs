using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet;

public sealed partial class WordDocument
{
    private static void MergeMetadata(DocumentModel destination, DocumentModel incoming, string prefix, ConversionContext context)
    {
        if (!destination.AllBlocks.Any() && destination.Theme == null) destination.Theme = incoming.Theme;
        else if (!(destination.Theme == null && incoming.Theme == null) && !(destination.Theme?.Same(incoming.Theme) ?? false))
            throw new NotSupportedException("Composition.ThemeConflict: Documents with different or missing themes cannot be composed without resolving their document-wide theme.");
        foreach (DocumentProperty property in incoming.CustomProperties.Values)
        {
            context.CheckCancellation();
            if (destination.CustomProperties.TryGetValue(property.Name, out DocumentProperty? existing))
            {
                if (existing.Type != property.Type || !existing.Value.Equals(property.Value))
                    throw new NotSupportedException("Composition.CustomPropertyConflict: Resolve conflicting document-wide custom properties before appending.");
            }
            else destination.CustomProperties.Add(property.Name, property);
        }
        var mapping = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var style in incoming.Styles)
        {
            context.CheckCancellation();
            string id = style.Key;
            if (destination.Styles.TryGetValue(id, out string? name) && name != style.Value)
            {
                id = prefix + "s" + mapping.Count.ToString(CultureInfo.InvariantCulture);
                while (destination.Styles.ContainsKey(id) || incoming.Styles.ContainsKey(id)) id += "_";
            }
            mapping.Add(style.Key, id); destination.Styles[id] = style.Value;
        }
        foreach (ParagraphBlock paragraph in incoming.Paragraphs)
        {
            if (paragraph.StyleId != null && mapping.TryGetValue(paragraph.StyleId, out string? replacement)) paragraph.StyleId = replacement;
            for (int i = 0; i < paragraph.Runs.Count; i++)
            {
                TextRun run = paragraph.Runs[i];
                if (run.FieldInstruction == null || DocumentFields.Kind(run.FieldInstruction) != "STYLEREF") continue;
                Match match = Regex.Match(run.FieldInstruction, "^\\s*STYLEREF\\s+(?:\"([^\"]+)\"|([^\\s\\\\]+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                if (!match.Success) continue;
                Group argument = match.Groups[1].Success ? match.Groups[1] : match.Groups[2];
                string[] ids = incoming.Styles.ContainsKey(argument.Value) ? new[] { argument.Value }
                    : incoming.Styles.Where(p => string.Equals(p.Value, argument.Value, StringComparison.OrdinalIgnoreCase)).Select(p => p.Key).ToArray();
                if (ids.Length != 1) continue;
                string instruction = run.FieldInstruction.Remove(argument.Index, argument.Length).Insert(argument.Index, mapping[ids[0]]);
                paragraph.Runs[i] = new TextRun(run.Text, run.Bold, run.Italic, run.Code, run.Link, run.FontSize, run.Color, run.Image,
                    run.Appearance.Copy(), instruction, run.Note, run.Box, run.Bookmark, run.Control, run.Object).RetainMetadata(run);
            }
        }
        incoming.Styles.Clear();
        foreach (var style in mapping) incoming.Styles.Add(style.Value, destination.Styles[style.Value]);
    }
}
