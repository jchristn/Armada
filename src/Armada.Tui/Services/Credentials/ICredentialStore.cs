namespace Armada.Tui.Services.Credentials
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Stores session tokens and API keys at rest (Decision D4: the OS keychain where available, else a 0600 file).
    /// Keys are opaque account names (the TUI uses one per server profile). Implementations are thread-safe.
    /// </summary>
    public interface ICredentialStore
    {
        /// <summary>
        /// Short store name for diagnostics, for example <c>keychain</c> or <c>file</c>.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Read a secret.
        /// </summary>
        /// <param name="key">Account key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The secret, or null when none is stored.</returns>
        Task<string?> GetAsync(string key, CancellationToken token = default);

        /// <summary>
        /// Store or replace a secret.
        /// </summary>
        /// <param name="key">Account key.</param>
        /// <param name="secret">Secret.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when stored.</returns>
        Task<bool> SetAsync(string key, string secret, CancellationToken token = default);

        /// <summary>
        /// Remove a secret (no-op when absent).
        /// </summary>
        /// <param name="key">Account key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task.</returns>
        Task DeleteAsync(string key, CancellationToken token = default);
    }
}
