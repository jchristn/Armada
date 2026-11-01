namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Protocol;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// AC3 Services.UsageQuarantine, decision D6: only fresh authoritative exhausted windows for the captain's
    /// own account affect routing; estimated, stale, failed, unsupported, missing-auth and wrong-account
    /// observations never hold or release; per-model windows only affect captains on that model; a passed reset
    /// expires; CaptainService only ever extends or clears a quota-owned hold (manual, health and provider
    /// holds survive), never interrupts a working captain, and refuses older samples by compare-and-set; and a
    /// rejected observation committed through the real capture service preserves the account's last-good data.
    /// </summary>
    public sealed class UsageQuarantineSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.UsageQuarantine";
        private const string TenantId = "ten_usage_quarantine";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("only_fresh_authoritative_exhausted_windows_hold", "Ok fresh exhausted windows hold; estimated, failed, unsupported, missing-auth, stale and future observations never route", TestTags.Positive, () =>
            {
                DateTime now = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
                Captain captain = NewCaptain("opus");
                UsageProbeResult exhausted = Observation(now, Window("five_hour", 100, now.AddHours(2)), Window("seven_day", 40, now.AddDays(3)));
                UsageQuotaDecision hold = UsageQuotaPolicy.Evaluate(captain, exhausted, "acct-1", 300, now);
                AssertEqual(UsageQuotaActionEnum.Hold, hold.Action, "a fresh authoritative exhausted window holds");
                AssertEqual(now.AddHours(2), hold.HoldUntilUtc, "the hold lasts until the exhausted window resets");
                AssertEqual("five_hour", hold.ExhaustedWindows[0], "the exhausted window is named");
                AssertEqual(now, hold.ObservedUtc, "the decision carries the observation time for compare-and-set");

                foreach (UsageSnapshotStatusEnum status in new[] { UsageSnapshotStatusEnum.Estimated, UsageSnapshotStatusEnum.Failed, UsageSnapshotStatusEnum.Unsupported, UsageSnapshotStatusEnum.AuthMissing })
                {
                    UsageProbeResult other = Observation(now, Window("five_hour", 100, now.AddHours(2)));
                    other.Status = status;
                    AssertEqual(UsageQuotaActionEnum.NoChange, UsageQuotaPolicy.Evaluate(captain, other, "acct-1", 300, now).Action, status + " never quarantines");
                    UsageProbeResult low = Observation(now, Window("five_hour", 10, now.AddHours(2)));
                    low.Status = status;
                    AssertEqual(UsageQuotaActionEnum.NoChange, UsageQuotaPolicy.Evaluate(captain, low, "acct-1", 300, now).Action, status + " never releases");
                }

                UsageProbeResult derived = Observation(now, Window("five_hour", 100, now.AddHours(2)));
                derived.Source = UsageCaptureSourceEnum.Derived;
                AssertEqual("Estimated", UsageQuotaPolicy.Evaluate(captain, derived, "acct-1", 300, now).ReasonCode, "a Derived estimate never routes even when labeled Ok");

                // Freshness horizon: max(2 x interval, 120 seconds).
                AssertEqual(TimeSpan.FromSeconds(600), UsageQuotaPolicy.FreshnessHorizon(300), "twice the interval");
                AssertEqual(TimeSpan.FromSeconds(120), UsageQuotaPolicy.FreshnessHorizon(30), "the 120 second floor");
                UsageProbeResult stale = Observation(now.AddSeconds(-601), Window("five_hour", 100, now.AddHours(2)));
                AssertEqual("Stale", UsageQuotaPolicy.Evaluate(captain, stale, "acct-1", 300, now).ReasonCode, "an observation older than the horizon is stale");
                UsageProbeResult edge = Observation(now.AddSeconds(-599), Window("five_hour", 100, now.AddHours(2)));
                AssertEqual(UsageQuotaActionEnum.Hold, UsageQuotaPolicy.Evaluate(captain, edge, "acct-1", 300, now).Action, "an observation inside the horizon is fresh");
                UsageProbeResult future = Observation(now.AddMinutes(10), Window("five_hour", 100, now.AddHours(2)));
                AssertEqual("FutureObservation", UsageQuotaPolicy.Evaluate(captain, future, "acct-1", 300, now).ReasonCode, "a future-dated observation is refused");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("per_model_windows_wrong_account_null_windows_and_passed_resets", "Model windows only affect that model; wrong account, null windows and passed resets never hold", TestTags.Negative, () =>
            {
                DateTime now = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
                UsageWindow opusWindow = Window("seven_day_opus", 100, now.AddDays(2));
                opusWindow.Model = "opus";
                UsageProbeResult modelExhausted = Observation(now, Window("five_hour", 50, now.AddHours(1)), opusWindow);
                AssertEqual(UsageQuotaActionEnum.Hold, UsageQuotaPolicy.Evaluate(NewCaptain("opus"), modelExhausted, null, 300, now).Action, "the opus captain is held by the opus window");
                AssertEqual(UsageQuotaActionEnum.Release, UsageQuotaPolicy.Evaluate(NewCaptain("sonnet"), modelExhausted, null, 300, now).Action, "a sonnet captain is not held by an opus window");
                AssertEqual(UsageQuotaActionEnum.Release, UsageQuotaPolicy.Evaluate(NewCaptain(null), modelExhausted, null, 300, now).Action, "a captain with no known model is not held by a model window");

                UsageProbeResult accountWide = Observation(now, Window("five_hour", 100, now.AddHours(1)));
                accountWide.AccountKey = "acct-other";
                AssertEqual("WrongAccount", UsageQuotaPolicy.Evaluate(NewCaptain("opus"), accountWide, "acct-mine", 300, now).ReasonCode, "another account's observation never routes this captain");
                accountWide.AccountKey = "acct-mine";
                AssertEqual(UsageQuotaActionEnum.Hold, UsageQuotaPolicy.Evaluate(NewCaptain("sonnet"), accountWide, "acct-mine", 300, now).Action, "an account-wide window holds every member of that account");

                UsageProbeResult nullWindows = Observation(now, new UsageWindow { Name = "five_hour", UsedPercent = null });
                AssertEqual("NoApplicableWindows", UsageQuotaPolicy.Evaluate(NewCaptain("opus"), nullWindows, null, 300, now).ReasonCode, "null percentages neither hold nor release");
                UsageProbeResult empty = Observation(now);
                AssertEqual(UsageQuotaActionEnum.NoChange, UsageQuotaPolicy.Evaluate(NewCaptain("opus"), empty, null, 300, now).Action, "no windows neither hold nor release");

                UsageProbeResult passed = Observation(now, Window("five_hour", 100, now.AddMinutes(-1)), Window("seven_day", 30, now.AddDays(2)));
                AssertEqual(UsageQuotaActionEnum.Release, UsageQuotaPolicy.Evaluate(NewCaptain("opus"), passed, null, 300, now).Action, "a passed reset expires that window's hold");
                UsageProbeResult weekly = Observation(now, Window("five_hour", 100, now.AddMinutes(-1)), Window("seven_day", 100, now.AddDays(2)));
                UsageQuotaDecision weeklyHold = UsageQuotaPolicy.Evaluate(NewCaptain("opus"), weekly, null, 300, now);
                AssertEqual(UsageQuotaActionEnum.Hold, weeklyHold.Action, "a passed five-hour reset does not erase an active weekly limit");
                AssertEqual(now.AddDays(2), weeklyHold.HoldUntilUtc, "the weekly reset bounds the hold");

                UsageProbeResult noReset = Observation(now, new UsageWindow { Name = "five_hour", UsedPercent = 100 });
                AssertEqual(now.AddSeconds(600), UsageQuotaPolicy.Evaluate(NewCaptain("opus"), noReset, null, 300, now).HoldUntilUtc, "without a reported reset the hold lasts only until the next fresh read");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("captain_service_only_touches_quota_owned_holds", "Holds move only idle captains; manual, health and provider holds survive; working captains are never interrupted", TestTags.Positive, async () =>
            {
                using TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                CaptainService service = await CreateCaptainServiceAsync(db).ConfigureAwait(false);
                DateTime now = DateTime.UtcNow;
                UsageQuotaDecision hold = UsageQuotaPolicy.Evaluate(NewCaptain(null), Observation(now, Window("five_hour", 100, now.AddHours(2))), null, 300, now);
                UsageQuotaDecision release = UsageQuotaPolicy.Evaluate(NewCaptain(null), Observation(now.AddSeconds(30), Window("five_hour", 20, now.AddHours(2))), null, 300, now.AddSeconds(30));

                Captain idle = await CreateCaptainAsync(db, "quota-idle", CaptainStateEnum.Idle, null).ConfigureAwait(false);
                AssertEqual(UsageQuotaOutcomeEnum.Held, await service.ApplyUsageQuotaAsync(idle.Id, hold).ConfigureAwait(false), "an idle captain is held");
                Captain held = (await db.Driver.Captains.ReadAsync(idle.Id).ConfigureAwait(false))!;
                AssertEqual(CaptainStateEnum.Quarantined, held.State, "the held captain is out of dispatch");
                AssertTrue(UsageQuotaHold.IsQuotaOwned(held.QuarantineReason), "the hold is quota-owned: " + held.QuarantineReason);
                AssertEqual(hold.HoldUntilUtc!.Value.ToString("yyyy-MM-ddTHH:mm:ss"), held.QuarantineUntilUtc!.Value.ToString("yyyy-MM-ddTHH:mm:ss"), "the hold lasts until the reset");
                AssertEqual(UsageQuotaOutcomeEnum.Released, await service.ApplyUsageQuotaAsync(idle.Id, release).ConfigureAwait(false), "a newer below-limit observation releases the quota hold");
                Captain released = (await db.Driver.Captains.ReadAsync(idle.Id).ConfigureAwait(false))!;
                AssertEqual(CaptainStateEnum.Idle, released.State, "the released captain is idle");
                AssertNull(released.QuarantineReason, "the quota reason is cleared");
                AssertNull(released.QuarantineUntilUtc, "the quota window is cleared");

                foreach (string reason in new[] { "operator hold", "provider usage limit", "crash loop", "configuration: bad model" })
                {
                    Captain foreign = await CreateCaptainAsync(db, "quota-foreign-" + reason.Length, CaptainStateEnum.Quarantined, reason).ConfigureAwait(false);
                    AssertEqual(UsageQuotaOutcomeEnum.SkippedForeignHold, await service.ApplyUsageQuotaAsync(foreign.Id, release).ConfigureAwait(false), "a release never clears '" + reason + "'");
                    AssertEqual(UsageQuotaOutcomeEnum.SkippedForeignHold, await service.ApplyUsageQuotaAsync(foreign.Id, hold).ConfigureAwait(false), "a hold never overwrites '" + reason + "'");
                    Captain after = (await db.Driver.Captains.ReadAsync(foreign.Id).ConfigureAwait(false))!;
                    AssertEqual(CaptainStateEnum.Quarantined, after.State, "'" + reason + "' still holds the captain");
                    AssertEqual(reason, after.QuarantineReason, "'" + reason + "' is unchanged");
                }

                Captain working = await CreateCaptainAsync(db, "quota-working", CaptainStateEnum.Working, null).ConfigureAwait(false);
                AssertEqual(UsageQuotaOutcomeEnum.SkippedBusy, await service.ApplyUsageQuotaAsync(working.Id, hold).ConfigureAwait(false), "a working captain is never interrupted");
                AssertEqual(CaptainStateEnum.Working, (await db.Driver.Captains.ReadAsync(working.Id).ConfigureAwait(false))!.State, "the working captain keeps working");

                Captain plain = await CreateCaptainAsync(db, "quota-plain", CaptainStateEnum.Idle, null).ConfigureAwait(false);
                AssertEqual(UsageQuotaOutcomeEnum.Unchanged, await service.ApplyUsageQuotaAsync(plain.Id, release).ConfigureAwait(false), "a release without a quota hold changes nothing");
                AssertEqual(UsageQuotaOutcomeEnum.Unchanged, await service.ApplyUsageQuotaAsync(plain.Id, UsageQuotaDecision.NoChange("Stale", now)).ConfigureAwait(false), "NoChange changes nothing");
                AssertEqual(UsageQuotaOutcomeEnum.NotFound, await service.ApplyUsageQuotaAsync("cpt_missing_quota", hold).ConfigureAwait(false), "an unknown captain is reported");
            }));

            cases.Add(CaseAsync("captain_service_compare_and_set_refuses_older_samples", "A late older low sample cannot release, and an older exhausted sample cannot replace a newer hold", TestTags.Reliability, async () =>
            {
                using TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                CaptainService service = await CreateCaptainServiceAsync(db).ConfigureAwait(false);
                DateTime t1 = DateTime.UtcNow.AddSeconds(-60);
                DateTime t2 = t1.AddSeconds(20);
                DateTime t3 = t2.AddSeconds(20);
                Captain captain = await CreateCaptainAsync(db, "quota-cas", CaptainStateEnum.Idle, null).ConfigureAwait(false);

                UsageQuotaDecision holdT2 = UsageQuotaPolicy.Evaluate(captain, Observation(t2, Window("five_hour", 100, t2.AddHours(1))), null, 300, t3);
                AssertEqual(UsageQuotaOutcomeEnum.Held, await service.ApplyUsageQuotaAsync(captain.Id, holdT2).ConfigureAwait(false), "the t2 exhausted sample holds");

                UsageQuotaDecision releaseT1 = UsageQuotaPolicy.Evaluate(captain, Observation(t1, Window("five_hour", 10, t2.AddHours(1))), null, 300, t3);
                AssertEqual(UsageQuotaActionEnum.Release, releaseT1.Action, "the old sample alone would release");
                AssertEqual(UsageQuotaOutcomeEnum.SkippedStale, await service.ApplyUsageQuotaAsync(captain.Id, releaseT1).ConfigureAwait(false), "an older low sample arriving late cannot release");
                AssertEqual(UsageQuotaOutcomeEnum.SkippedStale, await service.ApplyUsageQuotaAsync(captain.Id, holdT2).ConfigureAwait(false), "re-applying the same sample is idempotent");

                UsageQuotaDecision holdT1 = UsageQuotaPolicy.Evaluate(captain, Observation(t1, Window("five_hour", 100, t1.AddHours(5))), null, 300, t3);
                AssertEqual(UsageQuotaOutcomeEnum.SkippedStale, await service.ApplyUsageQuotaAsync(captain.Id, holdT1).ConfigureAwait(false), "an older exhausted sample cannot replace the newer hold");
                Captain still = (await db.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!;
                AssertEqual(t2.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ss"), still.QuarantineUntilUtc!.Value.ToString("yyyy-MM-ddTHH:mm:ss"), "the newer hold's window survives");

                UsageQuotaDecision holdT3 = UsageQuotaPolicy.Evaluate(captain, Observation(t3, Window("seven_day", 100, t3.AddDays(2))), null, 300, t3);
                AssertEqual(UsageQuotaOutcomeEnum.Extended, await service.ApplyUsageQuotaAsync(captain.Id, holdT3).ConfigureAwait(false), "a newer exhausted sample refreshes the hold");
                Captain extended = (await db.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!;
                AssertContains("seven_day", extended.QuarantineReason!, "the refreshed hold names the newer window");

                UsageQuotaDecision releaseT3 = UsageQuotaPolicy.Evaluate(captain, Observation(t3.AddSeconds(5), Window("seven_day", 10, t3.AddDays(2))), null, 300, t3.AddSeconds(5));
                AssertEqual(UsageQuotaOutcomeEnum.Released, await service.ApplyUsageQuotaAsync(captain.Id, releaseT3).ConfigureAwait(false), "a newer low sample releases");
            }));

            cases.Add(CaseAsync("rejected_observation_preserves_last_good", "A rejected probe answer committed through the real capture path keeps the account's last-good data", TestTags.Negative, async () =>
            {
                using TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await SeedTenantAsync(db).ConfigureAwait(false);
                LoggingModule logging = CreateLogging();
                ArmadaSettings settings = new ArmadaSettings();
                settings.UsageCapture = new UsageCaptureSettings { Enabled = true, MinimumIntervalSeconds = 60 };
                const string hostId = "host-quarantine-lastgood";
                UsageAccountResolver accounts = new UsageAccountResolver(hostId, new string('b', 64));
                ScriptedProbe scripted = new ScriptedProbe();
                UsageProbeHostRunner runner = new UsageProbeHostRunner(logging, accounts, runtime => scripted);
                LocalHostCommandExecutor executor = new LocalHostCommandExecutor { UsageProbe = runner };
                ManualTimeProvider clock = new ManualTimeProvider(DateTime.UtcNow);
                UsageCaptureService service = new UsageCaptureService(db.Driver, settings, accounts, executor, null, logging, clock, () => 1.0);
                UsageCaptureGroup group = new UsageCaptureGroup
                {
                    GroupKey = UsageAccountResolver.GroupKey(TenantId, hostId, AgentRuntimeEnum.Codex, null),
                    ConfigurationRevision = UsageAccountResolver.ConfigurationRevision(hostId, AgentRuntimeEnum.Codex, null),
                    TenantId = TenantId,
                    HostId = hostId,
                    Runtime = AgentRuntimeEnum.Codex,
                    Mode = UsageCaptureModeEnum.Auto,
                    EffectiveIntervalSeconds = 300,
                    MemberCaptainIds = new List<string> { "cpt_quota_lastgood" }
                };

                long reset = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
                string good = "{\"accountId\":\"acct-lastgood\",\"rateLimits\":{\"limitId\":\"codex\",\"primary\":{\"usedPercent\":42,\"windowDurationMins\":300,\"resetsAt\":" + reset + "}}}";
                scripted.Next = () =>
                {
                    UsageProbeResult result = CodexRateLimitUsage.ToResult(JsonSerializer.Deserialize<CodexRateLimitsReadResponse>(good), DateTime.UtcNow);
                    result.AccountKey = accounts.DeriveAccountKey(TenantId, AgentRuntimeEnum.Codex, "acct-lastgood");
                    return result;
                };
                UsageCaptureAttempt first = await service.TryStartAsync(group, null).ConfigureAwait(false);
                AssertTrue(first.Started, "the first attempt starts: " + first.SkippedReason);
                UsageCaptureAttemptResult firstResult = await AwaitAsync(first.Completion).ConfigureAwait(false);
                AssertEqual(UsageSnapshotStatusEnum.Ok, firstResult.Status, "the good observation commits");
                string accountKey = accounts.DeriveAccountKey(TenantId, AgentRuntimeEnum.Codex, "acct-lastgood")!;
                UsageLatestState before = (await db.Driver.UsageSnapshots.ReadLatestStateAsync(TenantId, hostId, AgentRuntimeEnum.Codex, accountKey).ConfigureAwait(false))!;
                AssertNotNull(before, "the account has latest state");
                AssertContains("codex:primary", before.LastGoodWindowsJson!, "the last-good windows are stored");

                string malformed = "{\"accountId\":\"acct-lastgood\",\"rateLimits\":{\"limitId\":\"codex\",\"primary\":{\"usedPercent\":250}}}";
                scripted.Next = () => CodexRateLimitUsage.ToResult(JsonSerializer.Deserialize<CodexRateLimitsReadResponse>(malformed), DateTime.UtcNow);
                clock.Set(service.NextEligibleUtc(group.GroupKey) ?? clock.GetUtcNow().UtcDateTime);
                UsageCaptureAttempt second = await service.TryStartAsync(group, null).ConfigureAwait(false);
                AssertTrue(second.Started, "the second attempt starts: " + second.SkippedReason);
                UsageCaptureAttemptResult secondResult = await AwaitAsync(second.Completion).ConfigureAwait(false);
                AssertEqual(UsageSnapshotStatusEnum.Failed, secondResult.Status, "the rejected observation is stored as a failure");
                AssertEqual("MalformedOutput", secondResult.Snapshot!.ErrorCode, "the failure carries the parser's stable code");
                AssertEqual("[]", secondResult.Snapshot.WindowsJson ?? "[]", "the rejected observation stores no windows");

                UsageLatestState after = (await db.Driver.UsageSnapshots.ReadLatestStateAsync(TenantId, hostId, AgentRuntimeEnum.Codex, accountKey).ConfigureAwait(false))!;
                AssertEqual(before.LastGoodSnapshotId, after.LastGoodSnapshotId, "the last-good snapshot is preserved");
                AssertEqual(before.LastGoodWindowsJson, after.LastGoodWindowsJson, "the last-good windows are preserved");
                AssertEqual("MalformedOutput", after.LastErrorCode, "the rejection is recorded as the latest error");

                UsageQuotaDecision decision = UsageQuotaPolicy.Evaluate(NewCaptain(null), UsageProbeResult.Failed("MalformedOutput"), accountKey, 300, DateTime.UtcNow);
                AssertEqual(UsageQuotaActionEnum.NoChange, decision.Action, "a rejected observation never routes");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Usage Capture Quarantine Policy",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static Captain NewCaptain(string? model)
        {
            return new Captain("quota-policy", AgentRuntimeEnum.ClaudeCode) { TenantId = TenantId, Model = model };
        }

        private static UsageWindow Window(string name, double percent, DateTime? resets)
        {
            return new UsageWindow { Name = name, UsedPercent = percent, Unit = "percent", ResetsUtc = resets };
        }

        private static UsageProbeResult Observation(DateTime captured, params UsageWindow[] windows)
        {
            return new UsageProbeResult
            {
                Status = UsageSnapshotStatusEnum.Ok,
                Source = UsageCaptureSourceEnum.AppServer,
                CapturedUtc = captured,
                Windows = new List<UsageWindow>(windows)
            };
        }

        private static async Task SeedTenantAsync(TestDatabase db)
        {
            try
            {
                await db.Driver.Tenants.CreateAsync(new TenantMetadata { Id = TenantId, Name = "Usage Quarantine Tenant" }).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Already present in a shared server database.
            }
        }

        private static async Task<CaptainService> CreateCaptainServiceAsync(TestDatabase db)
        {
            await SeedTenantAsync(db).ConfigureAwait(false);
            LoggingModule logging = CreateLogging();
            ArmadaSettings settings = new ArmadaSettings();
            StubGitService git = new StubGitService { IsRepositoryResult = true };
            IDockService docks = new DockService(logging, db.Driver, settings, git);
            return new CaptainService(logging, db.Driver, settings, git, docks);
        }

        private static async Task<Captain> CreateCaptainAsync(TestDatabase db, string name, CaptainStateEnum state, string? reason)
        {
            Captain captain = new Captain(name + "-" + Guid.NewGuid().ToString("N").Substring(0, 6), AgentRuntimeEnum.ClaudeCode) { TenantId = TenantId };
            captain = await db.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);
            if (state != CaptainStateEnum.Idle || reason != null)
            {
                captain.State = state;
                captain.QuarantineReason = reason;
                captain.QuarantineUntilUtc = reason == null ? null : DateTime.UtcNow.AddHours(1);
                captain = await db.Driver.Captains.UpdateAsync(captain).ConfigureAwait(false);
            }

            return captain;
        }

        private static async Task<UsageCaptureAttemptResult> AwaitAsync(Task<UsageCaptureAttemptResult> task)
        {
            Task done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(false);
            AssertTrue(done == task, "the capture attempt completes within 30 seconds");
            return await task.ConfigureAwait(false);
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

        private sealed class ScriptedProbe : IUsageProbe
        {
            public Func<UsageProbeResult> Next { get; set; } = () => UsageProbeResult.Failed("NotScripted");

            public Task<UsageProbeResult> CaptureAsync(UsageProbeHost host, CancellationToken token = default)
            {
                return Task.FromResult(Next());
            }
        }

        private sealed class ManualTimeProvider : TimeProvider
        {
            private readonly object _Lock = new object();
            private DateTimeOffset _Now;

            public ManualTimeProvider(DateTime startUtc)
            {
                _Now = new DateTimeOffset(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc), TimeSpan.Zero);
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
