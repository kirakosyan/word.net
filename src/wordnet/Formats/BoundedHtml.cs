using System.IO;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using WordNet.Internal;

namespace WordNet.Formats;

internal static class BoundedHtml
{
    internal static IHtmlDocument Parse(string text, ConversionContext context)
    {
        IElement? pending = null;
        int elements = 0;
        var parser = new HtmlParser(new HtmlParserOptions
        {
            IsScripting = false,
            // OnCreated runs before attachment. At the next creation/token the
            // previous element has been attached, so inspect its actual ancestry.
            OnCreated = (element, _) =>
            {
                Check();
                if (++elements > context.Options.MaximumCharacters)
                    throw new InvalidDataException("Limit.HtmlTree: HTML element count exceeds the limit.");
                pending = element;
            },
            OnToken = (_, _) => Check()
        });
        IHtmlDocument document = parser.ParseDocument(text);
        try { Check(); return document; }
        catch { document.Dispose(); throw; }

        void Check()
        {
            context.CheckCancellation();
            int depth = 0;
            for (INode? node = pending; node != null; node = node.Parent)
                if (depth++ > context.Options.MaximumNestingDepth)
                    throw new InvalidDataException("Limit.HtmlTree: HTML nesting exceeds the limit during parsing.");
        }
    }
}
