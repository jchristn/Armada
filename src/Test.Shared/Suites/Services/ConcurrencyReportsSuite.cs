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
    using Armada.Server;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Mission tree, wait and concurrency reports (CONCURRENCY_IMPROVEMENTS.md 7.6 and 8.3): the tree builder, the
    /// parentMissionId/rootsOnly/waitingGate filters, the cross-mission interval query, the vessel report, windowed
    /// metrics and the Concurrency settings patch.
    /// </summary>
    public sealed class ConcurrencyReportsSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.ConcurrencyReports";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case("tree_groups_stages_and_children", "Stages hang off their root, a child of a stage sits under the root, and depth is counted by logical mission", TreeAsync),
                Case("enumerate_hierarchy_and_gate_filters", "parentMissionId, rootsOnly and waitingGate filter full and summary enumeration", FiltersAsync),
                Case("interval_query_filters", "The interval query filters by vessel, open state and window, newest first, with a row cap", IntervalQueryAsync),
                Case("vessel_report_slots_waits_and_percentiles", "The vessel report counts held slots, lists waiting missions by gate and computes slot wait from capacity gates only", VesselReportAsync),
                Case("metrics_window_buckets_and_validation", "Metrics sum wait by gate within the window, bucket it and reject an inverted or oversized window", MetricsAsync),
                Case("captain_attribution_and_filter", "Assignment stamps the captain on the episode's waits, pinned waits belong to the requested captain, and metrics filter by captain", CaptainFilterAsync),
                Case("captain_max_concurrency_filter", "maxConcurrency filters captain enumeration exactly, in every scope, and parses from the querystring", MaxConcurrencyFilterAsync),
                Case("settings_patch_partial_clamped_and_detects_change", "A Concurrency patch changes only sent members, clamps values and reports whether anything changed", PatchAsync)
            };
            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Concurrency reports", cases: cases);
        }

        private static Task TreeAsync(CancellationToken token)
        {
            DateTime t = DateTime.UtcNow.AddMinutes(-10);
            MissionSummary a = Summary("a", t);
            MissionSummary a2 = Summary("a2", t.AddSeconds(1), dependsOn: "a");
            MissionSummary a3 = Summary("a3", t.AddSeconds(2), dependsOn: "a2");
            MissionSummary b = Summary("b", t.AddSeconds(3), parent: "a");
            MissionSummary c = Summary("c", t.AddSeconds(4), parent: "a2");
            MissionSummary d = Summary("d", t.AddSeconds(5), parent: "b");
            MissionSummary e = Summary("e", t.AddSeconds(6));
            MissionSummary orphan = Summary("orphan", t.AddSeconds(7), parent: "missing");
            List<MissionTreeNode> roots = MissionTreeBuilder.Build(new[] { d, c, b, a3, a2, a, e, orphan });
            AssertEqual("a,e,orphan", String.Join(",", roots.Select(n => n.Mission.Id)), "top level in creation order; a missing parent leaves a mission at the top");
            MissionTreeNode na = roots[0];
            AssertEqual("a2,a3", String.Join(",", na.Stages.Select(s => s.Id)), "stages in chain order");
            AssertEqual("b,c", String.Join(",", na.Children.Select(n => n.Mission.Id)), "a child of a stage belongs to the root");
            AssertEqual("d", na.Children[0].Children.Single().Mission.Id);
            AssertEqual(2, MissionTreeBuilder.MaxDepth(roots));
            AssertEqual(0, MissionTreeBuilder.MaxDepth(new List<MissionTreeNode>()));
            return Task.CompletedTask;
        }

        private static async Task FiltersAsync(CancellationToken token)
        {
            using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            DatabaseDriver db = fixture.Driver;
            Vessel vessel = await db.Vessels.CreateAsync(new Vessel("filters", "https://github.com/test/repo")).ConfigureAwait(false);
            Voyage voyage = await db.Voyages.CreateAsync(new Voyage("filters")).ConfigureAwait(false);
            Mission root = await db.Missions.CreateAsync(new Mission("root") { VesselId = vessel.Id, VoyageId = voyage.Id }).ConfigureAwait(false);
            Mission stage = await db.Missions.CreateAsync(new Mission("[Judge] root") { VesselId = vessel.Id, VoyageId = voyage.Id, DependsOnMissionId = root.Id }).ConfigureAwait(false);
            Mission child = await db.Missions.CreateAsync(new Mission("child") { VesselId = vessel.Id, VoyageId = voyage.Id, ParentMissionId = root.Id, Depth = 1 }).ConfigureAwait(false);
            Mission other = await db.Missions.CreateAsync(new Mission("other") { VesselId = vessel.Id, VoyageId = voyage.Id }).ConfigureAwait(false);
            await db.MissionWaitIntervals.CreateAsync(new MissionWaitInterval { MissionId = child.Id, VesselId = vessel.Id, Gate = MissionWaitGateEnum.Parent, StartedUtc = DateTime.UtcNow.AddMinutes(-2) }).ConfigureAwait(false);
            await db.MissionWaitIntervals.CreateAsync(new MissionWaitInterval { MissionId = other.Id, VesselId = vessel.Id, Gate = MissionWaitGateEnum.CaptainSlot, StartedUtc = DateTime.UtcNow.AddMinutes(-1) }).ConfigureAwait(false);
            await db.MissionWaitIntervals.CreateAsync(new MissionWaitInterval { MissionId = root.Id, VesselId = vessel.Id, Gate = MissionWaitGateEnum.CaptainSlot, StartedUtc = DateTime.UtcNow.AddMinutes(-5), EndedUtc = DateTime.UtcNow.AddMinutes(-4) }).ConfigureAwait(false);

            async Task<string> Ids(EnumerationQuery query)
            {
                query.VoyageId = voyage.Id; query.PageSize = 50; query.Order = EnumerationOrderEnum.CreatedAscending;
                EnumerationResult<Mission> full = await db.Missions.EnumerateAsync(query).ConfigureAwait(false);
                EnumerationResult<MissionSummary> summaries = await db.Missions.EnumerateSummariesAsync(query).ConfigureAwait(false);
                string fullIds = String.Join(",", full.Objects.Select(m => m.Title));
                AssertEqual(fullIds, String.Join(",", summaries.Objects.Select(m => m.Title)), "full and summary enumeration agree");
                AssertEqual(full.Objects.Count, (int)full.TotalRecords, "the count uses the same filter");
                return fullIds;
            }

            AssertEqual("child", await Ids(new EnumerationQuery { ParentMissionId = root.Id }).ConfigureAwait(false));
            AssertEqual("root,other", await Ids(new EnumerationQuery { RootsOnly = true }).ConfigureAwait(false), "roots exclude children and later stages");
            AssertEqual("other", await Ids(new EnumerationQuery { WaitingGate = MissionWaitGateEnum.CaptainSlot }).ConfigureAwait(false), "a closed interval does not count");
            AssertEqual("child", await Ids(new EnumerationQuery { WaitingGate = MissionWaitGateEnum.Parent }).ConfigureAwait(false));
            Mission otherRow = (await db.Missions.ReadAsync(other.Id).ConfigureAwait(false))!;
            otherRow.Status = MissionStatusEnum.InProgress; await db.Missions.UpdateAsync(otherRow).ConfigureAwait(false);
            AssertEqual("", await Ids(new EnumerationQuery { WaitingGate = MissionWaitGateEnum.CaptainSlot }).ConfigureAwait(false), "only Pending missions are waiting");

            EnumerationQuery parsed = new EnumerationQuery();
            Dictionary<string, string> qs = new Dictionary<string, string> { ["parentMissionId"] = root.Id, ["rootsOnly"] = "true", ["waitingGate"] = "captainslot" };
            parsed.ApplyQuerystringOverrides(key => qs.TryGetValue(key, out string? v) ? v : null);
            AssertEqual(root.Id, parsed.ParentMissionId); AssertEqual(true, parsed.RootsOnly); AssertEqual(MissionWaitGateEnum.CaptainSlot, parsed.WaitingGate);
            EnumerationQuery invalid = new EnumerationQuery();
            invalid.ApplyQuerystringOverrides(key => key == "waitingGate" ? "99" : null);
            AssertNull(invalid.WaitingGate, "an undefined gate is ignored");
        }

        private static async Task IntervalQueryAsync(CancellationToken token)
        {
            using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            DatabaseDriver db = fixture.Driver;
            DateTime now = DateTime.UtcNow;
            Dictionary<string, string> v = await VesselsAsync(db, "vsl_1", "vsl_2").ConfigureAwait(false);
            Dictionary<string, string> m = await MissionsAsync(db, v["vsl_1"], "msn_a", "msn_b", "msn_c").ConfigureAwait(false);
            foreach (KeyValuePair<string, string> pair in await MissionsAsync(db, v["vsl_2"], "msn_d").ConfigureAwait(false)) m[pair.Key] = pair.Value;
            await Interval(db, m["msn_a"], v["vsl_1"], MissionWaitGateEnum.CaptainSlot, now.AddHours(-3), now.AddHours(-2)).ConfigureAwait(false);
            await Interval(db, m["msn_b"], v["vsl_1"], MissionWaitGateEnum.Dependency, now.AddMinutes(-30), null).ConfigureAwait(false);
            await Interval(db, m["msn_c"], v["vsl_1"], MissionWaitGateEnum.GlobalSlot, now.AddMinutes(-10), now.AddMinutes(-5)).ConfigureAwait(false);
            await Interval(db, m["msn_d"], v["vsl_2"], MissionWaitGateEnum.CaptainSlot, now.AddMinutes(-20), null).ConfigureAwait(false);
            Dictionary<string, string> names = m.ToDictionary(p => p.Value, p => p.Key);
            string Names(IEnumerable<MissionWaitInterval> rows) => String.Join(",", rows.Select(i => names[i.MissionId]));

            List<MissionWaitInterval> vessel1 = await db.MissionWaitIntervals.EnumerateAsync(new MissionWaitIntervalQuery { VesselId = v["vsl_1"] }).ConfigureAwait(false);
            AssertEqual("msn_c,msn_b,msn_a", Names(vessel1), "newest first");
            List<MissionWaitInterval> open = await db.MissionWaitIntervals.EnumerateAsync(new MissionWaitIntervalQuery { OpenOnly = true }).ConfigureAwait(false);
            AssertEqual("msn_b,msn_d", Names(open.OrderBy(i => names[i.MissionId])));
            List<MissionWaitInterval> lastHour = await db.MissionWaitIntervals.EnumerateAsync(new MissionWaitIntervalQuery { VesselId = v["vsl_1"], FromUtc = now.AddHours(-1), ToUtc = now }).ConfigureAwait(false);
            AssertEqual("msn_b,msn_c", Names(lastHour.OrderBy(i => names[i.MissionId])), "an interval that ended before the window is excluded; an open one is kept");
            List<MissionWaitInterval> early = await db.MissionWaitIntervals.EnumerateAsync(new MissionWaitIntervalQuery { ToUtc = now.AddMinutes(-25) }).ConfigureAwait(false);
            AssertEqual("msn_a,msn_b", Names(early.OrderBy(i => names[i.MissionId])), "only intervals started before the end");
            List<MissionWaitInterval> capped = await db.MissionWaitIntervals.EnumerateAsync(new MissionWaitIntervalQuery { MaxRows = 2 }).ConfigureAwait(false);
            AssertEqual(2, capped.Count);
            AssertEqual(1, new MissionWaitIntervalQuery { MaxRows = 0 }.MaxRows, "the cap is clamped");
        }

        private static async Task VesselReportAsync(CancellationToken token)
        {
            using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            DatabaseDriver db = fixture.Driver;
            DateTime now = DateTime.UtcNow;
            TenantMetadata tenant = await db.Tenants.CreateAsync(new TenantMetadata("reports")).ConfigureAwait(false);
            string ownerId = (await db.Users.CreateAsync(new UserMaster(tenant.Id, "owner@example.invalid", "fixture-password")).ConfigureAwait(false)).Id;
            string otherId = (await db.Users.CreateAsync(new UserMaster(tenant.Id, "other@example.invalid", "fixture-password")).ConfigureAwait(false)).Id;
            Vessel vessel = await db.Vessels.CreateAsync(new Vessel("report", "https://github.com/test/repo") { MaxParallelVoyages = 2, MaxParallelMissionsPerVoyage = 3 }).ConfigureAwait(false);
            Voyage running = await db.Voyages.CreateAsync(new Voyage("running") { Status = VoyageStatusEnum.InProgress, TenantId = tenant.Id, UserId = ownerId }).ConfigureAwait(false);
            Voyage queued = await db.Voyages.CreateAsync(new Voyage("queued") { Status = VoyageStatusEnum.Open }).ConfigureAwait(false);
            Mission active = await db.Missions.CreateAsync(new Mission("active") { VesselId = vessel.Id, VoyageId = running.Id, Status = MissionStatusEnum.InProgress, TenantId = tenant.Id, UserId = ownerId }).ConfigureAwait(false);
            Mission sibling = await db.Missions.CreateAsync(new Mission("sibling") { VesselId = vessel.Id, VoyageId = running.Id, TenantId = tenant.Id, UserId = ownerId }).ConfigureAwait(false);
            Mission waiting = await db.Missions.CreateAsync(new Mission("waiting") { VesselId = vessel.Id, VoyageId = queued.Id, TenantId = tenant.Id, UserId = otherId }).ConfigureAwait(false);
            Mission done = await db.Missions.CreateAsync(new Mission("done") { VesselId = vessel.Id, VoyageId = running.Id, Status = MissionStatusEnum.Complete }).ConfigureAwait(false);
            await Interval(db, waiting.Id, vessel.Id, MissionWaitGateEnum.VesselVoyageSlot, now.AddSeconds(-90), null).ConfigureAwait(false);
            await Interval(db, sibling.Id, vessel.Id, MissionWaitGateEnum.VoyageMissionSlot, now.AddSeconds(-30), null).ConfigureAwait(false);
            await Interval(db, done.Id, vessel.Id, MissionWaitGateEnum.CaptainSlot, now.AddHours(-2), now.AddHours(-2).AddSeconds(100)).ConfigureAwait(false);
            await Interval(db, done.Id, vessel.Id, MissionWaitGateEnum.Dependency, now.AddHours(-3), now.AddHours(-2)).ConfigureAwait(false);
            await Interval(db, active.Id, vessel.Id, MissionWaitGateEnum.CaptainSlot, now.AddDays(-3), now.AddDays(-3).AddSeconds(600)).ConfigureAwait(false);

            ConcurrencyReportService reports = new ConcurrencyReportService(db);
            VesselConcurrency report = await reports.GetVesselConcurrencyAsync(vessel, null, null).ConfigureAwait(false);
            AssertEqual(2, report.MaxParallelVoyages); AssertEqual(3, report.MaxParallelMissionsPerVoyage);
            AssertEqual(1, report.VoyageSlotsInUse, "only the running voyage holds a slot; the queued one is all Pending");
            AssertEqual(1, report.RunningMissions);
            AssertEqual(running.Id, report.HoldingVoyages.Single().VoyageId);
            AssertEqual(1, report.HoldingVoyages[0].RunningMissions); AssertEqual(1, report.HoldingVoyages[0].PendingMissions);
            AssertEqual(2, report.WaitingCount);
            AssertEqual("waiting,sibling", String.Join(",", report.Waiting.Select(w => w.Title)), "longest wait first");
            AssertEqual(MissionWaitGateEnum.VesselVoyageSlot, report.Waiting[0].Gate);
            AssertTrue(report.Waiting[0].WaitedSeconds >= 89, "waited seconds count from the interval start");
            AssertEqual(3, report.SlotWait24h.Missions, "capacity waits only; the dependency wait is excluded");
            AssertTrue(report.SlotWait24h.MaxSeconds >= 99 && report.SlotWait24h.MaxSeconds <= 101, "the longest 24h slot wait is the 100 s captain wait");
            AssertEqual(4, report.SlotWait7d.Missions, "the 7-day window adds the older wait");
            AssertTrue(report.SlotWait7d.MaxSeconds >= 599, "the 600 s wait three days ago");

            VesselConcurrency scoped = await reports.GetVesselConcurrencyAsync(vessel, null, ownerId).ConfigureAwait(false);
            AssertEqual(2, scoped.WaitingCount, "counts stay vessel-wide");
            AssertEqual("sibling", String.Join(",", scoped.Waiting.Select(w => w.Title)), "another user's waiting mission is not listed");
            AssertEqual(1, scoped.HoldingVoyages.Count, "the caller's own voyage is listed");
        }

        private static async Task MetricsAsync(CancellationToken token)
        {
            using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            DatabaseDriver db = fixture.Driver;
            DateTime to = DateTime.UtcNow;
            DateTime from = to.AddHours(-2);
            Dictionary<string, string> v = await VesselsAsync(db, "vsl_1", "vsl_2").ConfigureAwait(false);
            Dictionary<string, string> m = await MissionsAsync(db, v["vsl_1"], "msn_a", "msn_b").ConfigureAwait(false);
            foreach (KeyValuePair<string, string> pair in await MissionsAsync(db, v["vsl_2"], "msn_c").ConfigureAwait(false)) m[pair.Key] = pair.Value;
            await Interval(db, m["msn_a"], v["vsl_1"], MissionWaitGateEnum.CaptainSlot, from.AddMinutes(-30), from.AddMinutes(10)).ConfigureAwait(false);
            await Interval(db, m["msn_b"], v["vsl_1"], MissionWaitGateEnum.Dependency, from.AddMinutes(20), from.AddMinutes(50)).ConfigureAwait(false);
            await Interval(db, m["msn_b"], v["vsl_1"], MissionWaitGateEnum.GlobalSlot, from.AddMinutes(50), from.AddMinutes(55)).ConfigureAwait(false);
            await Interval(db, m["msn_c"], v["vsl_2"], MissionWaitGateEnum.CaptainSlot, from.AddMinutes(30), from.AddMinutes(31)).ConfigureAwait(false);

            ConcurrencyReportService reports = new ConcurrencyReportService(db);
            ConcurrencyMetrics all = await reports.GetMetricsAsync(from, to, null, null).ConfigureAwait(false);
            AssertEqual(300, all.BucketSeconds, "a 2 hour window uses 5 minute buckets");
            AssertEqual(24, all.Buckets.Count);
            GateWaitTotal dependency = all.ByGate.Single(g => g.Gate == MissionWaitGateEnum.Dependency);
            AssertTrue(Math.Abs(dependency.Seconds - 1800) < 1, "30 minutes on the dependency gate");
            GateWaitTotal captain = all.ByGate.Single(g => g.Gate == MissionWaitGateEnum.CaptainSlot);
            AssertTrue(Math.Abs(captain.Seconds - 660) < 1, "only the 10 minutes inside the window count, plus the 1 minute wait");
            AssertEqual(2, captain.Missions);
            AssertEqual(MissionWaitGateEnum.Dependency, all.ByGate[0].Gate, "largest first");
            AssertEqual(3, all.SlotWait.Missions);
            AssertFalse(all.Truncated);
            AssertEqual(1, all.Buckets[0].MissionsWaiting, "the first bucket has only the captain wait");
            AssertEqual(0, all.Buckets[23].MissionsWaiting);

            ConcurrencyMetrics vessel2 = await reports.GetMetricsAsync(from, to, v["vsl_2"], null).ConfigureAwait(false);
            AssertEqual(1, vessel2.ByGate.Single().Missions);

            ConcurrencyMetrics defaults = await reports.GetMetricsAsync(null, null, null, null).ConfigureAwait(false);
            AssertTrue(Math.Abs((defaults.ToUtc - defaults.FromUtc).TotalHours - 24) < 0.01, "the default window is 24 hours");
            AssertEqual(3600, defaults.BucketSeconds, "a day uses 1 hour buckets");

            bool threw = false;
            try { await reports.GetMetricsAsync(to, from, null, null).ConfigureAwait(false); } catch (ArgumentException) { threw = true; }
            AssertTrue(threw, "an inverted window is rejected");
            threw = false;
            try { await reports.GetMetricsAsync(to.AddDays(-40), to, null, null).ConfigureAwait(false); } catch (ArgumentException) { threw = true; }
            AssertTrue(threw, "a window over 31 days is rejected");
        }

        private static async Task CaptainFilterAsync(CancellationToken token)
        {
            using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            DatabaseDriver db = fixture.Driver;
            DateTime now = DateTime.UtcNow;
            Dictionary<string, string> v = await VesselsAsync(db, "vsl_1").ConfigureAwait(false);
            Captain first = await db.Captains.CreateAsync(new Captain("first")).ConfigureAwait(false);
            Captain second = await db.Captains.CreateAsync(new Captain("second")).ConfigureAwait(false);
            Dictionary<string, string> m = await MissionsAsync(db, v["vsl_1"], "ran", "repended", "pinned", "unassigned").ConfigureAwait(false);
            await Interval(db, m["ran"], v["vsl_1"], MissionWaitGateEnum.CaptainSlot, now.AddMinutes(-50), now.AddMinutes(-40)).ConfigureAwait(false);
            await Interval(db, m["repended"], v["vsl_1"], MissionWaitGateEnum.CaptainSlot, now.AddMinutes(-50), now.AddMinutes(-45)).ConfigureAwait(false);
            await Interval(db, m["repended"], v["vsl_1"], MissionWaitGateEnum.GlobalSlot, now.AddMinutes(-20), now.AddMinutes(-10)).ConfigureAwait(false);
            await db.MissionWaitIntervals.CreateAsync(new MissionWaitInterval { MissionId = m["pinned"], VesselId = v["vsl_1"], CaptainId = second.Id, Gate = MissionWaitGateEnum.CaptainSlot, StartedUtc = now.AddMinutes(-5) }).ConfigureAwait(false);
            await Interval(db, m["unassigned"], v["vsl_1"], MissionWaitGateEnum.CaptainSlot, now.AddMinutes(-5), null).ConfigureAwait(false);

            AssertEqual(1, await db.MissionWaitIntervals.AttributeCaptainAsync(m["ran"], first.Id, now.AddMinutes(-55)).ConfigureAwait(false));
            AssertEqual(2, await db.MissionWaitIntervals.AttributeCaptainAsync(m["repended"], first.Id, now.AddMinutes(-55)).ConfigureAwait(false), "both waits of the first episode");
            AssertEqual(1, await db.MissionWaitIntervals.AttributeCaptainAsync(m["repended"], second.Id, now.AddMinutes(-25)).ConfigureAwait(false), "a later episode is re-attributed without touching the earlier one");
            List<MissionWaitInterval> repended = await db.MissionWaitIntervals.EnumerateByMissionAsync(m["repended"]).ConfigureAwait(false);
            AssertEqual(first.Id + "," + second.Id, String.Join(",", repended.Select(i => i.CaptainId)));

            ConcurrencyReportService reports = new ConcurrencyReportService(db);
            ConcurrencyMetrics forFirst = await reports.GetMetricsAsync(now.AddHours(-1), now, null, null, first.Id).ConfigureAwait(false);
            AssertEqual(first.Id, forFirst.CaptainId);
            AssertEqual(2, forFirst.SlotWait.Missions, "the two missions the first captain ran");
            AssertTrue(Math.Abs(forFirst.ByGate.Single().Seconds - 900) < 1, "10 + 5 minutes on the captain slot gate");
            ConcurrencyMetrics forSecond = await reports.GetMetricsAsync(now.AddHours(-1), now, null, null, second.Id).ConfigureAwait(false);
            AssertEqual(2, forSecond.SlotWait.Missions, "the re-pended episode plus the open pinned wait");
            ConcurrencyMetrics everyone = await reports.GetMetricsAsync(now.AddHours(-1), now, null, null).ConfigureAwait(false);
            AssertEqual(4, everyone.SlotWait.Missions, "without a captain filter the unattributed wait counts too");
        }

        private static async Task MaxConcurrencyFilterAsync(CancellationToken token)
        {
            using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            DatabaseDriver db = fixture.Driver;
            TenantMetadata tenant = await db.Tenants.CreateAsync(new TenantMetadata("slots")).ConfigureAwait(false);
            UserMaster owner = await db.Users.CreateAsync(new UserMaster(tenant.Id, "slots@example.invalid", "fixture-password")).ConfigureAwait(false);
            UserMaster other = await db.Users.CreateAsync(new UserMaster(tenant.Id, "slots-other@example.invalid", "fixture-password")).ConfigureAwait(false);
            await db.Captains.CreateAsync(new Captain("serial") { TenantId = tenant.Id, UserId = owner.Id, MaxConcurrency = 1 }).ConfigureAwait(false);
            await db.Captains.CreateAsync(new Captain("wide-own") { TenantId = tenant.Id, UserId = owner.Id, MaxConcurrency = 3 }).ConfigureAwait(false);
            await db.Captains.CreateAsync(new Captain("wide-other") { TenantId = tenant.Id, UserId = other.Id, MaxConcurrency = 3 }).ConfigureAwait(false);

            string Names(EnumerationResult<Captain> page) => String.Join(",", page.Objects.Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal));
            EnumerationQuery Query(int slots) => new EnumerationQuery { MaxConcurrency = slots, PageSize = 50 };
            AssertEqual("wide-other,wide-own", Names(await db.Captains.EnumerateAsync(Query(3)).ConfigureAwait(false)));
            AssertEqual("serial", Names(await db.Captains.EnumerateAsync(Query(1)).ConfigureAwait(false)));
            EnumerationResult<Captain> tenantPage = await db.Captains.EnumerateAsync(tenant.Id, Query(3)).ConfigureAwait(false);
            AssertEqual("wide-other,wide-own", Names(tenantPage)); AssertEqual(2L, (long)tenantPage.TotalRecords, "the count uses the filter");
            AssertEqual("wide-own", Names(await db.Captains.EnumerateAsync(tenant.Id, owner.Id, Query(3)).ConfigureAwait(false)));
            AssertEqual("", Names(await db.Captains.EnumerateAsync(Query(7)).ConfigureAwait(false)));

            EnumerationQuery parsed = new EnumerationQuery();
            parsed.ApplyQuerystringOverrides(key => key == "maxConcurrency" ? "3" : null);
            AssertEqual(3, parsed.MaxConcurrency);
            EnumerationQuery invalid = new EnumerationQuery();
            invalid.ApplyQuerystringOverrides(key => key == "maxConcurrency" ? "many" : null);
            AssertNull(invalid.MaxConcurrency, "a non-number is ignored");
        }

        private static Task PatchAsync(CancellationToken token)
        {
            ConcurrencySettings settings = new ConcurrencySettings();
            int retention = settings.WaitIntervalRetentionDays;
            ConcurrencyPatch patch = new ConcurrencyPatch { StarvationWarningMinutes = 45 };
            patch.ApplyTo(settings, out bool changed);
            AssertTrue(changed); AssertEqual(45, settings.StarvationWarningMinutes);
            AssertEqual(retention, settings.WaitIntervalRetentionDays, "omitted members keep their values");
            new ConcurrencyPatch { StarvationWarningMinutes = 45 }.ApplyTo(settings, out changed);
            AssertFalse(changed, "the same value is not a change");
            IReadOnlyList<string> adjustments = new ConcurrencyPatch { MaxHierarchyDepth = 999 }.ApplyTo(settings, out changed);
            AssertEqual(16, settings.MaxHierarchyDepth, "clamped, not rejected");
            AssertTrue(adjustments.Count == 1 && adjustments[0].Contains("MaxHierarchyDepth"), "the clamp is reported");
            settings.DefaultEndpointCaptainMaxConcurrency = 8;
            settings.Normalize();
            new ConcurrencyPatch { MaxCaptainConcurrency = 3, DefaultEndpointCaptainMaxConcurrency = 6 }.ApplyTo(settings, out changed);
            AssertEqual(3, settings.MaxCaptainConcurrency);
            AssertEqual(3, settings.DefaultEndpointCaptainMaxConcurrency, "a default above the new ceiling is clamped to it");
            return Task.CompletedTask;
        }

        private static async Task<Dictionary<string, string>> VesselsAsync(DatabaseDriver db, params string[] names)
        {
            Dictionary<string, string> ids = new Dictionary<string, string>();
            foreach (string name in names) ids[name] = (await db.Vessels.CreateAsync(new Vessel(name, "https://github.com/test/" + name)).ConfigureAwait(false)).Id;
            return ids;
        }

        private static async Task<Dictionary<string, string>> MissionsAsync(DatabaseDriver db, string vesselId, params string[] names)
        {
            Dictionary<string, string> ids = new Dictionary<string, string>();
            foreach (string name in names) ids[name] = (await db.Missions.CreateAsync(new Mission(name) { VesselId = vesselId }).ConfigureAwait(false)).Id;
            return ids;
        }

        private static MissionSummary Summary(string id, DateTime created, string? parent = null, string? dependsOn = null)
            => new MissionSummary { Id = id, Title = id, CreatedUtc = created, ParentMissionId = parent, DependsOnMissionId = dependsOn };

        private static Task<MissionWaitInterval> Interval(DatabaseDriver db, string missionId, string vesselId, MissionWaitGateEnum gate, DateTime started, DateTime? ended)
            => db.MissionWaitIntervals.CreateAsync(new MissionWaitInterval { MissionId = missionId, VesselId = vesselId, Gate = gate, StartedUtc = started, EndedUtc = ended });

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
            => new TestCaseDescriptor(SuiteId, id, name, body, tags: new List<string> { TestTags.Positive });
    }
}
