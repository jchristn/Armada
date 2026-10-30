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
    /// Black box file rows and hot spots on every provider: bulk insert limits, role filter, hot-spot aggregation paged in SQL with tenant isolation, and delete by entry.
    /// </summary>
    public sealed class BlackBoxFileDatabaseSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Database.BlackBoxFiles";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("files_and_hotspots", "Hot spots group file rows by vessel and directory, largest first, tenant-scoped and paged", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = t.Driver;
                BlackBoxEntry e1 = await db.BlackBoxEntries.CreateAsync(BlackBoxTestKit.Entry("ten_h", "msn_h1")).ConfigureAwait(false);
                BlackBoxEntry e2 = await db.BlackBoxEntries.CreateAsync(BlackBoxTestKit.Entry("ten_h", "msn_h2")).ConfigureAwait(false);
                BlackBoxEntry eo = await db.BlackBoxEntries.CreateAsync(BlackBoxTestKit.Entry("ten_other", "msn_ho")).ConfigureAwait(false);
                List<BlackBoxFile> rows = new List<BlackBoxFile>();
                foreach (BlackBoxEntry e in new[] { e1, e2 })
                {
                    rows.Add(File(e, "src/core/a.cs", "src/core", BlackBoxFileRoleEnum.Conflicted));
                    rows.Add(File(e, "src/core/b.cs", "src/core", BlackBoxFileRoleEnum.Conflicted));
                    rows.Add(File(e, "docs/x.md", "docs", BlackBoxFileRoleEnum.Conflicted));
                }
                rows.Add(File(e1, "src/core/c.cs", "src/core", BlackBoxFileRoleEnum.DirtyInCheckout));
                rows.Add(File(eo, "src/core/a.cs", "src/core", BlackBoxFileRoleEnum.Conflicted));
                AssertEqual(rows.Count, await db.BlackBoxFiles.CreateManyAsync(rows).ConfigureAwait(false), "inserted");

                EnumerationResult<BlackBoxHotspot> hot = await db.BlackBoxFiles.EnumerateHotspotsAsync(new BlackBoxHotspotEnumerateRequest { TenantId = "ten_h" }).ConfigureAwait(false);
                AssertEqual(2L, hot.TotalRecords, "two directories");
                AssertEqual("src/core", hot.Objects[0].Directory, "largest first");
                AssertEqual(4L, hot.Objects[0].FileCount, "conflicted rows only by default");
                AssertEqual(2L, hot.Objects[0].EntryCount, "distinct entries");
                AssertNotNull(hot.Objects[0].LastSeenUtc, "last seen");
                EnumerationResult<BlackBoxHotspot> allRoles = await db.BlackBoxFiles.EnumerateHotspotsAsync(new BlackBoxHotspotEnumerateRequest { TenantId = "ten_h", Role = null }).ConfigureAwait(false);
                AssertEqual(5L, allRoles.Objects[0].FileCount, "every role");
                EnumerationResult<BlackBoxHotspot> p2 = await db.BlackBoxFiles.EnumerateHotspotsAsync(new BlackBoxHotspotEnumerateRequest { TenantId = "ten_h", PageSize = 1, PageNumber = 2 }).ConfigureAwait(false);
                AssertEqual("docs", p2.Objects.Single().Directory, "second page"); AssertEqual(2, p2.TotalPages);
                EnumerationResult<BlackBoxHotspot> other = await db.BlackBoxFiles.EnumerateHotspotsAsync(new BlackBoxHotspotEnumerateRequest { TenantId = "ten_other" }).ConfigureAwait(false);
                AssertEqual(1L, other.Objects.Single().FileCount, "tenant isolated");
                EnumerationResult<BlackBoxHotspot> none = await db.BlackBoxFiles.EnumerateHotspotsAsync(new BlackBoxHotspotEnumerateRequest { TenantId = "ten_h", VesselId = "vsl_none" }).ConfigureAwait(false);
                AssertEqual(0L, none.TotalRecords, "vessel filter");

                await db.BlackBoxEntries.UpdateStateAsync(e2.Id, BlackBoxStateEnum.Deleting).ConfigureAwait(false);
                hot = await db.BlackBoxFiles.EnumerateHotspotsAsync(new BlackBoxHotspotEnumerateRequest { TenantId = "ten_h" }).ConfigureAwait(false);
                AssertEqual(2L, hot.Objects[0].FileCount, "entries being deleted are excluded");

                EnumerationResult<BlackBoxFile> dirty = await db.BlackBoxFiles.EnumerateAsync(e1.Id, BlackBoxFileRoleEnum.DirtyInCheckout, new EnumerationQuery()).ConfigureAwait(false);
                AssertEqual("src/core/c.cs", dirty.Objects.Single().Path, "role filter");
                EnumerationResult<BlackBoxFile> files = await db.BlackBoxFiles.EnumerateAsync(e1.Id, null, new EnumerationQuery()).ConfigureAwait(false);
                AssertEqual("docs/x.md,src/core/a.cs,src/core/b.cs,src/core/c.cs", String.Join(",", files.Objects.Select(f => f.Path)), "ordered by path");
                AssertEqual(4, await db.BlackBoxFiles.DeleteByEntryAsync(e1.Id).ConfigureAwait(false), "delete by entry");
            }));

            cases.Add(Case("bulk_insert_limits", "More than 1000 rows in one call is rejected before anything is written; an empty list writes nothing", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = t.Driver;
                BlackBoxEntry e = await db.BlackBoxEntries.CreateAsync(BlackBoxTestKit.Entry("ten_l", "msn_l")).ConfigureAwait(false);
                List<BlackBoxFile> tooMany = Enumerable.Range(0, 1001).Select(i => File(e, "f" + i, "", BlackBoxFileRoleEnum.Conflicted)).ToList();
                await AssertThrowsAsync<CollectionLimitExceededException>(() => db.BlackBoxFiles.CreateManyAsync(tooMany), "files over the limit").ConfigureAwait(false);
                AssertEqual(0L, (await db.BlackBoxFiles.EnumerateAsync(e.Id, null, new EnumerationQuery()).ConfigureAwait(false)).TotalRecords, "nothing written");
                AssertEqual(0, await db.BlackBoxFiles.CreateManyAsync(new List<BlackBoxFile>()).ConfigureAwait(false), "empty");
                List<BlackBoxLink> links = Enumerable.Range(0, 1001).Select(i => new BlackBoxLink { EntryId = e.Id, MissionId = "msn_" + i }).ToList();
                await AssertThrowsAsync<CollectionLimitExceededException>(() => db.BlackBoxLinks.CreateManyAsync(links), "links over the limit").ConfigureAwait(false);
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box file rows and hot spots", cases: cases);
        }

        private static BlackBoxFile File(BlackBoxEntry e, string path, string directory, BlackBoxFileRoleEnum role)
        {
            return new BlackBoxFile { EntryId = e.Id, TenantId = e.TenantId, VesselId = e.VesselId, Path = path, Directory = directory, Role = role };
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }
    }
}
