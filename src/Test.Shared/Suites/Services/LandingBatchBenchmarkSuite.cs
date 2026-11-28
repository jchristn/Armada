namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Benchmark of batched merge queue landing against serial landing (RS-9.4), on real git repositories with a
    /// simulated verification of fixed duration. Opt-in: it runs only with <c>ARMADA_BENCH_LANDING=1</c> (see
    /// <c>benchmarks/run-landing-batch.sh</c>) and writes <c>benchmarks/results/landing-batch.json</c>. No model or
    /// network is used. <c>ARMADA_BENCH_LANDING_ENTRIES</c> (default 8) and <c>ARMADA_BENCH_LANDING_CHECK_MS</c>
    /// (default 1500) size the run.
    /// </summary>
    public sealed class LandingBatchBenchmarkSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.LandingBatchBenchmark";
        private const string EnabledVariable = "ARMADA_BENCH_LANDING";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            bool run = Environment.GetEnvironmentVariable(EnabledVariable) == "1";
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(
                    suiteId: SuiteId,
                    caseId: "serial_versus_batched_landing",
                    displayName: "Serial landing against batched landing, with and without a failing entry",
                    executeAsync: (CancellationToken ct) => RunAsync(),
                    tags: new List<string> { TestTags.Reliability, TestTags.Process },
                    skip: !run,
                    skipReason: run ? null : "Benchmark. Set " + EnabledVariable + "=1 (benchmarks/run-landing-batch.sh) to run it.")
            };
            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Landing batch benchmark", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task RunAsync()
        {
            int entries = ReadInt("ARMADA_BENCH_LANDING_ENTRIES", 8, 2, 40);
            int checkMs = ReadInt("ARMADA_BENCH_LANDING_CHECK_MS", 1500, 0, 60000);
            List<LandingBenchmarkArm> arms = new List<LandingBenchmarkArm>
            {
                await RunArmAsync("serial", 1, entries, checkMs, false).ConfigureAwait(false),
                await RunArmAsync("batched", 4, entries, checkMs, false).ConfigureAwait(false),
                await RunArmAsync("serial-one-failing", 1, entries, checkMs, true).ConfigureAwait(false),
                await RunArmAsync("batched-one-failing", 4, entries, checkMs, true).ConfigureAwait(false)
            };

            foreach (LandingBenchmarkArm arm in arms)
            {
                Console.WriteLine("  " + arm.Name.PadRight(22) + " batch " + arm.BatchSize + "  landed " + arm.Landed + "/" + arm.Entries
                    + "  checks " + arm.Checks + "  fetches " + arm.Fetches + "  " + arm.ElapsedMs.ToString(CultureInfo.InvariantCulture) + " ms");
            }

            string? results = FindResultsDirectory();
            if (results != null)
            {
                string path = Path.Combine(results, "landing-batch.json");
                File.WriteAllText(path, JsonSerializer.Serialize(arms, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine("  report: " + path);
            }
        }

        private static async Task<LandingBenchmarkArm> RunArmAsync(string name, int batchSize, int entryCount, int checkMs, bool oneFailing)
        {
            using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
            Vessel vessel = new Vessel("bench-" + Guid.NewGuid().ToString("N").Substring(0, 8), repos.Origin) { LocalPath = repos.VesselRepo, DefaultBranch = "main" };
            vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            ArmadaSettings settings = new ArmadaSettings
            {
                DocksDirectory = TestTemp.NewDirectory("bench_docks"),
                ReposDirectory = TestTemp.NewDirectory("bench_repos"),
                MergeQueueTestCommand = null,
                MergeQueueBatchSize = batchSize
            };
            MergeQueueService queue = new MergeQueueService(logging, testDb.Driver, settings, new GitService(logging));
            int failingIndex = oneFailing ? entryCount / 2 : -1;
            List<MergeEntry> enqueued = new List<MergeEntry>();
            for (int i = 0; i < entryCount; i++)
            {
                string branch = "feature/bench-" + i;
                repos.PushBranch(branch, i == failingIndex ? "bad.txt" : "bench-" + i + ".txt", "change " + i + "\n");
                enqueued.Add(await queue.EnqueueAsync(new MergeEntry(branch, "main") { VesselId = vessel.Id, TestCommand = "check", Priority = i }).ConfigureAwait(false));
            }

            int checks = 0;
            queue.LocalVerificationCommands = new FakeHostCommandExecutor { Handler = request =>
            {
                Interlocked.Increment(ref checks);
                if (checkMs > 0) Thread.Sleep(checkMs);
                bool bad = File.Exists(Path.Combine(request.WorkingDirectory, "bad.txt"));
                return new HostCommandResult { ExitCode = bad ? 1 : 0 };
            } };

            Stopwatch watch = Stopwatch.StartNew();
            await queue.ProcessQueueAsync().ConfigureAwait(false);
            watch.Stop();

            LandingBenchmarkArm arm = new LandingBenchmarkArm { Name = name, BatchSize = batchSize, Entries = entryCount, CheckMs = checkMs };
            foreach (MergeEntry entry in enqueued)
            {
                MergeStatusEnum status = (await queue.GetAsync(entry.Id).ConfigureAwait(false))!.Status;
                if (status == MergeStatusEnum.Landed) arm.Landed++;
                if (status == MergeStatusEnum.Failed) arm.Failed++;
            }
            arm.Checks = checks;
            arm.Fetches = queue.FetchCount;
            arm.ElapsedMs = watch.ElapsedMilliseconds;
            AssertEqual(oneFailing ? entryCount - 1 : entryCount, arm.Landed, name + ": landed entries");
            return arm;
        }

        private static int ReadInt(string variable, int fallback, int minimum, int maximum)
        {
            string? raw = Environment.GetEnvironmentVariable(variable);
            if (!Int32.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) return fallback;
            return Math.Clamp(value, minimum, maximum);
        }

        private static string? FindResultsDirectory()
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                string benchmarks = Path.Combine(directory.FullName, "benchmarks");
                if (File.Exists(Path.Combine(benchmarks, "README.md")))
                {
                    string results = Path.Combine(benchmarks, "results");
                    Directory.CreateDirectory(results);
                    return results;
                }
                directory = directory.Parent;
            }
            return null;
        }

        #endregion
    }
}
