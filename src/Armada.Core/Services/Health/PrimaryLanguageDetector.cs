namespace Armada.Core.Services.Health
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Picks a repository's primary language as the language with the most source files in the inventory, and counts
    /// recognized projects. Language names are stable codes (CSharp, FSharp, VisualBasic, TypeScript, JavaScript,
    /// Python, Go, Rust, Java, Kotlin, Ruby, PHP, Swift, C, Cpp). Stateless and thread-safe.
    /// </summary>
    public static class PrimaryLanguageDetector
    {
        #region Private-Members

        private static readonly Dictionary<string, string> _ExtensionLanguages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".cs", "CSharp" }, { ".fs", "FSharp" }, { ".vb", "VisualBasic" },
            { ".ts", "TypeScript" }, { ".tsx", "TypeScript" },
            { ".js", "JavaScript" }, { ".jsx", "JavaScript" }, { ".mjs", "JavaScript" }, { ".cjs", "JavaScript" },
            { ".py", "Python" }, { ".go", "Go" }, { ".rs", "Rust" }, { ".java", "Java" }, { ".kt", "Kotlin" },
            { ".rb", "Ruby" }, { ".php", "PHP" }, { ".swift", "Swift" },
            { ".c", "C" }, { ".h", "C" }, { ".cpp", "Cpp" }, { ".cc", "Cpp" }, { ".hpp", "Cpp" }
        };

        private static readonly string[] _ProjectExtensions = new string[] { ".csproj", ".fsproj", ".vbproj" };

        private static readonly string[] _ProjectFileNames = new string[] { "package.json", "pyproject.toml", "setup.py", "go.mod", "Cargo.toml", "pom.xml", "build.gradle", "build.gradle.kts", "Gemfile", "composer.json", "Package.swift" };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Detect the primary language.
        /// </summary>
        /// <param name="inventory">Repository inventory.</param>
        /// <returns>The language code, or null when no source files were recognized.</returns>
        /// <exception cref="ArgumentNullException">Thrown when inventory is null.</exception>
        public static string? Detect(RepositoryFileInventory inventory)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string file in inventory.Files)
            {
                string name = RepositoryFileInventory.GetFileName(file);
                int dot = name.LastIndexOf('.');
                if (dot < 0) continue;
                if (!_ExtensionLanguages.TryGetValue(name.Substring(dot), out string? language)) continue;
                counts[language] = counts.TryGetValue(language, out int current) ? current + 1 : 1;
            }

            if (counts.Count == 0) return null;
            return counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).First().Key;
        }

        /// <summary>
        /// Count recognized project manifests (.NET project files, package.json, pyproject.toml, setup.py, go.mod,
        /// Cargo.toml, pom.xml, build.gradle, Gemfile, composer.json, Package.swift).
        /// </summary>
        /// <param name="inventory">Repository inventory.</param>
        /// <returns>The project count.</returns>
        /// <exception cref="ArgumentNullException">Thrown when inventory is null.</exception>
        public static int CountProjects(RepositoryFileInventory inventory)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            int count = 0;
            foreach (string file in inventory.Files)
            {
                string name = RepositoryFileInventory.GetFileName(file);
                if (_ProjectExtensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase))) count++;
                else if (_ProjectFileNames.Any(n => String.Equals(n, name, StringComparison.OrdinalIgnoreCase))) count++;
            }

            return count;
        }

        #endregion
    }
}
