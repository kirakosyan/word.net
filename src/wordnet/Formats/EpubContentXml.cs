using System.IO;
using System.Xml;
using AngleSharp.Html;
using WordNet.Internal;

namespace WordNet.Formats;

internal static class EpubContentXml
{
    internal static void Validate(byte[] bytes, string part, ConversionContext context)
    {
        // Expand only XML character references. General entities remain references,
        // and are admitted only if they are predefined HTML named characters. The
        // DTD is ignored, never loaded or used to define replacement text.
        using var stream = new StringReader(TextFormat.Decode(bytes, context));
        using var reader = new XmlTextReader(stream)
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            EntityHandling = EntityHandling.ExpandCharEntities
        };
        try
        {
            long characters = 0;
            while (reader.Read())
            {
                context.CheckCancellation();
                if (reader.Depth > context.Options.MaximumNestingDepth)
                    throw new InvalidDataException("Limit.NestingDepth: EPUB chapter '" + part + "' exceeds the nesting limit.");
                characters += reader.Value.Length;
                if (characters > context.Options.MaximumCharacters)
                    throw new InvalidDataException("Limit.Characters: EPUB chapter '" + part + "' exceeds the character limit.");
                CheckEntity();
                if (reader.NodeType == XmlNodeType.Element && reader.MoveToFirstAttribute())
                {
                    do { while (reader.ReadAttributeValue()) CheckEntity(); } while (reader.MoveToNextAttribute());
                    reader.MoveToElement();
                }
            }
        }
        catch (XmlException error)
        { throw new InvalidDataException("Epub.ContentXml: Invalid XHTML in chapter '" + part + "' at line " + error.LineNumber + ", position " + error.LinePosition + ".", error); }

        void CheckEntity()
        {
            if (reader.NodeType != XmlNodeType.EntityReference) return;
            if (HtmlEntityProvider.Resolver.GetSymbol(reader.Name + ";") == null)
                throw new InvalidDataException("Epub.ContentEntity: Undeclared or unsupported entity in chapter '" + part + "'.");
        }
    }
}
