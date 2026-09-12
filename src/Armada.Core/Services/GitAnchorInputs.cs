namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Pure extraction of git-anchor inputs from a mission's title and description: the repository paths the
    /// mission names, and the notable subject terms whose presence in the tree is worth resolving up front.
    /// Kept side-effect free so it unit-tests without a repository.
    /// </summary>
    public static class GitAnchorInputs
    {
        #region Private-Members

        // A path-ish token: contains a slash and a path segment, or ends in a common source extension.
        private static readonly Regex _PathToken = new Regex(
            @"(?<![\w./-])([\w.-]+/[\w./-]+|[\w-]+\.(?:cs|ts|tsx|js|jsx|py|go|rs|java|rb|cpp|c|h|hpp|md|json|yml|yaml|sql|sh|css|html))(?![\w])",
            RegexOptions.Compiled);

        // A notable identifier: PascalCase / snake_case / kebab-ish word of length >= 4.
        private static readonly Regex _TermToken = new Regex(
            @"(?<![\w])([A-Z][a-zA-Z0-9]{3,}|[a-z][a-z0-9]{3,}(?:_[a-z0-9]+)+|[A-Za-z][A-Za-z0-9]*(?:[A-Z][a-z0-9]+)+)(?![\w])",
            RegexOptions.Compiled);

        private static readonly HashSet<string> _Stopwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "this", "that", "with", "from", "into", "when", "then", "should", "must", "will", "make",
            "update", "create", "delete", "remove", "change", "implement", "mission", "voyage", "captain",
            "vessel", "armada", "using", "based", "which", "their", "there", "these", "those"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Extract distinct path-like tokens from the mission text, in first-seen order, capped at
        /// <paramref name="max"/>.
        /// </summary>
        /// <param name="text">Combined mission title and description.</param>
        /// <param name="max">Maximum number of paths to return.</param>
        /// <returns>Distinct path tokens.</returns>
        public static IReadOnlyList<string> ExtractPaths(string? text, int max = 6)
        {
            List<string> result = new List<string>();
            if (String.IsNullOrWhiteSpace(text)) return result;

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in _PathToken.Matches(text))
            {
                string value = m.Groups[1].Value.Trim().Trim('.', '/', '-');
                if (value.Length < 3) continue;
                if (seen.Add(value)) result.Add(value);
                if (result.Count >= Math.Max(1, max)) break;
            }
            return result;
        }

        /// <summary>
        /// Extract distinct notable subject terms from the mission title (falling back to the description),
        /// in first-seen order, capped at <paramref name="max"/>. Stopwords and short words are dropped.
        /// </summary>
        /// <param name="title">Mission title.</param>
        /// <param name="description">Mission description.</param>
        /// <param name="max">Maximum number of terms to return.</param>
        /// <returns>Distinct subject terms.</returns>
        public static IReadOnlyList<string> ExtractSubjectTerms(string? title, string? description, int max = 4)
        {
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string source in new[] { title ?? String.Empty, description ?? String.Empty })
            {
                foreach (Match m in _TermToken.Matches(source))
                {
                    string value = m.Groups[1].Value.Trim();
                    if (value.Length < 4) continue;
                    if (_Stopwords.Contains(value)) continue;
                    if (seen.Add(value)) result.Add(value);
                    if (result.Count >= Math.Max(1, max)) return result;
                }
            }

            return result;
        }

        #endregion
    }
}
