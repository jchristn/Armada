namespace Armada.Test.Unit.Suites.Database
{
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;

    public class SignalDatabaseTests : TestSuite
    {
        public override string Name => "Signal Database";

        protected override async Task RunTestsAsync()
        {
            await RunTest("CreateAsync returns signal", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Signal signal = new Signal(SignalTypeEnum.Assignment, "{\"test\":true}");
                    Signal result = await db.Signals.CreateAsync(signal);

                    AssertNotNull(result);
                    AssertEqual(SignalTypeEnum.Assignment, result.Type);
                }
            });

            await RunTest("ReadAsync returns created signal", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Signal signal = new Signal(SignalTypeEnum.Heartbeat);
                    await db.Signals.CreateAsync(signal);

                    Signal? result = await db.Signals.ReadAsync(signal.Id);
                    AssertNotNull(result);
                    AssertEqual(signal.Id, result!.Id);
                    AssertFalse(result.Read);
                }
            });

            await RunTest("EnumerateByRecipientAsync filters correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
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
            });

            await RunTest("EnumerateRecentAsync returns limited", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    for (int i = 0; i < 5; i++)
                    {
                        await db.Signals.CreateAsync(new Signal(SignalTypeEnum.Nudge, "msg" + i));
                    }

                    List<Signal> recent = await db.Signals.EnumerateRecentAsync(3);
                    AssertEqual(3, recent.Count);
                }
            });

            await RunTest("MarkReadAsync sets read flag", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Signal signal = new Signal(SignalTypeEnum.Mail, "test");
                    await db.Signals.CreateAsync(signal);
                    AssertFalse(signal.Read);

                    await db.Signals.MarkReadAsync(signal.Id);

                    Signal? result = await db.Signals.ReadAsync(signal.Id);
                    AssertTrue(result!.Read);
                }
            });
        }
    }
}
