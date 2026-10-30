using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using UglyToad.PdfPig;
using Xunit;

namespace WordNet.Tests;

public sealed class DocxThemeTests
{
    private const string A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace W = TestDocuments.W;
    private static readonly XNamespace Ct = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static string Paragraph(string properties, string text = "Theme text") => "<w:p><w:r><w:rPr>" + properties + "</w:rPr><w:t>" + text + "</w:t></w:r></w:p>";
    private static string Theme(string major = "Liberation Mono", string minor = "Liberation Sans")
    {
        string[] names = { "dk1", "lt1", "dk2", "lt2", "accent1", "accent2", "accent3", "accent4", "accent5", "accent6", "hlink", "folHlink" };
        string[] colors = { "000000", "FFFFFF", "222222", "EEEEEE", "2266CC", "CC2200", "AA4400", "8844AA", "008888", "888800", "0000FF", "800080" };
        string scheme = string.Concat(names.Select((name, index) => "<a:" + name + "><a:srgbClr val='" + colors[index] + "'/></a:" + name + ">"));
        string Font(string name) => "<a:latin typeface='" + name + "'/><a:ea typeface=''/><a:cs typeface=''/>";
        string fill = "<a:solidFill><a:schemeClr val='phClr'/></a:solidFill>";
        string line = "<a:ln w='9525'>" + fill + "<a:prstDash val='solid'/></a:ln>";
        string effect = "<a:effectStyle><a:effectLst/></a:effectStyle>";
        return "<a:theme xmlns:a='" + A + "' name='Independent'><a:themeElements>"
            + "<a:clrScheme name='Independent'>" + scheme + "</a:clrScheme>"
            + "<a:fontScheme name='Independent'><a:majorFont>" + Font(major) + "</a:majorFont><a:minorFont>" + Font(minor) + "</a:minorFont></a:fontScheme>"
            + "<a:fmtScheme name='Independent'><a:fillStyleLst>" + fill + fill + fill + "</a:fillStyleLst>"
            + "<a:lnStyleLst>" + line + line + line + "</a:lnStyleLst><a:effectStyleLst>" + effect + effect + effect + "</a:effectStyleLst>"
            + "<a:bgFillStyleLst>" + fill + fill + fill + "</a:bgFillStyleLst></a:fmtScheme></a:themeElements></a:theme>";
    }
    private static byte[] Package(string body, bool includeTheme = true, string? theme = null, string? styles = null, string? settings = null,
        bool duplicateTheme = false, bool externalTheme = false, string? header = null, string? footnotes = null)
    {
        string relation = includeTheme ? "<Relationship Id='theme' Type='" + TestDocuments.R + "/theme' Target='theme/theme1.xml'"
            + (externalTheme ? " TargetMode='External'" : "") + "/>" : "";
        if (duplicateTheme) relation += "<Relationship Id='theme2' Type='" + TestDocuments.R + "/theme' Target='theme/theme1.xml'/>";
        if (settings != null) relation += "<Relationship Id='settings' Type='" + TestDocuments.R + "/settings' Target='settings.xml'/>";
        if (header != null) relation += "<Relationship Id='header' Type='" + TestDocuments.R + "/header' Target='header1.xml'/>";
        if (footnotes != null) relation += "<Relationship Id='footnotes' Type='" + TestDocuments.R + "/footnotes' Target='footnotes.xml'/>";
        byte[] bytes = TestDocuments.Docx(TestDocuments.Body(body), relation, styles,
            extra: includeTheme ? new KeyValuePair<string, string>("word/theme/theme1.xml", theme ?? Theme()) : null);
        using var output = new MemoryStream(); output.Write(bytes, 0, bytes.Length);
        using (var zip = new ZipArchive(output, ZipArchiveMode.Update, true))
        {
            ZipArchiveEntry typesEntry = zip.GetEntry("[Content_Types].xml")!; XDocument types;
            using (Stream source = typesEntry.Open()) types = XDocument.Load(source);
            typesEntry.Delete();
            if (includeTheme) types.Root!.Add(new XElement(Ct + "Override", new XAttribute("PartName", "/word/theme/theme1.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.theme+xml")));
            if (settings != null)
            {
                types.Root!.Add(new XElement(Ct + "Override", new XAttribute("PartName", "/word/settings.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml")));
                using var writer = new StreamWriter(zip.CreateEntry("word/settings.xml").Open(), new UTF8Encoding(false)); writer.Write(settings);
            }
            if (header != null) AddPart("header1.xml", "header", header);
            if (footnotes != null) AddPart("footnotes.xml", "footnotes", footnotes);
            using var target = zip.CreateEntry("[Content_Types].xml").Open(); types.Save(target);
            void AddPart(string name, string kind, string content)
            {
                types.Root!.Add(new XElement(Ct + "Override", new XAttribute("PartName", "/word/" + name),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml." + kind + "+xml")));
                using var writer = new StreamWriter(zip.CreateEntry("word/" + name).Open(), new UTF8Encoding(false)); writer.Write(content);
            }
        }
        return output.ToArray();
    }
    private static WordDocument Load(byte[] source, out ConversionResult result)
    {
        using var input = new MemoryStream(source);
        return WordDocument.Load(input, DocumentFormat.Docx, out result, new ConversionOptions(FidelityMode.Lenient), TestContext.Current.CancellationToken);
    }
    private static byte[] Save(WordDocument source, DocumentFormat format)
    { using var output = new MemoryStream(); source.Save(output, format, cancellationToken: TestContext.Current.CancellationToken); return output.ToArray(); }
    private static void Schema(byte[] bytes)
    { using var package = WordprocessingDocument.Open(new MemoryStream(bytes), false); Assert.Empty(new OpenXmlValidator().Validate(package)); }

    [Fact]
    public void LatinThemeFontAndColorRenderLikeIndependentExplicitFormatting()
    {
        string themeProperties = "<w:rFonts w:ascii='Liberation Sans' w:asciiTheme='majorAscii' w:hAnsiTheme='majorHAnsi'/><w:color w:val='FF0000' w:themeColor='accent1'/><w:sz w:val='22'/>";
        byte[] input = Package(Paragraph(themeProperties)); Schema(input);
        WordDocument source = Load(input, out ConversionResult result);
        Assert.Contains(result.Diagnostics, d => d.Code == "Docx.ThemeFont" && d.Severity == DiagnosticSeverity.Warning);
        Assert.Contains(result.Diagnostics, d => d.Code == "Docx.ThemeColor" && d.Severity == DiagnosticSeverity.Warning);
        Assert.Equal("Liberation Mono", source.Paragraphs[0].Runs[0].Formatting.FontFamily);
        Assert.Equal("2266CC", source.Paragraphs[0].Runs[0].Formatting.Color);
        WordDocument explicitDocument = Load(Package(Paragraph("<w:rFonts w:ascii='Liberation Mono' w:hAnsi='Liberation Mono'/><w:color w:val='2266CC'/><w:sz w:val='22'/>", "Theme text"), includeTheme: false), out _);
        using PdfDocument actual = PdfDocument.Open(Save(source, DocumentFormat.Pdf)); using PdfDocument expected = PdfDocument.Open(Save(explicitDocument, DocumentFormat.Pdf));
        var letters = actual.GetPage(1).Letters; var control = expected.GetPage(1).Letters; Assert.Equal(control.Count, letters.Count);
        Assert.Contains("LiberationMono", letters[0].FontName!.Replace("-", ""), StringComparison.OrdinalIgnoreCase);
        Assert.All(letters, l => { var rgb = l.Color.ToRGBValues(); Assert.Equal(34 / 255.0, rgb.r, 2); Assert.Equal(102 / 255.0, rgb.g, 2); Assert.Equal(204 / 255.0, rgb.b, 2); });
        for (int index = 0; index < letters.Count; index++)
        { Assert.Equal(control[index].Value, letters[index].Value); Assert.Equal(control[index].StartBaseLine.X, letters[index].StartBaseLine.X, 3); Assert.Equal(control[index].StartBaseLine.Y, letters[index].StartBaseLine.Y, 3); }
        using (var zip = new ZipArchive(new MemoryStream(Save(source, DocumentFormat.Xps))))
        using (Stream page = zip.GetEntry("Documents/1/Pages/1.fpage")!.Open())
            Assert.Contains(XDocument.Load(page).Descendants(), e => e.Name.LocalName == "Glyphs" && (string?)e.Attribute("Fill") == "#FF2266CC");
        byte[] saved = Save(source, DocumentFormat.Docx); Schema(saved);
        using (var zip = new ZipArchive(new MemoryStream(saved))) Assert.Null(zip.GetEntry("word/theme/theme1.xml"));
        using var strictSource = new MemoryStream(input); using var output = new MemoryStream(); output.WriteByte(42);
        Assert.Throws<NotSupportedException>(() => new WordConverter().Convert(strictSource, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.Strict), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray()); Assert.True(strictSource.CanRead); Assert.True(output.CanWrite);
    }

    [Fact]
    public void WordAuthoredThemeUsesLocalSchemeAndCallerConfiguredOpenFonts()
    {
        string wordTheme = TestDocuments.Fixture("Theme/OfficeWordParts/theme1.xml");
        string body = Paragraph("<w:rFonts w:asciiTheme='majorAscii'/><w:color w:val='FF0000' w:themeColor='accent1'/>", "Major")
            + Paragraph("<w:rFonts w:asciiTheme='minorAscii'/><w:color w:val='FF0000' w:themeColor='accent2'/>", "minor");
        byte[] bytes = Package(body, theme: wordTheme); Schema(bytes);
        var options = new ConversionOptions(FidelityMode.Lenient);
        options.Fonts.Substitutions["Cambria"] = "Caladea"; options.Fonts.Substitutions["Calibri"] = "Carlito";
        using var input = new MemoryStream(bytes);
        WordDocument source = WordDocument.Load(input, DocumentFormat.Docx, out ConversionResult load, options, TestContext.Current.CancellationToken);
        Assert.Equal("Cambria", source.Paragraphs[0].Runs[0].Formatting.FontFamily); Assert.Equal("4F81BD", source.Paragraphs[0].Runs[0].Formatting.Color);
        Assert.Equal("Calibri", source.Paragraphs[1].Runs[0].Formatting.FontFamily); Assert.Equal("C0504D", source.Paragraphs[1].Runs[0].Formatting.Color);
        Assert.Contains(load.Diagnostics, d => d.Code == "Docx.ThemeFont"); Assert.Contains(load.Diagnostics, d => d.Code == "Docx.ThemeColor");
        using var output = new MemoryStream(); ConversionResult result = source.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "Font.Substituted");
        using PdfDocument pdf = PdfDocument.Open(output.ToArray()); var letters = pdf.GetPage(1).Letters;
        var major = letters.First(l => l.Value == "M"); var minor = letters.First(l => l.Value == "m");
        Assert.Contains("Caladea", major.FontName!, StringComparison.OrdinalIgnoreCase); Assert.Contains("Carlito", minor.FontName!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(79 / 255.0, major.Color.ToRGBValues().r, 2); Assert.Equal(192 / 255.0, minor.Color.ToRGBValues().r, 2);
    }

    [Fact]
    public void ThemeColorsApplyTintShadeAndSettingsMapping()
    {
        string body = Paragraph("<w:color w:val='FF0000' w:themeColor='accent1'/>", "Base")
            + Paragraph("<w:color w:val='FF0000' w:themeColor='accent1' w:themeTint='99'/>", "Tint")
            + Paragraph("<w:color w:val='FF0000' w:themeColor='accent1' w:themeShade='80'/>", "Shade");
        byte[] input = Package(body); Schema(input); WordDocument source = Load(input, out _);
        Assert.Equal("2266CC", source.Paragraphs[0].Runs[0].Formatting.Color);
        Assert.Equal("7AA3E0", source.Paragraphs[1].Runs[0].Formatting.Color);
        Assert.Equal("113366", source.Paragraphs[2].Runs[0].Formatting.Color);
        using PdfDocument pdf = PdfDocument.Open(Save(source, DocumentFormat.Pdf));
        var page = pdf.GetPage(1); var first = page.Letters.First(l => l.Value == "B").Color.ToRGBValues(); var tint = page.Letters.First(l => l.Value == "T").Color.ToRGBValues();
        var shade = page.Letters.First(l => l.Value == "S").Color.ToRGBValues();
        Assert.Equal(34 / 255.0, first.r, 2); Assert.Equal(122 / 255.0, tint.r, 2); Assert.Equal(17 / 255.0, shade.r, 2);
        string settings = "<w:settings xmlns:w='" + W + "'><w:clrSchemeMapping w:bg1='dark2' w:t1='dark1' w:bg2='light2' w:t2='dark2'"
            + " w:accent1='accent2' w:accent2='accent2' w:accent3='accent3' w:accent4='accent4' w:accent5='accent5' w:accent6='accent6' w:hyperlink='hyperlink' w:followedHyperlink='followedHyperlink'/></w:settings>";
        input = Package(Paragraph("<w:color w:val='FF0000' w:themeColor='accent1'/>", "Mapped")
            + Paragraph("<w:color w:val='FF0000' w:themeColor='background1'/>", "Background"), settings: settings); Schema(input);
        WordDocument mapped = Load(input, out ConversionResult result); Assert.Equal("CC2200", mapped.Paragraphs[0].Runs[0].Formatting.Color);
        Assert.Equal("222222", mapped.Paragraphs[1].Runs[0].Formatting.Color);
        Assert.Contains(result.Diagnostics, d => d.Code == "Docx.Settings");
        string systemTheme = Theme().Replace("<a:dk1><a:srgbClr val='000000'/></a:dk1>", "<a:dk1><a:sysClr val='windowText' lastClr='123456'/></a:dk1>");
        input = Package(Paragraph("<w:color w:val='FF0000' w:themeColor='dark1'/>", "System"), theme: systemTheme); Schema(input);
        Assert.Equal("123456", Load(input, out _).Paragraphs[0].Runs[0].Formatting.Color);
    }

    [Theory]
    [InlineData("themeTint", "00", "FFFFFF")]
    [InlineData("themeTint", "FF", "2266CC")]
    [InlineData("themeShade", "00", "000000")]
    [InlineData("themeShade", "FF", "2266CC")]
    public void ThemeColorTransformEndpointsAreDeterministic(string transform, string value, string expected)
    {
        string properties = "<w:color w:val='FF0000' w:themeColor='accent1' w:" + transform + "='" + value + "'/>";
        WordDocument document = Load(Package(Paragraph(properties)), out _);
        Assert.Equal(expected, document.Paragraphs[0].Runs[0].Formatting.Color);
    }

    [Fact]
    public void HeaderAndFootnoteRunsUseTheDocumentTheme()
    {
        string properties = "<w:rFonts w:asciiTheme='majorAscii'/><w:color w:val='FF0000' w:themeColor='accent1'/>";
        string header = "<w:hdr xmlns:w='" + W + "'>" + Paragraph(properties, "HeaderTheme") + "</w:hdr>";
        string footnotes = "<w:footnotes xmlns:w='" + W + "'><w:footnote w:id='1'>" + Paragraph(properties, "FootnoteTheme") + "</w:footnote></w:footnotes>";
        string body = "<w:p><w:r><w:t>Body</w:t></w:r><w:r><w:footnoteReference w:id='1'/></w:r></w:p>"
            + "<w:sectPr><w:headerReference w:type='default' r:id='header'/></w:sectPr>";
        byte[] input = Package(body, header: header, footnotes: footnotes); Schema(input);
        WordDocument source = Load(input, out _); using PdfDocument pdf = PdfDocument.Open(Save(source, DocumentFormat.Pdf));
        var letters = pdf.GetPage(1).Letters;
        foreach (string value in new[] { "H", "F" })
        {
            var letter = letters.First(l => l.Value == value); Assert.Contains("LiberationMono", letter.FontName!.Replace("-", ""), StringComparison.OrdinalIgnoreCase);
            var color = letter.Color.ToRGBValues(); Assert.Equal(34 / 255.0, color.r, 2); Assert.Equal(102 / 255.0, color.g, 2); Assert.Equal(204 / 255.0, color.b, 2);
        }
    }

    [Fact]
    public void DirectCellThemeShadingAndBorderRenderWithExplicitLossDiagnostics()
    {
        string cell = "<w:tc><w:tcPr><w:tcW w:w='4000' w:type='dxa'/><w:tcBorders><w:top w:val='single' w:sz='8' w:color='FF0000' w:themeColor='accent1'/></w:tcBorders>"
            + "<w:shd w:val='clear' w:color='auto' w:fill='FF0000' w:themeFill='accent1' w:themeFillTint='99'/></w:tcPr>" + Paragraph("", "Cell") + "</w:tc>";
        string body = "<w:tbl><w:tblPr><w:tblW w:w='4000' w:type='dxa'/></w:tblPr><w:tblGrid><w:gridCol w:w='4000'/></w:tblGrid><w:tr>" + cell + "</w:tr></w:tbl>";
        byte[] input = Package(body); Schema(input); WordDocument source = Load(input, out ConversionResult result);
        Assert.Contains(result.Diagnostics, d => d.Code == "Docx.ThemeColor" && d.Severity == DiagnosticSeverity.Warning);
        using PdfDocument pdf = PdfDocument.Open(Save(source, DocumentFormat.Pdf)); var page = pdf.GetPage(1);
        Assert.Contains("Cell", page.Text, StringComparison.Ordinal);
        Assert.Contains(page.Paths, p => p.IsFilled && p.FillColor != null && Math.Abs(p.FillColor.ToRGBValues().r - 122 / 255.0) < 0.01
            && Math.Abs(p.FillColor.ToRGBValues().g - 163 / 255.0) < 0.01 && Math.Abs(p.FillColor.ToRGBValues().b - 224 / 255.0) < 0.01);
        using var zip = new ZipArchive(new MemoryStream(Save(source, DocumentFormat.Xps)));
        using Stream file = zip.GetEntry("Documents/1/Pages/1.fpage")!.Open(); XDocument xps = XDocument.Load(file);
        Assert.Contains(xps.Descendants(), e => (string?)e.Attribute("Fill") == "#FF7AA3E0");
        Assert.Contains(xps.Descendants(), e => (string?)e.Attribute("Stroke") == "#FF2266CC");
        byte[] saved = Save(source, DocumentFormat.Docx); Schema(saved);
    }

    [Fact]
    public void UnavailableLocalThemeFontUsesDiagnosedFallback()
    {
        byte[] input = Package(Paragraph("<w:rFonts w:asciiTheme='majorAscii'/>", "Missing face"), theme: Theme(major: "UnavailableThemeFace"));
        Schema(input); WordDocument source = Load(input, out ConversionResult load);
        Assert.Equal("UnavailableThemeFace", source.Paragraphs[0].Runs[0].Formatting.FontFamily);
        Assert.Contains(load.Diagnostics, d => d.Code == "Docx.ThemeFont");
        using var output = new MemoryStream(); ConversionResult result = source.Save(output, DocumentFormat.Pdf, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(result.Diagnostics, d => d.Code == "Font.Substituted" && d.Severity == DiagnosticSeverity.Warning);
        using PdfDocument pdf = PdfDocument.Open(output.ToArray()); Assert.Contains("Missing face", pdf.GetPage(1).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void CodeStyleThemeReferencesRemainExplicitlyDiagnosed()
    {
        string styles = "<w:styles xmlns:w='" + W + "'><w:style w:type='character' w:styleId='WordnetCode'><w:name w:val='Code'/>"
            + "<w:rPr><w:rFonts w:asciiTheme='majorAscii'/></w:rPr></w:style></w:styles>";
        byte[] input = Package(Paragraph("<w:rStyle w:val='WordnetCode'/>", "Code"), styles: styles); Schema(input);
        WordDocument source = Load(input, out ConversionResult result);
        Assert.Contains(result.Diagnostics, d => d.Code == "Docx.ThemeFont" && d.Severity == DiagnosticSeverity.Warning);
        using PdfDocument pdf = PdfDocument.Open(Save(source, DocumentFormat.Pdf)); Assert.Contains("Code", pdf.GetPage(1).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void NamespaceDeclarationsAreNotFontThemeReferences()
    {
        byte[] input = Package(Paragraph("<w:rFonts xmlns:hintTheme='urn:example' w:ascii='Liberation Sans'/>", "Explicit"), includeTheme: false);
        Schema(input); WordDocument source = Load(input, out ConversionResult result);
        Assert.Equal("Liberation Sans", source.Paragraphs[0].Runs[0].Formatting.FontFamily);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "Docx.ThemeFont");
    }

    [Fact]
    public void LocalThemeFontsResolveThroughDefaultsStylesAndDirectOverrides()
    {
        string styles = "<w:styles xmlns:w='" + W + "'><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:asciiTheme='majorAscii'/><w:b/></w:rPr></w:rPrDefault></w:docDefaults>"
            + "<w:style w:type='paragraph' w:default='1' w:styleId='Normal'><w:name w:val='Normal'/></w:style>"
            + "<w:style w:type='paragraph' w:styleId='Child'><w:name w:val='Child'/><w:basedOn w:val='Normal'/></w:style></w:styles>";
        string body = "<w:p><w:pPr><w:pStyle w:val='Child'/></w:pPr><w:r><w:t>Inherited</w:t></w:r></w:p>"
            + Paragraph("<w:rFonts w:ascii='Caladea'/><w:b w:val='0'/>", "Explicit") + Paragraph("<w:rFonts w:asciiTheme='minorAscii'/>", "Minor");
        byte[] input = Package(body, styles: styles); Schema(input); WordDocument source = Load(input, out _);
        Assert.Equal("Liberation Mono", source.Paragraphs[0].Runs[0].Formatting.FontFamily);
        Assert.Equal("Caladea", source.Paragraphs[1].Runs[0].Formatting.FontFamily);
        Assert.Equal("Liberation Sans", source.Paragraphs[2].Runs[0].Formatting.FontFamily);
        Assert.True(source.Paragraphs[0].Runs[0].Formatting.Bold); Assert.False(source.Paragraphs[1].Runs[0].Formatting.Bold);
    }

    [Fact]
    public void MissingUnsupportedAndMalformedThemesUseExplicitLimits()
    {
        string properties = "<w:rFonts w:ascii='Liberation Sans' w:asciiTheme='majorAscii'/><w:color w:val='FF0000' w:themeColor='accent1'/>";
        WordDocument fallback = Load(Package(Paragraph(properties), includeTheme: false), out ConversionResult result);
        Assert.Equal("Liberation Sans", fallback.Paragraphs[0].Runs[0].Formatting.FontFamily); Assert.Equal("FF0000", fallback.Paragraphs[0].Runs[0].Formatting.Color);
        Assert.Contains(result.Diagnostics, d => d.Code == "Docx.ThemeFont"); Assert.Contains(result.Diagnostics, d => d.Code == "Docx.ThemeColor");
        WordDocument unsupported = Load(Package(Paragraph("<w:rFonts w:ascii='Caladea' w:eastAsiaTheme='majorEastAsia'/><w:color w:val='FF0000' w:themeColor='accent1' w:themeShade='80' w:themeTint='99'/>")), out result);
        Assert.Equal("Caladea", unsupported.Paragraphs[0].Runs[0].Formatting.FontFamily); Assert.Equal("FF0000", unsupported.Paragraphs[0].Runs[0].Formatting.Color);
        Assert.Contains(result.Diagnostics, d => d.Code == "Docx.ThemeFont"); Assert.Contains(result.Diagnostics, d => d.Code == "Docx.ThemeColor");
        Assert.Throws<InvalidDataException>(() => Load(Package(Paragraph(properties), duplicateTheme: true), out _));
        Assert.Throws<InvalidDataException>(() => Load(Package(Paragraph(properties), externalTheme: true), out _));
        Assert.Throws<InvalidDataException>(() => Load(Package(Paragraph(properties), theme: Theme().Replace("2266CC", "bad")), out _));
        Assert.Throws<InvalidDataException>(() => Load(Package(Paragraph(properties), theme: "<a:other xmlns:a='" + A + "'/>"), out _));
        Assert.Throws<InvalidDataException>(() => Load(Package(Paragraph("<w:color w:val='FF0000' w:themeColor='accent1' w:themeTint='G0'/>")), out _));
        Assert.Throws<InvalidDataException>(() => Load(Package(Paragraph("<w:rFonts w:asciiTheme='unknown'/>")), out _));
        Assert.Throws<InvalidDataException>(() => Load(Package(Paragraph(properties), settings: "<w:settings xmlns:w='" + W + "'><w:clrSchemeMapping w:accent1='invalid'/></w:settings>"), out _));
        using var input = new MemoryStream(Package(Paragraph(properties))); using var output = new MemoryStream(); output.WriteByte(42);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => new WordConverter().Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf,
            new ConversionOptions(FidelityMode.Lenient), cancelled.Token));
        Assert.Equal(new byte[] { 42 }, output.ToArray()); Assert.True(input.CanRead); Assert.True(output.CanWrite);
    }
}
