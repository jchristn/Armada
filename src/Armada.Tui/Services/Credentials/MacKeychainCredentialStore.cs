namespace Armada.Tui.Services.Credentials
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// macOS Keychain credential store driven through <c>/usr/bin/security</c>. Secrets are passed to the tool's
    /// interactive mode on standard input, never on the command line. Thread-safe.
    /// </summary>
    public class MacKeychainCredentialStore : ICredentialStore
    {
        #region Public-Members

        /// <inheritdoc />
        public string Name
        {
            get { return "keychain"; }
        }

        /// <summary>
        /// Keychain service name. Default <c>armada-tui</c>.
        /// </summary>
        public string Service { get; set; } = "armada-tui";

        /// <summary>
        /// Path of the security tool. Default <c>/usr/bin/security</c>.
        /// </summary>
        public string ToolPath { get; set; } = "/usr/bin/security";

        /// <summary>
        /// True on macOS when the security tool exists.
        /// </summary>
        public bool IsAvailable
        {
            get { return OperatingSystem.IsMacOS() && ProcessRunner.Exists(ToolPath); }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<string?> GetAsync(string key, CancellationToken token = default)
        {
            ProcessResult result = await ProcessRunner.RunAsync(ToolPath, new List<string> { "find-generic-password", "-s", Service, "-a", key, "-w" }, null, 10000, token).ConfigureAwait(false);
            if (result.ExitCode != 0) return null;
            string value = result.StdOut.TrimEnd('\r', '\n');
            return value.Length > 0 ? value : null;
        }

        /// <inheritdoc />
        public async Task<bool> SetAsync(string key, string secret, CancellationToken token = default)
        {
            string command = "add-generic-password -U -s " + Quote(Service) + " -a " + Quote(key) + " -w " + Quote(secret ?? "") + "\n";
            ProcessResult result = await ProcessRunner.RunAsync(ToolPath, new List<string> { "-i" }, command, 10000, token).ConfigureAwait(false);
            return result.ExitCode == 0 && result.StdErr.IndexOf("error", StringComparison.OrdinalIgnoreCase) < 0;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string key, CancellationToken token = default)
        {
            await ProcessRunner.RunAsync(ToolPath, new List<string> { "delete-generic-password", "-s", Service, "-a", key }, null, 10000, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static string Quote(string value)
        {
            return "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        #endregion
    }
}
