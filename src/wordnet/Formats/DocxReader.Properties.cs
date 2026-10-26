using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxReader
{
    private readonly Dictionary<string, DocumentProperty> customProperties = new Dictionary<string, DocumentProperty>(StringComparer.OrdinalIgnoreCase);
    private bool readCustomProperties;
    private void ReadCustomProperties(XElement root)
    {
        XNamespace cp = "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";
        XNamespace vt = "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";
        if (readCustomProperties || root.Name != cp + "Properties") throw new InvalidDataException("Docx.CustomProperties: Duplicate or invalid property part.");
        readCustomProperties = true;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<int>();
        foreach (XElement element in root.Elements())
        {
            context.CheckCancellation();
            string name = DocxPackage.RequiredAttribute(element, "name");
            if (element.Name != cp + "property" || !names.Add(name) || !ids.Add(Integer(element, "pid", 2, int.MaxValue))
                || (string?)element.Attribute("fmtid") != "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}" || element.Elements().Count() != 1)
                throw new InvalidDataException("Docx.CustomProperties: Invalid or duplicate property.");
            if (names.Count > 1024) throw new InvalidDataException("Limit.CustomProperties: Too many properties.");
            XElement value = element.Elements().Single();
            DocumentProperty? property = null;
            try
            {
                if (element.Attribute("linkTarget") == null && value.Name.Namespace == vt && !value.HasElements)
                    property = value.Name.LocalName switch
                    {
                        "lpwstr" => new DocumentProperty(name, value.Value),
                        "bool" => new DocumentProperty(name, System.Xml.XmlConvert.ToBoolean(value.Value)),
                        "decimal" => new DocumentProperty(name, decimal.Parse(value.Value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)),
                        "i4" => new DocumentProperty(name, (decimal)int.Parse(value.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)),
                        "i8" => new DocumentProperty(name, (decimal)long.Parse(value.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)),
                        "filetime" when System.Text.RegularExpressions.Regex.IsMatch(value.Value, "(?:Z|[+-][0-9]{2}:[0-9]{2})$", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) => new DocumentProperty(name, System.Xml.XmlConvert.ToDateTimeOffset(value.Value)),
                        _ => null
                    };
            }
            catch (Exception error) when (error is FormatException or OverflowException or ArgumentException)
            { throw new InvalidDataException("Docx.CustomProperties: Invalid property value.", error); }
            if (property == null) context.Loss("Docx.CustomPropertyType", "Linked or unsupported custom-property types are omitted; their fields retain safe caches.");
            else customProperties.Add(name, property);
        }
    }
}
