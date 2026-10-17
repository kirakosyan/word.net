using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace WordNet.Qualification;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };
    private static readonly string[] SuccessfulProbe = { "probe", "ok" };
    private static readonly string[] FailedProbe = { "probe", "fail" };
    private static readonly string[] WaitingProbe = { "probe", "wait" };

    private static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "worker") return Worker(args);
        if (args.Length == 1 && args[0] == "self-test")
        {
            for (int i = 0; i < 32; i++)
                if ((await RunWorker(SuccessfulProbe, 10, 512)).Status != "completed") return 1;
            RunResult failed = await RunWorker(FailedProbe, 10, 512);
            RunResult timedOut = await RunWorker(WaitingProbe, 1, 512);
            RunResult memoryLimited = await RunWorker(WaitingProbe, 10, 1);
            if (failed.Status != "worker-failed" || failed.ExitCode != 7 || timedOut.Status != "timeout" || memoryLimited.Status != "memory-limit") return 1;
            Console.WriteLine("Supervisor: 32 short-lived workers, nonzero exit, external deadline and memory-guard checks passed.");
            return 0;
        }
        if (args.Length != 4 || args[0] is not ("fuzz" or "benchmark" or "ocr"))
        {
            Console.Error.WriteLine("Usage: wordnet.Qualification fuzz|benchmark|ocr <fixture-directory> <output-directory> <cases-per-format|iterations>");
            return 2;
        }
        string fixtures = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        int count = int.Parse(args[3], CultureInfo.InvariantCulture);
        if (count is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(args), "Count must be 1..10000.");
        Directory.CreateDirectory(output);
        var seeds = new List<(string File, DocumentFormat Format)>
        {
            ("plain.doc", DocumentFormat.Doc), ("digital.pdf", DocumentFormat.Pdf), ("plain.epub", DocumentFormat.Epub),
            ("plain.mobi", DocumentFormat.Mobi), ("plain.azw3", DocumentFormat.Azw3), ("sample.md", DocumentFormat.Markdown)
        };
        string docx = Path.Combine(output, args[0] + "-seed.docx"), text = Path.Combine(output, args[0] + "-seed.txt");
        File.WriteAllText(text, "Qualification heading\nEnglish printed text 12345.", new UTF8Encoding(false));
        CreateDocx(docx);
        seeds.Add((docx, DocumentFormat.Docx)); seeds.Add((text, DocumentFormat.Text));
        var results = new List<RunResult>();
        if (args[0] == "fuzz")
        {
            foreach (var seed in seeds)
            {
                string file = Path.GetFullPath(Path.Combine(fixtures, seed.File));
                for (int index = 0; index < count; index++)
                {
                    RunResult result = await RunWorker(new[] { "fuzz", file, seed.Format.ToString(), index.ToString(CultureInfo.InvariantCulture) }, 30, 512);
                    results.Add(result);
                    if (result.Status != "completed") Console.WriteLine($"FAIL {seed.Format} {index}: {result.Status}");
                }
                Console.WriteLine($"{seed.Format}: {count} cases finished.");
            }
        }
        else if (args[0] == "ocr")
        {
            for (int iteration = 0; iteration < count; iteration++)
                foreach (string file in Directory.GetFiles(fixtures, "ocr-*.pdf").OrderBy(f => f, StringComparer.Ordinal))
                    foreach (DocumentFormat target in new[] { DocumentFormat.Doc, DocumentFormat.Docx })
                        results.Add(await RunWorker(new[] { "ocr", file, target.ToString(), iteration.ToString(CultureInfo.InvariantCulture) }, 60, 512));
        }
        else
        {
            results.Add(await RunWorker(new[] { "benchmark", fixtures, "all", count.ToString(CultureInfo.InvariantCulture) }, 300, 768));
        }
        bool passed = results.All(r => r.Status == "completed");
        var report = new
        {
            schema = 1,
            campaign = args[0],
            utc = DateTimeOffset.UtcNow,
            source = Environment.GetEnvironmentVariable("GITHUB_SHA"),
            runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            librarySha256 = Hash(File.ReadAllBytes(typeof(WordConverter).Assembly.Location)),
            seeds = seeds.Select(s => new { format = s.Format.ToString(), path = s.File, sha256 = Hash(File.ReadAllBytes(Path.Combine(fixtures, s.File))) }),
            count,
            passed,
            results
        };
        string reportPath = Path.Combine(output, args[0] + ".json");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, Json));
        Console.WriteLine(reportPath);
        return passed ? 0 : 1;
    }

    private static int Worker(string[] args)
    {
        try
        {
            if (args[1] == "probe")
            {
                if (args[2] == "wait") Thread.Sleep(5000);
                return args[2] == "fail" ? 7 : 0;
            }
            if (args[1] == "benchmark") { Benchmark(args[2], int.Parse(args[4], CultureInfo.InvariantCulture)); return 0; }
            if (args[1] == "ocr")
            {
                string data = Environment.GetEnvironmentVariable("WORDNET_TESSDATA") ?? throw new InvalidOperationException("Provision WORDNET_TESSDATA.");
                string native = Environment.GetEnvironmentVariable("WORDNET_TESSERACT_LIBRARY") ?? throw new InvalidOperationException("Provision WORDNET_TESSERACT_LIBRARY.");
                using var scan = File.OpenRead(args[2]); using var word = new MemoryStream();
                var ocr = new EnglishOcrOptions(data, native, !args[2].EndsWith("ocr-columns.pdf", StringComparison.Ordinal),
                    resolutionDpi: args[2].EndsWith("ocr-phototest.pdf", StringComparison.Ordinal) ? 300 : 200);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                ConversionResult result = new WordConverter().Convert(scan, word, DocumentFormat.Pdf, Enum.Parse<DocumentFormat>(args[3]),
                    new ConversionOptions(FidelityMode.BestEffort, englishOcr: ocr), deadline.Token);
                if (!result.Diagnostics.Any(d => d.Code == "Ocr.English")) throw new InvalidOperationException("OCR was not executed.");
                Console.WriteLine(JsonSerializer.Serialize(new { outcome = "recognized", result.BytesWritten, result.Diagnostics }));
                return 0;
            }
            byte[] bytes = Mutate(File.ReadAllBytes(args[2]), int.Parse(args[4], CultureInfo.InvariantCulture));
            DocumentFormat format = Enum.Parse<DocumentFormat>(args[3]);
            using var input = new MemoryStream(bytes); using var output = new MemoryStream();
            output.WriteByte(42);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                new WordConverter().Convert(input, output, format, format == DocumentFormat.Docx ? DocumentFormat.Doc : DocumentFormat.Docx,
                    new ConversionOptions(FidelityMode.BestEffort, maximumInputBytes: 4 * 1024 * 1024, maximumOutputBytes: 8 * 1024 * 1024,
                        maximumExpandedBytes: 16 * 1024 * 1024, maximumCharacters: 250000, maximumPages: 100), cancellation.Token);
                if (output.Length <= 1 || output.GetBuffer()[0] != 42) throw new InvalidOperationException("Destination append contract failed.");
                Console.WriteLine(JsonSerializer.Serialize(new { outcome = "accepted", inputSha256 = Hash(bytes), outputBytes = output.Length - 1 }));
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or OperationCanceledException)
            {
                if (output.Length != 1 || output.GetBuffer()[0] != 42) throw new InvalidOperationException("Rejected input changed the destination.", error);
                Console.WriteLine(JsonSerializer.Serialize(new { outcome = error is OperationCanceledException ? "cancelled" : "rejected", inputSha256 = Hash(bytes), exception = error.GetType().Name, error.Message }));
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static async Task<RunResult> RunWorker(string[] args, int seconds, int memoryMiB)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location); start.ArgumentList.Add("worker");
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Worker could not start.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
        var clock = Stopwatch.StartNew();
        long peak = 0;
        string status = "completed";
        while (!process.HasExited)
        {
            // A short-lived worker can exit after HasExited but before the OS memory query.
            // Its final exit code/output still determines success; an ordinary exit is not
            // a supervisor failure. Keep unrelated process-query errors visible.
            try { process.Refresh(); peak = Math.Max(peak, process.WorkingSet64); }
            catch (InvalidOperationException) when (process.HasExited) { break; }
            if (clock.Elapsed.TotalSeconds > seconds || peak > memoryMiB * 1048576L)
            {
                status = clock.Elapsed.TotalSeconds > seconds ? "timeout" : "memory-limit";
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (process.HasExited) { }
                break;
            }
            await Task.Delay(50);
        }
        await process.WaitForExitAsync(); clock.Stop();
        if (process.ExitCode != 0 && status == "completed") status = "worker-failed";
        return new RunResult(args, status, process.ExitCode, clock.Elapsed.TotalMilliseconds, peak, await stdout, await stderr);
    }

    private static byte[] Mutate(byte[] bytes, int index)
    {
        if (index == 0) return bytes;
        var random = new Random(73013 + index);
        switch (index % 7)
        {
            case 1: return bytes.Take(random.Next(bytes.Length + 1)).ToArray();
            case 2:
                for (int i = 0; i < Math.Min(16, bytes.Length); i++) bytes[random.Next(bytes.Length)] ^= (byte)(1 << random.Next(8));
                break;
            case 3:
                if (bytes.Length >= 4) Array.Fill(bytes, (byte)255, random.Next(bytes.Length - 3), 4);
                break;
            case 4: Array.Clear(bytes, 0, Math.Min(bytes.Length, 64)); break;
            case 5: return bytes.Skip(random.Next(bytes.Length + 1)).ToArray();
            case 6:
                byte[] appended = new byte[bytes.Length + 4096]; bytes.CopyTo(appended, 0);
                random.NextBytes(appended.AsSpan(bytes.Length)); return appended;
            default:
                if (bytes.Length > 0) Array.Fill(bytes, (byte)0, random.Next(bytes.Length), 1);
                break;
        }
        return bytes;
    }

    private static void Benchmark(string fixtures, int iterations)
    {
        var converter = new WordConverter();
        var inputs = new Dictionary<DocumentFormat, byte[]>
        {
            [DocumentFormat.Doc] = File.ReadAllBytes(Path.Combine(fixtures, "plain.doc")),
            [DocumentFormat.Pdf] = File.ReadAllBytes(Path.Combine(fixtures, "digital.pdf")),
            [DocumentFormat.Epub] = File.ReadAllBytes(Path.Combine(fixtures, "plain.epub")),
            [DocumentFormat.Mobi] = File.ReadAllBytes(Path.Combine(fixtures, "plain.mobi")),
            [DocumentFormat.Azw3] = File.ReadAllBytes(Path.Combine(fixtures, "plain.azw3")),
            [DocumentFormat.Text] = Encoding.UTF8.GetBytes("Wordnet conversion test\nEnglish printed text 12345."),
            [DocumentFormat.Markdown] = Encoding.UTF8.GetBytes("# Wordnet conversion test\n\nEnglish printed text 12345.")
        };
        inputs[DocumentFormat.Docx] = Convert(inputs[DocumentFormat.Text], DocumentFormat.Text, DocumentFormat.Docx);
        inputs[DocumentFormat.Html] = Encoding.UTF8.GetBytes("<p>Wordnet conversion test</p><p>English printed text 12345.</p>");
        inputs[DocumentFormat.Rtf] = Encoding.ASCII.GetBytes(@"{\rtf1\ansi Wordnet conversion test\par English printed text 12345.\par}");
        var pairs = (from source in inputs.Keys from target in Enum.GetValues<DocumentFormat>() where converter.CanConvert(source, target) select (Source: source, Target: target)).ToArray();
        if (pairs.Length != 36 || pairs.Count(p => p.Source is not DocumentFormat.Html and not DocumentFormat.Rtf && p.Target is not DocumentFormat.Html and not DocumentFormat.Rtf) != 28
            || pairs.Count(p => p.Source is DocumentFormat.Html or DocumentFormat.Rtf) != 4
            || pairs.Count(p => p.Target is DocumentFormat.Html or DocumentFormat.Rtf) != 4)
            throw new InvalidOperationException("Expected the original 28 conversion directions and eight HTML/RTF exchanges with Word.");
        var measurements = new System.Collections.Concurrent.ConcurrentBag<Measurement>();
        Parallel.For(0, iterations, new ParallelOptions { MaxDegreeOfParallelism = 4 }, iteration =>
        {
            foreach (var pair in pairs)
            {
                var watch = Stopwatch.StartNew(); byte[] result = Convert(inputs[pair.Source], pair.Source, pair.Target); watch.Stop();
                measurements.Add(new Measurement(pair.Source.ToString(), pair.Target.ToString(), iteration, watch.Elapsed.TotalMilliseconds, result.Length));
                // Check content on each iteration, not just successful allocation or file signatures.
                if (pair.Target == DocumentFormat.Html)
                {
                    using var html = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(Encoding.UTF8.GetString(result));
                    if (html.Body == null || !html.Body.TextContent.Contains("English printed text 12345.", StringComparison.Ordinal))
                        throw new InvalidOperationException("Concurrent HTML conversion lost canonical content.");
                    continue;
                }
                if (pair.Target == DocumentFormat.Xps)
                {
                    using var zip = new ZipArchive(new MemoryStream(result));
                    string glyphs = string.Concat(zip.Entries.Where(e => e.FullName.EndsWith(".fpage", StringComparison.Ordinal)).SelectMany(entry =>
                    {
                        using Stream stream = entry.Open();
                        return System.Xml.Linq.XDocument.Load(stream).Descendants().Attributes("UnicodeString").Select(a => a.Value).ToArray();
                    }));
                    if (!glyphs.Contains("English printed text 12345.", StringComparison.Ordinal)) throw new InvalidOperationException("Concurrent XPS conversion lost glyph content.");
                    continue;
                }
                byte[] word = pair.Target is DocumentFormat.Doc or DocumentFormat.Docx ? result : Convert(result, pair.Target, DocumentFormat.Docx);
                string text = Encoding.UTF8.GetString(Convert(word, pair.Target == DocumentFormat.Doc ? DocumentFormat.Doc : DocumentFormat.Docx, DocumentFormat.Text));
                if (!text.Contains("English printed text 12345.", StringComparison.Ordinal)) throw new InvalidOperationException("Concurrent conversion lost canonical content.");
            }
        });
        byte[] large = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("A bounded document with fifty thousand characters.\n", 1000)));
        var largeClock = Stopwatch.StartNew();
        byte[] largeDoc = Convert(large, DocumentFormat.Text, DocumentFormat.Docx);
        byte[] largeText = Convert(largeDoc, DocumentFormat.Docx, DocumentFormat.Text); largeClock.Stop();
        if (Encoding.UTF8.GetString(largeText).TrimEnd() != Encoding.UTF8.GetString(large).TrimEnd()) throw new InvalidOperationException("Large-document round trip lost text.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            directions = pairs.Length,
            iterations,
            concurrency = 4,
            largeRoundTripMilliseconds = largeClock.Elapsed.TotalMilliseconds,
            measurements = measurements.OrderBy(m => m.Source, StringComparer.Ordinal).ThenBy(m => m.Target, StringComparer.Ordinal).ThenBy(m => m.Iteration)
        }));

        byte[] Convert(byte[] source, DocumentFormat from, DocumentFormat to)
        {
            using var input = new MemoryStream(source); using var output = new MemoryStream();
            converter.Convert(input, output, from, to, new ConversionOptions(FidelityMode.BestEffort));
            if (output.Length == 0) throw new InvalidOperationException("Conversion produced no output.");
            return output.ToArray();
        }
    }

    private static void CreateDocx(string path)
    {
        using var file = File.Create(path); using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        Add("[Content_Types].xml", "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/><Override PartName='/word/document.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/></Types>");
        Add("_rels/.rels", "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='r1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='word/document.xml'/></Relationships>");
        Add("word/document.xml", "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body><w:p><w:r><w:t>Qualification heading</w:t></w:r></w:p><w:p><w:r><w:t>English printed text 12345.</w:t></w:r></w:p></w:body></w:document>");
        void Add(string name, string value)
        {
            ZipArchiveEntry entry = zip.CreateEntry(name); entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)); writer.Write(value);
        }
    }

    private static string Hash(byte[] bytes) => System.Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private sealed record RunResult(string[] Arguments, string Status, int ExitCode, double ElapsedMilliseconds, long PeakBytes, string Output, string Error);
    private sealed record Measurement(string Source, string Target, int Iteration, double Milliseconds, long OutputBytes);
}
