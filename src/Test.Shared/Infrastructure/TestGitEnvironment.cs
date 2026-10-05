namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    /// <summary>
    /// Pins the git settings that change file contents on checkout for every git process the test run starts: the test
    /// helpers' own git commands and the git commands of the product code under test (GitService, DockService, ...).
    /// Without this, the host's git configuration leaks into test repositories. GitHub's Windows runners set
    /// <c>core.autocrlf=true</c> system-wide, so a test repository cloned there checks <c>"base\nworker change\n"</c> out
    /// as <c>"base\r\nworker change\r\n"</c> and every content assertion on a checked-out file fails on Windows only.
    /// The settings are passed through <c>GIT_CONFIG_COUNT</c> / <c>GIT_CONFIG_KEY_n</c> / <c>GIT_CONFIG_VALUE_n</c>
    /// (git 2.31+), which git applies after the system, global, and repository files.
    /// </summary>
    public static class TestGitEnvironment
    {
        #region Public-Members

        /// <summary>
        /// The pinned settings: no line-ending conversion on checkout or commit, and LF as the native line ending.
        /// </summary>
        public static IReadOnlyList<KeyValuePair<string, string>> PinnedSettings { get; } = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("core.autocrlf", "false"),
            new KeyValuePair<string, string>("core.eol", "lf")
        };

        #endregion

        #region Private-Members

        private static readonly object _Lock = new object();
        private static bool _Pinned = false;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add the pinned settings to this process's environment, after any <c>GIT_CONFIG_*</c> entries already present.
        /// Child processes inherit them. Safe to call more than once.
        /// </summary>
        public static void Pin()
        {
            lock (_Lock)
            {
                if (_Pinned) return;

                int count = 0;
                string? existing = Environment.GetEnvironmentVariable("GIT_CONFIG_COUNT");
                if (!String.IsNullOrWhiteSpace(existing) && Int32.TryParse(existing, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0)
                {
                    count = parsed;
                }

                foreach (KeyValuePair<string, string> setting in PinnedSettings)
                {
                    string index = count.ToString(CultureInfo.InvariantCulture);
                    Environment.SetEnvironmentVariable("GIT_CONFIG_KEY_" + index, setting.Key);
                    Environment.SetEnvironmentVariable("GIT_CONFIG_VALUE_" + index, setting.Value);
                    count++;
                }

                Environment.SetEnvironmentVariable("GIT_CONFIG_COUNT", count.ToString(CultureInfo.InvariantCulture));
                _Pinned = true;
            }
        }

        #endregion
    }
}
