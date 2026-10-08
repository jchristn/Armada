namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Matching a vessel's repository URL against a checkout's remotes: https, http, git, and ssh URLs and the scp-like
    /// form name the same repository regardless of a .git suffix, case, user, or port; different repositories differ.
    /// </summary>
    public sealed class GitRemoteUrlSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.GitRemoteUrl";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("https_and_ssh_forms_match", "https, ssh://, and scp-like URLs of one repository match", TestTags.Positive, () =>
            {
                string https = "https://github.com/Owner/Repo.git";
                AssertTrue(GitRemoteUrl.SameRepository(https, "git@github.com:owner/repo.git"), "scp-like ssh");
                AssertTrue(GitRemoteUrl.SameRepository(https, "ssh://git@github.com/owner/repo"), "ssh URL without .git");
                AssertTrue(GitRemoteUrl.SameRepository(https, "ssh://git@github.com:22/owner/repo.git"), "ssh URL with a port");
                AssertTrue(GitRemoteUrl.SameRepository(https, "http://GitHub.com/owner/repo/"), "http, case, trailing slash");
                AssertTrue(GitRemoteUrl.SameRepository(https, "https://user:token@github.com/owner/repo"), "credentials in the URL");
                AssertTrue(GitRemoteUrl.SameRepository(https, "git://github.com/owner/repo.git"), "git protocol");
                AssertEqual("github.com/owner/repo", GitRemoteUrl.Normalize(https));
            }));

            cases.Add(Case("different_repositories_do_not_match", "Another owner, repository, or host does not match", TestTags.Negative, () =>
            {
                string https = "https://github.com/owner/repo.git";
                AssertFalse(GitRemoteUrl.SameRepository(https, "git@github.com:owner/repo2.git"), "another repository");
                AssertFalse(GitRemoteUrl.SameRepository(https, "git@github.com:other/repo.git"), "another owner");
                AssertFalse(GitRemoteUrl.SameRepository(https, "git@gitlab.com:owner/repo.git"), "another host");
                AssertFalse(GitRemoteUrl.SameRepository(https, null), "no URL");
                AssertFalse(GitRemoteUrl.SameRepository("", ""), "two empty URLs");
                AssertNull(GitRemoteUrl.Normalize("   "));
            }));

            cases.Add(Case("local_paths_compare_as_paths", "Local paths and file:// URLs compare by path, ignoring a .git suffix", TestTags.Positive, () =>
            {
                AssertTrue(GitRemoteUrl.SameRepository("/srv/git/app.git", "file:///srv/git/app"), "file URL");
                AssertTrue(GitRemoteUrl.SameRepository("C:\\git\\app.git", "C:/git/app"), "Windows drive path");
                AssertFalse(GitRemoteUrl.SameRepository("/srv/git/app.git", "/srv/git/other.git"), "another path");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Git Remote URL Matching", cases: cases);
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
