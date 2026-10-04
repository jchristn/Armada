namespace Armada.Tui.Services.Credentials
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Linux credential store over libsecret's <c>secret-tool</c> (GNOME Keyring, KWallet via the Secret Service API).
    /// The secret is written to the tool's standard input. Thread-safe.
    /// </summary>
    public class SecretToolCredentialStore : ICredentialStore
    {
        #region Public-Members

        /// <inheritdoc />
        public string Name
        {
            get { return "secret-tool"; }
        }

        /// <summary>
        /// Service attribute. Default <c>armada-tui</c>.
        /// </summary>
        public string Service { get; set; } = "armada-tui";

        /// <summary>
        /// True on Linux when <c>secret-tool</c> is on PATH.
        /// </summary>
        public bool IsAvailable
        {
            get { return OperatingSystem.IsLinux() && ProcessRunner.Exists("secret-tool"); }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<string?> GetAsync(string key, CancellationToken token = default)
        {
            ProcessResult result = await ProcessRunner.RunAsync("secret-tool", new List<string> { "lookup", "service", Service, "account", key }, null, 10000, token).ConfigureAwait(false);
            if (result.ExitCode != 0) return null;
            string value = result.StdOut.TrimEnd('\r', '\n');
            return value.Length > 0 ? value : null;
        }

        /// <inheritdoc />
        public async Task<bool> SetAsync(string key, string secret, CancellationToken token = default)
        {
            ProcessResult result = await ProcessRunner.RunAsync("secret-tool", new List<string> { "store", "--label=Armada TUI (" + key + ")", "service", Service, "account", key }, secret ?? "", 10000, token).ConfigureAwait(false);
            return result.ExitCode == 0;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string key, CancellationToken token = default)
        {
            await ProcessRunner.RunAsync("secret-tool", new List<string> { "clear", "service", Service, "account", key }, null, 10000, token).ConfigureAwait(false);
        }

        #endregion
    }
}
