using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using WordNet.Formats;
using WordNet.Internal;
using Xunit;

namespace WordNet.Tests;

public sealed class ImportParsingResourceTests
{
    [Theory]
    [InlineData("<div>")]
    [InlineData("<b>")]
    [InlineData("<i><div>")]
    public void DeepHtmlRejectsDuringParsingForHtmlAndSharedEbookReader(string pattern)
    {
        string html = "<!doctype html><html><body>" + string.Concat(Enumerable.Repeat(pattern, 80000)) + "x</body></html>";
        var timer = Stopwatch.StartNew();
        Reject(html, DocumentFormat.Html, "Limit.HtmlTree");
        var error = Assert.Throws<InvalidDataException>(() => EbookContent.Read(Encoding.UTF8.GetBytes(html), Context()));
        Assert.StartsWith("Limit.HtmlTree:", error.Message, StringComparison.Ordinal);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5), "Deep HTML must reject before building the entire tree.");
    }

    [Theory]
    [InlineData("[a](")]
    [InlineData("[a](b")]
    [InlineData("![a](")]
    public void RepeatedUnclosedMarkdownLinksHaveBoundedWork(string pattern)
    {
        var timer = Stopwatch.StartNew();
        Reject(string.Concat(Enumerable.Repeat(pattern, 100000)), DocumentFormat.Markdown, "Limit.MarkdownWork");
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5), "Failed link lookahead must not grow quadratically.");
    }

    [Fact]
    public void FailedInlineLinksStillChargeWorkWhenAReferenceFallbackExists()
        => Reject("[a]: https://example.com\n\n" + string.Concat(Enumerable.Repeat("[a](", 100000)), DocumentFormat.Markdown, "Limit.MarkdownWork");

    [Theory]
    [InlineData("<div>")]
    [InlineData("<b>")]
    [InlineData("<i><div>")]
    public void DeepHtmlCanBeCancelledEvenWithARaisedDepthLimit(string pattern)
    {
        string html = string.Concat(Enumerable.Repeat(pattern, 80000));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var context = new ConversionContext(new ConversionOptions(FidelityMode.BestEffort, maximumNestingDepth: 1000000), cancellation.Token);
        var timer = Stopwatch.StartNew();
        using var deadline = new CancellationDeadline(cancellation, 10);
        Assert.ThrowsAny<OperationCanceledException>(() => { using var document = BoundedHtml.Parse(html, context); });
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5), "Deep HTML must observe cancellation while constructing the tree.");
    }

    [Theory]
    [InlineData("[a](")]
    [InlineData("[a](b")]
    [InlineData("![a](")]
    public void UnclosedMarkdownLinksObserveCancellationDuringLookahead(string pattern)
    {
        string text = string.Concat(Enumerable.Repeat(pattern, 100000));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var context = new ConversionContext(new ConversionOptions(FidelityMode.BestEffort), cancellation.Token);
        var builder = MarkdownFormat.CreatePipelineBuilder(context, text.Length);
        var cancelAtLookahead = new CancelAtLinkLookahead(cancellation);
        builder.InlineParsers.Insert(0, cancelAtLookahead);
        var pipeline = builder.Build();
        var timer = Stopwatch.StartNew();
        // Cancel inside inline parsing, immediately before the production link
        // parser runs. A timer can lose the race to a valid work-limit rejection.
        var error = Assert.ThrowsAny<OperationCanceledException>(() => Markdown.Parse(text, pipeline));
        Assert.True(cancelAtLookahead.Reached);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5), "Unclosed Markdown links must observe cancellation during parsing.");
    }

    [Fact]
    public void HtmlChecksCancellationWhileBuildingAnAdmittedTree()
    {
        string html = "<html><body>" + string.Concat(Enumerable.Repeat("<p>text</p>", 300000)) + "</body></html>";
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var context = new ConversionContext(new ConversionOptions(FidelityMode.BestEffort), cancellation.Token);
        var timer = Stopwatch.StartNew();
        using var deadline = new CancellationDeadline(cancellation, 50);
        Assert.ThrowsAny<OperationCanceledException>(() => { using var document = BoundedHtml.Parse(html, context); });
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5), "HTML cancellation must be observed inside parsing.");
    }

    [Fact]
    public void MarkdownChecksCancellationDuringInlineParsing()
    {
        // Many ordinary links avoid the failure budget and keep the parser busy.
        string text = string.Concat(Enumerable.Repeat("[a](https://example.com) ", 100000));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var context = new ConversionContext(new ConversionOptions(FidelityMode.BestEffort), cancellation.Token);
        var timer = Stopwatch.StartNew();
        using var deadline = new CancellationDeadline(cancellation, 50);
        Assert.ThrowsAny<OperationCanceledException>(() => MarkdownFormat.Read(text, context));
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5), "Markdown cancellation must be observed inside parsing.");
    }

    [Fact]
    public void HtmlGuardHandlesOptionalEndTagsCommentsRawTextAndAttributes()
    {
        string html = "<!--" + new string('<', 20000) + "--><script>" + string.Concat(Enumerable.Repeat("<div>", 20000))
            + "</script><ul>" + string.Concat(Enumerable.Repeat("<li title='> <div>'>item", 2000)) + "</ul>";
        using var document = BoundedHtml.Parse(html, Context());
        Assert.Equal(2000, document.QuerySelectorAll("li").Length);
        Assert.Equal("item", document.QuerySelector("li")!.TextContent);
    }

    [Fact]
    public void MarkdownGuardPreservesOrdinaryLinksAndLiteralUnclosedText()
    {
        string markdown = "[escaped](https://example.com/a\\(b\\)) [balanced](https://example.com/a(b))\n\n"
            + "`[a](`\n\n[a](\n\n" + string.Concat(Enumerable.Repeat("[good](https://example.com) ", 2000));
        var model = MarkdownFormat.Read(markdown, Context());
        Assert.Equal("escaped balanced", model.Paragraphs.First().Text);
        Assert.Contains(model.Paragraphs, p => p.Text == "[a](");
        Assert.Equal(2002, model.Paragraphs.SelectMany(p => p.Runs).Count(r => r.Link != null));
    }

    private static ConversionContext Context() => new ConversionContext(new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken);

    // The parallel full suite can occupy the thread pool long enough to delay
    // CancelAfter callbacks until parsing has completed. Keep the deadline on
    // its own thread, and join it before disposing the token source.
    private sealed class CancellationDeadline : IDisposable
    {
        private readonly ManualResetEventSlim started = new ManualResetEventSlim();
        private readonly ManualResetEventSlim completed = new ManualResetEventSlim();
        private readonly Thread worker;

        internal CancellationDeadline(CancellationTokenSource cancellation, int milliseconds)
        {
            worker = new Thread(() =>
            {
                started.Set();
                if (!completed.Wait(milliseconds)) cancellation.Cancel();
            })
            { IsBackground = true };
            worker.Start();
            started.Wait();
        }

        public void Dispose()
        {
            completed.Set();
            worker.Join();
            started.Dispose();
            completed.Dispose();
        }
    }

    private sealed class CancelAtLinkLookahead : InlineParser
    {
        private readonly CancellationTokenSource cancellation;

        internal CancelAtLinkLookahead(CancellationTokenSource cancellation)
        { this.cancellation = cancellation; OpeningCharacters = new[] { ']' }; }

        internal bool Reached { get; private set; }

        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            if (slice.PeekCharExtra(1) == '(')
            {
                Reached = true;
                cancellation.Cancel();
            }
            return false;
        }
    }

    private static void Reject(string text, DocumentFormat format, string code)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(text)); using var output = new MemoryStream(); output.WriteByte(42);
        var error = Assert.Throws<InvalidDataException>(() => new WordConverter().Convert(input, output, format, DocumentFormat.Docx,
            new ConversionOptions(FidelityMode.BestEffort), TestContext.Current.CancellationToken));
        Assert.StartsWith(code + ":", error.Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
    }
}
