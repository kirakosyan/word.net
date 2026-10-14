using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace WordNet.Tests
{
    // Only for negative runner checks. This does not execute conversion tests.
    internal static class FrameworkRunnerProbe
    {
        private static void Main()
        {
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "worker.pid"), Process.GetCurrentProcess().Id.ToString());
            if (Environment.GetEnvironmentVariable("WORDNET_RUNNER_PROBE_EXIT") == "fail") Environment.Exit(17);
            if (Environment.GetEnvironmentVariable("WORDNET_RUNNER_PROBE_EXIT") != "1") Thread.Sleep(30000);
        }
    }
}
