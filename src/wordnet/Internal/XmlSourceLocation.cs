using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace WordNet.Internal;

internal sealed class XmlSourceLocation
{
    private readonly Dictionary<XElement, int> indices = new Dictionary<XElement, int>();
    private XmlSourceLocation(XElement parent)
    {
        var counts = new Dictionary<XName, int>();
        foreach (XElement child in parent.Elements())
        {
            counts.TryGetValue(child.Name, out int index);
            counts[child.Name] = ++index;
            indices.Add(child, index);
        }
    }

    internal static SourceLocation Create(XElement element, string part) => new SourceLocation(part, element);

    // Paths and sibling indices are built only for observed diagnostics. Cache one
    // index per observed parent, rather than annotating every node in a whole part.
    internal static string Path(XElement element) => "/" + string.Join("/", element.AncestorsAndSelf().Reverse().Select(e =>
        e.Name.LocalName + "[" + Index(e).ToString(CultureInfo.InvariantCulture) + "]"));

    private static int Index(XElement element)
    {
        XElement? parent = element.Parent;
        if (parent == null) return 1;
        var index = parent.Annotation<XmlSourceLocation>();
        if (index == null) { index = new XmlSourceLocation(parent); parent.AddAnnotation(index); }
        return index.indices[element];
    }
}
