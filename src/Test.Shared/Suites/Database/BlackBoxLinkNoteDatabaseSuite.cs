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
    /// Black box link rows and notes on every provider: insert, paging and order, links by related mission with tenant scope, note reads scoped to a tenant, counts and deletes.
    /// </summary>
    public sealed class BlackBoxLinkNoteDatabaseSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Database.BlackBoxLinksNotes";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("links", "Links page in creation order and are found by the related mission, tenant-scoped", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = t.Driver;
                DateTime t0 = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);
                List<BlackBoxLink> links = new List<BlackBoxLink>
                {
                    new BlackBoxLink { EntryId = "bbx_l1", TenantId = "ten_a", MissionId = "msn_rel", Relation = BlackBoxLinkRelationEnum.LandedOnTargetSinceStart, CommitHash = "abc1234", OverlapFileCount = 3, CreatedUtc = t0 },
                    new BlackBoxLink { EntryId = "bbx_l1", TenantId = "ten_a", MissionId = "msn_sib", Relation = BlackBoxLinkRelationEnum.SiblingInVoyage, CreatedUtc = t0.AddSeconds(1) },
                    new BlackBoxLink { EntryId = "bbx_l2", TenantId = "ten_b", MissionId = "msn_rel", Relation = BlackBoxLinkRelationEnum.OverlappingFiles, CreatedUtc = t0 }
                };
                AssertEqual(3, await db.BlackBoxLinks.CreateManyAsync(links).ConfigureAwait(false));
                EnumerationResult<BlackBoxLink> page = await db.BlackBoxLinks.EnumerateAsync("bbx_l1", new EnumerationQuery()).ConfigureAwait(false);
                AssertEqual("msn_rel,msn_sib", String.Join(",", page.Objects.Select(l => l.MissionId)), "creation order");
                BlackBoxLink first = page.Objects[0];
                AssertEqual(BlackBoxLinkRelationEnum.LandedOnTargetSinceStart, first.Relation); AssertEqual("abc1234", first.CommitHash); AssertEqual(3, first.OverlapFileCount);
                AssertEqual(2L, (await db.BlackBoxLinks.EnumerateByMissionAsync("msn_rel", null, new EnumerationQuery()).ConfigureAwait(false)).TotalRecords, "all tenants");
                AssertEqual("bbx_l2", (await db.BlackBoxLinks.EnumerateByMissionAsync("msn_rel", "ten_b", new EnumerationQuery()).ConfigureAwait(false)).Objects.Single().EntryId, "tenant-scoped");
                AssertEqual(2, await db.BlackBoxLinks.DeleteByEntryAsync("bbx_l1").ConfigureAwait(false), "deleted");
            }));

            cases.Add(Case("notes", "Notes round-trip, page newest first (or oldest first), count, read scoped to a tenant and delete", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = t.Driver;
                DateTime t0 = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);
                BlackBoxNote a = await db.BlackBoxNotes.CreateAsync(new BlackBoxNote { EntryId = "bbx_n", TenantId = "ten_a", UserId = "usr_a", AuthorKind = BlackBoxNoteAuthorKindEnum.Agent, Body = "first **note** \u00e9", CreatedUtc = t0 }).ConfigureAwait(false);
                BlackBoxNote b = await db.BlackBoxNotes.CreateAsync(new BlackBoxNote { EntryId = "bbx_n", TenantId = "ten_a", Body = "second", CreatedUtc = t0.AddMinutes(1) }).ConfigureAwait(false);
                BlackBoxNote c = await db.BlackBoxNotes.CreateAsync(new BlackBoxNote { EntryId = "bbx_n", TenantId = "ten_a", Body = new string('x', 9000), CreatedUtc = t0.AddMinutes(2) }).ConfigureAwait(false);
                BlackBoxNote read = (await db.BlackBoxNotes.ReadAsync(a.Id).ConfigureAwait(false))!;
                AssertTrue(read.Id.StartsWith("bbn_", StringComparison.Ordinal), "prefix");
                AssertEqual(BlackBoxNoteAuthorKindEnum.Agent, read.AuthorKind); AssertEqual("usr_a", read.UserId); AssertEqual(a.Body, read.Body);
                AssertNull(await db.BlackBoxNotes.ReadAsync(a.Id, "ten_b").ConfigureAwait(false), "other tenant");
                AssertEqual(8000, (await db.BlackBoxNotes.ReadAsync(c.Id).ConfigureAwait(false))!.Body.Length, "capped at 8000 characters");
                EnumerationResult<BlackBoxNote> newest = await db.BlackBoxNotes.EnumerateAsync("bbx_n", new EnumerationQuery { PageSize = 2 }).ConfigureAwait(false);
                AssertEqual(3L, newest.TotalRecords); AssertEqual(c.Id + "," + b.Id, String.Join(",", newest.Objects.Select(n => n.Id)), "newest first");
                EnumerationResult<BlackBoxNote> oldest = await db.BlackBoxNotes.EnumerateAsync("bbx_n", new EnumerationQuery { Order = EnumerationOrderEnum.CreatedAscending, PageSize = 1 }).ConfigureAwait(false);
                AssertEqual(a.Id, oldest.Objects.Single().Id, "oldest first");
                AssertEqual(3L, await db.BlackBoxNotes.CountByEntryAsync("bbx_n").ConfigureAwait(false));
                AssertTrue(await db.BlackBoxNotes.DeleteAsync(b.Id).ConfigureAwait(false), "deleted");
                AssertFalse(await db.BlackBoxNotes.DeleteAsync(b.Id).ConfigureAwait(false), "second delete");
                AssertEqual(2, await db.BlackBoxNotes.DeleteByEntryAsync("bbx_n").ConfigureAwait(false), "delete by entry");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box links and notes", cases: cases);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }
    }
}
