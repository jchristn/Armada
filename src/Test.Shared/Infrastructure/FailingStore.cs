namespace Test.Shared.Infrastructure
{
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Tui.Services.Credentials;

    /// <summary>
    /// A credential store that always fails (a locked keychain).
    /// </summary>
    public sealed class FailingStore : ICredentialStore
    {
        /// <inheritdoc />
        public string Name
        {
            get { return "failing"; }
        }

        /// <inheritdoc />
        public Task<string?> GetAsync(string key, CancellationToken token = default)
        {
            return Task.FromResult<string?>(null);
        }

        /// <inheritdoc />
        public Task<bool> SetAsync(string key, string secret, CancellationToken token = default)
        {
            return Task.FromResult(false);
        }

        /// <inheritdoc />
        public Task DeleteAsync(string key, CancellationToken token = default)
        {
            return Task.CompletedTask;
        }
    }
}
