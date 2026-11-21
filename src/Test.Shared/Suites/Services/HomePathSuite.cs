namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Harbor;
    using Armada.Core.Hosting;
    using Armada.Runtimes;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="HomePath"/>: a leading <c>~</c> (alone, <c>~/</c>, or <c>~\</c>) expands to the home
    /// directory, other forms are left alone, and the Harbor job runner applies the expansion to a launch's working
    /// directory before checking that it exists.
    /// </summary>
    public sealed class HomePathSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.HomePath";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the home path suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("expands_leading_tilde", "~, ~/x and ~\\x expand to the given home directory", TestTags.Positive, () =>
            {
                string home = Path.Combine(Path.GetTempPath(), "home-of-someone");
                AssertEqual(home, HomePath.Expand("~", home), "~ alone");
                AssertEqual(home, HomePath.Expand("~/", home), "~/ alone");
                AssertEqual(home, HomePath.Expand("~\\", home), "~\\ alone");
                AssertEqual(Path.Combine(home, "Code/Armada"), HomePath.Expand("~/Code/Armada", home), "~/ prefix");
                AssertEqual(Path.Combine(home, "Code"), HomePath.Expand("~\\Code", home), "~\\ prefix");
                AssertTrue(HomePath.StartsWithHome("~"), "~ is a home reference");
                AssertTrue(HomePath.StartsWithHome("~/a"), "~/ is a home reference");
                AssertTrue(HomePath.StartsWithHome("~\\a"), "~\\ is a home reference");
            }));

            cases.Add(Case("other_forms_unchanged", "~user, a later ~, absolute and relative paths, null and empty are returned unchanged", TestTags.Negative, () =>
            {
                string home = Path.Combine(Path.GetTempPath(), "home-of-someone");
                AssertEqual("~alice/Code", HomePath.Expand("~alice/Code", home), "~user is not expanded");
                AssertEqual("/srv/~/data", HomePath.Expand("/srv/~/data", home), "a ~ later in the path");
                AssertEqual("/srv/data", HomePath.Expand("/srv/data", home), "absolute path");
                AssertEqual("src/app", HomePath.Expand("src/app", home), "relative path");
                AssertNull(HomePath.Expand(null, home), "null");
                AssertEqual(String.Empty, HomePath.Expand(String.Empty, home), "empty");
                AssertEqual("~/Code", HomePath.Expand("~/Code", null), "no home directory to substitute");
                AssertFalse(HomePath.StartsWithHome("~alice"), "~user");
                AssertFalse(HomePath.StartsWithHome(null), "null");
                AssertFalse(HomePath.StartsWithHome("/a/~"), "later ~");
            }));

            cases.Add(Case("default_home_is_user_profile", "Expand without a home directory uses the current user's profile directory", TestTags.Positive, () =>
            {
                string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                AssertEqual(profile, HomePath.Expand("~"), "~ is the user profile");
                AssertEqual(Path.Combine(profile, "projects"), HomePath.Expand("~/projects"), "~/projects under the user profile");
            }));

            cases.Add(Case("harbor_runner_expands_working_directory", "The Harbor job runner expands ~ in a launch's working directory before deciding on a scratch folder", TestTags.Positive, () =>
            {
                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                string scratchRoot = TestTemp.NewDirectory("homepath_scratch");
                try
                {
                    LocalHarborJobRunner runner = new LocalHarborJobRunner(logging, new AgentRuntimeFactory(logging), scratchRoot);
                    string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                    HarborLaunchRequest home = new HarborLaunchRequest { JobId = "job-home", WorkingDirectory = "~", ScratchWorkingDirectory = true };
                    AssertEqual(profile, runner.ResolveWorkingDirectory(home), "~ exists on this host, so no scratch folder is used");

                    HarborLaunchRequest strict = new HarborLaunchRequest { JobId = "job-strict", WorkingDirectory = "~/armada-homepath-missing-" + Guid.NewGuid().ToString("N"), ScratchWorkingDirectory = false };
                    string? resolved = runner.ResolveWorkingDirectory(strict);
                    AssertNotNull(resolved, "a named directory resolves");
                    AssertTrue(resolved!.StartsWith(profile, StringComparison.Ordinal), "expanded under the home directory: " + resolved);
                    AssertFalse(resolved.Contains("~", StringComparison.Ordinal), "no ~ left in the path");

                    HarborLaunchRequest fallback = new HarborLaunchRequest { JobId = "job-fallback", WorkingDirectory = strict.WorkingDirectory, ScratchWorkingDirectory = true };
                    AssertContains(scratchRoot, runner.ResolveWorkingDirectory(fallback) ?? String.Empty, "a missing folder still falls back to scratch when the launch allows it");
                }
                finally
                {
                    TestTemp.TryDelete(scratchRoot);
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Home path expansion",
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
