namespace Armada.Core.Services
{
    using System.Diagnostics;

    /// <summary>
    /// Infers git repository information from a local directory.
    /// </summary>
    public static class GitInference
    {
        #region Public-Methods

        /// <summary>
        /// Check if a directory is inside a git repository.
        /// </summary>
        /// <param name="directory">Directory path to check.</param>
        /// <returns>True if the directory is in a git repo.</returns>
        public static bool IsGitRepository(string directory)
        {
            string? result = RunGit(directory, "rev-parse --is-inside-work-tree");
            return result != null && result.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Get the remote origin URL for a git repository.
        /// </summary>
        /// <param name="directory">Directory inside the repo.</param>
        /// <returns>Remote URL, or null if not found.</returns>
        public static string? GetRemoteUrl(string directory)
        {
            string? result = RunGit(directory, "remote get-url origin");
            return result?.Trim();
        }

        /// <summary>
        /// Get the repository root directory.
        /// </summary>
        /// <param name="directory">Directory inside the repo.</param>
        /// <returns>Root path, or null if not in a repo.</returns>
        public static string? GetRepoRoot(string directory)
        {
            string? result = RunGit(directory, "rev-parse --show-toplevel");
            return result?.Trim();
        }

        /// <summary>
        /// Get the default branch name (main, master, etc.).
        /// </summary>
        /// <param name="directory">Directory inside the repo.</param>
        /// <returns>Default branch name, or "main" as fallback.</returns>
        public static string GetDefaultBranch(string directory)
        {
            // Try to get the remote HEAD
            string? result = RunGit(directory, "symbolic-ref refs/remotes/origin/HEAD");
            if (!string.IsNullOrWhiteSpace(result))
            {
                string branch = result.Trim();
                // refs/remotes/origin/main -> main
                int lastSlash = branch.LastIndexOf('/');
                if (lastSlash >= 0) return branch.Substring(lastSlash + 1);
                return branch;
            }

            // Fallback: check if main or master exists
            string? branches = RunGit(directory, "branch -r");
            if (branches != null)
            {
                if (branches.Contains("origin/main")) return "main";
                if (branches.Contains("origin/master")) return "master";
            }

            return "main";
        }

        /// <summary>
        /// Infer a vessel name from a remote URL.
        /// </summary>
        /// <param name="remoteUrl">Git remote URL.</param>
        /// <returns>Short name for the repository.</returns>
        public static string InferVesselName(string remoteUrl)
        {
            string name = remoteUrl;

            // Strip .git suffix
            if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);

            // Strip trailing slash
            name = name.TrimEnd('/');

            // Get the last path segment (repo name)
            int lastSlash = name.LastIndexOf('/');
            if (lastSlash >= 0)
                name = name.Substring(lastSlash + 1);

            // Handle SSH colon format: git@github.com:user/repo
            int colonIndex = name.LastIndexOf(':');
            if (colonIndex >= 0)
                name = name.Substring(colonIndex + 1);

            return string.IsNullOrEmpty(name) ? "unnamed" : name;
        }

        #endregion

        #region Private-Methods

        private static string? RunGit(string workingDirectory, string arguments)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using Process? process = Process.Start(startInfo);
                if (process == null) return null;

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);

                return process.ExitCode == 0 ? output : null;
            }
            catch
            {
                return null;
            }
        }

        #endregion
    }
}
