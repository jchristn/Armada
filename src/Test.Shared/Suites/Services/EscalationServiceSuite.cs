namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// <see cref="EscalationService"/> rule cooldowns: a rule fires once per cooldown per entity, and the cooldown is
    /// measured on the monotonic clock, so a wall-clock jump (the host sleeping and waking, an NTP step) cannot end it
    /// early.
    /// </summary>
    public sealed class EscalationServiceSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.EscalationService";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("cooldown_survives_wall_clock_jump", "A rule's cooldown is measured on the monotonic clock: a wall-clock jump does not let it fire again early", TestTags.Reliability, async () =>
            {
                // The cooldown compared DateTime.UtcNow readings, so a wall-clock jump past the cooldown re-fired the
                // rule at once (and a backward step silenced it). Every wall-clock reading here is 10 minutes after
                // the last.
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ArmadaSettings settings = new ArmadaSettings();
                    settings.EscalationRules.Add(new EscalationRule(EscalationTriggerEnum.PoolExhausted, EscalationActionEnum.Log) { CooldownMinutes = 5 });
                    EscalationService service = new EscalationService(CreateLogging(), testDb.Driver, settings);
                    JumpingTimeProvider time = new JumpingTimeProvider();
                    service.Time = time;

                    await service.FireAsync(EscalationTriggerEnum.PoolExhausted, "pool", "first").ConfigureAwait(false);
                    await service.FireAsync(EscalationTriggerEnum.PoolExhausted, "pool", "inside the cooldown").ConfigureAwait(false);
                    AssertEqual(1, await CountSignalsAsync(testDb).ConfigureAwait(false), "the second fire is inside the cooldown despite the wall-clock jump");

                    await service.FireAsync(EscalationTriggerEnum.PoolExhausted, "other", "another entity").ConfigureAwait(false);
                    AssertEqual(2, await CountSignalsAsync(testDb).ConfigureAwait(false), "cooldowns are per entity");

                    time.Advance(TimeSpan.FromMinutes(6));
                    await service.FireAsync(EscalationTriggerEnum.PoolExhausted, "pool", "after the cooldown").ConfigureAwait(false);
                    AssertEqual(3, await CountSignalsAsync(testDb).ConfigureAwait(false), "fires again once the cooldown has passed on the monotonic clock");
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Escalation Service",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<int> CountSignalsAsync(TestDatabase testDb)
        {
            List<Signal> signals = await testDb.Driver.Signals.EnumerateRecentAsync(100).ConfigureAwait(false);
            return signals.Count;
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
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
