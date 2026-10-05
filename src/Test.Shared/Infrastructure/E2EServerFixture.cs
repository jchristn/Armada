namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Text;
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
        /// Session token encryption key the server uses (auto-generated at start), so suites can mint tokens.
        /// </summary>
        public string SessionTokenEncryptionKey { get; private set; } = "";

        /// <summary>
        /// The in-process server (for suites that inspect its registered routes and MCP tools).
        /// </summary>
        public ArmadaServer Server
        {
            get { return _Server; }
        }

        /// <summary>
        /// Temp directory holding the server's database, logs, docks, and repos.
        /// </summary>
        public string TempDir { get; private set; } = "";

        /// <summary>
        /// The stub agent runtime every CLI captain runtime of this server launches (see <see cref="StubAgentProcesses"/>).
        /// Dispatch in E2E tests never starts a real agent CLI: the result is the same whether or not Claude Code,
        /// Codex, or another CLI is installed on the machine running the tests, and no model is ever called.
        /// </summary>
        public StubAgentProcesses StubAgents { get; private set; } = null!;

        #endregion

        #region Private-Members

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, string> _ApiKeysByRestPort = new System.Collections.Concurrent.ConcurrentDictionary<int, string>();
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

        private const int _MaxStartAttempts = 3;
        private const int _ReadyTimeoutSeconds = 30;

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

        /// <summary>
        /// API key of the in-process server listening on a REST port, so WebSocket helpers that only know the port can
        /// authenticate their /ws upgrade.
        /// </summary>
        /// <param name="restPort">REST port.</param>
        /// <returns>The API key, or null when no fixture uses the port.</returns>
        public static string? ApiKeyForRestPort(int restPort)
        {
            return _ApiKeysByRestPort.TryGetValue(restPort, out string? key) ? key : null;
        }

        /// <summary>
        /// Connect an admin-authenticated (X-Api-Key) WebSocket to the fixture's /ws endpoint.
        /// </summary>
        /// <param name="restPort">REST port.</param>
        /// <returns>The connected socket.</returns>
        public static async Task<System.Net.WebSockets.ClientWebSocket> ConnectAuthenticatedWebSocketAsync(int restPort)
        {
            System.Net.WebSockets.ClientWebSocket ws = new System.Net.WebSockets.ClientWebSocket();
            string? apiKey = ApiKeyForRestPort(restPort);
            if (!String.IsNullOrEmpty(apiKey)) ws.Options.SetRequestHeader("X-Api-Key", apiKey);
            await ws.ConnectAsync(new Uri("ws://127.0.0.1:" + restPort + "/ws"), CancellationToken.None).ConfigureAwait(false);
            return ws;
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

            // The test process hosts the server, the test clients, and sync-over-async waits in one thread pool.
            // Under machine load the pool's slow thread injection can stall request handling long enough to look
            // like a server that never became ready, so give it a floor.
            EnsureThreadPoolFloor();

            List<string> attempts = new List<string>();
            for (int attempt = 1; attempt <= _MaxStartAttempts; attempt++)
            {
                string? failure = await TryStartOnceAsync(attempt).ConfigureAwait(false);
                if (failure == null)
                {
                    AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();
                    return;
                }

                attempts.Add(failure);
                Shutdown();
                if (attempt < _MaxStartAttempts) await Task.Delay(250 * attempt).ConfigureAwait(false);
            }

            throw new TimeoutException("E2E server did not become ready after " + _MaxStartAttempts + " attempts:"
                + Environment.NewLine + String.Join(Environment.NewLine, attempts));
        }

        private async Task<string?> TryStartOnceAsync(int attempt)
        {
            TempDir = TestTemp.NewDirectory("e2e");

            string sqlitePath = Path.Combine(TempDir, "armada.db");
            DatabaseSettings dbSettings = new DatabaseSettings();
            dbSettings.Type = DatabaseTypeEnum.Sqlite;
            dbSettings.Filename = sqlitePath;

            // Reserve both ports at once (so they always differ) from a range below every OS's ephemeral range.
            // Ports handed out by the OS for port 0 come from the ephemeral range, which outbound connections also
            // draw from: between releasing the probe listener and the server binding, a client socket of this or
            // another test process could take the port, and the MCP listener used to fail silently when that
            // happened, leaving the fixture to time out.
            int[] ports = TestPorts.Reserve(2);
            RestPort = ports[0];
            McpPort = ports[1];
            ApiKey = "test-key-" + Guid.NewGuid().ToString("N");
            _ApiKeysByRestPort[RestPort] = ApiKey;

            string logPath = Path.Combine(TempDir, "fixture-warnings.log");
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            logging.Settings.MinimumSeverity = Severity.Warn;
            logging.Settings.FileLogging = FileLoggingMode.SingleLogFile;
            logging.Settings.LogFilename = logPath;

            ArmadaSettings settings = new ArmadaSettings();
            settings.DataDirectory = TempDir;
            // Keep any settings save (PUT /api/v1/settings, restore) inside the temp directory, never ~/.armada.
            settings.SettingsFilePath = Path.Combine(TempDir, "settings.json");
            settings.DatabasePath = dbSettings.Filename;
            settings.Database = dbSettings;
            settings.LogDirectory = Path.Combine(TempDir, "logs");
            settings.DocksDirectory = Path.Combine(TempDir, "docks");
            settings.ReposDirectory = Path.Combine(TempDir, "repos");
            settings.AdmiralPort = RestPort;
            settings.McpPort = McpPort;
            settings.ApiKey = ApiKey;
            settings.HeartbeatIntervalSeconds = 300;
            // Shared fixtures serve many suites from one loopback address, several of which test bad passwords and
            // invalid tokens on purpose: lift the login rate limits so one suite cannot lock out the next. The limiter
            // itself is covered by E2E.LoginSecurity on a dedicated server.
            settings.LoginRateLimit.MaxFailuresPerAccount = 1000;
            settings.LoginRateLimit.MaxFailuresPerAddress = 100000;
            // Bind the REST and MCP listeners to IPv4 loopback explicitly. The default "localhost"
            // makes clients resolve ::1 (IPv6) first on Windows, stalling every connection before it
            // falls back to 127.0.0.1 -- which massively inflates E2E time and pushes cases toward the
            // per-case timeout. Pinning to 127.0.0.1 on both ends keeps every connection pure IPv4.
            settings.Rest.Hostname = "127.0.0.1";
            // Self-registration is off by default since v0.10; the onboarding suites exercise it, so the shared test
            // server opts in explicitly.
            settings.AllowSelfRegistration = true;
            // Vessel import tests create repositories under the system temp directory, which is outside the user
            // profile on macOS, so allow it explicitly; a low inline limit lets them exercise the background-job path.
            settings.Import.AllowedRoots = new List<string> { Path.GetTempPath() };
            settings.Import.InlineBatchLimit = 3;
            settings.InitializeDirectories();

            // Pre-seed the server's database from the shared migrated-and-seeded template so the server's
            // InitializeAsync finds the schema already at the current version and skips the full migration
            // run (and re-seeding, which is existence-guarded) on every suite's boot. The result is
            // byte-identical to letting the server migrate from empty, just without the per-boot cost.
            TestDatabaseHelper.SeedDatabaseFile(sqlitePath);

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

            Stopwatch elapsed = Stopwatch.StartNew();
            _Server = new ArmadaServer(logging, settings, quiet: true);
            try
            {
                await _Server.StartAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return Describe(attempt, elapsed, "server StartAsync threw " + ex.GetType().Name + ": " + ex.Message, null, null, logPath);
            }
            SessionTokenEncryptionKey = settings.SessionTokenEncryptionKey ?? "";

            StubAgents = new StubAgentProcesses(logging);
            StubAgents.InstallOn(_Server);

            ReadinessProbe probe = await WaitForReadyAsync(TimeSpan.FromSeconds(_ReadyTimeoutSeconds)).ConfigureAwait(false);
            if (probe.Ready) return null;
            return Describe(attempt, elapsed, "listeners not ready within " + _ReadyTimeoutSeconds + " s", probe.Rest, probe.Mcp, logPath);
        }

        private async Task<ReadinessProbe> WaitForReadyAsync(TimeSpan timeout)
        {
            // Bounded retries with exponential backoff; each probe has its own short timeout so one hung request
            // cannot consume the whole readiness window (the shared clients allow 30 s per request). Readiness needs
            // both listeners: REST health and the MCP server's unauthenticated health check (GET /), since the MCP
            // listener starts separately from REST.
            ReadinessProbe probe = new ReadinessProbe();
            MonotonicDeadline deadline = MonotonicDeadline.After(timeout);
            int delayMs = 50;
            while (!deadline.Passed)
            {
                if (!probe.RestOk) probe.Rest = await ProbeAsync(AuthClient, "/api/v1/status/health").ConfigureAwait(false);
                if (probe.RestOk && !probe.McpOk) probe.Mcp = await ProbeAsync(McpClient, "/").ConfigureAwait(false);
                if (probe.Ready) return probe;

                await Task.Delay(delayMs).ConfigureAwait(false);
                delayMs = Math.Min(delayMs * 2, 1000);
            }
            return probe;
        }

        private static async Task<string> ProbeAsync(HttpClient client, string path)
        {
            using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                try
                {
                    using (HttpResponseMessage response = await client.GetAsync(path, cts.Token).ConfigureAwait(false))
                    {
                        return response.StatusCode == HttpStatusCode.OK ? ReadinessProbe.Ok : "HTTP " + (int)response.StatusCode;
                    }
                }
                catch (OperationCanceledException)
                {
                    return "no response within 5 s";
                }
                catch (HttpRequestException ex)
                {
                    return ex.GetType().Name + ": " + ex.Message;
                }
            }
        }

        private string Describe(int attempt, Stopwatch elapsed, string reason, string? rest, string? mcp, string logPath)
        {
            ThreadPool.GetMinThreads(out int minWorkers, out int minIo);
            StringBuilder sb = new StringBuilder();
            sb.Append("  attempt ").Append(attempt).Append(" after ").Append(elapsed.ElapsedMilliseconds).Append(" ms: ").Append(reason);
            sb.Append(Environment.NewLine).Append("    REST 127.0.0.1:").Append(RestPort).Append(" -> ").Append(rest ?? "not probed")
                .Append(" (port ").Append(DescribePort(RestPort)).Append(')');
            sb.Append(Environment.NewLine).Append("    MCP  127.0.0.1:").Append(McpPort).Append(" -> ").Append(mcp ?? "not probed")
                .Append(" (port ").Append(DescribePort(McpPort)).Append(')');
            sb.Append(Environment.NewLine).Append("    thread pool: threads=").Append(ThreadPool.ThreadCount)
                .Append(" pending=").Append(ThreadPool.PendingWorkItemCount)
                .Append(" minWorkers=").Append(minWorkers).Append(" minIo=").Append(minIo);
            try
            {
                if (File.Exists(logPath))
                {
                    string[] lines = File.ReadAllLines(logPath);
                    int from = Math.Max(0, lines.Length - 8);
                    for (int i = from; i < lines.Length; i++) sb.Append(Environment.NewLine).Append("    log: ").Append(lines[i]);
                }
            }
            catch (IOException)
            {
            }
            return sb.ToString();
        }

        private static string DescribePort(int port)
        {
            try
            {
                TcpListener listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return "nothing listening";
            }
            catch (SocketException)
            {
                return "bound by a socket";
            }
        }

        private static void EnsureThreadPoolFloor()
        {
            ThreadPool.GetMinThreads(out int workers, out int io);
            int floor = Math.Max(32, Environment.ProcessorCount * 2);
            if (workers < floor || io < floor) ThreadPool.SetMinThreads(Math.Max(workers, floor), Math.Max(io, floor));
        }

        private void Shutdown()
        {
            try { AuthClient?.Dispose(); } catch { }
            try { UnauthClient?.Dispose(); } catch { }
            try { McpClient?.Dispose(); } catch { }
            try { _Server?.Stop(); } catch { }
            try { StubAgents?.StopAll(); } catch { }
            try
            {
                if (Directory.Exists(TempDir)) Directory.Delete(TempDir, true);
            }
            catch
            {
                // Best-effort cleanup; a lingering handle must not crash process exit.
            }
        }

        #endregion
    }
}
