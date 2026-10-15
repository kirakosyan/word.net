using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using UglyToad.PdfPig;
using WordNet;

namespace WordNet.Benchmarks;

// Uses only the 1.0 public conversion API, so the same harness can build against
// both revisions. No benchmark/provider type is added to the library API.
internal static class PerformanceMeasurement
{
    private static readonly JsonSerializerOptions ReportJson = new JsonSerializerOptions { WriteIndented = true };
    internal static ConversionOptions Options()
    {
        var options = new ConversionOptions(FidelityMode.BestEffort);
        options.GetType().GetProperty("Clock")?.SetValue(options,
            (Func<DateTimeOffset>)(() => new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero)));
        return options;
    }

    internal static int Run(string[] args)
    {
        try { Measure(args); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void Measure(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("Usage: --measure <ten-page image DOCX> <report.json> <source revision>");
        using Process process = Process.GetCurrentProcess();
        long selected;
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            long allowed = process.ProcessorAffinity.ToInt64(); selected = unchecked(allowed & -allowed);
            if (selected == 0) throw new InvalidOperationException("No allowed processor in the affinity mask.");
            process.ProcessorAffinity = new IntPtr(selected);
            if (process.ProcessorAffinity.ToInt64() != selected) throw new InvalidOperationException("Single-core affinity was not applied.");
        }
        else throw new PlatformNotSupportedException("Affinity qualification requires Windows or Linux.");
        byte[] input = File.ReadAllBytes(args[0]);
        if (input.Length > 16 * 1024 * 1024) throw new InvalidDataException("Benchmark input exceeds 16 MiB.");
        using var zip = new ZipArchive(new MemoryStream(input, writable: false));
        ZipArchiveEntry entry = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("Missing benchmark document.");
        if (entry.Length > 16 * 1024 * 1024) throw new InvalidDataException("Benchmark XML exceeds 16 MiB.");
        using Stream document = entry.Open();
        using XmlReader xml = XmlReader.Create(document, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 });
        XDocument source = XDocument.Load(xml);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main", a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        int tables = source.Descendants(w + "tbl").Count(), images = source.Descendants(a + "blip").Count();
        if (tables < 2 || images < 2) throw new InvalidDataException("The acceptance fixture must include tables and at least two images.");
        var converter = new WordConverter(); ConversionOptions options = Options();
        for (int i = 0; i < 20; i++) Validate(Export());
        var times = new List<double>(); var upperBounds = new List<long>(); var allocated = new List<long>();
        long peak = 0;
        using var stop = new ManualResetEventSlim(false);
        var sampler = new Thread(() =>
        {
            while (!stop.Wait(1)) RecordPeak(GC.GetTotalMemory(false));
        })
        { IsBackground = true, Name = "wordnet performance sampler" };
        sampler.Start();
        try
        {
            for (int i = 0; i < 31; i++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long existingHeap = Math.Max(GC.GetTotalMemory(false), GC.GetGCMemoryInfo().HeapSizeBytes);
                long before = GC.GetTotalAllocatedBytes(precise: true);
                var watch = Stopwatch.StartNew(); byte[] output = Export(); watch.Stop();
                long delta = GC.GetTotalAllocatedBytes(precise: true) - before;
                RecordPeak(GC.GetTotalMemory(false));
                times.Add(watch.Elapsed.TotalMilliseconds); allocated.Add(delta); upperBounds.Add(existingHeap + delta);
                Validate(output); GC.KeepAlive(output);
            }
        }
        finally { stop.Set(); sampler.Join(); }
        times.Sort();
        var report = new
        {
            Schema = 1,
            SourceRevision = args[2],
            AssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(WordConverter).Assembly.Location))).ToLowerInvariant(),
            Runtime = RuntimeInformation.FrameworkDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Machine = Environment.MachineName,
            OperatingSystem = RuntimeInformation.OSDescription,
            Processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? RuntimeInformation.OSArchitecture.ToString(),
            Affinity = unchecked((ulong)selected).ToString("X16", CultureInfo.InvariantCulture),
            ServerGc = GCSettings.IsServerGC,
            FixtureSha256 = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant(),
            DocumentPages = 10,
            DocumentTables = tables,
            DocumentImages = images,
            WarmupConversions = 20,
            MeasuredConversions = times.Count,
            Milliseconds = times,
            MedianMilliseconds = times[times.Count / 2],
            PeakSampledManagedBytes = peak,
            MaximumManagedUpperBoundBytes = upperBounds.Max(),
            MaximumAllocatedBytes = allocated.Max(),
        };
        string path = Path.GetFullPath(args[1]); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(report, ReportJson));
        Console.WriteLine("Single-core PDF median: " + report.MedianMilliseconds.ToString("0.000", CultureInfo.InvariantCulture)
            + " ms; managed upper bound: " + report.MaximumManagedUpperBoundBytes + "; report: " + path);

        byte[] Export()
        {
            using var from = new MemoryStream(input, writable: false); using var to = new MemoryStream();
            converter.Convert(from, to, DocumentFormat.Docx, DocumentFormat.Pdf, options);
            return to.ToArray();
        }
        void RecordPeak(long value)
        {
            long previous;
            do { previous = Interlocked.Read(ref peak); if (value <= previous) return; }
            while (Interlocked.CompareExchange(ref peak, value, previous) != previous);
        }
        void Validate(byte[] bytes)
        {
            using PdfDocument pdf = PdfDocument.Open(bytes);
            if (pdf.NumberOfPages != 10 || pdf.GetPages().Sum(p => p.NumberOfImages) != images)
                throw new InvalidDataException("Benchmark output changed pagination or lost an image.");
            if (pdf.GetPages().Count(p => p.Text.Contains("Invoice details", StringComparison.Ordinal)) != 10)
                throw new InvalidDataException("Benchmark output lost table text.");
        }
    }
}
