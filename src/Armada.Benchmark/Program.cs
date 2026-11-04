namespace Armada.Benchmark
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Benchmark.Client;
    using Armada.Benchmark.Reporting;
    using Armada.Benchmark.Runners;

    /// <summary>
    /// Armada black-box benchmark harness. Commands:
    /// <c>admission</c> measures captain admission under Disabled, Observe and Enforce selection on one Admiral;
    /// <c>compare</c> checks a candidate report against a baseline.
    /// Exit codes: 0 passed, 1 failed or incomparable run, 2 bad arguments, 3 budget or regression finding.
    /// </summary>
    public static class Program
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _Pretty = new JsonSerializerOptions { WriteIndented = true };

        #endregion

        #region Public-Methods

        /// <summary>Entry point.</summary>
        /// <param name="args">Command and options.</param>
        /// <returns>Exit code.</returns>
        public static async Task<int> Main(string[] args)
        {
            BenchmarkArguments arguments;
            try { arguments = BenchmarkArguments.Parse(args); }
            catch (ArgumentException ex) { return Usage(ex.Message); }
            try
            {
                if (arguments.Command == "admission") return await AdmissionAsync(arguments).ConfigureAwait(false);
                if (arguments.Command == "compare") return Compare(arguments);
                return Usage("Unknown command " + arguments.Command + ".");
            }
            catch (ArgumentException ex) { return Usage(ex.Message); }
            catch (Exception ex) when (ex is BenchRequestException || ex is InvalidOperationException || ex is IOException)
            {
                Console.Error.WriteLine("[bench] failed: " + ex.Message);
                return 1;
            }
        }

        #endregion

        #region Private-Methods

        private static async Task<int> AdmissionAsync(BenchmarkArguments arguments)
        {
            string root = RepositoryRoot();
            BenchmarkScenario scenario = new BenchmarkScenario
            {
                Candidates = arguments.Int("candidates", 10),
                Missions = arguments.Int("missions", 200),
                CaptainConcurrency = arguments.Int("captain-concurrency", 4),
                AccountUsedPercents = arguments.List("accounts", "10,20,30,50,89").Select(v => Double.Parse(v, CultureInfo.InvariantCulture)).ToList(),
                Strategy = arguments.Text("strategy", "MostRemainingAllowance"),
                ReservePercent = arguments.Number("reserve", 10),
                PerAttemptReservationPercent = arguments.Number("per-attempt", 0.25),
                SubmitConcurrency = arguments.Int("submit-concurrency", 16),
                Seed = arguments.Int("seed", 42)
            };
            if (scenario.Candidates < 1 || scenario.Missions < 1 || scenario.AccountUsedPercents.Count == 0)
                throw new ArgumentException("--candidates, --missions and --accounts must be positive.");
            string output = arguments.Text("output", Path.Combine(root, "benchmarks", "results",
                "admission-" + scenario.Candidates + "-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture) + ".json"));
            List<string> arms = arguments.List("arms", "Enforce,Observe,Disabled");

            BenchmarkReport report = new BenchmarkReport { Scenario = scenario };
            report.Provenance = new BenchmarkProvenance
            {
                Commit = Git(root, "rev-parse HEAD"), Dirty = Git(root, "status --porcelain --untracked-files=no").Length > 0,
                Os = RuntimeInformation.OSDescription, Architecture = RuntimeInformation.OSArchitecture.ToString(), Processors = Environment.ProcessorCount,
                Framework = RuntimeInformation.FrameworkDescription, DatabaseProvider = arguments.Text("provider", "Sqlite"),
                MaxConnections = BenchClient.MaxConnections, TimeoutSeconds = BenchClient.TimeoutSeconds,
                ScenarioHash = AdmissionRunner.Hash(scenario), StartedUtc = DateTime.UtcNow
            };
            using (BenchClient client = new BenchClient(arguments.Text("url", "http://127.0.0.1:25160"), arguments.Text("token", "default")))
            {
                AdmissionRunner runner = new AdmissionRunner(client, scenario, arguments.Text("repo-url", ""),
                    arguments.Text("capture-executable", "python3"), arguments.Text("capture-script", Path.Combine(root, "benchmarks", "stub", "usage.py")),
                    arguments.Int("arm-timeout", 900), arguments.Int("warmup", 20));
                if (String.IsNullOrWhiteSpace(arguments.Text("repo-url", ""))) throw new ArgumentException("--repo-url is required.");
                report.Arms = await runner.RunAsync(arms).ConfigureAwait(false);
            }
            ReportComparer.ApplyBudget(report);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, _Pretty)).ConfigureAwait(false);
            Console.WriteLine("[bench] report: " + output);
            foreach (string finding in report.Findings) Console.WriteLine("[bench] finding: " + finding);
            if (report.Incomparable) return 1;
            return report.Findings.Count > 0 ? 3 : 0;
        }

        private static int Compare(BenchmarkArguments arguments)
        {
            if (arguments.Positional.Count != 2) throw new ArgumentException("compare needs <baseline.json> <candidate.json>.");
            BenchmarkReport baseline = Read(arguments.Positional[0]);
            BenchmarkReport candidate = Read(arguments.Positional[1]);
            List<string> findings = ReportComparer.Compare(baseline, candidate, arguments.Number("tolerance", 0.20));
            foreach (string finding in findings) Console.WriteLine("[bench] regression: " + finding);
            if (findings.Count == 0) Console.WriteLine("[bench] no regression beyond tolerance");
            return findings.Count == 0 ? 0 : 3;
        }

        private static BenchmarkReport Read(string path)
            => JsonSerializer.Deserialize<BenchmarkReport>(File.ReadAllText(path)) ?? throw new IOException(path + " is not a benchmark report.");

        // Results and stubs resolve from the repository root (a folder holding .git and benchmarks/), never the working directory.
        private static string RepositoryRoot()
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "benchmarks")) && (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git"))))
                    return directory.FullName;
                directory = directory.Parent;
            }
            throw new IOException("Repository root (with .git and benchmarks/) not found above " + AppContext.BaseDirectory + ".");
        }

        private static string Git(string root, string arguments)
        {
            try
            {
                ProcessStartInfo start = new ProcessStartInfo("git", arguments) { WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false };
                using Process process = Process.Start(start)!;
                string text = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                return text;
            }
            catch (Exception) { return ""; }
        }

        private static int Usage(string message)
        {
            Console.Error.WriteLine("[bench] " + message);
            Console.Error.WriteLine("Usage: Armada.Benchmark admission --repo-url <url> [--url http://127.0.0.1:25160] [--token default] [--candidates 10] [--missions 200] [--captain-concurrency 4]");
            Console.Error.WriteLine("         [--accounts 10,20,30,50,89] [--strategy MostRemainingAllowance] [--reserve 10] [--per-attempt 0.25] [--arms Enforce,Observe,Disabled]");
            Console.Error.WriteLine("         [--warmup 20] [--arm-timeout 900] [--submit-concurrency 16] [--provider Sqlite] [--output file.json]");
            Console.Error.WriteLine("       Armada.Benchmark compare <baseline.json> <candidate.json> [--tolerance 0.20]");
            return 2;
        }

        #endregion
    }
}
