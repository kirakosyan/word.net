using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Linq;
using Android.App;
using Android.OS;
using Android.Widget;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;
using Environment = System.Environment;

namespace WordNet.Tests.Android;

[Activity(Label = "wordnet validation", MainLauncher = true, Exported = true, Name = "org.wordnet.validation.MainActivity")]
public sealed class MainActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var view = new TextView(this) { Text = "Running wordnet conversions and English OCR…" };
        SetContentView(view);
        _ = Task.Run(async () =>
        {
            string root = FilesDir!.AbsolutePath;
            var report = new XElement("testsuite");
            try
            {
                CopyAssets("Fixtures", root);
                CopyAssets("tessdata", root);
                AppContext.SetData("wordnet.TestFixtureDirectory", Path.Combine(root, "Fixtures"));
                Environment.SetEnvironmentVariable("WORDNET_TESSDATA", Path.Combine(root, "tessdata"));
                Environment.SetEnvironmentVariable("WORDNET_TESSERACT_LIBRARY", Path.Combine(ApplicationInfo!.NativeLibraryDir!, "libtesseract.so"));
                foreach (Type type in typeof(ConversionAcceptanceTests).Assembly.GetTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
                {
                    foreach (MethodInfo method in type.GetMethods().OrderBy(m => m.Name, StringComparer.Ordinal))
                    {
                        FactAttribute? fact = method.GetCustomAttribute<FactAttribute>();
                        if (fact == null) continue;
                        var cases = new List<object?[]>();
                        await using var disposalTracker = new DisposalTracker();
                        if (fact is TheoryAttribute)
                        {
                            foreach (DataAttribute data in method.GetCustomAttributes<DataAttribute>())
                                foreach (ITheoryDataRow row in await data.GetData(method, disposalTracker).ConfigureAwait(false))
                                    cases.Add(row.GetData());
                        }
                        else cases.Add(Array.Empty<object?>());
                        if (cases.Count == 0) throw new InvalidOperationException("No data for " + method.Name);
                        foreach (object?[] arguments in cases)
                        {
                            var test = new XElement("testcase", new XAttribute("name", type.Name + "." + method.Name + "(" + string.Join(",", arguments) + ")"));
                            report.Add(test);
                            try
                            {
                                if (fact.Skip != null) throw new InvalidOperationException("Pending test: " + fact.Skip);
                                object? instance = Activator.CreateInstance(type);
                                try { if (method.Invoke(instance, arguments) is Task task) await task.ConfigureAwait(false); }
                                finally { (instance as IDisposable)?.Dispose(); }
                            }
                            catch (Exception error)
                            {
                                if (error is TargetInvocationException { InnerException: not null } invocation) error = invocation.InnerException!;
                                test.Add(new XElement("failure", error.ToString()));
                            }
                        }
                    }
                }
            }
            catch (Exception error) { report.Add(new XElement("testcase", new XAttribute("name", "Harness"), new XElement("failure", error.ToString()))); }
            int total = report.Elements("testcase").Count(), failed = report.Descendants("failure").Count();
            report.SetAttributeValue("tests", total); report.SetAttributeValue("failures", failed); report.SetAttributeValue("skipped", 0);
            report.SetAttributeValue("runtime", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
            report.SetAttributeValue("architecture", System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);
            report.SetAttributeValue("android", Build.VERSION.Release);
            string path = Path.Combine(root, "results.xml");
            new XDocument(report).Save(path + ".tmp"); File.Move(path + ".tmp", path, true);
            string status = $"wordnet: {total - failed}/{total} passed, {failed} failed.";
            global::Android.Util.Log.Info("wordnet-tests", status);
            RunOnUiThread(() => view.Text = status);
        }).ContinueWith(task =>
        {
            // Catch failures during async state-machine loading as well as inside the suite.
            if (task.Exception == null) return;
            var failure = new XDocument(new XElement("testsuite", new XAttribute("tests", 1), new XAttribute("failures", 1), new XAttribute("skipped", 0),
                new XElement("testcase", new XAttribute("name", "Harness startup"), new XElement("failure", task.Exception.ToString()))));
            failure.Save(Path.Combine(FilesDir!.AbsolutePath, "results.xml"));
            RunOnUiThread(() => view.Text = "Validation app failed to start; see results.xml.");
        }, TaskScheduler.Default);
    }

    private void CopyAssets(string directory, string root)
    {
        Directory.CreateDirectory(Path.Combine(root, directory));
        foreach (string name in Assets!.List(directory)!)
        {
            string asset = directory + "/" + name;
            if (Assets.List(asset)!.Length > 0) CopyAssets(asset, root);
            else
            {
                using Stream source = Assets.Open(asset);
                using Stream output = File.Create(Path.Combine(root, asset)); source.CopyTo(output);
            }
        }
    }
}
