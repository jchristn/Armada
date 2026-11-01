namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.WebSockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Harbor;
    using Armada.Core.Hosting;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Core.Storage;
    using SyslogLogging;

    /// <summary>
    /// Runs this computer's link to the Admiral: connects, takes work, reconnects after drops. It has no UI of its own,
    /// so the window and a headless start (no display available) share one implementation; the window subscribes to
    /// the callbacks to show state.
    /// </summary>
    public class HarborLinkHost
    {
        #region Public-Members

        /// <summary>Raised when the link state changes. May be called on any thread.</summary>
        public Action<HarborLinkStateEnum>? OnLinkState { get; set; }

        /// <summary>Raised with a one-line description of what the link is doing. May be called on any thread.</summary>
        public Action<string>? OnDetail { get; set; }

        /// <summary>Raised with the session's MCP base URL, or null. May be called on any thread.</summary>
        public Action<string?>? OnMcp { get; set; }

        /// <summary>Raised for each activity log entry. May be called on any thread.</summary>
        public Action<HarborLogEntry>? OnLog { get; set; }

        /// <summary>Raised with the agent runtimes detected on this computer at the start of each session.</summary>
        public Action<List<HarborCapability>>? OnRuntimesDetected { get; set; }

        /// <summary>What Harbor knows about its jobs; lives as long as the host, across sessions.</summary>
        public HarborJobState JobState { get; }

        /// <summary>Link counters shared across sessions.</summary>
        public HarborLinkStatistics LinkStatistics { get; }

        /// <summary>Activity log classifier shared across sessions.</summary>
        public HarborLogClassifier LogClassifier { get; }

        /// <summary>The current session's link client, or null between sessions.</summary>
        public HarborLinkClient? Client { get; private set; }

        /// <summary>The job runner, created with the first session and kept for the host's lifetime; null before.</summary>
        public Armada.Runtimes.LocalHarborJobRunner? JobRunner => _JobRunner;

        #endregion

        #region Private-Members

        private readonly HarborAppSettings _Settings;
        private readonly LoggingModule _Logging;
        private readonly HarborRuntimeDetector _RuntimeDetector = new HarborRuntimeDetector();
        private LocalHostCommandExecutor? _Executor = null;
        private Armada.Runtimes.LocalHarborJobRunner? _JobRunner = null;
        private HarborDockManager? _DockManager = null;
        private Armada.Runtimes.UsageProbeHostRunner? _UsageProbeRunner = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create a link host.
        /// </summary>
        /// <param name="settings">Live Harbor settings.</param>
        /// <param name="logging">Harbor log.</param>
        /// <param name="jobState">Job state shared with the window, or null for a new one.</param>
        /// <param name="linkStatistics">Link counters shared with the window, or null for new ones.</param>
        /// <param name="logClassifier">Log classifier shared with the window, or null for a new one.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings or logging is null.</exception>
        public HarborLinkHost(HarborAppSettings settings, LoggingModule logging, HarborJobState? jobState = null,
            HarborLinkStatistics? linkStatistics = null, HarborLogClassifier? logClassifier = null)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            JobState = jobState ?? new HarborJobState();
            LinkStatistics = linkStatistics ?? new HarborLinkStatistics();
            LogClassifier = logClassifier ?? new HarborLogClassifier();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Keep the link to the Admiral up until cancelled: connect, run the session, and retry three seconds after any
        /// drop or connect failure. The job runner and dock manager are created once and kept across sessions, so jobs
        /// keep running through a link drop and report when the link is back.
        /// </summary>
        /// <param name="token">Cancellation token; cancel to disconnect.</param>
        /// <returns>A task that completes when cancelled.</returns>
        public async Task RunAsync(CancellationToken token)
        {
            if (_JobRunner == null || _Executor == null || _DockManager == null)
            {
                _Executor = new LocalHostCommandExecutor();

                // The host-side usage helper is built once and kept for the host's lifetime, so the account
                // identity it derives (the persisted host identity and key under the Harbor's settings
                // directory, named by this Harbor's id) survives reconnects: rebuilding it per session would
                // give every session a different account. Supplied to the link client, it is what advertises
                // the usage-probe capability and runs the probes on this, the credential host.
                Armada.Core.Services.UsageAccountResolver usageAccounts = new Armada.Core.Services.UsageAccountResolver(
                    HarborAppSettings.SettingsDirectory(), _Logging, _Settings.HarborId);
                _UsageProbeRunner = new Armada.Runtimes.UsageProbeHostRunner(_Logging, usageAccounts);
                _Executor.UsageProbe = _UsageProbeRunner;

                _JobRunner = new Armada.Runtimes.LocalHarborJobRunner(_Logging);
                // Each job's output is also kept on this computer, so its log can be read here (Status > Logs) even when
                // the Admiral is on another machine.
                _JobRunner.JobLogs = new HarborLogPaths(HarborAppSettings.LogDirectory());
                // An agent process that outlived an earlier Harbor run (a crash, a kill) would keep editing a dock the
                // Admiral relaunches into. Stop those before taking work, and record this run's.
                Armada.Runtimes.HarborProcessLedger ledger = new Armada.Runtimes.HarborProcessLedger(
                    Path.Combine(HarborAppSettings.SettingsDirectory(), "running-agents.json"), _Logging);
                int orphans = ledger.StopOrphans();
                if (orphans > 0) Info("Stopped " + orphans + " agent process(es) left running by an earlier Harbor run", HarborLogCategoryEnum.Link, HarborLogOutcomeEnum.Failed);
                _JobRunner.ProcessLedger = ledger;
                // Reads the live settings on every request, so repository edits apply without reconnecting.
                _DockManager = new HarborDockManager(() => _Settings.BuildDockSettings(), _Logging, _Executor);
                // The Workspace's Open in Terminal opens a terminal on this desktop in a vessel's checkout.
                _DockManager.TerminalLauncher = (string folder) => Armada.Core.Hosting.DesktopShellLauncher.OpenTerminal(folder, "This Harbor");
            }

            LocalHostCommandExecutor executor = _Executor;
            Armada.Runtimes.LocalHarborJobRunner jobRunner = _JobRunner;
            HarborDockManager dockManager = _DockManager;
            Armada.Runtimes.UsageProbeHostRunner usageProbeRunner = _UsageProbeRunner!;

            while (!token.IsCancellationRequested)
            {
                // Checked on every session, so a CLI installed while Harbor runs is offered after the next reconnect.
                List<HarborCapability> capabilities = BuildCapabilities();
                using (WebSocketHarborTransport transport = new WebSocketHarborTransport(new Uri(_Settings.ServerLinkUrl), BuildHeaders()))
                {
                    HarborLinkClient client = new HarborLinkClient(
                        _Settings.HarborId,
                        _Settings.Name,
                        capabilities,
                        _Settings.MaxConcurrentJobs,
                        executor,
                        _Logging,
                        _Settings.HeartbeatIntervalMs,
                        Log,
                        jobRunner,
                        dockManager,
                        usageProbe: usageProbeRunner);
                    client.StorageCollector = async storageToken =>
                    {
                        HarborDockSettings storageDocks = _Settings.BuildDockSettings();
                        List<StorageLocation> roots = new List<StorageLocation>
                        {
                            new StorageLocation { Category = "Logs/harbor-jobs", Path = Path.Combine(HarborAppSettings.LogDirectory(), "jobs") },
                            new StorageLocation { Category = "Logs/harbor", Path = HarborAppSettings.LogDirectory() },
                            new StorageLocation { Category = "Docks", Path = storageDocks.DocksDirectory },
                            new StorageLocation { Category = "Repositories", Path = storageDocks.ReposDirectory }
                        };
                        foreach (HarborRepositoryMapping mapping in storageDocks.Repositories)
                            roots.Add(new StorageLocation { Category = "Repositories", Path = mapping.Path, EntityId = mapping.Vessel });
                        return await new StorageFileCollector().CollectAsync(roots, new StorageSettings(), storageToken).ConfigureAwait(false);
                    };
                    // One set of link counters across sessions, so heartbeats report this process's reconnects.
                    client.LinkStatistics = LinkStatistics;
                    client.LogClassifier = LogClassifier;
                    client.JobState = JobState;
                    Client = client;

                    try
                    {
                        OnLinkState?.Invoke(HarborLinkStateEnum.Connecting);
                        OnDetail?.Invoke("Reaching the Admiral at " + _Settings.ServerLinkUrl + "...");
                        await client.RunSessionAsync(transport, token, () =>
                        {
                            OnLinkState?.Invoke(HarborLinkStateEnum.Connected);
                            OnDetail?.Invoke("Ready. Work the Admiral sends to this computer runs here.");
                            OnMcp?.Invoke(client.McpBaseUrl);
                        }).ConfigureAwait(false);

                        // A session ended by Disconnect or Reconnect must not report over the state they set.
                        if (token.IsCancellationRequested) break;
                        OnLinkState?.Invoke(HarborLinkStateEnum.Disconnected);
                        OnDetail?.Invoke("The connection to the Admiral closed. Trying again in 3 seconds.");
                        Info("Link closed; retrying in 3s", HarborLogCategoryEnum.Link, HarborLogOutcomeEnum.Failed);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (token.IsCancellationRequested) break;
                        OnLinkState?.Invoke(HarborLinkStateEnum.Error);
                        OnDetail?.Invoke(DescribeConnectError(ex));
                        Info("Connect failed: " + ex.Message, HarborLogCategoryEnum.Link, HarborLogOutcomeEnum.Failed);
                    }

                    OnMcp?.Invoke(client.McpBaseUrl);
                }

                Client = null;
                if (token.IsCancellationRequested) break;
                try
                {
                    await Task.Delay(3000, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        #endregion

        #region Private-Methods

        private List<HarborCapability> BuildCapabilities()
        {
            List<HarborCapability> capabilities = new List<HarborCapability>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string name in _Settings.Capabilities)
            {
                if (!String.IsNullOrWhiteSpace(name) && seen.Add(name))
                    capabilities.Add(new HarborCapability { Name = name, Available = true });
            }

            // Advertise the agent runtimes this host can launch so the Admiral's router can match a captain's requested
            // runtime: a runtime is available only when its CLI resolves on PATH (the login shell's PATH, imported at
            // startup), so the Admiral does not route a captain here only for its launch to fail.
            List<HarborCapability> runtimes = _RuntimeDetector.Detect();
            foreach (HarborCapability runtime in runtimes)
            {
                if (seen.Add(runtime.Name)) capabilities.Add(runtime);
            }

            OnRuntimesDetected?.Invoke(runtimes);
            return capabilities;
        }

        private Dictionary<string, string>? BuildHeaders()
        {
            Dictionary<string, string> headers = new Dictionary<string, string>();
            if (!String.IsNullOrWhiteSpace(_Settings.AccessKey)) headers["x-access-key"] = _Settings.AccessKey;
            if (!String.IsNullOrWhiteSpace(_Settings.Secret)) headers["x-secret-key"] = _Settings.Secret;
            // Informational only: the Admiral takes the owning user from the AccessKey credential and ignores
            // x-user-guid; x-tenant-guid applies to a credential-less loopback link or a global-admin credential.
            if (!String.IsNullOrWhiteSpace(_Settings.UserId)) headers["x-user-guid"] = _Settings.UserId;
            if (!String.IsNullOrWhiteSpace(_Settings.TenantId)) headers["x-tenant-guid"] = _Settings.TenantId;
            return headers.Count > 0 ? headers : null;
        }

        private void Log(HarborLogEntry entry)
        {
            if (OnLog != null) OnLog(entry);
            else _Logging.Info("[Harbor] " + (entry.Direction == HarborLogDirection.Info ? "" : entry.Direction + " ") + entry.Message);
        }

        private void Info(string message, HarborLogCategoryEnum category, HarborLogOutcomeEnum outcome)
        {
            Log(new HarborLogEntry(HarborLogDirection.Info, message) { Category = category, Outcome = outcome });
        }

        private string DescribeConnectError(Exception ex)
        {
            if (ex is WebSocketException || ex.InnerException is WebSocketException)
            {
                return "Could not connect to the Admiral at " + _Settings.ServerLinkUrl + ": " + Sentence(ex.Message)
                    + " Check that the Admiral is running, and the Admiral address and access key in Settings. Trying again in 3 seconds.";
            }

            return "Could not connect to the Admiral: " + Sentence(ex.Message) + " Trying again in 3 seconds.";
        }

        private static string Sentence(string message)
        {
            string trimmed = (message ?? String.Empty).Trim();
            if (trimmed.Length == 0) return "unknown error.";
            char last = trimmed[trimmed.Length - 1];
            return last == '.' || last == '!' || last == '?' ? trimmed : trimmed + ".";
        }

        #endregion
    }
}
