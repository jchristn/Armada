namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// What the Admiral records for the Harbor metrics as the link reports it (HarborMetricsRecorder through
    /// HarborConnectionManager): the job lifecycle (launch, start, first output, exit, stop, refused launch, lost on
    /// reconnect), heartbeat acknowledgements and per-minute samples, link transitions with the reconnect grace, the
    /// startup reconciliation, and a real HarborLinkClient measuring round trips and counting reconnects across sessions.
    /// </summary>
    public sealed class HarborMetricsRecorderSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.HarborMetricsRecorder";
        private static readonly DateTime _Start = new DateTime(2026, 10, 8, 12, 0, 10, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("protocol_roundtrip", "Heartbeat link-health fields and heartbeatAck survive the wire format", TestTags.Positive, () =>
            {
                HarborHeartbeat heartbeat = new HarborHeartbeat { LiveJobIds = new List<string> { "j1" }, Sequence = 9, LastRoundTripMs = 42, ReconnectCount = 3, LastReconnectUtc = _Start };
                HarborHeartbeat? back = HarborProtocol.Deserialize(HarborProtocol.Serialize(heartbeat)) as HarborHeartbeat;
                AssertNotNull(back, "heartbeat");
                AssertEqual((long?)9, back!.Sequence);
                AssertEqual((long?)42, back.LastRoundTripMs);
                AssertEqual((int?)3, back.ReconnectCount);
                AssertEqual((DateTime?)_Start, back.LastReconnectUtc?.ToUniversalTime());

                HarborHeartbeatAck? ack = HarborProtocol.Deserialize(HarborProtocol.Serialize(new HarborHeartbeatAck { Sequence = 9 })) as HarborHeartbeatAck;
                AssertNotNull(ack, "ack");
                AssertEqual(9L, ack!.Sequence);

                HarborHeartbeat? legacy = HarborProtocol.Deserialize("{\"type\":\"heartbeat\",\"liveJobIds\":[]}") as HarborHeartbeat;
                AssertNotNull(legacy, "a heartbeat without the new fields");
                AssertNull(legacy!.Sequence, "no sequence from an older Harbor");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("job_lifecycle", "A launch is recorded and updated through start, first output, and exit; a stop and a refused launch are told apart", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ManualUtcClock clock = new ManualUtcClock(_Start);
                HarborConnectionManager manager = CreateManager(testDb.Driver, clock, out HarborMetricsRecorder _);
                await manager.OnHandshakeAsync(Handshake("hbr_life"), "ten_life", "usr_life", NoopSend).ConfigureAwait(false);

                await manager.LaunchAsync("hbr_life", Launch("job_ok", HarborJobKindEnum.Mission, "ClaudeCode"), new RecordingHarborJobListener()).ConfigureAwait(false);
                HarborJobRecord? launched = await testDb.Driver.HarborJobs.ReadByJobIdAsync("job_ok").ConfigureAwait(false);
                AssertNotNull(launched, "recorded at launch");
                AssertEqual(HarborJobOutcomeEnum.Running, launched!.Outcome);
                AssertEqual("ten_life", launched.TenantId, "the link's tenant");
                AssertEqual(HarborJobKindEnum.Mission, launched.Kind);
                AssertEqual("msn_job_ok", launched.MissionId);

                clock.Advance(TimeSpan.FromSeconds(2));
                await manager.OnMessageAsync("hbr_life", new HarborStarted { JobId = "job_ok", ProcessId = 10 }).ConfigureAwait(false);
                clock.Advance(TimeSpan.FromSeconds(3));
                await manager.OnMessageAsync("hbr_life", new HarborOutput { JobId = "job_ok", Data = "hi" }).ConfigureAwait(false);
                clock.Advance(TimeSpan.FromSeconds(1));
                await manager.OnMessageAsync("hbr_life", new HarborOutput { JobId = "job_ok", Data = "more" }).ConfigureAwait(false);
                clock.Advance(TimeSpan.FromSeconds(10));
                await manager.OnMessageAsync("hbr_life", new HarborExited { JobId = "job_ok", ExitCode = 0, DurationMs = 15500, TimeToFirstTokenMs = 4900 }).ConfigureAwait(false);

                HarborJobRecord done = (await testDb.Driver.HarborJobs.ReadByJobIdAsync("job_ok").ConfigureAwait(false))!;
                AssertEqual(HarborJobOutcomeEnum.Succeeded, done.Outcome);
                AssertEqual((DateTime?)_Start.AddSeconds(2), done.StartedUtc, "started");
                AssertEqual((DateTime?)_Start.AddSeconds(5), done.FirstOutputUtc, "first output kept, later output ignored");
                AssertEqual((DateTime?)_Start.AddSeconds(16), done.EndedUtc, "ended");
                AssertEqual((long?)4900, done.TimeToFirstOutputMs, "the Harbor's measure wins");
                AssertEqual((long?)15500, done.DurationMs);
                AssertEqual((int?)0, done.ExitCode);

                await manager.LaunchAsync("hbr_life", Launch("job_stop", HarborJobKindEnum.AskTurn, "Codex"), new RecordingHarborJobListener()).ConfigureAwait(false);
                await manager.OnMessageAsync("hbr_life", new HarborStarted { JobId = "job_stop", ProcessId = 11 }).ConfigureAwait(false);
                clock.Advance(TimeSpan.FromSeconds(4));
                await manager.KillJobAsync("hbr_life", "job_stop", 100).ConfigureAwait(false);
                await manager.OnMessageAsync("hbr_life", new HarborExited { JobId = "job_stop", ExitCode = 137 }).ConfigureAwait(false);
                HarborJobRecord stopped = (await testDb.Driver.HarborJobs.ReadByJobIdAsync("job_stop").ConfigureAwait(false))!;
                AssertEqual(HarborJobOutcomeEnum.Stopped, stopped.Outcome, "a stop on request is not a failure");
                AssertTrue(stopped.StopRequested, "stop flag");
                AssertEqual((long?)4000, stopped.DurationMs, "duration from the Admiral's clock when the Harbor reports none");
                AssertNull(stopped.TimeToFirstOutputMs, "no output");

                await manager.LaunchAsync("hbr_life", Launch("job_refused", HarborJobKindEnum.Planning, "Gemini"), new RecordingHarborJobListener()).ConfigureAwait(false);
                await manager.OnMessageAsync("hbr_life", new HarborError { JobId = "job_refused", Message = "no gemini here" }).ConfigureAwait(false);
                HarborJobRecord refused = (await testDb.Driver.HarborJobs.ReadByJobIdAsync("job_refused").ConfigureAwait(false))!;
                AssertEqual(HarborJobOutcomeEnum.Failed, refused.Outcome, "a refused launch fails");
                AssertNotNull(refused.EndedUtc, "and ends");

                await manager.LaunchAsync("hbr_life", Launch("job_bad", HarborJobKindEnum.Mission, "ClaudeCode"), new RecordingHarborJobListener()).ConfigureAwait(false);
                await manager.OnMessageAsync("hbr_life", new HarborExited { JobId = "job_bad", ExitCode = 2 }).ConfigureAwait(false);
                AssertEqual(HarborJobOutcomeEnum.Failed, (await testDb.Driver.HarborJobs.ReadByJobIdAsync("job_bad").ConfigureAwait(false))!.Outcome, "non-zero exit fails");
            }));

            cases.Add(CaseAsync("heartbeat_ack_and_samples", "A sequenced heartbeat is acknowledged; heartbeats become one sample per minute", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ManualUtcClock clock = new ManualUtcClock(_Start);
                HarborConnectionManager manager = CreateManager(testDb.Driver, clock, out HarborMetricsRecorder recorder);
                ConcurrentQueue<HarborMessage> sent = new ConcurrentQueue<HarborMessage>();
                await manager.OnHandshakeAsync(Handshake("hbr_hb"), null, null, (message, token) => { sent.Enqueue(message); return Task.CompletedTask; }).ConfigureAwait(false);

                await manager.OnMessageAsync("hbr_hb", new HarborHeartbeat { Sequence = 5, LastRoundTripMs = 40, ReconnectCount = 1, LastReconnectUtc = _Start.AddMinutes(-3) }).ConfigureAwait(false);
                await manager.OnMessageAsync("hbr_hb", new HarborHeartbeat()).ConfigureAwait(false);
                List<HarborHeartbeatAck> acks = sent.OfType<HarborHeartbeatAck>().ToList();
                AssertEqual(1, acks.Count, "only the sequenced heartbeat is acknowledged");
                AssertEqual(5L, acks[0].Sequence);
                AssertEqual(0, (await testDb.Driver.HarborLinkSamples.EnumerateAsync("hbr_hb", _Start.AddHours(-1), _Start.AddHours(1)).ConfigureAwait(false)).Count, "the minute is still open");

                clock.Advance(TimeSpan.FromMinutes(1));
                await manager.OnMessageAsync("hbr_hb", new HarborHeartbeat { Sequence = 6, LastRoundTripMs = 60 }).ConfigureAwait(false);
                List<HarborLinkSample> samples = await testDb.Driver.HarborLinkSamples.EnumerateAsync("hbr_hb", _Start.AddHours(-1), _Start.AddHours(1)).ConfigureAwait(false);
                AssertEqual(1, samples.Count, "the first minute is written when the next begins");
                AssertEqual(new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc), samples[0].BucketStartUtc);
                AssertEqual(2, samples[0].HeartbeatCount);
                AssertEqual(1, samples[0].RoundTripCount);
                AssertEqual(40L, samples[0].RoundTripTotalMs);
                AssertEqual((long?)40, samples[0].RoundTripMaxMs);
                AssertEqual((int?)1, samples[0].ReconnectCount);

                clock.Advance(TimeSpan.FromMinutes(1));
                await recorder.SweepAsync().ConfigureAwait(false);
                HarborLinkSample? latest = await testDb.Driver.HarborLinkSamples.ReadLatestAsync("hbr_hb").ConfigureAwait(false);
                AssertEqual(new DateTime(2026, 10, 8, 12, 1, 0, DateTimeKind.Utc), latest!.BucketStartUtc, "the sweep writes an idle minute");
                AssertEqual(60L, latest.RoundTripTotalMs);
            }));

            cases.Add(CaseAsync("link_events_and_grace", "Connect, close, and a close past the reconnect grace are recorded as link events", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ManualUtcClock clock = new ManualUtcClock(_Start);
                HarborConnectionManager manager = CreateManager(testDb.Driver, clock, out HarborMetricsRecorder recorder);
                await manager.OnHandshakeAsync(Handshake("hbr_link"), null, null, NoopSend).ConfigureAwait(false);
                clock.Advance(TimeSpan.FromMinutes(5));
                await manager.OnDisconnectedAsync("hbr_link").ConfigureAwait(false);

                clock.Advance(TimeSpan.FromSeconds(30));
                await recorder.SweepAsync().ConfigureAwait(false);
                List<HarborLinkEvent> events = await Events(testDb.Driver, "hbr_link").ConfigureAwait(false);
                AssertEqual(2, events.Count, "no disconnect inside the grace");
                AssertEqual(HarborLinkEventTypeEnum.Connected, events[0].EventType);
                AssertEqual(HarborLinkEventTypeEnum.Reconnecting, events[1].EventType);
                AssertEqual(_Start.AddMinutes(5), events[1].OccurredUtc);

                clock.Advance(TimeSpan.FromSeconds(30));
                await recorder.SweepAsync().ConfigureAwait(false);
                await recorder.SweepAsync().ConfigureAwait(false);
                events = await Events(testDb.Driver, "hbr_link").ConfigureAwait(false);
                AssertEqual(3, events.Count, "one disconnect after the grace");
                AssertEqual(HarborLinkEventTypeEnum.Disconnected, events[2].EventType);
                AssertEqual(_Start.AddMinutes(5).AddSeconds(45), events[2].OccurredUtc, "at the end of the grace");

                await manager.OnHandshakeAsync(Handshake("hbr_link"), null, null, NoopSend).ConfigureAwait(false);
                await manager.OnDisconnectedAsync("hbr_link").ConfigureAwait(false);
                await manager.OnHandshakeAsync(Handshake("hbr_link"), null, null, NoopSend).ConfigureAwait(false);
                clock.Advance(TimeSpan.FromMinutes(5));
                await recorder.SweepAsync().ConfigureAwait(false);
                events = await Events(testDb.Driver, "hbr_link").ConfigureAwait(false);
                AssertEqual(HarborLinkEventTypeEnum.Connected, events[events.Count - 1].EventType, "a quick reconnect cancels the pending disconnect");
                AssertEqual(1, events.Count(e => e.EventType == HarborLinkEventTypeEnum.Disconnected), "still one disconnect");
            }));

            cases.Add(CaseAsync("lost_jobs_on_reconnect", "Jobs from an earlier link missing from the first heartbeat after a reconnect are lost; a later exit wins", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ManualUtcClock clock = new ManualUtcClock(_Start);
                HarborConnectionManager manager = CreateManager(testDb.Driver, clock, out HarborMetricsRecorder _);
                await manager.OnHandshakeAsync(Handshake("hbr_lost"), null, null, NoopSend).ConfigureAwait(false);
                await manager.OnMessageAsync("hbr_lost", new HarborHeartbeat()).ConfigureAwait(false);
                await manager.LaunchAsync("hbr_lost", Launch("job_gone", HarborJobKindEnum.Mission, "ClaudeCode"), new RecordingHarborJobListener()).ConfigureAwait(false);
                await manager.LaunchAsync("hbr_lost", Launch("job_alive", HarborJobKindEnum.Mission, "ClaudeCode"), new RecordingHarborJobListener()).ConfigureAwait(false);
                await manager.OnMessageAsync("hbr_lost", new HarborHeartbeat { LiveJobIds = new List<string>() }).ConfigureAwait(false);
                AssertEqual(HarborJobOutcomeEnum.Running, (await testDb.Driver.HarborJobs.ReadByJobIdAsync("job_gone").ConfigureAwait(false))!.Outcome, "later heartbeats on the same link settle nothing");

                clock.Advance(TimeSpan.FromSeconds(20));
                await manager.OnDisconnectedAsync("hbr_lost").ConfigureAwait(false);
                clock.Advance(TimeSpan.FromSeconds(5));
                await manager.OnHandshakeAsync(Handshake("hbr_lost"), null, null, NoopSend).ConfigureAwait(false);
                await manager.LaunchAsync("hbr_lost", Launch("job_new", HarborJobKindEnum.AskTurn, "ClaudeCode"), new RecordingHarborJobListener()).ConfigureAwait(false);
                await manager.OnMessageAsync("hbr_lost", new HarborHeartbeat { LiveJobIds = new List<string> { "job_alive" } }).ConfigureAwait(false);

                AssertEqual(HarborJobOutcomeEnum.Lost, (await testDb.Driver.HarborJobs.ReadByJobIdAsync("job_gone").ConfigureAwait(false))!.Outcome, "missing after the reconnect");
                AssertEqual(HarborJobOutcomeEnum.Running, (await testDb.Driver.HarborJobs.ReadByJobIdAsync("job_alive").ConfigureAwait(false))!.Outcome, "still running on the Harbor");
                AssertEqual(HarborJobOutcomeEnum.Running, (await testDb.Driver.HarborJobs.ReadByJobIdAsync("job_new").ConfigureAwait(false))!.Outcome, "launched on this link");

                await manager.OnMessageAsync("hbr_lost", new HarborExited { JobId = "job_gone", ExitCode = 0 }).ConfigureAwait(false);
                AssertEqual(HarborJobOutcomeEnum.Succeeded, (await testDb.Driver.HarborJobs.ReadByJobIdAsync("job_gone").ConfigureAwait(false))!.Outcome, "a late exit wins");
            }));

            cases.Add(CaseAsync("startup_reconcile", "At startup an open link is closed at its last heartbeat and a pending reconnect at the end of its grace", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                await db.Harbors.CreateAsync(new Harbor { Id = "hbr_open", Name = "Open", LastSeenUtc = _Start.AddMinutes(7) }).ConfigureAwait(false);
                await db.HarborLinkEvents.CreateAsync(new HarborLinkEvent { HarborId = "hbr_open", EventType = HarborLinkEventTypeEnum.Connected, OccurredUtc = _Start }).ConfigureAwait(false);
                await db.HarborLinkEvents.CreateAsync(new HarborLinkEvent { HarborId = "hbr_closing", EventType = HarborLinkEventTypeEnum.Reconnecting, OccurredUtc = _Start }).ConfigureAwait(false);
                await db.HarborLinkEvents.CreateAsync(new HarborLinkEvent { HarborId = "hbr_down", EventType = HarborLinkEventTypeEnum.Disconnected, OccurredUtc = _Start }).ConfigureAwait(false);

                HarborMetricsRecorder recorder = new HarborMetricsRecorder(db, Quiet(), new HarborServerSettings(), () => _Start.AddHours(1));
                AssertEqual(2, await recorder.ReconcileOnStartupAsync().ConfigureAwait(false), "two links left open");

                HarborLinkEvent? open = await db.HarborLinkEvents.ReadLatestBeforeAsync("hbr_open", DateTime.MaxValue).ConfigureAwait(false);
                AssertEqual(HarborLinkEventTypeEnum.Disconnected, open!.EventType);
                AssertEqual(_Start.AddMinutes(7), open.OccurredUtc, "at the last heartbeat");
                HarborLinkEvent? closing = await db.HarborLinkEvents.ReadLatestBeforeAsync("hbr_closing", DateTime.MaxValue).ConfigureAwait(false);
                AssertEqual(_Start.AddSeconds(45), closing!.OccurredUtc, "at the end of the grace");
                AssertEqual(0, await recorder.ReconcileOnStartupAsync().ConfigureAwait(false), "idempotent");
            }));

            cases.Add(CaseAsync("link_client_rtt_and_reconnects", "A HarborLinkClient times acknowledged heartbeats and reports reconnects across sessions", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, Quiet());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, Quiet(), null);
                HarborMetricsRecorder recorder = new HarborMetricsRecorder(testDb.Driver, Quiet(), new HarborServerSettings());
                manager.Metrics = recorder;
                HarborLinkStatistics statistics = new HarborLinkStatistics();

                HarborLinkClient first = NewClient(statistics);
                using (CancellationTokenSource firstSession = new CancellationTokenSource())
                {
                    Task run = first.RunSessionAsync(new LoopbackHarborTransport(manager, "hbr_rtt", null, null), firstSession.Token);
                    bool measured = await AskTestHarness.WaitUntilAsync(() => Task.FromResult(first.LastRoundTripMs.HasValue), 10000).ConfigureAwait(false);
                    AssertTrue(measured, "the first session measures a round trip");
                    AssertTrue(first.LastRoundTripMs!.Value >= 0, "non-negative");
                    AssertEqual(0, statistics.ReconnectCount, "the first session is not a reconnect");
                    firstSession.Cancel();
                    try { await run.ConfigureAwait(false); } catch (OperationCanceledException) { }
                }

                // The loopback transport reports the close when the session ends; a repeated close is not another drop.
                await manager.OnDisconnectedAsync("hbr_rtt").ConfigureAwait(false);

                HarborLinkClient second = NewClient(statistics);
                using (CancellationTokenSource secondSession = new CancellationTokenSource())
                {
                    Task run = second.RunSessionAsync(new LoopbackHarborTransport(manager, "hbr_rtt", null, null), secondSession.Token);
                    bool reported = await AskTestHarness.WaitUntilAsync(async () =>
                    {
                        await recorder.FlushAsync().ConfigureAwait(false);
                        List<HarborLinkSample> samples = await testDb.Driver.HarborLinkSamples.EnumerateAsync("hbr_rtt", DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1)).ConfigureAwait(false);
                        return samples.Any(s => s.ReconnectCount == 1 && s.LastReconnectUtc.HasValue && s.RoundTripCount > 0);
                    }, 10000).ConfigureAwait(false);
                    AssertTrue(reported, "the second session's heartbeats report one reconnect and a round trip");
                    AssertEqual(1, statistics.ReconnectCount);
                    AssertEqual(2, statistics.AcceptedSessions);
                    secondSession.Cancel();
                    try { await run.ConfigureAwait(false); } catch (OperationCanceledException) { }
                }

                List<HarborLinkEvent> events = await Events(testDb.Driver, "hbr_rtt").ConfigureAwait(false);
                AssertEqual("Connected,Reconnecting,Connected,Reconnecting", String.Join(",", events.Select(e => e.EventType.ToString())), "one event per transition");
            }));

            return new TestSuiteDescriptor(SuiteId, "Harbor Metrics Recorder", cases);
        }

        #endregion

        #region Private-Methods

        private static HarborLinkClient NewClient(HarborLinkStatistics statistics)
        {
            HarborLinkClient client = new HarborLinkClient("hbr_rtt", "Rig", new List<HarborCapability>(), 2, new LocalHostCommandExecutor(), Quiet(), 20);
            client.LinkStatistics = statistics;
            return client;
        }

        private static async Task<List<HarborLinkEvent>> Events(DatabaseDriver db, string harborId)
        {
            return await db.HarborLinkEvents.EnumerateAsync(harborId, DateTime.MinValue.AddYears(1), DateTime.MaxValue.AddYears(-1)).ConfigureAwait(false);
        }

        private static HarborConnectionManager CreateManager(DatabaseDriver db, ManualUtcClock clock, out HarborMetricsRecorder recorder)
        {
            HarborService harbors = new HarborService(db, Quiet());
            HarborConnectionManager manager = new HarborConnectionManager(harbors, Quiet(), null);
            recorder = new HarborMetricsRecorder(db, Quiet(), new HarborServerSettings(), clock.Now);
            manager.Metrics = recorder;
            return manager;
        }

        private static HarborHandshake Handshake(string harborId)
        {
            return new HarborHandshake { HarborId = harborId, Name = "Rig", ProtocolVersion = HarborProtocol.Version, MaxConcurrentJobs = 4 };
        }

        private static HarborLaunchRequest Launch(string jobId, HarborJobKindEnum kind, string runtime)
        {
            return new HarborLaunchRequest
            {
                JobId = jobId,
                Runtime = runtime,
                JobKind = kind.ToString(),
                MissionId = kind == HarborJobKindEnum.Mission ? "msn_" + jobId : null,
                CaptainId = "cpt_metrics"
            };
        }

        private static Task NoopSend(HarborMessage message, CancellationToken token) => Task.CompletedTask;

        private static LoggingModule Quiet()
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
    }
}
