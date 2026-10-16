using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using UglyToad.PdfPig;
using WordNet;

namespace WordNet.Benchmarks;

// Invoked only by eng/run-comparison.py, once per input/provider in a fresh process.
internal static class ComparisonMeasurement
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };

    internal static int Run(string[] args)
    {
        try
        {
            if (args.Length == 3 && args[0] == "snapshot")
            {
                Save(args[2], Snapshot(File.ReadAllBytes(args[1])));
                return 0;
            }
            if (args.Length != 7) throw new ArgumentException("Usage: --comparison <wordnet|aspose> <input.docx> <output.pdf> <report.json> <font-directory> <warm-count> <aspose-assembly-or-dash>");
            string engine = args[0];
            int iterations = int.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture);
            if (iterations is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(args));
            byte[] input = File.ReadAllBytes(args[1]);
            if (input.Length > 16 * 1024 * 1024) throw new InvalidDataException("Comparison input exceeds 16 MiB.");
            var options = PerformanceMeasurement.Options();
            foreach (FontSource font in FontSource.FromDirectory(args[4])) options.Fonts.Sources.Add(font);
            var converter = new WordConverter();
            Assembly? aspose = null;
            if (engine == "aspose")
            {
                string assemblyPath = Path.GetFullPath(args[6]);
                AssemblyLoadContext.Default.Resolving += (_, name) =>
                {
                    string dependency = Path.Combine(Path.GetDirectoryName(assemblyPath)!, name.Name + ".dll");
                    return File.Exists(dependency) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(dependency) : null;
                };
                aspose = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
                string? licensePath = Environment.GetEnvironmentVariable("WORDNET_ASPOSE_LICENSE");
                if (!string.IsNullOrEmpty(licensePath))
                {
                    dynamic license = Activator.CreateInstance(aspose.GetType("Aspose.Words.License", true)!)!;
                    try { license.SetLicense(licensePath); }
                    catch (Exception) { throw new InvalidOperationException("Aspose license could not be loaded."); }
                }
            }
            else if (engine != "wordnet") throw new ArgumentException("Unknown comparison engine.");
            using Process process = Process.GetCurrentProcess();
            var samples = new List<object>();
            byte[]? output = null;
            IReadOnlyList<ConversionDiagnostic>? diagnostics = null;
            for (int i = 0; i <= iterations; i++)
            {
                long before = GC.GetTotalAllocatedBytes(true);
                var clock = Stopwatch.StartNew();
                using var source = new MemoryStream(input, false);
                using var destination = new MemoryStream();
                if (aspose == null)
                    diagnostics = converter.Convert(source, destination, DocumentFormat.Docx, DocumentFormat.Pdf, options).Diagnostics;
                else
                {
                    dynamic settings = Activator.CreateInstance(aspose.GetType("Aspose.Words.Fonts.FontSettings", true)!)!;
                    settings.SetFontsFolder(Path.GetFullPath(args[4]), false);
                    dynamic document = Activator.CreateInstance(aspose.GetType("Aspose.Words.Document", true)!, source)!;
                    document.FontSettings = settings;
                    Type format = aspose.GetType("Aspose.Words.SaveFormat", true)!;
                    document.Save(destination, (dynamic)Enum.Parse(format, "Pdf"));
                }
                clock.Stop();
                long allocated = GC.GetTotalAllocatedBytes(true) - before;
                process.Refresh();
                samples.Add(new
                {
                    kind = i == 0 ? "cold-conversion" : "warm-conversion",
                    milliseconds = clock.Elapsed.TotalMilliseconds,
                    allocatedBytes = allocated,
                    rssBytes = process.WorkingSet64,
                    processPeakRssBytes = process.PeakWorkingSet64,
                    outputBytes = destination.Length
                });
                output = destination.ToArray();
            }
            File.WriteAllBytes(args[2], output!);
            // Editable export is checked independently by the orchestrator's DOC reader.
            using var editable = new MemoryStream();
            using (var source = new MemoryStream(input, false))
            {
                if (aspose == null) converter.Convert(source, editable, DocumentFormat.Docx, DocumentFormat.Doc, options);
                else
                {
                    dynamic document = Activator.CreateInstance(aspose.GetType("Aspose.Words.Document", true)!, source)!;
                    Type format = aspose.GetType("Aspose.Words.SaveFormat", true)!;
                    document.Save(editable, (dynamic)Enum.Parse(format, "Doc"));
                }
            }
            File.WriteAllBytes(Path.ChangeExtension(args[2], ".doc"), editable.ToArray());
            object snapshot = Snapshot(output!);
            var report = new
            {
                schemaVersion = 1,
                engine,
                artifactMode = aspose == null || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WORDNET_ASPOSE_LICENSE")) ? "licensed" : "evaluation",
                inputSha256 = Hash(input),
                outputSha256 = Hash(output!),
                editableSha256 = Hash(editable.ToArray()),
                editableBytes = editable.Length,
                runtime = RuntimeInformation.FrameworkDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                os = RuntimeInformation.OSDescription,
                machine = Environment.MachineName,
                logicalProcessors = Environment.ProcessorCount,
                processor = Cpu(),
                affinity = OperatingSystem.IsWindows() || OperatingSystem.IsLinux() ? process.ProcessorAffinity.ToInt64().ToString("X", System.Globalization.CultureInfo.InvariantCulture) : "unavailable",
                assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                    .OrderBy(a => a.GetName().Name, StringComparer.Ordinal).Select(a => new { name = a.GetName().Name, version = a.GetName().Version?.ToString(), sha256 = Hash(File.ReadAllBytes(a.Location)) }).ToArray(),
                fonts = Directory.GetFiles(args[4]).OrderBy(p => p, StringComparer.Ordinal).Select(p => new { name = Path.GetFileName(p), sha256 = Hash(File.ReadAllBytes(p)) }).ToArray(),
                samples,
                snapshot,
                diagnostics = diagnostics?.Select(d => new
                {
                    code = d.Code,
                    count = d.Count,
                    severity = d.Severity.ToString(),
                    occurrences = d.Occurrences.Select(o => new { part = o.Location.Part, path = o.Location.Path, phase = o.Phase.ToString() }).ToArray()
                }).ToArray(),
            };
            Save(args[3], report);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetType().Name + ": " + error.Message);
            return 1;
        }
    }

    private static string Cpu() => OperatingSystem.IsLinux() && File.Exists("/proc/cpuinfo")
        ? File.ReadLines("/proc/cpuinfo").FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal)) ?? "unavailable"
        : Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "unavailable";

    private static object Snapshot(byte[] bytes)
    {
        using PdfDocument pdf = PdfDocument.Open(bytes);
        return new
        {
            pageCount = pdf.NumberOfPages,
            pages = pdf.GetPages().Select(p => new
            {
                width = p.Width,
                height = p.Height,
                text = p.Text,
                glyphs = p.Letters.Select(l => new { text = l.Value, x = l.StartBaseLine.X, y = l.StartBaseLine.Y }).ToArray()
            }).ToArray(),
        };
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void Save(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
}
