using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WordNet;

internal static class Program
{
    private static readonly DateTimeOffset Clock = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private static ConversionOptions Options() => new ConversionOptions(FidelityMode.Lenient) { Clock = () => Clock };

    private static int Main(string[] args)
    {
        try { Run(args); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void Run(string[] args)
    {
        if (args.Length == 3 && args[0] == "pdfa")
        {
            Directory.CreateDirectory(args[2]);
            foreach (string path in Directory.GetFiles(args[1], "*.docx").OrderBy(p => p, StringComparer.Ordinal))
            {
                if (Path.GetFileNameWithoutExtension(path) is "malformed-xml" or "expanded-limit") continue;
                ConversionOptions options = Options(); options.Pdf.Conformance = PdfConformance.PdfA2b; options.Pdf.OutlineHeadings = true;
                File.WriteAllBytes(Path.Combine(args[2], Path.GetFileNameWithoutExtension(path) + ".pdf"), new WordConverter().ConvertToPdf(File.ReadAllBytes(path), options));
            }
            Console.WriteLine("Generated archival corpus for independent validation.");
            return;
        }
        if (args.Length < 1) throw new ArgumentException("Usage: probe <Enterprise fixture directory> [stress iterations per worker]");
        string root = Path.GetFullPath(args[0]); var converter = new WordConverter();
        string[] names = { "language-no", "language-fr", "nested-invoice", "letter-stories", "logo-png", "logo-jpg", "logo-gif", "logo-bmp" };
        byte[][] sources = names.Select(name => File.ReadAllBytes(Path.Combine(root, name + ".docx"))).ToArray();
        byte[][] pdfs = sources.Select(bytes => converter.ConvertToPdf(bytes, Options())).ToArray();
        byte[][] htmls = sources.Select(bytes => converter.ConvertToHtml(bytes, Options())).ToArray();
        foreach (string name in names)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(root, name + ".docx"));
            using var input = new MemoryStream(bytes); using var output = new MemoryStream();
            ConversionResult result = converter.Convert(input, output, DocumentFormat.Docx, DocumentFormat.Pdf, Options());
            if (result.BytesWritten < 100 || result.Metrics.PageCount < 1) throw new InvalidOperationException("Conversion produced no usable PDF.");
            if (converter.ConvertToHtml(bytes, Options()).Length < 100) throw new InvalidOperationException("Conversion produced no HTML.");
        }
        if (args.Length > 1)
        {
            int iterations = int.Parse(args[1], CultureInfo.InvariantCulture);
            var elapsed = Stopwatch.StartNew();
            int completed = 0;
            long peakManaged = 0;
            var tasks = Enumerable.Range(0, 64).Select(worker => Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    int index = (worker + i) % sources.Length;
                    if (!pdfs[index].SequenceEqual(converter.ConvertToPdf(sources[index], Options())) || !htmls[index].SequenceEqual(converter.ConvertToHtml(sources[index], Options())))
                        throw new InvalidOperationException("Concurrent output differs from the fixed-clock reference.");
                    long memory = GC.GetTotalMemory(false), previous;
                    do { previous = Interlocked.Read(ref peakManaged); if (memory <= previous) break; }
                    while (Interlocked.CompareExchange(ref peakManaged, memory, previous) != previous);
                    if (Interlocked.Increment(ref completed) % 1600 == 0) Console.WriteLine("Completed pairs: " + completed + "; sampled managed bytes: " + memory);
                }
            })).ToArray();
            Task.WaitAll(tasks);
            Console.WriteLine("Concurrency: 64 workers x " + iterations + " mixed iterations, PDF and HTML identical. Elapsed: " + elapsed.Elapsed);
            Console.WriteLine("Peak sampled managed bytes: " + peakManaged);
        }
        string[] forbidden = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name ?? "")
            .Where(n => Contains(n, "Skia") || Contains(n, "HarfBuzz") || n == "wordnet.Ocr").ToArray();
        if (forbidden.Length > 0) throw new InvalidOperationException("Managed export loaded native providers: " + string.Join(", ", forbidden));
        using Process process = Process.GetCurrentProcess();
        Console.WriteLine("Peak working-set bytes: " + process.PeakWorkingSet64);
        foreach (ProcessModule module in process.Modules)
            if (Contains(module.ModuleName, "skia") || Contains(module.ModuleName, "tesseract"))
                throw new InvalidOperationException("Managed export loaded a native graphics/OCR module.");
        Console.WriteLine("Runtime: " + Environment.Version + "; core: " + typeof(WordConverter).Assembly.FullName);
        Console.WriteLine("Managed-only DOCX PDF/HTML conversions passed; no native providers loaded.");
    }

    private static bool Contains(string text, string value)
    {
#if NET8_0_OR_GREATER
        return text.Contains(value, StringComparison.OrdinalIgnoreCase);
#else
        return text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
#endif
    }
}
