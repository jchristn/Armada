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
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Data-source paging and aggregation for missions, voyages, signals, events, and the inbox (bounded enumeration,
    /// group A): mission history counted per bucket and status in the database (window, vessel, fleet, and tenant
    /// filters), the bucket limit, deterministic id tie-breaks when timestamps are equal, sectioned paging across
    /// sources, the active voyage progress enumeration, and the inbox read one page at a time. Runs on every provider.
    /// </summary>
    public sealed class WorkBoundsDatabaseSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.WorkBounds";

        private static readonly DateTime _BaseTime = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("history_buckets_counted_in_database", "Mission history is counted per bucket and status in the database, inside the window only, with vessel and fleet filters", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Fleet fleetA = await db.Fleets.CreateAsync(new Fleet("History Fleet A") { CreatedUtc = _BaseTime });
                    Fleet fleetB = await db.Fleets.CreateAsync(new Fleet("History Fleet B") { CreatedUtc = _BaseTime });
                    Vessel vesselA = await db.Vessels.CreateAsync(new Vessel("History A", "https://github.com/test/a") { FleetId = fleetA.Id, CreatedUtc = _BaseTime });
                    Vessel vesselB = await db.Vessels.CreateAsync(new Vessel("History B", "https://github.com/test/b") { FleetId = fleetB.Id, CreatedUtc = _BaseTime });

                    // Bucket 0 (00:00-01:00): 2 Complete on A, 1 Failed on B. Bucket 2 (02:00-03:00): 1 Pending on A.
                    // 00:59:59.9 stays in bucket 0; exactly 02:00 starts bucket 2. One mission before and one at the end
                    // of the window are outside it.
                    await CreateMissionAtAsync(db, vesselA, _BaseTime.AddMinutes(5), MissionStatusEnum.Complete);
                    await CreateMissionAtAsync(db, vesselA, _BaseTime.AddMinutes(59).AddSeconds(59).AddMilliseconds(900), MissionStatusEnum.Complete);
                    await CreateMissionAtAsync(db, vesselB, _BaseTime.AddMinutes(30), MissionStatusEnum.Failed);
                    await CreateMissionAtAsync(db, vesselA, _BaseTime.AddHours(2), MissionStatusEnum.Pending);
                    await CreateMissionAtAsync(db, vesselA, _BaseTime.AddMinutes(-1), MissionStatusEnum.Complete);
                    await CreateMissionAtAsync(db, vesselA, _BaseTime.AddHours(3), MissionStatusEnum.Complete);

                    MissionHistoryQuery query = new MissionHistoryQuery { FromUtc = _BaseTime, ToUtc = _BaseTime.AddHours(3), BucketMinutes = 60 };
                    List<MissionHistoryBucketCount> all = await db.Missions.CountHistoryBucketsAsync(query);
                    AssertEqual(2L, CountOf(all, 0, MissionStatusEnum.Complete), "two Complete in bucket 0 (the 00:59:59.9 one included)");
                    AssertEqual(1L, CountOf(all, 0, MissionStatusEnum.Failed), "one Failed in bucket 0");
                    AssertEqual(1L, CountOf(all, 2, MissionStatusEnum.Pending), "the 02:00 mission starts bucket 2");
                    AssertEqual(4L, all.Sum(c => c.Count), "missions outside the window are not counted");
                    AssertEqual(3, all.Count, "one row per bucket and status, not per mission");

                    List<MissionHistoryBucketCount> byVessel = await db.Missions.CountHistoryBucketsAsync(new MissionHistoryQuery { FromUtc = query.FromUtc, ToUtc = query.ToUtc, BucketMinutes = 60, VesselId = vesselB.Id });
                    AssertEqual(1L, byVessel.Sum(c => c.Count), "vessel filter");
                    List<MissionHistoryBucketCount> byFleet = await db.Missions.CountHistoryBucketsAsync(new MissionHistoryQuery { FromUtc = query.FromUtc, ToUtc = query.ToUtc, BucketMinutes = 60, FleetId = fleetA.Id });
                    AssertEqual(3L, byFleet.Sum(c => c.Count), "fleet filter counts fleet A's vessels only, in the database");

                    List<MissionHistoryBucketCount> wide = await db.Missions.CountHistoryBucketsAsync(new MissionHistoryQuery { FromUtc = _BaseTime.AddMinutes(10), ToUtc = query.ToUtc, BucketMinutes = 180 });
                    AssertEqual(3L, wide.Sum(c => c.Count), "window start honored");
                    AssertTrue(wide.All(c => c.BucketIndex == 0), "an unaligned start falls in the aligned first bucket");
                }
            }));

            cases.Add(CaseAsync("history_buckets_tenant_scoped", "Tenant-scoped history counts only that tenant's missions", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    TenantMetadata tA = await db.Tenants.CreateAsync(new TenantMetadata("History Tenant A " + Guid.NewGuid().ToString("N").Substring(0, 6)));
                    TenantMetadata tB = await db.Tenants.CreateAsync(new TenantMetadata("History Tenant B " + Guid.NewGuid().ToString("N").Substring(0, 6)));
                    Vessel vessel = await db.Vessels.CreateAsync(new Vessel("History Tenant Vessel", "https://github.com/test/t") { CreatedUtc = _BaseTime });
                    await CreateMissionAtAsync(db, vessel, _BaseTime.AddMinutes(1), MissionStatusEnum.Complete, tA.Id);
                    await CreateMissionAtAsync(db, vessel, _BaseTime.AddMinutes(2), MissionStatusEnum.Complete, tA.Id);
                    await CreateMissionAtAsync(db, vessel, _BaseTime.AddMinutes(3), MissionStatusEnum.Complete, tB.Id);
                    MissionHistoryQuery query = new MissionHistoryQuery { FromUtc = _BaseTime, ToUtc = _BaseTime.AddHours(1), BucketMinutes = 15 };
                    AssertEqual(2L, (await db.Missions.CountHistoryBucketsAsync(tA.Id, query)).Sum(c => c.Count), "tenant A");
                    AssertEqual(1L, (await db.Missions.CountHistoryBucketsAsync(tB.Id, query)).Sum(c => c.Count), "tenant B");
                }
            }));

            cases.Add(Case("history_window_over_bucket_limit_rejected", "A history window that would produce more than MaxSummaryBuckets buckets is rejected with the field and limit", TestTags.Negative, () =>
            {
                MissionHistoryQuery ok = new MissionHistoryQuery { FromUtc = _BaseTime, ToUtc = _BaseTime.AddMinutes(999), BucketMinutes = 1 };
                ok.Validate();
                MissionHistoryQuery over = new MissionHistoryQuery { FromUtc = _BaseTime, ToUtc = _BaseTime.AddDays(30), BucketMinutes = 1 };
                CollectionLimitExceededException? ex = null;
                try { over.Validate(); }
                catch (CollectionLimitExceededException caught) { ex = caught; }
                AssertNotNull(ex, "a 30-day window of 1-minute buckets is rejected");
                AssertEqual(CollectionLimits.MaxSummaryBuckets, ex!.Limit, "limit named");
                AssertEqual("Buckets", ex.Field, "field named");
                new MissionHistoryQuery { FromUtc = _BaseTime, ToUtc = _BaseTime.AddDays(30), BucketMinutes = 60 }.Validate();
            }));

            cases.Add(CaseAsync("equal_timestamps_page_by_id", "Voyages, missions, signals, and events with equal created_utc page deterministically by id: no overlap, no gaps", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    List<string> voyageIds = new List<string>();
                    for (int i = 0; i < 7; i++) voyageIds.Add((await db.Voyages.CreateAsync(new Voyage("Tie " + i) { CreatedUtc = _BaseTime })).Id);
                    Vessel vessel = await db.Vessels.CreateAsync(new Vessel("Tie Vessel", "https://github.com/test/tie") { CreatedUtc = _BaseTime });
                    List<string> missionIds = new List<string>();
                    for (int i = 0; i < 7; i++) missionIds.Add((await CreateMissionAtAsync(db, vessel, _BaseTime, MissionStatusEnum.Pending)).Id);
                    List<string> eventIds = new List<string>();
                    for (int i = 0; i < 7; i++)
                    {
                        ArmadaEvent evt = new ArmadaEvent("tie.event", "tie " + i) { CreatedUtc = _BaseTime };
                        eventIds.Add((await db.Events.CreateAsync(evt)).Id);
                    }

                    foreach (EnumerationOrderEnum order in new[] { EnumerationOrderEnum.CreatedDescending, EnumerationOrderEnum.CreatedAscending })
                    {
                        // The id tie-break follows the provider's collation (case-sensitive or not), so the test checks
                        // what paging needs: every row exactly once across pages, and the same order on every read.
                        List<string> firstVoyages = await PageIdsAsync(page => db.Voyages.EnumerateAsync(new EnumerationQuery { PageNumber = page, PageSize = 2, Order = order }), v => v.Id);
                        List<string> againVoyages = await PageIdsAsync(page => db.Voyages.EnumerateAsync(new EnumerationQuery { PageNumber = page, PageSize = 2, Order = order }), v => v.Id);
                        AssertSamePagedSet(voyageIds, firstVoyages, againVoyages, "voyages (" + order + ")");

                        List<string> firstMissions = await PageIdsAsync(page => db.Missions.EnumerateAsync(new EnumerationQuery { PageNumber = page, PageSize = 2, Order = order }), m => m.Id);
                        List<string> againMissions = await PageIdsAsync(page => db.Missions.EnumerateAsync(new EnumerationQuery { PageNumber = page, PageSize = 2, Order = order }), m => m.Id);
                        AssertSamePagedSet(missionIds, firstMissions, againMissions, "missions (" + order + ")");

                        List<string> summaries = await PageIdsAsync(page => db.Missions.EnumerateSummariesAsync(new EnumerationQuery { PageNumber = page, PageSize = 2, Order = order }), m => m.Id);
                        AssertSamePagedSet(missionIds, summaries, firstMissions, "mission summaries match missions (" + order + ")");

                        List<string> firstEvents = await PageIdsAsync(page => db.Events.EnumerateAsync(new EnumerationQuery { PageNumber = page, PageSize = 2, Order = order, EventType = "tie.event" }), e => e.Id);
                        List<string> againEvents = await PageIdsAsync(page => db.Events.EnumerateAsync(new EnumerationQuery { PageNumber = page, PageSize = 2, Order = order, EventType = "tie.event" }), e => e.Id);
                        AssertSamePagedSet(eventIds, firstEvents, againEvents, "events (" + order + ")");
                    }
                }
            }));

            cases.Add(CaseAsync("sectioned_paging_windows", "Sectioned paging reads each source in bounded pages, places sections in order, and keeps exact totals past the end", TestTags.Positive, async () =>
            {
                List<int> first = Enumerable.Range(0, 7).ToList();
                List<int> second = Enumerable.Range(100, 5).ToList();
                int maxPageRead = 0;
                PageFetchDelegate<int> Source(List<int> rows) => (pageNumber, pageSize, token) =>
                {
                    maxPageRead = Math.Max(maxPageRead, pageSize);
                    return Task.FromResult(EnumerationResult<int>.Page(new EnumerationQuery { PageNumber = pageNumber, PageSize = pageSize }, rows));
                };
                List<PagedSection<int>> sections = new List<PagedSection<int>> { new PagedSection<int>("a", Source(first)), new PagedSection<int>("b", Source(second)) };
                List<int> all = new List<int>();
                for (int page = 1; page <= 4; page++)
                {
                    EnumerationResult<int> result = await SectionedPaging.PageAsync(new EnumerationQuery { PageNumber = page, PageSize = 4 }, sections);
                    AssertEqual(12L, result.TotalRecords, "total over both sections");
                    AssertEqual(3, result.TotalPages, "pages");
                    all.AddRange(result.Objects);
                }

                AssertEqual(String.Join(",", first.Concat(second)), String.Join(",", all), "sections in order, no overlap or gap across the boundary");
                AssertTrue(maxPageRead <= 4, "sources are read with the requested page size, never in full");
            }));

            cases.Add(CaseAsync("voyage_progress_pages_active_voyages", "Voyage progress pages active voyages in the database (InProgress first, then Open), with counts, status filter, and tenant scope", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    TenantMetadata tA = await db.Tenants.CreateAsync(new TenantMetadata("Progress A " + Guid.NewGuid().ToString("N").Substring(0, 6)));
                    TenantMetadata tB = await db.Tenants.CreateAsync(new TenantMetadata("Progress B " + Guid.NewGuid().ToString("N").Substring(0, 6)));
                    Vessel vessel = await db.Vessels.CreateAsync(new Vessel("Progress Vessel", "https://github.com/test/p") { CreatedUtc = _BaseTime });
                    List<string> inProgress = new List<string>();
                    for (int i = 0; i < 3; i++)
                    {
                        Voyage v = await db.Voyages.CreateAsync(new Voyage("Running " + i) { TenantId = tA.Id, Status = VoyageStatusEnum.InProgress, CreatedUtc = _BaseTime.AddMinutes(i) });
                        inProgress.Add(v.Id);
                        Mission m1 = await CreateMissionAtAsync(db, vessel, _BaseTime, MissionStatusEnum.Complete, tA.Id, v.Id);
                        await CreateMissionAtAsync(db, vessel, _BaseTime, MissionStatusEnum.InProgress, tA.Id, v.Id);
                    }

                    Voyage open = await db.Voyages.CreateAsync(new Voyage("Waiting") { TenantId = tA.Id, Status = VoyageStatusEnum.Open, CreatedUtc = _BaseTime.AddMinutes(10) });
                    await db.Voyages.CreateAsync(new Voyage("Done") { TenantId = tA.Id, Status = VoyageStatusEnum.Complete, CreatedUtc = _BaseTime.AddMinutes(11) });
                    await db.Voyages.CreateAsync(new Voyage("Other tenant") { TenantId = tB.Id, Status = VoyageStatusEnum.InProgress, CreatedUtc = _BaseTime.AddMinutes(12) });

                    AuthContext tenantAdmin = AuthContext.Authenticated(tA.Id, "usr_progress", false, true, "Test");
                    List<string> seen = new List<string>();
                    for (int page = 1; page <= 3; page++)
                    {
                        EnumerationResult<VoyageProgress> result = await VoyageProgressReader.EnumerateAsync(db, tenantAdmin, new EnumerationQuery { PageNumber = page, PageSize = 2 });
                        AssertEqual(4L, result.TotalRecords, "three InProgress and one Open in the tenant; the other tenant and the Complete voyage are not counted");
                        seen.AddRange(result.Objects.Select(p => p.Voyage.Id));
                        foreach (VoyageProgress p in result.Objects.Where(p => p.Voyage.Status == VoyageStatusEnum.InProgress))
                        {
                            AssertEqual(2, p.TotalMissions, "counts");
                            AssertEqual(1, p.MissionCountsByStatus["Complete"], "status counts");
                            AssertEqual(vessel.Id, p.VesselId, "vessel");
                            AssertEqual(1, p.VesselCount, "vessel count");
                        }
                    }

                    List<string> expected = inProgress.AsEnumerable().Reverse().Concat(new[] { open.Id }).ToList();
                    AssertEqual(String.Join(",", expected), String.Join(",", seen), "InProgress newest first, then Open");

                    EnumerationResult<VoyageProgress> complete = await VoyageProgressReader.EnumerateAsync(db, tenantAdmin, new EnumerationQuery { Status = "Complete" });
                    AssertEqual(1L, complete.TotalRecords, "status filter");
                    await AssertThrowsAsync<ArgumentException>(() => VoyageProgressReader.EnumerateAsync(db, tenantAdmin, new EnumerationQuery { Status = "Sideways" }));
                }
            }));

            cases.Add(CaseAsync("inbox_pages_at_source", "The inbox pages each source in the database: kinds in urgency order, newest first, no overlap across pages, exact total and counts", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    TenantMetadata tenant = await db.Tenants.CreateAsync(new TenantMetadata("Inbox Paging " + Guid.NewGuid().ToString("N").Substring(0, 6)));
                    Vessel vessel = await db.Vessels.CreateAsync(new Vessel("Inbox Vessel", "https://github.com/test/i") { CreatedUtc = _BaseTime });
                    List<string> failed = new List<string>();
                    for (int i = 0; i < 5; i++) failed.Add((await CreateMissionAtAsync(db, vessel, _BaseTime.AddMinutes(i), MissionStatusEnum.Failed, tenant.Id)).Id);
                    string review = (await CreateMissionAtAsync(db, vessel, _BaseTime, MissionStatusEnum.Review, tenant.Id)).Id;
                    string landing = (await CreateMissionAtAsync(db, vessel, _BaseTime, MissionStatusEnum.LandingFailed, tenant.Id)).Id;
                    await CreateMissionAtAsync(db, vessel, _BaseTime, MissionStatusEnum.Complete, tenant.Id);

                    InboxService inbox = new InboxService(db, new SyslogLogging.LoggingModule());
                    AuthContext admin = AuthContext.Authenticated(tenant.Id, "usr_inbox_paging", false, true, "Test");
                    List<InboxItem> items = new List<InboxItem>();
                    for (int page = 1; page <= 4; page++)
                    {
                        EnumerationResult<InboxItem> result = await inbox.EnumerateAsync(admin, new EnumerationQuery { PageNumber = page, PageSize = 3 });
                        AssertEqual(7L, result.TotalRecords, "review + landing failed + five failed");
                        AssertTrue(result.Objects.Count <= 3, "one page");
                        items.AddRange(result.Objects);
                    }

                    List<string> ids = items.Select(i => i.EntityId ?? "").ToList();
                    List<string> expected = new[] { review, landing }.Concat(failed.AsEnumerable().Reverse()).ToList();
                    AssertEqual(String.Join(",", expected), String.Join(",", ids), "review, then landing failed, then failed newest first");
                    AssertEqual(7, ids.Distinct().Count(), "no overlap across pages");
                    AssertEqual(1L, await inbox.CountAsync(admin, new[] { InboxItemKinds.Review }), "approval count without reading items");
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Bounded work reads in the database",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<Mission> CreateMissionAtAsync(DatabaseDriver db, Vessel vessel, DateTime createdUtc, MissionStatusEnum status, string? tenantId = null, string? voyageId = null)
        {
            Mission mission = new Mission("History " + Guid.NewGuid().ToString("N").Substring(0, 8), "desc");
            mission.VesselId = vessel.Id;
            mission.VoyageId = voyageId;
            mission.TenantId = tenantId;
            mission.Status = status;
            mission.CreatedUtc = createdUtc;
            mission.LastUpdateUtc = createdUtc;
            return await db.Missions.CreateAsync(mission).ConfigureAwait(false);
        }

        private static long CountOf(List<MissionHistoryBucketCount> counts, long bucket, MissionStatusEnum status)
        {
            return counts.Where(c => c.BucketIndex == bucket && c.Status == status).Sum(c => c.Count);
        }

        private static async Task<List<string>> PageIdsAsync<T>(Func<int, Task<EnumerationResult<T>>> page, Func<T, string> id)
        {
            List<string> ids = new List<string>();
            for (int number = 1; number <= 4; number++) ids.AddRange((await page(number).ConfigureAwait(false)).Objects.Select(id));
            return ids;
        }

        private static void AssertSamePagedSet(List<string> expected, List<string> paged, List<string> again, string label)
        {
            AssertEqual(expected.Count, paged.Count, label + ": every row once across pages");
            AssertEqual(expected.Count, paged.Distinct(StringComparer.Ordinal).Count(), label + ": no row on two pages");
            AssertTrue(expected.All(e => paged.Contains(e, StringComparer.Ordinal)), label + ": no row skipped");
            AssertEqual(String.Join(",", paged), String.Join(",", again), label + ": the same order on every read");
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

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; },
                tags: new List<string> { tag });
        }

        #endregion
    }
}
