using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace WordNet.Internal;

internal sealed class XmlSourceLocation
{
    private readonly Dictionary<XElement, int> indices = new Dictionary<XElement, int>();
    private HashSet<string>? ambiguousNames;
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

    // Public paths omit namespaces. Do not assert a unique owner when another
    // sibling namespace uses the same local name at any segment of the path.
    internal static bool Unambiguous(XElement element, ConversionContext context)
    {
        foreach (XElement item in element.AncestorsAndSelf())
        {
            context.CheckCancellation();
            if (item.Parent is XElement parent && For(parent).AmbiguousNames(context).Contains(item.Name.LocalName)) return false;
        }
        return true;
    }

    private HashSet<string> AmbiguousNames(ConversionContext context)
    {
        if (ambiguousNames != null) return ambiguousNames;
        var names = new HashSet<string>(StringComparer.Ordinal);
        var namespaces = new Dictionary<string, XNamespace>(StringComparer.Ordinal);
        foreach (XElement child in indices.Keys)
        {
            context.CheckCancellation();
            if (namespaces.TryGetValue(child.Name.LocalName, out XNamespace? existing) && existing != child.Name.Namespace)
                names.Add(child.Name.LocalName);
            else namespaces[child.Name.LocalName] = child.Name.Namespace;
        }
        ambiguousNames = names;
        return names;
    }

    private static XmlSourceLocation For(XElement parent)
    {
        var index = parent.Annotation<XmlSourceLocation>();
        if (index == null) { index = new XmlSourceLocation(parent); parent.AddAnnotation(index); }
        return index;
    }

    private static int Index(XElement element)
    {
        XElement? parent = element.Parent;
        if (parent == null) return 1;
        return For(parent).indices[element];
    }
}
