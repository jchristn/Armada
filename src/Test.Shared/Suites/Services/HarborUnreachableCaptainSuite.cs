namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// A captain whose Harbor loses its link is unreachable, not stalled: no recovery starts and nothing is counted while
    /// the link is down (for up to HarborDisconnectGraceMinutes). A stop that cannot be confirmed is recorded once and
    /// retried with backoff, survives an Admiral restart, and is delivered (or cancelled) when the Harbor reconnects.
    /// Uses a stand-in Harbor linked through a real HarborConnectionManager and one settable clock.
    /// </summary>
    public sealed class HarborUnreachableCaptainSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.HarborUnreachable";
        private static readonly TimeSpan _Tick = TimeSpan.FromSeconds(10);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("disconnected_harbor_holds_captain_unreachable", "A silent captain on a disconnected Harbor is unreachable: one captain.unreachable, no stall, no stop, no attempt counted", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborUnreachableFixture fixture = await HarborUnreachableFixture.CreateAsync(testDb.Driver, CreateSettings(), TimeSpan.FromMinutes(15)).ConfigureAwait(false);
                await fixture.DisconnectAsync().ConfigureAwait(false);
                DateTime lastActivity = (await fixture.ReadCaptainAsync().ConfigureAwait(false)).LastHeartbeatUtc!.Value;

                for (int i = 0; i < 12; i++) await fixture.TickAsync(_Tick).ConfigureAwait(false);

                List<ArmadaEvent> unreachable = await fixture.EventsAsync("captain.unreachable").ConfigureAwait(false);
                AssertEqual(1, unreachable.Count, "one captain.unreachable per episode");
                CaptainUnreachablePayload payload = JsonSerializer.Deserialize<CaptainUnreachablePayload>(unreachable[0].Payload!)!;
                AssertEqual(fixture.HarborId, payload.HarborId);
                AssertEqual(fixture.Captain.Id, payload.CaptainId);
                AssertEqual<DateTime?>(lastActivity, payload.LastActivityUtc, "the last activity is recorded");
                AssertTrue(payload.Since <= payload.DetectedUtc, "the link was lost before it was detected");
                AssertEqual(60, payload.GraceMinutes);
                AssertEqual(0, (await fixture.EventsAsync("captain.stalled").ConfigureAwait(false)).Count, "not judged stalled");
                AssertEqual(0, (await fixture.EventsAsync("captain.stop_unconfirmed").ConfigureAwait(false)).Count);
                AssertEqual(0, fixture.StopCalls, "no recovery starts");
                AssertEqual(0, fixture.Launches);
                AssertEqual(0, (await fixture.ReadCaptainAsync().ConfigureAwait(false)).RecoveryAttempts, "no attempt counted");
                Mission mission = await fixture.ReadMissionAsync().ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.InProgress, mission.Status, "the mission keeps running");
                AssertNull(mission.FailureReason);
            }));

            cases.Add(CaseAsync("connected_harbor_stall_is_still_recovered", "A silent captain on a connected Harbor is judged stalled and recovered as before", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborUnreachableFixture fixture = await HarborUnreachableFixture.CreateAsync(testDb.Driver, CreateSettings(), TimeSpan.FromMinutes(15)).ConfigureAwait(false);

                await fixture.TickAsync(_Tick).ConfigureAwait(false);

                AssertEqual(0, (await fixture.EventsAsync("captain.unreachable").ConfigureAwait(false)).Count, "a linked Harbor is reachable");
                List<ArmadaEvent> stalled = await fixture.EventsAsync("captain.stalled").ConfigureAwait(false);
                AssertEqual(1, stalled.Count);
                AssertContains("Automatic recovery 1 of 3 will be attempted", stalled[0].Message);
                CaptainStalledPayload payload = JsonSerializer.Deserialize<CaptainStalledPayload>(stalled[0].Payload!)!;
                AssertEqual(1, payload.Attempt);
                AssertFalse(payload.WaitingOnStop);
                AssertEqual(1, fixture.Kills.Count, "the stop reaches the Harbor");
                AssertEqual(1, fixture.Launches, "the captain is relaunched");
                AssertEqual(1, (await fixture.ReadCaptainAsync().ConfigureAwait(false)).RecoveryAttempts);
                AssertEqual(0, (await fixture.EventsAsync("captain.stop_unconfirmed").ConfigureAwait(false)).Count);
            }));

            cases.Add(CaseAsync("reconnect_restarts_the_stall_clock", "When the Harbor reconnects, monitoring resumes with the stall clock at the reconnect, then a real stall is still caught", TestTags.Reliability, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborUnreachableFixture fixture = await HarborUnreachableFixture.CreateAsync(testDb.Driver, CreateSettings(), TimeSpan.FromMinutes(1)).ConfigureAwait(false);
                await fixture.DisconnectAsync().ConfigureAwait(false);
                for (int i = 0; i < 180; i++) await fixture.TickAsync(_Tick).ConfigureAwait(false);

                await fixture.LinkAsync(true).ConfigureAwait(false);
                DateTime reconnected = fixture.Now;
                await fixture.TickAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                AssertEqual(0, (await fixture.EventsAsync("captain.reconciled").ConfigureAwait(false)).Count, "waits for the Harbor's first report after the reconnect");
                await fixture.HeartbeatAsync(true).ConfigureAwait(false);
                await fixture.TickAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

                List<ArmadaEvent> reconciled = await fixture.EventsAsync("captain.reconciled").ConfigureAwait(false);
                AssertEqual(1, reconciled.Count);
                CaptainReconciledPayload payload = JsonSerializer.Deserialize<CaptainReconciledPayload>(reconciled[0].Payload!)!;
                AssertEqual(CaptainReconcileOutcomeEnum.Resumed, payload.Outcome);
                AssertEqual<DateTime?>(reconnected, payload.ReconnectedUtc);
                DateTime restarted = (await fixture.ReadCaptainAsync().ConfigureAwait(false)).LastHeartbeatUtc!.Value;
                AssertTrue(Math.Abs((restarted - reconnected).TotalMilliseconds) < 1, "the stall clock restarts at the reconnect");

                // Within the threshold after the reconnect the captain is not judged stalled, although it was silent for 31 minutes.
                for (int i = 0; i < 54; i++) await fixture.TickAsync(_Tick).ConfigureAwait(false);
                AssertEqual(0, (await fixture.EventsAsync("captain.stalled").ConfigureAwait(false)).Count, "not stalled within the threshold after the reconnect");
                AssertEqual(0, fixture.StopCalls);

                // Silent for longer than the threshold after the reconnect: a real stall, recovered on the linked Harbor.
                for (int i = 0; i < 12 && fixture.Launches == 0; i++) await fixture.TickAsync(_Tick).ConfigureAwait(false);
                AssertEqual(1, (await fixture.EventsAsync("captain.stalled").ConfigureAwait(false)).Count, "a stall after the reconnect is still caught");
                double sinceReconnect = (fixture.Now - reconnected).TotalMinutes;
                AssertTrue(sinceReconnect > 10 && sinceReconnect < 10.5, "judged by the threshold from the reconnect (" + sinceReconnect.ToString("F2") + " min)");
                AssertEqual(1, fixture.Kills.Count, "and stopped on the reconnected Harbor");
                AssertEqual(1, fixture.Launches);
            }));

            cases.Add(CaseAsync("sixty_minute_disconnect_is_bounded", "A 60-minute Harbor outage with a health check every 10 s records a handful of captain events, launches nothing, and the mission resumes", TestTags.Reliability, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborUnreachableFixture fixture = await HarborUnreachableFixture.CreateAsync(testDb.Driver, CreateSettings(), TimeSpan.FromMinutes(2)).ConfigureAwait(false);
                await fixture.DisconnectAsync().ConfigureAwait(false);

                for (int i = 0; i < 359; i++) await fixture.TickAsync(_Tick).ConfigureAwait(false);

                List<ArmadaEvent> during = await fixture.CaptainEventsAsync().ConfigureAwait(false);
                AssertTrue(during.Count <= 3, "a handful of captain events, not one per tick (" + during.Count + ")");
                AssertEqual(1, during.Count(e => e.EventType == "captain.unreachable"));
                AssertEqual(0, during.Count(e => e.EventType == "captain.stalled"));
                AssertEqual(0, during.Count(e => e.EventType == "captain.stop_unconfirmed"));
                AssertEqual(0, fixture.StopCalls, "no stop while the link is down");
                AssertEqual(0, fixture.Launches, "no recovery launched");
                AssertEqual(MissionStatusEnum.InProgress, (await fixture.ReadMissionAsync().ConfigureAwait(false)).Status);

                await fixture.LinkAsync(true).ConfigureAwait(false);
                await fixture.HeartbeatAsync(true).ConfigureAwait(false);
                for (int i = 0; i < 120; i++)
                {
                    await fixture.TickAsync(_Tick).ConfigureAwait(false);
                    if (i % 6 == 0) await fixture.ReportActivityAsync(fixture.Now).ConfigureAwait(false);
                }

                List<ArmadaEvent> after = await fixture.CaptainEventsAsync().ConfigureAwait(false);
                AssertEqual(2, after.Count, "unreachable, then reconciled");
                AssertEqual("captain.reconciled", after[1].EventType);
                AssertEqual(0, fixture.StopCalls);
                AssertEqual(0, fixture.Launches);
                Captain captain = await fixture.ReadCaptainAsync().ConfigureAwait(false);
                AssertEqual(0, captain.RecoveryAttempts);
                AssertEqual(CaptainStateEnum.Working, captain.State);
                AssertEqual(MissionStatusEnum.InProgress, (await fixture.ReadMissionAsync().ConfigureAwait(false)).Status, "the mission resumes normally");
            }));

            cases.Add(CaseAsync("grace_elapsed_escalates_with_backoff", "After HarborDisconnectGraceMinutes the captain is handed to stall recovery: one stop_unconfirmed, attempts advance, retries back off", TestTags.Reliability, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = CreateSettings();
                settings.HarborDisconnectGraceMinutes = 5;
                HarborUnreachableFixture fixture = await HarborUnreachableFixture.CreateAsync(testDb.Driver, settings, TimeSpan.FromMinutes(15)).ConfigureAwait(false);
                await fixture.DisconnectAsync().ConfigureAwait(false);

                for (int i = 0; i < 180; i++) await fixture.TickAsync(_Tick).ConfigureAwait(false);

                AssertEqual(1, (await fixture.EventsAsync("captain.unreachable").ConfigureAwait(false)).Count);
                List<ArmadaEvent> unconfirmed = await fixture.EventsAsync("captain.stop_unconfirmed").ConfigureAwait(false);
                AssertEqual(1, unconfirmed.Count, "one captain.stop_unconfirmed for the episode");
                AssertContains("Recovery is waiting", unconfirmed[0].Message);
                AssertContains("Automatic recovery 1 of 3 is waiting", unconfirmed[0].Message);
                CaptainStopUnconfirmedPayload stop = JsonSerializer.Deserialize<CaptainStopUnconfirmedPayload>(unconfirmed[0].Payload!)!;
                AssertEqual(fixture.HarborId, stop.HarborId);
                AssertEqual(1, stop.RetryDelayMinutes);
                AssertEqual(CaptainStopPurposeEnum.Recovery, stop.Purpose);

                // Attempts at 5, 6, 8 (recovery 1-3), then 12 and 20 (exhausted, backoff 8, then 15): five in 25 minutes.
                List<ArmadaEvent> stalled = await fixture.EventsAsync("captain.stalled").ConfigureAwait(false);
                AssertEqual(5, stalled.Count, "one captain.stalled per real attempt, not per tick");
                AssertContains("Automatic recovery 1 of 3 will be attempted", stalled[0].Message);
                AssertContains("Automatic recovery 2 of 3 is attempted after waiting", stalled[1].Message);
                AssertContains("Automatic recovery 3 of 3 is attempted after waiting", stalled[2].Message);
                AssertContains("exhausted", stalled[3].Message);
                AssertEqual(5, fixture.StopCalls, "one stop per attempt");
                AssertEqual(0, fixture.Launches, "no replacement while the stop is unconfirmed");
                AssertEqual(3, (await fixture.ReadCaptainAsync().ConfigureAwait(false)).RecoveryAttempts, "the counter advances on each real attempt");
                Mission mission = await fixture.ReadMissionAsync().ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.InProgress, mission.Status, "ownership is retained while the stop is unconfirmed");
                AssertEqual(MissionFailureKindEnum.Infra, mission.FailureKind);
            }));

            cases.Add(CaseAsync("zero_grace_judges_unreachable_at_once", "HarborDisconnectGraceMinutes 0 hands an unreachable captain to stall recovery on the first check", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = CreateSettings();
                settings.HarborDisconnectGraceMinutes = 0;
                HarborUnreachableFixture fixture = await HarborUnreachableFixture.CreateAsync(testDb.Driver, settings, TimeSpan.FromMinutes(15)).ConfigureAwait(false);
                await fixture.DisconnectAsync().ConfigureAwait(false);

                for (int i = 0; i < 5; i++) await fixture.TickAsync(_Tick).ConfigureAwait(false);

                AssertEqual(1, (await fixture.EventsAsync("captain.unreachable").ConfigureAwait(false)).Count);
                AssertEqual(1, (await fixture.EventsAsync("captain.stalled").ConfigureAwait(false)).Count, "judged once, then waits on the stop");
                AssertEqual(1, (await fixture.EventsAsync("captain.stop_unconfirmed").ConfigureAwait(false)).Count);
                AssertEqual(1, fixture.StopCalls);
                AssertEqual(1, (await fixture.ReadCaptainAsync().ConfigureAwait(false)).RecoveryAttempts);
            }));

            cases.Add(CaseAsync("settings_grace_is_clamped_and_written", "HarborDisconnectGraceMinutes defaults to 60, is clamped to [0, 1440], and round-trips through settings JSON", TestTags.Positive, () =>
            {
                ArmadaSettings defaults = JsonSerializer.Deserialize<ArmadaSettings>("{}")!;
                AssertEqual(60, defaults.HarborDisconnectGraceMinutes);
                defaults.HarborDisconnectGraceMinutes = -5;
                AssertEqual(0, defaults.HarborDisconnectGraceMinutes);
                defaults.HarborDisconnectGraceMinutes = 5000;
                AssertEqual(1440, defaults.HarborDisconnectGraceMinutes);
                defaults.HarborDisconnectGraceMinutes = 30;
                ArmadaSettings roundTrip = JsonSerializer.Deserialize<ArmadaSettings>(JsonSerializer.Serialize(defaults))!;
                AssertEqual(30, roundTrip.HarborDisconnectGraceMinutes);
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("unconfirmed_stop_backoff_survives_restart", "A waiting unconfirmed stop is not retried on the next checks, nor right after an Admiral restart, and is retried once the backoff elapses", TestTags.Reliability, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = CreateSettings();
                settings.HarborDisconnectGraceMinutes = 0;
                HarborUnreachableFixture fixture = await HarborUnreachableFixture.CreateAsync(testDb.Driver, settings, TimeSpan.FromMinutes(15)).ConfigureAwait(false);
                await fixture.DisconnectAsync().ConfigureAwait(false);
                await fixture.TickAsync(_Tick).ConfigureAwait(false);
                AssertEqual(1, fixture.StopCalls);

                for (int i = 0; i < 2; i++) await fixture.TickAsync(_Tick).ConfigureAwait(false);
                AssertEqual(1, fixture.StopCalls, "waiting: no retry on the next checks");

                fixture.RestartAdmiral();
                await fixture.TickAsync(_Tick).ConfigureAwait(false);
                AssertEqual(1, fixture.StopCalls, "an Admiral restart does not restart the retries");
                AssertEqual(1, (await fixture.EventsAsync("captain.unreachable").ConfigureAwait(false)).Count, "the restarted Admiral continues the episode");
                AssertEqual(1, (await fixture.EventsAsync("captain.stop_unconfirmed").ConfigureAwait(false)).Count);

                await fixture.TickAsync(TimeSpan.FromSeconds(40)).ConfigureAwait(false);
                AssertEqual(2, fixture.StopCalls, "retried once the 1-minute backoff elapsed");
                AssertEqual(1, (await fixture.EventsAsync("captain.stop_unconfirmed").ConfigureAwait(false)).Count, "still one per episode");
                AssertEqual(2, (await fixture.ReadCaptainAsync().ConfigureAwait(false)).RecoveryAttempts);
            }));

            cases.Add(CaseAsync("reconnect_redelivers_stop_to_stuck_job", "On reconnect a pending stop of a job that is still running and silent is re-sent, confirmed, and recovery continues with one attempt counted", TestTags.Reliability, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = CreateSettings();
                settings.HarborDisconnectGraceMinutes = 1;
                HarborUnreachableFixture fixture = await HarborUnreachableFixture.CreateAsync(testDb.Driver, settings, TimeSpan.FromMinutes(15)).ConfigureAwait(false);
                await fixture.DisconnectAsync().ConfigureAwait(false);
                for (int i = 0; i < 7; i++) await fixture.TickAsync(_Tick).ConfigureAwait(false);
                AssertEqual(1, (await fixture.EventsAsync("captain.stop_unconfirmed").ConfigureAwait(false)).Count);
                AssertEqual(1, (await fixture.ReadCaptainAsync().ConfigureAwait(false)).RecoveryAttempts);
                AssertEqual(0, fixture.Kills.Count);

                await fixture.LinkAsync(true).ConfigureAwait(false);
                await fixture.HeartbeatAsync(true).ConfigureAwait(false);
                await fixture.TickAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

                List<ArmadaEvent> redelivered = await fixture.EventsAsync("captain.stop_redelivered").ConfigureAwait(false);
                AssertEqual(1, redelivered.Count);
                CaptainStopRedeliveredPayload payload = JsonSerializer.Deserialize<CaptainStopRedeliveredPayload>(redelivered[0].Payload!)!;
                AssertTrue(payload.Confirmed, "the reconnected Harbor confirms the stop");
                AssertEqual(fixture.HarborId, payload.HarborId);
                AssertEqual(1, fixture.Kills.Count, "the stop reached the Harbor after the reconnect");
                List<ArmadaEvent> reconciled = await fixture.EventsAsync("captain.reconciled").ConfigureAwait(false);
                AssertEqual(1, reconciled.Count);
                AssertEqual(CaptainReconcileOutcomeEnum.Stopped, JsonSerializer.Deserialize<CaptainReconciledPayload>(reconciled[0].Payload!)!.Outcome);
                AssertEqual(1, fixture.Launches, "recovery relaunches the captain");
                Captain captain = await fixture.ReadCaptainAsync().ConfigureAwait(false);
                AssertEqual(1, captain.RecoveryAttempts, "the waiting attempt is the one that relaunched, counted once");
                Mission mission = await fixture.ReadMissionAsync().ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.InProgress, mission.Status);
                AssertNull(mission.FailureReason, "the unconfirmed stop no longer describes the mission");
            }));

            cases.Add(CaseAsync("reconnect_cancels_stop_for_working_job", "On reconnect a pending stop of a job that reported activity since is cancelled and its attempts are given back", TestTags.Reliability, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = CreateSettings();
                settings.HarborDisconnectGraceMinutes = 1;
                HarborUnreachableFixture fixture = await HarborUnreachableFixture.CreateAsync(testDb.Driver, settings, TimeSpan.FromMinutes(15)).ConfigureAwait(false);
                await fixture.DisconnectAsync().ConfigureAwait(false);
                for (int i = 0; i < 7; i++) await fixture.TickAsync(_Tick).ConfigureAwait(false);
                AssertEqual(1, (await fixture.ReadCaptainAsync().ConfigureAwait(false)).RecoveryAttempts);

                await fixture.LinkAsync(true).ConfigureAwait(false);
                await fixture.HeartbeatAsync(true).ConfigureAwait(false);
                await fixture.ReportActivityAsync(fixture.Now.AddSeconds(1)).ConfigureAwait(false);
                await fixture.TickAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

                AssertEqual(0, fixture.Kills.Count, "the working job is not stopped");
                AssertEqual(0, (await fixture.EventsAsync("captain.stop_redelivered").ConfigureAwait(false)).Count);
                List<ArmadaEvent> reconciled = await fixture.EventsAsync("captain.reconciled").ConfigureAwait(false);
                AssertEqual(1, reconciled.Count);
                AssertEqual(CaptainReconcileOutcomeEnum.Resumed, JsonSerializer.Deserialize<CaptainReconciledPayload>(reconciled[0].Payload!)!.Outcome);
                AssertEqual(0, fixture.Launches);
                AssertEqual(0, (await fixture.ReadCaptainAsync().ConfigureAwait(false)).RecoveryAttempts, "attempts spent waiting are given back");
                Mission mission = await fixture.ReadMissionAsync().ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.InProgress, mission.Status);
                AssertNull(mission.FailureReason);
                AssertNull(mission.FailureKind);

                for (int i = 0; i < 12; i++) await fixture.TickAsync(_Tick).ConfigureAwait(false);
                AssertEqual(1, fixture.StopCalls, "no further stop attempts after the episode ended");
            }));

            cases.Add(CaseAsync("reconnect_with_exited_job_waits_for_its_exit", "On reconnect a job the Harbor no longer runs is not stopped or relaunched by the health check; its exit settles the episode", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = CreateSettings();
                settings.HarborDisconnectGraceMinutes = 1;
                HarborUnreachableFixture fixture = await HarborUnreachableFixture.CreateAsync(testDb.Driver, settings, TimeSpan.FromMinutes(15)).ConfigureAwait(false);
                await fixture.DisconnectAsync().ConfigureAwait(false);
                for (int i = 0; i < 7; i++) await fixture.TickAsync(_Tick).ConfigureAwait(false);
                int stopsBefore = fixture.StopCalls;

                await fixture.LinkAsync(false).ConfigureAwait(false);
                await fixture.HeartbeatAsync(false).ConfigureAwait(false);
                for (int i = 0; i < 3; i++) await fixture.TickAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

                AssertEqual(stopsBefore, fixture.StopCalls, "no stop for a job that already ended");
                AssertEqual(0, fixture.Kills.Count);
                AssertEqual(0, fixture.Launches, "the exit path, not the health check, decides");
                AssertEqual(0, (await fixture.EventsAsync("captain.reconciled").ConfigureAwait(false)).Count);
            }));

            cases.Add(CaseAsync("local_unconfirmed_stop_backs_off", "A stop that fails on the Admiral host is retried at 1, 2, 4 minutes, not on every check", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = CreateSettings();
                settings.MaxRecoveryAttempts = 3;
                StallTestFixture fixture = await StallTestFixture.CreateAsync(testDb.Driver, settings, DateTime.UtcNow.AddMinutes(-20)).ConfigureAwait(false);
                SettableTimeProvider clock = new SettableTimeProvider();
                clock.Advance(DateTimeOffset.UtcNow - clock.GetUtcNow());
                fixture.Admiral.Time = clock;
                int stops = 0;
                fixture.Admiral.OnStopAgent = _ =>
                {
                    Interlocked.Increment(ref stops);
                    throw new TimeoutException("The stop was not confirmed.");
                };

                for (int i = 0; i < 48; i++)
                {
                    await fixture.Admiral.HealthCheckAsync().ConfigureAwait(false);
                    clock.Advance(_Tick);
                }

                AssertEqual(4, stops, "attempts at 0, 1, 3 and 7 minutes");
                List<ArmadaEvent> events = await testDb.Driver.Events.EnumerateByMissionAsync(fixture.Mission.Id, 1000).ConfigureAwait(false);
                AssertEqual(1, events.Count(e => e.EventType == "captain.stop_unconfirmed"));
                AssertEqual(4, events.Count(e => e.EventType == "captain.stalled"));
                AssertEqual(0, fixture.Launched.Count);
                AssertEqual(MissionStatusEnum.InProgress, (await testDb.Driver.Missions.ReadAsync(fixture.Mission.Id).ConfigureAwait(false))!.Status);
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Unreachable Harbor Captains", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static ArmadaSettings CreateSettings()
        {
            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = Path.Combine(Path.GetTempPath(), "armada_test_docks_" + Guid.NewGuid().ToString("N"));
            settings.ReposDirectory = Path.Combine(Path.GetTempPath(), "armada_test_repos_" + Guid.NewGuid().ToString("N"));
            settings.MaxMissionRuntimeMinutes = 0;
            settings.StallThresholdMinutes = 10;
            settings.MaxRecoveryAttempts = 3;
            return settings;
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
    }
}
