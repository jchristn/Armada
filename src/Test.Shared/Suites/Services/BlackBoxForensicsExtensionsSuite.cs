namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Black box deeper forensics (plan 13.3): attempt diff, the success flight recorder and nearby successes (service
    /// and enricher), the combined timeline and the voyage cascade graph.
    /// </summary>
    public sealed class BlackBoxForensicsExtensionsSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.BlackBoxForensicsExtensions";

        private const string DiffA = "diff --git a/src/a.cs b/src/a.cs\n--- a/src/a.cs\n+++ b/src/a.cs\n@@ -1 +1 @@\n-x\n+y\ndiff --git a/src/shared.cs b/src/shared.cs\n--- a/src/shared.cs\n+++ b/src/shared.cs\n@@ -1 +1 @@\n-x\n+y\n";
        private const string DiffB = "diff --git a/src/b.cs b/src/b.cs\n--- a/src/b.cs\n+++ b/src/b.cs\n@@ -1 +1 @@\n-x\n+y\ndiff --git a/src/shared.cs b/src/shared.cs\n--- a/src/shared.cs\n+++ b/src/shared.cs\n@@ -1 +1 @@\n-x\n+y\n";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("diff_compares_two_attempts", "Two attempts of a mission differ in captain, model, step and changed files; the diff reports each", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                Vessel vessel = await t.Driver.Vessels.CreateAsync(new Vessel("bb-diff-" + BlackBoxPreventionSuite.Suffix(), "https://example.invalid/diff.git")).ConfigureAwait(false);
                Captain c1 = new Captain("diff-one"); c1.Model = "model-a";
                c1 = await t.Driver.Captains.CreateAsync(c1).ConfigureAwait(false);
                Captain c2 = new Captain("diff-two", AgentRuntimeEnum.Codex); c2.Model = "model-b";
                c2 = await t.Driver.Captains.CreateAsync(c2).ConfigureAwait(false);
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                mission.VesselId = vessel.Id;
                mission.DiffSnapshot = DiffA;
                mission = await t.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                string first = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation, c => { c.Captain = c1; c.Vessel = vessel; }).ConfigureAwait(false))!;
                await bb.Pipeline.ProcessAsync(first).ConfigureAwait(false);
                mission.DiffSnapshot = DiffB;
                mission.FailureKind = MissionFailureKindEnum.NoOp;
                mission = await t.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                string second = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.NoOp, c => { c.Captain = c2; c.Vessel = vessel; }).ConfigureAwait(false))!;
                await bb.Pipeline.ProcessAsync(second).ConfigureAwait(false);

                BlackBoxAttemptDiff diff = await bb.Insights.Service.DiffAsync(BlackBoxTestKit.Admin(), first, second).ConfigureAwait(false);
                AssertTrue(diff.SameMission); AssertEqual(1, diff.Attempt); AssertEqual(2, diff.OtherAttempt);
                AssertFalse(diff.SameFingerprint, "different failure modes");
                AssertTrue(diff.Differences.Any(d => d.Field == "CaptainId" && d.Value == c1.Id && d.OtherValue == c2.Id), "captain");
                AssertTrue(diff.Differences.Any(d => d.Field == "CaptainModel" && d.Value == "model-a" && d.OtherValue == "model-b"), "model");
                AssertTrue(diff.Differences.Any(d => d.Field == "CaptainRuntime"), "runtime");
                AssertTrue(diff.Differences.Any(d => d.Section == "Site" && d.Field == "Step" && d.Value == "ScopeViolation" && d.OtherValue == "NoOp"), "step");
                AssertTrue(diff.Differences.Any(d => d.Section == "Configuration"), "configuration values compared");
                AssertEqual(diff.Differences.Count, diff.DifferenceCount);
                AssertTrue(diff.ChangedFilesKnown, "both missions have changed-file rows");
                AssertEqual("src/a.cs", String.Join(",", diff.FilesOnlyInEntry)); AssertEqual("src/b.cs", String.Join(",", diff.FilesOnlyInOther)); AssertEqual(1, diff.FilesInBothCount);
            }));

            cases.Add(Case("diff_refusals", "The diff refuses the same entry twice, an unknown entry and another tenant's entry", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                mission.TenantId = "ten_a"; mission.UserId = "usr_a";
                string id = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false))!;
                await AssertThrowsAsync<ArgumentException>(() => bb.Insights.Service.DiffAsync(BlackBoxTestKit.Admin(), id, id), "same entry").ConfigureAwait(false);
                await AssertThrowsAsync<BlackBoxNotFoundException>(() => bb.Insights.Service.DiffAsync(BlackBoxTestKit.Admin(), id, "bbx_missing"), "unknown").ConfigureAwait(false);
                await AssertThrowsAsync<BlackBoxNotFoundException>(() => bb.Insights.Service.DiffAsync(BlackBoxTestKit.User("ten_b", "usr_b", true), id, id), "another tenant").ConfigureAwait(false);
                await AssertThrowsAsync<BlackBoxNotFoundException>(() => bb.Insights.Service.DiffAsync(BlackBoxTestKit.User("ten_a", "usr_other"), id, id), "a colleague's entry").ConfigureAwait(false);
            }));

            cases.Add(Case("success_recorder", "Successful landings are recorded once with timings, commits and changed files; retention 0 records nothing and removes the records; expired records are cleaned", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = BlackBoxTestKit.Settings();
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                Vessel vessel = await t.Driver.Vessels.CreateAsync(new Vessel("bb-succ-" + BlackBoxPreventionSuite.Suffix(), "https://example.invalid/succ.git")).ConfigureAwait(false);
                Mission landed = await LandedMissionAsync(t.Driver, vessel, DiffA, DateTime.UtcNow.AddMinutes(-1)).ConfigureAwait(false);
                await t.Driver.Events.CreateAsync(new ArmadaEvent("mission.completed", "Mission completed") { MissionId = landed.Id, VesselId = vessel.Id, EntityType = "mission", EntityId = landed.Id }).ConfigureAwait(false);
                AssertEqual(1, await bb.Insights.Successes.RunOnceAsync().ConfigureAwait(false), "recorded from the mission.completed event");
                AssertEqual(0, await bb.Insights.Successes.RunOnceAsync().ConfigureAwait(false), "an event is read once");
                AssertNull(await bb.Insights.Successes.RecordAsync(landed).ConfigureAwait(false), "a landing is recorded once");
                BlackBoxSuccess s = (await t.Driver.BlackBoxSuccesses.ReadLatestByMissionAsync(landed.Id).ConfigureAwait(false))!;
                AssertEqual(vessel.Id, s.VesselId); AssertEqual("abc123", s.BaseCommit); AssertEqual("def456", s.TargetCommit);
                AssertEqual(2, s.FileCount); AssertTrue(s.FilesChanged.Contains("src/shared.cs"));
                AssertTrue(s.RunMs.HasValue && s.RunMs.Value > 0, "run time"); AssertTrue(s.QueueMs.HasValue, "queue time");
                AssertTrue(s.ExpiresUtc > DateTime.UtcNow.AddDays(6), "kept 7 days");

                Mission failed = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                AssertNull(await bb.Insights.Successes.RecordAsync(failed).ConfigureAwait(false), "only Complete missions");

                settings.BlackBox.SuccessRetentionDays = 0;
                Mission another = await LandedMissionAsync(t.Driver, vessel, DiffB, DateTime.UtcNow).ConfigureAwait(false);
                AssertNull(await bb.Insights.Successes.RecordAsync(another).ConfigureAwait(false), "recorder off");
                AssertEqual(1, await bb.Insights.Successes.CleanupAsync().ConfigureAwait(false), "turning it off removes the records");
                AssertEqual(0L, await t.Driver.BlackBoxSuccesses.CountAsync().ConfigureAwait(false));

                settings.BlackBox.SuccessRetentionDays = 7;
                Mission old = await LandedMissionAsync(t.Driver, vessel, DiffB, DateTime.UtcNow.AddDays(-8)).ConfigureAwait(false);
                AssertNotNull(await bb.Insights.Successes.RecordAsync(old).ConfigureAwait(false));
                AssertEqual(1, await bb.Insights.Successes.CleanupAsync().ConfigureAwait(false), "an expired record is cleaned");
            }));

            cases.Add(Case("nearby_successes", "Nearby successes are same-vessel landings within the window that changed the failure's files; the enricher puts them in Related", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                Vessel vessel = await t.Driver.Vessels.CreateAsync(new Vessel("bb-near-" + BlackBoxPreventionSuite.Suffix(), "https://example.invalid/near.git")).ConfigureAwait(false);
                Vessel other = await t.Driver.Vessels.CreateAsync(new Vessel("bb-near-other-" + BlackBoxPreventionSuite.Suffix(), "https://example.invalid/near2.git")).ConfigureAwait(false);
                Mission near = await LandedMissionAsync(t.Driver, vessel, DiffA, DateTime.UtcNow.AddHours(-2)).ConfigureAwait(false);
                Mission unrelated = await LandedMissionAsync(t.Driver, vessel, "diff --git a/lib/z.cs b/lib/z.cs\n", DateTime.UtcNow.AddHours(-1)).ConfigureAwait(false);
                Mission elsewhere = await LandedMissionAsync(t.Driver, other, DiffA, DateTime.UtcNow.AddHours(-1)).ConfigureAwait(false);
                Mission far = await LandedMissionAsync(t.Driver, vessel, DiffA, DateTime.UtcNow.AddHours(-30)).ConfigureAwait(false);
                foreach (Mission m in new[] { near, unrelated, elsewhere, far }) await bb.Insights.Successes.RecordAsync(m).ConfigureAwait(false);

                Mission failing = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                failing.VesselId = vessel.Id;
                failing.DiffSnapshot = "diff --git a/src/a.cs b/src/a.cs\n";
                failing = await t.Driver.Missions.UpdateAsync(failing).ConfigureAwait(false);
                string id = (await bb.Capture.RecordMissionFailureAsync(failing, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation, c =>
                {
                    c.Vessel = vessel;
                    c.OutOfScopeFiles.Add("src/a.cs");
                }).ConfigureAwait(false))!;
                await bb.Pipeline.ProcessAsync(id).ConfigureAwait(false);

                EnumerationResult<BlackBoxNearbySuccess> page = await bb.Insights.Service.EnumerateNearbySuccessesAsync(BlackBoxTestKit.Admin(), id, null).ConfigureAwait(false);
                AssertEqual(1L, page.TotalRecords, "only the near landing on the same vessel that touched src/a.cs");
                BlackBoxNearbySuccess n = page.Objects.Single();
                AssertEqual(near.Id, n.MissionId); AssertEqual("src/a.cs", n.OverlappingFiles.Single()); AssertTrue(n.HoursFromFailure < -1.5 && n.HoursFromFailure > -2.5, "hours from the failure");
                BlackBoxRecord record = await bb.Service.GetRecordAsync(BlackBoxTestKit.Admin(), id).ConfigureAwait(false);
                AssertEqual(1, record.Related!.NearbySuccessTotal, "enricher total");
                AssertEqual(near.Id, record.Related.NearbySuccesses.Single().MissionId, "enricher lists it in Related");

                List<BlackBoxNearbySuccess> pure = BlackBoxInsightService.Nearby(DateTime.UtcNow, new List<string> { "x.cs" }, new List<BlackBoxSuccess>());
                AssertEqual(0, pure.Count, "nothing to compare");
                await AssertThrowsAsync<BlackBoxNotFoundException>(() => bb.Insights.Service.EnumerateNearbySuccessesAsync(BlackBoxTestKit.User("ten_x", "usr_x", true), id, null), "invisible entry").ConfigureAwait(false);
            }));

            cases.Add(Case("timeline_merges_sources", "The timeline merges entry, notes, mission and its events, dock, merge entry, captain and Harbor link events in time order, pages, and honors descending order", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                Vessel vessel = await t.Driver.Vessels.CreateAsync(new Vessel("bb-time-" + BlackBoxPreventionSuite.Suffix(), "https://example.invalid/time.git")).ConfigureAwait(false);
                Captain captain = await t.Driver.Captains.CreateAsync(new Captain("timeline-captain")).ConfigureAwait(false);
                Dock dock = new Dock(vessel.Id); dock.HarborId = "hbr_timeline"; dock.BranchName = "armada/timeline";
                dock = await t.Driver.Docks.CreateAsync(dock).ConfigureAwait(false);
                MergeEntry merge = new MergeEntry("armada/timeline"); merge.VesselId = vessel.Id; merge.Status = MergeStatusEnum.Failed; merge.TestStartedUtc = DateTime.UtcNow.AddMinutes(-3); merge.CompletedUtc = DateTime.UtcNow.AddMinutes(-2);
                merge = await t.Driver.MergeEntries.CreateAsync(merge).ConfigureAwait(false);
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                mission.VesselId = vessel.Id; mission.DockId = dock.Id; mission.CaptainId = captain.Id;
                mission.CreatedUtc = DateTime.UtcNow.AddMinutes(-30); mission.StartedUtc = DateTime.UtcNow.AddMinutes(-25);
                mission = await t.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                await t.Driver.Events.CreateAsync(new ArmadaEvent("mission.assigned", "Assigned") { MissionId = mission.Id, EntityType = "mission", EntityId = mission.Id, CreatedUtc = DateTime.UtcNow.AddMinutes(-24) }).ConfigureAwait(false);
                await t.Driver.Events.CreateAsync(new ArmadaEvent("dock.repaired", "Repaired") { EntityType = "dock", EntityId = dock.Id, CreatedUtc = DateTime.UtcNow.AddMinutes(-20) }).ConfigureAwait(false);
                await t.Driver.Events.CreateAsync(new ArmadaEvent("merge_queue.enqueued", "Enqueued") { EntityType = "merge_entry", EntityId = merge.Id, CreatedUtc = DateTime.UtcNow.AddMinutes(-4) }).ConfigureAwait(false);
                await t.Driver.Events.CreateAsync(new ArmadaEvent("captain.heartbeat", "Heartbeat") { CaptainId = captain.Id, EntityType = "captain", EntityId = captain.Id, CreatedUtc = DateTime.UtcNow }).ConfigureAwait(false);
                await t.Driver.Events.CreateAsync(new ArmadaEvent("captain.old", "Before the mission") { CaptainId = captain.Id, EntityType = "captain", EntityId = captain.Id, CreatedUtc = DateTime.UtcNow.AddDays(-2) }).ConfigureAwait(false);
                await t.Driver.HarborLinkEvents.CreateAsync(new HarborLinkEvent { HarborId = "hbr_timeline", EventType = HarborLinkEventTypeEnum.Disconnected, OccurredUtc = DateTime.UtcNow.AddSeconds(1), Detail = "network" }).ConfigureAwait(false);
                string id = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation, c => { c.Dock = dock; c.MergeEntryId = merge.Id; c.Vessel = vessel; }).ConfigureAwait(false))!;
                await bb.Service.AddNoteAsync(BlackBoxTestKit.Admin(), id, new BlackBoxNoteAddRequest { Body = "Looking into it" }).ConfigureAwait(false);

                BlackBoxTimelinePage all = await bb.Insights.Service.GetTimelineAsync(BlackBoxTestKit.Admin(), id, new EnumerationQuery { PageSize = 1000, Order = EnumerationOrderEnum.CreatedAscending }).ConfigureAwait(false);
                List<BlackBoxTimelineSourceEnum> sources = all.Objects.Select(i => i.Source).Distinct().ToList();
                foreach (BlackBoxTimelineSourceEnum expected in new[] { BlackBoxTimelineSourceEnum.Entry, BlackBoxTimelineSourceEnum.Note, BlackBoxTimelineSourceEnum.Mission, BlackBoxTimelineSourceEnum.Dock, BlackBoxTimelineSourceEnum.MergeEntry, BlackBoxTimelineSourceEnum.Captain, BlackBoxTimelineSourceEnum.Harbor })
                    AssertTrue(sources.Contains(expected), "source " + expected);
                for (int i = 1; i < all.Objects.Count; i++) AssertTrue(all.Objects[i - 1].TimestampUtc <= all.Objects[i].TimestampUtc, "oldest first");
                AssertFalse(all.Objects.Any(i => i.Kind == "captain.old"), "captain events outside the mission's window are left out");
                AssertTrue(all.Objects.Any(o => o.Kind == "mission.created"), "the mission's creation");
                AssertTrue(all.Objects.Select(o => o.EntityId).Where(e => e != null && e.StartsWith("evt_", StringComparison.Ordinal)).GroupBy(e => e).All(g => g.Count() == 1), "an event appears once");
                AssertFalse(all.Truncated);
                AssertTrue(all.Sources.Count >= 7, "per-source counts");

                BlackBoxTimelinePage first = await bb.Insights.Service.GetTimelineAsync(BlackBoxTestKit.Admin(), id, new EnumerationQuery { PageSize = 3, Order = EnumerationOrderEnum.CreatedAscending }).ConfigureAwait(false);
                AssertEqual(3, first.Objects.Count); AssertEqual(all.TotalRecords, first.TotalRecords, "paged with an exact total");
                BlackBoxTimelinePage newest = await bb.Insights.Service.GetTimelineAsync(BlackBoxTestKit.Admin(), id, new EnumerationQuery { PageSize = 1, Order = EnumerationOrderEnum.CreatedDescending }).ConfigureAwait(false);
                AssertEqual(all.Objects.Last().TimestampUtc, newest.Objects.Single().TimestampUtc, "newest first when asked");
                await AssertThrowsAsync<BlackBoxNotFoundException>(() => bb.Insights.Service.GetTimelineAsync(BlackBoxTestKit.User("ten_x", "usr_x", true), id, null), "invisible entry").ConfigureAwait(false);
            }));

            cases.Add(Case("cascade_graph", "The cascade graph finds the root failure, CancelledBecause and BlockedBy dependents, DependsOn chains and Sibling failures", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                Voyage voyage = await t.Driver.Voyages.CreateAsync(new Voyage("Cascade", "c")).ConfigureAwait(false);
                Mission root = await VoyageMissionAsync(t.Driver, voyage, "root", MissionStatusEnum.Failed, null, -50).ConfigureAwait(false);
                Mission cancelled = await VoyageMissionAsync(t.Driver, voyage, "cancelled", MissionStatusEnum.Cancelled, root.Id, -40).ConfigureAwait(false);
                Mission blocked = await VoyageMissionAsync(t.Driver, voyage, "blocked", MissionStatusEnum.Pending, root.Id, -30).ConfigureAwait(false);
                Mission sibling = await VoyageMissionAsync(t.Driver, voyage, "sibling", MissionStatusEnum.Failed, null, -20).ConfigureAwait(false);
                Mission fine = await VoyageMissionAsync(t.Driver, voyage, "fine", MissionStatusEnum.Complete, null, -10).ConfigureAwait(false);
                Mission next = await VoyageMissionAsync(t.Driver, voyage, "next", MissionStatusEnum.Pending, fine.Id, -5).ConfigureAwait(false);

                BlackBoxCascadeGraph graph = await bb.Insights.Service.GetVoyageCascadeAsync(BlackBoxTestKit.Admin(), voyage.Id).ConfigureAwait(false);
                AssertEqual(6L, graph.MissionCount); AssertEqual(6, graph.Nodes.Count); AssertFalse(graph.Truncated);
                AssertEqual(root.Id, graph.RootMissionId, "the earliest failure without a failed dependency");
                AssertTrue(graph.Nodes.Single(n => n.MissionId == root.Id).IsRoot);
                AssertEqual(BlackBoxCascadeEdgeKindEnum.CancelledBecause, graph.Edges.Single(e => e.ToMissionId == cancelled.Id).Kind);
                AssertEqual(BlackBoxCascadeEdgeKindEnum.BlockedBy, graph.Edges.Single(e => e.ToMissionId == blocked.Id).Kind);
                AssertEqual(BlackBoxCascadeEdgeKindEnum.DependsOn, graph.Edges.Single(e => e.ToMissionId == next.Id).Kind);
                BlackBoxCascadeEdge sib = graph.Edges.Single(e => e.ToMissionId == sibling.Id);
                AssertEqual(BlackBoxCascadeEdgeKindEnum.Sibling, sib.Kind); AssertEqual(root.Id, sib.FromMissionId);
                AssertFalse(graph.Edges.Any(e => e.ToMissionId == fine.Id), "a completed independent mission has no edge");

                List<Mission> many = Enumerable.Range(0, 250).Select(i => new Mission("m" + i, "d") { VoyageId = voyage.Id, Status = MissionStatusEnum.Complete, CreatedUtc = DateTime.UtcNow.AddMinutes(-i) }).ToList();
                BlackBoxCascadeGraph capped = BlackBoxInsightService.BuildCascade(voyage, many, 250);
                AssertEqual(BlackBoxInsightLimits.MaxCascadeNodes, capped.Nodes.Count); AssertTrue(capped.Truncated); AssertNull(capped.RootMissionId, "no failure, no root");
                await AssertThrowsAsync<KeyNotFoundException>(() => bb.Insights.Service.GetVoyageCascadeAsync(BlackBoxTestKit.Admin(), "vyg_missing"), "unknown voyage").ConfigureAwait(false);
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box forensics: attempt diff, success recorder, nearby successes, timeline, cascade", cases: cases);
        }

        private static async Task<Mission> LandedMissionAsync(DatabaseDriver db, Vessel vessel, string diff, DateTime landedUtc)
        {
            Mission m = new Mission("Landed " + BlackBoxPreventionSuite.Suffix(), "done");
            m.VesselId = vessel.Id;
            m.Status = MissionStatusEnum.Complete;
            m.DiffSnapshot = diff;
            m.StartCommit = "abc123";
            m.CommitHash = "def456";
            m.CreatedUtc = landedUtc.AddMinutes(-20);
            m.StartedUtc = landedUtc.AddMinutes(-15);
            m.CompletedUtc = landedUtc;
            return await db.Missions.CreateAsync(m).ConfigureAwait(false);
        }

        private static async Task<Mission> VoyageMissionAsync(DatabaseDriver db, Voyage voyage, string title, MissionStatusEnum status, string? dependsOn, int minutesAgo)
        {
            Mission m = new Mission(title, "cascade");
            m.VoyageId = voyage.Id;
            m.Status = status;
            m.DependsOnMissionId = dependsOn;
            m.CreatedUtc = DateTime.UtcNow.AddMinutes(minutesAgo);
            if (status == MissionStatusEnum.Failed || status == MissionStatusEnum.Cancelled || status == MissionStatusEnum.Complete) m.CompletedUtc = m.CreatedUtc.AddMinutes(1);
            return await db.Missions.CreateAsync(m).ConfigureAwait(false);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }
    }
}
