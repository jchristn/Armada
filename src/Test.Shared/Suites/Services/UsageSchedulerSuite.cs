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
    /// stale is discarded instead of applied. The credential host receives exactly one UsageProbe request
    /// carrying the group's identity, mode, deadline and configuration revision; a host fault that escapes
    /// the executor is recorded as one redacted failure with the same backoff (never a crash or a leak);
    /// and a server that starts with capture disabled neither invalidates a generation nor dispatches
    /// anything until the switch is enabled.
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

            cases.Add(CaseAsync("scheduler_tenants_with_shared_credentials_capture_separately", "Two tenants sharing one credential host and account key capture separately; tenant is the ownership boundary", TestTags.Negative, async () =>
            {
                using TestDatabaseFixture fixture = await TestDatabaseFixture.CreateAsync().ConfigureAwait(false);
                const string SecondTenantId = "ten_usage_sched_b";

                await fixture.Database.Tenants.CreateAsync(new Armada.Core.Models.TenantMetadata { Id = SecondTenantId, Name = "Usage Scheduler Tenant B" }).ConfigureAwait(false);

                Captain first = await fixture.CreateCaptainAsync("tenant-a", 300).ConfigureAwait(false);
                Captain second = new Captain("tenant-b", AgentRuntimeEnum.ClaudeCode)
                {
                    TenantId = SecondTenantId,
                    UsageCaptureEnabled = true,
                    UsageCaptureIntervalSeconds = 300,
                    UsageCaptureMode = UsageCaptureModeEnum.Auto
                };
                second = await fixture.Database.Captains.CreateAsync(second).ConfigureAwait(false);

                UsageCaptureScheduler scheduler = fixture.CreateScheduler();
                await scheduler.RebuildAsync().ConfigureAwait(false);

                // The two captains share a runtime, a credential host and (below) the account key their
                // probe reports. They must still be two accounts: the tenant is part of the group
                // discriminator (D1).
                List<UsageCaptureGroup> groups = scheduler.Groups();
                AssertEqual(2, groups.Count, "the same host and runtime in two tenants is two groups, not one");
                UsageCaptureGroup? groupA = groups.Find(candidate => String.Equals(candidate.TenantId, TenantId, StringComparison.Ordinal));
                UsageCaptureGroup? groupB = groups.Find(candidate => String.Equals(candidate.TenantId, SecondTenantId, StringComparison.Ordinal));
                AssertNotNull(groupA, "tenant A owns a capture group");
                AssertNotNull(groupB, "tenant B owns its own capture group");
                AssertFalse(String.Equals(groupA!.GroupKey, groupB!.GroupKey, StringComparison.Ordinal), "the group keys differ between tenants");
                string[]? partsA = UsageAccountResolver.SplitGroupKey(groupA.GroupKey);
                string[]? partsB = UsageAccountResolver.SplitGroupKey(groupB.GroupKey);
                AssertNotNull(partsA, "tenant A's group key splits into its segments");
                AssertNotNull(partsB, "tenant B's group key splits into its segments");
                AssertEqual(TenantId, partsA![0], "tenant A's group key names tenant A");
                AssertEqual(SecondTenantId, partsB![0], "tenant B's group key names tenant B");
                AssertEqual(1, groupA.MemberCaptainIds.Count, "tenant A's group holds only its own captain");
                AssertEqual(first.Id, groupA.MemberCaptainIds[0], "tenant A's member is its own captain");
                AssertEqual(1, groupB.MemberCaptainIds.Count, "tenant B's group holds only its own captain");
                AssertEqual(second.Id, groupB.MemberCaptainIds[0], "tenant B's member is its own captain");

                // Both probes report the same account key, as they would when two tenants share one set
                // of operator credentials. Each tenant still starts its own attempt and job.
                fixture.Clock.Set(fixture.Service.NextEligibleUtc(groupA.GroupKey) ?? fixture.Clock.Now());
                UsageCaptureAttempt attemptA = await fixture.Service.TryStartAsync(groupA, first.Id).ConfigureAwait(false);
                AssertTrue(attemptA.Started, "tenant A starts its own attempt");
                UsageCaptureAttempt attemptB = await fixture.Service.TryStartAsync(groupB, second.Id).ConfigureAwait(false);
                AssertTrue(attemptB.Started, "tenant B starts its own attempt instead of joining tenant A's");
                AssertFalse(String.Equals(attemptA.JobId, attemptB.JobId, StringComparison.Ordinal), "each tenant owns a distinct dedicated job");

                UsageCaptureAttemptResult resultA = await AwaitAsync(attemptA.Completion).ConfigureAwait(false);
                UsageCaptureAttemptResult resultB = await AwaitAsync(attemptB.Completion).ConfigureAwait(false);
                AssertTrue(resultA.Persisted, "tenant A's snapshot is persisted");
                AssertTrue(resultB.Persisted, "tenant B's snapshot is persisted");
                AssertNotNull(resultA.Snapshot, "tenant A receives its snapshot");
                AssertNotNull(resultB.Snapshot, "tenant B receives its snapshot");
                AssertFalse(String.Equals(resultA.Snapshot!.Id, resultB.Snapshot!.Id, StringComparison.Ordinal), "the shared credentials never collapse into one snapshot");
                AssertEqual(TenantId, resultA.Snapshot.TenantId, "tenant A's snapshot belongs to tenant A");
                AssertEqual(SecondTenantId, resultB.Snapshot.TenantId, "tenant B's snapshot belongs to tenant B");
                AssertEqual(fixture.Executor.Result.AccountKey, resultA.Snapshot.AccountKey, "tenant A records the observed account key");
                AssertEqual(fixture.Executor.Result.AccountKey, resultB.Snapshot.AccountKey, "tenant B records the same observed account key");

                // Enumeration and member links are tenant-scoped: neither tenant sees the other's rows.
                EnumerationResult<UsageSnapshot> snapshotsA = await fixture.Database.UsageSnapshots.EnumerateAsync(new UsageSnapshotQuery { TenantId = TenantId, PageSize = 10 }).ConfigureAwait(false);
                EnumerationResult<UsageSnapshot> snapshotsB = await fixture.Database.UsageSnapshots.EnumerateAsync(new UsageSnapshotQuery { TenantId = SecondTenantId, PageSize = 10 }).ConfigureAwait(false);
                AssertEqual(1, snapshotsA.Objects.Count, "tenant A enumerates exactly its own snapshot");
                AssertEqual(1, snapshotsB.Objects.Count, "tenant B enumerates exactly its own snapshot");
                AssertEqual(resultA.Snapshot.Id, snapshotsA.Objects[0].Id, "tenant A's enumeration is its snapshot");
                AssertEqual(resultB.Snapshot.Id, snapshotsB.Objects[0].Id, "tenant B's enumeration is its snapshot");
                AssertEqual(0, (await fixture.Database.UsageSnapshots.ReadCaptainsAsync(resultB.Snapshot.Id, TenantId).ConfigureAwait(false)).Count, "tenant A cannot read tenant B's member link");
                AssertEqual(1, (await fixture.Database.UsageSnapshots.ReadCaptainsAsync(resultB.Snapshot.Id, SecondTenantId).ConfigureAwait(false)).Count, "tenant B reads its own member link");

                List<UsageAccountMembership> crossTenant = await fixture.Database.UsageMemberships.ReadActiveAsync(SecondTenantId, first.Id).ConfigureAwait(false);
                AssertEqual(0, crossTenant.Count, "no membership crosses the tenant boundary");
                List<UsageAccountMembership> membershipB = await fixture.Database.UsageMemberships.ReadActiveAsync(SecondTenantId, second.Id).ConfigureAwait(false);
                AssertEqual(1, membershipB.Count, "tenant B's captain holds its own membership");
                AssertEqual(fixture.Executor.Result.AccountKey, membershipB[0].AccountKey, "the membership names the shared account key");

                List<Job> captureJobs = (await fixture.Database.Jobs.EnumerateAsync().ConfigureAwait(false)).Where(job => job.Kind == JobKindEnum.UsageProbe).ToList();
                AssertEqual(2, captureJobs.Count, "the two tenants own two dedicated capture jobs");
                AssertEqual(1, captureJobs.FindAll(job => String.Equals(job.TenantId, TenantId, StringComparison.Ordinal)).Count, "one job belongs to tenant A");
                AssertEqual(1, captureJobs.FindAll(job => String.Equals(job.TenantId, SecondTenantId, StringComparison.Ordinal)).Count, "one job belongs to tenant B");
            }));

            cases.Add(CaseAsync("scheduler_disabled_mode_wins_over_enabled_flag", "A captain with capture enabled but mode Disabled is never scheduled until the mode changes", TestTags.Negative, async () =>
            {
                using TestDatabaseFixture fixture = await TestDatabaseFixture.CreateAsync().ConfigureAwait(false);
                Captain captain = await fixture.CreateCaptainAsync("mode-disabled", 300).ConfigureAwait(false);
                captain.UsageCaptureEnabled = true;
                captain.UsageCaptureMode = UsageCaptureModeEnum.Disabled;
                await fixture.Database.Captains.UpdateAsync(captain).ConfigureAwait(false);

                UsageCaptureScheduler scheduler = fixture.CreateScheduler();
                await scheduler.RebuildAsync().ConfigureAwait(false);

                // D3: the Disabled mode wins over the enabled flag. The captain is not a member of any
                // group, is not reported as an unresolved host, and no tick starts a probe for it.
                AssertEqual(0, scheduler.Groups().Count, "disabled mode forms no capture group");
                AssertEqual(0, scheduler.UnresolvedCaptains().Count, "the inactive captain is not reported as an unresolved host");
                fixture.Clock.Advance(TimeSpan.FromSeconds(3600));
                List<UsageCaptureAttempt> ticks = await scheduler.TickAsync().ConfigureAwait(false);
                AssertEqual(0, ticks.Count, "no tick starts a probe for a mode-disabled captain");
                AssertEqual(0, fixture.Executor.Calls, "the credential host is never asked to probe");
                AssertEqual(0, (await fixture.Database.Jobs.EnumerateAsync().ConfigureAwait(false)).Count(job => job.Kind == JobKindEnum.UsageProbe), "no capture job is created for a mode-disabled captain");

                // Switching the mode to Auto makes the same captain schedulable again.
                captain.UsageCaptureMode = UsageCaptureModeEnum.Auto;
                await fixture.Database.Captains.UpdateAsync(captain).ConfigureAwait(false);
                await scheduler.RebuildAsync().ConfigureAwait(false);
                UsageCaptureGroup group = scheduler.Groups()[0];
                AssertEqual(1, group.MemberCaptainIds.Count, "the re-enabled captain forms one group");
                fixture.Clock.Set(fixture.Service.NextEligibleUtc(group.GroupKey) ?? fixture.Clock.Now());
                List<UsageCaptureAttempt> due = await scheduler.TickAsync().ConfigureAwait(false);
                AssertEqual(1, due.Count, "the re-enabled captain is scheduled when due");
                UsageCaptureAttemptResult result = await AwaitAsync(due[0].Completion).ConfigureAwait(false);
                AssertTrue(result.Persisted, "the re-enabled captain's capture commits");
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

            cases.Add(CaseAsync("scheduler_cancelled_host_waiter_returns_every_permit", "A cancelled attempt waiting for a host slot gives back the global permit it already holds", TestTags.Negative, async () =>
            {
                using TestDatabaseFixture fixture = await TestDatabaseFixture.CreateAsync(capture: new UsageCaptureSettings
                {
                    Enabled = true,
                    MinimumIntervalSeconds = 60,
                    MaximumConcurrentProbes = 4,
                    MaximumConcurrentProbesPerHost = 1
                }).ConfigureAwait(false);

                string hostId = fixture.Accounts.HostId;

                // Two distinct groups on ONE host with a single per-host slot: the first takes the host
                // slot, the second parks on the host gate while holding one of the four global permits.
                UsageCaptureGroup running = HostGroup(hostId, AgentRuntimeEnum.ClaudeCode, null, "cpt_permit_running");
                UsageCaptureGroup waiting = HostGroup(hostId, AgentRuntimeEnum.Codex, "fp-permit", "cpt_permit_waiting");

                fixture.Executor.Hold = true;
                UsageCaptureAttempt held = await fixture.Service.TryStartAsync(running, null).ConfigureAwait(false);
                AssertTrue(held.Started, "the first attempt takes the host's only slot");
                for (int i = 0; i < 100 && fixture.Executor.Calls < 1; i++) await Task.Delay(50).ConfigureAwait(false);
                AssertEqual(1, fixture.Executor.Calls, "the first attempt reached the credential host");

                UsageCaptureAttempt waiter = await fixture.Service.TryStartAsync(waiting, null).ConfigureAwait(false);
                AssertTrue(waiter.Started, "the second attempt is accepted and waits for the host slot");
                AssertTrue(fixture.Service.IsInFlight(waiting.GroupKey), "the host-waiting attempt is in flight");

                // The waiter parks on the host gate, which only the service's own gates can see: it is given
                // a bounded moment to take its global permit and reach the host slot, so the configuration
                // change below cancels an attempt that provably holds a global permit.
                Job? waiterJob = null;
                for (int i = 0; i < 100; i++)
                {
                    waiterJob = await fixture.Database.Jobs.ReadAsync(waiter.JobId!).ConfigureAwait(false);
                    if (waiterJob != null && waiterJob.Status == JobStatusEnum.Running) break;
                    await Task.Delay(50).ConfigureAwait(false);
                }
                AssertNotNull(waiterJob, "the waiting attempt's job row is readable");
                AssertEqual(JobStatusEnum.Running, waiterJob!.Status, "the waiting attempt's job runs while it waits for the host slot");
                await Task.Delay(500).ConfigureAwait(false);

                // A configuration change cancels both: the probe stops and the waiter is released from the
                // host gate. Every permit the two attempts acquired must come back.
                long generation = fixture.Service.Generation;
                fixture.Service.Invalidate();
                AssertTrue(fixture.Service.Generation > generation, "the invalidation bumps the generation");

                UsageCaptureAttemptResult heldResult = await AwaitAsync(held.Completion).ConfigureAwait(false);
                AssertFalse(heldResult.Persisted, "the cancelled probe's result is never applied");
                AssertEqual("SettingsGenerationChanged", heldResult.DiscardedReason, "the running attempt's discard names the generation change");

                UsageCaptureAttemptResult waiterResult = await AwaitAsync(waiter.Completion).ConfigureAwait(false);
                AssertFalse(waiterResult.Persisted, "the cancelled waiter's result is never applied");
                AssertEqual("SettingsGenerationChanged", waiterResult.DiscardedReason, "the waiter's discard names the generation change");
                AssertEqual("ProbeCancelled", waiterResult.ErrorCode, "the waiter was cancelled rather than run to its deadline");
                AssertFalse(fixture.Service.IsInFlight(running.GroupKey), "the running flight is cleaned up");
                AssertFalse(fixture.Service.IsInFlight(waiting.GroupKey), "the waiting flight is cleaned up");

                // All four global permits are free again: four attempts on four DISTINCT hosts (so no host
                // slot is contended) must all reach the credential host. A global permit stranded by the
                // cancelled host waiter leaves the fourth attempt waiting forever.
                UsageCaptureGroup[] fresh = new UsageCaptureGroup[4];
                for (int i = 0; i < fresh.Length; i++)
                    fresh[i] = HostGroup("host-permit-" + i, AgentRuntimeEnum.ClaudeCode, null, "cpt_permit_fresh_" + i);

                int reached = fixture.Executor.Calls;
                List<UsageCaptureAttempt> after = new List<UsageCaptureAttempt>();
                foreach (UsageCaptureGroup group in fresh)
                {
                    UsageCaptureAttempt attempt = await fixture.Service.TryStartAsync(group, null).ConfigureAwait(false);
                    AssertTrue(attempt.Started, "a fresh attempt on a free host starts");
                    after.Add(attempt);
                }

                for (int i = 0; i < 100 && fixture.Executor.Calls < reached + fresh.Length; i++) await Task.Delay(50).ConfigureAwait(false);
                AssertEqual(reached + fresh.Length, fixture.Executor.Calls, "every global permit is usable again after the cancelled host waiter");

                for (int i = 0; i < fresh.Length; i++) fixture.Executor.Release();
                foreach (UsageCaptureAttempt attempt in after)
                {
                    UsageCaptureAttemptResult result = await AwaitAsync(attempt.Completion).ConfigureAwait(false);
                    AssertTrue(result.Persisted, "a fresh attempt commits its snapshot rather than hanging on a stranded permit");
                }
            }));

            cases.Add(CaseAsync("scheduler_lowered_overall_limit_governs_later_attempts", "Lowering the server-wide probe limit and rebuilding admits only one probe at a time afterwards", TestTags.Negative, async () =>
            {
                await ConcurrencyLimitChangeAsync(overall: true, lower: true).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("scheduler_lowered_host_limit_governs_later_attempts", "Lowering the per-host probe limit after the host gate exists admits only one probe on that host afterwards", TestTags.Negative, async () =>
            {
                await ConcurrencyLimitChangeAsync(overall: false, lower: true).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("scheduler_raised_limits_admit_more_later_attempts", "Raising both probe limits and rebuilding lets two probes on one host run together afterwards", TestTags.Positive, async () =>
            {
                await ConcurrencyLimitChangeAsync(overall: true, lower: false).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("scheduler_overall_limit_bounds_a_burst_across_hosts", "The server-wide probe limit holds when six groups on distinct hosts start at once", TestTags.Reliability, async () =>
            {
                using TestDatabaseFixture fixture = await TestDatabaseFixture.CreateAsync(capture: new UsageCaptureSettings
                {
                    Enabled = true,
                    MinimumIntervalSeconds = 60,
                    MaximumConcurrentProbes = 2,
                    MaximumConcurrentProbesPerHost = 4
                }).ConfigureAwait(false);

                UsageCaptureGroup[] groups = new UsageCaptureGroup[6];
                for (int i = 0; i < groups.Length; i++)
                    groups[i] = HostGroup("host-burst-" + i, AgentRuntimeEnum.ClaudeCode, null, "cpt_burst_" + i);

                fixture.Executor.Hold = true;
                List<UsageCaptureAttempt> attempts = new List<UsageCaptureAttempt>();
                foreach (UsageCaptureGroup group in groups)
                {
                    UsageCaptureAttempt attempt = await fixture.Service.TryStartAsync(group, null).ConfigureAwait(false);
                    AssertTrue(attempt.Started, "every group on its own host starts its own attempt");
                    attempts.Add(attempt);
                }

                // The per-host limit is 4 and every group is on a DIFFERENT host, so only the server-wide
                // limit can bound this burst. A settle window lets an over-admitted probe arrive.
                for (int i = 0; i < 100 && fixture.Executor.Calls < 2; i++) await Task.Delay(50).ConfigureAwait(false);
                await Task.Delay(500).ConfigureAwait(false);
                AssertEqual(2, fixture.Executor.Calls, "no more than the server-wide limit reaches a credential host");

                // Each released server-wide permit admits exactly one queued group, never two.
                for (int expected = 3; expected <= groups.Length; expected++)
                {
                    fixture.Executor.Release();
                    for (int i = 0; i < 100 && fixture.Executor.Calls < expected; i++) await Task.Delay(50).ConfigureAwait(false);
                    AssertEqual(expected, fixture.Executor.Calls, "one released permit admits exactly one probe");
                }

                // Six probes are dispatched and four were released: two are still held.
                fixture.Executor.Release();
                fixture.Executor.Release();
                foreach (UsageCaptureAttempt attempt in attempts)
                {
                    UsageCaptureAttemptResult result = await AwaitAsync(attempt.Completion).ConfigureAwait(false);
                    AssertTrue(result.Persisted, "every burst attempt commits once its permit is free");
                }
            }));

            cases.Add(CaseAsync("scheduler_cancelled_overall_waiter_keeps_the_server_permit_count", "An attempt cancelled while queued for the server-wide slot never reaches a host and strands no permit", TestTags.Negative, async () =>
            {
                using TestDatabaseFixture fixture = await TestDatabaseFixture.CreateAsync(capture: new UsageCaptureSettings
                {
                    Enabled = true,
                    MinimumIntervalSeconds = 60,
                    MaximumConcurrentProbes = 1,
                    MaximumConcurrentProbesPerHost = 4
                }).ConfigureAwait(false);

                UsageCaptureGroup running = HostGroup("host-overall-a", AgentRuntimeEnum.ClaudeCode, null, "cpt_overall_running");
                UsageCaptureGroup queued = HostGroup("host-overall-b", AgentRuntimeEnum.Codex, "fp-overall-wait", "cpt_overall_queued");

                fixture.Executor.Hold = true;
                UsageCaptureAttempt held = await fixture.Service.TryStartAsync(running, null).ConfigureAwait(false);
                AssertTrue(held.Started, "the first attempt takes the only server-wide slot");
                for (int i = 0; i < 100 && fixture.Executor.Calls < 1; i++) await Task.Delay(50).ConfigureAwait(false);
                AssertEqual(1, fixture.Executor.Calls, "the first attempt reached its credential host");

                // A DIFFERENT host, so only the server-wide slot can park this attempt.
                UsageCaptureAttempt waiter = await fixture.Service.TryStartAsync(queued, null).ConfigureAwait(false);
                AssertTrue(waiter.Started, "the second attempt is accepted and waits for the server-wide slot");

                Job? waiterJob = null;
                for (int i = 0; i < 100; i++)
                {
                    waiterJob = await fixture.Database.Jobs.ReadAsync(waiter.JobId!).ConfigureAwait(false);
                    if (waiterJob != null && waiterJob.Status == JobStatusEnum.Running) break;
                    await Task.Delay(50).ConfigureAwait(false);
                }
                AssertNotNull(waiterJob, "the queued attempt's job row is readable");
                AssertEqual(JobStatusEnum.Running, waiterJob!.Status, "the queued attempt's job runs while it waits");
                await Task.Delay(500).ConfigureAwait(false);
                AssertEqual(1, fixture.Executor.Calls, "the queued attempt never touched a credential host");

                long generation = fixture.Service.Generation;
                fixture.Service.Invalidate();
                AssertTrue(fixture.Service.Generation > generation, "the invalidation bumps the generation");

                UsageCaptureAttemptResult heldResult = await AwaitAsync(held.Completion).ConfigureAwait(false);
                AssertFalse(heldResult.Persisted, "the cancelled probe's answer is never applied");
                AssertEqual("SettingsGenerationChanged", heldResult.DiscardedReason, "the running attempt's discard names the generation change");

                UsageCaptureAttemptResult waiterResult = await AwaitAsync(waiter.Completion).ConfigureAwait(false);
                AssertFalse(waiterResult.Persisted, "the cancelled queued attempt commits nothing");
                AssertEqual("ProbeCancelled", waiterResult.ErrorCode, "the queued attempt was cancelled before it ran");
                AssertEqual("SettingsGenerationChanged", waiterResult.DiscardedReason, "the queued attempt's discard names the generation change");
                AssertFalse(fixture.Service.IsInFlight(running.GroupKey), "the running flight is cleaned up");
                AssertFalse(fixture.Service.IsInFlight(queued.GroupKey), "the queued flight is cleaned up");

                // The single server-wide slot is still honoured: a fresh attempt on a third host runs and
                // commits. A permit stranded by the cancelled waiter would leave it parked forever.
                fixture.Executor.Hold = false;
                UsageCaptureGroup fresh = HostGroup("host-overall-c", AgentRuntimeEnum.ClaudeCode, null, "cpt_overall_fresh");
                UsageCaptureAttempt after = await fixture.Service.TryStartAsync(fresh, null).ConfigureAwait(false);
                AssertTrue(after.Started, "a fresh attempt starts after the cancelled waiter");
                UsageCaptureAttemptResult afterResult = await AwaitAsync(after.Completion).ConfigureAwait(false);
                AssertTrue(afterResult.Persisted, "the fresh attempt commits; the cancelled waiter stranded no server-wide permit");
                AssertEqual(2, fixture.Executor.Calls, "only the held and the fresh attempts reached a credential host");
            }));

            cases.Add(CaseAsync("scheduler_settings_change_cancels_and_reschedules", "A scheduling settings change cancels the work queued under the old bounds and reschedules from scratch", TestTags.Negative, async () =>
            {
                using TestDatabaseFixture fixture = await TestDatabaseFixture.CreateAsync().ConfigureAwait(false);
                Captain captain = await fixture.CreateCaptainAsync("bounds", 300).ConfigureAwait(false);

                UsageCaptureScheduler scheduler = fixture.CreateScheduler();
                await scheduler.RebuildAsync().ConfigureAwait(false);
                UsageCaptureGroup group = scheduler.Groups()[0];
                AssertEqual(300, group.EffectiveIntervalSeconds, "the group starts at its requested interval above the floor");

                // A probe is in flight under the old bounds when the operator raises the global floor.
                fixture.Clock.Set(fixture.Service.NextEligibleUtc(group.GroupKey) ?? fixture.Clock.Now());
                fixture.Executor.Hold = true;
                UsageCaptureAttempt attempt = await fixture.Service.TryStartAsync(group, captain.Id).ConfigureAwait(false);
                AssertTrue(attempt.Started, "the attempt starts under the old bounds");
                long generation = fixture.Service.Generation;

                fixture.Settings.UsageCapture.MinimumIntervalSeconds = 600;
                await scheduler.RebuildAsync().ConfigureAwait(false);

                AssertTrue(fixture.Service.Generation > generation, "raising the global floor invalidates the scheduling generation");
                UsageCaptureGroup floored = scheduler.Groups()[0];
                AssertEqual(600, floored.EffectiveIntervalSeconds, "the rebuilt group runs at the new floor");
                AssertEqual(fixture.Clock.Now().AddSeconds(600), fixture.Service.NextEligibleUtc(group.GroupKey), "the group reschedules from scratch at the new floor");

                // The probe queued under the old bounds is cancelled and its answer is never applied.
                UsageCaptureAttemptResult cancelled = await AwaitAsync(attempt.Completion).ConfigureAwait(false);
                AssertFalse(cancelled.Persisted, "a result from the old bounds is never applied");
                AssertEqual("SettingsGenerationChanged", cancelled.DiscardedReason, "the discard names the settings change");
                AssertEqual("ProbeCancelled", cancelled.ErrorCode, "the probe was cancelled rather than run to its deadline");

                // The rescheduled group runs when due at the new floor and commits.
                fixture.Executor.Hold = false;
                fixture.Clock.Set(fixture.Service.NextEligibleUtc(group.GroupKey) ?? fixture.Clock.Now());
                List<UsageCaptureAttempt> due = await scheduler.TickAsync().ConfigureAwait(false);
                AssertEqual(1, due.Count, "the rescheduled group starts when due at the new floor");
                UsageCaptureAttemptResult result = await AwaitAsync(due[0].Completion).ConfigureAwait(false);
                AssertTrue(result.Persisted, "the rescheduled capture commits");

                // A member's own requested interval is a configuration change too, not only the global
                // bounds: the work queued for the old interval is cancelled the same way.
                long nextGeneration = fixture.Service.Generation;
                fixture.Settings.UsageCapture.MinimumIntervalSeconds = 60;
                captain.UsageCaptureIntervalSeconds = 900;
                await fixture.Database.Captains.UpdateAsync(captain).ConfigureAwait(false);
                await scheduler.RebuildAsync().ConfigureAwait(false);
                AssertTrue(fixture.Service.Generation > nextGeneration, "a member's requested interval change invalidates the generation");
                AssertEqual(900, scheduler.Groups()[0].EffectiveIntervalSeconds, "the rebuilt group runs at the member's new interval");
            }));

            cases.Add(CaseAsync("scheduler_probe_request_carries_the_host_context", "The credential host receives one UsageProbe request carrying the group's identity, mode and deadline", TestTags.Positive, async () =>
            {
                using TestDatabaseFixture fixture = await TestDatabaseFixture.CreateAsync(null, new UsageCaptureSettings
                {
                    Enabled = true,
                    MinimumIntervalSeconds = 60,
                    ProbeTimeoutSeconds = 25
                }).ConfigureAwait(false);

                await fixture.CreateCaptainAsync("context-auto", 300).ConfigureAwait(false);
                string customCommand = "{\"executable\":\"/opt/two\",\"arguments\":[]}";
                await fixture.CreateCustomCaptainAsync("context-custom", customCommand).ConfigureAwait(false);

                UsageCaptureScheduler scheduler = fixture.CreateScheduler();
                await scheduler.RebuildAsync().ConfigureAwait(false);

                UsageCaptureGroup? autoFound = scheduler.Groups().Find(candidate => candidate.Mode == UsageCaptureModeEnum.Auto);
                AssertNotNull(autoFound, "the Auto group exists");
                UsageCaptureGroup autoGroup = autoFound!;
                UsageCaptureGroup? customFound = scheduler.Groups().Find(candidate => candidate.Mode == UsageCaptureModeEnum.Custom);
                AssertNotNull(customFound, "the Custom group exists");
                UsageCaptureGroup customGroup = customFound!;
                AssertEqual(0, fixture.Executor.Calls, "building the schedule dispatches nothing; the probe is attached before the action, not at rebuild");

                fixture.Clock.Set(fixture.Service.NextEligibleUtc(autoGroup.GroupKey) ?? fixture.Clock.Now());
                UsageCaptureAttempt attempt = await fixture.Service.TryStartAsync(autoGroup, null).ConfigureAwait(false);
                UsageCaptureAttemptResult result = await AwaitAsync(attempt.Completion).ConfigureAwait(false);
                AssertTrue(result.Persisted, "the Auto capture commits");

                AssertEqual(1, fixture.Executor.Calls, "exactly one probe reached the credential host");
                HostCommandRequest request = fixture.Executor.Requests[0];
                AssertEqual(HostCommandKindEnum.UsageProbe, request.Kind, "the capture is a usage-probe host command, never a git command");
                AssertEqual(25000, request.TimeoutMs, "the request carries the configured probe deadline in milliseconds");
                AssertContains("usage-capture", request.Label ?? String.Empty, "the request is labelled by the capture it serves");

                UsageProbeHost host = UsageProbeHost.Deserialize(request.UsageProbeJson);
                AssertEqual(TenantId, host.TenantId, "the host context names the owning tenant");
                AssertEqual(autoGroup.HostId, host.HostId, "the host context names the credential host");
                AssertNull(host.HarborId, "a local host carries no Harbor id");
                AssertEqual(AgentRuntimeEnum.ClaudeCode, host.Runtime, "the host context names the group's runtime");
                AssertEqual(UsageCaptureModeEnum.Auto, host.Mode, "the host context names the group's capture mode");
                AssertNull(host.CommandJson, "Auto mode sends no custom command");
                AssertFalse(host.AllowPty, "PTY stays opt-in and is never requested by default");
                AssertEqual(25, host.TimeoutSeconds, "the host context bounds the probe deadline");
                AssertEqual(autoGroup.ConfigurationRevision, host.ConfigurationRevision, "the host context carries the configuration revision the answer is applied against");
                AssertEqual(fixture.Clock.Now(), host.DispatchedUtc, "the host context records the dispatch time on the shared clock");

                UsageSnapshotQuery query = new UsageSnapshotQuery { TenantId = TenantId, PageSize = 10 };
                EnumerationResult<UsageSnapshot> snapshots = await fixture.Database.UsageSnapshots.EnumerateAsync(query).ConfigureAwait(false);
                AssertEqual(1, snapshots.Objects.Count, "the answer the host returned is the snapshot that was stored");
                AssertEqual(autoGroup.HostId, snapshots.Objects[0].HostId, "the stored snapshot belongs to the host the probe ran on");
                AssertEqual(AgentRuntimeEnum.ClaudeCode, snapshots.Objects[0].Runtime, "the stored snapshot belongs to the probed runtime");

                // A Custom group dispatches the administrator's executable contract verbatim, and its
                // configuration revision keeps it distinct from the Auto group on the same host.
                fixture.Clock.Set(fixture.Service.NextEligibleUtc(customGroup.GroupKey) ?? fixture.Clock.Now());
                UsageCaptureAttempt customAttempt = await fixture.Service.TryStartAsync(customGroup, null).ConfigureAwait(false);
                UsageCaptureAttemptResult customResult = await AwaitAsync(customAttempt.Completion).ConfigureAwait(false);
                AssertTrue(customResult.Persisted, "the Custom capture commits");

                AssertEqual(2, fixture.Executor.Calls, "the Custom group made its own dispatch");
                AssertEqual(HostCommandKindEnum.UsageProbe, fixture.Executor.Requests[1].Kind, "the Custom capture is a usage-probe host command too");
                UsageProbeHost customHost = UsageProbeHost.Deserialize(fixture.Executor.Requests[1].UsageProbeJson);
                AssertEqual(UsageCaptureModeEnum.Custom, customHost.Mode, "the Custom group's request names Custom mode");
                AssertEqual(customCommand, customHost.CommandJson, "the custom command travels verbatim, never as shell syntax");
                AssertEqual(AgentRuntimeEnum.Codex, customHost.Runtime, "the Custom group's request names its runtime");
                AssertFalse(String.Equals(customHost.ConfigurationRevision, host.ConfigurationRevision, StringComparison.Ordinal), "the Custom configuration revision differs from the Auto revision");
            }));

            cases.Add(CaseAsync("scheduler_host_exception_is_redacted_and_backs_off", "An executor exception becomes one redacted failure result, never a leak or a crash", TestTags.Negative, async () =>
            {
                using TestDatabaseFixture fixture = await TestDatabaseFixture.CreateAsync().ConfigureAwait(false);
                Captain captain = await fixture.CreateCaptainAsync("throwing", 300).ConfigureAwait(false);

                UsageCaptureScheduler scheduler = fixture.CreateScheduler();
                await scheduler.RebuildAsync().ConfigureAwait(false);
                UsageCaptureGroup group = scheduler.Groups()[0];

                string secret = "sk-live-abcdefghijklmnop0123456789";
                fixture.Executor.ThrowMessage = "remote usage probe failed: Authorization: Bearer " + secret;

                fixture.Clock.Set(fixture.Service.NextEligibleUtc(group.GroupKey) ?? fixture.Clock.Now());
                UsageCaptureAttempt attempt = await fixture.Service.TryStartAsync(group, captain.Id).ConfigureAwait(false);
                AssertTrue(attempt.Started, "the attempt starts");

                UsageCaptureAttemptResult result = await AwaitAsync(attempt.Completion).ConfigureAwait(false);
                AssertEqual(1, fixture.Executor.Calls, "the probe reached the credential host exactly once before it threw");
                AssertTrue(result.Persisted, "the failure is recorded instead of escaping the attempt");
                AssertEqual(UsageSnapshotStatusEnum.Failed, result.Status, "an exception is a failed capture");
                AssertEqual("CaptureAttemptFailed", result.ErrorCode, "the failure carries a stable error code");

                AssertNotNull(result.Snapshot, "the failure snapshot exists");
                UsageSnapshot snapshot = result.Snapshot!;
                AssertEqual("CaptureAttemptFailed", snapshot.ErrorCode, "the snapshot names the failure");
                AssertEqual(TenantId, snapshot.TenantId, "the failure snapshot stays tenant-scoped");
                AssertEqual(group.HostId, snapshot.HostId, "the failure snapshot stays on the credential host");
                AssertNotNull(snapshot.RawExcerpt, "the failure keeps a diagnostic excerpt");
                AssertContains("Bearer [REDACTED]", snapshot.RawExcerpt ?? String.Empty, "the diagnostic is redacted before it is stored");
                AssertFalse((snapshot.RawExcerpt ?? String.Empty).Contains(secret, StringComparison.Ordinal), "the credential never reaches the stored excerpt");

                Job? job = await fixture.Database.Jobs.ReadAsync(attempt.JobId!).ConfigureAwait(false);
                AssertNotNull(job, "the attempt's dedicated job row survives");
                AssertEqual(JobStatusEnum.Failed, job!.Status, "an exception fails the job");
                AssertEqual("CaptureAttemptFailed", job.ErrorReason, "the job carries the stable code, not the exception text");
                AssertFalse((job.ErrorReason ?? String.Empty).Contains(secret, StringComparison.Ordinal), "the credential never reaches the job row");

                AssertEqual(1, fixture.Service.FailureCount(group.GroupKey), "the failure counts once for this attempt");
                int delay = (int)(fixture.Service.NextEligibleUtc(group.GroupKey)!.Value - fixture.Clock.Now()).TotalSeconds;
                AssertEqual(600, delay, "an exception backs off at 2x like any other failure");
                AssertFalse(fixture.Service.IsInFlight(group.GroupKey), "the flight is cleaned up after the exception");

                List<UsageAccountMembership> memberships = await fixture.Database.UsageMemberships.ReadActiveAsync(TenantId, captain.Id).ConfigureAwait(false);
                AssertEqual(0, memberships.Count, "a failure with no identity never opens a membership");

                // The service is still usable afterwards: the next due attempt succeeds and resets backoff.
                fixture.Executor.ThrowMessage = null;
                fixture.Clock.Set(fixture.Service.NextEligibleUtc(group.GroupKey) ?? fixture.Clock.Now());
                UsageCaptureAttempt recovery = await fixture.Service.TryStartAsync(group, captain.Id).ConfigureAwait(false);
                UsageCaptureAttemptResult recoveryResult = await AwaitAsync(recovery.Completion).ConfigureAwait(false);
                AssertEqual(UsageSnapshotStatusEnum.Ok, recoveryResult.Status, "the group recovers after a host exception");
                AssertEqual(0, fixture.Service.FailureCount(group.GroupKey), "the recovery resets the backoff");
            }));

            cases.Add(CaseAsync("scheduler_starting_disabled_never_invalidates_or_dispatches", "A server that starts with capture disabled never invalidates or dispatches until it is enabled", TestTags.Negative, async () =>
            {
                using TestDatabaseFixture fixture = await TestDatabaseFixture.CreateAsync(null, new UsageCaptureSettings
                {
                    Enabled = false,
                    MinimumIntervalSeconds = 60
                }).ConfigureAwait(false);

                await fixture.CreateCaptainAsync("disabled-start", 300).ConfigureAwait(false);

                UsageCaptureScheduler scheduler = fixture.CreateScheduler();
                long generation = fixture.Service.Generation;

                List<UsageCaptureAttempt> first = await scheduler.TickAsync().ConfigureAwait(false);
                AssertEqual(0, first.Count, "a disabled switch starts nothing");
                AssertEqual(generation, fixture.Service.Generation, "the first disabled tick does not invalidate work that never existed");

                List<UsageCaptureAttempt> second = await scheduler.TickAsync().ConfigureAwait(false);
                AssertEqual(0, second.Count, "the disabled switch keeps starting nothing");
                AssertEqual(generation, fixture.Service.Generation, "a server that stays disabled never invalidates a generation");
                AssertEqual(0, fixture.Executor.Calls, "capture disabled means no probe reaches any host");
                AssertEqual(0, scheduler.Groups().Count, "a disabled tick builds no capture groups");

                // Enabling the switch is not a configuration change either: nothing is invalidated, the
                // group is scheduled from now, and it runs when due.
                fixture.Settings.UsageCapture.Enabled = true;
                List<UsageCaptureAttempt> enabled = await scheduler.TickAsync().ConfigureAwait(false);
                AssertEqual(0, enabled.Count, "an enabled tick does not start a group it just scheduled");
                AssertEqual(generation, fixture.Service.Generation, "enabling the switch does not invalidate the generation");
                AssertEqual(1, scheduler.Groups().Count, "the enabled tick builds the captain's group");
                AssertEqual(0, fixture.Executor.Calls, "the freshly scheduled group is not due yet");

                fixture.Clock.Set(fixture.Service.NextEligibleUtc(scheduler.Groups()[0].GroupKey) ?? fixture.Clock.Now());
                List<UsageCaptureAttempt> due = await scheduler.TickAsync().ConfigureAwait(false);
                AssertEqual(1, due.Count, "the due group starts once capture is enabled");
                UsageCaptureAttemptResult result = await AwaitAsync(due[0].Completion).ConfigureAwait(false);
                AssertTrue(result.Persisted, "the enabled capture commits");
                AssertEqual(1, fixture.Executor.Calls, "exactly one probe ran after enabling");
                AssertEqual(generation, fixture.Service.Generation, "no invalidation happened across the disabled-to-enabled sequence");
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

        /// <summary>
        /// Change a concurrency bound after both gates exist and prove the next attempts obey the NEW
        /// bound. The service starts at four/four (or one/one when raising), runs one capture so the
        /// host gate is created and cached at the old capacity, then the limit changes and the scheduler
        /// rebuilds (the scheduling revision includes both bounds, so the rebuild invalidates). Two
        /// distinct groups on ONE host are then started with the executor held: a lowered limit lets
        /// exactly one reach the host until it is released, a raised limit lets both reach it together.
        /// Every attempt still commits, so no permit is stranded by the resize.
        /// </summary>
        private static async Task ConcurrencyLimitChangeAsync(bool overall, bool lower)
        {
            int initial = lower ? 4 : 1;
            using TestDatabaseFixture fixture = await TestDatabaseFixture.CreateAsync(capture: new UsageCaptureSettings
            {
                Enabled = true,
                MinimumIntervalSeconds = 60,
                MaximumConcurrentProbes = initial,
                MaximumConcurrentProbesPerHost = initial
            }).ConfigureAwait(false);

            Captain captain = await fixture.CreateCaptainAsync(lower ? (overall ? "limit-overall" : "limit-host") : "limit-raise", 300).ConfigureAwait(false);
            UsageCaptureScheduler scheduler = fixture.CreateScheduler();
            await scheduler.RebuildAsync().ConfigureAwait(false);

            string hostId = fixture.Accounts.HostId;
            string tag = lower ? (overall ? "overall" : "host") : "raise";

            // One capture on the host creates and caches its gate at the old capacity.
            UsageCaptureAttempt warm = await fixture.Service.TryStartAsync(HostGroup(hostId, AgentRuntimeEnum.Gemini, "fp-limit-warm-" + tag, "cpt_limit_warm_" + tag), null).ConfigureAwait(false);
            AssertTrue(warm.Started, "the warm-up capture starts");
            UsageCaptureAttemptResult warmResult = await AwaitAsync(warm.Completion).ConfigureAwait(false);
            AssertTrue(warmResult.Persisted, "the warm-up capture commits and initializes the host gate");
            int baseline = fixture.Executor.Calls;

            long generation = fixture.Service.Generation;
            if (lower)
            {
                if (overall) fixture.Settings.UsageCapture.MaximumConcurrentProbes = 1;
                else fixture.Settings.UsageCapture.MaximumConcurrentProbesPerHost = 1;
            }
            else
            {
                fixture.Settings.UsageCapture.MaximumConcurrentProbes = 2;
                fixture.Settings.UsageCapture.MaximumConcurrentProbesPerHost = 2;
            }

            await scheduler.RebuildAsync().ConfigureAwait(false);
            AssertTrue(fixture.Service.Generation > generation, "a concurrency limit change invalidates the scheduling generation");

            fixture.Executor.Hold = true;
            UsageCaptureAttempt first = await fixture.Service.TryStartAsync(HostGroup(hostId, AgentRuntimeEnum.Codex, "fp-limit-a-" + tag, "cpt_limit_a_" + tag), null).ConfigureAwait(false);
            UsageCaptureAttempt second = await fixture.Service.TryStartAsync(HostGroup(hostId, AgentRuntimeEnum.Codex, "fp-limit-b-" + tag, "cpt_limit_b_" + tag), null).ConfigureAwait(false);
            AssertTrue(first.Started, "the first attempt after the change starts (" + first.SkippedReason + ")");
            AssertTrue(second.Started, "the second attempt after the change starts");

            int expected = lower ? 1 : 2;
            for (int i = 0; i < 100 && fixture.Executor.Calls < baseline + expected; i++) await Task.Delay(50).ConfigureAwait(false);
            // A settle window so an attempt the old capacity would wrongly admit has time to arrive.
            await Task.Delay(500).ConfigureAwait(false);
            AssertEqual(expected, fixture.Executor.Calls - baseline,
                lower ? "only one probe reaches the host under the lowered limit" : "both probes reach the host under the raised limits");

            if (lower)
            {
                // Releasing the holder admits the queued attempt: the resize never strands a permit.
                fixture.Executor.Release();
                for (int i = 0; i < 100 && fixture.Executor.Calls < baseline + 2; i++) await Task.Delay(50).ConfigureAwait(false);
                AssertEqual(2, fixture.Executor.Calls - baseline, "the queued probe runs once the first releases its permit");
                fixture.Executor.Release();
            }
            else
            {
                fixture.Executor.Release();
                fixture.Executor.Release();
            }

            UsageCaptureAttemptResult firstResult = await AwaitAsync(first.Completion).ConfigureAwait(false);
            UsageCaptureAttemptResult secondResult = await AwaitAsync(second.Completion).ConfigureAwait(false);
            AssertTrue(firstResult.Persisted, "the first attempt commits under the new limit");
            AssertTrue(secondResult.Persisted, "the second attempt commits under the new limit");
            AssertNotNull(captain.Id, "the scheduling captain exists");
        }

        /// <summary>
        /// A hand-built group pinned to one host, so a case can place several groups on the same or on
        /// distinct hosts without depending on dock resolution.
        /// </summary>
        private static UsageCaptureGroup HostGroup(string hostId, AgentRuntimeEnum runtime, string? fingerprint, string memberId)
        {
            return new UsageCaptureGroup
            {
                GroupKey = UsageAccountResolver.GroupKey(TenantId, hostId, runtime, fingerprint),
                ConfigurationRevision = UsageAccountResolver.ConfigurationRevision(hostId, runtime, fingerprint),
                TenantId = TenantId,
                HostId = hostId,
                Runtime = runtime,
                Mode = fingerprint == null ? UsageCaptureModeEnum.Auto : UsageCaptureModeEnum.Custom,
                CommandJson = fingerprint == null ? null : "{\"executable\":\"/opt/permit\",\"arguments\":[]}",
                CommandFingerprint = fingerprint,
                EffectiveIntervalSeconds = 300,
                MemberCaptainIds = new List<string> { memberId }
            };
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

            public static async Task<TestDatabaseFixture> CreateAsync(Func<double>? jitter = null, UsageCaptureSettings? capture = null)
            {
                TestDatabase testDatabase = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                settings.UsageCapture = capture ?? new UsageCaptureSettings { Enabled = true, MinimumIntervalSeconds = 60 };
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

            /// <summary>
            /// When set, the executor throws this message instead of answering, so a case can prove the
            /// capture service turns an escaping host fault into a recorded, redacted failure.
            /// </summary>
            public string? ThrowMessage { get; set; } = null;

            /// <summary>How many probes reached the credential host through this stub.</summary>
            public int Calls { get; private set; } = 0;

            /// <summary>The host commands the capture service dispatched, in order.</summary>
            public List<HostCommandRequest> Requests { get; } = new List<HostCommandRequest>();

            private readonly SemaphoreSlim _Gate = new SemaphoreSlim(0);

            public Task<HostCommandResult> RunAsync(HostCommandRequest request, CancellationToken token = default)
            {
                // Two attempts can dispatch concurrently (the raised-limit case), so count under a lock.
                lock (Requests)
                {
                    Calls++;
                    Requests.Add(request);
                }
                if (!String.IsNullOrWhiteSpace(ThrowMessage)) throw new InvalidOperationException(ThrowMessage);
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