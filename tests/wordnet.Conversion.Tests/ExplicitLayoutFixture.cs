using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace WordNet.Tests;

// Historical coordinate/raster fixtures were authored against 12 pt + 6 pt after.
// Make those assumptions explicit in a detached input; never rewrite the fixtures,
// goldens or tolerances. P1LayoutTests separately exercises the new absent-value policy.
internal static class ExplicitLayoutFixture
{
    internal static byte[] LegacyMetrics(byte[] source)
    {
        XNamespace w = TestDocuments.W;
        using var output = new MemoryStream(); output.Write(source, 0, source.Length);
        using (var zip = new ZipArchive(output, ZipArchiveMode.Update, leaveOpen: true))
        {
            foreach (ZipArchiveEntry part in zip.Entries.Where(e => e.FullName.StartsWith("word/", System.StringComparison.Ordinal) && e.FullName.EndsWith(".xml", System.StringComparison.Ordinal)).ToArray())
            {
                XDocument xml;
                using (Stream stream = part.Open()) xml = XDocument.Load(stream);
                bool changed = false;
                foreach (XElement paragraph in xml.Descendants(w + "p"))
                {
                    string legacySize = paragraph.Ancestors(w + "tc").Any() ? "22" : "24";
                    XElement? properties = paragraph.Element(w + "pPr");
                    if (properties == null) { properties = new XElement(w + "pPr"); paragraph.AddFirst(properties); }
                    XElement? spacing = properties.Element(w + "spacing");
                    if (spacing == null)
                    {
                        spacing = new XElement(w + "spacing");
                        XElement? following = properties.Elements().FirstOrDefault(e => e.Name == w + "ind" || e.Name == w + "jc" || e.Name == w + "rPr" || e.Name == w + "sectPr");
                        if (following == null) properties.Add(spacing); else following.AddBeforeSelf(spacing);
                    }
                    if (spacing.Attribute(w + "after") == null) spacing.Add(new XAttribute(w + "after", paragraph.Parent?.Name == w + "tc" && !paragraph.ElementsAfterSelf().Any() ? "0" : "120"));
                    XElement? mark = properties.Element(w + "rPr");
                    if (mark == null) { mark = new XElement(w + "rPr"); XElement? section = properties.Element(w + "sectPr"); if (section == null) properties.Add(mark); else section.AddBeforeSelf(mark); }
                    if (mark.Element(w + "sz") == null) mark.Add(new XElement(w + "sz", new XAttribute(w + "val", legacySize)));
                    foreach (XElement run in paragraph.Descendants(w + "r"))
                    {
                        XElement? runProperties = run.Element(w + "rPr");
                        if (runProperties == null) { runProperties = new XElement(w + "rPr"); run.AddFirst(runProperties); }
                        if (runProperties.Element(w + "sz") == null) runProperties.Add(new XElement(w + "sz", new XAttribute(w + "val", legacySize)));
                    }
                    changed = true;
                }
                foreach (XElement table in xml.Descendants(w + "tbl"))
                {
                    XElement? properties = table.Element(w + "tblPr");
                    if (properties == null) { properties = new XElement(w + "tblPr"); table.AddFirst(properties); }
                    if (properties.Element(w + "tblLayout") == null) properties.Add(new XElement(w + "tblLayout", new XAttribute(w + "type", "fixed")));
                    changed = true;
                }
                if (changed)
                {
                    string name = part.FullName; part.Delete();
                    using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false)); xml.Save(writer);
                }
            }
        }
        return output.ToArray();
    }
}
