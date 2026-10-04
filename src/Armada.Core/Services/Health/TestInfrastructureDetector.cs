namespace Armada.Core.Services.Health
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using Armada.Core.Services.Health.Json;

    /// <summary>
    /// Per-ecosystem test infrastructure detection that does not depend on any one author's conventions:
    /// .NET (references to Microsoft.NET.Test.Sdk, xunit*, NUnit*, MSTest*, or Touchstone*), Node (jest, vitest, mocha,
    /// or playwright in dependencies, or a test script other than npm's placeholder), Python (pytest.ini,
    /// [tool.pytest] in pyproject.toml, [tool:pytest] in setup.cfg, tox.ini, conftest.py, or a tests directory), Go
    /// (any *_test.go file), and Rust (a tests directory or #[cfg(test)] in a source file). Stateless and thread-safe.
    /// </summary>
    public static class TestInfrastructureDetector
    {
        #region Public-Members

        /// <summary>
        /// Maximum number of Rust source files read when looking for #[cfg(test)]. Default 200.
        /// </summary>
        public static int MaxRustFilesScanned { get; set; } = 200;

        #endregion

        #region Private-Members

        private static readonly Regex _DotNetTestReference = new Regex(
            "Include\\s*=\\s*\"(Microsoft\\.NET\\.Test\\.Sdk|xunit[^\"]*|NUnit[^\"]*|MSTest[^\"]*|Touchstone[^\"]*)\"",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex _IsTestProjectProperty = new Regex(
            "<IsTestProject>\\s*true\\s*</IsTestProject>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] _NodeTestPackages = new string[] { "jest", "vitest", "mocha", "playwright", "@playwright/test" };

        private static readonly string[] _PythonProjectFiles = new string[] { "pyproject.toml", "setup.py", "setup.cfg", "requirements.txt", "Pipfile" };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run every detector over an inventory.
        /// </summary>
        /// <param name="inventory">Repository inventory (file contents are read when it was scanned from disk).</param>
        /// <returns>The detection result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when inventory is null.</exception>
        public static TestDetectionResult Detect(RepositoryFileInventory inventory)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            TestDetectionResult result = new TestDetectionResult();
            DetectDotNet(inventory, result);
            DetectNode(inventory, result);
            DetectPython(inventory, result);
            DetectGo(inventory, result);
            DetectRust(inventory, result);
            return result;
        }

        /// <summary>
        /// Whether a .NET project file is a test project: it references a test framework or test SDK, or sets
        /// IsTestProject. When the content is unavailable, a project name containing "Test" counts.
        /// </summary>
        /// <param name="projectPath">Relative project path.</param>
        /// <param name="content">Project file content, or null when unavailable.</param>
        /// <returns>True for a test project.</returns>
        public static bool IsDotNetTestProject(string projectPath, string? content)
        {
            if (content != null) return _DotNetTestReference.IsMatch(content) || _IsTestProjectProperty.IsMatch(content);
            return RepositoryFileInventory.GetFileName(projectPath ?? "").Contains("Test", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether a package.json declares tests: a known test framework in dependencies or devDependencies, or a test
        /// script that is not npm's default "no test specified" placeholder.
        /// </summary>
        /// <param name="manifest">Parsed package.json.</param>
        /// <returns>True when tests are declared.</returns>
        /// <exception cref="ArgumentNullException">Thrown when manifest is null.</exception>
        public static bool NodeManifestHasTests(PackageJsonManifest manifest)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            foreach (string package in _NodeTestPackages)
            {
                if (manifest.DevDependencies != null && manifest.DevDependencies.ContainsKey(package)) return true;
                if (manifest.Dependencies != null && manifest.Dependencies.ContainsKey(package)) return true;
            }

            if (manifest.Scripts != null && manifest.Scripts.TryGetValue("test", out string? script) && !String.IsNullOrWhiteSpace(script))
            {
                if (!script.Contains("no test specified", StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        /// <summary>
        /// Parse package.json text.
        /// </summary>
        /// <param name="json">package.json text.</param>
        /// <returns>The manifest, or null when the text is not a valid package.json.</returns>
        public static PackageJsonManifest? ParsePackageJson(string? json)
        {
            if (String.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<PackageJsonManifest>(json, DependencyJson.Options);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        #endregion

        #region Private-Methods

        private static void DetectDotNet(RepositoryFileInventory inventory, TestDetectionResult result)
        {
            List<string> projects = inventory.FindByExtension(".csproj")
                .Concat(inventory.FindByExtension(".fsproj"))
                .Concat(inventory.FindByExtension(".vbproj"))
                .ToList();
            if (projects.Count == 0) return;
            result.RecognizedProjects += projects.Count;
            result.RecognizedEcosystems.Add("DotNet");
            int tests = projects.Count(p => IsDotNetTestProject(p, inventory.ReadText(p)));
            if (tests > 0)
            {
                result.TestIndicators += tests;
                result.EcosystemsWithTests.Add("DotNet");
            }
        }

        private static void DetectNode(RepositoryFileInventory inventory, TestDetectionResult result)
        {
            List<string> manifests = inventory.FindByFileName("package.json");
            if (manifests.Count == 0) return;
            result.RecognizedProjects += manifests.Count;
            result.RecognizedEcosystems.Add("Node");
            int tests = 0;
            foreach (string manifestPath in manifests)
            {
                PackageJsonManifest? manifest = ParsePackageJson(inventory.ReadText(manifestPath));
                if (manifest != null && NodeManifestHasTests(manifest)) tests++;
            }

            if (tests > 0)
            {
                result.TestIndicators += tests;
                result.EcosystemsWithTests.Add("Node");
            }
        }

        private static void DetectPython(RepositoryFileInventory inventory, TestDetectionResult result)
        {
            HashSet<string> projectDirectories = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in _PythonProjectFiles)
            {
                foreach (string file in inventory.FindByFileName(name)) projectDirectories.Add(RepositoryFileInventory.GetDirectory(file));
            }

            if (projectDirectories.Count == 0) return;
            result.RecognizedProjects += projectDirectories.Count;
            result.RecognizedEcosystems.Add("Python");

            int indicators = 0;
            indicators += inventory.FindByFileName("pytest.ini").Count;
            indicators += inventory.FindByFileName("tox.ini").Count;
            indicators += inventory.FindByFileName("conftest.py").Count;
            foreach (string pyproject in inventory.FindByFileName("pyproject.toml"))
            {
                string? text = inventory.ReadText(pyproject);
                if (text != null && text.Contains("[tool.pytest", StringComparison.OrdinalIgnoreCase)) indicators++;
            }

            foreach (string setupCfg in inventory.FindByFileName("setup.cfg"))
            {
                string? text = inventory.ReadText(setupCfg);
                if (text != null && text.Contains("[tool:pytest]", StringComparison.OrdinalIgnoreCase)) indicators++;
            }

            if (inventory.HasDirectoryNamed("tests")) indicators++;
            if (indicators > 0)
            {
                result.TestIndicators += indicators;
                result.EcosystemsWithTests.Add("Python");
            }
        }

        private static void DetectGo(RepositoryFileInventory inventory, TestDetectionResult result)
        {
            List<string> modules = inventory.FindByFileName("go.mod");
            if (modules.Count == 0) return;
            result.RecognizedProjects += modules.Count;
            result.RecognizedEcosystems.Add("Go");
            int tests = inventory.Files.Count(f => f.EndsWith("_test.go", StringComparison.OrdinalIgnoreCase));
            if (tests > 0)
            {
                result.TestIndicators += tests;
                result.EcosystemsWithTests.Add("Go");
            }
        }

        private static void DetectRust(RepositoryFileInventory inventory, TestDetectionResult result)
        {
            List<string> crates = inventory.FindByFileName("Cargo.toml");
            if (crates.Count == 0) return;
            result.RecognizedProjects += crates.Count;
            result.RecognizedEcosystems.Add("Rust");

            int indicators = 0;
            HashSet<string> crateDirectories = new HashSet<string>(crates.Select(RepositoryFileInventory.GetDirectory), StringComparer.Ordinal);
            foreach (string directory in crateDirectories)
            {
                string prefix = directory.Length == 0 ? "tests/" : directory + "/tests/";
                if (inventory.Files.Any(f => f.StartsWith(prefix, StringComparison.Ordinal))) indicators++;
            }

            if (indicators == 0)
            {
                int scanned = 0;
                foreach (string source in inventory.FindByExtension(".rs"))
                {
                    if (scanned++ >= MaxRustFilesScanned) break;
                    string? text = inventory.ReadText(source, 262144);
                    if (text != null && text.Contains("#[cfg(test)]", StringComparison.Ordinal)) { indicators++; break; }
                }
            }

            if (indicators > 0)
            {
                result.TestIndicators += indicators;
                result.EcosystemsWithTests.Add("Rust");
            }
        }

        #endregion
    }
}
