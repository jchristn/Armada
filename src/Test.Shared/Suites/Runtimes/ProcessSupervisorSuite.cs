namespace Test.Shared.Suites.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Enums;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="ProcessSupervisor"/> process identity: an exact start time recorded for a process id
    /// verifies or rejects the live process; without one (upgraded rows, Harbor launches) the approximate launch-time
    /// check from v1.3.0 still applies, so such processes are never made unstoppable.
    /// </summary>
    public sealed class ProcessSupervisorSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Runtimes.ProcessSupervisor";
        private static readonly DateTime _Launch = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the process supervisor suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("exact_start_time_verifies_and_rejects", "A recorded start time verifies the same process and rejects a later one", TestTags.Positive, () =>
            {
                AssertEqual(ProcessIdentityEnum.Verified, ProcessSupervisor.Compare(_Launch, 42, null, 42, _Launch), "same start");
                AssertEqual(ProcessIdentityEnum.Verified, ProcessSupervisor.Compare(_Launch.AddMilliseconds(900), 42, null, 42, _Launch), "within tolerance");
                AssertEqual(ProcessIdentityEnum.Reused, ProcessSupervisor.Compare(_Launch.AddSeconds(5), 42, null, 42, _Launch), "five seconds later is another process");
                AssertEqual(ProcessIdentityEnum.Reused, ProcessSupervisor.Compare(_Launch.AddSeconds(-5), 42, null, 42, _Launch), "five seconds earlier is another process");
                AssertEqual(ProcessIdentityEnum.Reused, ProcessSupervisor.Compare(_Launch.AddSeconds(5), 42, _Launch.AddMinutes(-1), 42, _Launch), "the exact record wins over the looser launch time");
            }));

            cases.Add(Case("record_for_another_pid_falls_back", "A start time recorded for a different process id falls back to the launch-time check", TestTags.Negative, () =>
            {
                AssertEqual(ProcessIdentityEnum.Unverified, ProcessSupervisor.Compare(_Launch.AddSeconds(5), 42, null, 41, _Launch), "stale record for an earlier pid is ignored");
                AssertEqual(ProcessIdentityEnum.Unverified, ProcessSupervisor.Compare(_Launch.AddMinutes(2), 42, _Launch, 41, _Launch), "within the launch tolerance");
                AssertEqual(ProcessIdentityEnum.Reused, ProcessSupervisor.Compare(_Launch.AddMinutes(10), 42, _Launch, 41, _Launch), "started long after the launch");
            }));

            cases.Add(Case("no_record_keeps_launch_time_check", "Without a recorded start time the v1.3.0 five-minute launch check is unchanged", TestTags.Positive, () =>
            {
                AssertEqual(ProcessIdentityEnum.Unverified, ProcessSupervisor.Compare(_Launch.AddMinutes(4), 42, _Launch, null, null), "inside five minutes");
                AssertEqual(ProcessIdentityEnum.Reused, ProcessSupervisor.Compare(_Launch.AddMinutes(6), 42, _Launch, null, null), "after five minutes");
                AssertEqual(ProcessIdentityEnum.Unverified, ProcessSupervisor.Compare(_Launch.AddDays(1), 42, null, null, null), "no launch reference at all");
                AssertEqual(ProcessIdentityEnum.Unverified, ProcessSupervisor.Compare(_Launch.AddSeconds(30), 42, null, 42, null), "a pid without a start time is no record");
            }));

            cases.Add(Case("tolerance_is_validated", "The start time tolerance rejects negative and over-long values", TestTags.Negative, () =>
            {
                TimeSpan original = ProcessSupervisor.StartTimeMatchTolerance;
                AssertEqual(TimeSpan.FromSeconds(2), original, "default");
                AssertThrows<ArgumentOutOfRangeException>(() => ProcessSupervisor.StartTimeMatchTolerance = TimeSpan.FromMilliseconds(-1), "negative");
                AssertThrows<ArgumentOutOfRangeException>(() => ProcessSupervisor.StartTimeMatchTolerance = TimeSpan.FromMinutes(2), "over one minute");
                AssertEqual(original, ProcessSupervisor.StartTimeMatchTolerance, "unchanged after rejected values");
            }));

            cases.Add(Case("live_process_identity", "The running test process verifies against its own start time and is rejected against another", TestTags.Positive, () =>
            {
                using (Process self = Process.GetCurrentProcess())
                {
                    int pid = self.Id;
                    AssertTrue(ProcessSupervisor.TryReadStartTimeUtc(pid, out DateTime startedUtc), "start time readable");
                    AssertEqual(DateTimeKind.Utc, startedUtc.Kind, "UTC");
                    AssertEqual(ProcessIdentityEnum.Verified, ProcessSupervisor.CheckIdentity(pid, null, pid, startedUtc), "own start time");
                    AssertEqual(ProcessIdentityEnum.Reused, ProcessSupervisor.CheckIdentity(pid, null, pid, startedUtc.AddHours(-1)), "an hour earlier is another process");
                    AssertFalse(ProcessSupervisor.IsTrackedProcessAlive(pid, null, pid, startedUtc.AddHours(-1)), "a reused id is not alive");
                    AssertTrue(ProcessSupervisor.IsTrackedProcessAlive(pid, null, pid, startedUtc), "the launched process is alive");
                    AssertTrue(ProcessSupervisor.IsTrackedProcessAlive(pid, startedUtc), "the existing launch-time overload still works");
                    AssertEqual(ProcessIdentityEnum.Unverified, ProcessSupervisor.CheckIdentity(pid, null, null, null), "no record");
                }

                AssertEqual(ProcessIdentityEnum.NotRunning, ProcessSupervisor.CheckIdentity(0, null, null, null), "pid 0");
                AssertEqual(ProcessIdentityEnum.NotRunning, ProcessSupervisor.CheckIdentity(-1, null, null, null), "pid -1");
                AssertFalse(ProcessSupervisor.TryReadStartTimeUtc(-1, out _), "no start time for pid -1");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Process Supervisor",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; },
                tags: new List<string> { tag });
        }

        #endregion
    }
}
