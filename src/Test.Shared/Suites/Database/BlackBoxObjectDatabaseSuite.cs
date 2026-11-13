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
    /// Black box object persistence on every provider: BLOB round trips (empty, binary, 1 MB), ranged reads cut in SQL, upsert by entry and name, missing flags, paging and delete by entry.
    /// </summary>
    public sealed class BlackBoxObjectDatabaseSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Database.BlackBoxObjects";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("blob_roundtrip_empty_binary_and_1mb", "Empty, binary and 1 MB objects round-trip byte for byte", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = t.Driver;
                byte[] big = new byte[1024 * 1024];
                new Random(7).NextBytes(big);
                byte[] binary = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
                Dictionary<string, byte[]> cases2 = new Dictionary<string, byte[]> { { "empty.bin", Array.Empty<byte>() }, { "binary.bin", binary }, { "big.bin", big } };
                foreach (KeyValuePair<string, byte[]> kv in cases2)
                {
                    BlackBoxObject obj = await db.BlackBoxObjects.UpsertAsync(new BlackBoxObject { EntryId = "bbx_blob", TenantId = "ten_a", Name = kv.Key, Data = kv.Value, SizeBytes = kv.Value.Length, UncompressedBytes = kv.Value.Length, Sha256 = "x" }).ConfigureAwait(false);
                    BlackBoxObject read = (await db.BlackBoxObjects.ReadAsync("bbx_blob", kv.Key, true).ConfigureAwait(false))!;
                    AssertNotNull(read.Data, kv.Key + " data");
                    AssertEqual(kv.Value.Length, read.Data!.Length, kv.Key + " length");
                    AssertTrue(kv.Value.SequenceEqual(read.Data), kv.Key + " bytes");
                    AssertNull((await db.BlackBoxObjects.ReadAsync("bbx_blob", kv.Key, false).ConfigureAwait(false))!.Data, kv.Key + " data not read unless asked");
                    AssertEqual(obj.Id, (await db.BlackBoxObjects.ReadByIdAsync(obj.Id).ConfigureAwait(false))!.Id, "by id");
                }
            }));

            cases.Add(Case("ranged_reads", "Ranged reads return the slice, a short tail at the end, empty past the end, and null when the data is missing", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = t.Driver;
                byte[] data = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
                BlackBoxObject obj = await db.BlackBoxObjects.UpsertAsync(new BlackBoxObject { EntryId = "bbx_range", Name = "r.bin", Data = data }).ConfigureAwait(false);
                byte[] slice = (await db.BlackBoxObjects.ReadDataRangeAsync(obj.Id, 10, 5).ConfigureAwait(false))!;
                AssertEqual("10,11,12,13,14", String.Join(",", slice), "slice");
                AssertEqual(3, (await db.BlackBoxObjects.ReadDataRangeAsync(obj.Id, 97, 10).ConfigureAwait(false))!.Length, "short tail");
                AssertEqual(0, (await db.BlackBoxObjects.ReadDataRangeAsync(obj.Id, 500, 10).ConfigureAwait(false))!.Length, "past the end is empty");
                AssertNull(await db.BlackBoxObjects.ReadDataRangeAsync("bbo_missing", 0, 10).ConfigureAwait(false), "missing row");
                AssertTrue(await db.BlackBoxObjects.WriteDataAsync(obj.Id, null).ConfigureAwait(false), "data cleared");
                AssertNull(await db.BlackBoxObjects.ReadDataRangeAsync(obj.Id, 0, 10).ConfigureAwait(false), "cleared data reads as null");
                AssertTrue(await db.BlackBoxObjects.WriteDataAsync(obj.Id, new byte[] { 9, 8 }).ConfigureAwait(false), "data rewritten");
                AssertEqual("9,8", String.Join(",", (await db.BlackBoxObjects.ReadDataRangeAsync(obj.Id, 0, 10).ConfigureAwait(false))!), "rewritten");
                AssertFalse(await db.BlackBoxObjects.WriteDataAsync("bbo_missing", new byte[] { 1 }).ConfigureAwait(false), "missing row not written");
                await AssertThrowsAsync<ArgumentOutOfRangeException>(() => db.BlackBoxObjects.ReadDataRangeAsync(obj.Id, -1, 1), "negative offset").ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentOutOfRangeException>(() => db.BlackBoxObjects.ReadDataRangeAsync(obj.Id, 0, 0), "zero length").ConfigureAwait(false);
            }));

            cases.Add(Case("upsert_page_missing_delete", "Upsert replaces by entry and name; objects page by name; missing flag; delete by entry", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = t.Driver;
                await db.BlackBoxObjects.UpsertAsync(new BlackBoxObject { EntryId = "bbx_up", Name = "brief.md", ContentType = "text/markdown", Data = new byte[] { 1 } }).ConfigureAwait(false);
                BlackBoxObject replaced = await db.BlackBoxObjects.UpsertAsync(new BlackBoxObject { EntryId = "bbx_up", Name = "brief.md", ContentType = "text/markdown", ContentEncoding = "gzip", Data = new byte[] { 2, 2 }, StorageKey = "k/brief.md" }).ConfigureAwait(false);
                for (int i = 0; i < 4; i++) await db.BlackBoxObjects.UpsertAsync(new BlackBoxObject { EntryId = "bbx_up", Name = "evidence/check-" + i + ".txt" }).ConfigureAwait(false);
                await db.BlackBoxObjects.UpsertAsync(new BlackBoxObject { EntryId = "bbx_other", Name = "brief.md" }).ConfigureAwait(false);
                EnumerationResult<BlackBoxObject> page = await db.BlackBoxObjects.EnumerateAsync("bbx_up", new EnumerationQuery { PageSize = 2 }).ConfigureAwait(false);
                AssertEqual(5L, page.TotalRecords, "replace kept one row per name");
                AssertEqual("brief.md,evidence/check-0.txt", String.Join(",", page.Objects.Select(o => o.Name)), "ordered by name");
                AssertNull(page.Objects[0].Data, "lists never carry data");
                BlackBoxObject read = (await db.BlackBoxObjects.ReadAsync("bbx_up", "brief.md", true).ConfigureAwait(false))!;
                AssertEqual(replaced.Id, read.Id); AssertEqual("gzip", read.ContentEncoding); AssertEqual("k/brief.md", read.StorageKey); AssertEqual(2, read.Data!.Length);
                AssertFalse(read.IsMissing, "not missing");
                await db.BlackBoxObjects.SetMissingAsync(read.Id, true).ConfigureAwait(false);
                AssertTrue((await db.BlackBoxObjects.ReadByIdAsync(read.Id).ConfigureAwait(false))!.IsMissing, "missing");
                AssertEqual(5, await db.BlackBoxObjects.DeleteByEntryAsync("bbx_up").ConfigureAwait(false), "deleted rows");
                AssertNotNull(await db.BlackBoxObjects.ReadAsync("bbx_other", "brief.md").ConfigureAwait(false), "other entry kept");
                AssertNull(await db.BlackBoxObjects.ReadAsync("bbx_up", "nope").ConfigureAwait(false), "missing name");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box object persistence", cases: cases);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }
    }
}
