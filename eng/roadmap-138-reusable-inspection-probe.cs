// MIT probe for independent verification of the fixed in-process inspection profile.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using WordNet;

string fixture = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
if (File.Exists(output)) throw new IOException("Choose a new output path.");
var schema = new TemplateDataSchema(new TemplateSchemaPath("Customer", TemplatePathType.Mapping),
    new TemplateSchemaPath("Customer.Name", TemplatePathType.Scalar), new TemplateSchemaPath("Customer.Code", TemplatePathType.Scalar));
DateTimeOffset clock = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
var converter = new WordConverter(); ReusableTemplateInspection plan;
using (var source = File.OpenRead(fixture)) plan = converter.CompileReusableTemplateInspection(source, schema, clock, FidelityMode.Lenient);
byte[] input = File.ReadAllBytes(fixture);
using var unchanged = new MemoryStream(input); TemplateInspection reused = plan.Reuse(unchanged, schema);
byte[] changed = (byte[])input.Clone(); changed[changed.Length - 1] ^= 1;
using var changedSource = new MemoryStream(changed); string staleInput = "none";
try { _ = plan.Reuse(changedSource, schema); } catch (InvalidDataException error) { staleInput = error.Message.Split(':')[0]; }
var changedSchema = new TemplateDataSchema(new TemplateSchemaPath("Customer", TemplatePathType.Mapping),
    new TemplateSchemaPath("Customer.Name", TemplatePathType.Scalar, required: false), new TemplateSchemaPath("Customer.Code", TemplatePathType.Scalar));
using var schemaSource = new MemoryStream(input); string staleSchema = "none";
try { _ = plan.Reuse(schemaSource, changedSchema); } catch (InvalidDataException error) { staleSchema = error.Message.Split(':')[0]; }
using var tooLarge = new MemoryStream(new byte[16 * 1024 * 1024 + 1]); string limit = "none";
try { _ = plan.Reuse(tooLarge, schema); } catch (InvalidDataException error) { limit = error.Message.Split(':')[0]; }
using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); using var cancelSource = new MemoryStream(input); bool cancellation = false;
try { _ = plan.Reuse(cancelSource, schema, cancelled.Token); } catch (OperationCanceledException) { cancellation = true; }
ReusableTemplateInspection shifted;
using (var source = new MemoryStream(input)) shifted = converter.CompileReusableTemplateInspection(source, schema, clock.AddMinutes(1), FidelityMode.Lenient);
Assembly core = typeof(WordConverter).Assembly;
Assembly fonts = AppDomain.CurrentDomain.GetAssemblies().Single(item => item.GetName().Name == "wordnet.Fonts");
var resources = new[] { core, fonts }.Select(assembly => new
{
    assembly = assembly.FullName, module = assembly.ManifestModule.ModuleVersionId.ToString("D"),
    fonts = assembly.GetManifestResourceNames().Where(name => name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)).OrderBy(name => name, StringComparer.Ordinal).Select(name =>
    {
        using Stream stream = assembly.GetManifestResourceStream(name)!;
        return new { name, sha256 = Convert.ToHexStringLower(SHA256.HashData(stream)) };
    }).ToArray()
}).ToArray();
var outputData = new
{
    inputSha256 = Convert.ToHexStringLower(SHA256.HashData(input)),
    coreSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(core.Location))),
    plan, shiftedOptionsSha256 = shifted.OptionsSha256, sameInspection = ReferenceEquals(plan.Inspection, reused),
    staleInput, staleSchema, schemaPosition = schemaSource.Position, limit, cancellation, cancelPosition = cancelSource.Position,
    streamsRemainOpen = unchanged.CanRead && changedSource.CanRead && schemaSource.CanRead && tooLarge.CanRead && cancelSource.CanRead,
    environment = new { core = core.FullName, module = core.ManifestModule.ModuleVersionId.ToString("D"), runtime = Environment.Version.ToString(),
        os = Environment.OSVersion.VersionString, pointerSize = IntPtr.Size, os64 = Environment.Is64BitOperatingSystem, resources }
};
File.WriteAllText(output, JsonSerializer.Serialize(outputData, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine("Captured fixed-profile identities, reuse and stale-input negatives.");
