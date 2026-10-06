namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core;
    using Armada.Core.Client;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Helm.Infrastructure;

    /// <summary>
    /// Base command providing typed API client to the Admiral API.
    /// Talks to the Admiral chosen by --server/--profile, ARMADA_SERVER_URL, the active profile, or the local default
    /// (see <see cref="AdmiralTargetResolver"/>). Falls back to an embedded in-process Admiral only when the local
    /// Admiral is the target and is not reachable; never for a remote target.
    /// Auto-initializes settings on first use.
    /// </summary>
    public abstract class BaseCommand<TSettings> : AsyncCommand<TSettings> where TSettings : CommandSettings
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        private ArmadaApiClient? _ApiClient;
        private HttpClient? _Client;
        private bool _ServerReady = false;
        private ArmadaSettings? _CachedSettings;
        private bool _AutoInitDone = false;
        private AdmiralTargetRequest _TargetRequest = new AdmiralTargetRequest();
        private AdmiralTarget? _Target;
        private bool _TargetNoticeShown = false;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Capture the targeting options (--server, --token, --profile) before the command runs.
        /// </summary>
        /// <param name="context">Command context.</param>
        /// <param name="settings">Settings.</param>
        /// <returns>Validation result.</returns>
        public override ValidationResult Validate(CommandContext context, TSettings settings)
        {
            if (settings is TargetSettings ts)
            {
                _TargetRequest = new AdmiralTargetRequest { Server = ts.Server, Token = ts.Token, Profile = ts.Profile };
            }

            return base.Validate(context, settings);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Commands that act only on this machine's Admiral (its process, settings file, or data) return their name and
        /// the reason; they are refused with <see cref="AdmiralTargetErrorEnum.LocalOnlyCommand"/> when a remote target is
        /// selected, instead of silently acting on the local Admiral. Null (the default) for remote-capable commands.
        /// </summary>
        protected virtual LocalOnlyCommandInfo? LocalOnly
        {
            get { return null; }
        }

        /// <summary>
        /// The Admiral this command talks to, resolved once per command (flag, environment, active profile, local).
        /// Throws <see cref="AdmiralTargetException"/> for a bad URL or profile, and for a local-only command with a
        /// remote target.
        /// </summary>
        /// <returns>Target.</returns>
        protected AdmiralTarget GetTarget()
        {
            if (_Target != null) return _Target;
            ArmadaSettings local = LoadLocalSettings();
            AdmiralTargetResolver resolver = AdmiralTargetResolver.CreateDefault(local.AdmiralPort, local.ApiKey);
            AdmiralTarget target = resolver.ResolveAsync(_TargetRequest).GetAwaiter().GetResult();
            LocalOnlyCommandInfo? localOnly = LocalOnly;
            if (localOnly != null && !target.IsLocal)
            {
                throw new AdmiralTargetException(
                    AdmiralTargetErrorEnum.LocalOnlyCommand,
                    "'armada " + localOnly.CommandName + "' acts only on this machine's Admiral (" + localOnly.Reason + "), but the target is "
                    + target.Describe() + ". Remove --server/--profile, unset " + AdmiralTargetResolver.ServerUrlEnvironmentVariable
                    + ", pass --profile local, or run 'armada profile use local'.",
                    localOnly.CommandName);
            }

            _Target = target;
            ShowTargetNotice(target);
            return target;
        }

        /// <summary>
        /// Resolve the target now, so a local-only command is refused before it touches anything.
        /// </summary>
        protected void RequireTarget()
        {
            GetTarget();
        }

        /// <summary>
        /// True when the resolved target is this machine's Admiral.
        /// </summary>
        /// <returns>True for local.</returns>
        protected bool IsLocalTarget()
        {
            return GetTarget().IsLocal;
        }

        /// <summary>
        /// Check if JSON output mode is enabled.
        /// </summary>
        protected bool IsJsonMode(TSettings settings)
        {
            if (settings is BaseSettings bs) return bs.Json;
            return false;
        }

        /// <summary>
        /// Build pagination querystring parameters from settings.
        /// </summary>
        protected string BuildPaginationQuery(TSettings settings)
        {
            List<string> parts = new List<string>();
            if (settings is BaseSettings bs)
            {
                if (bs.Page.HasValue) parts.Add("pageNumber=" + bs.Page.Value);
                if (bs.PageSize.HasValue) parts.Add("pageSize=" + bs.PageSize.Value);
            }
            return parts.Count > 0 ? string.Join("&", parts) : "";
        }

        /// <summary>
        /// Append pagination query params to a path.
        /// </summary>
        protected string AppendPagination(string path, TSettings settings)
        {
            string pq = BuildPaginationQuery(settings);
            if (string.IsNullOrEmpty(pq)) return path;
            return path + (path.Contains("?") ? "&" : "?") + pq;
        }

        /// <summary>
        /// Write an object as JSON to stdout.
        /// </summary>
        protected void WriteJson(object? value)
        {
            string json = JsonSerializer.Serialize(value, _JsonOptions);
            Console.WriteLine(json);
        }

        /// <summary>
        /// Get the base URL of the target Admiral (see <see cref="GetTarget"/>).
        /// </summary>
        protected string GetBaseUrl()
        {
            return GetTarget().BaseUrl;
        }

        /// <summary>
        /// The local Admiral's base URL (<c>http://127.0.0.1:&lt;admiralPort&gt;</c>), whatever the target.
        /// </summary>
        /// <returns>URL.</returns>
        protected string GetLocalBaseUrl()
        {
            return AdmiralTargetResolver.LocalBaseUrl((_CachedSettings ?? LoadLocalSettings()).AdmiralPort);
        }

        /// <summary>
        /// Create a short-lived HTTP client carrying the target's credential, for the server control endpoints (stop,
        /// restart), which always require an admin credential.
        /// </summary>
        /// <param name="timeout">Request timeout.</param>
        /// <returns>HTTP client; the caller disposes it.</returns>
        protected HttpClient CreateAdminHttpClient(TimeSpan timeout)
        {
            HttpClient client = new HttpClient { Timeout = timeout };
            GetTarget().ApplyCredentials(client.DefaultRequestHeaders);
            return client;
        }

        /// <summary>
        /// Get cached local settings, loading from disk on first access. Auto-initializes the local settings file
        /// when no config file exists and the target is the local Admiral.
        /// </summary>
        protected ArmadaSettings GetSettings()
        {
            if (_CachedSettings != null) return _CachedSettings;

            _CachedSettings = LoadLocalSettings();

            if (!_AutoInitDone && GetTarget().IsLocal)
            {
                _AutoInitDone = true;
                AutoInitializeIfNeeded(_CachedSettings);
            }

            return _CachedSettings;
        }

        /// <summary>
        /// The shared HTTP client for the target, with its credential headers applied.
        /// </summary>
        /// <returns>Client.</returns>
        protected HttpClient GetHttpClient()
        {
            if (_Client != null) return _Client;
            AdmiralTarget target = GetTarget();
            HttpClient client = new HttpClient();
            target.ApplyCredentials(client.DefaultRequestHeaders);
            _Client = client;
            return client;
        }

        /// <summary>
        /// Get the typed API client for the target, initializing if needed.
        /// </summary>
        protected ArmadaApiClient GetApiClient()
        {
            if (_ApiClient == null)
            {
                _ApiClient = new ArmadaApiClient(GetHttpClient(), GetBaseUrl());
            }
            return _ApiClient;
        }

        /// <summary>
        /// Ensure the Admiral is reachable. For the local Admiral, starts the embedded server if needed and ensures a
        /// default fleet exists. For any other target, only checks reachability: it never starts a local server and
        /// never creates data implicitly.
        /// </summary>
        /// <exception cref="AdmiralTargetException">Unreachable, for a remote target that does not answer.</exception>
        protected async Task EnsureServerAsync()
        {
            if (_ServerReady) return;

            AdmiralTarget target = GetTarget();
            bool healthy = await GetApiClient().HealthCheckAsync().ConfigureAwait(false);
            if (!target.IsLocal)
            {
                if (!healthy)
                {
                    throw new AdmiralTargetException(
                        AdmiralTargetErrorEnum.Unreachable,
                        "Cannot reach the Admiral at " + target.Describe() + ": GET /api/v1/status/health failed. Check the URL, that the Admiral listens on a "
                        + "non-loopback rest.hostname (or sits behind your proxy), and the firewall. Armada never starts a local server for a remote target.");
                }

                _ServerReady = true;
                return;
            }

            if (!healthy)
            {
                AnsiConsole.MarkupLine("[dim]Admiral not running -- starting embedded server...[/]");
                await EmbeddedServer.StartAsync().ConfigureAwait(false);

                // The embedded server may have just generated the local API key; reload settings and re-resolve the
                // target so subsequent authenticated calls carry it.
                _CachedSettings = await ArmadaSettings.LoadAsync().ConfigureAwait(false);
                if (target.CredentialSource != AdmiralCredentialSourceEnum.Flag)
                {
                    _Target = null;
                    _Client = null;
                    _ApiClient = null;
                    GetTarget();
                }
            }

            // Mark server as available (whether external or embedded)
            // to prevent re-checking on every API call.
            _ServerReady = true;

            // Ensure default fleet exists
            await EnsureDefaultFleetAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Ensure a default fleet exists, creating one if needed.
        /// </summary>
        protected async Task EnsureDefaultFleetAsync()
        {
            try
            {
                EnumerationResult<Fleet>? fleetResult = await GetAsync<EnumerationResult<Fleet>>("/api/v1/fleets").ConfigureAwait(false);
                List<Fleet>? fleets = fleetResult?.Objects;
                if (fleets != null && fleets.Count > 0) return;

                // Create default fleet
                Fleet? defaultFleet = await PostAsync<Fleet>("/api/v1/fleets", new
                {
                    Name = Constants.DefaultFleetName,
                    Description = "Default fleet (auto-created)"
                }).ConfigureAwait(false);

                if (defaultFleet != null)
                {
                    AnsiConsole.MarkupLine($"[dim]Created default fleet.[/]");
                }
            }
            catch
            {
                // Best-effort; don't block command execution
            }
        }

        /// <summary>
        /// Ensure at least one captain exists, auto-creating if needed.
        /// Returns the list of captains (may be empty if creation failed).
        /// </summary>
        protected async Task<List<Captain>> EnsureCaptainsAsync()
        {
            EnumerationResult<Captain>? captainResult = await GetAsync<EnumerationResult<Captain>>("/api/v1/captains").ConfigureAwait(false);
            List<Captain>? captains = captainResult?.Objects;
            if (captains != null && captains.Count > 0) return captains;

            // Runtime detection looks at this machine's PATH, which says nothing about the remote Admiral or its
            // Harbors, so never auto-create captains on a remote target.
            if (!IsLocalTarget())
            {
                AnsiConsole.MarkupLine("[red]No captains on " + Markup.Escape(GetTarget().Describe()) + ".[/]");
                AnsiConsole.MarkupLine("[dim]Add one there with: armada captain add <name> --runtime <runtime> (it runs on the Admiral's host or a Harbor).[/]");
                return new List<Captain>();
            }

            // Auto-detect runtime
            ArmadaSettings settings = GetSettings();
            Armada.Core.Enums.AgentRuntimeEnum runtimeValue = Armada.Core.Enums.AgentRuntimeEnum.ClaudeCode;

            if (!string.IsNullOrEmpty(settings.DefaultRuntime))
            {
                if (!AgentRuntimeParser.TryParse(settings.DefaultRuntime, out runtimeValue))
                {
                    AnsiConsole.MarkupLine("[red]DefaultRuntime setting: " + Markup.Escape(AgentRuntimeParser.DescribeInvalid(settings.DefaultRuntime)) + "[/]");
                    return new List<Captain>();
                }

                if (runtimeValue == Armada.Core.Enums.AgentRuntimeEnum.Mux)
                {
                    AnsiConsole.MarkupLine("[yellow]DefaultRuntime is set to Mux, but Armada cannot auto-create a Mux captain without a named endpoint.[/]");
                    AnsiConsole.MarkupLine("[dim]Create one explicitly with: armada captain add <name> --runtime mux --mux-endpoint <endpoint-name>[/]");
                    return new List<Captain>();
                }
            }
            else
            {
                Armada.Core.Enums.AgentRuntimeEnum? detected = RuntimeDetectionService.DetectDefaultRuntime();
                if (detected == null)
                {
                    AnsiConsole.MarkupLine("[red]No agent runtimes found on PATH.[/]");
                    AnsiConsole.MarkupLine($"[dim]Install Claude Code: {RuntimeDetectionService.GetInstallHint(Armada.Core.Enums.AgentRuntimeEnum.ClaudeCode)}[/]");
                    AnsiConsole.MarkupLine($"[dim]Install Codex:       {RuntimeDetectionService.GetInstallHint(Armada.Core.Enums.AgentRuntimeEnum.Codex)}[/]");
                    AnsiConsole.MarkupLine($"[dim]Install Mux:         {RuntimeDetectionService.GetInstallHint(Armada.Core.Enums.AgentRuntimeEnum.Mux)}[/]");
                    return new List<Captain>();
                }

                if (detected.Value == Armada.Core.Enums.AgentRuntimeEnum.Mux)
                {
                    AnsiConsole.MarkupLine("[yellow]Mux was detected, but Armada cannot auto-create a Mux captain without a named endpoint.[/]");
                    AnsiConsole.MarkupLine("[dim]Create one explicitly with: armada captain add <name> --runtime mux --mux-endpoint <endpoint-name>[/]");
                    return new List<Captain>();
                }

                runtimeValue = detected.Value;
            }

            // Create a captain
            Captain? captain = await PostAsync<Captain>("/api/v1/captains", new
            {
                Name = "captain-1",
                Runtime = runtimeValue
            }).ConfigureAwait(false);

            if (captain != null)
            {
                AnsiConsole.MarkupLine($"[dim]Auto-created captain-1 ({runtimeValue}).[/]");
                return new List<Captain> { captain };
            }

            return new List<Captain>();
        }

        /// <summary>
        /// Resolve a vessel from the current working directory, or auto-register it.
        /// </summary>
        /// <param name="repoPath">Optional explicit repo path or URL. If null, uses CWD.</param>
        /// <returns>Vessel ID if resolved, null otherwise.</returns>
        protected async Task<string?> ResolveOrRegisterVesselAsync(string? repoPath = null)
        {
            string directory = repoPath ?? Directory.GetCurrentDirectory();

            // If it's a URL, handle inline registration
            if (directory.StartsWith("http://") || directory.StartsWith("https://") || directory.StartsWith("git@"))
            {
                return await RegisterVesselFromUrlAsync(directory).ConfigureAwait(false);
            }

            // Resolve "." to CWD
            if (directory == ".") directory = Directory.GetCurrentDirectory();

            // Check if it's a git repo
            if (!GitInference.IsGitRepository(directory))
            {
                AnsiConsole.MarkupLine($"[red]Not a git repository:[/] {Markup.Escape(directory)}");
                return null;
            }

            string? remoteUrl = GitInference.GetRemoteUrl(directory);

            // Check existing vessels
            EnumerationResult<Vessel>? vesselResult = await GetAsync<EnumerationResult<Vessel>>("/api/v1/vessels").ConfigureAwait(false);
            List<Vessel>? vessels = vesselResult?.Objects;
            if (vessels != null && vessels.Count > 0)
            {
                Vessel? match = null;

                // Match by remote URL
                if (!string.IsNullOrEmpty(remoteUrl))
                {
                    match = EntityResolver.ResolveVesselByRemoteUrl(vessels, remoteUrl);
                }

                // If only one vessel, use it
                if (match == null && vessels.Count == 1)
                {
                    match = vessels[0];
                }

                if (match != null)
                {
                    // Backfill WorkingDirectory if it's missing and we're in a git repo. A local path means nothing to a
                    // remote Admiral, so only the local Admiral gets it.
                    if (IsLocalTarget() && string.IsNullOrEmpty(match.WorkingDirectory) && GitInference.IsGitRepository(directory))
                    {
                        match.WorkingDirectory = directory;
                        try { await PutAsync<Vessel>($"/api/v1/vessels/{match.Id}", match).ConfigureAwait(false); }
                        catch { }
                    }

                    return match.Id;
                }
            }

            // Auto-register
            if (string.IsNullOrEmpty(remoteUrl))
            {
                AnsiConsole.MarkupLine("[red]No git remote 'origin' found.[/] Register a vessel manually with [green]armada vessel add[/].");
                return null;
            }

            return await RegisterVesselFromUrlAsync(remoteUrl).ConfigureAwait(false);
        }

        /// <summary>
        /// Read the last lines of a session log through the REST API (used for remote targets, whose log files live on
        /// the Admiral's host).
        /// </summary>
        /// <param name="kind"><c>missions</c> or <c>captains</c>.</param>
        /// <param name="id">Mission or captain id.</param>
        /// <param name="lineCount">Lines to return from the end.</param>
        /// <returns>The page; <see cref="RemoteLogResponse.TotalLines"/> is the log length.</returns>
        protected async Task<RemoteLogResponse> GetRemoteLogTailAsync(string kind, string id, int lineCount)
        {
            RemoteLogResponse? probe = await GetAsync<RemoteLogResponse>("/api/v1/" + kind + "/" + Uri.EscapeDataString(id) + "/log?lines=1&offset=0").ConfigureAwait(false);
            int total = probe?.TotalLines ?? 0;
            int count = Math.Max(1, lineCount);
            int offset = Math.Max(0, total - count);
            RemoteLogResponse? page = await GetRemoteLogPageAsync(kind, id, offset, count).ConfigureAwait(false);
            return page;
        }

        /// <summary>
        /// Read a page of a session log through the REST API.
        /// </summary>
        /// <param name="kind"><c>missions</c> or <c>captains</c>.</param>
        /// <param name="id">Mission or captain id.</param>
        /// <param name="offset">First line (0-based).</param>
        /// <param name="lineCount">Maximum lines.</param>
        /// <returns>The page.</returns>
        protected async Task<RemoteLogResponse> GetRemoteLogPageAsync(string kind, string id, int offset, int lineCount)
        {
            RemoteLogResponse? page = await GetAsync<RemoteLogResponse>("/api/v1/" + kind + "/" + Uri.EscapeDataString(id) + "/log?lines=" + Math.Max(1, lineCount) + "&offset=" + Math.Max(0, offset)).ConfigureAwait(false);
            return page ?? new RemoteLogResponse();
        }

        /// <summary>
        /// Send a GET request and deserialize the response.
        /// </summary>
        protected async Task<T?> GetAsync<T>(string path) where T : class
        {
            await EnsureServerAsync().ConfigureAwait(false);
            HttpResponseMessage response = await GetHttpClient().GetAsync(GetBaseUrl() + path).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw await FailAsync(response, "GET " + path).ConfigureAwait(false);
            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(json, _JsonOptions);
        }

        /// <summary>
        /// Send a POST request with a body and deserialize the response.
        /// </summary>
        protected async Task<T?> PostAsync<T>(string path, object body) where T : class
        {
            await EnsureServerAsync().ConfigureAwait(false);
            HttpResponseMessage response = await GetHttpClient().PostAsJsonAsync(GetBaseUrl() + path, body, _JsonOptions).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw await FailAsync(response, "POST " + path).ConfigureAwait(false);
            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(json, _JsonOptions);
        }

        /// <summary>
        /// Send a POST request without a body.
        /// </summary>
        protected async Task PostAsync(string path)
        {
            await EnsureServerAsync().ConfigureAwait(false);
            HttpResponseMessage response = await GetHttpClient().PostAsync(GetBaseUrl() + path, null).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw await FailAsync(response, "POST " + path).ConfigureAwait(false);
        }

        /// <summary>
        /// Send a PUT request with a body and deserialize the response.
        /// </summary>
        protected async Task<T?> PutAsync<T>(string path, object body) where T : class
        {
            await EnsureServerAsync().ConfigureAwait(false);
            HttpResponseMessage response = await GetHttpClient().PutAsJsonAsync(GetBaseUrl() + path, body, _JsonOptions).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw await FailAsync(response, "PUT " + path).ConfigureAwait(false);
            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(json, _JsonOptions);
        }

        /// <summary>
        /// Send a DELETE request.
        /// </summary>
        protected async Task DeleteAsync(string path)
        {
            await EnsureServerAsync().ConfigureAwait(false);
            HttpResponseMessage response = await GetHttpClient().DeleteAsync(GetBaseUrl() + path).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw await FailAsync(response, "DELETE " + path).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task<Exception> FailAsync(HttpResponseMessage response, string description)
        {
            AdmiralTarget target = GetTarget();
            if (!target.IsLocal && (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden))
                return CredentialError(target, response.StatusCode, description);
            return await HelmHttpError.FromResponseAsync(response, description).ConfigureAwait(false);
        }

        /// <summary>
        /// The typed error for a 401 or 403 from a remote Admiral, naming the target and where the credential came from.
        /// </summary>
        /// <param name="target">Target.</param>
        /// <param name="status">401 or 403.</param>
        /// <param name="description">Request description.</param>
        /// <returns>Exception.</returns>
        internal static AdmiralTargetException CredentialError(AdmiralTarget target, HttpStatusCode status, string description)
        {
            if (status == HttpStatusCode.Forbidden)
            {
                return new AdmiralTargetException(
                    AdmiralTargetErrorEnum.Forbidden,
                    "HTTP 403 on " + description + ": the Admiral at " + target.Describe() + " accepted the credential (" + target.DescribeCredential()
                    + ") but its user may not do this. Ask an administrator for the needed role, or use a credential of a user who has it.");
            }

            if (!target.HasCredential)
            {
                return new AdmiralTargetException(
                    AdmiralTargetErrorEnum.Unauthorized,
                    "HTTP 401 on " + description + ": the Admiral at " + target.Describe() + " requires a credential and none was sent. Pass --token <bearer>, set "
                    + AdmiralTargetResolver.TokenEnvironmentVariable + ", or store one with 'armada profile add <name> --server " + target.BaseUrl + " --token <bearer>'.");
            }

            return new AdmiralTargetException(
                AdmiralTargetErrorEnum.Unauthorized,
                "HTTP 401 on " + description + ": the Admiral at " + target.Describe() + " rejected the credential (" + target.DescribeCredential()
                + "). It may be wrong, inactive, or an expired session; create a bearer token (dashboard: Credentials) and pass it with --token or store it with 'armada profile add'.");
        }

        private static ArmadaSettings LoadLocalSettings()
        {
            return ArmadaSettings.LoadAsync().GetAwaiter().GetResult();
        }

        private void ShowTargetNotice(AdmiralTarget target)
        {
            if (_TargetNoticeShown) return;
            _TargetNoticeShown = true;
            if (!target.IsLocal && target.Source != AdmiralTargetSourceEnum.Flag)
                Console.Error.WriteLine("armada: target is " + target.Describe());
            if (target.SendsCredentialInsecurely)
                Console.Error.WriteLine("armada: warning: sending a credential over plain HTTP to " + target.BaseUrl + "; put the Admiral behind a TLS-terminating proxy and use https://.");
        }

        /// <summary>
        /// Auto-initialize settings on first use if no settings file exists.
        /// </summary>
        private void AutoInitializeIfNeeded(ArmadaSettings settings)
        {
            if (File.Exists(ArmadaSettings.DefaultSettingsPath)) return;

            // First run — save defaults silently
            settings.InitializeDirectories();
            settings.SaveAsync().GetAwaiter().GetResult();
            AnsiConsole.MarkupLine($"[dim]Initialized Armada config at {Markup.Escape(ArmadaSettings.DefaultSettingsPath)}[/]");
        }

        /// <summary>
        /// Register a vessel from a git remote URL.
        /// </summary>
        private async Task<string?> RegisterVesselFromUrlAsync(string repoUrl)
        {
            string name = GitInference.InferVesselName(repoUrl);
            string branch = "main";

            // Try to detect default branch from local repo if in one
            string cwd = Directory.GetCurrentDirectory();
            if (GitInference.IsGitRepository(cwd))
            {
                branch = GitInference.GetDefaultBranch(cwd);
            }

            // Get default fleet
            EnumerationResult<Fleet>? fleetResult = await GetAsync<EnumerationResult<Fleet>>("/api/v1/fleets").ConfigureAwait(false);
            string? fleetId = fleetResult?.Objects?.FirstOrDefault()?.Id;

            Vessel? vessel = await PostAsync<Vessel>("/api/v1/vessels", new
            {
                Name = name,
                RepoUrl = repoUrl,
                FleetId = fleetId,
                DefaultBranch = branch,
                WorkingDirectory = IsLocalTarget() && GitInference.IsGitRepository(cwd) ? cwd : (string?)null
            }).ConfigureAwait(false);

            if (vessel != null)
            {
                AnsiConsole.MarkupLine($"[dim]Auto-registered vessel '{Markup.Escape(vessel.Name)}' from {Markup.Escape(repoUrl)}[/]");
                return vessel.Id;
            }

            return null;
        }

        #endregion
    }
}
