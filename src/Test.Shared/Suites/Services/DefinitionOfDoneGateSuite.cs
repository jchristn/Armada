namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
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
    /// Descriptors for <see cref="DefinitionOfDoneGate"/>, exercising the real shell executor with portable
    /// <c>exit</c> commands. Positive cases confirm a disabled/unconfigured gate is skipped and a clean
    /// build+test passes; negative cases confirm a failed build classifies Compile, a failed test classifies
    /// TestFail, and a missing checkout classifies Infra.
    /// </summary>
    public sealed class DefinitionOfDoneGateSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Definition-of-Done gate suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("disabled_gate_is_skipped", "A disabled gate is skipped and passes", TestTags.Positive, async () =>
            {
                DefinitionOfDoneGate gate = new DefinitionOfDoneGate(CreateLogging());
                string dir = TestTemp.NewDirectory("dod");
                try
                {
                    Vessel vessel = new Vessel("dod-vessel", "https://github.com/test/repo");
                    vessel.DefinitionOfDoneEnabled = false;
                    vessel.DefinitionOfDoneBuildCommand = "exit 1";
                    DefinitionOfDoneResult result = await gate.EvaluateAsync(vessel, dir).ConfigureAwait(false);
                    AssertTrue(result.Passed, "disabled gate should pass");
                    AssertTrue(result.Skipped, "disabled gate should be marked skipped");
                }
                finally { SafeDelete(dir); }
            }));

            cases.Add(CaseAsync("enabled_but_no_commands_is_skipped", "An enabled gate with no commands is skipped", TestTags.Positive, async () =>
            {
                DefinitionOfDoneGate gate = new DefinitionOfDoneGate(CreateLogging());
                string dir = TestTemp.NewDirectory("dod");
                try
                {
                    Vessel vessel = new Vessel("dod-vessel", "https://github.com/test/repo");
                    vessel.DefinitionOfDoneEnabled = true;
                    DefinitionOfDoneResult result = await gate.EvaluateAsync(vessel, dir).ConfigureAwait(false);
                    AssertTrue(result.Passed, "no-command gate should pass");
                    AssertTrue(result.Skipped, "no-command gate should be marked skipped");
                }
                finally { SafeDelete(dir); }
            }));

            cases.Add(CaseAsync("clean_build_and_test_passes", "A clean build and test passes the gate", TestTags.Positive, async () =>
            {
                DefinitionOfDoneGate gate = new DefinitionOfDoneGate(CreateLogging());
                string dir = TestTemp.NewDirectory("dod");
                try
                {
                    Vessel vessel = new Vessel("dod-vessel", "https://github.com/test/repo");
                    vessel.DefinitionOfDoneEnabled = true;
                    vessel.DefinitionOfDoneBuildCommand = "exit 0";
                    vessel.DefinitionOfDoneTestCommand = "exit 0";
                    DefinitionOfDoneResult result = await gate.EvaluateAsync(vessel, dir).ConfigureAwait(false);
                    AssertEqual(DefinitionOfDoneOutcomeEnum.Pass, result.Outcome, "clean build+test should pass");
                    AssertFalse(result.Skipped, "a real run is not skipped");
                }
                finally { SafeDelete(dir); }
            }));

            cases.Add(CaseAsync("failing_build_is_compile", "A failing build classifies as Compile", TestTags.Negative, async () =>
            {
                DefinitionOfDoneGate gate = new DefinitionOfDoneGate(CreateLogging());
                string dir = TestTemp.NewDirectory("dod");
                try
                {
                    Vessel vessel = new Vessel("dod-vessel", "https://github.com/test/repo");
                    vessel.DefinitionOfDoneEnabled = true;
                    vessel.DefinitionOfDoneBuildCommand = "exit 1";
                    vessel.DefinitionOfDoneTestCommand = "exit 0";
                    DefinitionOfDoneResult result = await gate.EvaluateAsync(vessel, dir).ConfigureAwait(false);
                    AssertEqual(DefinitionOfDoneOutcomeEnum.Compile, result.Outcome, "a failing build should classify Compile");
                    AssertFalse(result.Passed, "a failing build should not pass");
                }
                finally { SafeDelete(dir); }
            }));

            cases.Add(CaseAsync("failing_test_is_testfail", "A failing test after a clean build classifies as TestFail", TestTags.Negative, async () =>
            {
                DefinitionOfDoneGate gate = new DefinitionOfDoneGate(CreateLogging());
                string dir = TestTemp.NewDirectory("dod");
                try
                {
                    Vessel vessel = new Vessel("dod-vessel", "https://github.com/test/repo");
                    vessel.DefinitionOfDoneEnabled = true;
                    vessel.DefinitionOfDoneBuildCommand = "exit 0";
                    vessel.DefinitionOfDoneTestCommand = "exit 3";
                    DefinitionOfDoneResult result = await gate.EvaluateAsync(vessel, dir).ConfigureAwait(false);
                    AssertEqual(DefinitionOfDoneOutcomeEnum.TestFail, result.Outcome, "a failing test should classify TestFail");
                }
                finally { SafeDelete(dir); }
            }));

            cases.Add(CaseAsync("missing_checkout_is_infra", "A missing checkout classifies as Infra", TestTags.Negative, async () =>
            {
                DefinitionOfDoneGate gate = new DefinitionOfDoneGate(CreateLogging());
                Vessel vessel = new Vessel("dod-vessel", "https://github.com/test/repo");
                vessel.DefinitionOfDoneEnabled = true;
                vessel.DefinitionOfDoneBuildCommand = "exit 0";
                string missing = Path.Combine(Path.GetTempPath(), "armada_dod_missing_" + Guid.NewGuid().ToString("N"));
                DefinitionOfDoneResult result = await gate.EvaluateAsync(vessel, missing).ConfigureAwait(false);
                AssertEqual(DefinitionOfDoneOutcomeEnum.Infra, result.Outcome, "a missing checkout should classify Infra");
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.DefinitionOfDoneGate",
                displayName: "Definition-of-Done Gate",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static void SafeDelete(string dir)
        {
            if (Directory.Exists(dir))
            {
                try { Directory.Delete(dir, true); }
                catch { }
            }
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.DefinitionOfDoneGate",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
