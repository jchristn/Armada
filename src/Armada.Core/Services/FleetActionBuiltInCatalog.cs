namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// The definitions of the built-in fleet actions, and the earlier shipped versions of the ones that changed, so the
    /// seeder can upgrade a built-in a tenant never edited. Scan actions (Command) pair with fix actions (Mission)
    /// through <see cref="FleetAction.ScanSourceActionKey"/>: the fix prompt cites the vessel's latest scan result through
    /// <c>{{scan.summary}}</c>, and a follow-up run (FromRunId) dispatches the fix only where the scan found something.
    /// </summary>
    public static class FleetActionBuiltInCatalog
    {
        #region Public-Methods

        /// <summary>
        /// Build fresh (unsaved, tenant-less) instances of every built-in action.
        /// </summary>
        /// <param name="windows">True to use PowerShell command bodies, false for POSIX shell.</param>
        /// <returns>Built-in actions, in catalog order.</returns>
        public static List<FleetAction> Build(bool windows)
        {
            List<FleetAction> actions = new List<FleetAction>();

            // Code management.
            actions.Add(Command(FleetActionSeedService.FastForwardKey, "Fast-forward default branch",
                "Runs git pull --ff-only in each working directory. Skips vessels with uncommitted changes; never creates merge commits.",
                "git pull --ff-only", 300, 4, true));
            actions.Add(Command(FleetActionSeedService.PruneMergedBranchesKey, "Prune merged branches",
                "Fetches with --prune, then deletes local branches already merged into the vessel's default branch with git branch -d. Never deletes the current branch or the default branch.",
                windows ? _PruneMergedPowerShell : _PruneMergedPosix, 300, 4, true));
            actions.Add(Command(FleetActionSeedService.RepositoryStatusKey, "Repository status report",
                "Read-only snapshot of each checkout: current branch, last commit, ahead/behind its upstream as of the last fetch, uncommitted changes, local and merged branches, and stashes. Run it before a fleet-wide change.",
                windows ? _RepositoryStatusPowerShell : _RepositoryStatusPosix, 120, 8, false));
            actions.Add(UpdatePackage(windows));
            actions.Add(Mission(FleetActionSeedService.UpgradeRuntimeKey, "Upgrade runtime or framework",
                "Dispatches one voyage per vessel to move the repository to a new runtime or framework version (for example net10.0, Node 22, or Python 3.13), including project files, CI, and container images.",
                "Upgrade the {{vessel.name}} repository to {{input.target}}." + "\n\n"
                + "Detected ecosystems: {{vessel.ecosystems}}" + "\n\n"
                + "Update every place that pins the runtime or framework version: project and build files, version manifests (for "
                + "example global.json, .nvmrc, .python-version, go.mod, rust-toolchain), CI workflows, Dockerfiles and base images, "
                + "and developer documentation. Then fix the compile errors, deprecations, and behavior changes the upgrade causes. "
                + "Update dependencies only where the new version requires it. If the repository already targets {{input.target}} "
                + "or does not use that runtime, make no changes and say so." + "\n\n"
                + _ImplementationRules,
                Inputs(Input("target", "string", true, null, "Runtime or framework version to move to, for example net10.0, Node 22, or Python 3.13."))));
            actions.Add(Mission(FleetActionSeedService.StandardizeFilesKey, "Standardize repository files",
                "Dispatches one voyage per vessel to add or align shared repository files (for example .editorconfig, .gitignore, CI workflow, CODEOWNERS, LICENSE) with a stated standard, without touching code.",
                "Bring these repository files in {{vessel.name}} in line with the standard below: {{input.files}}" + "\n\n"
                + "Standard:" + "\n"
                + "{{input.standard}}" + "\n\n"
                + "Add each file that is missing and align each one that exists, keeping repository-specific entries that do not "
                + "conflict with the standard. Change only the files named above; do not change source code, dependencies, or "
                + "other configuration. If a file already meets the standard, leave it alone. In your final message, list each "
                + "file as added, changed, or already compliant.",
                Inputs(
                    Input("files", "string", true, null, "Files to add or align, for example .editorconfig, .gitignore, .github/workflows/ci.yml."),
                    Input("standard", "string", true, null, "What the files must contain: paste the reference content or describe the rules."))));
            actions.Add(Mission(FleetActionSeedService.SyncDocsKey, "Sync docs with code",
                "Dispatches one voyage per vessel to bring the README, build and run instructions, configuration reference, and API docs in line with the code. Docs only.",
                "Bring the documentation in the {{vessel.name}} repository in line with the code." + "\n\n"
                + "Check the README, getting-started and build instructions, configuration and environment variable references, "
                + "API documentation, and examples against the current code, build files, and CLI or HTTP surface. Fix what is "
                + "wrong or stale, document what is missing, and remove what no longer exists. Verify commands you document by "
                + "reading the build files; do not invent behavior. Change documentation only, not code. In your final message, "
                + "list each document you changed and why.",
                null));

            // Build and test.
            actions.Add(Command(FleetActionSeedService.BuildKey, "Build",
                "Runs the vessel's build command: its definition-of-done build command, else the repository's build script or the default for its detected ecosystem. Vessels where nothing resolves are skipped with reason NoBuildCommand.",
                "{{vessel.buildCommand}}", 1800, 2, false));
            actions.Add(Command(FleetActionSeedService.RunTestsKey, "Run tests",
                "Runs the vessel's test command (its override, the repository's test script, or the default for its detected ecosystem). A failing run records the output tail for Fix failing tests.",
                "{{vessel.testCommand}}", 3600, 2, false));
            actions.Add(Fix(FleetActionSeedService.FixFailingTestsKey, FleetActionSeedService.RunTestsKey, "Fix failing tests",
                "Dispatches one voyage per vessel to make the failing tests pass, citing the vessel's latest Run tests result. Start it from a Run tests run to target only the vessels that failed.",
                "Make the failing tests in the {{vessel.name}} repository pass." + "\n\n"
                + "Latest Run tests result for this repository:" + "\n"
                + "{{scan.summary}}" + "\n\n"
                + "If the result above is empty, run the test suite yourself ({{vessel.testCommand}}) to find the failures. For each "
                + "failure, decide whether the code or the test is wrong: fix the code when it violates the behavior the test "
                + "describes, and fix the test only when the test itself is wrong or out of date. Never delete, skip, or weaken a "
                + "test to make it pass. Run the whole suite before you finish. In your final message, list each failure, its "
                + "root cause, and whether you changed the code or the test." + "\n\n"
                + _ImplementationRules));
            actions.Add(Mission(FleetActionSeedService.AddTestProjectKey, "Add a test project",
                "Dispatches one voyage per vessel to add an automated test project to repositories that have none. Vessels that already have tests get no changes.",
                "Add automated tests to the {{vessel.name}} repository if it does not already have a test project." + "\n\n"
                + "First check for existing tests (test projects, test directories, test scripts, or a configured test command). If "
                + "the repository already has tests, make no changes and say where they are; use Improve test coverage for that "
                + "case." + "\n\n"
                + "Otherwise follow the conventions of the repository's primary language and build system ({{vessel.ecosystems}}). Add "
                + "a test project (or test directory) wired into the existing build so the standard test command runs it, and "
                + "write a small number of meaningful tests that cover existing behavior. Make sure the build and the new tests "
                + "pass. Do not change production code except where a test exposes a real bug, and describe any such change." + "\n\n"
                + "Current health findings for this repository:" + "\n"
                + "{{health.summary}}",
                null));
            actions.Add(Mission(FleetActionSeedService.ImproveCoverageKey, "Improve test coverage",
                "Dispatches one voyage per vessel to add meaningful tests for untested behavior, error paths, and edge cases without changing production code.",
                "Improve the automated test coverage of the {{vessel.name}} repository." + "\n\n"
                + "Area to cover (blank means choose the most important untested code): {{input.area}}" + "\n"
                + "Focus (blank means a balance of edge cases, error paths, and public API): {{input.focus}}" + "\n\n"
                + "Find behavior that has no tests, prioritizing code that is complex, frequently changed, or easy to break. Add "
                + "tests that assert real behavior and would fail if that behavior regressed; do not add tests that only execute "
                + "code. Follow the existing test conventions and frameworks. Do not change production code except where a test "
                + "exposes a real bug, and describe any such change. In your final message, list what is now covered." + "\n\n"
                + _ImplementationRules,
                Inputs(
                    Input("area", "string", false, null, "Optional directory, module, or feature to cover."),
                    Input("focus", "string", false, null, "Optional emphasis, for example edge cases, error paths, or public API."))));

            // Code quality.
            actions.Add(Command(FleetActionSeedService.LintKey, "Lint",
                "Runs the vessel's lint command (its override, the repository's lint script, or the default for its detected ecosystem). A failing run records the output tail for Fix lint findings.",
                "{{vessel.lintCommand}}", 1800, 4, false));
            actions.Add(Fix(FleetActionSeedService.FixLintKey, FleetActionSeedService.LintKey, "Fix lint findings",
                "Dispatches one voyage per vessel to fix the findings in the vessel's latest Lint result. Mechanical fixes only. Start it from a Lint run to target only the vessels that failed.",
                "Fix the lint findings in the {{vessel.name}} repository." + "\n\n"
                + "Latest Lint result for this repository:" + "\n"
                + "{{scan.summary}}" + "\n\n"
                + "If the result above is empty, run the linter yourself ({{vessel.lintCommand}}). Make the smallest change that "
                + "resolves each finding. Do not refactor, rename, or restructure code beyond what a finding requires, do not "
                + "change lint configuration to silence findings, and do not add suppression comments unless a finding is a "
                + "false positive (explain each one). Run the linter again before you finish." + "\n\n"
                + _ImplementationRules));
            actions.Add(Report(FleetActionSeedService.CleanlinessReviewKey, "Code cleanliness review",
                "Report-only: dispatches one read-only voyage per vessel to review dead code, duplication, complexity hotspots, and inconsistent naming and error handling. Changes nothing.",
                "Review the code cleanliness of the {{vessel.name}} repository." + "\n\n"
                + "Look for dead or unreachable code, unused files and dependencies, duplicated logic, overly complex functions "
                + "and classes, inconsistent naming, inconsistent or swallowed error handling, misleading comments, and code that "
                + "breaks the repository's own conventions." + "\n\n"
                + _ReportRules,
                null));
            actions.Add(Mission(FleetActionSeedService.RemoveDeadCodeKey, "Remove dead code and unused dependencies",
                "Dispatches one voyage per vessel to remove code, files, and dependencies nothing uses. The build and tests must still pass.",
                "Remove dead code and unused dependencies from the {{vessel.name}} repository." + "\n\n"
                + "Remove only what you can show is unused: unreferenced private and internal code, unreachable branches, files "
                + "nothing includes, commented-out code, and package references nothing imports. Treat anything public, "
                + "reflection-based, configured by name, or used by tests or tooling as used unless you can prove otherwise. Do "
                + "not change behavior. In your final message, list everything you removed and the evidence that it was unused." + "\n\n"
                + _ImplementationRules,
                null));

            // Performance.
            actions.Add(Report(FleetActionSeedService.PerformanceReviewKey, "Performance review",
                "Report-only: dispatches one read-only voyage per vessel to find likely performance problems, ranked by expected impact with evidence. Changes nothing.",
                "Review the {{vessel.name}} repository for performance problems." + "\n\n"
                + "Focus (blank means the whole repository): {{input.focus}}" + "\n\n"
                + "Look for hot paths doing unnecessary work, N+1 queries and missing indexes or pagination, synchronous I/O or "
                + "sync-over-async on request paths, needless allocations, copies, and serialization, unbounded loops, "
                + "collections, and caches, missing caching of expensive results, and contention on locks or shared resources. "
                + "Rank findings by expected impact and say how you would measure each one." + "\n\n"
                + _ReportRules,
                Inputs(Input("focus", "string", false, null, "Optional area to review, for example startup, API latency, or the import pipeline."))));
            actions.Add(Mission(FleetActionSeedService.ApplyPerformanceFixesKey, "Apply performance fixes",
                "Dispatches one voyage per vessel to apply approved performance findings (paste them from a Performance review) while keeping behavior identical.",
                "Apply these approved performance improvements to the {{vessel.name}} repository:" + "\n\n"
                + "{{input.findings}}" + "\n\n"
                + "Apply only the items listed, and skip any that do not apply to this repository. Keep behavior identical: same "
                + "results, ordering, errors, and public API. Prefer the simplest change that removes the cost. Where the "
                + "repository has benchmarks or performance tests, run them before and after and report the numbers. In your final "
                + "message, list each item as applied or skipped, with the reason." + "\n\n"
                + _ImplementationRules,
                Inputs(Input("findings", "string", true, null, "The approved findings to apply, for example pasted from a Performance review report."))));

            // Security and compliance.
            actions.Add(VulnerabilityAudit());
            actions.Add(Fix(FleetActionSeedService.FixVulnerabilitiesKey, FleetActionSeedService.VulnerabilityAuditKey, "Fix vulnerable dependencies",
                "Dispatches one voyage per vessel to upgrade the packages in the vessel's latest Vulnerability audit, to the smallest version that fixes each advisory. Start it from a Vulnerability audit run to target only the vessels with findings.",
                "Fix the vulnerable dependencies in the {{vessel.name}} repository." + "\n\n"
                + "Latest Vulnerability audit for this repository:" + "\n"
                + "{{scan.summary}}" + "\n\n"
                + "If the audit above is empty, run it yourself ({{vessel.auditCommand}}). For each advisory, upgrade the affected "
                + "package to the smallest version that fixes it, including transitive packages through the package manager's "
                + "override or resolution mechanism when the direct dependency has no fixed release. Regenerate lock files with the "
                + "package manager. Adapt code only where an upgrade requires it. Report any advisory that has no fix and what "
                + "mitigates it." + "\n\n"
                + _ImplementationRules));
            actions.Add(OutdatedReport());
            actions.Add(UpdateDependencies());
            actions.Add(SecretsScan(windows));
            actions.Add(Report(FleetActionSeedService.SecurityReviewKey, "Security review",
                "Report-only: dispatches one read-only voyage per vessel to review the code for security weaknesses, ranked by severity with the exploit path. Changes nothing.",
                "Review the {{vessel.name}} repository for security weaknesses." + "\n\n"
                + "Look for injection (SQL, command, template, path traversal), missing or inconsistent authentication and "
                + "authorization checks, insecure direct object references and tenant isolation gaps, server-side request forgery, "
                + "unsafe deserialization, weak or misused cryptography, secrets in code or configuration, overly broad CORS or "
                + "permissions, missing input validation and output encoding, and sensitive data in logs. For each finding give "
                + "the severity, the exploit path, and the fix." + "\n\n"
                + _ReportRules,
                null));
            actions.Add(LicenseCompliance(windows));

            return actions;
        }

        /// <summary>
        /// Earlier shipped versions of built-ins whose definition later changed. A tenant row that still matches one of
        /// these exactly was never edited, so the seeder replaces it with the current definition.
        /// </summary>
        /// <param name="windows">True for PowerShell command bodies, false for POSIX shell.</param>
        /// <returns>Earlier definitions; a key may appear more than once.</returns>
        public static List<FleetAction> PreviousVersions(bool windows)
        {
            List<FleetAction> actions = new List<FleetAction>();

            // v1.0 through v1.2.
            actions.Add(Command(FleetActionSeedService.BuildKey, "Build",
                "Runs the vessel's definition-of-done build command in its working directory. Vessels without a build command are skipped with reason NoBuildCommand.",
                "{{vessel.buildCommand}}", 1800, 2, false));
            actions.Add(Mission(FleetActionSeedService.UpdateDependenciesKey, "Update outdated dependencies",
                "Dispatches one voyage per vessel asking a captain to update the outdated and vulnerable packages listed in the vessel's health findings.",
                "Update the outdated dependencies in the {{vessel.name}} repository (default branch {{vessel.defaultBranch}})." + "\n\n"
                + "Current health findings for this repository:" + "\n"
                + "{{health.summary}}" + "\n\n"
                + "Update each outdated or vulnerable package to its latest compatible version. Prefer patch and minor updates; "
                + "take a major upgrade only when you can adapt the code safely, and explain any major upgrade you skip. "
                + "Build the solution and run the existing tests after updating, and fix anything the updates break. "
                + "Do not change unrelated code.",
                null));
            actions.Add(Mission(FleetActionSeedService.AddTestProjectKey, "Add a test project",
                "Dispatches one voyage per vessel asking a captain to add an automated test project that follows the repository's conventions.",
                "Add automated tests to the {{vessel.name}} repository if it does not already have a test project." + "\n\n"
                + "Follow the conventions of the repository's primary language and build system. Add a test project (or test "
                + "directory) wired into the existing build so the standard test command runs it, and write a small number of "
                + "meaningful tests that cover existing behavior. Make sure the build and the new tests pass. "
                + "Do not change production code except where a test exposes a real bug, and describe any such change." + "\n\n"
                + "Current health findings for this repository:" + "\n"
                + "{{health.summary}}",
                null));

            return actions;
        }

        #endregion

        #region Private-Members

        private static readonly string _ImplementationRules =
            "Keep the change focused: do not modify unrelated code, configuration, or dependencies. Build the repository and "
            + "run its tests before you finish, and fix anything your change breaks. In your final message, summarize what you "
            + "changed and anything you could not do.";

        private static readonly string _ReportRules =
            "Do not change any files. Deliver your report in Markdown: start with a short summary, then a numbered list of "
            + "findings ranked by severity or impact, each with the file and line, the evidence, and a concrete recommended "
            + "fix. If you find nothing material, say so plainly instead of padding the report.";

        // Manifest files where a package reference can appear, across the ecosystems the catalog detects.
        private static readonly string _ManifestPattern =
            @"(^|/)(([^/]+\.(csproj|fsproj|vbproj|props|targets))|packages\.config|package\.json|requirements[^/]*\.txt|pyproject\.toml|Pipfile|setup\.py|setup\.cfg|go\.mod|Cargo\.toml|Gemfile|composer\.json|pom\.xml|build\.gradle|build\.gradle\.kts|libs\.versions\.toml|pubspec\.yaml|mix\.exs|Package\.swift|vcpkg\.json|conanfile\.txt)$";

        private static readonly string _PruneMergedPosix =
            "git fetch --prune || echo \"git fetch failed; pruning against local refs\"" + "\n"
            + "default_branch=\"{{vessel.defaultBranch}}\"" + "\n"
            + "current_branch=\"$(git symbolic-ref --quiet --short HEAD || true)\"" + "\n"
            + "base=\"origin/$default_branch\"" + "\n"
            + "git rev-parse --verify --quiet \"$base\" >/dev/null || base=\"$default_branch\"" + "\n"
            + "git for-each-ref --format='%(refname:short)' --merged \"$base\" refs/heads | while IFS= read -r branch; do" + "\n"
            + "  if [ \"$branch\" != \"$current_branch\" ] && [ \"$branch\" != \"$default_branch\" ]; then" + "\n"
            + "    git branch -d -- \"$branch\" || echo \"could not delete $branch\"" + "\n"
            + "  fi" + "\n"
            + "done";

        private static readonly string _PruneMergedPowerShell =
            "git fetch --prune; if ($LASTEXITCODE -ne 0) { Write-Output 'git fetch failed; pruning against local refs' }; "
            + "$defaultBranch = '{{vessel.defaultBranch}}'; "
            + "$currentBranch = git symbolic-ref --quiet --short HEAD; "
            + "$base = 'origin/' + $defaultBranch; "
            + "git rev-parse --verify --quiet $base *> $null; if ($LASTEXITCODE -ne 0) { $base = $defaultBranch }; "
            + "git for-each-ref --format='%(refname:short)' --merged $base refs/heads | ForEach-Object { "
            + "if ($_ -ne $currentBranch -and $_ -ne $defaultBranch) { git branch -d -- $_; if ($LASTEXITCODE -ne 0) { Write-Output ('could not delete ' + $_) } } }; "
            + "exit 0";

        private static readonly string _RepositoryStatusPosix =
            "echo \"Branch: $(git symbolic-ref --quiet --short HEAD || echo '(detached HEAD)')\"" + "\n"
            + "echo \"Last commit: $(git log -1 --format='%h %ci %s' 2>/dev/null || echo none)\"" + "\n"
            + "upstream=\"$(git rev-parse --abbrev-ref --symbolic-full-name '@{u}' 2>/dev/null || true)\"" + "\n"
            + "if [ -n \"$upstream\" ]; then" + "\n"
            + "  counts=\"$(git rev-list --left-right --count \"HEAD...$upstream\")\"" + "\n"
            + "  echo \"Ahead/behind $upstream (as of the last fetch): +$(echo \"$counts\" | cut -f1) / -$(echo \"$counts\" | cut -f2)\"" + "\n"
            + "else" + "\n"
            + "  echo \"Upstream: none\"" + "\n"
            + "fi" + "\n"
            + "echo \"Uncommitted changes: $(git status --porcelain | wc -l | tr -d ' ') file(s)\"" + "\n"
            + "echo \"Local branches: $(git for-each-ref refs/heads --format='%(refname:short)' | wc -l | tr -d ' ')\"" + "\n"
            + "base=\"origin/{{vessel.defaultBranch}}\"" + "\n"
            + "git rev-parse --verify --quiet \"$base\" >/dev/null || base=\"{{vessel.defaultBranch}}\"" + "\n"
            + "echo \"Local branches merged into $base: $(git for-each-ref refs/heads --merged \"$base\" --format='%(refname:short)' | grep -vx '{{vessel.defaultBranch}}' | wc -l | tr -d ' ')\"" + "\n"
            + "echo \"Stashes: $(git stash list | wc -l | tr -d ' ')\"" + "\n"
            + "git status --short | head -20";

        private static readonly string _RepositoryStatusPowerShell =
            "$branch = git symbolic-ref --quiet --short HEAD; if (-not $branch) { $branch = '(detached HEAD)' }; Write-Output ('Branch: ' + $branch); "
            + "Write-Output ('Last commit: ' + (git log -1 --format='%h %ci %s')); "
            + "$upstream = git rev-parse --abbrev-ref --symbolic-full-name '@{u}' 2>$null; "
            + "if ($LASTEXITCODE -eq 0 -and $upstream) { $c = (git rev-list --left-right --count ('HEAD...' + $upstream)) -split '\\s+'; Write-Output ('Ahead/behind ' + $upstream + ' (as of the last fetch): +' + $c[0] + ' / -' + $c[1]) } else { Write-Output 'Upstream: none' }; "
            + "Write-Output ('Uncommitted changes: ' + @(git status --porcelain).Count + ' file(s)'); "
            + "Write-Output ('Local branches: ' + @(git for-each-ref refs/heads --format='%(refname:short)').Count); "
            + "$base = 'origin/{{vessel.defaultBranch}}'; git rev-parse --verify --quiet $base *> $null; if ($LASTEXITCODE -ne 0) { $base = '{{vessel.defaultBranch}}' }; "
            + "Write-Output ('Local branches merged into ' + $base + ': ' + @(git for-each-ref refs/heads --merged $base --format='%(refname:short)' | Where-Object { $_ -ne '{{vessel.defaultBranch}}' }).Count); "
            + "Write-Output ('Stashes: ' + @(git stash list).Count); "
            + "git status --short | Select-Object -First 20; exit 0";

        #endregion

        #region Private-Methods

        private static FleetAction Command(string key, string name, string description, string commandText, int timeoutSeconds, int concurrency, bool requiresCleanTree)
        {
            return new FleetAction
            {
                Name = name,
                Description = description,
                Kind = FleetActionKindEnum.Command,
                CommandText = commandText,
                TimeoutSeconds = timeoutSeconds,
                DefaultConcurrency = concurrency,
                RequiresCleanWorkingTree = requiresCleanTree,
                IsBuiltIn = true,
                BuiltInKey = key
            };
        }

        private static FleetAction Mission(string key, string name, string description, string prompt, List<FleetActionInput>? inputs)
        {
            return new FleetAction
            {
                Name = name,
                Description = description,
                Kind = FleetActionKindEnum.Mission,
                PromptTemplate = prompt,
                DefaultConcurrency = 2,
                RequiresCleanWorkingTree = false,
                Inputs = inputs ?? new List<FleetActionInput>(),
                IsBuiltIn = true,
                BuiltInKey = key
            };
        }

        private static FleetAction Fix(string key, string scanKey, string name, string description, string prompt)
        {
            FleetAction action = Mission(key, name, description, prompt, null);
            action.ScanSourceActionKey = scanKey;
            return action;
        }

        private static FleetAction Report(string key, string name, string description, string prompt, List<FleetActionInput>? inputs)
        {
            FleetAction action = Mission(key, name, description, prompt, inputs);
            action.MissionMode = MissionModeEnum.Audit;
            action.DefaultConcurrency = 4;
            return action;
        }

        private static FleetAction UpdatePackage(bool windows)
        {
            FleetAction action = Mission(FleetActionSeedService.UpdatePackageKey, "Update a specific dependency",
                "Dispatches one voyage per vessel that references the package, to move it to a version and adapt the code. Vessels whose manifests do not mention the package are skipped (NotApplicable) before any captain is dispatched.",
                "Update the dependency {{input.package}} in the {{vessel.name}} repository." + "\n\n"
                + "Target version (blank means the latest stable release): {{input.version}}" + "\n"
                + "Release notes and migration guidance (blank means read the package's release notes yourself):" + "\n"
                + "{{input.notes}}" + "\n\n"
                + "Find every reference to {{input.package}}: project and manifest files, central package management, version "
                + "catalogs, and lock files. Update each one to the target version and regenerate lock files with the package "
                + "manager. Adapt the code to breaking changes, following the migration guidance. Do not change other dependencies "
                + "unless the update requires it, and say which ones you changed and why. If the repository does not actually "
                + "depend on {{input.package}}, make no changes and say so." + "\n\n"
                + _ImplementationRules,
                Inputs(
                    Input("package", "package", true, null, "Package identifier as it appears in the manifest, for example Newtonsoft.Json, @scope/name, or com.example:artifact."),
                    Input("version", "string", false, null, "Target version; blank means the latest stable release."),
                    Input("notes", "string", false, null, "Optional release notes, breaking changes, or migration steps to follow.")));
            action.DefaultConcurrency = 4;
            action.ApplicabilityCommand = "pkg='{{input.package}}'" + "\n"
                + "files=\"$(git ls-files 2>/dev/null)\" || exit 0" + "\n"
                + "printf '%s\\n' \"$files\" | grep -E '" + _ManifestPattern + "' | tr '\\n' '\\000' | xargs -0 grep -lqiF -- \"$pkg\" 2>/dev/null";
            action.ApplicabilityCommandWindows = "$pkg = '{{input.package}}'; $files = git ls-files 2>$null; if ($LASTEXITCODE -ne 0) { exit 0 }; "
                + "$pattern = '" + _ManifestPattern + "'; "
                + "foreach ($f in $files) { if ($f -match $pattern -and (Select-String -LiteralPath $f -SimpleMatch -Pattern $pkg -Quiet)) { exit 0 } }; exit 1";
            return action;
        }

        private static FleetAction VulnerabilityAudit()
        {
            FleetAction action = Command(FleetActionSeedService.VulnerabilityAuditKey, "Vulnerability audit",
                "Runs the vessel's dependency audit (its override, the ecosystem's audit tool, or osv-scanner) and parses the advisories into findings. Pair with Fix vulnerable dependencies.",
                "{{vessel.auditCommand}}", 1800, 4, false);
            action.OutputParser = FleetActionOutputParsers.FleetActionParserRegistry.AutoParserId;
            action.RequiresNetwork = true;
            return action;
        }

        private static FleetAction OutdatedReport()
        {
            FleetAction action = Command(FleetActionSeedService.OutdatedReportKey, "Outdated dependencies report",
                "Runs the package manager's outdated report for each vessel (for example dotnet list package --outdated or npm outdated). Update outdated dependencies cites the latest report.",
                "{{vessel.outdatedCommand}}", 900, 4, false);
            action.RequiresNetwork = true;
            return action;
        }

        private static FleetAction UpdateDependencies()
        {
            FleetAction action = Mission(FleetActionSeedService.UpdateDependenciesKey, "Update outdated dependencies",
                "Dispatches one voyage per vessel to update outdated packages within a version policy, citing the vessel's latest Outdated dependencies report and health findings.",
                "Update the outdated dependencies in the {{vessel.name}} repository (default branch {{vessel.defaultBranch}})." + "\n\n"
                + "Version policy: {{input.policy}} (patch: patch releases only; minor: patch and minor releases; major: any "
                + "release, including major upgrades)." + "\n\n"
                + "Latest Outdated dependencies report for this repository (blank means run the package manager's outdated report "
                + "yourself):" + "\n"
                + "{{scan.summary}}" + "\n\n"
                + "Current health findings for this repository:" + "\n"
                + "{{health.summary}}" + "\n\n"
                + "Update each outdated package to the newest version the policy allows, and regenerate lock files with the "
                + "package manager. For a major upgrade, adapt the code to its breaking changes or skip it and explain why. "
                + "Leave packages pinned on purpose (look for comments or documented reasons) alone and say so." + "\n\n"
                + _ImplementationRules,
                Inputs(Input("policy", "enum", true, "minor", "Largest version step to take.", "patch", "minor", "major")));
            action.ScanSourceActionKey = FleetActionSeedService.OutdatedReportKey;
            return action;
        }

        private static FleetAction SecretsScan(bool windows)
        {
            // gitleaks writes its JSON report to a file; print it so the gitleaks-json parser reads it (raw output is redacted).
            string posix = "report=\"$(mktemp)\"" + "\n"
                + "gitleaks detect --source . --no-banner --redact --exit-code 0 --report-format json --report-path \"$report\" >/dev/null 2>&1" + "\n"
                + "code=$?" + "\n"
                + "cat \"$report\"" + "\n"
                + "rm -f \"$report\"" + "\n"
                + "exit $code";
            string powerShell = "$report = New-TemporaryFile; "
                + "gitleaks detect --source . --no-banner --redact --exit-code 0 --report-format json --report-path $report.FullName *> $null; "
                + "$code = $LASTEXITCODE; Get-Content -Raw $report.FullName; Remove-Item $report.FullName; exit $code";
            FleetAction action = Command(FleetActionSeedService.SecretsScanKey, "Secrets scan",
                "Scans each repository for committed secrets with gitleaks and counts them as findings. The raw output is redacted. There is no automatic fix: removing a secret from history and rotating it is a human decision.",
                windows ? powerShell : posix, 900, 4, false);
            action.OutputParser = "gitleaks-json";
            action.RequiredTools = new List<string> { "gitleaks" };
            return action;
        }

        private static FleetAction LicenseCompliance(bool windows)
        {
            string posix = "report=\"$({{vessel.licensesCommand}} 2>&1)\"" + "\n"
                + "status=$?" + "\n"
                + "printf '%s\\n' \"$report\"" + "\n"
                + "[ \"$status\" -eq 0 ] || exit \"$status\"" + "\n"
                + "case '{{input.policy}}' in" + "\n"
                + "  strong-copyleft) pattern='AGPL|SSPL|(^|[^L])GPL' ;;" + "\n"
                + "  copyleft) pattern='AGPL|SSPL|GPL|MPL|EPL|EUPL|CDDL' ;;" + "\n"
                + "  *) exit 0 ;;" + "\n"
                + "esac" + "\n"
                + "if printf '%s\\n' \"$report\" | grep -Eq \"$pattern\"; then" + "\n"
                + "  echo \"License policy {{input.policy}}: disallowed licenses found:\"" + "\n"
                + "  printf '%s\\n' \"$report\" | grep -E \"$pattern\"" + "\n"
                + "  exit 1" + "\n"
                + "fi";
            string powerShell = "$report = & { {{vessel.licensesCommand}} } 2>&1 | Out-String; $code = $LASTEXITCODE; Write-Output $report; "
                + "if ($code -ne 0) { exit $code }; $policy = '{{input.policy}}'; "
                + "$pattern = switch ($policy) { 'strong-copyleft' { 'AGPL|SSPL|(^|[^L])GPL' } 'copyleft' { 'AGPL|SSPL|GPL|MPL|EPL|EUPL|CDDL' } default { $null } }; "
                + "if ($pattern -and ($report -match $pattern)) { Write-Output ('License policy ' + $policy + ': disallowed licenses found.'); exit 1 }; exit 0";
            FleetAction action = Command(FleetActionSeedService.LicenseComplianceKey, "License compliance",
                "Runs the ecosystem's license report and fails the vessel when a license the policy disallows appears. Vessels whose ecosystems have no license report are skipped (NoMatchingEcosystem).",
                windows ? powerShell : posix, 1800, 4, false);
            action.RequiresNetwork = true;
            action.Inputs = Inputs(Input("policy", "enum", true, "strong-copyleft", "strong-copyleft fails on GPL, AGPL, and SSPL; copyleft also fails on LGPL, MPL, EPL, EUPL, and CDDL; report-only never fails.", "strong-copyleft", "copyleft", "report-only"));
            return action;
        }

        private static List<FleetActionInput> Inputs(params FleetActionInput[] inputs)
        {
            return new List<FleetActionInput>(inputs);
        }

        private static FleetActionInput Input(string name, string type, bool required, string? defaultValue, string description, params string[] allowedValues)
        {
            return new FleetActionInput
            {
                Name = name,
                Type = type,
                Required = required,
                Default = defaultValue,
                Description = description,
                AllowedValues = new List<string>(allowedValues)
            };
        }

        #endregion
    }
}
