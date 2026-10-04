namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// A Working captain with an InProgress mission and an active dock whose process is the test process itself, wired
    /// to recording OnStopAgent / OnLaunchAgent delegates, for exercising the stall branch of the health check.
    /// </summary>
    public sealed class StallTestFixture
    {
        #region Public-Members

        /// <summary>
        /// Admiral service under test.
        /// </summary>
        public AdmiralService Admiral { get; private set; } = null!;

        /// <summary>
        /// The stalled captain.
        /// </summary>
        public Captain Captain { get; private set; } = null!;

        /// <summary>
        /// The captain's InProgress mission.
        /// </summary>
        public Mission Mission { get; private set; } = null!;

        /// <summary>
        /// Captain ids passed to OnStopAgent.
        /// </summary>
        public List<string> Stopped { get; } = new List<string>();

        /// <summary>
        /// Captain ids passed to OnLaunchAgent.
        /// </summary>
        public List<string> Launched { get; } = new List<string>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create the entities and services.
        /// </summary>
        /// <param name="db">Database driver.</param>
        /// <param name="settings">Settings (stall threshold, recovery limit).</param>
        /// <param name="lastHeartbeatUtc">Captain's last heartbeat.</param>
        /// <returns>The fixture.</returns>
        public static async Task<StallTestFixture> CreateAsync(DatabaseDriver db, ArmadaSettings settings, DateTime lastHeartbeatUtc)
        {
            StallTestFixture fixture = new StallTestFixture();
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            StubGitService git = new StubGitService { IsRepositoryResult = true };
            IDockService docks = new DockService(logging, db, settings, git);
            CaptainService captains = new CaptainService(logging, db, settings, git, docks);
            IMissionService missions = new MissionService(logging, db, settings, docks, captains);
            IVoyageService voyages = new VoyageService(logging, db);
            fixture.Admiral = new AdmiralService(logging, db, settings, captains, missions, voyages, docks);
            captains.OnStopAgent = (Captain c) =>
            {
                fixture.Stopped.Add(c.Id);
                return Task.CompletedTask;
            };
            captains.OnLaunchAgent = (Captain c, Mission m, Dock d) =>
            {
                fixture.Launched.Add(c.Id);
                return Task.FromResult(4242);
            };

            Vessel vessel = await db.Vessels.CreateAsync(new Vessel("stall-vessel-" + Guid.NewGuid().ToString("N"), "https://github.com/test/repo.git")).ConfigureAwait(false);

            Captain captain = new Captain("stall-captain-" + Guid.NewGuid().ToString("N"));
            captain.State = CaptainStateEnum.Working;
            // The test process is the "agent": alive for the whole test, and never actually stopped.
            captain.ProcessId = Environment.ProcessId;
            captain.LastHeartbeatUtc = lastHeartbeatUtc;
            captain = await db.Captains.CreateAsync(captain).ConfigureAwait(false);

            Dock dock = new Dock(vessel.Id);
            dock.CaptainId = captain.Id;
            dock.WorktreePath = Path.Combine(Path.GetTempPath(), "armada_test_stall_" + Guid.NewGuid().ToString("N"));
            dock.BranchName = "armada/stall/test";
            dock.Active = true;
            dock = await db.Docks.CreateAsync(dock).ConfigureAwait(false);

            Mission mission = new Mission("Stalled mission");
            mission.VesselId = vessel.Id;
            mission.CaptainId = captain.Id;
            mission.DockId = dock.Id;
            mission.Status = MissionStatusEnum.InProgress;
            mission.StartedUtc = DateTime.UtcNow.AddMinutes(-30);
            mission.ProcessId = Environment.ProcessId;
            mission = await db.Missions.CreateAsync(mission).ConfigureAwait(false);

            captain.CurrentMissionId = mission.Id;
            captain.CurrentDockId = dock.Id;
            captain.LastHeartbeatUtc = lastHeartbeatUtc;
            await db.Captains.UpdateAsync(captain).ConfigureAwait(false);

            fixture.Captain = (await db.Captains.ReadAsync(captain.Id).ConfigureAwait(false)) ?? captain;
            fixture.Mission = mission;
            return fixture;
        }

        #endregion
    }
}
