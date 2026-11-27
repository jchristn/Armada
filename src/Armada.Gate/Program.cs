namespace Armada.Gate
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Entry point: <c>Armada.Gate [--url http://127.0.0.1:25160] [--token default] [--pipelines Gate-A,Gate-D]
    /// [--packs csharp,python] [--runs N] [--setup-only] [--no-upload] [--preset path] [--endpoints path] [--out dir]</c>.
    /// Ctrl+C cancels the running battery run on the Admiral; the finished report is uploaded unless --no-upload.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Run the gate.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>0 when every measured measure passed, 1 when one failed, 2 for usage or setup errors.</returns>
        public static async Task<int> Main(string[] args)
        {
            Dictionary<string, string> options = Parse(args);
            string root = RepositoryRoot();
            string presetPath = Option(options, "preset", Path.Combine(root, "benchmarks", "config", "reference-pipelines.json"));
            string endpointsPath = Option(options, "endpoints", Path.Combine(root, "benchmarks", "config", "gate-endpoints.local.json"));
            if (!File.Exists(endpointsPath))
            {
                Console.Error.WriteLine("[gate] " + endpointsPath + " is missing. Copy gate-endpoints.example.json to it and fill in the values (it is git-ignored).");
                return 2;
            }

            GatePresetFile preset = JsonSerializer.Deserialize<GatePresetFile>(File.ReadAllText(presetPath)) ?? throw new InvalidOperationException("The preset file is empty.");
            GateEndpointsFile endpoints = JsonSerializer.Deserialize<GateEndpointsFile>(File.ReadAllText(endpointsPath)) ?? new GateEndpointsFile();
            List<string> names = Option(options, "pipelines", String.Join(",", preset.Pipelines.Select(p => p.Name))).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            List<GatePipeline> selected = preset.Pipelines.Where(p => names.Contains(p.Name, StringComparer.Ordinal)).ToList();
            if (selected.Count == 0)
            {
                Console.Error.WriteLine("[gate] no pipeline matches " + String.Join(",", names) + ".");
                return 2;
            }

            List<string> packs = Option(options, "packs", String.Join(",", preset.Packs)).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            int? runs = options.TryGetValue("runs", out string? runsText) ? Int32.Parse(runsText) : null;

            ArmadaClientOptions clientOptions = new ArmadaClientOptions(Option(options, "url", "http://127.0.0.1:25160")) { BearerToken = Option(options, "token", "default") };
            using ArmadaClient client = new ArmadaClient(clientOptions);
            GateRunner runner = new GateRunner(client, preset, endpoints, message => Console.WriteLine("[gate] " + message));
            using CancellationTokenSource stop = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) => { e.Cancel = true; stop.Cancel(); };

            Dictionary<string, string> captains = await runner.SetupAsync(selected, stop.Token).ConfigureAwait(false);
            if (options.ContainsKey("setup-only"))
            {
                Console.WriteLine("[gate] setup complete: " + String.Join(", ", captains.Select(c => c.Key + "=" + c.Value)));
                return 0;
            }

            GateReport report = await runner.RunAsync(selected, captains, packs, runs, TimeSpan.FromSeconds(15), stop.Token).ConfigureAwait(false);
            string written = GateReportWriter.Write(report, Option(options, "out", Path.Combine(root, "benchmarks", "results")));
            Console.WriteLine(GateReportWriter.ToMarkdown(report));
            Console.WriteLine("[gate] report: " + written);
            if (!options.ContainsKey("no-upload"))
            {
                // The Admiral keeps the report for the dashboard reliability view; a failed upload never fails the gate.
                try
                {
                    GateReport? stored = await client.UploadGateReportAsync(report, CancellationToken.None).ConfigureAwait(false);
                    Console.WriteLine("[gate] uploaded as " + stored?.Id);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("[gate] could not upload the report: " + ex.Message);
                }
            }

            return report.Measures.Any(m => m.Status == GateMeasureStatusEnum.Fail) ? 1 : 0;
        }

        private static Dictionary<string, string> Parse(string[] args)
        {
            Dictionary<string, string> options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal)) continue;
                string key = args[i].Substring(2);
                bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
                options[key] = hasValue ? args[++i] : "true";
            }

            return options;
        }

        private static string Option(Dictionary<string, string> options, string name, string fallback)
        {
            return options.TryGetValue(name, out string? value) ? value : fallback;
        }

        private static string RepositoryRoot()
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "RELIABILITY_SIMPLICITY_PLAN.md"))) directory = directory.Parent;
            return directory?.FullName ?? Directory.GetCurrentDirectory();
        }
    }
}
