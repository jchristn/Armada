namespace Armada.Core.Services
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// The git exclude file (info/exclude) of the repository a worktree belongs to. git reads info/exclude only from
    /// the shared repository directory, so an entry written into a linked worktree's private git directory would be
    /// ignored; this resolves the shared one.
    /// </summary>
    public static class GitExcludeFile
    {
        #region Public-Methods

        /// <summary>
        /// The info/exclude path for a checkout or linked worktree on this machine, found by reading its .git folder or
        /// .git file and the linked worktree's commondir pointer. Null when the path is not a git checkout.
        /// </summary>
        /// <param name="worktreePath">Checkout or worktree path.</param>
        /// <returns>The exclude file path, or null.</returns>
        public static string? ResolvePath(string worktreePath)
        {
            if (String.IsNullOrEmpty(worktreePath)) return null;

            string gitPath = Path.Combine(worktreePath, ".git");
            if (Directory.Exists(gitPath))
            {
                return Path.Combine(gitPath, "info", "exclude");
            }

            if (!File.Exists(gitPath))
            {
                return null;
            }

            string gitPointer = File.ReadAllText(gitPath).Trim();
            const string prefix = "gitdir:";
            if (!gitPointer.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string gitDir = gitPointer.Substring(prefix.Length).Trim();
            if (!Path.IsPathRooted(gitDir))
            {
                gitDir = Path.GetFullPath(Path.Combine(worktreePath, gitDir));
            }

            // A linked worktree's private git directory names the shared repository directory in "commondir".
            string commonDirPointer = Path.Combine(gitDir, "commondir");
            if (File.Exists(commonDirPointer))
            {
                string commonDir = File.ReadAllText(commonDirPointer).Trim();
                if (!String.IsNullOrEmpty(commonDir))
                {
                    if (!Path.IsPathRooted(commonDir))
                    {
                        commonDir = Path.GetFullPath(Path.Combine(gitDir, commonDir));
                    }

                    return Path.Combine(commonDir, "info", "exclude");
                }
            }

            return Path.Combine(gitDir, "info", "exclude");
        }

        /// <summary>
        /// Append a line to an exclude file unless the file already has it, creating the file and its folder as needed.
        /// </summary>
        /// <param name="excludePath">Exclude file path.</param>
        /// <param name="entry">The line (an ignore pattern).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public static async Task AddEntryAsync(string excludePath, string entry, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(excludePath)) return;
            if (String.IsNullOrEmpty(entry)) return;

            Directory.CreateDirectory(Path.GetDirectoryName(excludePath)!);
            string excludeContent = File.Exists(excludePath)
                ? await File.ReadAllTextAsync(excludePath, token).ConfigureAwait(false)
                : "";
            bool hasEntry = excludeContent
                .Split('\n')
                .Select(l => l.Trim())
                .Any(l => String.Equals(l, entry, StringComparison.Ordinal));
            if (hasEntry) return;

            string suffix = excludeContent.Length > 0 && !excludeContent.EndsWith("\n", StringComparison.Ordinal) ? "\n" : "";
            await File.AppendAllTextAsync(excludePath, suffix + entry + "\n", token).ConfigureAwait(false);
        }

        #endregion
    }
}
