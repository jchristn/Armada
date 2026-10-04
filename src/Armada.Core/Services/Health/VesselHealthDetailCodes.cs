namespace Armada.Core.Services.Health
{
    /// <summary>
    /// Stable detail codes stored on vessel health findings (<see cref="Armada.Core.Models.VesselHealthFinding.DetailCode"/>).
    /// Clients localize these codes together with the typed ValueA and ValueB integers, so the codes and the meaning of
    /// the two values must never change once shipped. Each constant documents which criterion emits it and what
    /// ValueA and ValueB carry. Values not mentioned are null.
    /// </summary>
    public static class VesselHealthDetailCodes
    {
        #region General

        /// <summary>
        /// Any criterion, status Unknown: the criterion threw while evaluating. ValueA and ValueB are null.
        /// </summary>
        public const string EvaluationError = "EvaluationError";

        /// <summary>
        /// Any criterion, status Unknown: neither the working directory nor the bare clone exists as a git repository.
        /// </summary>
        public const string RepositoryUnavailable = "RepositoryUnavailable";

        /// <summary>
        /// WorkingTree, TestInfrastructure, Dependencies, Vulnerabilities, status NotApplicable: the evaluated path is a
        /// bare clone, so there is no checkout to inspect.
        /// </summary>
        public const string BareRepository = "BareRepository";

        /// <summary>
        /// Any criterion, status NotApplicable: the criterion does not apply to this vessel for another reason.
        /// </summary>
        public const string NotApplicable = "NotApplicable";

        #endregion

        #region GitDivergence

        /// <summary>
        /// GitDivergence, status Pass: HEAD is neither ahead of nor behind the default branch. ValueA = commits ahead (0),
        /// ValueB = commits behind (0).
        /// </summary>
        public const string Even = "Even";

        /// <summary>
        /// GitDivergence, status Pass: HEAD has commits the default branch does not, and is not behind. ValueA = commits
        /// ahead, ValueB = commits behind (0).
        /// </summary>
        public const string Ahead = "Ahead";

        /// <summary>
        /// GitDivergence, status Warn or Fail: HEAD is behind the default branch and not ahead. ValueA = commits ahead
        /// (0), ValueB = commits behind.
        /// </summary>
        public const string Behind = "Behind";

        /// <summary>
        /// GitDivergence, status Fail: HEAD is both ahead of and behind the default branch, or both ahead of and behind
        /// its upstream. ValueA = commits ahead of the default branch, ValueB = commits behind the default branch.
        /// </summary>
        public const string Diverged = "Diverged";

        /// <summary>
        /// GitDivergence, status Unknown: git fetch failed, so divergence counts would be stale.
        /// </summary>
        public const string FetchFailed = "FetchFailed";

        /// <summary>
        /// GitDivergence, status Unknown: neither origin/&lt;DefaultBranch&gt; nor &lt;DefaultBranch&gt; resolves.
        /// </summary>
        public const string DefaultBranchMissing = "DefaultBranchMissing";

        /// <summary>
        /// GitDivergence or CommitRecency, status Unknown: the repository has no commits.
        /// </summary>
        public const string NoCommits = "NoCommits";

        #endregion

        #region WorkingTree

        /// <summary>
        /// WorkingTree, status Pass: no modified tracked files and no untracked files. ValueA = modified tracked files (0),
        /// ValueB = untracked files (0).
        /// </summary>
        public const string Clean = "Clean";

        /// <summary>
        /// WorkingTree, status Warn: only untracked files. ValueA = modified tracked files (0), ValueB = untracked files.
        /// </summary>
        public const string UntrackedOnly = "UntrackedOnly";

        /// <summary>
        /// WorkingTree, status Fail: tracked files are modified. ValueA = modified tracked files, ValueB = untracked files.
        /// </summary>
        public const string Modified = "Modified";

        #endregion

        #region Branches

        /// <summary>
        /// Branches, status Pass: stale branches are below the warn threshold and there are no leftover armada/*
        /// branches. ValueA = stale branches, ValueB = armada/* branches (0).
        /// </summary>
        public const string BranchesOk = "BranchesOk";

        /// <summary>
        /// Branches, status Warn or Fail: stale branches reached the warn or fail threshold. ValueA = stale branches,
        /// ValueB = armada/* branches.
        /// </summary>
        public const string StaleBranches = "StaleBranches";

        /// <summary>
        /// Branches, status Warn: leftover armada/* branches exist while stale branches are below the warn threshold.
        /// ValueA = stale branches, ValueB = armada/* branches.
        /// </summary>
        public const string ArmadaBranches = "ArmadaBranches";

        #endregion

        #region CommitRecency

        /// <summary>
        /// CommitRecency, status Pass (informational): ValueA = whole days since the last commit, ValueB = null.
        /// </summary>
        public const string LastCommitAge = "LastCommitAge";

        #endregion

        #region Dependencies-and-Vulnerabilities

        /// <summary>
        /// Dependencies, status Pass: no outdated packages. ValueA = outdated packages (0), ValueB = packages with major
        /// drift (0).
        /// </summary>
        public const string NoOutdatedPackages = "NoOutdatedPackages";

        /// <summary>
        /// Dependencies, status Warn (minor or patch drift only) or Fail (any major drift). ValueA = distinct outdated
        /// packages, ValueB = distinct packages with major drift.
        /// </summary>
        public const string OutdatedPackages = "OutdatedPackages";

        /// <summary>
        /// Vulnerabilities, status Pass: no vulnerable packages. ValueA = vulnerable packages (0), ValueB = highest
        /// severity rank (0 = None).
        /// </summary>
        public const string NoVulnerabilities = "NoVulnerabilities";

        /// <summary>
        /// Vulnerabilities, status Warn (highest severity Low or Moderate) or Fail (High or Critical). ValueA = distinct
        /// vulnerable packages, ValueB = highest severity rank (1 = Low, 2 = Moderate, 3 = High, 4 = Critical).
        /// </summary>
        public const string VulnerablePackages = "VulnerablePackages";

        /// <summary>
        /// Dependencies or Vulnerabilities, status NotApplicable: no NuGet project or npm package.json with a lockfile
        /// was found.
        /// </summary>
        public const string NoPackageManifests = "NoPackageManifests";

        /// <summary>
        /// Dependencies or Vulnerabilities, status Unknown: the dotnet or npm executable is not installed on the host.
        /// </summary>
        public const string ToolMissing = "ToolMissing";

        /// <summary>
        /// Dependencies or Vulnerabilities, status Unknown: the tool reported that a package restore (or npm install)
        /// is required, or the restore failed.
        /// </summary>
        public const string RestoreRequired = "RestoreRequired";

        /// <summary>
        /// Dependencies or Vulnerabilities, status Unknown: the tool did not finish within
        /// RepositoryHealth.DependencyCommandTimeoutSeconds. ValueA = the timeout in seconds.
        /// </summary>
        public const string Timeout = "Timeout";

        /// <summary>
        /// Dependencies or Vulnerabilities, status Unknown: the tool output could not be parsed as the expected JSON.
        /// </summary>
        public const string ParseError = "ParseError";

        /// <summary>
        /// Dependencies or Vulnerabilities, status Unknown: the tool exited with an unexpected exit code and no
        /// recognizable cause. ValueA = the exit code.
        /// </summary>
        public const string ToolFailed = "ToolFailed";

        #endregion

        #region TestInfrastructure

        /// <summary>
        /// TestInfrastructure, status Pass: tests were found and the latest test check run passed. ValueA = test
        /// indicators found (test projects, test config files, or test files), ValueB = null.
        /// </summary>
        public const string TestsPassing = "TestsPassing";

        /// <summary>
        /// TestInfrastructure, status Warn: tests were found but Armada has no completed test check run for the vessel.
        /// ValueA = test indicators found.
        /// </summary>
        public const string NoTestRun = "NoTestRun";

        /// <summary>
        /// TestInfrastructure, status Warn: tests were found but the latest test check run did not pass. ValueA = test
        /// indicators found.
        /// </summary>
        public const string LastTestRunFailed = "LastTestRunFailed";

        /// <summary>
        /// TestInfrastructure, status Fail: a recognizable project was found with no test projects, configuration, or
        /// files. ValueA = recognized projects.
        /// </summary>
        public const string NoTestsFound = "NoTestsFound";

        /// <summary>
        /// TestInfrastructure, status NotApplicable: no .NET, Node, Python, Go, or Rust project was recognized.
        /// </summary>
        public const string NoRecognizedProject = "NoRecognizedProject";

        #endregion

        #region ContinuousIntegration

        /// <summary>
        /// ContinuousIntegration, status Pass: CI configuration is present. ValueA = CI configuration files found.
        /// </summary>
        public const string CiConfigured = "CiConfigured";

        /// <summary>
        /// ContinuousIntegration, status Fail: no GitHub Actions workflow, azure-pipelines.yml, .gitlab-ci.yml, or
        /// Jenkinsfile was found. ValueA = 0.
        /// </summary>
        public const string NoCiConfig = "NoCiConfig";

        #endregion

        #region ArmadaReadiness

        /// <summary>
        /// ArmadaReadiness, status Pass: no readiness issues. ValueA = errors (0), ValueB = warnings (0).
        /// </summary>
        public const string ReadinessOk = "ReadinessOk";

        /// <summary>
        /// ArmadaReadiness, status Warn: readiness warnings only. ValueA = errors (0), ValueB = warnings.
        /// </summary>
        public const string ReadinessWarnings = "ReadinessWarnings";

        /// <summary>
        /// ArmadaReadiness, status Fail: readiness errors. ValueA = errors, ValueB = warnings.
        /// </summary>
        public const string ReadinessErrors = "ReadinessErrors";

        #endregion

        #region MissionOutcomes

        /// <summary>
        /// MissionOutcomes, status Pass: no failed or landing-failed missions in the window. ValueA = failures (0),
        /// ValueB = window in days.
        /// </summary>
        public const string NoRecentFailures = "NoRecentFailures";

        /// <summary>
        /// MissionOutcomes, status Warn or Fail: failed or landing-failed missions in the window. ValueA = failures,
        /// ValueB = window in days.
        /// </summary>
        public const string RecentFailures = "RecentFailures";

        #endregion
    }
}
