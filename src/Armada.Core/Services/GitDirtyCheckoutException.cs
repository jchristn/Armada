namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Raised when Armada refuses to use a git checkout because tracked files are modified (worktree creation or a
    /// local landing merge). Carries the checkout path and the modified paths so callers do not parse the message.
    /// </summary>
    public class GitDirtyCheckoutException : InvalidOperationException
    {
        #region Public-Members

        /// <summary>
        /// The checkout (worktree or repository) path that is dirty.
        /// </summary>
        public string CheckoutPath { get; }

        /// <summary>
        /// The modified tracked paths reported by <c>git status --porcelain</c>.
        /// </summary>
        public IReadOnlyList<string> ModifiedPaths { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="checkoutPath">The dirty checkout path.</param>
        /// <param name="porcelainStatus">Output of <c>git status --porcelain --untracked-files=no</c>.</param>
        public GitDirtyCheckoutException(string checkoutPath, string porcelainStatus)
            : base("Git checkout " + checkoutPath + " contains tracked modifications: " + (porcelainStatus ?? String.Empty).Trim())
        {
            CheckoutPath = checkoutPath ?? String.Empty;
            ModifiedPaths = ParsePaths(porcelainStatus);
        }

        #endregion

        #region Private-Methods

        private static List<string> ParsePaths(string? porcelainStatus)
        {
            List<string> paths = new List<string>();
            if (String.IsNullOrEmpty(porcelainStatus)) return paths;
            foreach (string rawLine in porcelainStatus.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');
                // Porcelain v1: two status characters, a space, then the path (renames: "old -> new").
                if (line.Length < 4) continue;
                string path = line.Substring(3);
                int arrow = path.IndexOf(" -> ", StringComparison.Ordinal);
                if (arrow >= 0) path = path.Substring(arrow + 4);
                if (path.Length > 1 && path[0] == '"' && path[path.Length - 1] == '"') path = path.Substring(1, path.Length - 2);
                paths.Add(path);
            }

            return paths;
        }

        #endregion
    }
}
