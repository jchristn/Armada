namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Concurrent;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Threading;

    /// <summary>
    /// Centralized temp-artifact management for the test suites. Every temp directory and file the
    /// tests create shares the <c>armada_</c> prefix under the system temp directory. Two mechanisms
    /// keep that directory from accumulating orphans across runs:
    ///
    /// 1. A one-time startup sweep (<see cref="Sweep"/>), invoked from a module initializer when the
    ///    assembly loads -- so it runs once for the console runner, the xUnit adapter, and the NUnit
    ///    adapter alike, with no per-suite changes. It best-effort deletes <c>armada_*</c> entries left
    ///    behind by prior runs that were killed before their own cleanup could run. Only entries older
    ///    than <see cref="_StaleAfter"/> are removed, so a test run executing concurrently in another
    ///    process never has its fresh artifacts deleted out from under it.
    ///
    /// 2. Directories and files created via <see cref="NewDirectory"/> / <see cref="NewFile"/>, plus
    ///    any path handed to <see cref="Track"/>, are deleted on normal process exit. This reclaims the
    ///    long-lived artifacts a run reuses (the SQLite template, the shared bare repo, the current e2e
    ///    server directory) without waiting for the next run's sweep.
    ///
    /// The two mechanisms are complementary: tracking handles the current run's own artifacts promptly,
    /// and the age-gated sweep is the safety net for anything a previous run failed to release.
    /// </summary>
    public static class TestTemp
    {
        #region Public-Members

        /// <summary>
        /// Shared filename prefix for every temp artifact the tests create. The startup sweep keys off
        /// this prefix, so any test temp path must begin with it to be reclaimed.
        /// </summary>
        public const string Prefix = "armada_";

        #endregion

        #region Private-Members

        private static readonly TimeSpan _StaleAfter = TimeSpan.FromHours(2);
        private static readonly ConcurrentDictionary<string, byte> _Tracked =
            new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        private static int _ExitHooked;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Module initializer: sweep stale artifacts from prior runs and arm the process-exit cleanup.
        /// Runs exactly once when the test assembly is loaded.
        /// </summary>
        // CA2255: a module initializer is exactly the right tool here -- this is a test-support assembly,
        // and the whole point is to sweep stale temp artifacts once, automatically, when the test host
        // loads it, without every suite having to opt in.
#pragma warning disable CA2255
        [ModuleInitializer]
        public static void Initialize()
        {
            Sweep();
            EnsureExitHook();
            IsolateUserProfile();
        }
#pragma warning restore CA2255

        /// <summary>
        /// The per-run sandbox that stands in for the user's <c>~/.armada</c> while tests run, or null before
        /// <see cref="Initialize"/> ran.
        /// </summary>
        public static string? ProfileSandbox { get; private set; } = null;

        /// <summary>
        /// Create a fresh, uniquely-named temp directory under the system temp directory and register it
        /// for deletion on process exit.
        /// </summary>
        /// <param name="label">Optional human-readable label embedded in the directory name.</param>
        /// <returns>The absolute path to the created directory.</returns>
        public static string NewDirectory(string? label = null)
        {
            string path = ReservePath(label);
            Directory.CreateDirectory(path);
            Track(path);
            return path;
        }

        /// <summary>
        /// Reserve a fresh, uniquely-named temp file path under the system temp directory and register it
        /// for deletion on process exit. The file itself is not created; the caller writes to it.
        /// </summary>
        /// <param name="label">Optional human-readable label embedded in the file name.</param>
        /// <param name="extension">Optional file extension, e.g. <c>.db</c> (included verbatim).</param>
        /// <returns>The absolute path reserved for the file.</returns>
        public static string NewFile(string? label = null, string extension = "")
        {
            string path = ReservePath(label) + (extension ?? "");
            Track(path);
            return path;
        }

        /// <summary>
        /// Register an existing temp path (file or directory) for best-effort deletion on process exit.
        /// Safe to call more than once for the same path.
        /// </summary>
        /// <param name="path">The absolute path to track.</param>
        public static void Track(string? path)
        {
            if (String.IsNullOrEmpty(path)) return;
            _Tracked[path!] = 0;
            EnsureExitHook();
        }

        /// <summary>
        /// Best-effort delete a temp path (file or directory tree) immediately and stop tracking it.
        /// Never throws.
        /// </summary>
        /// <param name="path">The absolute path to delete.</param>
        public static void TryDelete(string? path)
        {
            if (String.IsNullOrEmpty(path)) return;

            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
                else if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // Best effort: a lingering handle or permission issue must not fail a run.
            }

            _Tracked.TryRemove(path!, out byte _);
        }

        #endregion

        #region Private-Methods

        private static void IsolateUserProfile()
        {
            // Tests must never read or write the developer's real ~/.armada (settings.json, the database, TUI
            // preferences, or the OS keychain). Point every default at a per-run sandbox before Armada.Core's
            // Constants resolve their defaults, and fail fast if they already resolved to the real profile.
            string sandbox = NewDirectory("profile");
            Environment.SetEnvironmentVariable(Armada.Core.Constants.DataDirectoryEnvVar, sandbox);
            Environment.SetEnvironmentVariable("ARMADA_TUI_PREFERENCES", Path.Combine(sandbox, "tui.json"));
            Environment.SetEnvironmentVariable("ARMADA_TUI_CREDENTIALS", Path.Combine(sandbox, "tui-credentials.json"));
            Environment.SetEnvironmentVariable("ARMADA_TUI_CREDENTIAL_STORE", "file");
            ProfileSandbox = sandbox;

            if (!String.Equals(Armada.Core.Constants.DefaultDataDirectory, sandbox, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Test isolation failed: Armada.Core.Constants.DefaultDataDirectory resolved to '" +
                    Armada.Core.Constants.DefaultDataDirectory + "' before the test sandbox was set. Tests would touch the real profile.");
            }
        }

        private static string ReservePath(string? label)
        {
            string safeLabel = String.IsNullOrWhiteSpace(label) ? "" : Sanitize(label!) + "_";
            return Path.Combine(Path.GetTempPath(), Prefix + safeLabel + Guid.NewGuid().ToString("N"));
        }

        private static string Sanitize(string label)
        {
            char[] chars = label.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (!Char.IsLetterOrDigit(c) && c != '-' && c != '_') chars[i] = '_';
            }
            return new string(chars);
        }

        private static void EnsureExitHook()
        {
            if (Interlocked.Exchange(ref _ExitHooked, 1) == 1) return;
            AppDomain.CurrentDomain.ProcessExit += (object? _, EventArgs __) => DeleteTracked();
        }

        private static void DeleteTracked()
        {
            foreach (string path in _Tracked.Keys) TryDelete(path);
        }

        private static void Sweep()
        {
            string root;
            try
            {
                root = Path.GetTempPath();
            }
            catch
            {
                return;
            }

            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(root, Prefix + "*");
            }
            catch
            {
                return;
            }

            DateTime cutoffUtc = DateTime.UtcNow - _StaleAfter;
            foreach (string entry in entries)
            {
                try
                {
                    // Age-gate on last-write time so a test run executing concurrently in another
                    // process -- whose artifacts are necessarily fresh -- is never disturbed.
                    if (File.GetLastWriteTimeUtc(entry) > cutoffUtc) continue;

                    if (Directory.Exists(entry)) Directory.Delete(entry, true);
                    else File.Delete(entry);
                }
                catch
                {
                    // Best effort: skip anything still locked or already gone.
                }
            }
        }

        #endregion
    }
}
