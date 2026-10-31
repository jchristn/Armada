namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the account usage host execution path (AC2 Services.UsageHostExecution): the
    /// host-side usage helper's Custom executable contract on this platform's real processes (the
    /// documented single JSON object output, the host-owned account HMAC, the host-owned source and
    /// status, and the validation that rejects malformed, wrong-version and out-of-range output), the
    /// host execution deadline (the process tree is terminated on expiry and on cancellation), the
    /// Harbor link path (a capable Harbor runs the probe on its own host through the usage-probe
    /// capability; an older Harbor answers Unsupported with HostUpgradeRequired, and a disconnected
    /// host answers Failed with its last good data retained and a backoff instead of a tight retry),
    /// and the dedicated-job guarantee (a capture is its own UsageProbe job; a Harbor runs no captain
    /// job for it and a captain's state and mission slot never change).
    /// </summary>
    public sealed class UsageHostExecutionSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.UsageHostExecution";
        private const string TenantId = "ten_usage_exec";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the usage host execution suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("custom_probe_contract_round_trip_on_real_process", "The Custom command contract round-trips through a real process with the host-owned account key", TestTags.Positive, async () =>
            {
                LoggingModule logging = CreateLogging();
                UsageAccountResolver accounts = new UsageAccountResolver("host-exec-1", new string('b', 64));
                UsageProbeHostRunner runner = new UsageProbeHostRunner(logging, accounts);

                UsageProbeHost host = new UsageProbeHost
                {
                    TenantId = TenantId,
                    HostId = "host-exec-1",
                    Runtime = AgentRuntimeEnum.ClaudeCode,
                    Mode = UsageCaptureModeEnum.Custom,
                    TimeoutSeconds = 10
                };
                host.CommandJson = CommandJson("/bin/echo", new string[]
                {
                    "{\"schemaVersion\":1,\"status\":\"Ok\",\"accountKey\":\"acct-raw-7\",\"provider\":\"anthropic\","
                    + "\"capturedUtc\":\"2026-10-27T13:00:00Z\",\"source\":\"Custom\",\"windows\":["
                    + "{\"name\":\"five_hour\",\"usedPercent\":62,\"used\":62500,\"limit\":100000,\"unit\":\"percent\","
                    + "\"windowMinutes\":300,\"resetsUtc\":\"2026-10-27T14:05:00Z\"}]}"
                });

                UsageProbeResult result = await runner.CaptureAsync(host).ConfigureAwait(false);
                AssertEqual(UsageSnapshotStatusEnum.Ok, result.Status, "a valid contract output is an Ok capture");
                AssertEqual(UsageCaptureSourceEnum.Custom, result.Source, "the host owns the source: the executable cannot claim another");
                AssertEqual("anthropic", result.Provider, "the provider round-trips");
                AssertEqual(1, result.Windows.Count, "the windows round-trip");
                AssertEqual("five_hour", result.Windows[0].Name, "the window name round-trips");
                AssertEqual(62, result.Windows[0].UsedPercent, "the utilization round-trips");
                AssertEqual(62500, result.Windows[0].Used, "the usage count round-trips");
                AssertEqual(100000, result.Windows[0].Limit, "the limit round-trips");
                AssertEqual(new DateTime(2026, 10, 27, 14, 5, 0, DateTimeKind.Utc), result.Windows[0].ResetsUtc, "the reset time round-trips");
                AssertNull(result.ErrorCode, "a valid capture has no error code");

                // The host owns the final account key: the raw provider identity is HMAC'd with the
                // host-local key and the tenant namespace, never returned or stored as-is.
                string expected = accounts.DeriveAccountKey(TenantId, AgentRuntimeEnum.ClaudeCode, "acct-raw-7")!;
                AssertEqual(expected, result.AccountKey, "the account key is the host-derived HMAC of the raw identity");
                AssertFalse(String.Equals(result.AccountKey, "acct-raw-7", StringComparison.Ordinal), "the raw identity never survives as the account key");
            }));

            cases.Add(CaseAsync("custom_probe_rejects_malformed_and_out_of_contract_output", "Malformed, wrong-version and out-of-range contract output is Failed with a stable code", TestTags.Negative, async () =>
            {
                LoggingModule logging = CreateLogging();
                UsageProbeHostRunner runner = new UsageProbeHostRunner(logging, new UsageAccountResolver("host-exec-2", new string('c', 64)));

                List<KeyValuePair<string, string>> fixtures = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("not-json", "MalformedOutput"),
                    new KeyValuePair<string, string>("{}", "UnsupportedSchemaVersion"),
                    new KeyValuePair<string, string>("{\"schemaVersion\":2,\"status\":\"Ok\"}", "UnsupportedSchemaVersion"),
                    new KeyValuePair<string, string>("{\"schemaVersion\":1}", "MalformedOutput"),
                    new KeyValuePair<string, string>("{\"schemaVersion\":1,\"status\":\"Nonsense\"}", "MalformedOutput"),
                    new KeyValuePair<string, string>("{\"schemaVersion\":1,\"status\":\"Ok\",\"windows\":[{\"name\":\"w\",\"usedPercent\":150}]}", "MalformedOutput"),
                    new KeyValuePair<string, string>("{\"schemaVersion\":1,\"status\":\"Ok\",\"windows\":[{\"name\":\"w\",\"usedPercent\":-1}]}", "MalformedOutput"),
                    new KeyValuePair<string, string>("{\"schemaVersion\":1,\"status\":\"Ok\",\"windows\":[{\"name\":\"w\",\"limit\":0}]}", "MalformedOutput"),
                    new KeyValuePair<string, string>("{\"schemaVersion\":1,\"status\":\"Ok\",\"windows\":[{\"name\":\"" + new string('w', 400) + "\"}]}", "MalformedOutput")
                };

                foreach (KeyValuePair<string, string> fixture in fixtures)
                {
                    UsageProbeHost host = NewCustomHost("host-exec-2", CommandJson("/bin/echo", new string[] { fixture.Key }));
                    UsageProbeResult result = await runner.CaptureAsync(host).ConfigureAwait(false);
                    AssertEqual(UsageSnapshotStatusEnum.Failed, result.Status, "an out-of-contract output is a failed attempt: " + fixture.Key);
                    AssertEqual(fixture.Value, result.ErrorCode, "the failure carries its stable error code: " + fixture.Key);
                    AssertNull(result.AccountKey, "a failed attempt never invents an account key");
                }

                // An AuthMissing answer is a valid read of a failed capture state, and it stays ungrouped.
                UsageProbeHost authHost = NewCustomHost("host-exec-2", CommandJson("/bin/echo", new string[]
                {
                    "{\"schemaVersion\":1,\"status\":\"AuthMissing\",\"errorCode\":\"CredentialUnavailable\"}"
                }));
                UsageProbeResult authMissing = await runner.CaptureAsync(authHost).ConfigureAwait(false);
                AssertEqual(UsageSnapshotStatusEnum.AuthMissing, authMissing.Status, "AuthMissing round-trips as itself");
                AssertEqual("CredentialUnavailable", authMissing.ErrorCode, "the executable's stable error code round-trips");
                AssertNull(authMissing.AccountKey, "AuthMissing never produces an account key");

                // Shell syntax in the executable is rejected: the contract is an executable and arguments,
                // never a shell string.
                UsageProbeHost shellHost = NewCustomHost("host-exec-2", "{\"executable\":\"/bin/sh -c 'echo hi'\",\"arguments\":[]}");
                UsageProbeResult shell = await runner.CaptureAsync(shellHost).ConfigureAwait(false);
                AssertEqual(UsageSnapshotStatusEnum.Failed, shell.Status, "shell syntax in the executable is refused");
                AssertEqual("MalformedCommandConfig", shell.ErrorCode, "the refusal names the command configuration");
            }));

            cases.Add(CaseAsync("custom_probe_timeout_kills_process_tree", "A probe past its deadline is Failed and its process tree is terminated", TestTags.Positive, async () =>
            {
                LoggingModule logging = CreateLogging();
                UsageProbeHostRunner runner = new UsageProbeHostRunner(logging, new UsageAccountResolver("host-exec-3", new string('d', 64)));

                string pidFile = Path.Combine(TestTemp.NewDirectory("usage_probe_tree"), "shell.pid");
                string command = "echo $$ > " + Quote(pidFile) + "; sleep 600";
                UsageProbeHost host = NewCustomHost("host-exec-3", CommandJson("/bin/sh", new string[] { "-c", command }));
                host.TimeoutSeconds = 1;

                Stopwatch watch = Stopwatch.StartNew();
                UsageProbeResult result = await runner.CaptureAsync(host).ConfigureAwait(false);
                watch.Stop();

                AssertEqual(UsageSnapshotStatusEnum.Failed, result.Status, "a probe past its deadline is a failed attempt");
                AssertEqual("ProbeTimeout", result.ErrorCode, "the failure names the timeout");
                AssertTrue(watch.Elapsed < TimeSpan.FromSeconds(15), "the deadline is enforced promptly, not by the round-trip wait");
                AssertTrue(watch.Elapsed >= TimeSpan.FromSeconds(1), "the probe did run for its deadline");

                // The process tree is terminated: the shell that hosted the probe is gone.
                AssertTrue(File.Exists(pidFile), "the probe process wrote its pid before the deadline");
                int pid = Int32.Parse((await File.ReadAllTextAsync(pidFile).ConfigureAwait(false)).Trim());
                AssertFalse(IsProcessAlive(pid), "the probe's shell process tree was terminated");
            }));

            cases.Add(CaseAsync("custom_probe_cancellation_stops_process_tree", "Cancelling the capture stops the probe and cleans up its process tree", TestTags.Positive, async () =>
            {
                LoggingModule logging = CreateLogging();
                UsageProbeHostRunner runner = new UsageProbeHostRunner(logging, new UsageAccountResolver("host-exec-4", new string('e', 64)));

                string pidFile = Path.Combine(TestTemp.NewDirectory("usage_probe_cancel"), "shell.pid");
                string command = "echo $$ > " + Quote(pidFile) + "; sleep 600";
                UsageProbeHost host = NewCustomHost("host-exec-4", CommandJson("/bin/sh", new string[] { "-c", command }));

                using CancellationTokenSource cancellation = new CancellationTokenSource();
                Task<UsageProbeResult> capture = runner.CaptureAsync(host, cancellation.Token);
                await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
                cancellation.Cancel();
                UsageProbeResult result = await capture.ConfigureAwait(false);

                AssertEqual(UsageSnapshotStatusEnum.Failed, result.Status, "a cancelled capture is a failed attempt");
                AssertEqual("ProbeCancelled", result.ErrorCode, "the failure names the cancellation");
                AssertTrue(File.Exists(pidFile), "the probe process wrote its pid before the cancellation");
                int pid = Int32.Parse((await File.ReadAllTextAsync(pidFile).ConfigureAwait(false)).Trim());
                AssertFalse(IsProcessAlive(pid), "the cancelled probe's process tree was terminated");
            }));

            cases.Add(CaseAsync("older_harbor_answers_unsupported_until_it_advertises_the_capability", "A Harbor without the usage-probe capability answers Unsupported with HostUpgradeRequired", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                LoggingModule logging = CreateLogging();
                HarborConnectionManager manager = new HarborConnectionManager(new HarborService(testDb.Driver, logging), logging, null);

                // A capable Harbor (its link client carries the host-side helper) advertises the capability.
                InProcessHarbor capable = await InProcessHarbor.ConnectAsync(
                    manager, "hbr_capable", TenantId, null, new AgentRuntimeFactory(logging), new string[] { "git" }, logging,
                    usageProbe: new UsageProbeHostRunner(logging, new UsageAccountResolver("hbr_capable", new string('f', 64)))).ConfigureAwait(false);
                try
                {
                    AssertTrue(manager.HostsUsageProbes("hbr_capable"), "a Harbor with the helper advertises the usage-probe capability");

                    // The probe itself is answered on the Harbor's host, and the sanitized result returns.
                    RemoteHostCommandExecutor executor = new RemoteHostCommandExecutor(manager, "hbr_capable");
                    UsageProbeHost host = new UsageProbeHost
                    {
                        TenantId = TenantId,
                        HostId = "hbr_capable",
                        HarborId = "hbr_capable",
                        Runtime = AgentRuntimeEnum.ClaudeCode,
                        Mode = UsageCaptureModeEnum.Auto,
                        TimeoutSeconds = 5
                    };
                    HostCommandResult reply = await executor.RunAsync(new HostCommandRequest
                    {
                        Kind = HostCommandKindEnum.UsageProbe,
                        UsageProbeJson = host.Serialize(),
                        TimeoutMs = 5000,
                        Label = "usage-probe"
                    }).ConfigureAwait(false);
                    UsageProbeResult result = UsageProbeResult.Deserialize(reply.StandardOutput);
                    AssertEqual(UsageSnapshotStatusEnum.Unsupported, result.Status, "no runtime probe adapter ships yet, so Auto answers Unsupported");
                    AssertEqual("RuntimeProbeUnavailable", result.ErrorCode, "the Unsupported answer names the missing adapter");
                    AssertEqual(0, capable.Runner.Launches.Count, "a probe never launches a captain job on the Harbor");
                }
                finally
                {
                    capable.Dispose();
                }

                // An older Harbor (no helper) does not advertise the capability: the Admiral answers
                // Unsupported with HostUpgradeRequired instead of probing it.
                InProcessHarbor older = await InProcessHarbor.ConnectAsync(
                    manager, "hbr_older", TenantId, null, new AgentRuntimeFactory(logging), new string[] { "git" }, logging).ConfigureAwait(false);
                try
                {
                    AssertFalse(manager.HostsUsageProbes("hbr_older"), "an older Harbor does not advertise the usage-probe capability");
                    RemoteHostCommandExecutor executor = new RemoteHostCommandExecutor(manager, "hbr_older");
                    HostCommandResult reply = await executor.RunAsync(new HostCommandRequest
                    {
                        Kind = HostCommandKindEnum.UsageProbe,
                        UsageProbeJson = "{\"tenantId\":\"" + TenantId + "\",\"hostId\":\"hbr_older\"}",
                        TimeoutMs = 5000
                    }).ConfigureAwait(false);
                    UsageProbeResult result = UsageProbeResult.Deserialize(reply.StandardOutput);
                    AssertEqual(UsageSnapshotStatusEnum.Unsupported, result.Status, "an older Harbor makes the capture Unsupported");
                    AssertEqual("HostUpgradeRequired", result.ErrorCode, "the answer tells the operator to update the Harbor");
                }
                finally
                {
                    older.Dispose();
                }
            }));

            cases.Add(CaseAsync("harbor_side_probe_runs_over_the_link_and_persists", "A capable Harbor runs the Custom probe on its host and the Admiral commits the sanitized snapshot", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await SeedTenantAsync(testDb.Driver).ConfigureAwait(false);
                LoggingModule logging = CreateLogging();
                HarborConnectionManager manager = new HarborConnectionManager(new HarborService(testDb.Driver, logging), logging, null);

                UsageAccountResolver harborAccounts = new UsageAccountResolver("hbr_split", new string('1', 64), "hbr_split");
                InProcessHarbor harbor = await InProcessHarbor.ConnectAsync(
                    manager, "hbr_split", TenantId, null, new AgentRuntimeFactory(logging), new string[] { "git" }, logging,
                    usageProbe: new UsageProbeHostRunner(logging, harborAccounts)).ConfigureAwait(false);
                try
                {
                    ArmadaSettings settings = new ArmadaSettings();
                    settings.UsageCapture = new UsageCaptureSettings { Enabled = true, ProbeTimeoutSeconds = 10 };

                    UsageAccountResolver admiralAccounts = new UsageAccountResolver("host-split", new string('2', 64));
                    UsageCaptureService service = new UsageCaptureService(
                        testDb.Driver, settings, admiralAccounts, new StubExecutor(), manager, logging);

                    UsageCaptureGroup group = new UsageCaptureGroup
                    {
                        GroupKey = UsageAccountResolver.GroupKey(TenantId, "hbr_split", AgentRuntimeEnum.Codex, "fingerprint-1"),
                        TenantId = TenantId,
                        HostId = "hbr_split",
                        HarborId = "hbr_split",
                        Runtime = AgentRuntimeEnum.Codex,
                        Mode = UsageCaptureModeEnum.Custom,
                        CommandJson = SplitCommandJson(),
                        CommandFingerprint = "fingerprint-1",
                        ConfigurationRevision = "hbr_split|Codex|fingerprint-1",
                        EffectiveIntervalSeconds = 300,
                        MemberCaptainIds = new List<string> { "cpt_split_a", "cpt_split_b" }
                    };

                    UsageCaptureAttempt attempt = await service.TryStartAsync(group, "cpt_split_a").ConfigureAwait(false);
                    AssertTrue(attempt.Started, "the split-mode capture starts");
                    UsageCaptureAttemptResult result = await AwaitAsync(attempt.Completion).ConfigureAwait(false);
                    AssertTrue(result.Persisted, "the sanitized snapshot is committed on the Admiral");
                    AssertEqual(UsageSnapshotStatusEnum.Ok, result.Status, "the Harbor-side Custom probe answered Ok");
                    AssertNotNull(result.Snapshot, "the snapshot is available");
                    AssertEqual("hbr_split", result.Snapshot!.HarborId, "the snapshot names the Harbor it ran on");
                    AssertEqual("hbr_split", result.Snapshot.HostId, "the snapshot keys on the credential host");
                    AssertEqual(AgentRuntimeEnum.Codex, result.Snapshot.Runtime, "the snapshot records the group's runtime");
                    AssertEqual("cpt_split_a", result.Snapshot.CaptainId, "the triggering captain is recorded");
                    AssertEqual(harborAccounts.DeriveAccountKey(TenantId, AgentRuntimeEnum.Codex, "acct-split-9")!, result.Snapshot.AccountKey, "the account key is the Harbor host's HMAC, not the Admiral's");
                    List<string> members = await testDb.Driver.UsageSnapshots.ReadCaptainsAsync(result.Snapshot.Id, TenantId).ConfigureAwait(false);
                    AssertEqual(2, members.Count, "the snapshot links every member captain");
                    AssertEqual(0, harbor.Runner.Launches.Count, "a usage probe never launches a captain job on the Harbor");
                }
                finally
                {
                    harbor.Dispose();
                }
            }));

            cases.Add(CaseAsync("disconnected_host_fails_with_backoff_and_keeps_last_good", "A disconnected credential host is Failed, keeps its last good data and backs off", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await SeedTenantAsync(testDb.Driver).ConfigureAwait(false);
                LoggingModule logging = CreateLogging();
                HarborConnectionManager manager = new HarborConnectionManager(new HarborService(testDb.Driver, logging), logging, null);

                InProcessHarbor harbor = await InProcessHarbor.ConnectAsync(
                    manager, "hbr_offline", TenantId, null, new AgentRuntimeFactory(logging), new string[] { "git" }, logging,
                    usageProbe: new UsageProbeHostRunner(logging, new UsageAccountResolver("hbr_offline", new string('3', 64)))).ConfigureAwait(false);

                try
                {
                    ArmadaSettings settings = new ArmadaSettings();
                    settings.UsageCapture = new UsageCaptureSettings { Enabled = true, ProbeTimeoutSeconds = 5, MaximumBackoffSeconds = 3600 };

                    FakeTimeProvider clock = new FakeTimeProvider(DateTime.UtcNow);
                    UsageCaptureService service = new UsageCaptureService(
                        testDb.Driver, settings, new UsageAccountResolver("host-offline", new string('4', 64)), new StubExecutor(), manager, logging,
                        clock, jitter: () => 1.0);

                    UsageCaptureGroup group = new UsageCaptureGroup
                    {
                        GroupKey = "offline-group",
                        TenantId = TenantId,
                        HostId = "hbr_offline",
                        HarborId = "hbr_offline",
                        Runtime = AgentRuntimeEnum.ClaudeCode,
                        Mode = UsageCaptureModeEnum.Custom,
                        CommandJson = SplitCommandJson(),
                        CommandFingerprint = "offline-fingerprint",
                        ConfigurationRevision = "hbr_offline|ClaudeCode|offline-fingerprint",
                        EffectiveIntervalSeconds = 300,
                        MemberCaptainIds = new List<string> { "cpt_offline" }
                    };

                    // A first good capture exists before the host goes away.
                    UsageCaptureAttempt good = await service.TryStartAsync(group, null).ConfigureAwait(false);
                    UsageCaptureAttemptResult goodResult = await AwaitAsync(good.Completion).ConfigureAwait(false);
                    AssertTrue(goodResult.Persisted, "the first capture commits while the host is reachable");
                    UsageLatestState? lastGood = await testDb.Driver.UsageSnapshots.ReadLatestStateAsync(TenantId, "hbr_offline", AgentRuntimeEnum.ClaudeCode, goodResult.Snapshot!.AccountKey!).ConfigureAwait(false);
                    AssertNotNull(lastGood, "the account has latest state");
                    AssertEqual(UsageSnapshotStatusEnum.Ok, lastGood!.LastGoodStatus, "the last good state is Ok");

                    // The host goes away: the capture is Failed with HostUnavailable, never a fallback to
                    // the Admiral's credentials, and the last good data is retained. The first success put
                    // the group on its cooldown, so the clock moves to the next eligible time first.
                    await manager.OnDisconnectedAsync("hbr_offline").ConfigureAwait(false);
                    AssertFalse(manager.IsConnected("hbr_offline"), "the host is disconnected");
                    clock.Set(service.NextEligibleUtc(group.GroupKey) ?? clock.Now());

                    UsageCaptureAttempt failed = await service.TryStartAsync(group, null).ConfigureAwait(false);
                    AssertTrue(failed.Started, "the attempt starts while the host is gone");
                    UsageCaptureAttemptResult failedResult = await AwaitAsync(failed.Completion).ConfigureAwait(false);
                    AssertTrue(failedResult.Persisted, "the failure is one persisted result for the attempt");
                    AssertEqual(UsageSnapshotStatusEnum.Failed, failedResult.Status, "a disconnected host is a Failed capture");
                    AssertEqual("HostUnavailable", failedResult.ErrorCode, "the failure names the host");

                    UsageLatestState? retained = await testDb.Driver.UsageSnapshots.ReadLatestStateAsync(TenantId, "hbr_offline", AgentRuntimeEnum.ClaudeCode, goodResult.Snapshot!.AccountKey!).ConfigureAwait(false);
                    AssertNotNull(retained, "the latest state survives the failure");
                    AssertEqual(UsageSnapshotStatusEnum.Ok, retained!.LastGoodStatus, "the last good windows are retained, not overwritten");
                    AssertEqual(UsageSnapshotStatusEnum.Failed, retained.LastErrorStatus, "the latest error is stored separately");
                    AssertEqual("HostUnavailable", retained.LastErrorCode, "the latest error names the host");

                    // Backoff, not a tight retry loop: the failure count grows and the next attempt waits.
                    AssertEqual(1, service.FailureCount(group.GroupKey), "the failure count grows");
                    AssertNotNull(service.NextEligibleUtc(group.GroupKey), "the group is backed off");
                    clock.Set(clock.Now().AddSeconds(1));
                    UsageCaptureAttempt tight = await service.TryStartAsync(group, null).ConfigureAwait(false);
                    AssertFalse(tight.Started, "no tight retry loop re-probes the offline host");
                    AssertEqual("Cooldown", tight.SkippedReason, "the immediate retry is refused by the cooldown");
                }
                finally
                {
                    harbor.Dispose();
                }
            }));

            cases.Add(CaseAsync("dedicated_jobs_visible_and_captain_states_unchanged", "A capture is its own dedicated job; captain states and mission slots never change", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await SeedTenantAsync(testDb.Driver).ConfigureAwait(false);
                LoggingModule logging = CreateLogging();

                Captain captain = new Captain("usage-slot-captain", AgentRuntimeEnum.ClaudeCode)
                {
                    TenantId = TenantId,
                    UsageCaptureEnabled = true,
                    UsageCaptureIntervalSeconds = 300,
                    UsageCaptureMode = UsageCaptureModeEnum.Auto,
                    State = CaptainStateEnum.Idle
                };
                captain = await testDb.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);

                ArmadaSettings settings = new ArmadaSettings();
                settings.UsageCapture = new UsageCaptureSettings { Enabled = true };
                UsageAccountResolver accounts = new UsageAccountResolver("host-slots", new string('5', 64));
                StubExecutor executor = new StubExecutor();
                UsageCaptureService service = new UsageCaptureService(testDb.Driver, settings, accounts, executor, null, logging);

                UsageCaptureGroup group = new UsageCaptureGroup
                {
                    GroupKey = "slots-group",
                    TenantId = TenantId,
                    HostId = "host-slots",
                    Runtime = AgentRuntimeEnum.ClaudeCode,
                    Mode = UsageCaptureModeEnum.Auto,
                    EffectiveIntervalSeconds = 300,
                    MemberCaptainIds = new List<string> { captain.Id }
                };

                UsageCaptureAttempt attempt = await service.TryStartAsync(group, captain.Id).ConfigureAwait(false);
                UsageCaptureAttemptResult result = await AwaitAsync(attempt.Completion).ConfigureAwait(false);
                AssertTrue(result.Persisted, "the capture committed");

                // The dedicated job is visible with its own kind.
                List<Job> jobs = await testDb.Driver.Jobs.EnumerateAsync().ConfigureAwait(false);
                List<Job> captureJobs = jobs.Where(job => job.Kind == JobKindEnum.UsageProbe).ToList();
                AssertEqual(1, captureJobs.Count, "the capture owns one dedicated job");
                AssertEqual(JobStatusEnum.Succeeded, captureJobs[0].Status, "the capture job finished");
                AssertNotNull(captureJobs[0].StartedUtc, "the capture job ran");
                AssertTrue(captureJobs[0].Name.StartsWith("usage-capture:", StringComparison.Ordinal), "the job names the capture group");

                // The captain's state, mission and slot are untouched by capture work.
                Captain? after = await testDb.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false);
                AssertNotNull(after, "the captain is readable");
                AssertEqual(CaptainStateEnum.Idle, after!.State, "a capture never changes a captain's state");
                AssertNull(after.CurrentMissionId, "a capture never borrows a mission");
                AssertNull(after.ProcessId, "a capture never launches a captain process");
                AssertEqual(0, after.RecoveryAttempts, "a capture never touches recovery");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Usage Host Execution",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task SeedTenantAsync(DatabaseDriver database)
        {
            try
            {
                await database.Tenants.CreateAsync(new Armada.Core.Models.TenantMetadata { Id = TenantId, Name = "Usage Host Execution Tenant" }).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The tenant already exists (a server database shared across cases keeps its rows).
            }
        }

        private static string CommandJson(string executable, string[] arguments)
        {
            return "{\"executable\":\"" + executable + "\",\"arguments\":" + System.Text.Json.JsonSerializer.Serialize(arguments) + "}";
        }

        private static string SplitCommandJson()
        {
            string json = "{\"schemaVersion\":1,\"status\":\"Ok\",\"accountKey\":\"acct-split-9\",\"provider\":\"openai\","
                + "\"windows\":[{\"name\":\"primary\",\"usedPercent\":31,\"windowMinutes\":300}]}";
            return "{\"executable\":\"/bin/echo\",\"arguments\":" + System.Text.Json.JsonSerializer.Serialize(new string[] { json }) + "}";
        }

        private static UsageProbeHost NewCustomHost(string hostId, string commandJson)
        {
            return new UsageProbeHost
            {
                TenantId = TenantId,
                HostId = hostId,
                Runtime = AgentRuntimeEnum.ClaudeCode,
                Mode = UsageCaptureModeEnum.Custom,
                CommandJson = commandJson,
                TimeoutSeconds = 10,
                DispatchedUtc = DateTime.UtcNow
            };
        }

        private static string Quote(string path)
        {
            return "'" + path.Replace("'", "'\\''") + "'";
        }

        private static bool IsProcessAlive(int pid)
        {
            try
            {
                Process process = Process.GetProcessById(pid);
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static async Task<UsageCaptureAttemptResult> AwaitAsync(Task<UsageCaptureAttemptResult> completion)
        {
            return await completion.WaitAsync(TimeSpan.FromSeconds(45)).ConfigureAwait(false);
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion

        #region Private-Types

        /// <summary>
        /// The credential host's seam for the Admiral-host path: answers a canned sanitized result without
        /// running anything (the host-side helper is the unit under test elsewhere).
        /// </summary>
        private sealed class StubExecutor : IHostCommandExecutor
        {
            public Task<HostCommandResult> RunAsync(HostCommandRequest request, CancellationToken token = default)
            {
                UsageProbeResult result = new UsageProbeResult
                {
                    Status = UsageSnapshotStatusEnum.Ok,
                    Source = UsageCaptureSourceEnum.OAuthEndpoint,
                    Provider = "anthropic",
                    AccountKey = "acct-admiral-1",
                    Windows = new List<UsageWindow> { new UsageWindow { Name = "five_hour", UsedPercent = 12, WindowMinutes = 300 } }
                };
                return Task.FromResult(new HostCommandResult
                {
                    ExitCode = 0,
                    StandardOutput = result.Serialize(),
                    StandardError = String.Empty
                });
            }
        }

        /// <summary>
        /// A UTC clock a test moves by hand, shared with the service under test.
        /// </summary>
        private sealed class FakeTimeProvider : TimeProvider
        {
            private readonly object _Lock = new object();
            private DateTimeOffset _Now;

            public FakeTimeProvider(DateTime startUtc)
            {
                _Now = new DateTimeOffset(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc), TimeSpan.Zero);
            }

            public DateTime Now()
            {
                return GetUtcNow().UtcDateTime;
            }

            public void Set(DateTime utc)
            {
                lock (_Lock) _Now = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeSpan.Zero);
            }

            public override DateTimeOffset GetUtcNow()
            {
                lock (_Lock) return _Now;
            }
        }

        #endregion
    }
}