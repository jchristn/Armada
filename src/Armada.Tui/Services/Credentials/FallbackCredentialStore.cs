namespace Armada.Tui.Services.Credentials
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Uses a primary store (an OS keychain) and falls back to a secondary store (the 0600 file) when the primary
    /// fails, for example when the keychain is locked or unavailable over SSH. Reads check both. Thread-safe when both
    /// stores are.
    /// </summary>
    public class FallbackCredentialStore : ICredentialStore
    {
        #region Public-Members

        /// <inheritdoc />
        public string Name
        {
            get { return _Primary.Name + "+" + _Fallback.Name; }
        }

        #endregion

        #region Private-Members

        private readonly ICredentialStore _Primary;
        private readonly ICredentialStore _Fallback;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="primary">Primary store.</param>
        /// <param name="fallback">Fallback store.</param>
        /// <exception cref="ArgumentNullException">Thrown when either store is null.</exception>
        public FallbackCredentialStore(ICredentialStore primary, ICredentialStore fallback)
        {
            _Primary = primary ?? throw new ArgumentNullException(nameof(primary));
            _Fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<string?> GetAsync(string key, CancellationToken token = default)
        {
            string? value = await _Primary.GetAsync(key, token).ConfigureAwait(false);
            if (value != null) return value;
            return await _Fallback.GetAsync(key, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> SetAsync(string key, string secret, CancellationToken token = default)
        {
            if (await _Primary.SetAsync(key, secret, token).ConfigureAwait(false))
            {
                await _Fallback.DeleteAsync(key, token).ConfigureAwait(false);
                return true;
            }

            return await _Fallback.SetAsync(key, secret, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string key, CancellationToken token = default)
        {
            await _Primary.DeleteAsync(key, token).ConfigureAwait(false);
            await _Fallback.DeleteAsync(key, token).ConfigureAwait(false);
        }

        #endregion
    }
}
