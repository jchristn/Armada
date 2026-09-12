namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="MemoryService"/>: upsert-by-key consolidation (update in place instead of
    /// duplicating), salience-first recall ordering, per-user scope isolation, and edit-permission
    /// enforcement. Positive cases assert the consolidation and ordering; negative cases assert cross-user
    /// invisibility and a forbidden delete.
    /// </summary>
    public sealed class MemoryServiceSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the MemoryService suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("upsert_by_key_updates_in_place", "UpsertAsync updates an existing memory with the same key instead of duplicating", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                MemoryService service = new MemoryService(testDb.Driver, CreateLogging());
                AuthContext user = User("ten_up", "usr_up");

                Memory first = new Memory { Type = MemoryTypeEnum.Semantic, Key = "style/no-var", Content = "dislikes var" };
                await service.UpsertAsync(user, first).ConfigureAwait(false);

                Memory second = new Memory { Type = MemoryTypeEnum.Semantic, Key = "style/no-var", Content = "strongly dislikes var and tuples" };
                Memory saved = await service.UpsertAsync(user, second).ConfigureAwait(false);

                EnumerationResult<Memory> all = await service.EnumerateAsync(user, new EnumerationQuery()).ConfigureAwait(false);
                AssertEqual(1, (int)all.TotalRecords);
                AssertEqual("strongly dislikes var and tuples", all.Objects[0].Content);
                AssertEqual(2, saved.Version);
            }));

            cases.Add(CaseAsync("search_orders_by_salience", "EnumerateAsync orders by salience (highest first)", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                MemoryService service = new MemoryService(testDb.Driver, CreateLogging());
                AuthContext user = User("ten_sal", "usr_sal");

                await service.CreateAsync(user, new Memory { Content = "minor", Salience = 0.2 }).ConfigureAwait(false);
                await service.CreateAsync(user, new Memory { Content = "load-bearing", Salience = 0.9 }).ConfigureAwait(false);

                EnumerationResult<Memory> result = await service.EnumerateAsync(user, new EnumerationQuery()).ConfigureAwait(false);
                AssertEqual(2, (int)result.TotalRecords);
                AssertEqual("load-bearing", result.Objects[0].Content);
            }));

            cases.Add(CaseAsync("search_filters_by_type", "EnumerateAsync filters by memory type", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                MemoryService service = new MemoryService(testDb.Driver, CreateLogging());
                AuthContext user = User("ten_ty", "usr_ty");

                await service.CreateAsync(user, new Memory { Type = MemoryTypeEnum.Episodic, Content = "did a thing" }).ConfigureAwait(false);
                await service.CreateAsync(user, new Memory { Type = MemoryTypeEnum.Procedural, Content = "how to do a thing" }).ConfigureAwait(false);

                EnumerationResult<Memory> procedural = await service.EnumerateAsync(user, new EnumerationQuery(), null, MemoryTypeEnum.Procedural).ConfigureAwait(false);
                AssertEqual(1, (int)procedural.TotalRecords);
                AssertEqual(MemoryTypeEnum.Procedural, procedural.Objects[0].Type);
            }));

            cases.Add(CaseAsync("user_specific_memory_hidden_from_other_user", "A user-specific memory is invisible to another user in the same tenant", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                MemoryService service = new MemoryService(testDb.Driver, CreateLogging());
                AuthContext userA = User("ten_iso", "usr_a");
                AuthContext userB = User("ten_iso", "usr_b");

                Memory created = await service.CreateAsync(userA, new Memory { Content = "A's private note" }).ConfigureAwait(false);
                AssertEqual(ScopeEnum.UserSpecific, created.Scope);

                EnumerationResult<Memory> asB = await service.EnumerateAsync(userB, new EnumerationQuery()).ConfigureAwait(false);
                AssertEqual(0, (int)asB.TotalRecords);

                EnumerationResult<Memory> asA = await service.EnumerateAsync(userA, new EnumerationQuery()).ConfigureAwait(false);
                AssertEqual(1, (int)asA.TotalRecords);
            }));

            cases.Add(CaseAsync("delete_forbidden_across_users", "A user cannot delete another user's user-specific memory", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                MemoryService service = new MemoryService(testDb.Driver, CreateLogging());
                AuthContext userA = User("ten_del", "usr_a");
                AuthContext userB = User("ten_del", "usr_b");

                Memory created = await service.CreateAsync(userA, new Memory { Content = "A's note" }).ConfigureAwait(false);

                // B is not permitted to delete A's user-specific memory.
                await AssertThrowsAsync<UnauthorizedAccessException>(() => service.DeleteAsync(userB, created.Id));
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.MemoryService",
                displayName: "Memory Service",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static AuthContext User(string tenantId, string userId)
        {
            return new AuthContext
            {
                IsAuthenticated = true,
                TenantId = tenantId,
                UserId = userId,
                IsAdmin = false,
                IsTenantAdmin = false
            };
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.MemoryService",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
