using System;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using WordNet.Internal;

namespace WordNet.Formats;

internal sealed partial class DocxWriter
{
    private void WriteCustomProperties(ZipArchive archive, XElement types, XElement relations)
    {
        if (model.CustomProperties.Count == 0) return;
        CustomPropertyValues.Validate(model, context);
        XNamespace cp = "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";
        XNamespace vt = "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";
        var root = new XElement(cp + "Properties", new XAttribute(XNamespace.Xmlns + "vt", vt));
        int id = 2;
        foreach (DocumentProperty property in model.CustomProperties.Values.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            context.CheckCancellation();
            string display = CustomPropertyValues.Display(property);
            string type = property.Type switch { DocumentPropertyType.Text => "lpwstr", DocumentPropertyType.Boolean => "bool", DocumentPropertyType.Number => "decimal", _ => "filetime" };
            if (property.Value is decimal number)
            {
                if (number == decimal.Truncate(number) && number >= int.MinValue && number <= int.MaxValue) type = "i4";
                else if (decimal.Parse(number.ToString("G15", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture) == number) type = "r8";
            }
            root.Add(new XElement(cp + "property", new XAttribute("fmtid", "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}"),
                new XAttribute("pid", id++), new XAttribute("name", property.Name), new XElement(vt + type, display)));
        }
        relations.Add(Relationship("rCustom", "custom-properties", "docProps/custom.xml"));
        types.Add(new XElement(DocxPackage.ContentTypes + "Override", new XAttribute("PartName", "/docProps/custom.xml"),
            new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.custom-properties+xml")));
        WritePart(archive, "docProps/custom.xml", root);
    }
}
