namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Architect fan-out keeps the voyage's playbooks (RELIABILITY_SIMPLICITY_PLAN.md RS-2.8): every Worker slice and
    /// every stage cloned for it carries the playbook snapshots of the mission it was cloned from.
    /// </summary>
    public sealed class ArchitectFanOutPlaybooksSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.ArchitectFanOutPlaybooks";

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(
                    suiteId: SuiteId,
                    caseId: "every_slice_and_stage_keeps_playbooks",
                    displayName: "Slices 2..N and their cloned Judge stages carry the template's playbook snapshots",
                    executeAsync: (CancellationToken ct) => RunAsync(),
                    tags: new List<string> { TestTags.Reliability })
            };
            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Architect fan-out playbooks", cases: cases);
        }

        private static async Task RunAsync()
        {
            using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            DatabaseDriver db = testDb.Driver;
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            ArmadaSettings settings = new ArmadaSettings
            {
                DocksDirectory = TestTemp.NewDirectory("fanout_pb_docks"),
                ReposDirectory = TestTemp.NewDirectory("fanout_pb_repos"),
                LogDirectory = TestTemp.NewDirectory("fanout_pb_logs")
            };
            StubGitService git = new StubGitService { HeadCommitResult = "8888888888888888888888888888888888888888" };
            IDockService docks = new DockService(logging, db, settings, git);
            CaptainService captains = new CaptainService(logging, db, settings, git, docks);
            MissionService missions = new MissionService(logging, db, settings, docks, captains, git: git);
            missions.OnGetMissionOutput = _ => "```armada-plan\n{\"missions\":[{\"title\":\"Alpha\",\"description\":\"do alpha\"},{\"title\":\"Bravo\",\"description\":\"do bravo\"},{\"title\":\"Charlie\",\"description\":\"do charlie\"}]}\n```\n";

            Vessel vessel = await db.Vessels.CreateAsync(new Vessel("fanout-pb", "https://github.com/test/repo.git") { DefaultBranch = "main" }).ConfigureAwait(false);
            Captain captain = await CaptainAssignmentFixture.CreateAsync(db, new Captain("fanout-pb") { State = CaptainStateEnum.Working }).ConfigureAwait(false);
            Voyage voyage = await db.Voyages.CreateAsync(new Voyage("fanout playbooks") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
            Mission architect = await db.Missions.CreateAsync(new Mission("[Architect] Plan", "Plan it")
            {
                VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Architect", Status = MissionStatusEnum.InProgress,
                CaptainId = captain.Id, BranchName = "armada/fanout-pb/architect"
            }).ConfigureAwait(false);
            Dock dock = new Dock(vessel.Id) { CaptainId = captain.Id, BranchName = architect.BranchName, Active = true };
            dock.WorktreePath = Path.Combine(settings.DocksDirectory, vessel.Name, architect.Id);
            Directory.CreateDirectory(dock.WorktreePath);
            dock = await db.Docks.CreateAsync(dock).ConfigureAwait(false);
            architect.DockId = dock.Id;
            await db.Missions.UpdateAsync(architect).ConfigureAwait(false);
            captain.Assignment = architect;
            await CaptainAssignmentFixture.UpdateAsync(db, captain).ConfigureAwait(false);

            Mission worker = await db.Missions.CreateAsync(new Mission("[Worker] Build", "Build it") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", DependsOnMissionId = architect.Id }).ConfigureAwait(false);
            Mission judge = await db.Missions.CreateAsync(new Mission("[Judge] Review", "Review it") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Judge", DependsOnMissionId = worker.Id }).ConfigureAwait(false);
            List<MissionPlaybookSnapshot> snapshot = new List<MissionPlaybookSnapshot>
            {
                new MissionPlaybookSnapshot { FileName = "STYLE.md", Content = "Use explicit types.", DeliveryMode = PlaybookDeliveryModeEnum.InlineFullContent }
            };
            await db.Playbooks.SetMissionSnapshotsAsync(worker.Id, snapshot).ConfigureAwait(false);
            await db.Playbooks.SetMissionSnapshotsAsync(judge.Id, snapshot).ConfigureAwait(false);

            await missions.HandleCompletionAsync(captain, architect.Id).ConfigureAwait(false);

            List<Mission> all = await db.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
            List<Mission> slices = all.Where(m => m.Persona == "Worker").ToList();
            List<Mission> judges = all.Where(m => m.Persona == "Judge").ToList();
            AssertEqual(3, slices.Count, "three Worker slices");
            AssertEqual(3, judges.Count, "one Judge per slice");
            foreach (Mission stage in slices.Concat(judges))
            {
                List<MissionPlaybookSnapshot> stored = await db.Playbooks.GetMissionSnapshotsAsync(stage.Id).ConfigureAwait(false);
                AssertEqual(1, stored.Count, stage.Title + " keeps the voyage's playbook");
                AssertEqual("Use explicit types.", stored[0].Content, stage.Title);
            }
        }
    }
}
