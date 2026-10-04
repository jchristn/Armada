namespace Armada.Tui.Services
{
    using System;
    using System.IO;

    /// <summary>
    /// File locations and the environment variables that override them (tests and sandboxes point these at temporary
    /// directories so the user's <c>~/.armada</c> is never touched). Thread-safe (stateless).
    /// </summary>
    public static class TuiPaths
    {
        #region Public-Members

        /// <summary>
        /// Environment variable overriding the preferences file path.
        /// </summary>
        public const string PreferencesEnvironmentVariable = "ARMADA_TUI_PREFERENCES";

        /// <summary>
        /// Environment variable overriding the credential file path (file store only).
        /// </summary>
        public const string CredentialsEnvironmentVariable = "ARMADA_TUI_CREDENTIALS";

        /// <summary>
        /// Environment variable selecting the credential store: <c>file</c>, <c>keychain</c>, <c>wincred</c>,
        /// <c>secret-tool</c>, or <c>auto</c> (default).
        /// </summary>
        public const string CredentialStoreEnvironmentVariable = "ARMADA_TUI_CREDENTIAL_STORE";

        /// <summary>
        /// Environment variable with a server URL for scripted starts.
        /// </summary>
        public const string ServerUrlEnvironmentVariable = "ARMADA_URL";

        /// <summary>
        /// Environment variable with a token or API key for scripted starts.
        /// </summary>
        public const string TokenEnvironmentVariable = "ARMADA_TOKEN";

        #endregion

        #region Public-Methods

        /// <summary>
        /// The Armada data directory (<c>~/.armada</c>).
        /// </summary>
        /// <returns>Directory path.</returns>
        public static string DataDirectory()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".armada");
        }

        /// <summary>
        /// The preferences file path (<see cref="PreferencesEnvironmentVariable"/> or <c>~/.armada/tui.json</c>).
        /// </summary>
        /// <returns>File path.</returns>
        public static string PreferencesFile()
        {
            string? overridden = Environment.GetEnvironmentVariable(PreferencesEnvironmentVariable);
            if (!String.IsNullOrWhiteSpace(overridden)) return Path.GetFullPath(overridden!);
            return Path.Combine(DataDirectory(), "tui.json");
        }

        /// <summary>
        /// The credential file path for the file store (<see cref="CredentialsEnvironmentVariable"/>, or
        /// <c>tui-credentials.json</c> next to the preferences file).
        /// </summary>
        /// <returns>File path.</returns>
        public static string CredentialsFile()
        {
            string? overridden = Environment.GetEnvironmentVariable(CredentialsEnvironmentVariable);
            if (!String.IsNullOrWhiteSpace(overridden)) return Path.GetFullPath(overridden!);
            string dir = Path.GetDirectoryName(PreferencesFile()) ?? DataDirectory();
            return Path.Combine(dir, "tui-credentials.json");
        }

        /// <summary>
        /// The notification history file next to the preferences file.
        /// </summary>
        /// <returns>File path.</returns>
        public static string NotificationsFile()
        {
            string dir = Path.GetDirectoryName(PreferencesFile()) ?? DataDirectory();
            return Path.Combine(dir, "tui-notifications.json");
        }

        #endregion
    }
}
