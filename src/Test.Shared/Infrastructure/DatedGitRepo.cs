namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Threading.Tasks;

    /// <summary>
    /// Builds git repositories with fixed commit dates for history tests. Every git run uses a fixed identity, no
    /// signing, and neither the system nor the global git config, so results do not depend on the host.
    /// </summary>
    public static class DatedGitRepo
    {
        #region Private-Members

        private static string? _EmptyConfig = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create an empty repository (branch main) in a fresh temp directory.
        /// </summary>
        /// <returns>Repository path.</returns>
        public static string Create()
        {
            string repo = Path.Combine(TestTemp.NewDirectory("git-history"), "repo");
            Directory.CreateDirectory(repo);
            Git(repo, null, "init", "-q", "-b", "main");
            return repo;
        }

        /// <summary>
        /// Write a file, stage everything, and commit with the given author and committer date.
        /// </summary>
        /// <param name="repo">Working tree.</param>
        /// <param name="message">Commit message.</param>
        /// <param name="when">Author and committer date.</param>
        /// <param name="path">Repository-relative file path.</param>
        /// <param name="content">File content.</param>
        public static void Commit(string repo, string message, DateTimeOffset when, string path, string content)
        {
            CommitWith(repo, message, when, null, null, null, path, content);
        }

        /// <summary>
        /// Write a file, stage everything, and commit with a committer date and optional author name, email, and date.
        /// </summary>
        /// <param name="repo">Working tree.</param>
        /// <param name="message">Commit message.</param>
        /// <param name="committed">Committer date (and author date unless authored is given).</param>
        /// <param name="authorName">Author name, or null for the fixed identity.</param>
        /// <param name="authorEmail">Author email, or null for the fixed identity.</param>
        /// <param name="authored">Author date, or null for the committer date.</param>
        /// <param name="path">Repository-relative file path.</param>
        /// <param name="content">File content.</param>
        public static void CommitWith(string repo, string message, DateTimeOffset committed, string? authorName, string? authorEmail, DateTimeOffset? authored, string path, string content)
        {
            string full = Path.Combine(repo, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
            Git(repo, null, "add", "-A");
            Dictionary<string, string> env = DateEnvironment(committed);
            if (authored.HasValue) env["GIT_AUTHOR_DATE"] = authored.Value.ToUnixTimeSeconds() + " +0000";
            if (authorName != null) env["GIT_AUTHOR_NAME"] = authorName;
            if (authorEmail != null) env["GIT_AUTHOR_EMAIL"] = authorEmail;
            Run(repo, env, "commit", "-q", "-m", message);
        }

        /// <summary>
        /// Run git; throws when it fails.
        /// </summary>
        /// <param name="workingDirectory">Working directory, or null for the current one.</param>
        /// <param name="when">Author and committer date for commits the command makes, or null.</param>
        /// <param name="args">Arguments.</param>
        /// <returns>Standard output.</returns>
        public static string Git(string? workingDirectory, DateTimeOffset? when, params string[] args)
        {
            return Run(workingDirectory, when.HasValue ? DateEnvironment(when.Value) : new Dictionary<string, string>(), args);
        }

        #endregion

        #region Private-Methods

        private static Dictionary<string, string> DateEnvironment(DateTimeOffset when)
        {
            string stamp = when.ToUnixTimeSeconds() + " +0000";
            return new Dictionary<string, string> { ["GIT_AUTHOR_DATE"] = stamp, ["GIT_COMMITTER_DATE"] = stamp };
        }

        private static string Run(string? workingDirectory, Dictionary<string, string> env, params string[] args)
        {
            ProcessStartInfo info = new ProcessStartInfo("git");
            if (workingDirectory != null) info.WorkingDirectory = workingDirectory;
            foreach (string arg in new[] { "-c", "user.name=History Test", "-c", "user.email=history@armada.test", "-c", "commit.gpgsign=false", "-c", "init.defaultBranch=main" })
                info.ArgumentList.Add(arg);
            foreach (string arg in args) info.ArgumentList.Add(arg);
            info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            info.Environment["GIT_CONFIG_GLOBAL"] = EmptyConfigPath();
            info.Environment["LC_ALL"] = "C";
            foreach (KeyValuePair<string, string> pair in env) info.Environment[pair.Key] = pair.Value;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.UseShellExecute = false;
            using (Process process = Process.Start(info)!)
            {
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                string stdout = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(60000))
                {
                    try { process.Kill(true); }
                    catch (InvalidOperationException) { }
                    throw new TimeoutException("git " + String.Join(" ", args) + " timed out");
                }
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("git " + String.Join(" ", args) + " failed (exit " + process.ExitCode + "): " + stderr.Result + stdout);
                return stdout;
            }
        }

        private static string EmptyConfigPath()
        {
            if (_EmptyConfig == null)
            {
                string path = TestTemp.NewFile("git-history-config", ".gitconfig");
                File.WriteAllText(path, "");
                _EmptyConfig = path;
            }
            return _EmptyConfig;
        }

        #endregion
    }
}
