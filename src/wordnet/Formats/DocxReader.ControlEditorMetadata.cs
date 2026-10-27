using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using WordNet.Internal;

namespace WordNet.Formats;

internal sealed partial class DocxReader
{
    private readonly Dictionary<XElement, bool> typedDefaultScript = new Dictionary<XElement, bool>();

    private void ReportTypedEditorMetadata() => context.Report("Control.EditorMetadata", "Editor identity, proofing, cached page markers or inactive script slots are omitted; admitted control text and primary appearance are retained.");

    private void ValidateTypedTextOwnerAttributes(XElement element)
    {
        if (element.Name == W + "p")
            Attributes(element, W + "rsidR", W + "rsidRPr", W + "rsidP", W + "rsidDel", W + "rsidRDefault", ControlValues.W14 + "paraId", ControlValues.W14 + "textId");
        else Attributes(element, W + "rsidR", W + "rsidRPr", W + "rsidDel");
        foreach (XAttribute attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (attribute.Value.Length != 8 || attribute.Value.Any(ch => !Uri.IsHexDigit(ch))) throw new InvalidDataException("Control.Malformed: Editor identity and revision-session attributes require eight hexadecimal digits.");
            ReportTypedEditorMetadata();
        }
    }

    private void NormalizeTypedEditorRunProperties(XElement properties)
    {
        foreach (XElement property in properties.Elements().ToArray())
        {
            if (property.Name == W + "rFonts")
            {
                Attributes(property, W + "ascii", W + "hAnsi", W + "cs", W + "eastAsia", W + "hint");
                foreach (XAttribute slot in property.Attributes().Where(a => a.Name == W + "cs" || a.Name == W + "eastAsia" || a.Name == W + "hint").ToArray())
                {
                    if (slot.Name == W + "hint")
                    { if (slot.Value is not "default" and not "cs" and not "eastAsia") throw new InvalidDataException("Control.Malformed: Invalid font hint."); }
                    else if (slot.Value.Length is 0 or > 256) throw new InvalidDataException("Limit.Controls: Script font names must be bounded.");
                    if (slot.Name != W + "hint" || slot.Value != "default") RequireTypedDefaultScript(properties);
                    slot.Remove(); ReportTypedEditorMetadata();
                }
            }
            else if (property.Name == W + "szCs" || property.Name == W + "bCs" || property.Name == W + "iCs")
            {
                Leaf(property, W + "val");
                if (property.Name == W + "szCs") _ = Integer(property, W + "val", 2, 3168);
                else _ = ControlValues.On(property, W);
                RequireTypedDefaultScript(properties); property.Remove(); ReportTypedEditorMetadata();
            }
            else if (property.Name == W + "lang")
            {
                Leaf(property, W + "val", W + "eastAsia", W + "bidi");
                if (property.Attributes().Any(a => !a.IsNamespaceDeclaration && a.Value.Length is 0 or > 85)) throw new InvalidDataException("Limit.Controls: Language metadata must be bounded.");
                property.Remove(); ReportTypedEditorMetadata();
            }
        }
    }

    private void RequireTypedDefaultScript(XElement properties)
    {
        XElement scope = properties.Ancestors(W + "sdt").FirstOrDefault() ?? properties.Ancestors(W + "docPart").FirstOrDefault() ?? properties;
        if (!typedDefaultScript.TryGetValue(scope, out bool supported))
        {
            supported = true;
            foreach (XElement text in scope.Descendants(W + "t"))
            {
                context.ReserveControlWork(text.Value.Length);
                foreach (char ch in text.Value)
                {
                    UnicodeCategory category = char.GetUnicodeCategory(ch);
                    if (char.IsSurrogate(ch) || ch > '\u024f' && (char.IsLetterOrDigit(ch) || category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)) supported = false;
                }
            }
            typedDefaultScript.Add(scope, supported);
        }
        if (!supported) throw new NotSupportedException("Control.ScriptFormatting: Script-specific font/size/emphasis slots require independently qualified script rendering; only Latin/default-script control text is admitted with those slots.");
    }
}
