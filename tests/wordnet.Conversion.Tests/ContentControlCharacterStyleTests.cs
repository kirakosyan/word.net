using System;
using System.Linq;
using UglyToad.PdfPig;
using Xunit;
using static WordNet.Tests.ControlTestDocuments;

namespace WordNet.Tests;

public sealed class ContentControlCharacterStyleTests
{
    [Theory]
    [InlineData(false, "008000")]
    [InlineData(true, "224466")]
    public void BoundedCharacterStyleMatchesAnExplicitIndependentColorControl(bool directOverride, string color)
    {
        string styles = "<w:styles xmlns:w='" + W + "'><w:style w:type='character' w:styleId='Base'><w:name w:val='Base'/><w:rPr><w:color w:val='008000'/></w:rPr></w:style>"
            + "<w:style w:type='character' w:styleId='Child'><w:name w:val='Child'/><w:basedOn w:val='Base'/></w:style></w:styles>";
        string direct = directOverride ? "<w:color w:val='224466'/>" : "";
        string content = "<w:r><w:rPr><w:rStyle w:val='Child'/>" + direct + "</w:rPr><w:t>Control</w:t></w:r>";
        string sourceBody = "<w:p>" + Control(1, "<w:text/>", "", content: content) + "</w:p>";
        string explicitBody = "<w:p>" + Control(1, "<w:text/>", "", content: "<w:r><w:rPr><w:color w:val='" + color + "'/></w:rPr><w:t>Control</w:t></w:r>") + "</w:p>";
        byte[] source = TestDocuments.Docx(Document(sourceBody), styles: styles); Validate(source);
        WordDocument styled = Load(source, new ConversionOptions(FidelityMode.Lenient)), explicitColor = LoadBody(explicitBody);
        Assert.Contains(styled.Diagnostics, diagnostic => diagnostic.Code == "Control.CharacterStyle");
        using PdfDocument first = PdfDocument.Open(Save(styled, DocumentFormat.Pdf));
        using PdfDocument second = PdfDocument.Open(Save(explicitColor, DocumentFormat.Pdf));
        var left = first.GetPage(1).Letters; var right = second.GetPage(1).Letters;
        Assert.Equal("Control", string.Concat(left.Select(letter => letter.Value)));
        Assert.Equal(left.Count, right.Count);
        for (int index = 0; index < left.Count; index++)
        {
            Assert.Equal(right[index].Value, left[index].Value);
            Assert.Equal(right[index].Color.ToRGBValues(), left[index].Color.ToRGBValues());
            Assert.Equal(right[index].BoundingBox.Left, left[index].BoundingBox.Left, 2);
        }
        byte[] saved = Save(styled); Validate(saved);
        Assert.Empty(Xml(saved, "word/document.xml").Descendants(W + "rStyle"));
        Assert.Equal(color, (string?)Xml(saved, "word/document.xml").Descendants(W + "color").Single().Attribute(W + "val"));
        Assert.Equal("Control", Assert.Single(Load(saved).ContentControls).DisplayText);
    }
}
