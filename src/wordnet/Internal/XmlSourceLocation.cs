using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace WordNet.Internal;

internal sealed class XmlSourceLocation
{
    private XmlSourceLocation(int index) => Index = index;
    private int Index { get; }

    internal static SourceLocation Create(XElement element, string part)
    {
        XElement root = element.AncestorsAndSelf().Last();
        if (root.Annotation<XmlSourceLocation>() == null)
        {
            root.AddAnnotation(new XmlSourceLocation(1));
            foreach (XElement parent in root.DescendantsAndSelf())
            {
                var counts = new Dictionary<XName, int>();
                foreach (XElement child in parent.Elements())
                {
                    counts.TryGetValue(child.Name, out int count);
                    counts[child.Name] = ++count;
                    child.AddAnnotation(new XmlSourceLocation(count));
                }
            }
        }
        return new SourceLocation(part, "/" + string.Join("/", element.AncestorsAndSelf().Reverse().Select(e =>
            e.Name.LocalName + "[" + e.Annotation<XmlSourceLocation>()!.Index.ToString(CultureInfo.InvariantCulture) + "]")));
    }
}
