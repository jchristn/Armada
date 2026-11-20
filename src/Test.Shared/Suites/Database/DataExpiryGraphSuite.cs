namespace Test.Shared.Suites.Database
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
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Data expiry over a v1.3.0-shaped graph on the active provider (the parity script runs it on SQLite, PostgreSQL,
    /// MySQL and SQL Server): a finished mission hierarchy, a hierarchy with a live child, a rescue of an old failure,
    /// mission slot and exclusive session ownership, open and closed wait intervals, merge entries, deployments,
    /// incidents, black box entries and successes, check runs and docks. Expiry must remove exactly the unreferenced old
    /// records, keep everything a remaining record points at, leave no dangling link, and change nothing on a second run.
    /// </summary>
    public sealed class DataExpiryGraphSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.DataExpiryGraph";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the data expiry graph suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: "v130_graph_expires_only_unreferenced_old_records",
                displayName: "Expiry of a v1.3.0 graph removes only unreferenced old records and respects every foreign key",
                executeAsync: (CancellationToken ct) => ExpireGraphAsync(ct),
                tags: new List<string> { TestTags.Positive, TestTags.Database }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Data Expiry Graph",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task ExpireGraphAsync(CancellationToken token)
        {
            using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
            {
                DatabaseDriver db = testDb.Driver;
                DateTime old = DateTime.UtcNow.AddDays(-60);
                DateTime recent = DateTime.UtcNow.AddDays(-2);

                Fleet fleet = await db.Fleets.CreateAsync(new Fleet("expiry-graph"), token).ConfigureAwait(false);
                Vessel vessel = new Vessel("expiry-graph", "https://github.com/test/expiry-graph");
                vessel.FleetId = fleet.Id;
                await db.Vessels.CreateAsync(vessel, token).ConfigureAwait(false);

                // 1. A finished voyage whose three-level hierarchy is all old: everything goes, children first.
                Voyage finished = await Voyage(db, "finished", VoyageStatusEnum.Complete, old, token).ConfigureAwait(false);
                Dock finishedDock = await DockAsync(db, vessel.Id, old, token).ConfigureAwait(false);
                Mission root = await MissionAsync(db, "root", MissionStatusEnum.Complete, old, finished.Id, null, token, dockId: finishedDock.Id).ConfigureAwait(false);
                Mission child = await MissionAsync(db, "child", MissionStatusEnum.Complete, old, finished.Id, root.Id, token).ConfigureAwait(false);
                Mission grandchild = await MissionAsync(db, "grandchild", MissionStatusEnum.Failed, old, finished.Id, child.Id, token).ConfigureAwait(false);
                await db.MissionWaitIntervals.CreateAsync(new MissionWaitInterval { MissionId = root.Id, VoyageId = finished.Id, StartedUtc = old.AddHours(-2), EndedUtc = old.AddHours(-1) }, token).ConfigureAwait(false);
                MergeEntry landed = await MergeAsync(db, root.Id, vessel.Id, MergeStatusEnum.Landed, old, token).ConfigureAwait(false);
                StageOutcome outcome = await db.StageOutcomes.CreateAsync(new StageOutcome { MissionId = child.Id, VoyageId = finished.Id }, token).ConfigureAwait(false);
                HandoffArtifact artifact = await db.HandoffArtifacts.CreateAsync(new HandoffArtifact { MissionId = grandchild.Id }, token).ConfigureAwait(false);
                CheckRun check = await db.CheckRuns.CreateAsync(new CheckRun { MissionId = root.Id, VoyageId = finished.Id, VesselId = vessel.Id, Command = "dotnet build" }, token).ConfigureAwait(false);

                // 2. A finished voyage whose old parent still has a live child: the parent, child and voyage stay.
                Voyage live = await Voyage(db, "live child", VoyageStatusEnum.Complete, old, token).ConfigureAwait(false);
                Dock parentDock = await DockAsync(db, vessel.Id, old, token).ConfigureAwait(false);
                Mission liveParent = await MissionAsync(db, "old parent", MissionStatusEnum.Complete, old, live.Id, null, token, dockId: parentDock.Id).ConfigureAwait(false);
                Mission liveChild = await MissionAsync(db, "live child", MissionStatusEnum.InProgress, null, live.Id, liveParent.Id, token).ConfigureAwait(false);

                // 3. An old failure with a live rescue: both stay with the link intact.
                Mission original = await MissionAsync(db, "original", MissionStatusEnum.Failed, old, null, null, token).ConfigureAwait(false);
                Mission rescue = await MissionAsync(db, "rescue", MissionStatusEnum.Pending, null, null, null, token, rescueOf: original.Id).ConfigureAwait(false);

                // 4. Slot ownership: an old terminal mission still holding a captain slot (and its dock) stays.
                Captain slotCaptain = await db.Captains.CreateAsync(new Captain("expiry-slot") { MaxConcurrency = 2 }, token).ConfigureAwait(false);
                Dock slotDock = await DockAsync(db, vessel.Id, old, token).ConfigureAwait(false);
                Mission slotted = await MissionAsync(db, "slotted", MissionStatusEnum.Pending, null, null, null, token).ConfigureAwait(false);
                AssertTrue(await db.Captains.TryClaimAsync(slotCaptain.Id, slotted.Id, slotDock.Id, token).ConfigureAwait(false), "slot claimed");
                slotted = await FinishAsync(db, slotted.Id, MissionStatusEnum.Complete, old, token).ConfigureAwait(false);

                // 5. Session ownership: an exclusive session whose id is an old mission's id keeps that mission.
                Captain sessionCaptain = await db.Captains.CreateAsync(new Captain("expiry-session"), token).ConfigureAwait(false);
                Mission sessionMission = await MissionAsync(db, "session", MissionStatusEnum.Complete, old, null, null, token).ConfigureAwait(false);
                AssertTrue(await db.Captains.TryClaimExclusiveAsync(sessionCaptain.Id, sessionMission.Id, CaptainStateEnum.Planning, token).ConfigureAwait(false), "session claimed");

                // 6. An open wait interval keeps its mission.
                Mission waiting = await MissionAsync(db, "waiting", MissionStatusEnum.Cancelled, old, null, null, token).ConfigureAwait(false);
                await db.MissionWaitIntervals.CreateAsync(new MissionWaitInterval { MissionId = waiting.Id, StartedUtc = old }, token).ConfigureAwait(false);

                // 7. An unfinished merge entry keeps its mission; a finished old one with no reference goes.
                Mission queued = await MissionAsync(db, "queued", MissionStatusEnum.Complete, old, null, null, token).ConfigureAwait(false);
                MergeEntry queuedEntry = await MergeAsync(db, queued.Id, vessel.Id, MergeStatusEnum.Queued, null, token).ConfigureAwait(false);

                // 8. Unexpired black box evidence keeps its mission, dock and merge entry; expired evidence does not.
                Dock evidenceDock = await DockAsync(db, vessel.Id, old, token).ConfigureAwait(false);
                Mission evidenced = await MissionAsync(db, "evidenced", MissionStatusEnum.Failed, old, null, null, token).ConfigureAwait(false);
                MergeEntry evidencedEntry = await MergeAsync(db, evidenced.Id, vessel.Id, MergeStatusEnum.Failed, old, token).ConfigureAwait(false);
                BlackBoxEntry liveEntry = BlackBoxTestKit.Entry(null, evidenced.Id);
                liveEntry.DockId = evidenceDock.Id;
                liveEntry.MergeEntryId = evidencedEntry.Id;
                liveEntry.ExpiresUtc = DateTime.UtcNow.AddDays(100);
                await db.BlackBoxEntries.CreateAsync(liveEntry, token).ConfigureAwait(false);
                Mission pinnedMission = await MissionAsync(db, "pinned evidence", MissionStatusEnum.Failed, old, null, null, token).ConfigureAwait(false);
                BlackBoxEntry pinnedEntry = BlackBoxTestKit.Entry(null, pinnedMission.Id);
                pinnedEntry.Pinned = true;
                pinnedEntry.ExpiresUtc = DateTime.UtcNow.AddDays(-1);
                await db.BlackBoxEntries.CreateAsync(pinnedEntry, token).ConfigureAwait(false);
                Mission staleEvidence = await MissionAsync(db, "stale evidence", MissionStatusEnum.Failed, old, null, null, token).ConfigureAwait(false);
                BlackBoxEntry staleEntry = BlackBoxTestKit.Entry(null, staleEvidence.Id);
                staleEntry.ExpiresUtc = DateTime.UtcNow.AddDays(-1);
                await db.BlackBoxEntries.CreateAsync(staleEntry, token).ConfigureAwait(false);
                Mission succeeded = await MissionAsync(db, "success evidence", MissionStatusEnum.Complete, old, null, null, token).ConfigureAwait(false);
                await db.BlackBoxSuccesses.CreateAsync(new BlackBoxSuccess { MissionId = succeeded.Id, VesselId = vessel.Id, MissionTitle = "success evidence", ExpiresUtc = DateTime.UtcNow.AddDays(3) }, token).ConfigureAwait(false);

                // 9. A deployment (no ON DELETE action on SQL Server) keeps its mission and voyage, and the voyage its missions.
                Voyage deployedVoyage = await Voyage(db, "deployed", VoyageStatusEnum.Complete, old, token).ConfigureAwait(false);
                Mission deployedMission = await MissionAsync(db, "deployed", MissionStatusEnum.Complete, old, deployedVoyage.Id, null, token).ConfigureAwait(false);
                Mission deployedSibling = await MissionAsync(db, "deployed sibling", MissionStatusEnum.Complete, old, deployedVoyage.Id, null, token).ConfigureAwait(false);
                await db.Deployments.CreateAsync(new Deployment { Title = "expiry graph deployment", MissionId = deployedMission.Id, VoyageId = deployedVoyage.Id }, token).ConfigureAwait(false);

                // 10. An incident keeps its mission.
                Mission incidentMission = await MissionAsync(db, "incident", MissionStatusEnum.Failed, old, null, null, token).ConfigureAwait(false);
                await db.Incidents.CreateAsync(new Incident { Title = "expiry graph incident", MissionId = incidentMission.Id, Status = IncidentStatusEnum.Open, Severity = IncidentSeverityEnum.Low, DetectedUtc = old, LastUpdateUtc = old }, token).ConfigureAwait(false);

                // 11. Recent and unfinished work is never touched; an old unreferenced dock goes.
                Mission recentMission = await MissionAsync(db, "recent", MissionStatusEnum.Complete, recent, null, null, token).ConfigureAwait(false);
                Mission oldButRunning = await MissionAsync(db, "running", MissionStatusEnum.InProgress, null, null, null, token).ConfigureAwait(false);
                Dock freeDock = await DockAsync(db, vessel.Id, old, token).ConfigureAwait(false);
                Mission standalone = await MissionAsync(db, "standalone", MissionStatusEnum.Cancelled, old, null, null, token).ConfigureAwait(false);

                DataExpiryService expiry = new DataExpiryService(Quiet(), TestDatabaseConfig.Type, testDb.ConnectionString, 30);
                int deleted = await expiry.PurgeExpiredDataAsync(token).ConfigureAwait(false);
                AssertTrue(deleted > 0, "the first run deletes something");

                // Removed: the finished hierarchy and everything that hung off it, stale evidence, the standalone mission.
                foreach (Mission gone in new[] { root, child, grandchild, staleEvidence, standalone })
                    AssertNull(await db.Missions.ReadAsync(gone.Id, token).ConfigureAwait(false), gone.Title + " expired");
                AssertNull(await db.Voyages.ReadAsync(finished.Id, token).ConfigureAwait(false), "finished voyage expired");
                AssertNull(await db.MergeEntries.ReadAsync(landed.Id, token).ConfigureAwait(false), "landed merge entry expired");
                AssertNull(await db.StageOutcomes.ReadAsync(outcome.Id, token).ConfigureAwait(false), "stage outcome expired with its mission");
                AssertNull(await db.HandoffArtifacts.ReadAsync(artifact.Id, token).ConfigureAwait(false), "handoff artifact expired with its mission");
                AssertEqual(0, (await db.MissionWaitIntervals.EnumerateByMissionAsync(root.Id, null, token).ConfigureAwait(false)).Count, "closed wait interval removed with its mission");
                AssertNull(await db.Docks.ReadAsync(finishedDock.Id, token).ConfigureAwait(false), "dock of an expired mission expired");
                AssertNull(await db.Docks.ReadAsync(freeDock.Id, token).ConfigureAwait(false), "unreferenced old dock expired");

                CheckRun? keptCheck = await db.CheckRuns.ReadAsync(check.Id, null, token).ConfigureAwait(false);
                AssertNotNull(keptCheck, "check run kept");
                AssertNull(keptCheck!.MissionId, "check run mission link cleared");
                AssertNull(keptCheck.VoyageId, "check run voyage link cleared");

                // Kept: everything a remaining record points at.
                foreach (Mission kept in new[] { liveParent, liveChild, original, rescue, slotted, sessionMission, waiting, queued, evidenced, pinnedMission, succeeded, deployedMission, deployedSibling, incidentMission, recentMission, oldButRunning })
                    AssertNotNull(await db.Missions.ReadAsync(kept.Id, token).ConfigureAwait(false), kept.Title + " kept");
                AssertNotNull(await db.Voyages.ReadAsync(live.Id, token).ConfigureAwait(false), "voyage with a live child kept");
                AssertNotNull(await db.Voyages.ReadAsync(deployedVoyage.Id, token).ConfigureAwait(false), "deployed voyage kept");
                AssertNotNull(await db.MergeEntries.ReadAsync(queuedEntry.Id, token).ConfigureAwait(false), "queued merge entry kept");
                AssertNotNull(await db.MergeEntries.ReadAsync(evidencedEntry.Id, token).ConfigureAwait(false), "merge entry with live evidence kept");
                AssertNotNull(await db.Docks.ReadAsync(parentDock.Id, token).ConfigureAwait(false), "dock of a kept mission kept");
                AssertNotNull(await db.Docks.ReadAsync(slotDock.Id, token).ConfigureAwait(false), "slot dock kept");
                AssertNotNull(await db.Docks.ReadAsync(evidenceDock.Id, token).ConfigureAwait(false), "dock with live evidence kept");
                AssertEqual(liveParent.Id, (await db.Missions.ReadAsync(liveChild.Id, token).ConfigureAwait(false))!.ParentMissionId, "live child's parent link intact");
                AssertEqual(original.Id, (await db.Missions.ReadAsync(rescue.Id, token).ConfigureAwait(false))!.RescueOfMissionId, "rescue link intact");
                AssertEqual(1, (await db.MissionWaitIntervals.EnumerateByMissionAsync(waiting.Id, null, token).ConfigureAwait(false)).Count, "open wait interval kept");

                await AssertNoDanglingLinksAsync(db, token).ConfigureAwait(false);

                int again = await expiry.PurgeExpiredDataAsync(token).ConfigureAwait(false);
                AssertEqual(0, again, "a second run deletes nothing more");
            }
        }

        private static async Task AssertNoDanglingLinksAsync(DatabaseDriver db, CancellationToken token)
        {
            List<Mission> missions = await db.Missions.EnumerateAsync(token).ConfigureAwait(false);
            HashSet<string> ids = new HashSet<string>(missions.Select(m => m.Id), StringComparer.Ordinal);
            foreach (Mission mission in missions)
            {
                if (mission.ParentMissionId != null) AssertTrue(ids.Contains(mission.ParentMissionId), mission.Title + " parent exists");
                if (mission.RescueOfMissionId != null) AssertTrue(ids.Contains(mission.RescueOfMissionId), mission.Title + " rescue original exists");
                if (mission.VoyageId != null) AssertNotNull(await db.Voyages.ReadAsync(mission.VoyageId, token).ConfigureAwait(false), mission.Title + " voyage exists");
            }
        }

        private static async Task<Voyage> Voyage(DatabaseDriver db, string title, VoyageStatusEnum status, DateTime completedUtc, CancellationToken token)
        {
            Voyage voyage = new Voyage("expiry graph " + title);
            voyage.Status = status;
            voyage.CompletedUtc = completedUtc;
            return await db.Voyages.CreateAsync(voyage, token).ConfigureAwait(false);
        }

        private static async Task<Mission> MissionAsync(DatabaseDriver db, string title, MissionStatusEnum status, DateTime? completedUtc, string? voyageId, string? parentId, CancellationToken token, string? rescueOf = null, string? dockId = null)
        {
            Mission mission = new Mission(title);
            mission.Status = status;
            mission.CompletedUtc = completedUtc;
            mission.VoyageId = voyageId;
            mission.ParentMissionId = parentId;
            mission.RescueOfMissionId = rescueOf;
            mission.IsRescue = rescueOf != null;
            mission.DockId = dockId;
            if (completedUtc.HasValue) mission.CreatedUtc = completedUtc.Value.AddHours(-3);
            return await db.Missions.CreateAsync(mission, token).ConfigureAwait(false);
        }

        private static async Task<Mission> FinishAsync(DatabaseDriver db, string missionId, MissionStatusEnum status, DateTime completedUtc, CancellationToken token)
        {
            Mission mission = (await db.Missions.ReadAsync(missionId, token).ConfigureAwait(false))!;
            mission.Status = status;
            mission.CompletedUtc = completedUtc;
            return await db.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
        }

        private static async Task<Dock> DockAsync(DatabaseDriver db, string vesselId, DateTime createdUtc, CancellationToken token)
        {
            Dock dock = new Dock(vesselId);
            dock.WorktreePath = "/tmp/expiry-graph/" + Guid.NewGuid().ToString("N");
            dock.Active = false;
            dock.CreatedUtc = createdUtc;
            return await db.Docks.CreateAsync(dock, token).ConfigureAwait(false);
        }

        private static async Task<MergeEntry> MergeAsync(DatabaseDriver db, string missionId, string vesselId, MergeStatusEnum status, DateTime? completedUtc, CancellationToken token)
        {
            MergeEntry entry = new MergeEntry("expiry/" + missionId, "main");
            entry.MissionId = missionId;
            entry.VesselId = vesselId;
            entry.Status = status;
            entry.CompletedUtc = completedUtc;
            return await db.MergeEntries.CreateAsync(entry, token).ConfigureAwait(false);
        }

        private static LoggingModule Quiet()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        #endregion
    }
}
