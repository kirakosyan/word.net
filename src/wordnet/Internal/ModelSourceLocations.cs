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
        string part = format.ToString();
        Walk(model.Blocks, () => "/model/body");
        int section = 0;
        foreach (PageSettings settings in model.Blocks.Select(b => b.Section).Concat(new[] { model.FinalSection }).OfType<PageSettings>().Distinct())
        {
            string sectionPath = "/model/section[" + Number(++section) + "]/";
            foreach (var story in settings.Stories.OrderBy(p => p.Key, System.StringComparer.Ordinal))
                Walk(story.Value, () => sectionPath + story.Key);
        }

        void Walk(System.Collections.Generic.IEnumerable<DocumentBlock> blocks, System.Func<string> parentPath)
        {
            int paragraph = 0, tableIndex = 0;
            foreach (DocumentBlock block in blocks)
            {
                context.CheckCancellation();
                if (block is ParagraphBlock)
                {
                    paragraph++;
                    if (block.Location == null) block.Location = new SourceLocation(part, parentPath() + "/paragraph[" + Number(paragraph) + "]");
                    continue;
                }
                int index = ++tableIndex;
                string? path = null;
                string BlockPath() => path ??= parentPath() + "/table[" + Number(index) + "]";
                if (block.Location == null) block.Location = new SourceLocation(part, BlockPath());
                if (block is TableBlock table)
                    for (int row = 0; row < table.Rows.Count; row++)
                        for (int cell = 0; cell < table.Rows[row].Count; cell++)
                        {
                            TableCell value = table.Rows[row][cell];
                            int rowNumber = row + 1, cellNumber = cell + 1;
                            Walk(value.Content.Count > 0 ? value.Content : value.Paragraphs,
                                () => BlockPath() + "/row[" + Number(rowNumber) + "]/cell[" + Number(cellNumber) + "]");
                        }
            }
        }
        static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
