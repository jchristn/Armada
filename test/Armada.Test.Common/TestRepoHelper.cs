namespace Armada.Test.Common
{
    using System;
    using System.Diagnostics;
    using System.IO;

    /// <summary>
    /// Helper for creating local bare git repositories for use in automated tests.
    /// Avoids cloning from fake GitHub URLs which causes 500 errors when the server
    /// tries to provision docks for mission assignment.
    /// </summary>
    public static class TestRepoHelper
    {
        #region Private-Members

        private static string? _BareRepoPath = null;
        private static readonly object _Lock = new object();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Returns a file:// URL to a local bare git repository with at least one commit.
        /// The repository is created once and reused across all test suites.
        /// </summary>
        /// <returns>A file:// URL suitable for use as a vessel RepoUrl.</returns>
        public static string GetLocalBareRepoUrl()
        {
            lock (_Lock)
            {
                if (_BareRepoPath == null)
                {
                    string tempBase = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    string workDir = Path.Combine(tempBase, "work");
                    _BareRepoPath = Path.Combine(tempBase, "bare.git");
                    Directory.CreateDirectory(workDir);

                    RunGit(workDir, "init -b main");
                    RunGit(workDir, "config user.email test@test.com");
                    RunGit(workDir, "config user.name Test");
                    File.WriteAllText(Path.Combine(workDir, "README.md"), "test");
                    RunGit(workDir, "add .");
                    RunGit(workDir, "commit -m init");
                    RunGit(tempBase, "clone --bare " + workDir.Replace("\\", "/") + " bare.git");

                    try { Directory.Delete(workDir, true); } catch { }
                }
                return "file:///" + _BareRepoPath.Replace("\\", "/");
            }
        }

        #endregion

        #region Private-Methods

        private static void RunGit(string workingDir, string arguments)
        {
            Process process = new Process();
            process.StartInfo.FileName = "git";
            process.StartInfo.Arguments = arguments;
            process.StartInfo.WorkingDirectory = workingDir;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.Start();
            process.WaitForExit(10000);
        }

        #endregion
    }
}
