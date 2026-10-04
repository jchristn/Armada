namespace Armada.Tui.Services.Credentials
{
    using System;

    /// <summary>
    /// Chooses the credential store (Decision D4): the OS keychain where available (macOS Keychain, Windows Credential
    /// Manager, libsecret) with the 0600 file as fallback. <see cref="TuiPaths.CredentialStoreEnvironmentVariable"/>
    /// forces a store (<c>file</c>, <c>keychain</c>, <c>wincred</c>, <c>secret-tool</c>). Thread-safe (stateless).
    /// </summary>
    public static class CredentialStoreFactory
    {
        #region Public-Methods

        /// <summary>
        /// Create the store for this machine.
        /// </summary>
        /// <param name="selection">Store name, or null to read the environment variable (default auto).</param>
        /// <param name="filePath">File store path, or null for the default.</param>
        /// <returns>The store.</returns>
        public static ICredentialStore Create(string? selection = null, string? filePath = null)
        {
            string choice = (selection ?? Environment.GetEnvironmentVariable(TuiPaths.CredentialStoreEnvironmentVariable) ?? "auto").Trim().ToLowerInvariant();
            FileCredentialStore file = new FileCredentialStore(filePath);
            switch (choice)
            {
                case "file":
                    return file;
                case "keychain":
                    return new FallbackCredentialStore(new MacKeychainCredentialStore(), file);
                case "wincred":
                    return new FallbackCredentialStore(new WindowsCredentialStore(), file);
                case "secret-tool":
                case "libsecret":
                    return new FallbackCredentialStore(new SecretToolCredentialStore(), file);
                default:
                    MacKeychainCredentialStore mac = new MacKeychainCredentialStore();
                    if (mac.IsAvailable) return new FallbackCredentialStore(mac, file);
                    WindowsCredentialStore win = new WindowsCredentialStore();
                    if (win.IsAvailable) return new FallbackCredentialStore(win, file);
                    SecretToolCredentialStore secret = new SecretToolCredentialStore();
                    if (secret.IsAvailable) return new FallbackCredentialStore(secret, file);
                    return file;
            }
        }

        #endregion
    }
}
