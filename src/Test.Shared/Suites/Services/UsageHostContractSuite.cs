namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the account usage host contract (AC2 Services.UsageHostContract), the seams the
    /// scheduler and host execution suites depend on: the Harbor command/job wire names and the
    /// usage-probe capability (the probe kind is collapsed with routine git work in the activity log,
    /// and a serialized probe payload never reaches that log), the host context's tolerance of absent or
    /// malformed payloads and its 1..300 second deadline clamp, the host runner's guard rails (invalid
    /// context, disabled mode, Auto never falling back to PTY, a runtime probe still bounded by the host
    /// deadline and sanitized on the way out), the Custom command's stable failure codes with redacted
    /// stderr, the local executor's missing-helper answer and its round-trip-timeout-to-deadline mapping,
    /// the capture service's null/no-member/global-switch guards with the lease window derived from the
    /// configured timeout, and the runtime capture capability descriptors D4 relies on.
    /// </summary>
    public sealed class UsageHostContractSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.UsageHostContract";
        private const string TenantId = "ten_usage_contract";
        private const string Leak = "Bear" + "er abcdefghijklmnop123456";

        private static readonly JsonSerializerOptions _WireOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the usage host contract suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("usage_probe_wire_names_and_activity_log", "The usage-probe command kind, job kinds and capability name are stable and never log the payload", TestTags.Positive, () =>
            {
                AssertEqual("usage-probe", HarborProtocol.UsageProbeCapability, "the advertised capability name is the protocol constant");

                HarborGitRequest defaults = new HarborGitRequest();
                AssertNull(defaults.Kind, "a request without a kind is routine git work, as an older sender means it");
                AssertNull(defaults.UsageProbeJson, "a request that predates the field carries no probe payload");

                UsageProbeHost payload = new UsageProbeHost
                {
                    TenantId = TenantId,
                    HostId = "host-wire-1",
                    Runtime = AgentRuntimeEnum.Codex,
                    Mode = UsageCaptureModeEnum.Custom,
                    CommandJson = CommandJson("/bin/echo", "{}")
                };
                HarborGitRequest request = new HarborGitRequest
                {
                    RequestId = "req-usage-wire-1",
                    WorkingDirectory = "/repos/wire",
                    Kind = HostCommandKindEnum.UsageProbe,
                    UsageProbeJson = payload.Serialize(),
                    Label = "wire"
                };

                string json = JsonSerializer.Serialize(request);
                HarborGitRequest? roundTrip = JsonSerializer.Deserialize<HarborGitRequest>(json);
                AssertNotNull(roundTrip, "the usage-probe request round-trips through JSON");
                AssertEqual(HostCommandKindEnum.UsageProbe, roundTrip!.Kind!.Value, "the usage-probe kind round-trips");
                AssertEqual(request.UsageProbeJson, roundTrip.UsageProbeJson, "the serialized host context round-trips byte for byte");

                AssertEqual("\"UsageProbe\"", JsonSerializer.Serialize(HostCommandKindEnum.UsageProbe), "the command kind keeps its wire name");
                AssertEqual("\"UsageProbe\"", JsonSerializer.Serialize(JobKindEnum.UsageProbe), "the Jobs kind keeps its wire name");
                AssertEqual("\"UsageProbe\"", JsonSerializer.Serialize(HarborJobKindEnum.UsageProbe), "the Harbor job kind keeps its wire name");

                // The activity log names the probe as its own activity, not as collapsed git work, and never
                // repeats the serialized payload (which carries configuration, not credentials).
                HarborGitRequest leaking = new HarborGitRequest
                {
                    RequestId = "req-usage-wire-2",
                    WorkingDirectory = "/repos/wire",
                    Executable = "/bin/echo",
                    Kind = HostCommandKindEnum.UsageProbe,
                    UsageProbeJson = "{\"tenantId\":\"" + TenantId + "\",\"excerpt\":\"" + Leak + "\"}"
                };
                HarborLogClassifier classifier = new HarborLogClassifier();
                HarborLogEntry probeEntry = classifier.GitRequest(leaking);
                AssertEqual(HarborLogCategoryEnum.Command, probeEntry.Category, "a usage probe is an operator-visible command, not collapsed git work");
                AssertEqual(HarborLogLevelEnum.Summary, probeEntry.Level, "a usage probe is Summary-level activity");
                AssertContains("Usage probe", probeEntry.Summary, "the summary names the usage probe");
                AssertFalse(probeEntry.Summary.Contains("abcdefghijklmnop123456", StringComparison.Ordinal), "the serialized payload never reaches the log summary");
                AssertFalse(probeEntry.Message.Contains("abcdefghijklmnop123456", StringComparison.Ordinal), "the serialized payload never reaches the log message");

                HarborLogEntry done = classifier.GitResult(leaking, new HostCommandResult { ExitCode = 0, StandardOutput = "{}" });
                AssertEqual(HarborLogCategoryEnum.Command, done.Category, "the result keeps the command category");
                AssertEqual(HarborLogOutcomeEnum.Ok, done.Outcome, "a clean probe result is Ok");
                AssertContains("Usage probe", done.Summary, "the result summary names the usage probe");
                AssertContains("exit 0", done.Summary, "the result summary reports the exit code");

                HarborLogEntry timedOut = classifier.GitResult(leaking, new HostCommandResult { ExitCode = -1, TimedOut = true });
                AssertEqual(HarborLogOutcomeEnum.Failed, timedOut.Outcome, "a timed-out probe is a failed result");
                AssertContains("timed out", timedOut.Summary, "the result summary names the timeout");

                HarborGitRequest routine = new HarborGitRequest
                {
                    RequestId = "req-routine",
                    WorkingDirectory = "/repos/wire",
                    Executable = "git",
                    Arguments = new List<string> { "status" }
                };
                HarborLogEntry routineEntry = classifier.GitRequest(routine);
                AssertEqual(HarborLogCategoryEnum.Git, routineEntry.Category, "routine git work stays in the collapsed Git bucket");
                AssertEqual(HarborLogLevelEnum.Detail, routineEntry.Level, "routine git work stays Detail-level");

                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("usage_probe_host_context_tolerates_absent_payloads_and_bounds_timeout", "The host context never throws on absent or malformed payloads and clamps its deadline to 1..300 seconds", TestTags.Negative, () =>
            {
                string?[] payloads = new string?[] { null, String.Empty, "   ", "not-json", "{}", "null", "[]", "{\"timeoutSeconds\":" };
                foreach (string? payload in payloads)
                {
                    UsageProbeHost host = UsageProbeHost.Deserialize(payload);
                    AssertNotNull(host, "an unreadable payload becomes an empty context, never null: " + (payload ?? "<null>"));
                    AssertEqual(String.Empty, host.TenantId, "an empty context has no tenant: " + (payload ?? "<null>"));
                    AssertEqual(String.Empty, host.HostId, "an empty context has no host: " + (payload ?? "<null>"));
                    AssertEqual(UsageCaptureModeEnum.Auto, host.Mode, "an empty context defaults to Auto: " + (payload ?? "<null>"));
                    AssertEqual(20, host.TimeoutSeconds, "an empty context keeps the 20-second default deadline: " + (payload ?? "<null>"));
                }

                AssertEqual(1, UsageProbeHost.Deserialize("{\"timeoutSeconds\":0}").TimeoutSeconds, "a zero deadline clamps up to 1 second");
                AssertEqual(1, UsageProbeHost.Deserialize("{\"timeoutSeconds\":-5}").TimeoutSeconds, "a negative deadline clamps up to 1 second");
                AssertEqual(300, UsageProbeHost.Deserialize("{\"timeoutSeconds\":301}").TimeoutSeconds, "an oversized deadline clamps down to 300 seconds");
                AssertEqual(20, UsageProbeHost.Deserialize("{\"timeoutSeconds\":20}").TimeoutSeconds, "an in-range deadline is kept");

                UsageProbeHost nulls = UsageProbeHost.Deserialize("{\"tenantId\":null,\"hostId\":null,\"commandJson\":null}");
                AssertEqual(String.Empty, nulls.TenantId, "an explicit null tenant normalizes to empty");
                AssertEqual(String.Empty, nulls.HostId, "an explicit null host normalizes to empty");
                AssertNull(nulls.CommandJson, "an explicit null command stays null");

                UsageProbeHost populated = new UsageProbeHost
                {
                    TenantId = TenantId,
                    HostId = "host-wire-2",
                    Mode = UsageCaptureModeEnum.Custom,
                    CommandJson = CommandJson("/bin/echo", "{}")
                };
                string serialized = populated.Serialize();
                AssertContains("\"tenantId\"", serialized, "the wire form is camelCase");
                AssertContains("\"timeoutSeconds\"", serialized, "the wire form carries the deadline");
                AssertFalse(serialized.Contains("credential", StringComparison.OrdinalIgnoreCase), "the host context carries no credential field");
                AssertFalse(serialized.Contains("accountKey", StringComparison.OrdinalIgnoreCase), "the host context carries no account key");
                AssertFalse(serialized.Contains("token", StringComparison.OrdinalIgnoreCase), "the host context carries no token field");

                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("probe_runner_rejects_invalid_context_and_disabled_mode", "The host runner refuses a context without tenant or host identity and a disabled captain without touching a process", TestTags.Negative, async () =>
            {
                UsageProbeHostRunner runner = new UsageProbeHostRunner(CreateLogging(), new UsageAccountResolver("host-contract-3", new string('a', 64)));

                await AssertThrowsAsync<ArgumentNullException>(() => runner.CaptureAsync(null!)).ConfigureAwait(false);

                UsageProbeHost noTenant = new UsageProbeHost { HostId = "host-contract-3" };
                UsageProbeResult noTenantResult = await runner.CaptureAsync(noTenant).ConfigureAwait(false);
                AssertEqual(UsageSnapshotStatusEnum.Failed, noTenantResult.Status, "a context without a tenant is a failed attempt");
                AssertEqual("InvalidProbeContext", noTenantResult.ErrorCode, "the failure names the missing identity");

                UsageProbeHost blankTenant = new UsageProbeHost { TenantId = "   ", HostId = "host-contract-3" };
                AssertEqual("InvalidProbeContext", (await runner.CaptureAsync(blankTenant).ConfigureAwait(false)).ErrorCode, "a whitespace tenant is no identity");

                UsageProbeHost noHost = new UsageProbeHost { TenantId = TenantId };
                UsageProbeResult noHostResult = await runner.CaptureAsync(noHost).ConfigureAwait(false);
                AssertEqual(UsageSnapshotStatusEnum.Failed, noHostResult.Status, "a context without a host is a failed attempt");
                AssertEqual("InvalidProbeContext", noHostResult.ErrorCode, "the failure names the missing host identity");
                AssertNull(noHostResult.AccountKey, "an invalid context never invents an account key");

                UsageProbeHost disabled = new UsageProbeHost
                {
                    TenantId = TenantId,
                    HostId = "host-contract-3",
                    Mode = UsageCaptureModeEnum.Disabled,
                    CommandJson = CommandJson("/bin/echo", "{\"schemaVersion\":1,\"status\":\"Ok\"}")
                };
                UsageProbeResult disabledResult = await runner.CaptureAsync(disabled).ConfigureAwait(false);
                AssertEqual(UsageSnapshotStatusEnum.Failed, disabledResult.Status, "a disabled captain is not captured on the host");
                AssertEqual("CaptureDisabled", disabledResult.ErrorCode, "the failure names the disabled mode");
                AssertNull(disabledResult.AccountKey, "a disabled capture has no account key");
                AssertEqual(0, disabledResult.Windows.Count, "a disabled capture reports no windows");
            }));

            cases.Add(CaseAsync("custom_probe_command_failures_are_stable_and_redacted", "Custom command configuration, start and exit failures keep stable codes and redact stderr", TestTags.Negative, async () =>
            {
                UsageProbeHostRunner runner = new UsageProbeHostRunner(CreateLogging(), new UsageAccountResolver("host-contract-4", new string('b', 64)));

                // Configuration shapes that are not an executable plus arguments.
                string?[] malformed = new string?[]
                {
                    null,
                    String.Empty,
                    "   ",
                    "not-json",
                    "{}",
                    "{\"executable\":\"   \",\"arguments\":[]}",
                    "{\"executable\":\"/bin/echo\",\"arguments\":[\"a\\u0000b\"]}"
                };
                foreach (string? config in malformed)
                {
                    UsageProbeHost host = new UsageProbeHost
                    {
                        TenantId = TenantId,
                        HostId = "host-contract-4",
                        Mode = UsageCaptureModeEnum.Custom,
                        CommandJson = config
                    };
                    UsageProbeResult result = await runner.CaptureAsync(host).ConfigureAwait(false);
                    AssertEqual(UsageSnapshotStatusEnum.Failed, result.Status, "an unusable command configuration is a failed attempt: " + (config ?? "<null>"));
                    AssertEqual("MalformedCommandConfig", result.ErrorCode, "the failure names the command configuration: " + (config ?? "<null>"));
                    AssertNull(result.AccountKey, "a configuration failure never invents an account key");
                }

                // A configured executable that cannot be started is a start failure, not an exception.
                string missing = Path.Combine(TestTemp.NewDirectory("usage_probe_missing"), "no-such-probe-helper");
                UsageProbeResult startFailed = await runner.CaptureAsync(NewCustomHost("host-contract-4", CommandJson(missing))).ConfigureAwait(false);
                AssertEqual(UsageSnapshotStatusEnum.Failed, startFailed.Status, "an executable that cannot start is a failed attempt");
                AssertEqual("CommandStartFailed", startFailed.ErrorCode, "the failure names the start");
                AssertNotNull(startFailed.RawExcerpt, "the start failure carries a diagnostic");
                AssertContains("could not start", startFailed.RawExcerpt!, "the diagnostic names the start failure");

                // A non-zero exit is a command failure whose stderr is redacted and bounded, never raw.
                string shell = "printf '" + Leak + "\\n' >&2; exit 3";
                UsageProbeHost failing = NewCustomHost("host-contract-4", CommandJson("/bin/sh", "-c", shell));
                UsageProbeResult commandFailed = await runner.CaptureAsync(failing).ConfigureAwait(false);
                AssertEqual(UsageSnapshotStatusEnum.Failed, commandFailed.Status, "a non-zero exit is a failed attempt");
                AssertEqual("CommandFailed", commandFailed.ErrorCode, "the failure names the command");
                AssertNotNull(commandFailed.RawExcerpt, "the command failure keeps a bounded diagnostic");
                AssertFalse(commandFailed.RawExcerpt!.Contains("abcdefghijklmnop123456", StringComparison.Ordinal), "the stderr excerpt never keeps the raw secret");
                AssertContains("[REDACTED]", commandFailed.RawExcerpt!, "the stderr excerpt marks the redaction");
                AssertNull(commandFailed.AccountKey, "a command failure never invents an account key");
                AssertEqual(0, commandFailed.Windows.Count, "a command failure reports no windows");
            }));

            cases.Add(CaseAsync("auto_mode_has_no_adapter_never_uses_pty_and_is_deadline_bounded", "Auto mode answers Unsupported without an adapter, never falls back to PTY, and a registered probe is bounded and sanitized", TestTags.Negative, async () =>
            {
                LoggingModule logging = CreateLogging();
                UsageAccountResolver accounts = new UsageAccountResolver("host-contract-5", new string('c', 64));

                // No verified adapter ships in this build: a supported runtime answers Unsupported.
                UsageProbeHostRunner bare = new UsageProbeHostRunner(logging, accounts);
                UsageProbeHost auto = new UsageProbeHost { TenantId = TenantId, HostId = "host-contract-5", Runtime = AgentRuntimeEnum.ClaudeCode };
                UsageProbeResult noAdapter = await bare.CaptureAsync(auto).ConfigureAwait(false);
                AssertEqual(UsageSnapshotStatusEnum.Unsupported, noAdapter.Status, "Auto without an adapter is Unsupported, not Failed");
                AssertEqual("RuntimeProbeUnavailable", noAdapter.ErrorCode, "the answer names the missing adapter");
                AssertContains("no usage probe adapter", noAdapter.RawExcerpt!, "the answer explains the missing adapter");
                AssertEqual(0, noAdapter.Windows.Count, "Unsupported never invents windows");

                // Even with the captain asking for PTY, there is no automatic PTY fallback.
                auto.AllowPty = true;
                UsageProbeResult pty = await bare.CaptureAsync(auto).ConfigureAwait(false);
                AssertEqual(UsageSnapshotStatusEnum.Unsupported, pty.Status, "an opted-in PTY request is still Unsupported without a PTY probe");
                AssertContains("PTY", pty.RawExcerpt!, "the answer mentions the separately opted-in PTY source");

                // A runtime with no verified source answers its own stable code and the capability reason.
                UsageProbeHost cursor = new UsageProbeHost { TenantId = TenantId, HostId = "host-contract-5", Runtime = AgentRuntimeEnum.Cursor };
                UsageProbeResult cursorResult = await bare.CaptureAsync(cursor).ConfigureAwait(false);
                AssertEqual(UsageSnapshotStatusEnum.Unsupported, cursorResult.Status, "Cursor has no verified source");
                AssertEqual("UnsupportedCursor", cursorResult.ErrorCode, "the answer carries the runtime-specific code");
                AssertContains("no verified account limit interface", cursorResult.RawExcerpt!, "the answer carries the capability reason");

                // A registered runtime probe is still bounded by the host execution deadline.
                UsageProbeHostRunner slowRunner = new UsageProbeHostRunner(logging, accounts, runtime => runtime == AgentRuntimeEnum.Codex ? new DelayedProbe(TimeSpan.FromSeconds(30)) : null);
                UsageProbeHost slowHost = new UsageProbeHost { TenantId = TenantId, HostId = "host-contract-5", Runtime = AgentRuntimeEnum.Codex, TimeoutSeconds = 1 };
                Stopwatch watch = Stopwatch.StartNew();
                UsageProbeResult slow = await slowRunner.CaptureAsync(slowHost).ConfigureAwait(false);
                watch.Stop();
                AssertEqual(UsageSnapshotStatusEnum.Failed, slow.Status, "a runtime probe past the deadline is a failed attempt");
                AssertEqual("ProbeTimeout", slow.ErrorCode, "the failure names the deadline");
                AssertTrue(watch.Elapsed < TimeSpan.FromSeconds(10), "the host deadline bounds a runtime probe, not the probe's own patience");
                AssertNull(slow.AccountKey, "a timed-out probe has no account key");

                // A probe that returns nothing is a failure, and a probe's diagnostics are sanitized.
                UsageProbeHostRunner nullRunner = new UsageProbeHostRunner(logging, accounts, runtime => new NullProbe());
                UsageProbeHost nullHost = new UsageProbeHost { TenantId = TenantId, HostId = "host-contract-5", Runtime = AgentRuntimeEnum.Codex };
                UsageProbeResult nothing = await nullRunner.CaptureAsync(nullHost).ConfigureAwait(false);
                AssertEqual(UsageSnapshotStatusEnum.Failed, nothing.Status, "a probe that returns nothing is a failed attempt");
                AssertEqual("ProbeFailed", nothing.ErrorCode, "the failure names the empty probe");

                UsageProbeHostRunner sanitizing = new UsageProbeHostRunner(logging, accounts, runtime => new SanitizingProbe());
                UsageProbeResult sanitized = await sanitizing.CaptureAsync(nullHost).ConfigureAwait(false);
                AssertEqual(UsageSnapshotStatusEnum.Ok, sanitized.Status, "the sanitized probe result is Ok");
                AssertEqual("anthropic", sanitized.Provider, "the provider is trimmed");
                AssertFalse(sanitized.RawExcerpt!.Contains("abcdefghijklmnop123456", StringComparison.Ordinal), "the probe excerpt never keeps the raw secret");
                AssertContains("[REDACTED]", sanitized.RawExcerpt!, "the probe excerpt marks the redaction");
            }));

            cases.Add(CaseAsync("local_executor_helper_gate_and_deadline_mapping", "The local executor answers Unsupported without a helper, maps the round-trip timeout to the host deadline, and never throws on a probe fault", TestTags.Negative, async () =>
            {
                UsageProbeHost payload = new UsageProbeHost { TenantId = TenantId, HostId = "host-contract-6", TimeoutSeconds = 7 };

                LocalHostCommandExecutor bare = new LocalHostCommandExecutor();
                HostCommandResult unavailable = await bare.RunAsync(new HostCommandRequest
                {
                    Kind = HostCommandKindEnum.UsageProbe,
                    UsageProbeJson = payload.Serialize()
                }).ConfigureAwait(false);
                AssertEqual(0, unavailable.ExitCode, "a host without the helper still answers, with exit 0");
                AssertFalse(unavailable.TimedOut, "a host without the helper did not time out");
                UsageProbeResult unavailableResult = UsageProbeResult.Deserialize(unavailable.StandardOutput);
                AssertEqual(UsageSnapshotStatusEnum.Unsupported, unavailableResult.Status, "the missing helper is Unsupported, not Failed");
                AssertEqual("ProbeHelperUnavailable", unavailableResult.ErrorCode, "the answer names the missing helper");
                AssertContains("update it", unavailableResult.RawExcerpt!, "the answer tells the operator to update the host");

                // The Admiral's round-trip timeout becomes the host-side execution deadline; 0 keeps the payload's.
                RecordingProbe recording = new RecordingProbe();
                LocalHostCommandExecutor wired = new LocalHostCommandExecutor { UsageProbe = recording };
                HostCommandResult mapped = await wired.RunAsync(new HostCommandRequest
                {
                    Kind = HostCommandKindEnum.UsageProbe,
                    UsageProbeJson = payload.Serialize(),
                    TimeoutMs = 45000
                }).ConfigureAwait(false);
                AssertEqual(45, recording.LastHost!.TimeoutSeconds, "the round-trip timeout maps to a 45-second host deadline");
                AssertEqual(UsageSnapshotStatusEnum.Ok, UsageProbeResult.Deserialize(mapped.StandardOutput).Status, "the wired helper's result round-trips");

                HostCommandResult unmapped = await wired.RunAsync(new HostCommandRequest
                {
                    Kind = HostCommandKindEnum.UsageProbe,
                    UsageProbeJson = payload.Serialize(),
                    TimeoutMs = 0
                }).ConfigureAwait(false);
                AssertEqual(7, recording.LastHost!.TimeoutSeconds, "a zero round-trip timeout keeps the payload deadline");

                // A helper that throws is a Failed answer with a redacted diagnostic, never an exception.
                ThrowingProbe throwing = new ThrowingProbe();
                wired.UsageProbe = throwing;
                HostCommandResult fault = await wired.RunAsync(new HostCommandRequest
                {
                    Kind = HostCommandKindEnum.UsageProbe,
                    UsageProbeJson = payload.Serialize()
                }).ConfigureAwait(false);
                AssertEqual(0, fault.ExitCode, "a helper fault is reported, not thrown");
                UsageProbeResult faultResult = UsageProbeResult.Deserialize(fault.StandardOutput);
                AssertEqual(UsageSnapshotStatusEnum.Failed, faultResult.Status, "a helper fault is a failed attempt");
                AssertEqual("ProbeFailed", faultResult.ErrorCode, "the failure names the helper");
                AssertEqual("ProbeFailed", fault.StandardError, "the error channel carries the stable code, not the exception");
                AssertFalse(fault.StandardError.Contains("abcdefghijklmnop123456", StringComparison.Ordinal), "the error channel never carries the raw secret");
                AssertFalse(faultResult.RawExcerpt!.Contains("abcdefghijklmnop123456", StringComparison.Ordinal), "the diagnostic never carries the raw secret");
                AssertContains("[REDACTED]", faultResult.RawExcerpt!, "the diagnostic marks the redaction");
            }));

            cases.Add(CaseAsync("capture_service_guards_and_lease_window", "The capture service guards null and memberless groups, derives its lease window from the timeout, and does no work while disabled", TestTags.Negative, async () =>
            {
                using TestDatabase database = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                settings.UsageCapture = new UsageCaptureSettings { Enabled = true };
                UsageAccountResolver accounts = new UsageAccountResolver("host-contract-7", new string('d', 64));
                UsageCaptureService service = new UsageCaptureService(database.Driver, settings, accounts, new NoopExecutor(), null, CreateLogging());

                await AssertThrowsAsync<ArgumentNullException>(() => service.TryStartAsync(null!, null)).ConfigureAwait(false);
                AssertThrows<ArgumentNullException>(() => service.ScheduleInitial(null!), "scheduling a null group is a caller bug");

                AssertEqual(TimeSpan.FromSeconds(70), service.LeaseWindow, "the default 20-second deadline gives a 2 x 35-second lease window");
                settings.UsageCapture.ProbeTimeoutSeconds = 1;
                AssertEqual(TimeSpan.FromSeconds(32), service.LeaseWindow, "the lease window follows the configured deadline; it is not a fixed constant");
                settings.UsageCapture.ProbeTimeoutSeconds = 20;

                UsageCaptureGroup memberless = new UsageCaptureGroup
                {
                    GroupKey = "group-memberless",
                    TenantId = TenantId,
                    HostId = "host-contract-7"
                };
                UsageCaptureAttempt skipped = await service.TryStartAsync(memberless, null).ConfigureAwait(false);
                AssertFalse(skipped.Started, "a memberless group starts nothing");
                AssertFalse(skipped.JoinedExisting, "a memberless group joins nothing");
                AssertNull(skipped.JobId, "a memberless group gets no job");
                AssertEqual("NoMembers", skipped.SkippedReason, "the skip names the missing members");
                AssertEqual(0, (await database.Driver.Jobs.EnumerateAsync().ConfigureAwait(false)).Count, "a memberless group enqueues no job row");
                AssertFalse(service.IsInFlight(memberless.GroupKey), "a memberless group has no flight");

                settings.UsageCapture.Enabled = false;
                UsageCaptureAttempt disabled = await service.TryStartAsync(memberless, null).ConfigureAwait(false);
                AssertEqual("CaptureDisabled", disabled.SkippedReason, "the server switch is checked before anything else");
                AssertNull(disabled.JobId, "a disabled capture gets no job");

                // With the switch off, a tick does not even rebuild groups; re-enabling it does.
                await database.Driver.Tenants.CreateAsync(new TenantMetadata { Id = TenantId, Name = "Usage Host Contract Tenant" }).ConfigureAwait(false);
                Captain captain = new Captain("contract-captain", AgentRuntimeEnum.ClaudeCode)
                {
                    TenantId = TenantId,
                    UsageCaptureEnabled = true,
                    UsageCaptureIntervalSeconds = 300
                };
                await database.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);

                UsageCaptureScheduler scheduler = new UsageCaptureScheduler(service, database.Driver, accounts, settings, null, CreateLogging());
                List<UsageCaptureAttempt> disabledTicks = await scheduler.TickAsync().ConfigureAwait(false);
                AssertEqual(0, disabledTicks.Count, "a disabled switch starts no attempts");
                AssertEqual(0, scheduler.Groups().Count, "a disabled switch does not even rebuild groups");
                AssertEqual(0, (await database.Driver.Jobs.EnumerateAsync().ConfigureAwait(false)).Count, "a disabled switch enqueues no job");

                settings.UsageCapture.Enabled = true;
                await scheduler.RebuildAsync().ConfigureAwait(false);
                AssertEqual(1, scheduler.Groups().Count, "the enabled captain forms one group");
                AssertEqual(0, scheduler.UnresolvedCaptains().Count, "the local captain resolves the Admiral host");
                AssertEqual(0, (await database.Driver.Jobs.EnumerateAsync().ConfigureAwait(false)).Count, "a rebuild alone enqueues no job");

                // A Harbor that never advertised the capability is not assumed to have it.
                HarborConnectionManager manager = new HarborConnectionManager(new HarborService(database.Driver, CreateLogging()), CreateLogging(), null);
                AssertFalse(manager.HostsUsageProbes("hbr_never_connected"), "an unknown Harbor does not host usage probes");
                AssertFalse(manager.HostsUsageProbes(String.Empty), "an empty Harbor id does not host usage probes");
                AssertFalse(manager.HostsUsageProbes(null!), "a null Harbor id does not host usage probes");
            }));

            cases.Add(CaseAsync("runtime_capture_capability_descriptors", "The runtime capture capability descriptors declare the verified sources and their unsupported reasons", TestTags.Positive, () =>
            {
                AssertEqual(UsageCaptureKindEnum.Active, AgentRuntimeCapabilities.UsageCapture(AgentRuntimeEnum.ClaudeCode).Kind, "Claude Code captures actively");
                AssertEqual(UsageCaptureKindEnum.Active, AgentRuntimeCapabilities.UsageCapture(AgentRuntimeEnum.Codex).Kind, "Codex captures actively");
                AssertEqual(UsageCaptureKindEnum.Active, AgentRuntimeCapabilities.UsageCapture(AgentRuntimeEnum.Custom).Kind, "a Custom executable captures actively");
                AssertEqual(UsageCaptureKindEnum.Passive, AgentRuntimeCapabilities.UsageCapture(AgentRuntimeEnum.ApiEndpoint).Kind, "an API endpoint captures passively");
                AssertEqual(UsageCaptureKindEnum.Estimated, AgentRuntimeCapabilities.UsageCapture(AgentRuntimeEnum.Gemini).Kind, "Gemini captures an estimate");

                AgentRuntimeEnum[] unsupported = new AgentRuntimeEnum[]
                {
                    AgentRuntimeEnum.Cursor,
                    AgentRuntimeEnum.Mux,
                    AgentRuntimeEnum.OpenCode,
                    AgentRuntimeEnum.Pi
                };
                foreach (AgentRuntimeEnum runtime in unsupported)
                {
                    AssertFalse(AgentRuntimeCapabilities.SupportsUsageCapture(runtime), runtime + " has no verified capture source");
                    UsageCaptureCapability capability = AgentRuntimeCapabilities.UsageCapture(runtime);
                    AssertEqual(UsageCaptureKindEnum.Unsupported, capability.Kind, runtime + " is declared Unsupported");
                    AssertNotNull(capability.Reason, runtime + " names why it is unsupported");
                    AssertFalse(String.IsNullOrWhiteSpace(capability.Reason), runtime + " has a non-blank reason");
                }

                foreach (UsageCaptureCapability capability in AgentRuntimeCapabilities.UsageCaptureCapabilities)
                {
                    if (!capability.Supported) continue;
                    AssertNull(capability.Reason, "a supported capability carries no refusal reason: " + capability.Runtime);
                }

                UsageCaptureCapability unknown = AgentRuntimeCapabilities.UsageCapture((AgentRuntimeEnum)9999);
                AssertFalse(unknown.Supported, "an unknown runtime is not silently supported");
                AssertFalse(String.IsNullOrWhiteSpace(unknown.Reason), "an unknown runtime has an operator-facing reason");
                AssertFalse(AgentRuntimeCapabilities.SupportsUsageCapture((AgentRuntimeEnum)9999), "an unknown runtime never pretends to capture");

                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("production_harbor_supplies_the_usage_probe_helper", "The production Harbor link composes a persistent usage helper and hands it to its link client", TestTags.Positive, () =>
            {
                string source = File.ReadAllText(Path.Combine(DashboardSource.RepoRoot(), "src", "Armada.Harbor", "HarborLinkHost.cs"));

                // The helper is composed once for the host's lifetime, from the Harbor's own persisted identity
                // (the settings directory and this Harbor's id), before the session loop: rebuilding it per
                // session would give every session a different account, and omitting it would stop an
                // upgraded Harbor from advertising or executing usage probes at all.
                int setup = source.IndexOf("if (_JobRunner == null", StringComparison.Ordinal);
                int sessionLoop = source.IndexOf("while (!token.IsCancellationRequested)", StringComparison.Ordinal);
                int helper = source.IndexOf("new Armada.Runtimes.UsageProbeHostRunner(", StringComparison.Ordinal);
                AssertTrue(setup >= 0, "the link host has a once-only setup block");
                AssertTrue(sessionLoop > setup, "the session loop follows the setup block");
                AssertTrue(helper > setup && helper < sessionLoop, "the usage helper is composed once, before the session loop");

                AssertEqual(1, CountOccurrences(source, "new Armada.Runtimes.UsageProbeHostRunner("), "the helper is composed once, never per session");
                AssertTrue(source.Contains("new Armada.Core.Services.UsageAccountResolver(", StringComparison.Ordinal), "the helper is built with an account resolver");
                AssertTrue(source.Contains("HarborAppSettings.SettingsDirectory()", StringComparison.Ordinal), "the resolver persists the Harbor's host identity in its settings directory");
                AssertTrue(source.Contains("_Settings.HarborId", StringComparison.Ordinal), "the resolver names this Harbor as its host identity");
                AssertTrue(source.Contains("_Executor.UsageProbe = _UsageProbeRunner;", StringComparison.Ordinal), "the local host-command seam carries the helper too");
                AssertTrue(source.Contains("usageProbe: usageProbeRunner", StringComparison.Ordinal), "the link client is constructed with the helper, which advertises the capability and runs the probes");

                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("production_server_ticks_the_scheduler_while_capture_is_disabled", "The production health loop ticks the usage scheduler even while capture is disabled, so the switch cancels in-flight work", TestTags.Positive, () =>
            {
                string source = File.ReadAllText(Path.Combine(DashboardSource.RepoRoot(), "src", "Armada.Server", "ArmadaServer.cs"));

                // The tick must not be gated on the switch: a tick that observes capture turned off is the
                // production path that invalidates the generation and cancels the probes still in flight.
                // A call skipped while capture is disabled leaves the queued and running work alive until
                // the process restarts, and a late answer is only discarded at commit time.
                AssertEqual(1, CountOccurrences(source, "_UsageCaptureScheduler.TickAsync("), "the scheduler is ticked from exactly one production site");

                int loopStart = source.IndexOf("private async Task HealthCheckLoopAsync", StringComparison.Ordinal);
                AssertTrue(loopStart >= 0, "the health check loop exists");
                string loop = source.Substring(loopStart);
                int tick = loop.IndexOf("_UsageCaptureScheduler.TickAsync(", StringComparison.Ordinal);
                AssertTrue(tick > 0, "the health check loop ticks the usage capture scheduler");

                AssertTrue(source.Contains("await _UsageCaptureScheduler.TickAsync(token).ConfigureAwait(false);", StringComparison.Ordinal), "the tick is awaited on the health loop's token");

                // The statement's own guard scope: nothing between the tick's try block and the call may
                // consult the capture switch.
                int windowStart = Math.Max(0, tick - 400);
                string preceding = loop.Substring(windowStart, tick - windowStart);
                AssertFalse(preceding.Contains("UsageCapture.Enabled", StringComparison.Ordinal), "the tick is not skipped while UsageCapture.Enabled is false");

                // The scheduler is the component that decides what a disabled switch means: its own
                // disabled branch invalidates the generation once, on the transition, and starts nothing.
                string schedulerSource = File.ReadAllText(Path.Combine(DashboardSource.RepoRoot(), "src", "Armada.Core", "Services", "UsageCaptureScheduler.cs"));
                AssertTrue(schedulerSource.Contains("if (!_Settings.UsageCapture.Enabled)", StringComparison.Ordinal), "the scheduler owns the disabled-switch branch");

                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Usage Capture Host Contract",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static string CommandJson(string executable, params string[] arguments)
        {
            CustomCommandConfig config = new CustomCommandConfig
            {
                Executable = executable,
                Arguments = new List<string>(arguments)
            };
            return JsonSerializer.Serialize(config, _WireOptions);
        }

        private static UsageProbeHost NewCustomHost(string hostId, string? commandJson)
        {
            return new UsageProbeHost
            {
                TenantId = TenantId,
                HostId = hostId,
                Mode = UsageCaptureModeEnum.Custom,
                CommandJson = commandJson,
                TimeoutSeconds = 10
            };
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

        /// <summary>
        /// How many times a needle occurs in a source file, for once-only construction contracts.
        /// </summary>
        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0;
            int index = 0;
            while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }

        #endregion

        #region Private-Types

        /// <summary>
        /// The Custom command configuration the host accepts: an executable and an argument list.
        /// </summary>
        private sealed class CustomCommandConfig
        {
            public string Executable { get; set; } = String.Empty;
            public List<string> Arguments { get; set; } = new List<string>();
        }

        /// <summary>
        /// An executor that should never be reached by the guarded cases.
        /// </summary>
        private sealed class NoopExecutor : IHostCommandExecutor
        {
            public Task<HostCommandResult> RunAsync(HostCommandRequest request, CancellationToken token = default)
            {
                return Task.FromResult(new HostCommandResult { ExitCode = 0, StandardOutput = String.Empty, StandardError = String.Empty });
            }
        }

        /// <summary>
        /// A probe that never produces an observation.
        /// </summary>
        private sealed class NullProbe : IUsageProbe
        {
            public Task<UsageProbeResult> CaptureAsync(UsageProbeHost host, CancellationToken token = default)
            {
                return Task.FromResult<UsageProbeResult>(null!);
            }
        }

        /// <summary>
        /// A probe that outlives any sane deadline.
        /// </summary>
        private sealed class DelayedProbe : IUsageProbe
        {
            private readonly TimeSpan _Delay;

            public DelayedProbe(TimeSpan delay)
            {
                _Delay = delay;
            }

            public async Task<UsageProbeResult> CaptureAsync(UsageProbeHost host, CancellationToken token = default)
            {
                await Task.Delay(_Delay, token).ConfigureAwait(false);
                return new UsageProbeResult { Status = UsageSnapshotStatusEnum.Ok, Source = UsageCaptureSourceEnum.OAuthEndpoint };
            }
        }

        /// <summary>
        /// A probe whose diagnostics contain a secret-shaped value.
        /// </summary>
        private sealed class SanitizingProbe : IUsageProbe
        {
            public Task<UsageProbeResult> CaptureAsync(UsageProbeHost host, CancellationToken token = default)
            {
                return Task.FromResult(new UsageProbeResult
                {
                    Status = UsageSnapshotStatusEnum.Ok,
                    Source = UsageCaptureSourceEnum.OAuthEndpoint,
                    Provider = "  anthropic  ",
                    RawExcerpt = Leak,
                    Windows = new List<UsageWindow>()
                });
            }
        }

        /// <summary>
        /// A probe that records the host context it was handed and answers Ok.
        /// </summary>
        private sealed class RecordingProbe : IUsageProbe
        {
            public UsageProbeHost? LastHost { get; private set; } = null;

            public Task<UsageProbeResult> CaptureAsync(UsageProbeHost host, CancellationToken token = default)
            {
                LastHost = host;
                return Task.FromResult(new UsageProbeResult
                {
                    Status = UsageSnapshotStatusEnum.Ok,
                    Source = UsageCaptureSourceEnum.Custom,
                    Provider = "anthropic",
                    Windows = new List<UsageWindow>()
                });
            }
        }

        /// <summary>
        /// A probe that crashes with a secret-shaped diagnostic.
        /// </summary>
        private sealed class ThrowingProbe : IUsageProbe
        {
            public Task<UsageProbeResult> CaptureAsync(UsageProbeHost host, CancellationToken token = default)
            {
                throw new InvalidOperationException("probe crashed with " + Leak);
            }
        }

        #endregion
    }
}
