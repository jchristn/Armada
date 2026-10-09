namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Microsoft.Data.Sqlite;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The SQLite write gate: every write the provider makes queues on one first-in, first-out gate per database file,
    /// so a queued write cannot be starved by other writers (CI saw one CLI permission insert wait 17 s behind
    /// request-history inserts that bypassed the old lock). Covers grant order, cancellation, nesting, sharing across
    /// drivers, the per-connection settings, the commit audit that refuses unguarded writes, and two end-to-end
    /// reproductions through the driver: request-history writers queued after a CLI permission insert cannot commit
    /// before it, and under a continuous request-history burst a CLI permission insert is overtaken by at most one
    /// write per concurrent writer. SQLite only; the cases pass trivially on server providers.
    /// </summary>
    public sealed class SqliteWriteGateSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.SqliteWriteGate";
        private static readonly TimeSpan _Guard = TimeSpan.FromSeconds(60);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the SQLite write gate suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("grants_in_request_order", "Writers queued on the gate are granted it strictly in the order they asked", TestTags.Positive, async () =>
            {
                if (!TestDatabaseConfig.IsSqlite) return;
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                SqliteWriteGate gate = Sqlite(testDb).WriteGate;

                SqliteWriteLease holder = await HoldAsync(gate).ConfigureAwait(false);
                ConcurrentQueue<int> order = new ConcurrentQueue<int>();
                List<Task> writers = new List<Task>();
                const int count = 16;
                for (int i = 0; i < count; i++) writers.Add(RecordGrantAsync(gate, i, order));

                AssertEqual(count, gate.QueueLength, "every writer is queued behind the holder");
                holder.Dispose();
                await Task.WhenAll(writers).WaitAsync(_Guard).ConfigureAwait(false);

                AssertEqual(String.Join(",", Enumerable.Range(0, count)), String.Join(",", order), "grant order is request order");
                AssertFalse(gate.IsHeld, "released after the last writer");
                AssertEqual(0, gate.QueueLength);
            }));

            cases.Add(CaseAsync("cancelled_waiter_leaves_the_queue", "A cancelled waiter leaves the queue and the writers behind it are still granted in order", TestTags.Negative, async () =>
            {
                if (!TestDatabaseConfig.IsSqlite) return;
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                SqliteWriteGate gate = Sqlite(testDb).WriteGate;

                SqliteWriteLease holder = await HoldAsync(gate).ConfigureAwait(false);
                ConcurrentQueue<int> order = new ConcurrentQueue<int>();
                using CancellationTokenSource cts = new CancellationTokenSource();
                Task first = RecordGrantAsync(gate, 1, order);
                Task cancelled = RecordGrantAsync(gate, 2, order, cts.Token);
                Task third = RecordGrantAsync(gate, 3, order);
                AssertEqual(3, gate.QueueLength);

                cts.Cancel();
                bool threw = false;
                try { await cancelled.WaitAsync(_Guard).ConfigureAwait(false); }
                catch (OperationCanceledException) { threw = true; }
                AssertTrue(threw, "the cancelled waiter observes cancellation");
                AssertEqual(2, gate.QueueLength, "the cancelled waiter is out of the queue");

                holder.Dispose();
                await Task.WhenAll(first, third).WaitAsync(_Guard).ConfigureAwait(false);
                AssertEqual("1,3", String.Join(",", order));
                AssertFalse(gate.IsHeld);
            }));

            cases.Add(CaseAsync("nested_entry_throws_instead_of_deadlocking", "Entering the gate again from the flow that holds it throws instead of deadlocking", TestTags.Negative, async () =>
            {
                if (!TestDatabaseConfig.IsSqlite) return;
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                SqliteWriteGate gate = Sqlite(testDb).WriteGate;

                using (SqliteWriteLease lease = await gate.EnterAsync().ConfigureAwait(false))
                {
                    AssertTrue(gate.IsHeldByCurrentFlow);
                    bool threw = false;
                    try { await gate.EnterAsync().ConfigureAwait(false); }
                    catch (InvalidOperationException) { threw = true; }
                    AssertTrue(threw, "nested entry throws");
                }

                AssertFalse(gate.IsHeldByCurrentFlow, "released");
                using (SqliteWriteLease again = await gate.EnterAsync().ConfigureAwait(false))
                {
                    AssertTrue(again.IsActive, "the gate can be entered again after release");
                }
            }));

            cases.Add(CaseAsync("drivers_on_one_file_share_one_gate", "Every driver and connection string naming the same file shares one gate", TestTags.Positive, async () =>
            {
                if (!TestDatabaseConfig.IsSqlite) return;
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                SqliteDatabaseDriver first = Sqlite(testDb);
                SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder(testDb.ConnectionString);

                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                using SqliteDatabaseDriver second = new SqliteDatabaseDriver("Data Source=" + builder.DataSource + ";Pooling=False;Default Timeout=45", logging);

                AssertTrue(ReferenceEquals(first.WriteGate, second.WriteGate), "two drivers on one file share the gate");
                AssertTrue(ReferenceEquals(first.WriteGate, SqliteWriteGate.ForConnectionString("Data Source=" + builder.DataSource)), "a bare connection string shares it too");
                AssertFalse(ReferenceEquals(first.WriteGate, SqliteWriteGate.ForConnectionString("Data Source=" + builder.DataSource + ".other")), "another file has its own gate");
            }));

            cases.Add(CaseAsync("provider_connections_use_wal_busy_timeout_and_normal_sync", "Provider connections run in WAL mode with a busy timeout and synchronous=NORMAL", TestTags.Positive, async () =>
            {
                if (!TestDatabaseConfig.IsSqlite) return;
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);

                using SqliteConnection conn = new SqliteProviderConnection(testDb.ConnectionString);
                await conn.OpenAsync().ConfigureAwait(false);
                AssertEqual("wal", Convert.ToString(await ScalarAsync(conn, "PRAGMA journal_mode;").ConfigureAwait(false))!.ToLowerInvariant());
                AssertEqual((long)SqliteProviderConnection.BusyTimeoutMs, Convert.ToInt64(await ScalarAsync(conn, "PRAGMA busy_timeout;").ConfigureAwait(false)));
                AssertEqual(1L, Convert.ToInt64(await ScalarAsync(conn, "PRAGMA synchronous;").ConfigureAwait(false)), "synchronous=NORMAL");
            }));

            cases.Add(CaseAsync("unguarded_write_is_refused_under_strict_audit", "A write committed without the gate on a provider connection is counted and, under the test runner's strict audit, refused", TestTags.Negative, async () =>
            {
                if (!TestDatabaseConfig.IsSqlite) return;
                AssertTrue(SqliteWriteGate.StrictAudit, "the test runners turn the strict audit on");
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                SqliteDatabaseDriver driver = Sqlite(testDb);
                long before = driver.WriteGate.UnguardedCommitCount;

                using (SqliteConnection conn = new SqliteProviderConnection(testDb.ConnectionString))
                {
                    await conn.OpenAsync().ConfigureAwait(false);
                    bool refused = false;
                    try
                    {
                        await ExecuteAsync(conn, "CREATE TABLE unguarded_probe (id INTEGER);").ConfigureAwait(false);
                    }
                    catch (SqliteException)
                    {
                        refused = true;
                    }

                    AssertTrue(refused, "the unguarded commit is refused");
                    AssertEqual(before + 1, driver.WriteGate.UnguardedCommitCount, "and counted");
                    AssertNotNull(driver.WriteGate.LastUnguardedCommitStack, "with the stack of the offending write");
                    AssertEqual(0L, Convert.ToInt64(await ScalarAsync(conn, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'unguarded_probe';").ConfigureAwait(false)), "nothing was written");
                }

                CliPermissionRequest request = NewRequest("guarded");
                await driver.CliPermissionRequests.CreateAsync(request).ConfigureAwait(false);
                AssertNotNull(await driver.CliPermissionRequests.ReadAsync(request.Id).ConfigureAwait(false), "a provider write goes through");
                AssertEqual(before + 1, driver.WriteGate.UnguardedCommitCount, "provider writes are not counted as unguarded");
            }));

            cases.Add(CaseAsync("queued_write_is_not_overtaken_by_request_history", "Request-history inserts started after a queued CLI permission insert cannot commit before it", TestTags.Positive, async () =>
            {
                if (!TestDatabaseConfig.IsSqlite) return;
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                SqliteDatabaseDriver driver = Sqlite(testDb);
                SqliteWriteGate gate = driver.WriteGate;

                // A long writer holds the gate. The CLI permission insert queues first, then a burst of
                // request-history inserts (the writers that bypassed the old lock) arrives.
                SqliteWriteLease holder = await HoldAsync(gate).ConfigureAwait(false);
                CliPermissionRequest request = NewRequest("queued");
                Task<CliPermissionRequest> target = driver.CliPermissionRequests.CreateAsync(request);
                await gate.WhenQueuedAsync(1).WaitAsync(_Guard).ConfigureAwait(false);

                const int burst = 12;
                List<Task<RequestHistoryRecord>> history = new List<Task<RequestHistoryRecord>>();
                for (int i = 0; i < burst; i++) history.Add(driver.RequestHistory.CreateAsync(NewHistoryEntry(i), null));

                // The request-history flusher reaches the gate and queues behind the CLI permission insert. With the
                // old lock-free inserts the burst committed here, while the queued insert waited.
                await gate.WhenQueuedAsync(2).WaitAsync(_Guard).ConfigureAwait(false);
                AssertFalse(history.Any(t => t.IsCompleted), "no request-history insert commits while a queued writer waits");
                AssertFalse(target.IsCompleted, "the CLI permission insert waits for the holder");
                AssertEqual(0L, await CountAsync(testDb, "SELECT COUNT(*) FROM request_history;").ConfigureAwait(false), "nothing committed behind the holder's back");

                long grantsBefore = gate.GrantCount;
                holder.Dispose();
                await target.WaitAsync(_Guard).ConfigureAwait(false);
                long requestRowsAfterTarget = await CountAsync(testDb, "SELECT COUNT(*) FROM cli_permission_requests WHERE id = '" + request.Id + "';").ConfigureAwait(false);
                AssertEqual(1L, requestRowsAfterTarget, "the CLI permission request is stored");

                await Task.WhenAll(history).WaitAsync(_Guard).ConfigureAwait(false);
                AssertEqual((long)burst, await CountAsync(testDb, "SELECT COUNT(*) FROM request_history;").ConfigureAwait(false), "every request-history row is stored");
                AssertTrue(gate.GrantCount - grantsBefore <= 1 + 1 + burst, "the burst was group-committed (" + (gate.GrantCount - grantsBefore) + " grants)");
            }));

            cases.Add(CaseAsync("request_history_burst_cannot_starve_a_write", "Under a continuous request-history burst a CLI permission insert is overtaken by at most one write per concurrent writer", TestTags.Positive, async () =>
            {
                if (!TestDatabaseConfig.IsSqlite) return;
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                SqliteDatabaseDriver driver = Sqlite(testDb);

                // Each request-history insert does a fixed amount of work while holding the database lock, standing
                // in for a slow commit (a WAL flush on a busy Windows runner).
                using (SqliteWriteLease setup = await driver.WriteGate.EnterAsync().ConfigureAwait(false))
                using (SqliteConnection conn = new SqliteProviderConnection(testDb.ConnectionString))
                {
                    await conn.OpenAsync().ConfigureAwait(false);
                    await ExecuteAsync(conn, "CREATE TRIGGER burn_request_history AFTER INSERT ON request_history BEGIN SELECT length(hex(randomblob(200000))); END;").ConfigureAwait(false);
                }

                SqliteStarvationProbe probe = new SqliteStarvationProbe(driver, 8, 10);
                SqliteStarvationResult result = await probe.RunAsync().WaitAsync(TimeSpan.FromMinutes(5)).ConfigureAwait(false);

                // FIFO bound: a write that asked for the gate after the CLI permission insert is granted after it, so
                // the only writes that can finish inside its window are ones already in the queue or batch ahead of
                // it: at most one per concurrent writer. The median discards a trial whose target thread was
                // descheduled between stamping its start and reaching the gate.
                AssertTrue(result.MedianOvertakes <= probe.Writers,
                    "median overtakes " + result.MedianOvertakes + " <= " + probe.Writers + " writers (" + result.Describe() + ")");

                // Latency bound from the work itself: a FIFO wait is at most the writes ahead of it, and each of those
                // is no longer than the slowest request-history insert observed in the same run.
                double bound = (probe.Writers + 1) * result.MaxWriterLatencyMs;
                AssertTrue(result.MaxTargetLatencyMs <= bound,
                    "slowest CLI permission insert " + result.MaxTargetLatencyMs.ToString("F1") + " ms <= " + bound.ToString("F1") + " ms (" + result.Describe() + ")");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "SQLite Write Gate",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static SqliteDatabaseDriver Sqlite(TestDatabase testDb)
        {
            SqliteDatabaseDriver? driver = testDb.Driver as SqliteDatabaseDriver;
            if (driver == null) throw new InvalidOperationException("SQLite driver expected");
            return driver;
        }

        /// <summary>
        /// Take the gate from a separate async method, so the caller's flow does not carry the lease.
        /// </summary>
        private static async Task<SqliteWriteLease> HoldAsync(SqliteWriteGate gate)
        {
            return await gate.EnterAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Queue on the gate (synchronously, before the first await) and record the grant.
        /// </summary>
        private static async Task RecordGrantAsync(SqliteWriteGate gate, int id, ConcurrentQueue<int> order, CancellationToken token = default)
        {
            using (SqliteWriteLease lease = await gate.EnterAsync(token).ConfigureAwait(false))
            {
                order.Enqueue(id);
            }
        }

        private static CliPermissionRequest NewRequest(string label)
        {
            CliPermissionRequest request = new CliPermissionRequest();
            request.TenantId = "ten_gate";
            request.ThreadId = "ath_gate_" + label;
            request.Runtime = AgentRuntimeEnum.ClaudeCode;
            request.ToolName = "Bash";
            request.InputText = "{\"command\":\"git status\"}";
            request.SummaryText = "git status (" + label + ")";
            return request;
        }

        internal static RequestHistoryEntry NewHistoryEntry(int index)
        {
            return new RequestHistoryEntry
            {
                TenantId = "ten_gate",
                Method = "GET",
                Route = "/api/v1/ask/threads/" + index,
                RouteTemplate = "/api/v1/ask/threads/{id}",
                StatusCode = 200,
                DurationMs = 1,
                IsSuccess = true,
                CreatedUtc = DateTime.UtcNow
            };
        }

        private static async Task<object?> ScalarAsync(SqliteConnection conn, string sql)
        {
            using (SqliteCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                return await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            }
        }

        private static async Task ExecuteAsync(SqliteConnection conn, string sql)
        {
            using (SqliteCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        private static async Task<long> CountAsync(TestDatabase testDb, string sql)
        {
            using (SqliteConnection conn = new SqliteConnection(testDb.ConnectionString))
            {
                await conn.OpenAsync().ConfigureAwait(false);
                return Convert.ToInt64(await ScalarAsync(conn, sql).ConfigureAwait(false));
            }
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
