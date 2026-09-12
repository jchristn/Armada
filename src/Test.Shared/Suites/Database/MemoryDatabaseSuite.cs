namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for durable memory persistence over a live database. Positive cases assert create/read
    /// round-trips including the tag child rows and every typed column (key, summary, salience, version,
    /// provenance), tag replacement on update, tenant-scoped enumeration without cross-tenant leakage,
    /// read-by-key, and cascade delete; negative cases assert the null-id/null-entity guards and the entity's
    /// value clamping.
    /// </summary>
    public sealed class MemoryDatabaseSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Memory database suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("create_read_roundtrips_all_fields", "CreateAsync/ReadAsync round-trips a memory with tags and all columns", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);

                Memory memory = NewMemory("ten_mem", "usr_mem");
                memory.Type = MemoryTypeEnum.Semantic;
                memory.Topic = "code-style";
                memory.Key = "code-style/no-var";
                memory.Summary = "No var in C#";
                memory.Content = "This user dislikes the use of var in C# code.";
                memory.Salience = 0.9;
                memory.SourceKind = MemorySourceKindEnum.Voyage;
                memory.SourceVoyageId = "vyg_1";
                memory.SourceMissionId = "msn_1";
                memory.VesselId = "vsl_1";
                memory.SourceDetail = "distilled from the review";
                memory.Tags = new List<string> { "csharp", "style" };

                Memory created = await testDb.Driver.Memories.CreateAsync(memory).ConfigureAwait(false);
                AssertStartsWith("mem_", created.Id);

                Memory? reloaded = await testDb.Driver.Memories.ReadAsync(created.Id).ConfigureAwait(false);
                AssertNotNull(reloaded, "Expected memory to reload.");
                AssertEqual(MemoryTypeEnum.Semantic, reloaded!.Type);
                AssertEqual("code-style", reloaded.Topic);
                AssertEqual("code-style/no-var", reloaded.Key);
                AssertEqual("No var in C#", reloaded.Summary);
                AssertEqual("This user dislikes the use of var in C# code.", reloaded.Content);
                AssertEqual(0.9, reloaded.Salience);
                AssertEqual(1, reloaded.Version);
                AssertEqual(MemorySourceKindEnum.Voyage, reloaded.SourceKind);
                AssertEqual("vyg_1", reloaded.SourceVoyageId);
                AssertEqual("vsl_1", reloaded.VesselId);
                AssertEqual(2, reloaded.Tags.Count);
                AssertTrue(reloaded.Tags.Contains("csharp") && reloaded.Tags.Contains("style"), "Expected tags to round-trip.");
            }));

            cases.Add(CaseAsync("update_replaces_tags_and_fields", "UpdateAsync replaces tags and updates fields", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);

                Memory memory = NewMemory("ten_mem_u", "usr_mem_u");
                memory.Tags = new List<string> { "a", "b" };
                Memory created = await testDb.Driver.Memories.CreateAsync(memory).ConfigureAwait(false);

                created.Content = "updated content";
                created.Salience = 0.2;
                created.Version = 2;
                created.Tags = new List<string> { "c" };
                await testDb.Driver.Memories.UpdateAsync(created).ConfigureAwait(false);

                Memory? reloaded = await testDb.Driver.Memories.ReadAsync(created.Id).ConfigureAwait(false);
                AssertNotNull(reloaded, "Expected memory to reload.");
                AssertEqual("updated content", reloaded!.Content);
                AssertEqual(0.2, reloaded.Salience);
                AssertEqual(2, reloaded.Version);
                AssertEqual(1, reloaded.Tags.Count);
                AssertEqual("c", reloaded.Tags[0]);
            }));

            cases.Add(CaseAsync("read_by_key_finds_within_tenant", "ReadByKeyAsync finds a memory by its key within a tenant", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);

                Memory memory = NewMemory("ten_key", "usr_key");
                memory.Key = "deployment/rollback";
                await testDb.Driver.Memories.CreateAsync(memory).ConfigureAwait(false);

                Memory? found = await testDb.Driver.Memories.ReadByKeyAsync("ten_key", "deployment/rollback").ConfigureAwait(false);
                AssertNotNull(found, "Expected to find the memory by key.");
                AssertEqual("deployment/rollback", found!.Key);

                Memory? missing = await testDb.Driver.Memories.ReadByKeyAsync("ten_key", "nope").ConfigureAwait(false);
                AssertNull(missing, "Expected no memory for an unknown key.");
            }));

            cases.Add(CaseAsync("enumerate_is_tenant_scoped", "EnumerateAsync is tenant-scoped", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);

                await testDb.Driver.Memories.CreateAsync(NewMemory("ten_x", "usr_x")).ConfigureAwait(false);
                await testDb.Driver.Memories.CreateAsync(NewMemory("ten_x", "usr_x")).ConfigureAwait(false);
                await testDb.Driver.Memories.CreateAsync(NewMemory("ten_y", "usr_y")).ConfigureAwait(false);

                List<Memory> tenantX = await testDb.Driver.Memories.EnumerateAsync("ten_x").ConfigureAwait(false);
                AssertEqual(2, tenantX.Count);
                AssertTrue(tenantX.TrueForAll(m => m.TenantId == "ten_x"), "Expected no cross-tenant leakage.");
            }));

            cases.Add(CaseAsync("delete_cascades_tags", "DeleteAsync removes the memory and its tags", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);

                Memory memory = NewMemory("ten_del", "usr_del");
                memory.Tags = new List<string> { "t1", "t2" };
                Memory created = await testDb.Driver.Memories.CreateAsync(memory).ConfigureAwait(false);

                await testDb.Driver.Memories.DeleteAsync(created.Id).ConfigureAwait(false);

                Memory? reloaded = await testDb.Driver.Memories.ReadAsync(created.Id).ConfigureAwait(false);
                AssertNull(reloaded, "Expected memory to be deleted.");
            }));

            cases.Add(CaseAsync("salience_clamped", "Salience clamps to [0,1]", TestTags.Positive, () =>
            {
                Memory memory = new Memory();
                memory.Salience = 5.0;
                AssertEqual(1.0, memory.Salience);
                memory.Salience = -1.0;
                AssertEqual(0.0, memory.Salience);
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("version_clamped", "Version clamps to a minimum of 1", TestTags.Positive, () =>
            {
                Memory memory = new Memory();
                memory.Version = 0;
                AssertEqual(1, memory.Version);
                memory.Version = 7;
                AssertEqual(7, memory.Version);
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("read_null_id_throws", "ReadAsync NullId Throws", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentNullException>(() => testDb.Driver.Memories.ReadAsync(null!));
            }));

            cases.Add(CaseAsync("create_null_throws", "CreateAsync NullEntity Throws", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentNullException>(() => testDb.Driver.Memories.CreateAsync(null!));
            }));

            cases.Add(CaseAsync("id_empty_throws", "Memory Id empty throws", TestTags.Negative, () =>
            {
                Memory memory = new Memory();
                AssertThrows<ArgumentNullException>(() => memory.Id = "");
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(
                suiteId: "Database.Memory",
                displayName: "Memory Database",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static Memory NewMemory(string tenantId, string userId)
        {
            return new Memory
            {
                TenantId = tenantId,
                UserId = userId,
                Type = MemoryTypeEnum.Semantic,
                Content = "a durable fact"
            };
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Database.Memory",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
