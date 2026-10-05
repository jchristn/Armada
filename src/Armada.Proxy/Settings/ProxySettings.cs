namespace Armada.Proxy.Settings
{
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core;
    using SyslogLogging;

    /// <summary>
    /// Runtime settings for Armada.Proxy.
    /// </summary>
    public class ProxySettings
    {
        #region Public-Members

        /// <summary>
        /// Root data directory for Armada.Proxy.
        /// </summary>
        public string DataDirectory
        {
            get => _DataDirectory;
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(DataDirectory));
                _DataDirectory = value;
            }
        }

        /// <summary>
        /// Directory for Armada.Proxy log files.
        /// </summary>
        public string LogDirectory
        {
            get => _LogDirectory;
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(LogDirectory));
                _LogDirectory = value;
            }
        }

        /// <summary>
        /// Interface hostname to bind.
        /// </summary>
        public string Hostname { get; set; } = "localhost";

        /// <summary>
        /// HTTP port to bind.
        /// </summary>
        public int Port
        {
            get => _Port;
            set
            {
                if (value < 1 || value > 65535) throw new ArgumentOutOfRangeException(nameof(Port), "Must be in range [1, 65535]");
                _Port = value;
            }
        }

        /// <summary>
        /// Whether enrollment tokens are required for handshake acceptance.
        /// </summary>
        public bool RequireEnrollmentToken { get; set; } = false;

        /// <summary>
        /// Accepted enrollment tokens.
        /// </summary>
        public List<string> EnrollmentTokens { get; set; } = new List<string>();

        /// <summary>
        /// Shared password required for tunnel and browser authentication.
        /// Blank values normalize to the built-in default "armadaadmin", which the proxy refuses to start with unless
        /// <see cref="AllowDefaultPassword"/> is set.
        /// </summary>
        public string? Password
        {
            get => _Password;
            set => _Password = RemoteTunnelAuth.NormalizePassword(value);
        }

        /// <summary>
        /// Allow the proxy to start while the shared password is the built-in default ("armadaadmin") or blank.
        /// Default false: the proxy refuses to start until a real password is configured
        /// (<c>Password</c> in proxysettings.json or the ARMADA_PROXY_PASSWORD environment variable). Set this only for
        /// a throwaway local test; it can also be set with ARMADA_PROXY_ALLOW_DEFAULT_PASSWORD=true.
        /// </summary>
        public bool AllowDefaultPassword { get; set; } = false;

        /// <summary>
        /// Trust the X-Forwarded-For, Forwarded, and X-Forwarded-Proto headers to identify the client address and scheme.
        /// Default false: the socket peer address is used. Enable only when the proxy sits behind a reverse proxy that
        /// overwrites these headers.
        /// </summary>
        public bool TrustForwardedHeaders { get; set; } = false;

        /// <summary>
        /// Always mark the browser session cookie Secure. Default false; the cookie is also marked Secure when
        /// <see cref="TrustForwardedHeaders"/> is on and the request arrived as HTTPS (X-Forwarded-Proto: https).
        /// Enable when the proxy is served over HTTPS.
        /// </summary>
        public bool SecureCookie { get; set; } = false;

        /// <summary>
        /// Failed browser logins or tunnel handshakes from one client address, within
        /// <see cref="LoginFailureWindowSeconds"/>, that trigger a lockout. Default 10, range 1 to 1000.
        /// </summary>
        public int LoginMaxFailures
        {
            get => _LoginMaxFailures;
            set
            {
                if (value < 1 || value > 1000) throw new ArgumentOutOfRangeException(nameof(LoginMaxFailures), "Must be in range [1, 1000]");
                _LoginMaxFailures = value;
            }
        }

        /// <summary>
        /// Window in seconds over which failed logins are counted. Default 900 (15 minutes), range 10 to 86400.
        /// </summary>
        public int LoginFailureWindowSeconds
        {
            get => _LoginFailureWindowSeconds;
            set
            {
                if (value < 10 || value > 86400) throw new ArgumentOutOfRangeException(nameof(LoginFailureWindowSeconds), "Must be in range [10, 86400]");
                _LoginFailureWindowSeconds = value;
            }
        }

        /// <summary>
        /// Lockout duration in seconds once <see cref="LoginMaxFailures"/> is reached; requests during the lockout get
        /// 429 with Retry-After. Default 900 (15 minutes), range 1 to 86400.
        /// </summary>
        public int LoginLockoutSeconds
        {
            get => _LoginLockoutSeconds;
            set
            {
                if (value < 1 || value > 86400) throw new ArgumentOutOfRangeException(nameof(LoginLockoutSeconds), "Must be in range [1, 86400]");
                _LoginLockoutSeconds = value;
            }
        }

        /// <summary>
        /// Whether the configured shared password is the built-in default (blank values normalize to the default).
        /// </summary>
        public bool IsDefaultPassword
        {
            get => String.Equals(RemoteTunnelAuth.NormalizePassword(_Password), Constants.DefaultRemoteTunnelPassword, StringComparison.Ordinal);
        }

        /// <summary>
        /// Initial handshake timeout in seconds.
        /// </summary>
        public int HandshakeTimeoutSeconds
        {
            get => _HandshakeTimeoutSeconds;
            set
            {
                if (value < 1 || value > 300) throw new ArgumentOutOfRangeException(nameof(HandshakeTimeoutSeconds), "Must be in range [1, 300]");
                _HandshakeTimeoutSeconds = value;
            }
        }

        /// <summary>
        /// Time after which a connected instance is considered stale without tunnel activity.
        /// </summary>
        public int StaleAfterSeconds
        {
            get => _StaleAfterSeconds;
            set
            {
                if (value < 5 || value > 86400) throw new ArgumentOutOfRangeException(nameof(StaleAfterSeconds), "Must be in range [5, 86400]");
                _StaleAfterSeconds = value;
            }
        }

        /// <summary>
        /// Timeout for live request/response calls over the tunnel.
        /// </summary>
        public int RequestTimeoutSeconds
        {
            get => _RequestTimeoutSeconds;
            set
            {
                if (value < 1 || value > 300) throw new ArgumentOutOfRangeException(nameof(RequestTimeoutSeconds), "Must be in range [1, 300]");
                _RequestTimeoutSeconds = value;
            }
        }

        /// <summary>
        /// Maximum retained recent events per instance.
        /// </summary>
        public int MaxRecentEvents
        {
            get => _MaxRecentEvents;
            set
            {
                if (value < 1 || value > 500) throw new ArgumentOutOfRangeException(nameof(MaxRecentEvents), "Must be in range [1, 500]");
                _MaxRecentEvents = value;
            }
        }

        /// <summary>
        /// Syslog targets for proxy process logging.
        /// Defaults to a local syslog listener on 127.0.0.1:514.
        /// </summary>
        public List<SyslogServer> SyslogServers { get; set; } = new List<SyslogServer>
        {
            new SyslogServer("127.0.0.1", 514)
        };

        /// <summary>
        /// Normalize configured enrollment tokens by trimming blanks.
        /// </summary>
        /// <returns>Distinct non-empty tokens.</returns>
        public HashSet<string> GetEnrollmentTokenSet()
        {
            return new HashSet<string>(
                EnrollmentTokens
                    .Where(token => !String.IsNullOrWhiteSpace(token))
                    .Select(token => token.Trim()),
                StringComparer.Ordinal);
        }

        /// <summary>
        /// Load proxy settings from disk.
        /// </summary>
        /// <param name="explicitConfigPath">Optional explicit JSON configuration path.</param>
        /// <returns>Loaded settings with defaults applied.</returns>
        /// <exception cref="InvalidDataException">A settings file is not a JSON object or a property has the wrong
        /// type.</exception>
        public static ProxySettings Load(string? explicitConfigPath = null)
        {
            ProxySettings settings = new ProxySettings();

            foreach (string path in GetCandidateConfigPaths(explicitConfigPath))
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                ProxySettingsFileRoot root = ReadSettingsFile(path);
                settings.Apply(root.ArmadaProxy ?? root);
            }

            settings.ApplyEnvironmentOverrides();
            return settings;
        }

        /// <summary>
        /// Load settings from a single file on top of the defaults, without other candidate files or environment
        /// overrides.
        /// </summary>
        /// <param name="path">JSON settings file.</param>
        /// <returns>Loaded settings.</returns>
        /// <exception cref="InvalidDataException">The file is not a JSON object or a property has the wrong type.</exception>
        public static ProxySettings LoadFromFile(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            ProxySettings settings = new ProxySettings();
            ProxySettingsFileRoot root = ReadSettingsFile(Path.GetFullPath(path));
            settings.Apply(root.ArmadaProxy ?? root);
            return settings;
        }

        /// <summary>
        /// Name of the environment variable that sets the shared proxy password (overrides the settings file).
        /// </summary>
        public const string PasswordEnvironmentVariable = "ARMADA_PROXY_PASSWORD";

        /// <summary>
        /// Name of the environment variable that sets <see cref="AllowDefaultPassword"/> (overrides the settings file).
        /// </summary>
        public const string AllowDefaultPasswordEnvironmentVariable = "ARMADA_PROXY_ALLOW_DEFAULT_PASSWORD";

        /// <summary>
        /// Return the reason the proxy must not start with these settings, or null when it may start. The proxy refuses
        /// to start with the built-in default password unless <see cref="AllowDefaultPassword"/> is set.
        /// </summary>
        /// <returns>Error text, or null.</returns>
        public string? GetStartupSecurityError()
        {
            if (IsDefaultPassword && !AllowDefaultPassword)
            {
                return "Refusing to start: the proxy password is the built-in default (\"" + Constants.DefaultRemoteTunnelPassword + "\") or blank. " +
                    "Set a strong shared password in proxysettings.json (\"Password\") or the " + PasswordEnvironmentVariable + " environment variable, " +
                    "and set the same value as RemoteControl.Password on each Armada instance. " +
                    "For a throwaway local test only, set \"AllowDefaultPassword\": true or " + AllowDefaultPasswordEnvironmentVariable + "=true.";
            }

            return null;
        }

        /// <summary>
        /// Apply environment variable overrides (<see cref="PasswordEnvironmentVariable"/>,
        /// <see cref="AllowDefaultPasswordEnvironmentVariable"/>).
        /// </summary>
        public void ApplyEnvironmentOverrides()
        {
            string? password = Environment.GetEnvironmentVariable(PasswordEnvironmentVariable);
            if (!String.IsNullOrWhiteSpace(password)) Password = password;

            string? allowDefault = Environment.GetEnvironmentVariable(AllowDefaultPasswordEnvironmentVariable);
            if (!String.IsNullOrWhiteSpace(allowDefault) && Boolean.TryParse(allowDefault.Trim(), out bool allow)) AllowDefaultPassword = allow;
        }

        /// <summary>
        /// Ensure the configured proxy directories exist.
        /// </summary>
        public void InitializeDirectories()
        {
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(LogDirectory);
        }

        #endregion

        #region Private-Members

        private string _DataDirectory = Constants.DefaultDataDirectory;
        private string _LogDirectory = Path.Combine(Constants.DefaultDataDirectory, "logs");
        private int _Port = Constants.DefaultProxyPort;
        private int _HandshakeTimeoutSeconds = Constants.DefaultProxyHandshakeTimeoutSeconds;
        private int _StaleAfterSeconds = Constants.DefaultProxyStaleAfterSeconds;
        private int _RequestTimeoutSeconds = Constants.DefaultProxyRequestTimeoutSeconds;
        private int _MaxRecentEvents = 50;
        private string? _Password = Constants.DefaultRemoteTunnelPassword;
        private int _LoginMaxFailures = 10;
        private int _LoginFailureWindowSeconds = 900;
        private int _LoginLockoutSeconds = 900;

        #endregion

        #region Private-Methods

        private static readonly JsonSerializerOptions _FileJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private static ProxySettingsFileRoot ReadSettingsFile(string path)
        {
            try
            {
                ProxySettingsFileRoot? root = JsonSerializer.Deserialize<ProxySettingsFileRoot>(File.ReadAllText(path), _FileJsonOptions);
                if (root == null)
                {
                    throw new InvalidDataException("Proxy settings file " + path + " must contain a JSON object.");
                }

                return root;
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("Proxy settings file " + path + " is invalid: " + ex.Message, ex);
            }
        }

        private void Apply(ProxySettingsFile section)
        {
            if (section.DataDirectory != null) DataDirectory = section.DataDirectory;
            if (section.LogDirectory != null) LogDirectory = section.LogDirectory;
            if (section.Hostname != null) Hostname = section.Hostname;
            if (section.Port.HasValue) Port = section.Port.Value;
            if (section.RequireEnrollmentToken.HasValue) RequireEnrollmentToken = section.RequireEnrollmentToken.Value;
            if (section.HandshakeTimeoutSeconds.HasValue) HandshakeTimeoutSeconds = section.HandshakeTimeoutSeconds.Value;
            if (section.StaleAfterSeconds.HasValue) StaleAfterSeconds = section.StaleAfterSeconds.Value;
            if (section.RequestTimeoutSeconds.HasValue) RequestTimeoutSeconds = section.RequestTimeoutSeconds.Value;
            if (section.MaxRecentEvents.HasValue) MaxRecentEvents = section.MaxRecentEvents.Value;
            if (section.AllowDefaultPassword.HasValue) AllowDefaultPassword = section.AllowDefaultPassword.Value;
            if (section.TrustForwardedHeaders.HasValue) TrustForwardedHeaders = section.TrustForwardedHeaders.Value;
            if (section.SecureCookie.HasValue) SecureCookie = section.SecureCookie.Value;
            if (section.LoginMaxFailures.HasValue) LoginMaxFailures = section.LoginMaxFailures.Value;
            if (section.LoginFailureWindowSeconds.HasValue) LoginFailureWindowSeconds = section.LoginFailureWindowSeconds.Value;
            if (section.LoginLockoutSeconds.HasValue) LoginLockoutSeconds = section.LoginLockoutSeconds.Value;
            if (section.Password != null) Password = section.Password;

            if (section.SyslogServers != null)
            {
                SyslogServers = section.SyslogServers
                    .Where(server => server != null && !String.IsNullOrWhiteSpace(server.Hostname))
                    .ToList();
            }

            if (section.EnrollmentTokens != null)
            {
                EnrollmentTokens = section.EnrollmentTokens
                    .Where(token => !String.IsNullOrWhiteSpace(token))
                    .Select(token => token.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            }
        }

        private static IEnumerable<string> GetCandidateConfigPaths(string? explicitConfigPath)
        {
            if (!String.IsNullOrWhiteSpace(explicitConfigPath))
            {
                yield return Path.GetFullPath(explicitConfigPath);
            }

            string? environmentPath = Environment.GetEnvironmentVariable("ARMADA_PROXY_SETTINGS_FILE");
            if (!String.IsNullOrWhiteSpace(environmentPath))
            {
                yield return Path.GetFullPath(environmentPath);
            }

            string baseDirectory = AppContext.BaseDirectory;
            string currentDirectory = Directory.GetCurrentDirectory();

            yield return Path.Combine(baseDirectory, "proxysettings.json");
            yield return Path.Combine(currentDirectory, "proxysettings.json");
            yield return Path.Combine(baseDirectory, "appsettings.json");
            yield return Path.Combine(currentDirectory, "appsettings.json");
            yield return Path.Combine(Constants.DefaultDataDirectory, "proxysettings.json");
        }

        #endregion
    }
}
