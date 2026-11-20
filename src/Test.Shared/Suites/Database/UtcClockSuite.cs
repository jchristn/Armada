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
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the shared UTC clock rule: a timestamp with no kind is already on the UTC clock and
    /// must not be moved by the local offset, on every provider and through every private timestamp helper
    /// (MySQL text binding, the PostgreSQL event, signal and merge-entry helpers, query filters). Each case
    /// runs with TZ=America/New_York so the local offset is not zero; Windows ignores TZ, so it skips there.
    /// </summary>
    public sealed class UtcClockSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.UtcClock";
        private static readonly DateTime _Wall = new DateTime(2020, 6, 15, 15, 0, 0);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the UTC clock suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("utc_clock_marks_unspecified_as_utc", "UtcClock marks a value with no kind as UTC and converts a local value", TestTags.Positive, async () =>
            {
                using (LocalTimeZoneScope zone = await LocalTimeZoneScope.EnterAsync().ConfigureAwait(false))
                {
                    DateTime utc = DateTime.SpecifyKind(_Wall, DateTimeKind.Utc);
                    DateTime unspecified = DateTime.SpecifyKind(_Wall, DateTimeKind.Unspecified);
                    DateTime local = DateTime.SpecifyKind(_Wall, DateTimeKind.Local);

                    AssertEqual(utc.Ticks, UtcClock.ToUtc(unspecified).Ticks, "unspecified ticks");
                    AssertEqual(DateTimeKind.Utc, UtcClock.ToUtc(unspecified).Kind, "unspecified kind");
                    AssertEqual(utc.Ticks, UtcClock.ToUtc(utc).Ticks, "utc ticks");
                    AssertEqual(local.ToUniversalTime().Ticks, UtcClock.ToUtc(local).Ticks, "local ticks");
                    AssertNull(UtcClock.ToUtc((DateTime?)null), "null stays null");
                    AssertEqual("2020-06-15T15:00:00.0000000Z", UtcClock.Format(unspecified, "yyyy-MM-ddTHH:mm:ss.fffffffZ"), "unspecified text");

                    AssertEqual(utc.Ticks, UtcClock.Parse("2020-06-15T15:00:00.0000000Z").Ticks, "parse Z");
                    AssertEqual(utc.Ticks, UtcClock.Parse("2020-06-15T15:00:00.0000000").Ticks, "parse text with no offset as UTC");
                    AssertEqual(utc.Ticks, UtcClock.Parse("2020-06-15T11:00:00.0000000-04:00").Ticks, "parse offset");
                    AssertEqual(DateTimeKind.Utc, UtcClock.Parse("2020-06-15T15:00:00.0000000").Kind, "parse kind");
                }
            }));

            cases.Add(CaseAsync("private_helpers_keep_unspecified_on_utc_clock", "Event, signal, merge entry and fleet timestamps with no kind keep their UTC clock", TestTags.Positive, async () =>
            {
                using (LocalTimeZoneScope zone = await LocalTimeZoneScope.EnterAsync().ConfigureAwait(false))
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver db = testDb.Driver;
                    DateTime unspecified = DateTime.SpecifyKind(_Wall, DateTimeKind.Unspecified);
                    long expected = DateTime.SpecifyKind(_Wall, DateTimeKind.Utc).Ticks;

                    ArmadaEvent evt = new ArmadaEvent("utc.clock", "unspecified");
                    evt.CreatedUtc = unspecified;
                    await db.Events.CreateAsync(evt).ConfigureAwait(false);
                    ArmadaEvent? evtRead = await db.Events.ReadAsync(evt.Id).ConfigureAwait(false);
                    AssertNotNull(evtRead, "event read");
                    AssertEqual(expected, evtRead!.CreatedUtc.Ticks, "event created ticks");

                    Signal signal = new Signal(SignalTypeEnum.Heartbeat, "clock");
                    signal.CreatedUtc = unspecified;
                    await db.Signals.CreateAsync(signal).ConfigureAwait(false);
                    Signal? signalRead = await db.Signals.ReadAsync(signal.Id).ConfigureAwait(false);
                    AssertNotNull(signalRead, "signal read");
                    AssertEqual(expected, signalRead!.CreatedUtc.Ticks, "signal created ticks");

                    MergeEntry entry = new MergeEntry("clock/branch", "main");
                    entry.CreatedUtc = unspecified;
                    entry.TestStartedUtc = unspecified;
                    await db.MergeEntries.CreateAsync(entry).ConfigureAwait(false);
                    MergeEntry? entryRead = await db.MergeEntries.ReadAsync(entry.Id).ConfigureAwait(false);
                    AssertNotNull(entryRead, "merge entry read");
                    AssertEqual(expected, entryRead!.CreatedUtc.Ticks, "merge entry created ticks");
                    AssertNotNull(entryRead.TestStartedUtc, "merge entry test started");
                    AssertEqual(expected, entryRead.TestStartedUtc!.Value.Ticks, "merge entry test started ticks");

                    Fleet fleet = new Fleet("utc-clock-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                    fleet.CreatedUtc = unspecified;
                    await db.Fleets.CreateAsync(fleet).ConfigureAwait(false);
                    Fleet? fleetRead = await db.Fleets.ReadAsync(fleet.Id).ConfigureAwait(false);
                    AssertNotNull(fleetRead, "fleet read");
                    AssertEqual(expected, fleetRead!.CreatedUtc.Ticks, "fleet created ticks");
                }
            }));

            cases.Add(CaseAsync("query_filter_with_unspecified_bound_uses_utc_clock", "A created-after filter with no kind compares on the UTC clock", TestTags.Positive, async () =>
            {
                using (LocalTimeZoneScope zone = await LocalTimeZoneScope.EnterAsync().ConfigureAwait(false))
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver db = testDb.Driver;
                    string eventType = "utc.clock.filter." + Guid.NewGuid().ToString("N").Substring(0, 8);
                    ArmadaEvent evt = new ArmadaEvent(eventType, "filter");
                    evt.CreatedUtc = DateTime.SpecifyKind(_Wall, DateTimeKind.Utc);
                    await db.Events.CreateAsync(evt).ConfigureAwait(false);

                    EnumerationQuery before = new EnumerationQuery();
                    before.EventType = eventType;
                    before.CreatedAfter = DateTime.SpecifyKind(_Wall.AddMinutes(-1), DateTimeKind.Unspecified);
                    EnumerationResult<ArmadaEvent> included = await db.Events.EnumerateAsync(before).ConfigureAwait(false);
                    AssertTrue(included.Objects.Any(e => e.Id == evt.Id), "an event one minute after an unspecified lower bound is included");

                    EnumerationQuery after = new EnumerationQuery();
                    after.EventType = eventType;
                    after.CreatedAfter = DateTime.SpecifyKind(_Wall.AddMinutes(1), DateTimeKind.Unspecified);
                    EnumerationResult<ArmadaEvent> excluded = await db.Events.EnumerateAsync(after).ConfigureAwait(false);
                    AssertFalse(excluded.Objects.Any(e => e.Id == evt.Id), "an event one minute before an unspecified lower bound is excluded");
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "UTC Clock",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            bool windows = OperatingSystem.IsWindows();
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag, TestTags.Database },
                skip: windows,
                skipReason: windows ? "The TZ variable does not change the .NET local zone on Windows." : null);
        }

        #endregion
    }
}
