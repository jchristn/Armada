namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;

    /// <summary>
    /// Real git repositories for merge queue tests: an "origin" bare repository, the vessel's bare clone of it (the
    /// repository Armada lands from), and a working clone used to author branches. Branches are pushed into the vessel
    /// repository, as a captain's dock leaves them; main advances on origin, as other developers' pushes would.
    /// </summary>
    public sealed class MergeQueueRepoSet
    {
        #region Public-Members

        /// <summary>
        /// Path of the origin bare repository.
        /// </summary>
        public string Origin { get; private set; } = "";

        /// <summary>
        /// Path of the vessel's bare repository (vessel LocalPath).
        /// </summary>
        public string VesselRepo { get; private set; } = "";

        /// <summary>
        /// Path of the authoring working clone.
        /// </summary>
        public string Work { get; private set; } = "";

        /// <summary>
        /// Vessel id once a test has created the vessel row.
        /// </summary>
        public string? VesselId { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create the three repositories from the shared test template (main has one commit adding README.md).
        /// </summary>
        /// <returns>The repository set.</returns>
        public static MergeQueueRepoSet Create()
        {
            MergeQueueRepoSet set = new MergeQueueRepoSet();
            set.Origin = TestGitRepoHelper.CreateBareRepoCopy();
            string parent = TestTemp.NewDirectory("mq_clones");
            set.Work = Path.Combine(parent, "work");
            set.VesselRepo = Path.Combine(parent, "vessel.git");
            Git(parent, "clone", "-q", set.Origin, set.Work);
            Git(set.Work, "config", "user.name", "Armada Test");
            Git(set.Work, "config", "user.email", "test@armada.local");
            Git(set.Work, "config", "commit.gpgsign", "false");
            Git(parent, "clone", "-q", "--bare", set.Origin, set.VesselRepo);
            return set;
        }

        /// <summary>
        /// Create a branch from origin's current main with one commit writing <paramref name="file"/>, and push it into
        /// the vessel repository.
        /// </summary>
        /// <param name="branch">Branch name.</param>
        /// <param name="file">Relative file path.</param>
        /// <param name="content">File content.</param>
        public void PushBranch(string branch, string file, string content)
        {
            Git(Work, "fetch", "-q", "origin");
            Git(Work, "checkout", "-q", "-B", branch, "origin/main");
            File.WriteAllText(Path.Combine(Work, file), content);
            Git(Work, "add", file);
            Git(Work, "commit", "-q", "-m", "Change " + file + " on " + branch);
            Git(Work, "push", "-q", VesselRepo, branch + ":refs/heads/" + branch);
            Git(Work, "checkout", "-q", "--detach", "origin/main");
        }

        /// <summary>
        /// Commit a change on origin's main.
        /// </summary>
        /// <param name="file">Relative file path.</param>
        /// <param name="content">File content.</param>
        public void AdvanceMain(string file, string content)
        {
            Git(Work, "fetch", "-q", "origin");
            Git(Work, "checkout", "-q", "-B", "main", "origin/main");
            File.WriteAllText(Path.Combine(Work, file), content);
            Git(Work, "add", file);
            Git(Work, "commit", "-q", "-m", "Advance main: " + file);
            Git(Work, "push", "-q", "origin", "main");
            Git(Work, "checkout", "-q", "--detach", "origin/main");
        }

        /// <summary>
        /// Commit id of a branch on origin.
        /// </summary>
        /// <param name="branch">Branch name.</param>
        /// <returns>Commit id.</returns>
        public string OriginHead(string branch)
        {
            return Git(Origin, "rev-parse", "refs/heads/" + branch).Trim();
        }

        /// <summary>
        /// True when a file exists in the tree of an origin branch.
        /// </summary>
        /// <param name="branch">Branch name.</param>
        /// <param name="file">Relative file path.</param>
        /// <returns>True when present.</returns>
        public bool OriginHasFile(string branch, string file)
        {
            string listing = Git(Origin, "ls-tree", "--name-only", "refs/heads/" + branch);
            foreach (string line in listing.Split('\n'))
            {
                if (String.Equals(line.Trim(), file, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        #endregion

        #region Private-Methods

        private static string Git(string workingDirectory, params string[] args)
        {
            ProcessStartInfo info = new ProcessStartInfo("git");
            foreach (string arg in args) info.ArgumentList.Add(arg);
            info.WorkingDirectory = workingDirectory;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.UseShellExecute = false;
            info.Environment["GIT_TERMINAL_PROMPT"] = "0";
            using (Process process = Process.Start(info)!)
            {
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException("git " + String.Join(" ", args) + " failed in " + workingDirectory + ": " + stderr);
                }
                return stdout;
            }
        }

        #endregion
    }
}
