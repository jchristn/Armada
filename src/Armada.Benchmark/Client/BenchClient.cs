namespace Armada.Benchmark.Client
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// The only place that knows Admiral routes and payload shapes. One pooled HTTP client with an explicit connection
    /// limit and timeout (both recorded in the report); every call is a public REST request a client could make.
    /// </summary>
    public sealed class BenchClient : IDisposable
    {
        #region Public-Members

        /// <summary>Maximum concurrent connections to the Admiral.</summary>
        public const int MaxConnections = 32;

        /// <summary>Per-request timeout in seconds.</summary>
        public const int TimeoutSeconds = 120;

        /// <summary>Requests that returned a non-success status.</summary>
        public int Errors => _Errors;

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        private readonly HttpClient _Http;
        private int _Errors;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Create a client for one Admiral.</summary>
        /// <param name="baseUrl">Admiral base URL, for example http://127.0.0.1:25160.</param>
        /// <param name="token">Bearer token.</param>
        /// <exception cref="ArgumentException">Thrown when the URL or token is empty.</exception>
        public BenchClient(string baseUrl, string token)
        {
            if (String.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentException("Base URL is required.", nameof(baseUrl));
            if (String.IsNullOrWhiteSpace(token)) throw new ArgumentException("Token is required.", nameof(token));
            SocketsHttpHandler handler = new SocketsHttpHandler { MaxConnectionsPerServer = MaxConnections, PooledConnectionLifetime = TimeSpan.FromMinutes(10) };
            _Http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(TimeoutSeconds) };
            _Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        #endregion

        #region Public-Methods

        /// <summary>Create a vessel with unlimited voyage and mission parallelism so only captain and usage gates apply.</summary>
        /// <param name="name">Vessel name.</param>
        /// <param name="repoUrl">Local bare repository URL.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The vessel.</returns>
        public Task<BenchVessel> CreateVesselAsync(string name, string repoUrl, CancellationToken token = default)
            => SendAsync<BenchVessel>(HttpMethod.Post, "api/v1/vessels", new Dictionary<string, object?>
            {
                ["Name"] = name, ["RepoUrl"] = repoUrl, ["DefaultBranch"] = "main",
                ["MaxParallelVoyages"] = 0, ["MaxParallelMissionsPerVoyage"] = 0, ["SerializeOverlappingMissions"] = false
            }, token);

        /// <summary>Create a Claude Code captain whose usage comes from the benchmark's Custom capture stub.</summary>
        /// <param name="name">Captain name.</param>
        /// <param name="captureExecutable">Capture executable.</param>
        /// <param name="captureArguments">Capture arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The captain.</returns>
        /// <param name="maxConcurrency">Missions the captain may run at once.</param>
        public Task<BenchCaptain> CreateCaptainAsync(string name, string captureExecutable, IReadOnlyList<string> captureArguments, int maxConcurrency, CancellationToken token = default)
            => SendAsync<BenchCaptain>(HttpMethod.Post, "api/v1/captains", new Dictionary<string, object?>
            {
                ["Name"] = name, ["Runtime"] = "ClaudeCode", ["Model"] = "opus", ["MaxConcurrency"] = maxConcurrency,
                ["UsageCaptureEnabled"] = true, ["UsageCaptureMode"] = "Custom", ["UsageCaptureIntervalSeconds"] = 60,
                ["UsageCaptureCommand"] = JsonSerializer.Serialize(new Dictionary<string, object> { ["executable"] = captureExecutable, ["arguments"] = captureArguments })
            }, token);

        /// <summary>
        /// Request an on-demand usage capture for a captain's account. A refusal inside the capture cooldown (429) is not an
        /// error: the scheduled capture follows within the interval.
        /// </summary>
        /// <param name="captainId">Captain identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the capture was accepted.</returns>
        public async Task<bool> CaptureAsync(string captainId, CancellationToken token = default)
        {
            try
            {
                await SendAsync<JsonElementHolder>(HttpMethod.Post, "api/v1/captains/" + captainId + "/usage/capture", new Dictionary<string, object?>(), token).ConfigureAwait(false);
                return true;
            }
            catch (BenchRequestException ex) when (ex.StatusCode == 429)
            {
                Interlocked.Decrement(ref _Errors);
                return false;
            }
        }

        /// <summary>Read latest account usage rows.</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The first page of up to 100 rows (one row per account; the scenario uses at most a few accounts).</returns>
        public Task<BenchUsagePage> LatestUsageAsync(CancellationToken token = default)
            => SendAsync<BenchUsagePage>(HttpMethod.Get, "api/v1/usage/latest?pageSize=100", null, token);

        /// <summary>Read settings.</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Settings.</returns>
        public Task<BenchSettings> ReadSettingsAsync(CancellationToken token = default)
            => SendAsync<BenchSettings>(HttpMethod.Get, "api/v1/settings", null, token);

        /// <summary>Replace the usage-selection group at the given revision.</summary>
        /// <param name="selection">Complete group.</param>
        /// <param name="revision">Expected revision.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task.</returns>
        public async Task UpdateSelectionAsync(BenchUsageSelection selection, long revision, CancellationToken token = default)
            => await SendAsync<JsonElementHolder>(HttpMethod.Put, "api/v1/settings", new Dictionary<string, object?>
            {
                ["UsageSelection"] = selection, ["ExpectedUsageSelectionRevision"] = revision
            }, token).ConfigureAwait(false);

        /// <summary>Create one read-only Research mission on a vessel; the Admiral dispatches it.</summary>
        /// <param name="vesselId">Vessel identifier.</param>
        /// <param name="title">Mission title.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The mission.</returns>
        public Task<BenchMission> CreateMissionAsync(string vesselId, string title, CancellationToken token = default)
            => SendAsync<BenchMission>(HttpMethod.Post, "api/v1/missions", new Dictionary<string, object?>
            {
                ["Title"] = title, ["Description"] = "Benchmark admission probe. Reply with a one-line report.", ["VesselId"] = vesselId, ["Mode"] = "Research"
            }, token);

        /// <summary>Read every mission on a vessel (at most 1000).</summary>
        /// <param name="vesselId">Vessel identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The missions.</returns>
        public Task<BenchMissionPage> MissionsAsync(string vesselId, CancellationToken token = default)
            => SendAsync<BenchMissionPage>(HttpMethod.Get, "api/v1/missions?vesselId=" + Uri.EscapeDataString(vesselId) + "&pageSize=1000", null, token);

        /// <summary>Cancel a mission that did not finish.</summary>
        /// <param name="missionId">Mission identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task.</returns>
        public async Task CancelMissionAsync(string missionId, CancellationToken token = default)
        {
            using HttpResponseMessage response = await _Http.DeleteAsync("api/v1/missions/" + missionId, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) Interlocked.Increment(ref _Errors);
        }

        /// <inheritdoc />
        public void Dispose() => _Http.Dispose();

        #endregion

        #region Private-Methods

        private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken token) where T : class, new()
        {
            using HttpRequestMessage request = new HttpRequestMessage(method, path);
            if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await _Http.SendAsync(request, token).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Interlocked.Increment(ref _Errors);
                throw new BenchRequestException(method + " /" + path + " returned " + (int)response.StatusCode + ": " + (text.Length > 300 ? text.Substring(0, 300) : text), (int)response.StatusCode, text);
            }
            if (typeof(T) == typeof(JsonElementHolder) || String.IsNullOrWhiteSpace(text)) return new T();
            return JsonSerializer.Deserialize<T>(text, _Json) ?? new T();
        }

        #endregion
    }
}
