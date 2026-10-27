using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Xunit;
using static WordNet.Tests.ControlTestDocuments;

namespace WordNet.Tests;

public sealed class ContentControlDateTests
{
    private static byte[] CalendarInput()
    {
        XDocument source = XDocument.Parse(Fixture("dates.xml")); source.Root!.Element(W + "body")!.Elements(W + "p").Remove(); return Package(source.ToString());
    }
    [Theory]
    [InlineData("fr-FR")]
    [InlineData("tr-TR")]
    public void InstantDisplayUsesUtcAndInvariantPicturesWithoutClockOrTextInference(string culture)
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            int clockReads = 0;
            var options = new ConversionOptions { Clock = () => { clockReads++; return new DateTimeOffset(1990, 1, 1, 0, 0, 0, TimeSpan.Zero); } };
            WordDocument document = Load(Package(Fixture("dates.xml")), options);
            Assert.Equal("2025-12-31 22:30", document.ContentControls.Single(c => c.Id == 21).DisplayText);
            Assert.Equal(new DateTimeOffset(2025, 12, 31, 22, 30, 0, TimeSpan.Zero), document.ContentControls.Single(c => c.Id == 21).DateValue!.Value);
            Assert.Contains(document.Diagnostics, d => d.Code == "Control.CacheRefreshed"); Assert.Equal(0, clockReads);
            Assert.Equal("2024-02-292025-12-3122:30", Compact(Pdf(document))); Assert.Equal("2024-02-292025-12-3122:30", Compact(Xps(document)));
            byte[] word = Save(document); Validate(word);
            var type = Xml(word, "word/document.xml").Descendants(W + "date").Last(); Assert.Equal("2026-01-01T00:30:00+02:00", (string?)type.Attribute(W + "fullDate")); Assert.Equal("en-GB", (string?)type.Element(W + "lid")!.Attribute(W + "val"));
            document.SetControlDate(document.ContentControls.Single(c => c.Id == 21), ContentControlDateValue.FromInstant(new DateTimeOffset(2024, 3, 1, 1, 5, 0, TimeSpan.FromHours(2))), TestContext.Current.CancellationToken);
            Assert.Equal("2024-02-29 23:05", document.ContentControls.Single(c => c.Id == 21).DisplayText); Validate(Save(document));
        }
        finally { CultureInfo.CurrentCulture = before; }
    }
    [Theory]
    [InlineData(1, 1, 1, "0001-01-01")]
    [InlineData(2024, 2, 29, "2024-02-29")]
    [InlineData(9999, 12, 31, "9999-12-31")]
    public void CalendarDatesRoundTripWithExplicitPolicyAndNoHiddenTimeTruncation(int year, int month, int day, string expected)
    {
        WordDocument document = Load(CalendarInput(), mode: ContentControlDateMode.DateOnly);
        document.SetControlDate(Assert.Single(document.ContentControls), ContentControlDateValue.FromDate(year, month, day), TestContext.Current.CancellationToken);
        byte[] word = Save(document); Validate(word); Assert.Equal(expected + "T00:00:00Z", (string?)Xml(word, "word/document.xml").Descendants(W + "date").Single().Attribute(W + "fullDate"));
        Assert.Equal(expected, Pdf(document)); Assert.Equal(expected, Assert.Single(Load(word, mode: ContentControlDateMode.DateOnly).ContentControls).DisplayText);
        var handle = Assert.Single(document.ContentControls);
        Assert.Throws<ArgumentException>(() => document.SetControlDate(handle, ContentControlDateValue.FromInstant(DateTimeOffset.MinValue), TestContext.Current.CancellationToken));
        document.SetControlDate(handle, null, TestContext.Current.CancellationToken); Assert.Null(Assert.Single(document.ContentControls).DateValue); Assert.Equal("", Assert.Single(document.ContentControls).DisplayText); Validate(Save(document));
    }
    [Theory]
    [InlineData("2023-02-29T00:00:00Z")]
    [InlineData("2024-02-29")]
    [InlineData("2024-02-29T00:00:00")]
    [InlineData("0000-01-01T00:00:00Z")]
    [InlineData("2024-01-01T00:00:00.123456789Z")]
    public void MalformedOrImplicitZoneDatesAreRejected(string value)
    {
        string type = "<w:date w:fullDate='" + value + "'><w:dateFormat w:val='yyyy-MM-dd'/></w:date>";
        Assert.Throws<System.IO.InvalidDataException>(() => LoadBody(Control(1, type, "unchanged", block: true), new ConversionOptions(FidelityMode.Lenient)));
    }
    [Theory]
    [InlineData("2024-02-29T01:00:00Z")]
    [InlineData("2024-02-29T00:00:00+02:00")]
    public void CalendarModeRejectsImportedTimesAndOffsetsRatherThanChangingTheirDate(string value)
    {
        string type = "<w:date w:fullDate='" + value + "'><w:dateFormat w:val='yyyy-MM-dd'/></w:date>";
        Assert.Throws<System.IO.InvalidDataException>(() => LoadBody(Control(1, type, "cache", block: true), mode: ContentControlDateMode.DateOnly));
    }
    [Theory]
    [InlineData("<w:dateFormat w:val='yyyy zzz'/>")]
    [InlineData("<w:dateFormat w:val='yyyy-MM-dd'/><w:calendar w:val='hebrew'/>")]
    [InlineData("<w:dateFormat w:val='yyyy-MM-dd'/><w:lid w:val='fr-FR'/>")]
    [InlineData("")]
    public void UnsupportedCalendarsLocalesAndPicturesFailPreservation(string properties)
    { Assert.Throws<NotSupportedException>(() => LoadBody(Control(1, "<w:date>" + properties + "</w:date>", "cache", block: true), new ConversionOptions(FidelityMode.Lenient))); }
    [Fact]
    public void MissingDateKeepsLiteralCacheAndPlaceholderStateWithoutInferringAValue()
    {
        string type = "<w:date><w:dateFormat w:val='yyyy-MM-dd'/></w:date>";
        WordDocument document = LoadBody("<w:p>" + Control(1, type, "February 29") + Control(2, type, "Choose date", "<w:showingPlcHdr/>") + "</w:p>");
        Assert.Null(document.ContentControls[0].DateValue); Assert.Equal(ContentControlValueState.Unresolved, document.ContentControls[0].State);
        Assert.Equal(ContentControlValueState.Placeholder, document.ContentControls[1].State);
        document.SetControlDate(document.ContentControls[1], ContentControlDateValue.FromInstant(new DateTimeOffset(2024, 2, 29, 0, 0, 0, TimeSpan.Zero)), TestContext.Current.CancellationToken);
        document.ShowControlPlaceholder(document.ContentControls[1], TestContext.Current.CancellationToken); Assert.Equal("Choose date", document.ContentControls[1].DisplayText); Assert.Null(document.ContentControls[1].DateValue); Validate(Save(document));
    }
    [Fact]
    public void DateOnlyPicturesRejectTimeTokensButPermitQuotedTimeLetters()
    {
        string bad = "<w:date><w:dateFormat w:val='yyyy-MM-dd HH:mm'/></w:date>";
        Assert.Throws<NotSupportedException>(() => LoadBody(Control(1, bad, "cache", block: true), mode: ContentControlDateMode.DateOnly));
        var format = new XElement(W + "dateFormat", new XAttribute(W + "val", "yyyy-MM-dd 'time'"));
        WordDocument document = LoadBody(Control(1, "<w:date>" + format + "</w:date>", "", block: true), mode: ContentControlDateMode.DateOnly);
        document.SetControlDate(Assert.Single(document.ContentControls), ContentControlDateValue.FromDate(2024, 2, 29), TestContext.Current.CancellationToken);
        Assert.Equal("2024-02-29 time", Pdf(document));
    }
    [Fact]
    public void DatePictureLengthAndTokenBudgetsHaveAdmittedAndRejectedBoundaries()
    {
        WordDocument WithPicture(string picture)
        {
            var type = new XElement(W + "date", new XAttribute(W + "fullDate", "2024-02-29T00:00:00Z"), new XElement(W + "dateFormat", new XAttribute(W + "val", picture)));
            return LoadBody(Control(1, type.ToString(), "cache", block: true));
        }
        string literal = new string('x', 126);
        WordDocument maximumLength = WithPicture("'" + literal + "'");
        Assert.Equal(literal, maximumLength.ContentControls[0].DisplayText); Validate(Save(maximumLength));
        Assert.Throws<NotSupportedException>(() => WithPicture("'" + literal + "x'"));
        string maximumTokens = string.Concat(Enumerable.Repeat("M-", 32));
        WordDocument tokens = WithPicture(maximumTokens); Assert.Equal(string.Concat(Enumerable.Repeat("2-", 32)), tokens.ContentControls[0].DisplayText); Validate(Save(tokens));
        Assert.Throws<NotSupportedException>(() => WithPicture(maximumTokens + "M"));
    }

}
