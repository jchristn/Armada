namespace Armada.Test.Unit.Suites.Services
{
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;
    using SyslogLogging;

    public class DockServiceTests : TestSuite
    {
        public override string Name => "Dock Service Tests";

        private LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private ArmadaSettings CreateSettings()
        {
            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = Path.Combine(Path.GetTempPath(), "armada_test_docks_" + Guid.NewGuid().ToString("N"));
            settings.ReposDirectory = Path.Combine(Path.GetTempPath(), "armada_test_repos_" + Guid.NewGuid().ToString("N"));
            return settings;
        }

        protected override async Task RunTestsAsync()
        {
            await RunTest("ProvisionAsync Deactivates Stale Active Dock For Same Path", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    ArmadaSettings settings = CreateSettings();
                    LoggingModule logging = CreateLogging();
                    DockService docks = new DockService(logging, db, settings, git);

                    Vessel vessel = new Vessel("TestVessel", "https://github.com/test/repo");
                    vessel.LocalPath = Path.Combine(settings.ReposDirectory, vessel.Name + ".git");
                    await db.Vessels.CreateAsync(vessel);

                    Captain captain = new Captain("captain-1");
                    await db.Captains.CreateAsync(captain);
                    Captain staleCaptain = new Captain("stale-captain");
                    await db.Captains.CreateAsync(staleCaptain);

                    string missionId = "msn_test_conflict";
                    string branchName = "armada/captain-1/" + missionId;
                    string worktreePath = Path.Combine(settings.DocksDirectory, vessel.Name, missionId);

                    Dock staleDock = new Dock(vessel.Id);
                    staleDock.CaptainId = staleCaptain.Id;
                    staleDock.WorktreePath = worktreePath;
                    staleDock.BranchName = branchName;
                    staleDock.Active = true;
                    await db.Docks.CreateAsync(staleDock);

                    Dock? provisioned = await docks.ProvisionAsync(vessel, captain, branchName, missionId);
                    AssertNotNull(provisioned, "ProvisionAsync should return a dock");

                    Dock? updatedStaleDock = await db.Docks.ReadAsync(staleDock.Id);
                    AssertNotNull(updatedStaleDock, "Stale dock should still exist in the DB");
                    AssertFalse(updatedStaleDock!.Active, "Stale conflicting dock should be deactivated");
                    AssertNull(updatedStaleDock.CaptainId, "Stale conflicting dock should release its captain binding");

                    List<Dock> vesselDocks = await db.Docks.EnumerateByVesselAsync(vessel.Id);
                    int activeDockCount = vesselDocks.Count(d =>
                        d.Active &&
                        String.Equals(d.WorktreePath, worktreePath, StringComparison.OrdinalIgnoreCase));

                    AssertEqual(1, activeDockCount, "Only one active dock should remain for the target worktree path");
                    AssertEqual(1, git.WorktreeCalls.Count, "ProvisionAsync should create exactly one worktree");
                }
            });
        }
    }
}
