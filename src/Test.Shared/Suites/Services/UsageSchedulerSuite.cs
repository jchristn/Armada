namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the account usage capture scheduler (AC2 Services.UsageScheduler): the scheduling
    /// semantics of <see cref="UsageCaptureScheduler"/> and <see cref="UsageCaptureService"/> against a
    /// real database and a stub host executor. A fake clock drives due times, deterministic jitter makes
    /// every due time exact, groups share one attempt and one snapshot (two captains, two member links,
    /// one dedicated job), the effective interval is the shortest requested interval floored by the
    /// server-wide minimum, the persisted lease keeps a restart from replaying an attempt until it
    /// expires, manual captures join a flight and respect the cooldown rather than bypassing the
    /// interval, failures back off at 2x per consecutive failure up to the configured bound, AuthMissing
    /// retries slowly, Unsupported waits for a configuration change, and a result whose generation is
    /// stale is discarded instead of applied.
    /// </summary>
    public sealed class UsageSchedulerSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.UsageScheduler";
        private const string TenantId = "ten_usage_sched";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the usage capture scheduler suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("scheduler_groups_shortest_interval_and_command_fingerprints", "Groups share the shortest interval, the global floor and the command fingerprint", TestTags.Positive, async () =>
            {
                using TestDatabaseFixture fixture = await TestDatabaseFixture.CreateAsync().ConfigureAwait(false);

                await fixture.CreateCaptainAsync("short", 300).ConfigureAwait(false);
                await fixture.CreateCaptainAsync("long", 900).ConfigureAwait(false);
                fixture.Settings.UsageCapture.MinimumIntervalSeconds = 600;

                UsageCaptureScheduler scheduler = fixture.CreateScheduler();
                await scheduler.RebuildAsync().ConfigureAwait(false);

                List<UsageCaptureGroup> groups = scheduler.Groups();
                AssertEqual(1, groups.Count, "captains on one host and runtime form one group");
                UsageCaptureGroup group = groups[0];
                AssertEqual(2, group.MemberCaptainIds.Count, "both captains are members of the group");
                AssertEqual(300, group.RequestedIntervalSeconds, "the group runs at the shortest requested interval");
                AssertEqual(600, group.EffectiveIntervalSeconds, "the global minimum floors the effective interval");
                AssertFalse(group.AllowPty, "PTY stays opt-in on the group");
                AssertEqual(0, scheduler.UnresolvedCaptains().Count, "standalone captains resolve to the Admiral host");

                // Two different Custom command identities never share a group even on the same runtime and
                // host; the same command does.
                await fixture.CreateCustomCaptainAsync("custom-a", "{\"executable\":\"/opt/one\",\"arguments\":[]}").ConfigureAwait(false);
                await fixture.CreateCustomCaptainAsync("custom-b", "{\"executable\":\"/opt/two\",\"arguments\":[]}").ConfigureAwait(false);
                await fixture.CreateCustomCaptainAsync("custom-c", "{\"executable\":\"/opt/two\",\"arguments\":[]}").ConfigureAwait(false);

                UsageCaptureScheduler customScheduler = fixture.CreateScheduler();
                await customScheduler.RebuildAsync().ConfigureAwait(false);

                List<UsageCaptureGroup> customGroups = customScheduler.Groups();
                AssertEqual(3, customGroups.Count, "Auto and each distinct Custom command identity form their own groups");
                UsageCaptureGroup shared = customGroups.Find(candidate => candidate.Mode == UsageCaptureModeEnum.Custom && candidate.MemberCaptainIds.Count == 2);
                AssertNotNull(shared, "captains with the same command share one group");
                AssertEqual(2, shared!.MemberCaptainIds.Count, "the same executable identity is one group");
                UsageCaptureGroup other = customGroups.Find(candidate => candidate.Mode == UsageCaptureModeEnum.Custom && candidate.MemberCaptainIds.Count == 1);
                AssertNotNull(other, "a different executable identity stays its own group");
                AssertFalse(String.Equals(other!.CommandFingerprint, shared.CommandFingerprint, StringComparison.Ordinal), "command fingerprints differ between the groups");
            }));

            cases.Add(CaseAsync("scheduler_fake_clock_deterministic_jitter_and_minimum_floor", "Due times follow the fake clock, exact jitter, and the minimum floors after jitter", TestTags.Positive, async () =>
            {
                using TestDatabaseFixture fixture = await TestDatabaseFixture.CreateAsync().ConfigureAwait(false);
                fixture.Settings.UsageCapture.MinimumIntervalSeconds = 60;

                await fixture.CreateCaptainAsync("jittered", 100).ConfigureAwait(false);
                UsageCaptureScheduler scheduler = fixture.CreateScheduler();
                await scheduler.RebuildAsync().ConfigureAwait(false);

                UsageCaptureGroup group = scheduler.Groups()[0];
                AssertEqual(100, group.EffectiveIntervalSeconds, "in-range interval is preserved");
                DateTime start = fixture.Clock.Now();
                AssertEqual(start.AddSeconds(100), fixture.Service.NextEligibleUtc(group.GroupKey), "initial due is the effective interval at jitter 1.0");

                // Not due one second early: the fake clock decides, nothing else.
                fixture.Clock.Advance(TimeSpan.FromSeconds(99));
                List<UsageCaptureAttempt> early = await scheduler.TickAsync().ConfigureAwait(false);
                AssertEqual(0, early.Count, "a group one second short of its due time does not run");

                fixture.Clock.Advance(TimeSpan.FromSeconds(1));
                List<UsageCaptureAttempt> due = await scheduler.TickAsync().ConfigureAwait(false);
                AssertEqual(1, due.Count, "a due group starts its attempt");
                UsageCaptureAttemptResult result = await AwaitAsync(due[0].Completion).ConfigureAwait(false);
                AssertTrue(result.Persisted, "the scheduled attempt commits its snapshot");
                AssertEqual(UsageSnapshotStatusEnum.Ok, result.Status, "the stub executor answers Ok");
                AssertEqual(fixture.Clock.Now().AddSeconds(100), fixture.Service.NextEligibleUtc(group.GroupKey), "a success reschedules the effective interval");

                // The floor applies after jitter: a 0.5 jitter on a 100-second effective interval is
                // floored by the 60-second minimum.
                using TestDatabaseFixture floored = await TestDatabaseFixture.CreateAsync(() => 0.5).ConfigureAwait(false);
                floored.Settings.UsageCapture.MinimumIntervalSeconds = 60;
                await floored.CreateCaptainAsync("floored", 100).ConfigureAwait(false);
                UsageCaptureScheduler flooredScheduler = floored.CreateScheduler();
                await flooredScheduler.RebuildAsync().ConfigureAwait(false);
                DateTime floorStart = floored.Clock.Now();
                AssertEqual(floorStart.AddSeconds(60), floored.Service.NextEligibleUtc(flooredScheduler.Groups()[0].GroupKey), "the global minimum is enforced after the jitter");

                // With a minimum above the interval, the effective interval is the minimum itself.
                using TestDatabaseFixture raised = await TestDatabaseFixture.CreateAsync(() => 0.5).ConfigureAwait(false);
                raised.Settings.UsageCapture.MinimumIntervalSeconds = 120;
                await raised.CreateCaptainAsync("raised", 100).ConfigureAwait(false);
                UsageCaptureScheduler raisedScheduler = raised.CreateScheduler();
                await raisedScheduler.RebuildAsync().ConfigureAwait(false);
                AssertEqual(120, raisedScheduler.Groups()[0].EffectiveIntervalSeconds, "the minimum raises the effective interval");
                AssertEqual(raised.Clock.Now().AddSeconds(120), raised.Service.NextEligibleUtc(raisedScheduler.Groups()[0].GroupKey), "a raised minimum is never jittered below itself");
            }));

            cases.Add(CaseAsync("scheduler_two_captains_share_one_attempt_and_links", "Two captains share one attempt, one snapshot and one dedicated job", TestTags.Positive, async () =>
            {
                using TestDatabaseFixture fixture = await TestDatabaseFixture.CreateAsync().ConfigureAwait(false);
                Captain first = await fixture.CreateCaptainAsync("first", 300).ConfigureAwait(false);
                Captain second = await fixture.CreateCaptainAsync("second", 300).ConfigureAwait(false);

                UsageCaptureScheduler scheduler = fixture.CreateScheduler();
                await scheduler.RebuildAsync().ConfigureAwait(false);
                UsageCaptureGroup group = scheduler.Groups()[0];
                AssertEqual(2, group.MemberCaptainIds.Count, "both captains share the group");

                fixture.Clock.Set(fixture.Service.NextEligibleUtc(group.GroupKey) ?? fixture.Clock.Now());
                fixture.Executor.Hold = true;
                UsageCaptureAttempt attempt = await fixture.Service.TryStartAsync(group, first.Id).ConfigureAwait(false);
                AssertTrue(attempt.Started, "the first request starts the attempt");
                AssertNotNull(attempt.JobId, "the attempt owns a dedicated job");

                // A second captain's capture joins the in-flight attempt: same job, no duplicate probe.
                UsageCaptureAttempt joined = await fixture.Service.TryStartAsync(group, second.Id).ConfigureAwait(false);
                AssertTrue(joined.JoinedExisting, "the second captain joins the existing flight");
                AssertFalse(joined.Started, "the join starts nothing");
                AssertEqual(attempt.JobId, joined.JobId, "the join returns the same job id");
                AssertTrue(fixture.Service.IsInFlight(group.GroupKey), "the flight is in flight while the probe runs");

                fixture.Executor.Release();
                UsageCaptureAttemptResult result = await AwaitAsync(attempt.Completion).ConfigureAwait(false);
                AssertTrue(result.Persisted, "the shared attempt commits exactly one snapshot");
                AssertNotNull(result.Snapshot, "the snapshot is returned to both captains");
                AssertEqual(first.Id, result.Snapshot!.CaptainId, "the triggering captain is recorded, not the account owner");

                UsageSnapshotQuery query = new UsageSnapshotQuery { TenantId = TenantId, PageSize = 10 };
                EnumerationResult<UsageSnapshot> snapshots = await fixture.Database.UsageSnapshots.EnumerateAsync(query).ConfigureAwait(false);
                AssertEqual(1, snapshots.Objects.Count, "one attempt stores one snapshot for the group");

                List<string> members = await fixture.Database.UsageSnapshots.ReadCaptainsAsync(result.Snapshot.Id, TenantId).ConfigureAwait(false);
                AssertEqual(2, members.Count, "the snapshot links every member captain");
                AssertTrue(members.Contains(first.Id) && members.Contains(second.Id), "both captains are linked at capture time");

                // Both members hold an open membership on the shared account key (D1: the account, not the
                // captain, owns the limit).
                foreach (string captainId in new string[] { first.Id, second.Id })
                {
                    List<UsageAccountMembership> memberships = await fixture.Database.UsageMemberships.ReadActiveAsync(TenantId, captainId).ConfigureAwait(false);
                    AssertEqual(1, memberships.Count, "each member holds one open membership");
                    AssertEqual(fixture.Executor.Result.AccountKey, memberships[0].AccountKey, "the membership names the observed account key");
                    AssertEqual(group.HostId, memberships[0].HostId, "the membership names the credential host");
                }

                // One dedicated job, visible on Jobs, succeeded.
                List<Job> jobs = await fixture.Database.Jobs.EnumerateAsync().ConfigureAwait(false);
                List<Job> captureJobs = jobs.Where(job => job.Kind == JobKindEnum.UsageProbe).ToList();
                AssertEqual(1, captureJobs.Count, "the shared attempt is one job");
                AssertEqual(UsageAccountResolver.JobName(group.GroupKey), captureJobs[0].Name, "the job names its group");
                AssertEqual(JobStatusEnum.Succeeded, captureJobs[0].Status, "the capture job completed");

                // The joined captain saw the same completion.
                UsageCaptureAttemptResult joinedResult = await AwaitAsync(joined.Completion).ConfigureAwait(false);
                AssertEqual(result.Snapshot.Id, joinedResult.Snapshot?.Id, "the join observes the same committed snapshot");
            }));

            cases.Add(CaseAsync("scheduler_manual_capture_respects_cooldown_and_server_switch", "Manual capture respects the cooldown and the server switch; it is not an interval bypass", TestTags.Negative, async () =>
            {
                using TestDatabaseFixture fixture = await TestDatabaseFixture.CreateAsync().ConfigureAwait(false);
                Captain captain = await fixture.CreateCaptainAsync("manual", 300).ConfigureAwait(false);

                UsageCaptureScheduler scheduler = fixture.CreateScheduler();
                await scheduler.RebuildAsync().ConfigureAwait(false);
                UsageCaptureGroup group = scheduler.Groups()[0];

                fixture.Clock.Set(fixture.Service.NextEligibleUtc(group.GroupKey) ?? fixture.Clock.Now());
                UsageCaptureAttempt first = await fixture.Service.TryStartAsync(group, captain.Id).ConfigureAwait(false);
                AssertTrue(first.Started, "a manual capture outside the cooldown starts its attempt");
                await AwaitAsync(first.Completion).ConfigureAwait(false);

                // A success moved the next eligible time into the future: a manual capture inside the
                // interval is refused with that time, not run early.
                fixture.Clock.Advance(TimeSpan.FromSeconds(100));
                DateTime eligible = fixture.Service.NextEligibleUtc(group.GroupKey) ?? DateTime.MinValue;
                AssertTrue(eligible > fixture.Clock.Now(), "the group is cooling down after a capture");
                UsageCaptureAttempt cooldown = await fixture.Service.TryStartAsync(group, captain.Id).ConfigureAwait(false);
                AssertFalse(cooldown.Started, "a manual capture inside the cooldown starts nothing");
                AssertEqual("Cooldown", cooldown.SkippedReason, "the refusal names the cooldown");
                AssertEqual(eligible, cooldown.NextEligibleUtc ?? DateTime.MinValue, "the refusal reports when capture is eligible again");
                AssertFalse(fixture.Service.IsInFlight(group.GroupKey), "no flight was created");

                // The server-wide switch disables manual captures too.
                fixture.Settings.UsageCapture.Enabled = false;
                UsageCaptureAttempt disabled = await fixture.Service.TryStartAsync(group, captain.Id).ConfigureAwait(false);
                AssertFalse(disabled.Started, "a disabled server starts nothing");
                AssertEqual("CaptureDisabled", disabled.SkippedReason, "the refusal names the disabled switch");

                // A captain that is not capturing has no group at all: nothing is scheduled for it.
                fixture.Settings.UsageCapture.Enabled = true;
                Captain idle = await fixture.CreateCaptainAsync("idle", 300).ConfigureAwait(false);
                idle.UsageCaptureEnabled = false;
                await fixture.Database.Captains.UpdateAsync(idle).ConfigureAwait(false);
                UsageCaptureScheduler rebuilt = fixture.CreateScheduler();
                await rebuilt.RebuildAsync().ConfigureAwait(false);
                AssertEqual(1, rebuilt.Groups().Count, "only the capturing captain forms a group");
                AssertFalse(rebuilt.Groups()[0].MemberCaptainIds.Contains(idle.Id), "the disabled captain is not a member of any group");
            }));

            cases.Add(CaseAsync("scheduler_restart_lease_blocks_then_expires", "A restart never replays an attempt while its lease holds, and an expired lease fails without a result", TestTags.Positive, async () =>
            {
                using TestDatabaseFixture fixture = await TestDatabaseFixture.CreateAsync().ConfigureAwait(false);
                // A 60-second interval (the global minimum) keeps the fake clock within the lease window of
                // the job row's real timestamps, so the lease interplay is observable without racing the
                // wall clock.
                Captain captain = await fixture.CreateCaptainAsync("lease", 60).ConfigureAwait(false);

                UsageCaptureScheduler scheduler = fixture.CreateScheduler();
                await scheduler.RebuildAsync().ConfigureAwait(false);
                UsageCaptureGroup group = scheduler.Groups()[0];

                fixture.Clock.Set(fixture.Service.NextEligibleUtc(group.GroupKey) ?? fixture.Clock.Now());
                fixture.Executor.Hold = true;
                UsageCaptureAttempt previous = await fixture.Service.TryStartAsync(group, captain.Id).ConfigureAwait(false);
                AssertTrue(previous.Started, "the previous process started the attempt");

                // A restarted process (fresh service state, same database) must not replay the probe.
                using TestDatabaseFixture restarted = TestDatabaseFixture.AttachAsync(fixture);
                UsageCaptureAttempt held = await restarted.Service.TryStartAsync(group, captain.Id).ConfigureAwait(false);
                AssertTrue(held.JoinedExisting, "the restart joins the held lease rather than replaying");
                AssertFalse(held.Started, "the restart starts nothing while the lease holds");
                AssertEqual(previous.JobId, held.JobId, "the held lease is the previous attempt's job");
                AssertEqual("LeaseHeld", held.SkippedReason, "the refusal names the held lease");

                // After the lease window passes, the prior lease is failed and a new attempt may start.
                TimeSpan pastLease = TimeSpan.FromSeconds(restarted.Service.LeaseWindow.TotalSeconds + 5);
                restarted.Clock.Advance(pastLease);
                fixture.Clock.Advance(pastLease);
                UsageCaptureAttempt fresh = await restarted.Service.TryStartAsync(group, captain.Id).ConfigureAwait(false);
                AssertTrue(fresh.Started, "an expired lease lets a new attempt start");
                AssertNotEqual(previous.JobId, fresh.JobId, "the new attempt owns its own job");

                Job? oldJob = await restarted.Database.Jobs.ReadAsync(previous.JobId!).ConfigureAwait(false);
                AssertNotNull(oldJob, "the stale lease row survives");
                AssertEqual(JobStatusEnum.Failed, oldJob!.Status, "the stale lease fails");
                AssertEqual("LeaseExpired", oldJob.ErrorReason, "the stale lease names its expiry");

                // The previous process's late result is discarded rather than applied (no result may be
                // applied after its lease expired).
                fixture.Executor.Release();
                UsageCaptureAttemptResult late = await AwaitAsync(previous.Completion).ConfigureAwait(false);
                AssertFalse(late.Persisted, "a late result past its lease is never applied");
                AssertEqual("LeaseExpired", late.DiscardedReason, "the late result names the lease expiry");

                UsageCaptureAttemptResult freshResult = await AwaitAsync(fresh.Completion).ConfigureAwait(false);
                AssertTrue(freshResult.Persisted, "the fresh attempt commits its snapshot");
                UsageSnapshotQuery query = new UsageSnapshotQuery { TenantId = TenantId, PageSize = 10 };
                EnumerationResult<UsageSnapshot> snapshots = await restarted.Database.UsageSnapshots.EnumerateAsync(query).ConfigureAwait(false);
                AssertEqual(1, snapshots.Objects.Count, "only the fresh attempt stored a snapshot");
            }));

            cases.Add(CaseAsync("scheduler_stale_generation_discards_result", "A result from a stale settings generation is discarded, not applied", TestTags.Negative, async () =>
            {
                using TestDatabaseFixture fixture = await TestDatabaseFixture.CreateAsync().ConfigureAwait(false);
                Captain captain = await fixture.CreateCaptainAsync("generation", 300).ConfigureAwait(false);

                UsageCaptureScheduler scheduler = fixture.CreateScheduler();
                await scheduler.RebuildAsync().ConfigureAwait(false);
                UsageCaptureGroup group = scheduler.Groups()[0];

                fixture.Clock.Set(fixture.Service.NextEligibleUtc(group.GroupKey) ?? fixture.Clock.Now());
                fixture.Executor.Hold = true;
                UsageCaptureAttempt attempt = await fixture.Service.TryStartAsync(group, captain.Id).ConfigureAwait(false);
                long generation = fixture.Service.Generation;

                // A settings or configuration change invalidates the scheduling generation.
                fixture.Service.Invalidate();
                AssertTrue(fixture.Service.Generation > generation, "the invalidation bumps the generation");
                fixture.Executor.Release();

                UsageCaptureAttemptResult result = await AwaitAsync(attempt.Completion).ConfigureAwait(false);
                AssertFalse(result.Persisted, "a stale-generation result is never applied");
                AssertEqual("SettingsGenerationChanged", result.DiscardedReason, "the discard names the generation change");
                AssertNull(fixture.Service.NextEligibleUtc(group.GroupKey), "a discarded result does not reschedule the group");

                UsageSnapshotQuery query = new UsageSnapshotQuery { TenantId = TenantId, PageSize = 10 };
                EnumerationResult<UsageSnapshot> snapshots = await fixture.Database.UsageSnapshots.EnumerateAsync(query).ConfigureAwait(false);
                AssertEqual(0, snapshots.Objects.Count, "no snapshot was committed for the discarded attempt");

                Job? job = await fixture.Database.Jobs.ReadAsync(attempt.JobId!).ConfigureAwait(false);
                AssertNotNull(job, "the discarded attempt's job row survives");
                AssertEqual(JobStatusEnum.Failed, job!.Status, "the discarded attempt's job fails");
                AssertEqual("SettingsGenerationChanged", job.ErrorReason, "the job names the generation change");

                // The invalidated group reschedules from scratch (a fresh randomized initial due time).
                UsageCaptureScheduler rebuilt = fixture.CreateScheduler();
                await rebuilt.RebuildAsync().ConfigureAwait(false);
                AssertNotNull(fixture.Service.NextEligibleUtc(group.GroupKey), "the group is scheduled again after the invalidation");
            }));

            cases.Add(CaseAsync("scheduler_failure_backoff_doubles_and_success_resets", "Failures back off at 2x per consecutive failure up to the bound; success resets", TestTags.Positive, async () =>
            {
                using TestDatabaseFixture fixture = await TestDatabaseFixture.CreateAsync().ConfigureAwait(false);
                fixture.Settings.UsageCapture.MaximumBackoffSeconds = 3600;
                await fixture.CreateCaptainAsync("backoff", 300).ConfigureAwait(false);

                UsageCaptureScheduler scheduler = fixture.CreateScheduler();
                await scheduler.RebuildAsync().ConfigureAwait(false);
                UsageCaptureGroup group = scheduler.Groups()[0];

                fixture.Executor.Result = UsageProbeResult.Failed("HostUnavailable", "host is offline");

                List<int> expectedDelays = new List<int> { 600, 1200, 2400, 3600 };
                for (int attempt = 1; attempt <= expectedDelays.Count; attempt++)
                {
                    DateTime due = fixture.Service.NextEligibleUtc(group.GroupKey) ?? fixture.Clock.Now();
                    fixture.Clock.Set(due);
                    UsageCaptureAttempt receipt = await fixture.Service.TryStartAsync(group, null).ConfigureAwait(false);
                    AssertTrue(receipt.Started, "a due group starts its attempt after each failure");
                    UsageCaptureAttemptResult result = await AwaitAsync(receipt.Completion).ConfigureAwait(false);
                    AssertTrue(result.Persisted, "each failure attempt persists its failure result");
                    AssertEqual(UsageSnapshotStatusEnum.Failed, result.Status, "the attempt records a failed snapshot");
                    AssertEqual(attempt, fixture.Service.FailureCount(group.GroupKey), "the failure count grows per consecutive failure");
                    int delay = (int)(fixture.Service.NextEligibleUtc(group.GroupKey)!.Value - fixture.Clock.Now()).TotalSeconds;
                    AssertEqual(expectedDelays[attempt - 1], delay, "backoff doubles per consecutive failure and caps at the configured bound");
                    AssertNotNull(result.Snapshot, "the failure snapshot exists");
                    AssertEqual("HostUnavailable", result.Snapshot!.ErrorCode, "the failure carries its stable error code");
                }

                // Success resets the backoff: the next due time is the effective interval again.
                fixture.Executor.Result = OkResult();
                DateTime successDue = fixture.Service.NextEligibleUtc(group.GroupKey) ?? fixture.Clock.Now();
                fixture.Clock.Set(successDue);
                UsageCaptureAttempt success = await fixture.Service.TryStartAsync(group, null).ConfigureAwait(false);
                UsageCaptureAttemptResult successResult = await AwaitAsync(success.Completion).ConfigureAwait(false);
                AssertEqual(UsageSnapshotStatusEnum.Ok, successResult.Status, "the success is recorded");
                AssertEqual(0, fixture.Service.FailureCount(group.GroupKey), "a success resets the backoff");
                int successDelay = (int)(fixture.Service.NextEligibleUtc(group.GroupKey)!.Value - fixture.Clock.Now()).TotalSeconds;
                AssertEqual(300, successDelay, "the success reschedules at the effective interval");
            }));

            cases.Add(CaseAsync("scheduler_auth_missing_slow_unsupported_waits_for_change", "AuthMissing retries hourly, Unsupported waits for a configuration change", TestTags.Negative, async () =>
            {
                using TestDatabaseFixture fixture = await TestDatabaseFixture.CreateAsync().ConfigureAwait(false);
                await fixture.CreateCaptainAsync("auth", 300).ConfigureAwait(false);

                UsageCaptureScheduler scheduler = fixture.CreateScheduler();
                await scheduler.RebuildAsync().ConfigureAwait(false);
                UsageCaptureGroup group = scheduler.Groups()[0];

                fixture.Clock.Set(fixture.Service.NextEligibleUtc(group.GroupKey) ?? fixture.Clock.Now());

                // AuthMissing: an hourly retry, never the interval, and no unknown-account merge.
                fixture.Executor.Result = UsageProbeResult.AuthMissing("CredentialUnavailable", "no credentials on this host");
                UsageCaptureAttempt authMissing = await fixture.Service.TryStartAsync(group, null).ConfigureAwait(false);
                UsageCaptureAttemptResult authResult = await AwaitAsync(authMissing.Completion).ConfigureAwait(false);
                AssertTrue(authResult.Persisted, "an auth-missing answer is stored as a capture state");
                AssertEqual(UsageSnapshotStatusEnum.AuthMissing, authResult.Status, "the auth-missing status round-trips");
                int delay = (int)(fixture.Service.NextEligibleUtc(group.GroupKey)!.Value - fixture.Clock.Now()).TotalSeconds;
                AssertEqual(3600, delay, "AuthMissing retries hourly, not at the interval");
                AssertNull(authResult.Snapshot!.AccountKey, "an auth-missing capture never invents an account key");
                List<UsageAccountMembership> memberships = await fixture.Database.UsageMemberships.ReadActiveAsync(TenantId, group.MemberCaptainIds[0]).ConfigureAwait(false);
                AssertEqual(0, memberships.Count, "a missing identity never opens a membership");

                // Unsupported: not scheduled again until the runtime, version or configuration changes.
                fixture.Clock.Advance(TimeSpan.FromSeconds(3600));
                fixture.Executor.Result = UsageProbeResult.Unsupported("UnsupportedCursor", "no verified account limit interface");
                UsageCaptureAttempt unsupported = await fixture.Service.TryStartAsync(group, null).ConfigureAwait(false);
                UsageCaptureAttemptResult unsupportedResult = await AwaitAsync(unsupported.Completion).ConfigureAwait(false);
                AssertEqual(UsageSnapshotStatusEnum.Unsupported, unsupportedResult.Status, "the unsupported answer is stored");
                AssertNull(fixture.Service.NextEligibleUtc(group.GroupKey), "an unsupported source is not scheduled again");

                List<UsageCaptureAttempt> idleTicks = await scheduler.TickAsync().ConfigureAwait(false);
                AssertEqual(0, idleTicks.Count, "ticks do not retry an unsupported source");

                // A configuration change invalidates the generation and reschedules the group.
                fixture.Service.Invalidate();
                await scheduler.RebuildAsync().ConfigureAwait(false);
                AssertNotNull(fixture.Service.NextEligibleUtc(group.GroupKey), "an invalidation reschedules the group after a configuration change");
            }));

            cases.Add(CaseAsync("scheduler_jobs_cancellation_stops_the_probe", "Cancelling the capture job from Jobs stops the probe attempt", TestTags.Positive, async () =>
            {
                using TestDatabaseFixture fixture = await TestDatabaseFixture.CreateAsync().ConfigureAwait(false);
                Captain captain = await fixture.CreateCaptainAsync("cancel", 300).ConfigureAwait(false);

                UsageCaptureScheduler scheduler = fixture.CreateScheduler();
                await scheduler.RebuildAsync().ConfigureAwait(false);
                UsageCaptureGroup group = scheduler.Groups()[0];

                fixture.Clock.Set(fixture.Service.NextEligibleUtc(group.GroupKey) ?? fixture.Clock.Now());
                fixture.Executor.Hold = true;
                UsageCaptureAttempt attempt = await fixture.Service.TryStartAsync(group, captain.Id).ConfigureAwait(false);
                AssertTrue(attempt.Started, "the attempt starts");

                // The operator cancels the dedicated job from Jobs: the probe stops rather than running to
                // its deadline, and the job keeps the operator's terminal status.
                Armada.Core.Services.JobService jobs = new Armada.Core.Services.JobService(fixture.Database, CreateLogging());
                Job? running = await fixture.Database.Jobs.ReadAsync(attempt.JobId!).ConfigureAwait(false);
                AssertNotNull(running, "the attempt's job row is readable");
                Job cancelled = await jobs.CancelAsync(running!).ConfigureAwait(false);
                AssertEqual(JobStatusEnum.Cancelled, cancelled.Status, "the job row is cancelled");

                UsageCaptureAttemptResult result = await AwaitAsync(attempt.Completion).ConfigureAwait(false);
                AssertEqual(UsageSnapshotStatusEnum.Failed, result.Status, "the stopped attempt is a failed capture");
                AssertEqual("ProbeCancelled", result.ErrorCode, "the failure names the cancellation");
                AssertFalse(fixture.Service.IsInFlight(group.GroupKey), "the flight is cleaned up");

                Job? after = await fixture.Database.Jobs.ReadAsync(attempt.JobId!).ConfigureAwait(false);
                AssertNotNull(after, "the job row survives");
                AssertEqual(JobStatusEnum.Cancelled, after!.Status, "the operator's cancellation is not overwritten");
            }));

            cases.Add(CaseAsync("scheduler_resolves_credential_host_per_dock", "Host resolution follows the captain's dock; no dock in a split deployment is unresolved", TestTags.Positive, async () =>
            {
                using TestDatabaseFixture fixture = await TestDatabaseFixture.CreateAsync().ConfigureAwait(false);
                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                HarborConnectionManager manager = new HarborConnectionManager(new HarborService(fixture.Database, logging), logging, null);

                // A split deployment with one connected Harbor.
                InProcessHarbor harbor = await InProcessHarbor.ConnectAsync(
                    manager, "hbr_sched", TenantId, null, new Armada.Runtimes.AgentRuntimeFactory(logging), new string[] { "git" }, logging).ConfigureAwait(false);
                try
                {
                    // A captain whose dock was created on the Admiral is worked on locally: its credential
                    // host is the Admiral's host even in a split deployment.
                    Fleet fleet = new Fleet("UsageSchedFleet");
                    await fixture.Database.Fleets.CreateAsync(fleet).ConfigureAwait(false);
                    Vessel localVessel = new Vessel("vsl_local", "https://github.com/test/local");
                    localVessel.FleetId = fleet.Id;
                    await fixture.Database.Vessels.CreateAsync(localVessel).ConfigureAwait(false);

                    Captain local = await fixture.CreateCaptainAsync("local-dock", 300).ConfigureAwait(false);
                    Dock localDock = new Dock(localVessel.Id) { TenantId = TenantId, CaptainId = local.Id };
                    localDock = await fixture.Database.Docks.CreateAsync(localDock).ConfigureAwait(false);
                    local.CurrentDockId = localDock.Id;
                    local = await fixture.Database.Captains.UpdateAsync(local).ConfigureAwait(false);

                    UsageProbeHost? resolved = await fixture.Accounts.TryResolveHostAsync(fixture.Database, manager, local, fixture.Settings).ConfigureAwait(false);
                    AssertNotNull(resolved, "a local dock resolves to the Admiral host");
                    AssertEqual(fixture.Accounts.HostId, resolved!.HostId, "the Admiral host is the credential host for a local dock");
                    AssertNull(resolved.HarborId, "no Harbor is named for a local dock");

                    // A captain whose dock lives on a Harbor is probed on that Harbor, connected or not.
                    Vessel remoteVessel = new Vessel("vsl_remote", "https://github.com/test/remote");
                    remoteVessel.FleetId = fleet.Id;
                    await fixture.Database.Vessels.CreateAsync(remoteVessel).ConfigureAwait(false);

                    Captain remote = await fixture.CreateCaptainAsync("harbor-dock", 300).ConfigureAwait(false);
                    Dock harborDock = new Dock(remoteVessel.Id) { TenantId = TenantId, CaptainId = remote.Id, HarborId = "hbr_sched", RepositoryPath = "/repos/vsl_remote" };
                    harborDock = await fixture.Database.Docks.CreateAsync(harborDock).ConfigureAwait(false);
                    remote.CurrentDockId = harborDock.Id;
                    remote = await fixture.Database.Captains.UpdateAsync(remote).ConfigureAwait(false);

                    UsageProbeHost? harborHost = await fixture.Accounts.TryResolveHostAsync(fixture.Database, manager, remote, fixture.Settings).ConfigureAwait(false);
                    AssertNotNull(harborHost, "a Harbor dock resolves to that Harbor");
                    AssertEqual("hbr_sched", harborHost!.HostId, "the Harbor is the credential host");
                    AssertEqual("hbr_sched", harborHost.HarborId, "the Harbor is named on the probe context");

                    // A captain with no dock in a split deployment has no resolved credential host: it is
                    // reported and never captured on the Admiral's credentials as a fallback.
                    Captain dockless = await fixture.CreateCaptainAsync("dockless", 300).ConfigureAwait(false);
                    UsageProbeHost? unresolved = await fixture.Accounts.TryResolveHostAsync(fixture.Database, manager, dockless, fixture.Settings).ConfigureAwait(false);
                    AssertNull(unresolved, "a dockless captain in a split deployment resolves no host");

                    UsageCaptureScheduler scheduler = fixture.CreateScheduler(manager);
                    await scheduler.RebuildAsync().ConfigureAwait(false);
                    AssertEqual(2, scheduler.Groups().Count, "the local dock and the Harbor dock form their own groups");
                    AssertEqual(1, scheduler.UnresolvedCaptains().Count, "the dockless captain is reported unresolved");
                    AssertTrue(scheduler.UnresolvedCaptains().Contains(dockless.Id), "the unresolved captain is the dockless one");
                }
                finally
                {
                    harbor.Dispose();
                }
            }));

            cases.Add(CaseAsync("scheduler_lease_lookup_traverses_pages_of_newer_jobs", "An unexpired lease behind newer active jobs is still found, so no second attempt starts", TestTags.Positive, async () =>
            {
                using TestDatabaseFixture fixture = await TestDatabaseFixture.CreateAsync().ConfigureAwait(false);

                UsageCaptureGroup group = new UsageCaptureGroup
                {
                    GroupKey = UsageAccountResolver.GroupKey(TenantId, fixture.Accounts.HostId, AgentRuntimeEnum.ClaudeCode, null),
                    ConfigurationRevision = UsageAccountResolver.ConfigurationRevision(fixture.Accounts.HostId, AgentRuntimeEnum.ClaudeCode, null),
                    TenantId = TenantId,
                    HostId = fixture.Accounts.HostId,
                    Runtime = AgentRuntimeEnum.ClaudeCode,
                    Mode = UsageCaptureModeEnum.Auto,
                    EffectiveIntervalSeconds = 300,
                    MemberCaptainIds = new List<string> { "cpt_paged_lease" }
                };

                // A previous process holds this group's lease: an active UsageProbe job named for the group.
                DateTime now = DateTime.UtcNow;
                Job lease = new Job(UsageAccountResolver.JobName(group.GroupKey), JobKindEnum.UsageProbe)
                {
                    TenantId = TenantId,
                    Status = JobStatusEnum.Running,
                    CreatedUtc = now,
                    LastUpdateUtc = now
                };
                lease = await fixture.Database.Jobs.CreateAsync(lease).ConfigureAwait(false);

                // 201 newer active capture jobs (other groups' leases) sort ahead of it, so this group's
                // lease is on the page after the first one.
                for (int i = 0; i < 201; i++)
                {
                    Job newer = new Job(UsageAccountResolver.JobName(TenantId + "|host|ClaudeCode|fp-" + i), JobKindEnum.UsageProbe)
                    {
                        TenantId = TenantId,
                        Status = i % 2 == 0 ? JobStatusEnum.Running : JobStatusEnum.Queued,
                        CreatedUtc = now.AddSeconds(i + 1),
                        LastUpdateUtc = now.AddSeconds(i + 1)
                    };
                    await fixture.Database.Jobs.CreateAsync(newer).ConfigureAwait(false);
                }

                // The held lease is reported, not replayed: no second attempt for the group may start.
                UsageCaptureAttempt attempt = await fixture.Service.TryStartAsync(group, null).ConfigureAwait(false);
                AssertFalse(attempt.Started, "an unexpired lease behind newer jobs still blocks a new attempt");
                AssertTrue(attempt.JoinedExisting, "the attempt reports the existing lease");
                AssertEqual("LeaseHeld", attempt.SkippedReason, "the refusal names the held lease");
                AssertEqual(lease.Id, attempt.JobId, "the held lease is this group's own job");
                AssertFalse(fixture.Service.IsInFlight(group.GroupKey), "no flight was started for the held lease");
            }));

            cases.Add(CaseAsync("scheduler_gate_wait_consumes_the_lease", "Waiting for a concurrency slot consumes the attempt's lease, before execution and before commit", TestTags.Negative, async () =>
            {
                using TestDatabaseFixture fixture = await TestDatabaseFixture.CreateAsync().ConfigureAwait(false);
                fixture.Settings.UsageCapture.MaximumConcurrentProbes = 1;
                fixture.Settings.UsageCapture.MaximumConcurrentProbesPerHost = 1;

                string hostId = fixture.Accounts.HostId;
                UsageCaptureGroup first = new UsageCaptureGroup
                {
                    GroupKey = UsageAccountResolver.GroupKey(TenantId, hostId, AgentRuntimeEnum.ClaudeCode, null),
                    ConfigurationRevision = UsageAccountResolver.ConfigurationRevision(hostId, AgentRuntimeEnum.ClaudeCode, null),
                    TenantId = TenantId,
                    HostId = hostId,
                    Runtime = AgentRuntimeEnum.ClaudeCode,
                    Mode = UsageCaptureModeEnum.Auto,
                    EffectiveIntervalSeconds = 300,
                    MemberCaptainIds = new List<string> { "cpt_wait_first" }
                };
                UsageCaptureGroup second = new UsageCaptureGroup
                {
                    GroupKey = UsageAccountResolver.GroupKey(TenantId, hostId, AgentRuntimeEnum.Codex, "fp-wait"),
                    ConfigurationRevision = UsageAccountResolver.ConfigurationRevision(hostId, AgentRuntimeEnum.Codex, "fp-wait"),
                    TenantId = TenantId,
                    HostId = hostId,
                    Runtime = AgentRuntimeEnum.Codex,
                    Mode = UsageCaptureModeEnum.Custom,
                    CommandJson = "{\"executable\":\"/opt/wait\",\"arguments\":[]}",
                    CommandFingerprint = "fp-wait",
                    EffectiveIntervalSeconds = 300,
                    MemberCaptainIds = new List<string> { "cpt_wait_second" }
                };

                // The first attempt takes the single concurrency slot and holds the probe; the second one
                // queues behind it, its lease clock already running.
                fixture.Executor.Hold = true;
                UsageCaptureAttempt running = await fixture.Service.TryStartAsync(first, null).ConfigureAwait(false);
                AssertTrue(running.Started, "the first attempt starts and takes the only concurrency slot");
                for (int i = 0; i < 100 && fixture.Executor.Calls < 1; i++) await Task.Delay(50).ConfigureAwait(false);
                AssertEqual(1, fixture.Executor.Calls, "the first attempt reached the credential host");

                UsageCaptureAttempt queued = await fixture.Service.TryStartAsync(second, null).ConfigureAwait(false);
                AssertTrue(queued.Started, "the second attempt is accepted and waits for a slot");

                // Both leases expire while the first probe is still held: the wait counts against them.
                fixture.Clock.Advance(TimeSpan.FromSeconds(fixture.Service.LeaseWindow.TotalSeconds + 5));
                fixture.Executor.Release();
                fixture.Executor.Hold = false; // any probe that should not run answers immediately and is caught below

                UsageCaptureAttemptResult runningResult = await AwaitAsync(running.Completion).ConfigureAwait(false);
                AssertFalse(runningResult.Persisted, "a result past its lease is never applied");
                AssertEqual("LeaseExpired", runningResult.DiscardedReason, "the held attempt's own lease expired while it ran");

                UsageCaptureAttemptResult queuedResult = await AwaitAsync(queued.Completion).ConfigureAwait(false);
                AssertFalse(queuedResult.Persisted, "an attempt whose lease expired while it queued commits nothing");
                AssertEqual("LeaseExpired", queuedResult.DiscardedReason, "the wait for a slot consumed the queued attempt's lease");
                AssertEqual("LeaseExpired", queuedResult.ErrorCode, "the discard names the lease expiry");
                AssertEqual(1, fixture.Executor.Calls, "the queued attempt never reached the credential host");

                foreach (string jobId in new string[] { running.JobId!, queued.JobId! })
                {
                    Job? job = await fixture.Database.Jobs.ReadAsync(jobId).ConfigureAwait(false);
                    AssertNotNull(job, "the attempt's job row survives");
                    AssertEqual(JobStatusEnum.Failed, job!.Status, "the expired attempt's job fails");
                    AssertEqual("LeaseExpired", job.ErrorReason, "the job names the lease expiry");
                }

                UsageSnapshotQuery query = new UsageSnapshotQuery { TenantId = TenantId, PageSize = 10 };
                EnumerationResult<UsageSnapshot> snapshots = await fixture.Database.UsageSnapshots.EnumerateAsync(query).ConfigureAwait(false);
                AssertEqual(0, snapshots.Objects.Count, "no snapshot is committed for an expired lease");
            }));

            cases.Add(CaseAsync("scheduler_configuration_changes_cancel_and_reject_stale_writes", "A disabled switch and a disabled captain stop their in-flight captures and never persist a stale result", TestTags.Negative, async () =>
            {
                using TestDatabaseFixture fixture = await TestDatabaseFixture.CreateAsync().ConfigureAwait(false);
                Captain captain = await fixture.CreateCaptainAsync("switch", 300).ConfigureAwait(false);
                UsageCaptureScheduler scheduler = fixture.CreateScheduler();
                await scheduler.RebuildAsync().ConfigureAwait(false);
                UsageCaptureGroup group = scheduler.Groups()[0];

                fixture.Clock.Set(fixture.Service.NextEligibleUtc(group.GroupKey) ?? fixture.Clock.Now());
                fixture.Executor.Hold = true;
                UsageCaptureAttempt attempt = await fixture.Service.TryStartAsync(group, captain.Id).ConfigureAwait(false);
                AssertTrue(attempt.Started, "the attempt starts while capture is enabled");

                // The operator disables capture while the probe is held: the answer is a stale write.
                fixture.Settings.UsageCapture.Enabled = false;
                fixture.Executor.Release();
                UsageCaptureAttemptResult result = await AwaitAsync(attempt.Completion).ConfigureAwait(false);
                AssertFalse(result.Persisted, "a result captured while capture was disabled is never applied");
                AssertEqual("CaptureDisabled", result.DiscardedReason, "the discard names the disabled switch");
                AssertNull(result.Snapshot, "no snapshot is returned for a discarded attempt");

                Job? job = await fixture.Database.Jobs.ReadAsync(attempt.JobId!).ConfigureAwait(false);
                AssertNotNull(job, "the discarded attempt's job row survives");
                AssertEqual(JobStatusEnum.Failed, job!.Status, "the discarded attempt's job fails");
                AssertEqual("CaptureDisabled", job.ErrorReason, "the job names the disabled switch");

                UsageSnapshotQuery snapshots = new UsageSnapshotQuery { TenantId = TenantId, PageSize = 10 };
                AssertEqual(0, (await fixture.Database.UsageSnapshots.EnumerateAsync(snapshots).ConfigureAwait(false)).Objects.Count, "no snapshot was committed while capture was disabled");

                // A tick that observes the switch off cancels the work it still has in flight: the probe
                // stops instead of running to its deadline, and the generation change discards its result.
                fixture.Settings.UsageCapture.Enabled = true;
                fixture.Clock.Set(fixture.Service.NextEligibleUtc(group.GroupKey) ?? fixture.Clock.Now());
                fixture.Executor.Hold = true;
                UsageCaptureAttempt held = await fixture.Service.TryStartAsync(group, captain.Id).ConfigureAwait(false);
                AssertTrue(held.Started, "the second attempt starts while capture is enabled");
                long generation = fixture.Service.Generation;

                fixture.Settings.UsageCapture.Enabled = false;
                List<UsageCaptureAttempt> ticked = await scheduler.TickAsync().ConfigureAwait(false);
                AssertEqual(0, ticked.Count, "a disabled switch starts nothing");
                AssertTrue(fixture.Service.Generation > generation, "the tick invalidates the generation when it observes capture disabled");

                UsageCaptureAttemptResult cancelled = await AwaitAsync(held.Completion).ConfigureAwait(false);
                AssertFalse(cancelled.Persisted, "the cancelled probe's result is never applied");
                AssertEqual("SettingsGenerationChanged", cancelled.DiscardedReason, "the invalidation discards the in-flight result");
                AssertEqual("ProbeCancelled", cancelled.ErrorCode, "the probe was cancelled rather than run to its deadline");
                AssertFalse(fixture.Service.IsInFlight(group.GroupKey), "the flight is cleaned up");

                Job? cancelledJob = await fixture.Database.Jobs.ReadAsync(held.JobId!).ConfigureAwait(false);
                AssertNotNull(cancelledJob, "the cancelled attempt's job row survives");
                AssertEqual(JobStatusEnum.Failed, cancelledJob!.Status, "the cancelled attempt's job fails");
                AssertEqual("SettingsGenerationChanged", cancelledJob.ErrorReason, "the job names the configuration change");
            }));

            cases.Add(CaseAsync("scheduler_captain_disabled_mid_flight_is_not_attributed", "A captain that opts out mid-flight is not attributed the result; a group left with no member commits nothing", TestTags.Negative, async () =>
            {
                using TestDatabaseFixture fixture = await TestDatabaseFixture.CreateAsync().ConfigureAwait(false);
                Captain kept = await fixture.CreateCaptainAsync("kept", 300).ConfigureAwait(false);
                Captain dropped = await fixture.CreateCaptainAsync("dropped", 300).ConfigureAwait(false);

                UsageCaptureScheduler scheduler = fixture.CreateScheduler();
                await scheduler.RebuildAsync().ConfigureAwait(false);
                UsageCaptureGroup group = scheduler.Groups()[0];
                AssertEqual(2, group.MemberCaptainIds.Count, "both captains share the group");

                fixture.Clock.Set(fixture.Service.NextEligibleUtc(group.GroupKey) ?? fixture.Clock.Now());
                fixture.Executor.Hold = true;
                UsageCaptureAttempt attempt = await fixture.Service.TryStartAsync(group, kept.Id).ConfigureAwait(false);

                // The dropped captain opts out while the probe is held.
                dropped.UsageCaptureEnabled = false;
                await fixture.Database.Captains.UpdateAsync(dropped).ConfigureAwait(false);

                fixture.Executor.Release();
                UsageCaptureAttemptResult result = await AwaitAsync(attempt.Completion).ConfigureAwait(false);
                AssertTrue(result.Persisted, "the still-capturing member's result is committed");
                List<string> members = await fixture.Database.UsageSnapshots.ReadCaptainsAsync(result.Snapshot!.Id, TenantId).ConfigureAwait(false);
                AssertEqual(1, members.Count, "only the still-capturing captain is linked to the snapshot");
                AssertTrue(members.Contains(kept.Id), "the captain that still captures is linked");
                AssertFalse(members.Contains(dropped.Id), "the captain that opted out mid-flight is not attributed");
                List<UsageAccountMembership> droppedMemberships = await fixture.Database.UsageMemberships.ReadActiveAsync(TenantId, dropped.Id).ConfigureAwait(false);
                AssertEqual(0, droppedMemberships.Count, "no membership is opened for a captain that opted out mid-flight");

                // The opt-out is a configuration change: the rebuild invalidates and the group keeps one member.
                await scheduler.RebuildAsync().ConfigureAwait(false);
                UsageCaptureGroup single = scheduler.Groups()[0];
                AssertEqual(1, single.MemberCaptainIds.Count, "the rebuilt group keeps only the capturing captain");
                AssertTrue(single.MemberCaptainIds.Contains(kept.Id), "the remaining member is the captain that still captures");

                // A group whose only member opts out while its probe runs has nothing left to attribute.
                fixture.Clock.Set(fixture.Service.NextEligibleUtc(single.GroupKey) ?? fixture.Clock.Now());
                fixture.Executor.Hold = true;
                UsageCaptureAttempt singleAttempt = await fixture.Service.TryStartAsync(single, kept.Id).ConfigureAwait(false);
                AssertTrue(singleAttempt.Started, "the single-member attempt starts");

                kept.UsageCaptureEnabled = false;
                await fixture.Database.Captains.UpdateAsync(kept).ConfigureAwait(false);
                fixture.Executor.Release();

                UsageCaptureAttemptResult discarded = await AwaitAsync(singleAttempt.Completion).ConfigureAwait(false);
                AssertFalse(discarded.Persisted, "a group with no member left commits nothing");
                AssertEqual("NoMembers", discarded.DiscardedReason, "the discard names the empty group");

                Job? noMembers = await fixture.Database.Jobs.ReadAsync(singleAttempt.JobId!).ConfigureAwait(false);
                AssertNotNull(noMembers, "the discarded attempt's job row survives");
                AssertEqual(JobStatusEnum.Failed, noMembers!.Status, "the discarded attempt's job fails");
                AssertEqual("NoMembers", noMembers.ErrorReason, "the job names the empty group");

                // The disabled attempt's result is never written: the earlier snapshot is still the last one.
                UsageSnapshotQuery query = new UsageSnapshotQuery { TenantId = TenantId, PageSize = 10 };
                EnumerationResult<UsageSnapshot> snapshots = await fixture.Database.UsageSnapshots.EnumerateAsync(query).ConfigureAwait(false);
                AssertEqual(1, snapshots.Objects.Count, "only the attributed snapshot exists");
            }));

            cases.Add(CaseAsync("scheduler_dockless_captain_stays_unresolved_without_live_harbors", "A dockless captain stays unresolved while Harbors are disconnected, and only a Harbor-free deployment resolves locally", TestTags.Negative, async () =>
            {
                using TestDatabaseFixture fixture = await TestDatabaseFixture.CreateAsync().ConfigureAwait(false);
                LoggingModule logging = CreateLogging();
                HarborConnectionManager manager = new HarborConnectionManager(new HarborService(fixture.Database, logging), logging, null);

                Captain dockless = await fixture.CreateCaptainAsync("dockless", 300).ConfigureAwait(false);

                InProcessHarbor harbor = await InProcessHarbor.ConnectAsync(
                    manager, "hbr_disconnect", TenantId, null, new Armada.Runtimes.AgentRuntimeFactory(logging), new string[] { "git" }, logging).ConfigureAwait(false);
                try
                {
                    UsageProbeHost? connected = await fixture.Accounts.TryResolveHostAsync(fixture.Database, manager, dockless, fixture.Settings).ConfigureAwait(false);
                    AssertNull(connected, "a dockless captain in a split deployment resolves no host while a Harbor is connected");

                    // The Harbor goes away. The number of live connections is not credential ownership: the
                    // registered Harbor still makes this a split deployment, so the captain stays unresolved
                    // instead of being captured on the Admiral's credentials.
                    await manager.OnDisconnectedAsync("hbr_disconnect").ConfigureAwait(false);
                    AssertFalse(manager.HasConnectedHarbors(), "no Harbor is connected");
                    UsageProbeHost? offline = await fixture.Accounts.TryResolveHostAsync(fixture.Database, manager, dockless, fixture.Settings).ConfigureAwait(false);
                    AssertNull(offline, "a disconnected Harbor does not make a dockless captain resolve to the Admiral host");
                }
                finally
                {
                    harbor.Dispose();
                }

                // A deployment with no Harbor registered at all is standalone: the Admiral's own host holds
                // the credentials.
                using TestDatabaseFixture standalone = await TestDatabaseFixture.CreateAsync().ConfigureAwait(false);
                Captain local = await standalone.CreateCaptainAsync("standalone", 300).ConfigureAwait(false);
                HarborConnectionManager empty = new HarborConnectionManager(new HarborService(standalone.Database, logging), logging, null);
                UsageProbeHost? resolved = await standalone.Accounts.TryResolveHostAsync(standalone.Database, empty, local, standalone.Settings).ConfigureAwait(false);
                AssertNotNull(resolved, "a deployment with no Harbor at all resolves a credential host");
                AssertEqual(standalone.Accounts.HostId, resolved!.HostId, "a standalone deployment's credential host is the Admiral's own");
                AssertNull(resolved.HarborId, "no Harbor is named for a standalone deployment");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Usage Capture Scheduler",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static UsageProbeResult OkResult()
        {
            return new UsageProbeResult
            {
                Status = UsageSnapshotStatusEnum.Ok,
                Source = UsageCaptureSourceEnum.OAuthEndpoint,
                Provider = "anthropic",
                AccountKey = "acct-hmac-1",
                CapturedUtc = DateTime.UtcNow,
                Windows = new List<UsageWindow>
                {
                    new UsageWindow { Name = "five_hour", UsedPercent = 42, WindowMinutes = 300 }
                }
            };
        }

        private static async Task<UsageCaptureAttemptResult> AwaitAsync(Task<UsageCaptureAttemptResult> completion)
        {
            return await completion.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
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
        /// One test's database, settings, resolver, stub host executor, capture service and scheduler
        /// sharing one fake clock. Attach creates a second service over the same database (a restart).
        /// </summary>
        private sealed class TestDatabaseFixture : IDisposable
        {
            public DatabaseDriver Database { get; }
            public ArmadaSettings Settings { get; }
            public UsageAccountResolver Accounts { get; }
            public StubProbeExecutor Executor { get; }
            public FakeTimeProvider Clock { get; }
            public UsageCaptureService Service { get; private set; }
            public Func<double> ServiceJitter { get; } = () => 1.0;

            private TestDatabase? OwnedDatabase;

            private TestDatabaseFixture(TestDatabase? ownedDatabase, DatabaseDriver database, ArmadaSettings settings, UsageAccountResolver accounts, StubProbeExecutor executor, FakeTimeProvider clock, UsageCaptureService service, Func<double> jitter)
            {
                OwnedDatabase = ownedDatabase;
                Database = database;
                Settings = settings;
                Accounts = accounts;
                Executor = executor;
                Clock = clock;
                Service = service;
                ServiceJitter = jitter;
            }

            public static async Task<TestDatabaseFixture> CreateAsync(Func<double>? jitter = null)
            {
                TestDatabase testDatabase = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                settings.UsageCapture = new UsageCaptureSettings { Enabled = true, MinimumIntervalSeconds = 60 };
                UsageAccountResolver accounts = new UsageAccountResolver("host-sched-1", new string('a', 64));
                FakeTimeProvider clock = new FakeTimeProvider(DateTime.UtcNow);
                StubProbeExecutor executor = new StubProbeExecutor();
                UsageCaptureService service = new UsageCaptureService(
                    testDatabase.Driver, settings, accounts, executor, null, CreateLogging(), clock, jitter ?? (() => 1.0));
                TestDatabaseFixture fixture = new TestDatabaseFixture(testDatabase, testDatabase.Driver, settings, accounts, executor, clock, service, jitter ?? (() => 1.0));
                await fixture.SeedTenantAsync().ConfigureAwait(false);
                return fixture;
            }

            private async Task SeedTenantAsync()
            {
                try
                {
                    await Database.Tenants.CreateAsync(new Armada.Core.Models.TenantMetadata { Id = TenantId, Name = "Usage Scheduler Tenant" }).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // The tenant already exists (a server database shared across cases keeps its rows).
                }
            }

            /// <summary>
            /// A restarted process over the same database: a fresh service and scheduler whose in-memory
            /// state is empty (the previous process's lease survives in the jobs table). The restarted
            /// fixture does not own the database.
            /// </summary>
            public static TestDatabaseFixture AttachAsync(TestDatabaseFixture original)
            {
                FakeTimeProvider clock = new FakeTimeProvider(original.Clock.Now());
                StubProbeExecutor executor = new StubProbeExecutor();
                UsageCaptureService service = new UsageCaptureService(
                    original.Database, original.Settings, original.Accounts, executor, null, CreateLogging(), clock, original.ServiceJitter);
                return new TestDatabaseFixture(null, original.Database, original.Settings, original.Accounts, executor, clock, service, original.ServiceJitter);
            }

            public UsageCaptureScheduler CreateScheduler(HarborConnectionManager? harbors = null)
            {
                return new UsageCaptureScheduler(Service, Database, Accounts, Settings, harbors, CreateLogging());
            }

            public async Task<Captain> CreateCaptainAsync(string name, int intervalSeconds)
            {
                Captain captain = new Captain(name, AgentRuntimeEnum.ClaudeCode)
                {
                    TenantId = TenantId,
                    UsageCaptureEnabled = true,
                    UsageCaptureIntervalSeconds = intervalSeconds,
                    UsageCaptureMode = UsageCaptureModeEnum.Auto
                };
                return await Database.Captains.CreateAsync(captain).ConfigureAwait(false);
            }

            public async Task<Captain> CreateCustomCaptainAsync(string name, string commandJson)
            {
                Captain captain = new Captain(name, AgentRuntimeEnum.Codex)
                {
                    TenantId = TenantId,
                    UsageCaptureEnabled = true,
                    UsageCaptureIntervalSeconds = 300,
                    UsageCaptureMode = UsageCaptureModeEnum.Custom,
                    UsageCaptureCommand = commandJson
                };
                return await Database.Captains.CreateAsync(captain).ConfigureAwait(false);
            }

            public void Dispose()
            {
                OwnedDatabase?.Dispose();
            }
        }

        /// <summary>
        /// A UTC clock a test moves by hand, shared by the service and the scheduler under test.
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

            public void Advance(TimeSpan by)
            {
                lock (_Lock) _Now = _Now.Add(by);
            }

            public override DateTimeOffset GetUtcNow()
            {
                lock (_Lock) return _Now;
            }
        }

        /// <summary>
        /// The credential host's seam under test: answers a canned sanitized result (the probe ran on the
        /// host), and can be held mid-probe so a test can observe flights, leases and generations.
        /// </summary>
        private sealed class StubProbeExecutor : IHostCommandExecutor
        {
            public UsageProbeResult Result { get; set; } = new UsageProbeResult
            {
                Status = UsageSnapshotStatusEnum.Ok,
                Source = UsageCaptureSourceEnum.OAuthEndpoint,
                Provider = "anthropic",
                AccountKey = "acct-hmac-1",
                Windows = new List<UsageWindow> { new UsageWindow { Name = "five_hour", UsedPercent = 42, WindowMinutes = 300 } }
            };

            public bool Hold { get; set; } = false;

            /// <summary>How many probes reached the credential host through this stub.</summary>
            public int Calls { get; private set; } = 0;

            private readonly SemaphoreSlim _Gate = new SemaphoreSlim(0);

            public Task<HostCommandResult> RunAsync(HostCommandRequest request, CancellationToken token = default)
            {
                Calls++;
                if (!Hold) return Task.FromResult(Reply(Result));
                return HeldAsync(token);
            }

            public void Release()
            {
                _Gate.Release();
            }

            private async Task<HostCommandResult> HeldAsync(CancellationToken token)
            {
                await _Gate.WaitAsync(token).ConfigureAwait(false);
                return Reply(Result);
            }

            private static HostCommandResult Reply(UsageProbeResult result)
            {
                return new HostCommandResult
                {
                    ExitCode = 0,
                    StandardOutput = result.Serialize(),
                    StandardError = String.Empty
                };
            }
        }

        #endregion
    }
}