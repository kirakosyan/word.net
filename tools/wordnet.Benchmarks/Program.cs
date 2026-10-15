using System;
using System.IO;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using WordNet;

namespace WordNet.Benchmarks;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--measure") return PerformanceMeasurement.Run(args.Skip(1).ToArray());
        var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args).ToArray();
        return summaries.Length > 0 && summaries.All(s => s.Reports.Length > 0 && s.Reports.All(r => r.Success)) ? 0 : 1;
    }
}

/// <summary>Warm managed export; run from the repository root.</summary>
[MemoryDiagnoser]
public class EnterpriseExport
{
    private readonly WordConverter converter = new WordConverter();
    private readonly ConversionOptions options = PerformanceMeasurement.Options();
    private byte[] input = null!;

    [GlobalSetup]
    public void Setup()
    {
        string fixture = Environment.GetEnvironmentVariable("WORDNET_BENCHMARK_INPUT")
            ?? throw new InvalidOperationException("Set WORDNET_BENCHMARK_INPUT to the absolute benchmark-10-pages.docx path.");
        input = File.ReadAllBytes(fixture);
    }

    [Benchmark]
    public byte[] Pdf() => Export(DocumentFormat.Pdf);

    [Benchmark]
    public byte[] Html() => Export(Enum.Parse<DocumentFormat>("Html"));

    private byte[] Export(DocumentFormat format)
    {
        using var source = new MemoryStream(input, writable: false); using var output = new MemoryStream();
        converter.Convert(source, output, DocumentFormat.Docx, format, options);
        return output.ToArray();
    }
}
