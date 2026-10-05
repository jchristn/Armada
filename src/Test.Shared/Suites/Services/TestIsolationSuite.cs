namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Settings;
    using Armada.Tui.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Guards the test run against the developer's real profile: every default path Armada derives (data directory,
    /// settings file, database, TUI preferences and credentials) must resolve inside the per-run sandbox that
    /// <see cref="TestTemp"/> sets up, and the TUI must use the file credential store instead of the OS keychain.
    /// </summary>
    public sealed class TestIsolationSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the test isolation suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("defaults_resolve_inside_sandbox", "Default data, settings, and TUI paths resolve inside the test sandbox, never ~/.armada", () =>
            {
                string? sandbox = TestTemp.ProfileSandbox;
                AssertNotNull(sandbox, "sandbox initialized");
                string realProfile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".armada");

                AssertEqual(sandbox!, Constants.DefaultDataDirectory, "default data directory");
                AssertTrue(ArmadaSettings.DefaultSettingsPath.StartsWith(sandbox!, StringComparison.Ordinal), "default settings path");
                AssertTrue(TuiPaths.PreferencesFile().StartsWith(sandbox!, StringComparison.Ordinal), "TUI preferences");
                AssertTrue(TuiPaths.CredentialsFile().StartsWith(sandbox!, StringComparison.Ordinal), "TUI credentials");
                AssertEqual("file", Environment.GetEnvironmentVariable("ARMADA_TUI_CREDENTIAL_STORE"), "no OS keychain in tests");

                foreach (string path in new string[] { Constants.DefaultDataDirectory, ArmadaSettings.DefaultSettingsPath, TuiPaths.PreferencesFile(), TuiPaths.CredentialsFile() })
                {
                    AssertFalse(path.StartsWith(realProfile, StringComparison.Ordinal), "must not touch the real profile: " + path);
                }
            }));

            cases.Add(Case("unsaved_settings_save_into_sandbox", "Settings saved without an explicit path land in the sandbox", () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                AssertTrue(settings.EffectiveSettingsFilePath.StartsWith(TestTemp.ProfileSandbox!, StringComparison.Ordinal), "effective settings path");
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.TestIsolation",
                displayName: "Test isolation from the real profile",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.TestIsolation",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { TestTags.Positive });
        }

        #endregion
    }
}
