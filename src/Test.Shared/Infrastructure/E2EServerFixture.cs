namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using Armada.Server;
    using SyslogLogging;

    /// <summary>
    /// Lazily boots a single in-process <see cref="ArmadaServer"/> backed by a temp SQLite
    /// database and exposes shared HTTP clients for end-to-end suites. The server starts once
    /// on first access (thread-safe) and is reused across every e2e suite, matching the legacy
    /// automated harness. Readiness is confirmed by polling the REST and MCP health endpoints rather than a
    /// fixed sleep, so slow machines do not flake. The server and temp directory are torn down
    /// on process exit.
    /// </summary>
    public sealed class E2EServerFixture
    {
        #region Public-Members

        /// <summary>
        /// HTTP client authenticated with the test API key.
        /// </summary>
        public HttpClient AuthClient { get; private set; } = null!;

        /// <summary>
        /// HTTP client with no authentication headers.
        /// </summary>
        public HttpClient UnauthClient { get; private set; } = null!;

        /// <summary>
        /// HTTP client targeting the MCP port.
        /// </summary>
        public HttpClient McpClient { get; private set; } = null!;

        /// <summary>
        /// Base URL of the REST API (http://127.0.0.1:{restPort}).
        /// </summary>
        public string BaseUrl { get; private set; } = "";

        /// <summary>
        /// Test API key accepted by the server.
        /// </summary>
        public string ApiKey { get; private set; } = "";

        /// <summary>
        /// REST API port.
        /// </summary>
        public int RestPort { get; private set; }

        /// <summary>
        /// MCP port.
        /// </summary>
        public int McpPort { get; private set; }

        /// <summary>
        /// Temp directory holding the server's database, logs, docks, and repos.
        /// </summary>
        public string TempDir { get; private set; } = "";

        #endregion

        #region Private-Members

        private static readonly SemaphoreSlim _Gate = new SemaphoreSlim(1, 1);
        private static E2EServerFixture? _Current;
        private static object? _CurrentKey;

        // A single persistent server shared by isolation-safe, read-mostly suites. Sharing one boot
        // across this whole group (instead of one boot per suite) is safe precisely because none of
        // these suites create a captain or rely on mission dispatch: with no captain present, no mission
        // is ever assigned, launched, or stopped, so a suite can only ever accumulate CRUD data -- which
        // every suite in the group already asserts against tolerantly (>=, full page, filtered, or by
        // its own ids). Dispatch/captain suites are NOT in this set and keep their own fresh server.
        private static readonly SemaphoreSlim _SharedGate = new SemaphoreSlim(1, 1);
        private static E2EServerFixture? _Shared;

        private static readonly HashSet<string> _SharedSuiteTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "FleetSuite",
            "VesselSuite",
            "DockSuite",
            "EnvironmentSuite",
            "DeploymentSuite",
            "IncidentSuite",
            "ReleaseSuite",
            "WorkflowProfileCheckRunSuite",
            "MergeQueueSuite",
            "AuthApiSuite",
            "GitHubIntegrationSuite"
        };

        private ArmadaServer _Server = null!;

        #endregion

        #region Constructors-and-Factories

        private E2EServerFixture()
        {
        }

        /// <summary>
        /// Acquire the in-process server for the given suite. Isolation-safe, read-mostly suites (see
        /// <see cref="_SharedSuiteTypes"/>) share one persistent server booted once for the whole group;
        /// they never create a captain or rely on dispatch, so they can only accumulate CRUD data that
        /// their assertions already tolerate. Every other suite gets its own fresh server: the first
        /// case boots it, later cases in the same suite reuse it (preserving intra-suite state), and it
        /// is torn down when a different isolated suite starts -- so at most one isolated server plus the
        /// shared server is alive at a time, since the runners execute a suite's cases consecutively.
        /// </summary>
        /// <param name="suiteKey">A stable per-suite key (pass the suite instance, <c>this</c>).</param>
        /// <returns>The initialized fixture for this suite.</returns>
        public static async Task<E2EServerFixture> AcquireAsync(object suiteKey)
        {
            if (suiteKey == null) throw new ArgumentNullException(nameof(suiteKey));

            // Isolation-safe read-mostly suites share one persistent server, booted once for the group.
            if (_SharedSuiteTypes.Contains(suiteKey.GetType().Name))
            {
                return await AcquireSharedAsync().ConfigureAwait(false);
            }

            // Every other suite gets its own fresh server, torn down when a different isolated suite
            // starts, so exactly one isolated server (plus the shared one) is alive at a time.
            E2EServerFixture? current = _Current;
            if (current != null && ReferenceEquals(_CurrentKey, suiteKey)) return current;

            await _Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_Current != null && ReferenceEquals(_CurrentKey, suiteKey)) return _Current;

                if (_Current != null)
                {
                    _Current.Shutdown();
                    _Current = null;
                    _CurrentKey = null;
                }

                E2EServerFixture fixture = new E2EServerFixture();
                await fixture.StartAsync().ConfigureAwait(false);
                _Current = fixture;
                _CurrentKey = suiteKey;
                return fixture;
            }
            finally
            {
                _Gate.Release();
            }
        }

        private static async Task<E2EServerFixture> AcquireSharedAsync()
        {
            E2EServerFixture? shared = _Shared;
            if (shared != null) return shared;

            await _SharedGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_Shared != null) return _Shared;

                E2EServerFixture fixture = new E2EServerFixture();
                await fixture.StartAsync().ConfigureAwait(false);
                _Shared = fixture;
                return fixture;
            }
            finally
            {
                _SharedGate.Release();
            }
        }

        #endregion

        #region Private-Methods

        private async Task StartAsync()
        {
            // Recalling/stopping agents in tests must not block on the full production graceful-exit
            // window. Any agent processes launched against this in-process server are stopped almost
            // immediately (a stop-all over several captains otherwise waits ~10s each). Production keeps
            // the 10s default.
            BaseAgentRuntime.GracefulStopTimeoutMs = 500;


            TempDir = TestTemp.NewDirectory("e2e");

            string sqlitePath = Path.Combine(TempDir, "armada.db");
            DatabaseSettings dbSettings = new DatabaseSettings();
            dbSettings.Type = DatabaseTypeEnum.Sqlite;
            dbSettings.Filename = sqlitePath;

            RestPort = GetAvailablePort();
            McpPort = GetAvailablePort();
            ApiKey = "test-key-" + Guid.NewGuid().ToString("N");

            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;

            ArmadaSettings settings = new ArmadaSettings();
            settings.DataDirectory = TempDir;
            settings.DatabasePath = dbSettings.Filename;
            settings.Database = dbSettings;
            settings.LogDirectory = Path.Combine(TempDir, "logs");
            settings.DocksDirectory = Path.Combine(TempDir, "docks");
            settings.ReposDirectory = Path.Combine(TempDir, "repos");
            settings.AdmiralPort = RestPort;
            settings.McpPort = McpPort;
            settings.ApiKey = ApiKey;
            settings.HeartbeatIntervalSeconds = 300;
            // Bind the REST and MCP listeners to IPv4 loopback explicitly. The default "localhost"
            // makes clients resolve ::1 (IPv6) first on Windows, stalling every connection before it
            // falls back to 127.0.0.1 -- which massively inflates E2E time and pushes cases toward the
            // per-case timeout. Pinning to 127.0.0.1 on both ends keeps every connection pure IPv4.
            settings.Rest.Hostname = "127.0.0.1";
            settings.InitializeDirectories();

            // Pre-seed the server's database from the shared migrated-and-seeded template so the server's
            // InitializeAsync finds the schema already at the current version and skips the full migration
            // run (and re-seeding, which is existence-guarded) on every suite's boot. The result is
            // byte-identical to letting the server migrate from empty, just without the per-boot cost.
            TestDatabaseHelper.SeedDatabaseFile(sqlitePath);

            _Server = new ArmadaServer(logging, settings, quiet: true);
            await _Server.StartAsync().ConfigureAwait(false);

            BaseUrl = "http://127.0.0.1:" + RestPort;

            TimeSpan clientTimeout = TimeSpan.FromSeconds(30);

            AuthClient = new HttpClient();
            AuthClient.BaseAddress = new Uri(BaseUrl);
            AuthClient.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
            AuthClient.Timeout = clientTimeout;

            UnauthClient = new HttpClient();
            UnauthClient.BaseAddress = new Uri(BaseUrl);
            UnauthClient.Timeout = clientTimeout;

            McpClient = new HttpClient();
            McpClient.BaseAddress = new Uri("http://127.0.0.1:" + McpPort);
            McpClient.Timeout = clientTimeout;

            await WaitForReadyAsync().ConfigureAwait(false);

            AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();
        }

        private async Task WaitForReadyAsync()
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            Exception? last = null;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    // The MCP listener is started on a background task, so the REST health check alone does
                    // not prove it is accepting connections; also probe the MCP server's unauthenticated
                    // health check (GET /) before declaring the fixture ready.
                    HttpResponseMessage response = await AuthClient.GetAsync("/api/v1/status/health").ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        HttpResponseMessage mcpResponse = await McpClient.GetAsync("/").ConfigureAwait(false);
                        if (mcpResponse.StatusCode == HttpStatusCode.OK) return;
                    }
                }
                catch (Exception ex)
                {
                    last = ex;
                }

                await Task.Delay(100).ConfigureAwait(false);
            }

            throw new TimeoutException("E2E server did not become ready within 30 seconds." +
                (last != null ? " Last error: " + last.Message : ""));
        }

        private void Shutdown()
        {
            try { AuthClient?.Dispose(); } catch { }
            try { UnauthClient?.Dispose(); } catch { }
            try { McpClient?.Dispose(); } catch { }
            try { _Server?.Stop(); } catch { }
            try
            {
                if (Directory.Exists(TempDir)) Directory.Delete(TempDir, true);
            }
            catch
            {
                // Best-effort cleanup; a lingering handle must not crash process exit.
            }
        }

        private static int GetAvailablePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        #endregion
    }
}
