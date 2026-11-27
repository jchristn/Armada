namespace Armada.Client
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Typed client for the Armada REST API. Calls are grouped into one sub-client per area (for example
    /// <c>client.Missions.GetAsync(id)</c>, <c>client.Fleets.ListAsync()</c>); each sub-client method mirrors one
    /// server-calling function in the web dashboard's <c>api/client.ts</c> and names it with
    /// <see cref="DashboardExportAttribute"/>. Requests are sent with the server's PascalCase JSON; non-success
    /// responses raise <see cref="ArmadaApiException"/>; a 401 also raises <see cref="Unauthorized"/> first.
    /// Thread-safe for concurrent requests. <see cref="SetToken"/> may be called at any time; requests already in
    /// flight keep the credential they started with.
    /// </summary>
    public sealed class ArmadaClient : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Connection options. Never null.
        /// </summary>
        public ArmadaClientOptions Options { get; }

        /// <summary>
        /// Base URL of the Admiral (no trailing slash).
        /// </summary>
        public string BaseUrl
        {
            get { return Options.BaseUrl; }
        }

        /// <summary>
        /// Raised before an <see cref="ArmadaApiException"/> is thrown for a 401 response (the dashboard's
        /// <c>setOnUnauthorized</c> hook). Raised on the thread that completed the request.
        /// </summary>
        public event EventHandler<ArmadaApiException>? Unauthorized;

        /// <summary>
        /// Raised after every completed request with the method, path, status code, and duration. Used for
        /// diagnostics; handlers must be cheap and must not throw.
        /// </summary>
        public event EventHandler<ArmadaRequestCompletedEventArgs>? RequestCompleted;

        /// <summary>
        /// Ask API calls.
        /// </summary>
        public AskClient Ask { get; }

        /// <summary>
        /// Auth API calls.
        /// </summary>
        public AuthClient Auth { get; }

        /// <summary>
        /// Fixture battery calls: list the packs, run packs against a captain and read a run (<c>/api/v1/battery</c>).
        /// </summary>
        public BatteryClient Battery { get; }

        /// <summary>
        /// Mission and voyage black box calls (<c>/api/v1/blackbox</c>, <c>/api/v1/missions/{id}/blackbox</c>, <c>/api/v1/voyages/{id}/blackbox</c>). Errors carry the black box code in <see cref="ArmadaApiException"/>: 404 BlackBoxNotFound, 409 BlackBoxDeleting, BlackBoxObjectCorrupt or BlackBoxConfirmationRequired, 410 BlackBoxObjectMissing, 503 BlackBoxStoreUnavailable or BlackBoxActionsUnavailable, 400 InvalidArgument or CollectionLimitExceeded.
        /// </summary>
        public BlackBoxClient BlackBox { get; }

        /// <summary>
        /// Black box prevention, forensics and operations API calls: overlap preview, vessel reservations, brief lint, configuration doctor, attempt diff, nearby successes, combined timeline, voyage cascade graph and recovery statistics.
        /// </summary>
        public BlackBoxInsightsClient BlackBoxInsights { get; }

        /// <summary>
        /// CaptainSelection API calls.
        /// </summary>
        public CaptainSelectionClient CaptainSelection { get; }

        /// <summary>
        /// Captains API calls.
        /// </summary>
        public CaptainsClient Captains { get; }

        /// <summary>
        /// CheckRuns API calls.
        /// </summary>
        public CheckRunsClient CheckRuns { get; }

        /// <summary>
        /// CLI tool permission calls: requests (<c>/api/v1/cli-permissions/requests</c>), rules (<c>/api/v1/cli-permissions/rules</c>), and the captain and Ask thread policy overrides.
        /// </summary>
        public CliPermissionsClient CliPermissions { get; }

        /// <summary>
        /// Mission tree, wait and concurrency report calls.
        /// </summary>
        public ConcurrencyClient Concurrency { get; }

        /// <summary>
        /// Credentials API calls.
        /// </summary>
        public CredentialsClient Credentials { get; }

        /// <summary>
        /// Deployments API calls.
        /// </summary>
        public DeploymentsClient Deployments { get; }

        /// <summary>
        /// Docks API calls.
        /// </summary>
        public DocksClient Docks { get; }

        /// <summary>
        /// Entities API calls.
        /// </summary>
        public EntitiesClient Entities { get; }

        /// <summary>
        /// Environments API calls.
        /// </summary>
        public EnvironmentsClient Environments { get; }

        /// <summary>
        /// Events API calls.
        /// </summary>
        public EventsClient Events { get; }

        /// <summary>
        /// FleetActions API calls.
        /// </summary>
        public FleetActionsClient FleetActions { get; }

        /// <summary>
        /// Fleets API calls.
        /// </summary>
        public FleetsClient Fleets { get; }

        /// <summary>
        /// Handoff artifact calls (<c>/api/v1/missions/{id}/handoff-artifacts</c>, <c>/api/v1/handoff-artifacts/{id}</c>).
        /// </summary>
        public HandoffArtifactsClient HandoffArtifacts { get; }

        /// <summary>
        /// Harbors API calls.
        /// </summary>
        public HarborsClient Harbors { get; }

        /// <summary>
        /// History API calls.
        /// </summary>
        public HistoryClient History { get; }

        /// <summary>
        /// Inbox API calls.
        /// </summary>
        public InboxClient Inbox { get; }

        /// <summary>
        /// Incidents API calls.
        /// </summary>
        public IncidentsClient Incidents { get; }

        /// <summary>
        /// Jobs API calls.
        /// </summary>
        public JobsClient Jobs { get; }

        /// <summary>
        /// Memories API calls.
        /// </summary>
        public MemoriesClient Memories { get; }

        /// <summary>
        /// MergeQueue API calls.
        /// </summary>
        public MergeQueueClient MergeQueue { get; }

        /// <summary>
        /// Missions API calls.
        /// </summary>
        public MissionsClient Missions { get; }

        /// <summary>
        /// Model alert calls (<c>/api/v1/model-alerts</c>): list and read scorecard alerts, evaluate now, read and replace the thresholds, apply a suggested action and ignore an alert. Alerts are advisory; nothing changes until an action is applied.
        /// </summary>
        public ModelAlertsClient ModelAlerts { get; }

        /// <summary>
        /// ModelEndpoints API calls.
        /// </summary>
        public ModelEndpointsClient ModelEndpoints { get; }

        /// <summary>
        /// Evaluate calls: start a probe-only Quick Evaluate of a captain or a model endpoint and read the runs it records (<c>/api/v1/model-probe-runs</c>).
        /// </summary>
        public ModelProbesClient ModelProbes { get; }

        /// <summary>
        /// Model profile calls (<c>/api/v1/model-profiles</c>): list and read the profiles of the models captains run, update their mutable parts, and mark revisions compatible. Profiles are advisory and change no routing.
        /// </summary>
        public ModelProfilesClient ModelProfiles { get; }

        /// <summary>
        /// Mux runtime endpoint API calls.
        /// </summary>
        public MuxEndpointsClient MuxEndpoints { get; }

        /// <summary>
        /// Objectives API calls.
        /// </summary>
        public ObjectivesClient Objectives { get; }

        /// <summary>
        /// Personas API calls. CaptainPriorities accepts Runtime ApiEndpoint with Endpoint set to an exact managed inference endpoint ID.
        /// </summary>
        public PersonasClient Personas { get; }

        /// <summary>
        /// Pipelines API calls.
        /// </summary>
        public PipelinesClient Pipelines { get; }

        /// <summary>
        /// PlanningSessions API calls.
        /// </summary>
        public PlanningSessionsClient PlanningSessions { get; }

        /// <summary>
        /// Playbooks API calls.
        /// </summary>
        public PlaybooksClient Playbooks { get; }

        /// <summary>
        /// ProjectProfiles API calls.
        /// </summary>
        public ProjectProfilesClient ProjectProfiles { get; }

        /// <summary>
        /// PromptTemplates API calls.
        /// </summary>
        public PromptTemplatesClient PromptTemplates { get; }

        /// <summary>
        /// Push notification device API calls (mobile apps).
        /// </summary>
        public PushClient Push { get; }

        /// <summary>
        /// Releases API calls.
        /// </summary>
        public ReleasesClient Releases { get; }

        /// <summary>
        /// Pipeline reliability calls: a mission's attempt history, the reliability summary per model and persona, and the acceptance gate reports (<c>/api/v1/missions/{id}/attempts</c>, <c>/api/v1/reliability</c>).
        /// </summary>
        public ReliabilityClient Reliability { get; }

        /// <summary>
        /// RequestHistory API calls.
        /// </summary>
        public RequestHistoryClient RequestHistory { get; }

        /// <summary>
        /// Runbooks API calls.
        /// </summary>
        public RunbooksClient Runbooks { get; }

        /// <summary>
        /// Server API calls.
        /// </summary>
        public ServerClient Server { get; }

        /// <summary>
        /// Signals API calls.
        /// </summary>
        public SignalsClient Signals { get; }

        /// <summary>
        /// Skills API calls.
        /// </summary>
        public SkillsClient Skills { get; }

        /// <summary>
        /// Stage outcome and failure-cause rule calls: outcomes (<c>/api/v1/stage-outcomes</c>) and rules (<c>/api/v1/failure-cause-rules</c>).
        /// </summary>
        public StageOutcomesClient StageOutcomes { get; }

        /// <summary>
        /// Stage submission calls (<c>/api/v1/missions/{id}/submissions</c>).
        /// </summary>
        public StageSubmissionsClient StageSubmissions { get; }

        /// <summary>
        /// Status API calls.
        /// </summary>
        public StatusClient Status { get; }

        /// <summary>
        /// Calls the TUI needs beyond the dashboard's <c>api/client.ts</c> (the shared i18n catalog and the OpenAPI document), plus private helpers shared by the area files.
        /// </summary>
        public SupportClient Support { get; }

        /// <summary>
        /// Tenants API calls.
        /// </summary>
        public TenantsClient Tenants { get; }

        /// <summary>
        /// Token usage API calls.
        /// </summary>
        public TokenUsageClient TokenUsage { get; }

        /// <summary>
        /// Usage API calls.
        /// </summary>
        public UsageClient Usage { get; }

        /// <summary>
        /// Users API calls.
        /// </summary>
        public UsersClient Users { get; }

        /// <summary>
        /// VesselHealth API calls.
        /// </summary>
        public VesselHealthClient VesselHealth { get; }

        /// <summary>
        /// VesselImport API calls.
        /// </summary>
        public VesselImportClient VesselImport { get; }

        /// <summary>
        /// Vessels API calls.
        /// </summary>
        public VesselsClient Vessels { get; }

        /// <summary>
        /// Voyages API calls.
        /// </summary>
        public VoyagesClient Voyages { get; }

        /// <summary>
        /// WorkflowProfiles API calls.
        /// </summary>
        public WorkflowProfilesClient WorkflowProfiles { get; }

        /// <summary>
        /// Workspace API calls.
        /// </summary>
        public WorkspaceClient Workspace { get; }

        /// <summary>
        /// Time source. Request durations are measured on its monotonic clock, never its wall clock, so a wall-clock
        /// jump (the host sleeping and waking, an NTP step) cannot inflate or negate them. Defaults to
        /// <see cref="TimeProvider.System"/>; tests substitute a provider whose wall clock jumps.
        /// </summary>
        internal TimeProvider Time
        {
            get => _Transport.Time;
            set => _Transport.Time = value;
        }

        #endregion

        #region Private-Members

        private readonly ArmadaTransport _Transport;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for a base URL with no credentials.
        /// </summary>
        /// <param name="baseUrl">Absolute http(s) base URL of the Admiral.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="baseUrl"/> is null or whitespace.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="baseUrl"/> is not an absolute http(s) URL.</exception>
        public ArmadaClient(string baseUrl) : this(new ArmadaClientOptions(baseUrl), null)
        {
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="options">Connection options.</param>
        /// <param name="handler">Optional HTTP handler (tests pass a stub); null uses the default handler.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
        public ArmadaClient(ArmadaClientOptions options, HttpMessageHandler? handler = null)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            _Transport = new ArmadaTransport(this, options, handler);
            Ask = new AskClient(_Transport);
            Auth = new AuthClient(_Transport);
            Battery = new BatteryClient(_Transport);
            BlackBox = new BlackBoxClient(_Transport);
            BlackBoxInsights = new BlackBoxInsightsClient(_Transport);
            CaptainSelection = new CaptainSelectionClient(_Transport);
            Captains = new CaptainsClient(_Transport);
            CheckRuns = new CheckRunsClient(_Transport);
            CliPermissions = new CliPermissionsClient(_Transport);
            Concurrency = new ConcurrencyClient(_Transport);
            Credentials = new CredentialsClient(_Transport);
            Deployments = new DeploymentsClient(_Transport);
            Docks = new DocksClient(_Transport);
            Entities = new EntitiesClient(_Transport);
            Environments = new EnvironmentsClient(_Transport);
            Events = new EventsClient(_Transport);
            FleetActions = new FleetActionsClient(_Transport);
            Fleets = new FleetsClient(_Transport);
            HandoffArtifacts = new HandoffArtifactsClient(_Transport);
            Harbors = new HarborsClient(_Transport);
            History = new HistoryClient(_Transport);
            Inbox = new InboxClient(_Transport);
            Incidents = new IncidentsClient(_Transport);
            Jobs = new JobsClient(_Transport);
            Memories = new MemoriesClient(_Transport);
            MergeQueue = new MergeQueueClient(_Transport);
            Missions = new MissionsClient(_Transport);
            ModelAlerts = new ModelAlertsClient(_Transport);
            ModelEndpoints = new ModelEndpointsClient(_Transport);
            ModelProbes = new ModelProbesClient(_Transport);
            ModelProfiles = new ModelProfilesClient(_Transport);
            MuxEndpoints = new MuxEndpointsClient(_Transport);
            Objectives = new ObjectivesClient(_Transport);
            Personas = new PersonasClient(_Transport);
            Pipelines = new PipelinesClient(_Transport);
            PlanningSessions = new PlanningSessionsClient(_Transport);
            Playbooks = new PlaybooksClient(_Transport);
            ProjectProfiles = new ProjectProfilesClient(_Transport);
            PromptTemplates = new PromptTemplatesClient(_Transport);
            Push = new PushClient(_Transport);
            Releases = new ReleasesClient(_Transport);
            Reliability = new ReliabilityClient(_Transport);
            RequestHistory = new RequestHistoryClient(_Transport);
            Runbooks = new RunbooksClient(_Transport);
            Server = new ServerClient(_Transport);
            Signals = new SignalsClient(_Transport);
            Skills = new SkillsClient(_Transport);
            StageOutcomes = new StageOutcomesClient(_Transport);
            StageSubmissions = new StageSubmissionsClient(_Transport);
            Status = new StatusClient(_Transport);
            Support = new SupportClient(_Transport);
            Tenants = new TenantsClient(_Transport);
            TokenUsage = new TokenUsageClient(_Transport);
            Usage = new UsageClient(_Transport);
            Users = new UsersClient(_Transport);
            VesselHealth = new VesselHealthClient(_Transport);
            VesselImport = new VesselImportClient(_Transport);
            Vessels = new VesselsClient(_Transport);
            Voyages = new VoyagesClient(_Transport);
            WorkflowProfiles = new WorkflowProfilesClient(_Transport);
            Workspace = new WorkspaceClient(_Transport);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Replace the session token sent as <c>X-Token</c> (the dashboard's <c>setAuthToken</c>). Null clears it.
        /// </summary>
        /// <param name="token">Session token or API key, or null.</param>
        public void SetToken(string? token)
        {
            Options.Token = String.IsNullOrWhiteSpace(token) ? null : token.Trim();
        }

        /// <summary>
        /// Send an arbitrary request with the client's credentials and return the raw response (used by the API
        /// Explorer and for binary payloads). The caller owns and must dispose the response. Does not throw for
        /// non-success statuses.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="pathAndQuery">Path relative to the base URL, starting with <c>/</c>, or an absolute URL.</param>
        /// <param name="content">Optional request content.</param>
        /// <param name="extraHeaders">Optional extra headers.</param>
        /// <param name="timeoutMs">Optional timeout override in milliseconds.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="method"/> or <paramref name="pathAndQuery"/> is null.</exception>
        /// <exception cref="ArmadaApiException">Thrown on timeout or transport failure.</exception>
        public Task<HttpResponseMessage> SendRawAsync(
            HttpMethod method,
            string pathAndQuery,
            HttpContent? content = null,
            IDictionary<string, string>? extraHeaders = null,
            int? timeoutMs = null,
            CancellationToken token = default)
        {
            return _Transport.SendRawAsync(method, pathAndQuery, content, extraHeaders, timeoutMs, token);
        }

        /// <summary>
        /// Dispose the HTTP client.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Transport.Dispose();
            _Disposed = true;
        }

        #endregion

        #region Internal-Methods

        internal void OnUnauthorized(ArmadaApiException ex)
        {
            EventHandler<ArmadaApiException>? handler = Unauthorized;
            if (handler == null) return;
            try
            {
                handler(this, ex);
            }
            catch (Exception)
            {
                // A faulty observer must not mask the API error.
            }
        }

        internal void OnRequestCompleted(string method, string path, int status, TimeSpan duration)
        {
            EventHandler<ArmadaRequestCompletedEventArgs>? handler = RequestCompleted;
            if (handler == null) return;
            try
            {
                handler(this, new ArmadaRequestCompletedEventArgs(method, path, status, duration));
            }
            catch (Exception)
            {
                // Diagnostics observers must not break requests.
            }
        }

        #endregion
    }
}
