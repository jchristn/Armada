namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;

    /// <summary>
    /// Git helpers for vessel health suites: a working clone of a local bare origin built from
    /// <see cref="TestGitRepoHelper"/>, plus commit and branch helpers with controllable commit dates.
    /// </summary>
    public static class VesselHealthGitHelper
    {
        #region Public-Methods

        /// <summary>
        /// Create a bare origin (from the shared template) and a fresh working clone of it.
        /// </summary>
        /// <returns>The paths.</returns>
        public static VesselHealthRepoPair CreateCloneWithOrigin()
        {
            string origin = TestGitRepoHelper.CreateBareRepoCopy();
            string parent = TestTemp.NewDirectory("health_clone");
            string working = Path.Combine(parent, "work");
            RunGit(parent, null, "clone", "--quiet", origin, working);
            RunGit(working, null, "config", "user.name", "Armada Tests");
            RunGit(working, null, "config", "user.email", "armada-tests@example.com");
            return new VesselHealthRepoPair { OriginPath = origin, WorkingPath = working };
        }

        /// <summary>
        /// Clone the origin into a second working copy, add a commit, and push it, so the first clone falls behind.
        /// </summary>
        /// <param name="originPath">Bare origin path.</param>
        /// <param name="commits">Number of commits to push.</param>
        public static void PushCommitsToOrigin(string originPath, int commits)
        {
            string parent = TestTemp.NewDirectory("health_pusher");
            string pusher = Path.Combine(parent, "push");
            RunGit(parent, null, "clone", "--quiet", originPath, pusher);
            RunGit(pusher, null, "config", "user.name", "Armada Tests");
            RunGit(pusher, null, "config", "user.email", "armada-tests@example.com");
            for (int i = 0; i < commits; i++) Commit(pusher, "upstream-" + Guid.NewGuid().ToString("N") + ".txt", "x", "upstream " + i, null);
            RunGit(pusher, null, "push", "--quiet", "origin", "main");
        }

        /// <summary>
        /// Write a file and commit it.
        /// </summary>
        /// <param name="repo">Working repository.</param>
        /// <param name="fileName">Relative file name.</param>
        /// <param name="content">File content.</param>
        /// <param name="message">Commit message.</param>
        /// <param name="date">Author and committer date, or null for now.</param>
        public static void Commit(string repo, string fileName, string content, string message, DateTime? date)
        {
            string full = Path.Combine(repo, fileName);
            string? directory = Path.GetDirectoryName(full);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(full, content);
            RunGit(repo, null, "add", fileName);
            Dictionary<string, string>? env = null;
            if (date.HasValue)
            {
                string stamp = date.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                env = new Dictionary<string, string> { { "GIT_AUTHOR_DATE", stamp }, { "GIT_COMMITTER_DATE", stamp } };
            }

            RunGit(repo, env, "commit", "--quiet", "-m", message);
        }

        /// <summary>
        /// Create a branch from main with one commit at the given date, then switch back to main.
        /// </summary>
        /// <param name="repo">Working repository.</param>
        /// <param name="branch">Branch name.</param>
        /// <param name="date">Commit date.</param>
        public static void CreateBranchWithCommit(string repo, string branch, DateTime date)
        {
            RunGit(repo, null, "checkout", "--quiet", "-b", branch, "main");
            Commit(repo, "branch-" + Guid.NewGuid().ToString("N") + ".txt", branch, "work on " + branch, date);
            RunGit(repo, null, "checkout", "--quiet", "main");
        }

        /// <summary>
        /// Run git and return standard output.
        /// </summary>
        /// <param name="workingDirectory">Working directory.</param>
        /// <param name="environment">Extra environment variables, or null.</param>
        /// <param name="arguments">Arguments.</param>
        /// <returns>Standard output.</returns>
        /// <exception cref="InvalidOperationException">Thrown when git fails.</exception>
        public static string RunGit(string workingDirectory, Dictionary<string, string>? environment, params string[] arguments)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
            if (environment != null)
            {
                foreach (KeyValuePair<string, string> pair in environment) startInfo.Environment[pair.Key] = pair.Value;
            }

            using Process process = new Process { StartInfo = startInfo };
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException("git " + String.Join(" ", arguments) + " failed (exit " + process.ExitCode + "): " + error.Trim());
            return output;
        }

        #endregion
    }
}
