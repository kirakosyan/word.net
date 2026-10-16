using System.Globalization;
using System.Linq;
using WordNet.Model;

namespace WordNet.Internal;

// Explicit logical-model identifiers for adapters without physical source mapping.
// Never overwrite the more precise DOCX part/XML identifiers.
internal static class ModelSourceLocations
{
    internal static void Assign(DocumentModel model, DocumentFormat format, ConversionContext context)
    {
        Walk(model.Blocks, "/model/body");
        int section = 0;
        foreach (PageSettings settings in model.Blocks.Select(b => b.Section).Concat(new[] { model.FinalSection }).OfType<PageSettings>().Distinct())
        {
            section++;
            foreach (var story in settings.Stories.OrderBy(p => p.Key, System.StringComparer.Ordinal))
                Walk(story.Value, "/model/section[" + Number(section) + "]/" + story.Key);
        }

        void Walk(System.Collections.Generic.IEnumerable<DocumentBlock> blocks, string parent)
        {
            int paragraph = 0, tableIndex = 0;
            foreach (DocumentBlock block in blocks)
            {
                context.CheckCancellation();
                string path = parent + (block is ParagraphBlock ? "/paragraph[" + Number(++paragraph) : "/table[" + Number(++tableIndex)) + "]";
                block.Location ??= new SourceLocation(format.ToString(), path);
                if (block is TableBlock table)
                    for (int row = 0; row < table.Rows.Count; row++)
                        for (int cell = 0; cell < table.Rows[row].Count; cell++)
                        {
                            TableCell value = table.Rows[row][cell];
                            string cellPath = path + "/row[" + Number(row + 1) + "]/cell[" + Number(cell + 1) + "]";
                            Walk(value.Content.Count > 0 ? value.Content : value.Paragraphs, cellPath);
                        }
            }
        }
        static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
