using System;
using System.Linq;
using System.Xml.Linq;

namespace WordNet.Formats;

internal sealed partial class DocxReader
{
    private static readonly XNamespace Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    private bool Container(XElement element, out XElement? content)
    {
        content = element;
        if (element.Name == W + "sdt") { content = element.Element(W + "sdtContent"); return true; }
        if (element.Name == W + "customXml" || element.Name == W + "smartTag" || element.Name == W + "sdtContent") return true;
        if (element.Name == W + "ins" || element.Name == W + "moveTo" || element.Name == W + "del" || element.Name == W + "moveFrom")
        {
            context.Loss("Revisions.Accepted", "Revision markup is rendered in its accepted state; revision history is not retained.");
            if (element.Name == W + "del" || element.Name == W + "moveFrom") content = null;
            return true;
        }
        if (element.Name == W + "bookmarkStart" || element.Name == W + "bookmarkEnd" || element.Name == W + "proofErr"
            || element.Name == W + "permStart" || element.Name == W + "permEnd") { content = null; return true; }
        if (element.Name == W + "commentRangeStart" || element.Name == W + "commentRangeEnd" || element.Name == W + "commentReference")
        { context.Loss("Comments.Ignored", "Comments are omitted from the rendered document."); content = null; return true; }
        if (element.Name == W + "customXmlPr" || element.Name == W + "smartTagPr") { content = null; return true; }
        if (element.Name != Mc + "AlternateContent") return false;
        content = element.Elements(Mc + "Choice").FirstOrDefault(choice => ((string?)choice.Attribute("Requires") ?? "")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).All(prefix =>
            {
                XNamespace? ns = choice.GetNamespaceOfPrefix(prefix);
                return ns == W || ns == A || ns == Wp || ns == Pic;
            })) ?? element.Element(Mc + "Fallback");
        if (content == null) context.Loss("Docx.AlternateContent", "Alternate content without an understood choice or fallback is omitted.");
        return true;
    }
}
