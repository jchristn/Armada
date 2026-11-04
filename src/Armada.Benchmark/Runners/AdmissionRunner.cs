namespace Armada.Benchmark.Runners
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Benchmark.Client;
    using Armada.Benchmark.Metrics;
    using Armada.Benchmark.Reporting;

    /// <summary>
    /// Admission benchmark. Provisions synthetic accounts and candidate captains over REST, then submits the same mission
    /// batch under each selection mode and times creation to atomic slot assignment from the missions' own server
    /// timestamps. While an arm runs the harness samples the reservation estimates Armada publishes for each account; in
    /// Enforce the active plus unsettled estimate must never exceed the headroom above the reserve.
    /// </summary>
    public sealed class AdmissionRunner
    {
        #region Private-Members

        private static readonly HashSet<string> _Terminal = new HashSet<string>(StringComparer.Ordinal)
        {
            "Complete", "Failed", "Cancelled", "WorkProduced", "PullRequestOpen", "LandingFailed"
        };

        private readonly BenchClient _Client;
        private readonly BenchmarkScenario _Scenario;
        private readonly string _RepoUrl;
        private readonly string _CaptureExecutable;
        private readonly string _CaptureScript;
        private readonly int _ArmTimeoutSeconds;
        private readonly int _WarmupMissions;
        private readonly Dictionary<string, int> _AccountOfCaptain = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<string> _CaptureCaptains = new List<string>();
        private DateTime? _LastCompletionUtc;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Create the runner.</summary>
        /// <param name="client">Admiral client.</param>
        /// <param name="scenario">Seeded inputs.</param>
        /// <param name="repoUrl">Local bare repository for every vessel.</param>
        /// <param name="captureExecutable">Interpreter that runs the usage stub.</param>
        /// <param name="captureScript">Usage stub script path.</param>
        /// <param name="armTimeoutSeconds">Per-arm completion timeout.</param>
        /// <param name="warmupMissions">Unrecorded Observe missions run first.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public AdmissionRunner(BenchClient client, BenchmarkScenario scenario, string repoUrl, string captureExecutable, string captureScript, int armTimeoutSeconds, int warmupMissions)
        {
            _Client = client ?? throw new ArgumentNullException(nameof(client));
            _Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            _RepoUrl = repoUrl ?? throw new ArgumentNullException(nameof(repoUrl));
            _CaptureExecutable = captureExecutable ?? throw new ArgumentNullException(nameof(captureExecutable));
            _CaptureScript = captureScript ?? throw new ArgumentNullException(nameof(captureScript));
            _ArmTimeoutSeconds = armTimeoutSeconds;
            _WarmupMissions = warmupMissions;
        }

        #endregion

        #region Public-Methods

        /// <summary>SHA-256 of the scenario, so two reports that claim the same inputs can be checked.</summary>
        /// <param name="scenario">Scenario.</param>
        /// <returns>Lowercase hex hash.</returns>
        public static string Hash(BenchmarkScenario scenario)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(scenario)))).ToLowerInvariant();

        /// <summary>Run every arm in order and return their results.</summary>
        /// <param name="arms">Modes in run order.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Per-arm results.</returns>
        public async Task<List<ArmResult>> RunAsync(IReadOnlyList<string> arms, CancellationToken token = default)
        {
            await ProvisionAsync(token).ConfigureAwait(false);
            if (_WarmupMissions > 0)
            {
                Log("warmup: " + _WarmupMissions + " Observe missions (not recorded)");
                await RunArmAsync("Observe", _WarmupMissions, "warmup", token).ConfigureAwait(false);
            }
            List<ArmResult> results = new List<ArmResult>();
            foreach (string arm in arms)
            {
                ArmResult result = await RunArmAsync(arm, _Scenario.Missions, arm.ToLowerInvariant(), token).ConfigureAwait(false);
                results.Add(result);
                Log(arm + ": admitted " + result.Admitted + "/" + result.Missions + ", p50 " + result.AdmissionLatency.P50Ms + " ms, p95 "
                    + result.AdmissionLatency.P95Ms + " ms, " + result.ThroughputPerSecond + "/s, incomplete " + result.Incomplete + ", violations " + result.AccountingViolations);
            }
            return results;
        }

        #endregion

        #region Private-Methods

        private async Task ProvisionAsync(CancellationToken token)
        {
            Log("provisioning " + _Scenario.Candidates + " captains over " + _Scenario.AccountUsedPercents.Count + " accounts");
            for (int i = 0; i < _Scenario.Candidates; i++)
            {
                int account = i % _Scenario.AccountUsedPercents.Count;
                List<string> arguments = new List<string> { _CaptureScript, "--account", AccountName(account),
                    "--used", _Scenario.AccountUsedPercents[account].ToString(CultureInfo.InvariantCulture) };
                BenchCaptain captain = await _Client.CreateCaptainAsync("bench-" + i.ToString("D4", CultureInfo.InvariantCulture), _CaptureExecutable, arguments, _Scenario.CaptainConcurrency, token).ConfigureAwait(false);
                _AccountOfCaptain[captain.Id] = account;
                if (i < _Scenario.AccountUsedPercents.Count) _CaptureCaptains.Add(captain.Id);
            }
            await CaptureAllAsync(token).ConfigureAwait(false);
        }

        // One capture per account updates every member captain. Wait until every captain is on an Ok latest row captured at
        // or after notBeforeUtc; a capture refused inside the cooldown is left to the 60 s scheduled capture.
        private async Task CaptureAllAsync(CancellationToken token, DateTime? notBeforeUtc = null)
        {
            DateTime floor = notBeforeUtc ?? DateTime.MinValue;
            foreach (string captainId in _CaptureCaptains) await _Client.CaptureAsync(captainId, token).ConfigureAwait(false);
            DateTime deadline = DateTime.UtcNow.AddSeconds(180);
            while (true)
            {
                BenchUsagePage page = await _Client.LatestUsageAsync(token).ConfigureAwait(false);
                HashSet<string> covered = page.Objects.Where(r => r.Status == "Ok" && r.CapturedUtc.ToUniversalTime() >= floor)
                    .SelectMany(r => r.CaptainIds).ToHashSet(StringComparer.Ordinal);
                if (_AccountOfCaptain.Keys.All(covered.Contains)) return;
                if (DateTime.UtcNow > deadline) throw new InvalidOperationException("Usage captures did not cover every captain within 180 s (" + covered.Count + "/" + _AccountOfCaptain.Count + ").");
                await Task.Delay(1000, token).ConfigureAwait(false);
            }
        }

        private async Task<ArmResult> RunArmAsync(string mode, int missions, string label, CancellationToken token)
        {
            BenchSettings settings = await _Client.ReadSettingsAsync(token).ConfigureAwait(false);
            BenchUsageSelection selection = settings.UsageSelection ?? new BenchUsageSelection();
            selection.Mode = mode; selection.Strategy = _Scenario.Strategy;
            selection.ReservePercent = _Scenario.ReservePercent; selection.PerAttemptReservationPercent = _Scenario.PerAttemptReservationPercent;
            selection.SettlementLagSeconds = 0;
            selection.MaximumCandidates = Math.Max(selection.MaximumCandidates, _Scenario.Candidates);
            await _Client.UpdateSelectionAsync(selection, settings.UsageSelectionRevision, token).ConfigureAwait(false);
            // A capture after the previous arm ended settles its reservations on the next reconciler tick.
            await CaptureAllAsync(token, _LastCompletionUtc).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(12), token).ConfigureAwait(false);

            int errorsBefore = _Client.Errors;
            BenchVessel vessel = await _Client.CreateVesselAsync("bench-" + label + "-" + Guid.NewGuid().ToString("N").Substring(0, 6), _RepoUrl, token).ConfigureAwait(false);
            Stopwatch watch = Stopwatch.StartNew();
            using (SemaphoreSlim gate = new SemaphoreSlim(Math.Max(1, _Scenario.SubmitConcurrency)))
            {
                List<Task> submits = new List<Task>();
                for (int i = 0; i < missions; i++)
                {
                    string title = "bench " + label + " " + i.ToString("D4", CultureInfo.InvariantCulture);
                    await gate.WaitAsync(token).ConfigureAwait(false);
                    submits.Add(Task.Run(async () =>
                    {
                        try { await _Client.CreateMissionAsync(vessel.Id, title, token).ConfigureAwait(false); }
                        catch (BenchRequestException ex) { Log("submit failed: " + ex.Message); }
                        finally { gate.Release(); }
                    }, token));
                }
                await Task.WhenAll(submits).ConfigureAwait(false);
            }

            Dictionary<int, double> reserved = new Dictionary<int, double>();
            List<BenchMission> final = await WaitForTerminalAsync(vessel.Id, missions, reserved, token).ConfigureAwait(false);
            double duration = watch.Elapsed.TotalSeconds;
            List<BenchMission> unfinished = final.Where(m => !_Terminal.Contains(m.Status)).ToList();
            DateTime? lastCompletion = final.Where(m => m.CompletedUtc.HasValue).Select(m => (DateTime?)m.CompletedUtc!.Value.ToUniversalTime()).DefaultIfEmpty(null).Max();
            if (lastCompletion.HasValue) _LastCompletionUtc = lastCompletion.Value.AddSeconds(1);
            foreach (BenchMission mission in unfinished) await _Client.CancelMissionAsync(mission.Id, token).ConfigureAwait(false);
            return Summarize(mode, final, unfinished.Count + Math.Max(0, missions - final.Count), _Client.Errors - errorsBefore, duration, reserved);
        }

        private async Task<List<BenchMission>> WaitForTerminalAsync(string vesselId, int expected, Dictionary<int, double> reserved, CancellationToken token)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(_ArmTimeoutSeconds);
            List<BenchMission> missions = new List<BenchMission>();
            while (DateTime.UtcNow < deadline)
            {
                missions = (await _Client.MissionsAsync(vesselId, token).ConfigureAwait(false)).Objects;
                await SampleReservationsAsync(reserved, token).ConfigureAwait(false);
                if (missions.Count >= expected && missions.All(m => _Terminal.Contains(m.Status))) return missions;
                await Task.Delay(1000, token).ConfigureAwait(false);
            }
            Log("arm timed out after " + _ArmTimeoutSeconds + " s with " + missions.Count(m => !_Terminal.Contains(m.Status)) + " missions unfinished");
            return missions;
        }

        private async Task SampleReservationsAsync(Dictionary<int, double> reserved, CancellationToken token)
        {
            BenchUsagePage page = await _Client.LatestUsageAsync(token).ConfigureAwait(false);
            foreach (BenchUsageRow row in page.Objects)
            {
                double total = row.ReservationEstimates.Count == 0 ? 0 : row.ReservationEstimates.Max(e => e.ActivePercent + e.UnsettledPercent);
                foreach (int account in row.CaptainIds.Where(_AccountOfCaptain.ContainsKey).Select(id => _AccountOfCaptain[id]).Distinct())
                    reserved[account] = Math.Max(reserved.TryGetValue(account, out double seen) ? seen : 0, total);
            }
        }

        private ArmResult Summarize(string mode, List<BenchMission> missions, int incomplete, int errors, double duration, Dictionary<int, double> reserved)
        {
            List<BenchMission> admitted = missions.Where(m => m.AssignedUtc.HasValue).ToList();
            List<double> latency = admitted.Select(m => Math.Max(0, (m.AssignedUtc!.Value - m.CreatedUtc).TotalMilliseconds)).ToList();
            ArmResult result = new ArmResult
            {
                Mode = mode, Strategy = _Scenario.Strategy, Missions = _Scenario.Missions, Admitted = admitted.Count, Incomplete = incomplete, Errors = errors,
                AdmissionLatency = LatencyStatistics.From(latency), WaitedOverFiveSeconds = latency.Count(v => v > 5000),
                DurationSeconds = Math.Round(duration, 1),
                FinalStatuses = missions.GroupBy(m => m.Status).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal)
            };
            if (admitted.Count > 0)
            {
                double span = (admitted.Max(m => m.AssignedUtc!.Value) - missions.Min(m => m.CreatedUtc)).TotalSeconds;
                result.ThroughputPerSecond = span <= 0 ? admitted.Count : Math.Round(admitted.Count / span, 2);
            }
            for (int account = 0; account < _Scenario.AccountUsedPercents.Count; account++)
            {
                double used = _Scenario.AccountUsedPercents[account];
                int capacity = (int)Math.Floor(Math.Max(0, 100 - used - _Scenario.ReservePercent) / _Scenario.PerAttemptReservationPercent + 1e-9);
                int onAccount = admitted.Count(m => m.CaptainId != null && _AccountOfCaptain.TryGetValue(m.CaptainId, out int a) && a == account);
                double headroom = Math.Max(0, 100 - used - _Scenario.ReservePercent);
                double maxReserved = Math.Round(reserved.TryGetValue(account, out double seen) ? seen : 0, 6);
                AccountShare share = new AccountShare { Account = AccountName(account), UsedPercent = used, Admissions = onAccount, Capacity = capacity,
                    MaxReservedPercent = maxReserved, HeadroomPercent = headroom, Violation = mode == "Enforce" && maxReserved > headroom + 1e-6 };
                result.Accounts.Add(share);
            }
            result.AccountingViolations = result.Accounts.Count(a => a.Violation);
            return result;
        }

        private static string AccountName(int index) => "bench-account-" + index.ToString(CultureInfo.InvariantCulture);

        private static void Log(string message) => Console.WriteLine("[bench] " + message);

        #endregion
    }
}
