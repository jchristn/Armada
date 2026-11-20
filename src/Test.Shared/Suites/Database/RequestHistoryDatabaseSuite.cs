namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for request-history database operations: create/read round-tripping of an entry
    /// together with its detail row, filtered enumeration by route and success state, and retention
    /// pruning via DeleteByFilterAsync using a date cutoff. Positive cases cover persistence and the
    /// query/prune happy paths; negative audit cases cover a not-found read returning null and a
    /// filter that matches nothing deleting zero rows. One case checks that a created time with no
    /// kind keeps its UTC clock on every provider.
    /// </summary>
    public sealed class RequestHistoryDatabaseSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.RequestHistoryDatabase";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the RequestHistory Database suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("create_and_read_persist_entry_detail", "CreateAsync and ReadAsync persist entry detail", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;

                    RequestHistoryEntry entry = BuildEntry("GET", "/api/v1/fleets", 200, "usr_one");
                    RequestHistoryDetail detail = BuildDetail(entry.Id, "{\"route\":\"/api/v1/fleets\"}");

                    await db.RequestHistory.CreateAsync(entry, detail).ConfigureAwait(false);
                    RequestHistoryRecord? result = await db.RequestHistory.ReadAsync(entry.Id).ConfigureAwait(false);

                    AssertNotNull(result);
                    AssertEqual(entry.Id, result!.Entry.Id);
                    AssertEqual("/api/v1/fleets", result.Entry.Route);
                    AssertEqual(detail.RequestBodyText, result.Detail!.RequestBodyText);
                }
            }));

            cases.Add(CaseAsync("enumerate_filters_by_route_and_success_state", "EnumerateAsync filters by route and success state", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;

                    RequestHistoryEntry successEntry = BuildEntry("GET", "/api/v1/fleets", 200, "usr_one");
                    RequestHistoryEntry failureEntry = BuildEntry("POST", "/api/v1/missions", 500, "usr_two");
                    await db.RequestHistory.CreateAsync(successEntry, BuildDetail(successEntry.Id, "{\"ok\":true}")).ConfigureAwait(false);
                    await db.RequestHistory.CreateAsync(failureEntry, BuildDetail(failureEntry.Id, "{\"ok\":false}")).ConfigureAwait(false);

                    EnumerationResult<RequestHistoryEntry> routeFiltered = await db.RequestHistory.EnumerateAsync(new RequestHistoryQuery
                    {
                        Route = "/api/v1/missions",
                        PageNumber = 1,
                        PageSize = 25
                    }).ConfigureAwait(false);
                    AssertEqual(1, routeFiltered.Objects.Count);
                    AssertEqual(failureEntry.Id, routeFiltered.Objects[0].Id);

                    EnumerationResult<RequestHistoryEntry> successFiltered = await db.RequestHistory.EnumerateAsync(new RequestHistoryQuery
                    {
                        IsSuccess = true,
                        PageNumber = 1,
                        PageSize = 25
                    }).ConfigureAwait(false);
                    AssertEqual(1, successFiltered.Objects.Count);
                    AssertEqual(successEntry.Id, successFiltered.Objects[0].Id);
                }
            }));

            cases.Add(CaseAsync("delete_by_filter_removes_matching_rows", "DeleteByFilterAsync removes matching request-history rows", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;

                    RequestHistoryEntry oldEntry = BuildEntry("GET", "/api/v1/status", 200, "usr_old");
                    oldEntry.CreatedUtc = DateTime.UtcNow.AddDays(-10);
                    RequestHistoryEntry newEntry = BuildEntry("GET", "/api/v1/status", 200, "usr_new");
                    newEntry.CreatedUtc = DateTime.UtcNow;

                    await db.RequestHistory.CreateAsync(oldEntry, BuildDetail(oldEntry.Id, "{\"age\":\"old\"}")).ConfigureAwait(false);
                    await db.RequestHistory.CreateAsync(newEntry, BuildDetail(newEntry.Id, "{\"age\":\"new\"}")).ConfigureAwait(false);

                    int deleted = await db.RequestHistory.DeleteByFilterAsync(new RequestHistoryQuery
                    {
                        ToUtc = DateTime.UtcNow.AddDays(-5)
                    }).ConfigureAwait(false);

                    AssertEqual(1, deleted);
                    AssertNull(await db.RequestHistory.ReadAsync(oldEntry.Id).ConfigureAwait(false));
                    AssertNotNull(await db.RequestHistory.ReadAsync(newEntry.Id).ConfigureAwait(false));
                }
            }));

            cases.Add(CaseAsync("read_non_existent_returns_null_audit", "ReadAsync NonExistent ReturnsNull", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    RequestHistoryRecord? result = await db.RequestHistory.ReadAsync("req_nonexistent").ConfigureAwait(false);
                    AssertNull(result);
                }
            }));

            cases.Add(CaseAsync("delete_by_filter_no_match_deletes_nothing_audit", "DeleteByFilterAsync NoMatch DeletesNothing", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;

                    RequestHistoryEntry entry = BuildEntry("GET", "/api/v1/fleets", 200, "usr_keep");
                    await db.RequestHistory.CreateAsync(entry, BuildDetail(entry.Id, "{\"keep\":true}")).ConfigureAwait(false);

                    int deleted = await db.RequestHistory.DeleteByFilterAsync(new RequestHistoryQuery
                    {
                        Route = "/does/not/match"
                    }).ConfigureAwait(false);

                    AssertEqual(0, deleted);
                    AssertNotNull(await db.RequestHistory.ReadAsync(entry.Id).ConfigureAwait(false));
                }
            }));

            cases.Add(new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: "unspecified_created_utc_keeps_utc_clock",
                displayName: "A created time with no kind keeps its UTC clock",
                executeAsync: (CancellationToken ct) => UnspecifiedCreatedUtcKeepsUtcClockAsync(),
                tags: new List<string> { TestTags.Positive },
                skip: OperatingSystem.IsWindows(),
                skipReason: OperatingSystem.IsWindows() ? "The TZ variable does not change the .NET local zone on Windows." : null));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "RequestHistory Database",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static RequestHistoryEntry BuildEntry(string method, string route, int statusCode, string userId)
        {
            return new RequestHistoryEntry
            {
                TenantId = "ten_request_history",
                UserId = userId,
                PrincipalDisplay = userId + "@armada",
                AuthMethod = "Session",
                Method = method,
                Route = route,
                RouteTemplate = route,
                QueryString = null,
                StatusCode = statusCode,
                DurationMs = 12.34,
                RequestSizeBytes = 42,
                ResponseSizeBytes = 84,
                RequestContentType = "application/json",
                ResponseContentType = "application/json",
                IsSuccess = statusCode >= 200 && statusCode < 400,
                CreatedUtc = DateTime.UtcNow
            };
        }

        private static RequestHistoryDetail BuildDetail(string requestHistoryId, string requestBody)
        {
            return new RequestHistoryDetail
            {
                RequestHistoryId = requestHistoryId,
                RequestHeadersJson = "{\"X-Test\":\"true\"}",
                ResponseHeadersJson = "{\"Content-Type\":\"application/json\"}",
                RequestBodyText = requestBody,
                ResponseBodyText = "{\"status\":\"ok\"}"
            };
        }

        private static async Task UnspecifiedCreatedUtcKeepsUtcClockAsync()
        {
            using (LocalTimeZoneScope zone = await LocalTimeZoneScope.EnterAsync().ConfigureAwait(false))
            {
                DateTime wall = new DateTime(2020, 6, 15, 15, 0, 0);
                DateTime unspecified = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
                DateTime utc = DateTime.SpecifyKind(wall, DateTimeKind.Utc);
                DateTime local = new DateTime(2020, 6, 15, 15, 0, 0, DateTimeKind.Local);
                DateTime localUtc = local.ToUniversalTime();

                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver db = testDb.Driver;
                    DateTime utcRead = await ReadCreatedUtcAsync(db, utc, "usr_clock_utc").ConfigureAwait(false);
                    AssertEqual(DateTimeKind.Utc, utcRead.Kind, "utc kind");
                    AssertEqual(utc.Ticks, utcRead.Ticks, "utc ticks");

                    DateTime localRead = await ReadCreatedUtcAsync(db, local, "usr_clock_local").ConfigureAwait(false);
                    AssertEqual(DateTimeKind.Utc, localRead.Kind, "local kind");
                    AssertEqual(localUtc.Ticks, localRead.Ticks, "local ticks");

                    DateTime unspecifiedRead = await ReadCreatedUtcAsync(db, unspecified, "usr_clock_unspecified").ConfigureAwait(false);
                    AssertEqual(DateTimeKind.Utc, unspecifiedRead.Kind, "unspecified kind");
                    AssertEqual(utc.Ticks, unspecifiedRead.Ticks, "unspecified ticks");
                }
            }
        }

        private static async Task<DateTime> ReadCreatedUtcAsync(DatabaseDriver db, DateTime createdUtc, string userId)
        {
            RequestHistoryEntry entry = BuildEntry("GET", "/api/v1/history-clock", 200, userId);
            entry.CreatedUtc = createdUtc;
            await db.RequestHistory.CreateAsync(entry, BuildDetail(entry.Id, "{\"clock\":true}")).ConfigureAwait(false);
            RequestHistoryRecord? result = await db.RequestHistory.ReadAsync(entry.Id).ConfigureAwait(false);
            AssertNotNull(result);
            return result!.Entry.CreatedUtc;
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

        #endregion
    }
}
