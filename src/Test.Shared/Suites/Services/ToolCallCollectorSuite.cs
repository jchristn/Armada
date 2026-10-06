namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Server.Ask;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// <see cref="ToolCallCollector"/> elapsed times: measured on the monotonic clock, so a wall-clock jump (the host
    /// sleeping and waking, an NTP step) during a tool call neither inflates nor negates them.
    /// </summary>
    public sealed class ToolCallCollectorSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.ToolCallCollector";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("elapsed_uses_monotonic_clock", "A tool call's elapsed time is measured on the monotonic clock, not inflated by a wall-clock jump", TestTags.Reliability, () =>
            {
                // ElapsedSince was DateTime.UtcNow - started. Every wall-clock reading here is 10 minutes after the last.
                ToolCallCollector collector = new ToolCallCollector();
                JumpingTimeProvider time = new JumpingTimeProvider();
                collector.Time = time;
                collector.Observe(new CaptainToolActivity { Phase = "started", Id = "call_1", Name = "Bash" });

                double? soon = collector.ElapsedSince("call_1");
                AssertNotNull(soon, "the call was seen");
                AssertTrue(soon!.Value >= 0 && soon.Value < 60000, "elapsed right after the start is the real time, not the wall-clock jumps: " + soon.Value);

                time.Advance(TimeSpan.FromSeconds(3));
                double? later = collector.ElapsedSince("call_1");
                AssertTrue(later!.Value >= 3000 && later.Value < 63000, "elapsed follows the monotonic clock: " + later.Value);

                AssertNull(collector.ElapsedSince("call_unknown"), "an unseen call has no elapsed time");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Tool Call Collector",
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
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        #endregion
    }
}
