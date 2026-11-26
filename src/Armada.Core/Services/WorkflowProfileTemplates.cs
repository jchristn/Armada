namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// The workflow profiles Armada creates out of the box: one per common toolchain (.NET, Node.js, Python, Go, Rust,
    /// Maven, Gradle) with that ecosystem's usual lint, build, test, and package commands, and a generic profile for a
    /// repository Armada cannot recognize, whose only command is a whitespace and conflict-marker check. <see cref="DefaultWorkflowProfileProvisioner"/> picks one from a
    /// vessel's files and attaches it to the vessel. Thread-safe.
    /// </summary>
    public static class WorkflowProfileTemplates
    {
        #region Public-Members

        /// <summary>Template key for .NET (a solution or project file).</summary>
        public const string DotNet = "dotnet";

        /// <summary>Template key for Node.js (package.json).</summary>
        public const string Node = "node";

        /// <summary>Template key for Python (pyproject.toml, setup.py, or requirements.txt).</summary>
        public const string Python = "python";

        /// <summary>Template key for Go (go.mod).</summary>
        public const string Go = "go";

        /// <summary>Template key for Rust (Cargo.toml).</summary>
        public const string Rust = "rust";

        /// <summary>Template key for Java with Maven (pom.xml).</summary>
        public const string Maven = "maven";

        /// <summary>Template key for Java or Kotlin with Gradle (build.gradle or build.gradle.kts).</summary>
        public const string Gradle = "gradle";

        /// <summary>Template key for a repository Armada does not recognize (no commands).</summary>
        public const string Generic = "generic";

        /// <summary>
        /// Validation advice for .NET repositories, added to mission instructions when the vessel's workflow profile is
        /// .NET. Restoring is a separate step in a fresh worktree, so a failed restore must not read as broken source.
        /// </summary>
        public const string DotNetValidationGuidance =
            "Restore dependencies before validation in a fresh worktree. For .NET, omit --no-restore until restore succeeds. " +
            "A missing project.assets.json means the check is unverified, not that the source is broken.";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validation advice for the mission instructions from a vessel's active workflow profiles, or an empty string.
        /// </summary>
        /// <param name="profiles">The vessel's profiles; null or empty yields no advice.</param>
        /// <returns>Advice text.</returns>
        public static string ValidationGuidance(IEnumerable<WorkflowProfile>? profiles)
        {
            if (profiles == null) return String.Empty;
            bool dotnet = profiles.Any(p => p != null && p.Active
                && (String.Equals(p.SourceTemplate, DotNet, StringComparison.OrdinalIgnoreCase)
                    || p.LanguageHints.Any(h => String.Equals(h, ".NET", StringComparison.OrdinalIgnoreCase))));
            return dotnet ? DotNetValidationGuidance : String.Empty;
        }

        /// <summary>
        /// The template key for a repository layout, or <see cref="Generic"/> when nothing is recognized.
        /// </summary>
        /// <param name="layout">Layout, or null when the files could not be read.</param>
        /// <returns>Key.</returns>
        public static string Match(RepositoryLayout? layout)
        {
            if (layout == null || layout.Paths.Count == 0) return Generic;
            if (layout.Shallowest(".sln") != null || layout.Shallowest(".slnx") != null || layout.Shallowest(".csproj") != null || layout.Shallowest(".fsproj") != null) return DotNet;
            if (layout.HasRootFile("package.json")) return Node;
            if (layout.HasRootFile("go.mod")) return Go;
            if (layout.HasRootFile("Cargo.toml")) return Rust;
            if (layout.HasRootFile("pom.xml")) return Maven;
            if (layout.HasRootFile("build.gradle") || layout.HasRootFile("build.gradle.kts")) return Gradle;
            if (layout.HasRootFile("pyproject.toml") || layout.HasRootFile("setup.py") || layout.HasRootFile("requirements.txt")) return Python;
            return Generic;
        }

        /// <summary>
        /// Build the profile for a vessel from a template: scoped to the vessel, the vessel's default, tenant-wide, and
        /// marked with <see cref="WorkflowProfile.SourceTemplate"/> so Armada can upgrade a generic one later.
        /// </summary>
        /// <param name="vessel">Vessel.</param>
        /// <param name="layout">Layout used to fill in paths (for example the .NET solution), or null.</param>
        /// <param name="key">Template key from <see cref="Match"/>.</param>
        /// <returns>Profile, not yet stored.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="vessel"/> is null.</exception>
        public static WorkflowProfile Build(Vessel vessel, RepositoryLayout? layout, string key)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            WorkflowProfile profile = new WorkflowProfile
            {
                TenantId = vessel.TenantId,
                UserId = vessel.UserId,
                OwnershipScope = ScopeEnum.TenantWide,
                Scope = WorkflowProfileScopeEnum.Vessel,
                VesselId = vessel.Id,
                IsDefault = true,
                Active = true,
                SourceTemplate = key
            };

            string evidence = layout == null || String.IsNullOrWhiteSpace(layout.Source) ? "" : " (read from " + layout.Source + ")";
            switch (key)
            {
                case DotNet:
                    string target = layout?.Shallowest(".sln") ?? layout?.Shallowest(".slnx") ?? layout?.Shallowest(".csproj") ?? layout?.Shallowest(".fsproj") ?? "";
                    string quoted = target.Length > 0 ? " \"" + target + "\"" : "";
                    profile.Name = vessel.Name + ": .NET";
                    profile.LanguageHints = new List<string> { "C#", ".NET" };
                    profile.LintCommand = "dotnet format" + quoted + " --verify-no-changes";
                    profile.BuildCommand = "dotnet build" + quoted + " -c Release";
                    profile.UnitTestCommand = layout?.Paths.Contains("src/Test.Xunit/Test.Xunit.csproj") == true
                        && layout.Paths.Contains("src/Test.Automated/Test.Automated.csproj")
                        && layout.Paths.Contains("src/Armada.sln")
                        && layout.Paths.Contains("src/Armada.Core/Armada.Core.csproj")
                        ? "dotnet run --project \"src/Test.Automated\" -c Release -f net10.0 -- --suites Services"
                        : "dotnet test" + quoted + " -c Release";
                    if (layout?.Paths.Contains("src/Armada.Core/Armada.Core.csproj") == true
                        && layout.Paths.Contains("src/Test.Automated/Test.Automated.csproj"))
                        profile.IntegrationTestCommand = LandingVerification.ArmadaCommand;
                    profile.PackageCommand = "dotnet pack" + quoted + " -c Release -o artifacts";
                    profile.ExpectedArtifacts = new List<string> { "artifacts/*.nupkg" };
                    profile.Description = "Created by Armada for a .NET repository" + (target.Length > 0 ? " built from " + target : "") + evidence + ". Edit the commands to match how this project is built and tested.";
                    break;
                case Node:
                    profile.Name = vessel.Name + ": Node.js";
                    profile.LanguageHints = new List<string> { "JavaScript", "TypeScript" };
                    profile.LintCommand = "npm run lint --if-present";
                    profile.BuildCommand = "npm ci && npm run build --if-present";
                    profile.UnitTestCommand = "npm test --if-present";
                    profile.PackageCommand = "npm pack";
                    profile.Description = "Created by Armada for a Node.js repository (package.json)" + evidence + ". Edit the commands to match this project's scripts.";
                    break;
                case Python:
                    profile.Name = vessel.Name + ": Python";
                    profile.LanguageHints = new List<string> { "Python" };
                    profile.LintCommand = "python -m ruff check .";
                    profile.BuildCommand = layout != null && layout.HasRootFile("requirements.txt") ? "python -m pip install -r requirements.txt" : "python -m pip install -e .";
                    profile.UnitTestCommand = "python -m pytest";
                    profile.PackageCommand = layout != null && layout.HasRootFile("pyproject.toml") ? "python -m build" : null;
                    profile.Description = "Created by Armada for a Python repository" + evidence + ". Edit the commands to match this project's tooling.";
                    break;
                case Go:
                    profile.Name = vessel.Name + ": Go";
                    profile.LanguageHints = new List<string> { "Go" };
                    profile.LintCommand = "go vet ./...";
                    profile.BuildCommand = "go build ./...";
                    profile.UnitTestCommand = "go test ./...";
                    profile.Description = "Created by Armada for a Go module (go.mod)" + evidence + ". Edit the commands if this project builds differently.";
                    break;
                case Rust:
                    profile.Name = vessel.Name + ": Rust";
                    profile.LanguageHints = new List<string> { "Rust" };
                    profile.LintCommand = "cargo clippy";
                    profile.BuildCommand = "cargo build --release";
                    profile.UnitTestCommand = "cargo test";
                    profile.PackageCommand = "cargo package";
                    profile.Description = "Created by Armada for a Rust crate (Cargo.toml)" + evidence + ". Edit the commands if this project builds differently.";
                    break;
                case Maven:
                    profile.Name = vessel.Name + ": Maven";
                    profile.LanguageHints = new List<string> { "Java" };
                    profile.BuildCommand = "mvn -B package -DskipTests";
                    profile.UnitTestCommand = "mvn -B test";
                    profile.PackageCommand = "mvn -B package";
                    profile.ExpectedArtifacts = new List<string> { "target/*.jar" };
                    profile.Description = "Created by Armada for a Maven project (pom.xml)" + evidence + ". Edit the commands if this project builds differently.";
                    break;
                case Gradle:
                    bool wrapper = layout != null && layout.HasRootFile("gradlew");
                    string gradle = wrapper ? "./gradlew" : "gradle";
                    profile.Name = vessel.Name + ": Gradle";
                    profile.LanguageHints = new List<string> { "Java", "Kotlin" };
                    profile.BuildCommand = gradle + " build -x test";
                    profile.UnitTestCommand = gradle + " test";
                    profile.PackageCommand = gradle + " assemble";
                    profile.Description = "Created by Armada for a Gradle project" + evidence + ". Edit the commands if this project builds differently.";
                    break;
                default:
                    profile.Name = vessel.Name + ": basic checks";
                    profile.LintCommand = "git diff --check";
                    profile.SourceTemplate = Generic;
                    profile.Description = "Created by Armada because no recognizable build files were found" + evidence
                        + ". Its only command (git diff --check) fails on conflict markers and whitespace errors in uncommitted changes. Missions dispatch normally; add this vessel's build, test, and package commands here to enable real checks and deployments. Armada replaces this profile automatically once it can recognize the repository.";
                    break;
            }

            return profile;
        }

        #endregion
    }
}
