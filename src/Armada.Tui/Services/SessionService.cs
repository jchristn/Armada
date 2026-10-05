namespace Armada.Tui.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Models;
    using Armada.Tui.Services.Credentials;

    /// <summary>
    /// Login and session handling (W1.14): the dashboard's email, tenant lookup, tenant picker, password flow and the
    /// API key flow (validated with <c>whoami</c>), token storage per server profile in the credential store, resume on
    /// start (stored token or <c>ARMADA_TOKEN</c>), roles from <c>whoami</c>, proxy-mode context, and 401 handling
    /// (the client's Unauthorized hook expires the session on the UI loop). Async methods may be awaited from any
    /// thread; events are raised on the UI loop through the dispatcher.
    /// </summary>
    public class SessionService
    {
        #region Public-Members

        /// <summary>
        /// The client for the active profile. Replaced when the profile changes. Never null.
        /// </summary>
        public ArmadaClient Client { get; private set; }

        /// <summary>
        /// Active server profile. Never null.
        /// </summary>
        public ServerProfile Profile { get; private set; }

        /// <summary>
        /// Signed-in identity, or null.
        /// </summary>
        public WhoAmIResult? Identity { get; private set; } = null;

        /// <summary>
        /// Proxy session context when connected through Armada.Proxy, or null.
        /// </summary>
        public ProxySessionContext? Proxy { get; private set; } = null;

        /// <summary>
        /// True when signed in.
        /// </summary>
        public bool IsSignedIn
        {
            get { return Identity != null; }
        }

        /// <summary>
        /// Global admin (the dashboard's isAdmin).
        /// </summary>
        public bool IsGlobalAdmin
        {
            get { return Identity?.User?.IsAdmin ?? false; }
        }

        /// <summary>
        /// Tenant admin (global admins included).
        /// </summary>
        public bool IsTenantAdmin
        {
            get { return IsGlobalAdmin || (Identity?.User?.IsTenantAdmin ?? false); }
        }

        /// <summary>
        /// Signed-in email, or empty.
        /// </summary>
        public string UserEmail
        {
            get { return Identity?.User?.Email ?? ""; }
        }

        /// <summary>
        /// Tenant name, or empty.
        /// </summary>
        public string TenantName
        {
            get { return Identity?.Tenant?.Name ?? ""; }
        }

        /// <summary>
        /// Current token (session token or API key), or null.
        /// </summary>
        public string? Token
        {
            get { return Client.Options.Token ?? Client.Options.BearerToken ?? Client.Options.ApiKey; }
        }

        /// <summary>
        /// True when the server reports default credentials still in use (admins and tenant admins only).
        /// </summary>
        public bool DefaultCredentialsInUse
        {
            get { return Identity?.DefaultCredentialsInUse ?? false; }
        }

        /// <summary>
        /// Raised on the UI loop after sign-in.
        /// </summary>
        public event EventHandler? SignedIn;

        /// <summary>
        /// Raised on the UI loop after sign-out or expiry, with an English reason (null for a user sign-out).
        /// </summary>
        public event EventHandler<string?>? SignedOut;

        #endregion

        #region Private-Members

        private readonly Func<string, ArmadaClient> _ClientFactory;
        private readonly ICredentialStore _Credentials;
        private readonly PreferencesService _Prefs;
        private readonly IUiDispatcher _Dispatcher;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for a profile.
        /// </summary>
        /// <param name="profile">Server profile.</param>
        /// <param name="clientFactory">Creates a client for a base URL (tests inject stub handlers).</param>
        /// <param name="credentials">Credential store.</param>
        /// <param name="prefs">Preferences.</param>
        /// <param name="dispatcher">UI dispatcher.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public SessionService(ServerProfile profile, Func<string, ArmadaClient> clientFactory, ICredentialStore credentials, PreferencesService prefs, IUiDispatcher dispatcher)
        {
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _ClientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
            _Credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
            _Prefs = prefs ?? throw new ArgumentNullException(nameof(prefs));
            _Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            Client = CreateClient(profile.Url);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Credential store key for a profile.
        /// </summary>
        /// <param name="profile">Profile.</param>
        /// <returns>Key.</returns>
        public static string CredentialKey(ServerProfile profile)
        {
            return "profile:" + profile.Name + "@" + profile.Url;
        }

        /// <summary>
        /// Switch to another profile (signs out of the current one without deleting its stored token).
        /// </summary>
        /// <param name="profile">Profile.</param>
        public void SwitchProfile(ServerProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            Identity = null;
            Proxy = null;
            Profile = profile;
            Client.Dispose();
            Client = CreateClient(profile.Url);
            _Prefs.Current.ActiveProfile = profile.Name;
            _Prefs.Save();
        }

        /// <summary>
        /// Resume a session from <paramref name="envToken"/> or the stored token. Returns false (signed out) when no
        /// token exists or it is rejected.
        /// </summary>
        /// <param name="envToken">Token from the environment, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when signed in.</returns>
        public async Task<bool> TryResumeAsync(string? envToken, CancellationToken token = default)
        {
            string? stored = !String.IsNullOrWhiteSpace(envToken) ? envToken : await _Credentials.GetAsync(CredentialKey(Profile), token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(stored)) return false;
            SignInResult result = await SignInWithTokenAsync(stored!, String.IsNullOrWhiteSpace(envToken), token).ConfigureAwait(false);
            return result.Success;
        }

        /// <summary>
        /// Look up the tenants for an email (the first login step).
        /// </summary>
        /// <param name="email">Email.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tenants (possibly empty).</returns>
        /// <exception cref="ArmadaApiException">Thrown when the lookup fails.</exception>
        public async Task<List<TenantListEntry>> LookupTenantsAsync(string email, CancellationToken token = default)
        {
            TenantLookupResult? result = await Client.LookupTenantsAsync(email.Trim(), token).ConfigureAwait(false);
            return result?.Tenants ?? new List<TenantListEntry>();
        }

        /// <summary>
        /// Sign in with email, tenant, and password.
        /// </summary>
        /// <param name="email">Email.</param>
        /// <param name="tenantId">Tenant id.</param>
        /// <param name="password">Password.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result (English error "Authentication failed." on failure, like the dashboard).</returns>
        public async Task<SignInResult> SignInWithPasswordAsync(string email, string tenantId, string password, CancellationToken token = default)
        {
            try
            {
                AuthenticateRequest req = new AuthenticateRequest();
                req.Email = email.Trim();
                req.TenantId = tenantId;
                req.Password = password;
                AuthenticateResult? result = await Client.AuthenticateAsync(req, token).ConfigureAwait(false);
                if (result == null || !result.Success || String.IsNullOrEmpty(result.Token)) return SignInResult.Fail("Authentication failed.");
                Profile.LastUser = email.Trim();
                Profile.LastTenantId = tenantId;
                Profile.AuthMethod = "password";
                SignInResult signed = await SignInWithTokenAsync(result.Token!, true, token).ConfigureAwait(false);
                return signed.Success ? signed : SignInResult.Fail("Authentication failed.");
            }
            catch (ArmadaApiException)
            {
                return SignInResult.Fail("Authentication failed.");
            }
        }

        /// <summary>
        /// Sign in with an API key or bearer token (validated with <c>whoami</c>).
        /// </summary>
        /// <param name="apiKey">Key or token.</param>
        /// <param name="store">Store the token in the credential store.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result (English error "API key authentication failed." on failure).</returns>
        public async Task<SignInResult> SignInWithTokenAsync(string apiKey, bool store, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(apiKey)) return SignInResult.Fail("API key authentication failed.");
            string secret = apiKey.Trim();

            // The dashboard sends whatever is pasted as X-Token (session tokens and credential bearer tokens). The
            // Admiral's own API key is only accepted as X-Api-Key or a bearer token, so try each scheme in turn.
            for (int scheme = 0; scheme < 3; scheme++)
            {
                ClearCredentials();
                if (scheme == 0) Client.Options.Token = secret;
                else if (scheme == 1) Client.Options.BearerToken = secret;
                else Client.Options.ApiKey = secret;
                WhoAmIResult? me;
                try
                {
                    me = await Client.WhoamiAsync(token).ConfigureAwait(false);
                }
                catch (ArmadaApiException ex) when (ex.IsUnauthorized || ex.StatusCode == 403)
                {
                    continue;
                }
                catch (ArmadaApiException)
                {
                    ClearCredentials();
                    return SignInResult.Fail("API key authentication failed.");
                }

                if (me == null || me.User == null) continue;

                try { Proxy = await Client.GetProxySessionContextAsync(token).ConfigureAwait(false); }
                catch (ArmadaApiException) { Proxy = null; }

                if (store) await _Credentials.SetAsync(CredentialKey(Profile), secret, token).ConfigureAwait(false);
                if (Profile.AuthMethod != "password" || String.IsNullOrEmpty(Profile.LastUser)) Profile.LastUser = me.User.Email;
                Profile.LastUsedUtc = DateTime.UtcNow;
                _Prefs.Current.ActiveProfile = Profile.Name;
                _Prefs.Save();
                _Dispatcher.Post(() =>
                {
                    Identity = me;
                    SignedIn?.Invoke(this, EventArgs.Empty);
                });
                return SignInResult.Ok();
            }

            ClearCredentials();
            return SignInResult.Fail("API key authentication failed.");
        }

        /// <summary>
        /// Mark the API-key login method on the profile (called by the login screen before signing in).
        /// </summary>
        public void UseApiKeyMethod()
        {
            Profile.AuthMethod = "apikey";
        }

        /// <summary>
        /// Sign out: forget the stored token and raise <see cref="SignedOut"/>.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task.</returns>
        public async Task SignOutAsync(CancellationToken token = default)
        {
            await _Credentials.DeleteAsync(CredentialKey(Profile), token).ConfigureAwait(false);
            ClearCredentials();
            _Dispatcher.Post(() => EndSession(null));
        }

        /// <summary>
        /// Expire the session after a 401 (keeps nothing; the user signs in again). Call on the UI loop.
        /// </summary>
        /// <param name="reason">English reason.</param>
        public void Expire(string reason)
        {
            if (!IsSignedIn) return;
            ClearCredentials();
            EndSession(reason);
        }

        #endregion

        #region Private-Methods

        private ArmadaClient CreateClient(string url)
        {
            ArmadaClient client = _ClientFactory(url);
            client.Unauthorized += (s, e) => _Dispatcher.Post(() => Expire("Your session expired. Sign in again."));
            return client;
        }

        private void ClearCredentials()
        {
            Client.Options.Token = null;
            Client.Options.BearerToken = null;
            Client.Options.ApiKey = null;
        }

        private void EndSession(string? reason)
        {
            Identity = null;
            Proxy = null;
            SignedOut?.Invoke(this, reason);
        }

        #endregion
    }
}
