namespace Armada.Helm.Infrastructure
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Tui.Services;
    using Armada.Tui.Services.Credentials;

    /// <summary>
    /// Resolves which Admiral a CLI command talks to. Order: <c>--server</c> or <c>--profile</c>, then
    /// <c>ARMADA_SERVER_URL</c> (or the TUI's <c>ARMADA_URL</c>), then the active profile in the shared TUI
    /// preferences (<c>tui.json</c>), then this machine's Admiral at <c>http://127.0.0.1:&lt;admiralPort&gt;</c> with the
    /// local API key. Profiles and their tokens are the TUI's: one store, shared by <c>armada tui</c> and the CLI.
    /// </summary>
    public class AdmiralTargetResolver
    {
        #region Public-Members

        /// <summary>
        /// Environment variable naming the Admiral URL for CLI commands and <c>armada tui</c>.
        /// </summary>
        public const string ServerUrlEnvironmentVariable = "ARMADA_SERVER_URL";

        /// <summary>
        /// The TUI's original server URL variable, also honored (after <see cref="ServerUrlEnvironmentVariable"/>).
        /// </summary>
        public const string LegacyServerUrlEnvironmentVariable = TuiPaths.ServerUrlEnvironmentVariable;

        /// <summary>
        /// Environment variable with a bearer token (shared with the TUI).
        /// </summary>
        public const string TokenEnvironmentVariable = TuiPaths.TokenEnvironmentVariable;

        /// <summary>
        /// Reserved profile name meaning this machine's Admiral.
        /// </summary>
        public const string LocalProfileName = "local";

        #endregion

        #region Private-Members

        private readonly Func<string, string?> _Environment;
        private readonly PreferencesService _Preferences;
        private readonly ICredentialStore _Credentials;
        private readonly int _LocalPort;
        private readonly string? _LocalApiKey;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="environment">Environment variable lookup.</param>
        /// <param name="preferences">Loaded TUI preferences (profiles).</param>
        /// <param name="credentials">Credential store holding profile tokens.</param>
        /// <param name="localPort">The local Admiral's admiralPort.</param>
        /// <param name="localApiKey">The local Admiral's API key, or null.</param>
        public AdmiralTargetResolver(Func<string, string?> environment, PreferencesService preferences, ICredentialStore credentials, int localPort, string? localApiKey)
        {
            _Environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _Preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
            _Credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
            _LocalPort = localPort;
            _LocalApiKey = localApiKey;
        }

        /// <summary>
        /// Create a resolver for this process: real environment, the TUI preferences file, and the TUI credential store.
        /// </summary>
        /// <param name="localPort">Local admiralPort.</param>
        /// <param name="localApiKey">Local API key, or null.</param>
        /// <returns>Resolver.</returns>
        public static AdmiralTargetResolver CreateDefault(int localPort, string? localApiKey)
        {
            PreferencesService prefs = new PreferencesService();
            prefs.Load();
            return new AdmiralTargetResolver(Environment.GetEnvironmentVariable, prefs, CredentialStoreFactory.Create(), localPort, localApiKey);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The local Admiral's base URL. Watson binds the default "localhost" host to IPv4 loopback; 127.0.0.1 avoids a
        /// slow IPv6 localhost fallback.
        /// </summary>
        /// <param name="port">admiralPort.</param>
        /// <returns>URL.</returns>
        public static string LocalBaseUrl(int port)
        {
            return "http://127.0.0.1:" + port;
        }

        /// <summary>
        /// Validate and normalize an Admiral URL: absolute http or https, no query or fragment, no trailing slash.
        /// </summary>
        /// <param name="value">URL.</param>
        /// <param name="origin">Where it came from, for the message (for example <c>--server</c>).</param>
        /// <returns>Normalized URL.</returns>
        /// <exception cref="AdmiralTargetException">InvalidServerUrl.</exception>
        public static string NormalizeServerUrl(string? value, string origin)
        {
            string trimmed = (value ?? String.Empty).Trim();
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || String.IsNullOrEmpty(uri.Host)
                || !String.IsNullOrEmpty(uri.Query)
                || !String.IsNullOrEmpty(uri.Fragment))
            {
                throw new AdmiralTargetException(
                    AdmiralTargetErrorEnum.InvalidServerUrl,
                    "Invalid Admiral URL '" + trimmed + "' (" + origin + "): use an absolute http:// or https:// URL such as https://armada.example.com or http://10.0.0.5:7890.");
            }

            return trimmed.TrimEnd('/');
        }

        /// <summary>
        /// Whether a profile is the auto-created one that follows the local Admiral (same rule as the TUI).
        /// </summary>
        /// <param name="profile">Profile.</param>
        /// <returns>True when it follows the local Admiral.</returns>
        public static bool FollowsLocalAdmiral(ServerProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            bool follows = profile.FollowsLocalAdmiral ?? String.Equals(profile.Name, "default", StringComparison.OrdinalIgnoreCase);
            return follows && LocalAdmiralDefaults.IsLoopback(profile.Url);
        }

        /// <summary>
        /// Resolve the target.
        /// </summary>
        /// <param name="request">Command-line options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Target.</returns>
        /// <exception cref="AdmiralTargetException">InvalidServerUrl, UnknownProfile, or ConflictingOptions.</exception>
        public async Task<AdmiralTarget> ResolveAsync(AdmiralTargetRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            string? flagServer = Clean(request.Server);
            string? flagProfile = Clean(request.Profile);
            string? flagToken = Clean(request.Token);
            string? envToken = Clean(_Environment(TokenEnvironmentVariable));

            if (flagServer != null && flagProfile != null)
                throw new AdmiralTargetException(AdmiralTargetErrorEnum.ConflictingOptions, "Use either --server or --profile, not both.");

            if (flagServer != null)
            {
                string url = NormalizeServerUrl(flagServer, "--server");
                return Tag(await BuildExplicitAsync(url, AdmiralTargetSourceEnum.Flag, FindByUrl(url), flagToken, envToken, token).ConfigureAwait(false), "--server");
            }

            if (flagProfile != null)
            {
                if (String.Equals(flagProfile, LocalProfileName, StringComparison.OrdinalIgnoreCase)) return Tag(Local(AdmiralTargetSourceEnum.Flag, flagToken), "--profile");
                ServerProfile? named = _Preferences.FindProfile(flagProfile);
                if (named == null)
                    throw new AdmiralTargetException(AdmiralTargetErrorEnum.UnknownProfile, "No profile named '" + flagProfile + "'. List profiles with 'armada profile list' or add one with 'armada profile add " + flagProfile + " --server <url>'.");
                if (FollowsLocalAdmiral(named)) return Tag(Local(AdmiralTargetSourceEnum.Flag, flagToken), "--profile");
                return Tag(await BuildExplicitAsync(named.Url, AdmiralTargetSourceEnum.Flag, named, flagToken, envToken, token).ConfigureAwait(false), "--profile");
            }

            string? envServer = Clean(_Environment(ServerUrlEnvironmentVariable));
            string envName = ServerUrlEnvironmentVariable;
            if (envServer == null)
            {
                envServer = Clean(_Environment(LegacyServerUrlEnvironmentVariable));
                envName = LegacyServerUrlEnvironmentVariable;
            }

            if (envServer != null)
            {
                string url = NormalizeServerUrl(envServer, envName);
                return Tag(await BuildExplicitAsync(url, AdmiralTargetSourceEnum.Environment, FindByUrl(url), flagToken, envToken, token).ConfigureAwait(false), envName);
            }

            ServerProfile? active = _Preferences.FindProfile(_Preferences.Current.ActiveProfile);
            if (active != null && !FollowsLocalAdmiral(active))
            {
                string url = NormalizeServerUrl(active.Url, "profile '" + active.Name + "'");
                return await BuildExplicitAsync(url, AdmiralTargetSourceEnum.Profile, active, flagToken, envToken, token).ConfigureAwait(false);
            }

            return Local(AdmiralTargetSourceEnum.LocalDefault, flagToken);
        }

        #endregion

        #region Private-Methods

        private AdmiralTarget Local(AdmiralTargetSourceEnum source, string? flagToken)
        {
            // The local target keeps the CLI's long-standing behavior: the local API key, unless --token names a user.
            if (flagToken != null)
                return new AdmiralTarget(LocalBaseUrl(_LocalPort), source, null, true, flagToken, null, AdmiralCredentialSourceEnum.Flag);
            return new AdmiralTarget(LocalBaseUrl(_LocalPort), source, null, true, null, _LocalApiKey,
                String.IsNullOrEmpty(_LocalApiKey) ? AdmiralCredentialSourceEnum.None : AdmiralCredentialSourceEnum.LocalApiKey);
        }

        private async Task<AdmiralTarget> BuildExplicitAsync(string url, AdmiralTargetSourceEnum source, ServerProfile? profile, string? flagToken, string? envToken, CancellationToken token)
        {
            bool isLocal = IsLocalAdmiralUrl(url);
            string? profileName = profile?.Name;
            if (flagToken != null) return new AdmiralTarget(url, source, profileName, isLocal, flagToken, null, AdmiralCredentialSourceEnum.Flag);
            if (envToken != null) return new AdmiralTarget(url, source, profileName, isLocal, envToken, null, AdmiralCredentialSourceEnum.Environment);
            if (isLocal)
            {
                return new AdmiralTarget(url, source, profileName, true, null, _LocalApiKey,
                    String.IsNullOrEmpty(_LocalApiKey) ? AdmiralCredentialSourceEnum.None : AdmiralCredentialSourceEnum.LocalApiKey);
            }

            if (profile != null)
            {
                string? stored = Clean(await _Credentials.GetAsync(SessionService.CredentialKey(profile), token).ConfigureAwait(false));
                if (stored != null) return new AdmiralTarget(url, source, profileName, false, stored, null, AdmiralCredentialSourceEnum.ProfileStore);
            }

            return new AdmiralTarget(url, source, profileName, false, null, null, AdmiralCredentialSourceEnum.None);
        }

        private static AdmiralTarget Tag(AdmiralTarget target, string detail)
        {
            target.SourceDetail = detail;
            return target;
        }

        private bool IsLocalAdmiralUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return false;
            return uri.Scheme == Uri.UriSchemeHttp && AdmiralTarget.IsLoopbackHost(uri.Host) && uri.Port == _LocalPort;
        }

        private ServerProfile? FindByUrl(string url)
        {
            foreach (ServerProfile p in _Preferences.Current.Profiles)
            {
                if (String.Equals((p.Url ?? String.Empty).Trim().TrimEnd('/'), url, StringComparison.OrdinalIgnoreCase)) return p;
            }

            return null;
        }

        private static string? Clean(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        #endregion
    }
}
