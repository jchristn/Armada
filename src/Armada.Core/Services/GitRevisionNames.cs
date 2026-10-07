namespace Armada.Core.Services
{
    using System;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Validation for branch names and commit ids taken from requests before they reach a git command line.
    /// Pure and side-effect free.
    /// </summary>
    public static class GitRevisionNames
    {
        #region Private-Members

        private static readonly Regex _CommitIdPattern = new Regex("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a branch name is safe to pass to git as a revision: non-empty, at most 255 characters, not starting
        /// with '-' (never read as an option) or '/', and free of whitespace, control characters, "..", "@{", and the
        /// characters git forbids in ref names (~ ^ : ? * [ \).
        /// </summary>
        /// <param name="name">Branch name.</param>
        /// <returns>True when safe.</returns>
        public static bool IsSafeBranchName(string? name)
        {
            if (String.IsNullOrEmpty(name)) return false;
            if (name.Length > 255) return false;
            if (name[0] == '-' || name[0] == '/') return false;
            if (name.EndsWith("/", StringComparison.Ordinal) || name.EndsWith(".", StringComparison.Ordinal)) return false;
            if (name.Contains("..", StringComparison.Ordinal) || name.Contains("@{", StringComparison.Ordinal) || name.Contains("//", StringComparison.Ordinal)) return false;
            foreach (char c in name)
            {
                if (c <= ' ' || c == 0x7f) return false;
                if (c == '~' || c == '^' || c == ':' || c == '?' || c == '*' || c == '[' || c == '\\') return false;
            }

            return true;
        }

        /// <summary>
        /// Whether a value is a full commit id (40 lowercase hex characters for SHA-1, 64 for SHA-256).
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>True for a full commit id.</returns>
        public static bool IsFullCommitId(string? value)
        {
            if (String.IsNullOrEmpty(value)) return false;
            return _CommitIdPattern.IsMatch(value);
        }

        #endregion
    }
}
