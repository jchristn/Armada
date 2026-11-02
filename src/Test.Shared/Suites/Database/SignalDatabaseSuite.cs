namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the signal database methods: create, read, recipient-filtered enumeration
    /// (unread-only and all), recent enumeration with a limit, and marking a signal read. Includes
    /// a negative case for reading a missing signal. Reads also return a stored user id and keep a
    /// null user id null. A user-scoped read returns only that user's rows. Each case runs against
    /// its own fresh store.
    /// </summary>
    public sealed class SignalDatabaseSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.SignalDatabase";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Signal Database suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("create_async_returns_signal", "CreateAsync returns signal", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Signal signal = new Signal(SignalTypeEnum.Assignment, "{\"test\":true}");
                    Signal result = await db.Signals.CreateAsync(signal);

                    AssertNotNull(result);
                    AssertEqual(SignalTypeEnum.Assignment, result.Type);
                }
            }));

            cases.Add(CaseAsync("read_async_returns_created_signal", "ReadAsync returns created signal", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Signal signal = new Signal(SignalTypeEnum.Heartbeat);
                    await db.Signals.CreateAsync(signal);

                    Signal? result = await db.Signals.ReadAsync(signal.Id);
                    AssertNotNull(result);
                    AssertEqual(signal.Id, result!.Id);
                    AssertFalse(result.Read);
                }
            }));

            cases.Add(CaseAsync("enumerate_by_recipient_async_filters_correctly", "EnumerateByRecipientAsync filters correctly", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("receiver");
                    await db.Captains.CreateAsync(captain);

                    Signal s1 = new Signal(SignalTypeEnum.Nudge, "msg1");
                    s1.ToCaptainId = captain.Id;
                    Signal s2 = new Signal(SignalTypeEnum.Nudge, "msg2");
                    s2.ToCaptainId = captain.Id;
                    s2.Read = true;
                    Signal s3 = new Signal(SignalTypeEnum.Nudge, "msg3");

                    await db.Signals.CreateAsync(s1);
                    await db.Signals.CreateAsync(s2);
                    await db.Signals.CreateAsync(s3);

                    List<Signal> unread = await db.Signals.EnumerateByRecipientAsync(captain.Id, unreadOnly: true);
                    AssertEqual(1, unread.Count);

                    List<Signal> all = await db.Signals.EnumerateByRecipientAsync(captain.Id, unreadOnly: false);
                    AssertEqual(2, all.Count);
                }
            }));

            cases.Add(CaseAsync("enumerate_recent_async_returns_limited", "EnumerateRecentAsync returns limited", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    for (int i = 0; i < 5; i++)
                    {
                        await db.Signals.CreateAsync(new Signal(SignalTypeEnum.Nudge, "msg" + i));
                    }

                    List<Signal> recent = await db.Signals.EnumerateRecentAsync(3);
                    AssertEqual(3, recent.Count);
                }
            }));

            cases.Add(CaseAsync("mark_read_async_sets_read_flag", "MarkReadAsync sets read flag", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Signal signal = new Signal(SignalTypeEnum.Mail, "test");
                    await db.Signals.CreateAsync(signal);
                    AssertFalse(signal.Read);

                    await db.Signals.MarkReadAsync(signal.Id);

                    Signal? result = await db.Signals.ReadAsync(signal.Id);
                    AssertTrue(result!.Read);
                }
            }));

            cases.Add(CaseAsync("read_async_returns_stored_user_id", "ReadAsync returns stored user id", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    UserMaster owner = await db.Users.CreateAsync(new UserMaster(Constants.DefaultTenantId, "owner-a@example.com", "password"));
                    UserMaster other = await db.Users.CreateAsync(new UserMaster(Constants.DefaultTenantId, "owner-b@example.com", "password"));

                    Signal owned = new Signal(SignalTypeEnum.Mail, "{\"kind\":\"owner\"}");
                    owned.TenantId = Constants.DefaultTenantId;
                    owned.UserId = owner.Id;
                    await db.Signals.CreateAsync(owned);

                    Signal otherOwned = new Signal(SignalTypeEnum.Mail, "{\"kind\":\"other\"}");
                    otherOwned.TenantId = Constants.DefaultTenantId;
                    otherOwned.UserId = other.Id;
                    await db.Signals.CreateAsync(otherOwned);

                    Signal? read = await db.Signals.ReadAsync(owned.Id);
                    AssertNotNull(read);
                    AssertEqual(owner.Id, read!.UserId);

                    Signal? scoped = await db.Signals.ReadAsync(Constants.DefaultTenantId, owner.Id, owned.Id);
                    AssertNotNull(scoped);
                    AssertEqual(owner.Id, scoped!.UserId);

                    List<Signal> ownerRows = await db.Signals.EnumerateAsync(Constants.DefaultTenantId, owner.Id);
                    AssertEqual(1, ownerRows.Count);
                    AssertEqual(owned.Id, ownerRows[0].Id);
                    AssertEqual(owner.Id, ownerRows[0].UserId);

                    List<Signal> otherRows = await db.Signals.EnumerateAsync(Constants.DefaultTenantId, other.Id);
                    AssertEqual(1, otherRows.Count);
                    AssertEqual(otherOwned.Id, otherRows[0].Id);
                    AssertEqual(other.Id, otherRows[0].UserId);

                    Signal? hidden = await db.Signals.ReadAsync(Constants.DefaultTenantId, other.Id, owned.Id);
                    AssertNull(hidden);
                }
            }));

            cases.Add(CaseAsync("read_async_null_user_id_stays_null", "ReadAsync null user id stays null", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Signal signal = new Signal(SignalTypeEnum.Nudge, "{\"kind\":\"unowned\"}");
                    signal.TenantId = Constants.DefaultTenantId;
                    signal.UserId = null;
                    await db.Signals.CreateAsync(signal);

                    Signal? read = await db.Signals.ReadAsync(signal.Id);
                    AssertNotNull(read);
                    AssertNull(read!.UserId);

                    List<Signal> defaultUserRows = await db.Signals.EnumerateAsync(Constants.DefaultTenantId, Constants.DefaultUserId);
                    bool found = false;
                    foreach (Signal row in defaultUserRows)
                    {
                        if (row.Id == signal.Id) found = true;
                    }
                    AssertFalse(found);
                }
            }));

            // Audit addition: read of a missing signal id must return null (not-found path).
            cases.Add(CaseAsync("read_async_missing_returns_null", "ReadAsync missing returns null", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Signal? result = await db.Signals.ReadAsync("sig_nonexistent");
                    AssertNull(result);
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Signal Database",
                cases: cases);
        }

        #endregion

        #region Private-Methods

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
