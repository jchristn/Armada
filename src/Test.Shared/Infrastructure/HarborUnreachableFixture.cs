namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// A Working captain whose process is a job on a stand-in Harbor, linked through a real
    /// <see cref="HarborConnectionManager"/> and watched by a real <see cref="AdmiralService"/> health check. One settable
    /// clock drives both, so a test can play an hour-long Harbor outage in health-check steps. The stand-in Harbor
    /// confirms a stop by reporting the job's exit; OnStopAgent stops through the manager as the Admiral's lifecycle
    /// handler does (it fails while the Harbor is disconnected), and OnLaunchAgent records relaunches.
    /// </summary>
    public sealed class HarborUnreachableFixture
    {
        #region Public-Members

        /// <summary>
        /// Clock shared by the Admiral and the connection manager.
        /// </summary>
        public SettableTimeProvider Clock { get; } = new SettableTimeProvider();

        /// <summary>
        /// Connection manager the stand-in Harbor links through.
        /// </summary>
        public HarborConnectionManager Manager { get; private set; } = null!;

        /// <summary>
        /// Admiral under test.
        /// </summary>
        public AdmiralService Admiral { get; private set; } = null!;

        /// <summary>
        /// Settings shared by the services.
        /// </summary>
        public ArmadaSettings Settings { get; private set; } = null!;

        /// <summary>
        /// Stand-in Harbor identifier.
        /// </summary>
        public string HarborId { get; } = "hbr_unreachable_" + Guid.NewGuid().ToString("N").Substring(0, 8);

        /// <summary>
        /// The captain's job on the Harbor.
        /// </summary>
        public string JobId { get; } = "job_unreachable_" + Guid.NewGuid().ToString("N").Substring(0, 8);

        /// <summary>
        /// Admiral process handle of the job.
        /// </summary>
        public int ProcessHandle { get; private set; } = 0;

        /// <summary>
        /// The captain.
        /// </summary>
        public Captain Captain { get; private set; } = null!;

        /// <summary>
        /// The captain's InProgress mission.
        /// </summary>
        public Mission Mission { get; private set; } = null!;

        /// <summary>
        /// Stop requests the Harbor received.
        /// </summary>
        public List<HarborKillRequest> Kills { get; } = new List<HarborKillRequest>();

        /// <summary>
        /// Calls of OnStopAgent (delivered or not).
        /// </summary>
        public int StopCalls => _StopCalls;

        /// <summary>
        /// Calls of OnLaunchAgent (relaunches).
        /// </summary>
        public int Launches => _Launches;

        /// <summary>
        /// Listener of the job's lifecycle.
        /// </summary>
        public RecordingHarborJobListener Listener { get; } = new RecordingHarborJobListener();

        /// <summary>
        /// The shared clock's current time (UTC).
        /// </summary>
        public DateTime Now => Clock.GetUtcNow().UtcDateTime;

        #endregion

        #region Private-Members

        private DatabaseDriver _Db = null!;
        private LoggingModule _Logging = null!;
        private CaptainService _Captains = null!;
        private IMissionService _Missions = null!;
        private IDockService _Docks = null!;
        private int _StopCalls = 0;
        private int _Launches = 0;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create the services, link the Harbor, start the job, and create the captain and mission.
        /// </summary>
        /// <param name="db">Database driver.</param>
        /// <param name="settings">Settings.</param>
        /// <param name="heartbeatAge">How long ago the captain last reported activity.</param>
        /// <returns>The fixture.</returns>
        public static async Task<HarborUnreachableFixture> CreateAsync(DatabaseDriver db, ArmadaSettings settings, TimeSpan heartbeatAge)
        {
            HarborUnreachableFixture fixture = new HarborUnreachableFixture();
            fixture._Db = db;
            fixture.Settings = settings;
            fixture.Clock.Advance(DateTimeOffset.UtcNow - fixture.Clock.GetUtcNow());
            fixture._Logging = new LoggingModule();
            fixture._Logging.Settings.EnableConsole = false;

            fixture.Manager = new HarborConnectionManager(new HarborService(db, fixture._Logging), fixture._Logging, null);
            fixture.Manager.Clock = () => fixture.Clock.GetUtcNow().UtcDateTime;
            fixture.Manager.SweepInterval = TimeSpan.Zero;

            StubGitService git = new StubGitService { IsRepositoryResult = true };
            fixture._Docks = new DockService(fixture._Logging, db, settings, git);
            fixture._Captains = new CaptainService(fixture._Logging, db, settings, git, fixture._Docks);
            fixture._Missions = new MissionService(fixture._Logging, db, settings, fixture._Docks, fixture._Captains);
            fixture.Admiral = fixture.NewAdmiral();

            fixture._Captains.OnStopAgent = async (Captain c) =>
            {
                Interlocked.Increment(ref fixture._StopCalls);
                int pid = c.ProcessId!.Value;
                await fixture.Manager.StopProcessAndConfirmAsync(pid).ConfigureAwait(false);
                fixture.Manager.UnregisterProcessId(pid);
            };
            fixture._Captains.OnLaunchAgent = (Captain c, Mission m, Dock d) =>
            {
                Interlocked.Increment(ref fixture._Launches);
                return Task.FromResult(Environment.ProcessId);
            };

            await fixture.LinkAsync(false).ConfigureAwait(false);
            await fixture.Manager.LaunchAsync(fixture.HarborId, new HarborLaunchRequest { JobId = fixture.JobId, Runtime = "ClaudeCode", WorkingDirectory = "/w" }, fixture.Listener).ConfigureAwait(false);
            await fixture.Manager.OnMessageAsync(fixture.HarborId, new HarborStarted { JobId = fixture.JobId, ProcessId = 7001 }).ConfigureAwait(false);
            fixture.ProcessHandle = fixture.Manager.RegisterLaunchedJob(fixture.HarborId, fixture.JobId);
            await fixture.HeartbeatAsync(true).ConfigureAwait(false);

            DateTime now = fixture.Now;
            Vessel vessel = await db.Vessels.CreateAsync(new Vessel("unreachable-vessel-" + Guid.NewGuid().ToString("N"), "https://github.com/test/repo.git")).ConfigureAwait(false);
            Captain captain = new Captain("unreachable-captain-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            captain.State = CaptainStateEnum.Working;
            captain.ProcessId = fixture.ProcessHandle;
            captain.LastHeartbeatUtc = now - heartbeatAge;
            captain = await db.Captains.CreateAsync(captain).ConfigureAwait(false);

            Dock dock = new Dock(vessel.Id);
            dock.CaptainId = captain.Id;
            dock.HarborId = fixture.HarborId;
            dock.WorktreePath = Path.Combine(Path.GetTempPath(), "armada_test_unreachable_" + Guid.NewGuid().ToString("N"));
            dock.BranchName = "armada/unreachable/test";
            dock.Active = true;
            dock = await db.Docks.CreateAsync(dock).ConfigureAwait(false);

            Mission mission = new Mission("Mission on a Harbor that loses its link");
            mission.VesselId = vessel.Id;
            mission.CaptainId = captain.Id;
            mission.DockId = dock.Id;
            mission.AssignedHarborId = fixture.HarborId;
            mission.Status = MissionStatusEnum.InProgress;
            mission.StartedUtc = now.AddMinutes(-30);
            mission.ProcessId = fixture.ProcessHandle;
            mission = await db.Missions.CreateAsync(mission).ConfigureAwait(false);

            captain.CurrentMissionId = mission.Id;
            captain.CurrentDockId = dock.Id;
            captain.LastHeartbeatUtc = now - heartbeatAge;
            await db.Captains.UpdateAsync(captain).ConfigureAwait(false);
            fixture.Captain = (await db.Captains.ReadAsync(captain.Id).ConfigureAwait(false)) ?? captain;
            fixture.Mission = mission;
            return fixture;
        }

        /// <summary>
        /// A second Admiral on the same database and services, as after an Admiral restart (no in-memory state).
        /// </summary>
        /// <returns>The new Admiral, which also replaces <see cref="Admiral"/>.</returns>
        public AdmiralService RestartAdmiral()
        {
            Admiral = NewAdmiral();
            return Admiral;
        }

        /// <summary>
        /// Link (or relink) the Harbor. It reports the job running when <paramref name="jobRunning"/> is true.
        /// </summary>
        /// <param name="jobRunning">Whether the handshake lists the job.</param>
        /// <returns>Task.</returns>
        public async Task LinkAsync(bool jobRunning)
        {
            HarborSendDelegate send = async (message, token) =>
            {
                if (message is HarborKillRequest kill)
                {
                    lock (Kills) Kills.Add(kill);
                    await Manager.OnMessageAsync(HarborId, new HarborExited { JobId = kill.JobId, ExitCode = -1 }, token).ConfigureAwait(false);
                }
            };

            HarborHandshake handshake = new HarborHandshake
            {
                HarborId = HarborId,
                Name = HarborId,
                ProtocolVersion = HarborProtocol.Version,
                MaxConcurrentJobs = 4,
                HeartbeatIntervalMs = 0,
                LiveJobIds = jobRunning ? new List<string> { JobId } : new List<string>(),
                Capabilities = new List<HarborCapability> { new HarborCapability { Name = "ClaudeCode", Available = true } }
            };
            HarborHandshakeAck ack = await Manager.OnHandshakeAsync(handshake, null, null, send).ConfigureAwait(false);
            if (!ack.Accepted) throw new InvalidOperationException("The stand-in Harbor was refused: " + ack.Reason);
        }

        /// <summary>
        /// The Harbor reports a heartbeat listing the job when <paramref name="jobRunning"/> is true.
        /// </summary>
        /// <param name="jobRunning">Whether the job is listed as running.</param>
        /// <returns>Task.</returns>
        public Task HeartbeatAsync(bool jobRunning)
        {
            return Manager.OnMessageAsync(HarborId, new HarborHeartbeat { LiveJobIds = jobRunning ? new List<string> { JobId } : new List<string>() });
        }

        /// <summary>
        /// The Harbor's link drops; its job keeps running on the host.
        /// </summary>
        /// <returns>Task.</returns>
        public Task DisconnectAsync()
        {
            return Manager.OnDisconnectedAsync(HarborId);
        }

        /// <summary>
        /// Move the clock and run one Admiral health check.
        /// </summary>
        /// <param name="advance">How far to move the clock first.</param>
        /// <returns>Task.</returns>
        public async Task TickAsync(TimeSpan advance)
        {
            Clock.Advance(advance);
            await Admiral.HealthCheckAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// The captain reports activity at the given time.
        /// </summary>
        /// <param name="whenUtc">Activity time (UTC).</param>
        /// <returns>Task.</returns>
        public async Task ReportActivityAsync(DateTime whenUtc)
        {
            Captain captain = (await _Db.Captains.ReadAsync(Captain.Id).ConfigureAwait(false))!;
            captain.LastHeartbeatUtc = whenUtc;
            await _Db.Captains.UpdateAsync(captain).ConfigureAwait(false);
        }

        /// <summary>
        /// The captain's current record.
        /// </summary>
        /// <returns>The captain.</returns>
        public async Task<Captain> ReadCaptainAsync()
        {
            return (await _Db.Captains.ReadAsync(Captain.Id).ConfigureAwait(false))!;
        }

        /// <summary>
        /// The mission's current record.
        /// </summary>
        /// <returns>The mission.</returns>
        public async Task<Mission> ReadMissionAsync()
        {
            return (await _Db.Missions.ReadAsync(Mission.Id).ConfigureAwait(false))!;
        }

        /// <summary>
        /// The captain.* events recorded for the mission, oldest first.
        /// </summary>
        /// <returns>The events.</returns>
        public async Task<List<ArmadaEvent>> CaptainEventsAsync()
        {
            List<ArmadaEvent> events = await _Db.Events.EnumerateByMissionAsync(Mission.Id, 5000).ConfigureAwait(false);
            return events.Where(e => e.EventType.StartsWith("captain.", StringComparison.Ordinal)).OrderBy(e => e.CreatedUtc).ToList();
        }

        /// <summary>
        /// The events of one type recorded for the mission, oldest first.
        /// </summary>
        /// <param name="eventType">Event type.</param>
        /// <returns>The events.</returns>
        public async Task<List<ArmadaEvent>> EventsAsync(string eventType)
        {
            List<ArmadaEvent> events = await CaptainEventsAsync().ConfigureAwait(false);
            return events.Where(e => e.EventType == eventType).ToList();
        }

        #endregion

        #region Private-Methods

        private AdmiralService NewAdmiral()
        {
            AdmiralService admiral = new AdmiralService(_Logging, _Db, Settings, _Captains, _Missions, new VoyageService(_Logging, _Db), _Docks);
            admiral.Time = Clock;
            admiral.HarborLinks = Manager;
            admiral.OnIsProcessTracked = Manager.IsProcessTracked;
            admiral.OnIsRemoteProcessTracked = Manager.IsProcessTracked;
            admiral.OnIsProcessAwaitingReconnect = Manager.IsProcessAwaitingReconnect;
            return admiral;
        }

        #endregion
    }
}
