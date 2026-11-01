using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace WordNet.Internal;

internal sealed class XmlSourceLocation
{
    private readonly Dictionary<XElement, int> indices = new Dictionary<XElement, int>();
    private readonly XElement parent;
    private HashSet<string>? ambiguousNames;
    private Dictionary<XElement, int>? absoluteIndices;
    private XmlSourceLocation(XElement parent, Action? work)
    {
        this.parent = parent;
        var counts = new Dictionary<XName, int>();
        foreach (XElement child in parent.Elements())
        {
            work?.Invoke();
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

    // Numeric positions count all element children, not just same-named siblings.
    // Build each parent's index once and charge the inspection work budget.
    internal static int[] ChildIndices(XElement element, Action work)
    {
        var result = new List<int>();
        foreach (XElement item in element.AncestorsAndSelf())
        {
            work();
            result.Add(item.Parent is XElement owner ? For(owner, work).AbsoluteIndex(item, work) : 1);
        }
        result.Reverse();
        return result.ToArray();
    }

    private int AbsoluteIndex(XElement element, Action work)
    {
        if (absoluteIndices == null)
        {
            var positions = new Dictionary<XElement, int>(); int index = 0;
            foreach (XElement child in parent.Elements()) { work(); positions.Add(child, checked(++index)); }
            absoluteIndices = positions;
        }
        return absoluteIndices[element];
    }

    // Public paths omit namespaces. Do not assert a unique owner when another
    // sibling namespace uses the same local name at any segment of the path.
    internal static bool Unambiguous(XElement element, ConversionContext context)
    {
        foreach (XElement item in element.AncestorsAndSelf())
        {
            context.CheckCancellation();
            if (item.Parent is XElement parent && For(parent, context.CheckCancellation).AmbiguousNames(context).Contains(item.Name.LocalName)) return false;
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

    private static XmlSourceLocation For(XElement parent, Action? work = null)
    {
        var index = parent.Annotation<XmlSourceLocation>();
        if (index == null) { index = new XmlSourceLocation(parent, work); parent.AddAnnotation(index); }
        return index;
    }

    private static int Index(XElement element)
    {
        XElement? parent = element.Parent;
        if (parent == null) return 1;
        return For(parent).indices[element];
    }
}
