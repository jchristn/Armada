namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;

    /// <summary>
    /// What a Judge PASS must cite (RS-6.8): at least one check the Judge ran or one file it inspected. A PASS that cites
    /// neither is a format failure. The format-only repair turn uses the same readers to confirm that a restatement cites
    /// nothing the original output did not.
    /// </summary>
    public static class JudgeEvidence
    {
        #region Public-Members

        /// <summary>
        /// Why a PASS without evidence is rejected.
        /// </summary>
        public const string MissingEvidenceReason = "Judge PASS verdict cites no evidence: name the checks you ran or at least one file you inspected";

        #endregion

        #region Private-Members

        private const string SourceExtensions =
            "cs|csproj|sln|slnx|props|targets|fs|vb|ts|tsx|js|jsx|mjs|cjs|py|go|rs|java|kt|kts|swift|rb|php|c|h|cc|cpp|hpp|m|mm|"
            + "sql|json|jsonc|ya?ml|toml|xml|xaml|axaml|md|sh|bash|ps1|psm1|bat|cmd|html|css|scss|less|vue|svelte|gradle|ini|cfg|"
            + "conf|proto|graphql|tf|dockerfile|razor|cshtml|txt|lock";

        private static readonly Regex _FileReference = new Regex(
            @"(?<![\w/\\.-])(?<path>(?:[\w.-]+[/\\])*[\w-][\w.-]*\.(?:" + SourceExtensions + @"))(?::\d+(?::\d+)?)?(?![\w-])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex _CheckCommand = new Regex(
            @"\b(?:dotnet\s+(?:build|test|run|format)|npm\s+(?:test|run|ci)|npx\s+\w+|pnpm\s+\w+|yarn\s+(?:test|build|lint|run)"
            + @"|pytest|python3?\s+-m\s+\w+|go\s+(?:test|build|vet)|cargo\s+(?:test|build|check|clippy)|mvn\s+\w+|gradlew?\s+\w+"
            + @"|make\s+\w+|tsc\b|eslint\b|ruff\b|mypy\b|jest\b|vitest\b|git\s+(?:diff|log|show|status|grep)|rg\s+\S|grep\s+\S)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        #endregion

        #region Public-Methods

        /// <summary>
        /// True when the output cites at least one check it ran or one file it inspected.
        /// </summary>
        /// <param name="output">Judge output.</param>
        /// <returns>True when evidence is cited.</returns>
        public static bool HasPassEvidence(string? output)
        {
            if (String.IsNullOrWhiteSpace(output)) return false;
            return CitesCheck(output) || _FileReference.IsMatch(output);
        }

        /// <summary>
        /// True when the output names a build, test, lint or inspection command.
        /// </summary>
        /// <param name="output">Output text.</param>
        /// <returns>True when a check is named.</returns>
        public static bool CitesCheck(string? output)
        {
            if (String.IsNullOrWhiteSpace(output)) return false;
            return _CheckCommand.IsMatch(output);
        }

        /// <summary>
        /// The file paths the output cites, without line numbers, in order of first appearance.
        /// </summary>
        /// <param name="output">Output text.</param>
        /// <returns>Distinct file paths (compared case-insensitively).</returns>
        public static List<string> FileReferences(string? output)
        {
            List<string> paths = new List<string>();
            if (String.IsNullOrWhiteSpace(output)) return paths;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in _FileReference.Matches(output))
            {
                string path = match.Groups["path"].Value.Replace('\\', '/');
                if (seen.Add(path)) paths.Add(path);
            }

            return paths;
        }

        #endregion
    }
}
