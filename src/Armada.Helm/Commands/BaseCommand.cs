namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Text.Json;
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
    /// Falls back to an embedded in-process Admiral when the server is not reachable.
    /// Auto-initializes settings on first use.
    /// </summary>
    public abstract class BaseCommand<TSettings> : AsyncCommand<TSettings> where TSettings : CommandSettings
    {
        #region Private-Members

        private static ArmadaApiClient? _ApiClient;
        private static readonly HttpClient _Client = new HttpClient();
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        private static bool _ServerReady = false;
        private static ArmadaSettings? _CachedSettings;
        private static bool _AutoInitDone = false;

        #endregion

        #region Protected-Methods

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
        /// Get the base URL for the Admiral API, reading port from settings.
        /// </summary>
        protected string GetBaseUrl()
        {
            ArmadaSettings settings = GetSettings();
            return "http://localhost:" + settings.AdmiralPort;
        }

        /// <summary>
        /// Get cached settings, loading from disk on first access.
        /// Auto-initializes settings if no config file exists.
        /// </summary>
        protected ArmadaSettings GetSettings()
        {
            if (_CachedSettings != null) return _CachedSettings;

            _CachedSettings = ArmadaSettings.LoadAsync().GetAwaiter().GetResult();

            if (!_AutoInitDone)
            {
                _AutoInitDone = true;
                AutoInitializeIfNeeded(_CachedSettings);
            }

            return _CachedSettings;
        }

        /// <summary>
        /// Get the typed API client, initializing if needed.
        /// </summary>
        protected ArmadaApiClient GetApiClient()
        {
            if (_ApiClient == null)
            {
                _ApiClient = new ArmadaApiClient(_Client, GetBaseUrl());
            }
            return _ApiClient;
        }

        /// <summary>
        /// Ensure the Admiral is reachable, starting the embedded server if needed.
        /// Also ensures default fleet exists.
        /// </summary>
        protected async Task EnsureServerAsync()
        {
            if (_ServerReady) return;

            bool healthy = await GetApiClient().HealthCheckAsync().ConfigureAwait(false);
            if (!healthy)
            {
                AnsiConsole.MarkupLine("[dim]Admiral not running -- starting embedded server...[/]");
                await EmbeddedServer.StartAsync().ConfigureAwait(false);
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

            // Auto-detect runtime
            ArmadaSettings settings = GetSettings();
            string runtimeValue = "ClaudeCode";

            if (!string.IsNullOrEmpty(settings.DefaultRuntime))
            {
                runtimeValue = settings.DefaultRuntime;
            }
            else
            {
                Armada.Core.Enums.AgentRuntimeEnum? detected = RuntimeDetectionService.DetectDefaultRuntime();
                if (detected == null)
                {
                    AnsiConsole.MarkupLine("[red]No agent runtimes found on PATH.[/]");
                    AnsiConsole.MarkupLine($"[dim]Install Claude Code: {RuntimeDetectionService.GetInstallHint(Armada.Core.Enums.AgentRuntimeEnum.ClaudeCode)}[/]");
                    AnsiConsole.MarkupLine($"[dim]Install Codex:       {RuntimeDetectionService.GetInstallHint(Armada.Core.Enums.AgentRuntimeEnum.Codex)}[/]");
                    return new List<Captain>();
                }
                runtimeValue = detected.Value.ToString();
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
                    // Backfill WorkingDirectory if it's missing and we're in a git repo
                    if (string.IsNullOrEmpty(match.WorkingDirectory) && GitInference.IsGitRepository(directory))
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
        /// Send a GET request and deserialize the response.
        /// </summary>
        protected async Task<T?> GetAsync<T>(string path) where T : class
        {
            await EnsureServerAsync().ConfigureAwait(false);
            HttpResponseMessage response = await _Client.GetAsync(GetBaseUrl() + path).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                throw new HttpRequestException($"HTTP {(int)response.StatusCode} on GET {path}: {errorBody}");
            }
            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(json, _JsonOptions);
        }

        /// <summary>
        /// Send a POST request with a body and deserialize the response.
        /// </summary>
        protected async Task<T?> PostAsync<T>(string path, object body) where T : class
        {
            await EnsureServerAsync().ConfigureAwait(false);
            HttpResponseMessage response = await _Client.PostAsJsonAsync(GetBaseUrl() + path, body, _JsonOptions).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {errorBody}");
            }
            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(json, _JsonOptions);
        }

        /// <summary>
        /// Send a POST request without a body.
        /// </summary>
        protected async Task PostAsync(string path)
        {
            await EnsureServerAsync().ConfigureAwait(false);
            HttpResponseMessage response = await _Client.PostAsync(GetBaseUrl() + path, null).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// Send a PUT request with a body and deserialize the response.
        /// </summary>
        protected async Task<T?> PutAsync<T>(string path, object body) where T : class
        {
            await EnsureServerAsync().ConfigureAwait(false);
            HttpResponseMessage response = await _Client.PutAsJsonAsync(GetBaseUrl() + path, body, _JsonOptions).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(json, _JsonOptions);
        }

        /// <summary>
        /// Send a DELETE request.
        /// </summary>
        protected async Task DeleteAsync(string path)
        {
            await EnsureServerAsync().ConfigureAwait(false);
            HttpResponseMessage response = await _Client.DeleteAsync(GetBaseUrl() + path).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }

        #endregion

        #region Private-Methods

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
                WorkingDirectory = GitInference.IsGitRepository(cwd) ? cwd : (string?)null
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
