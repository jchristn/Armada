namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Runs probe-only (Quick) Evaluates of captains and model endpoints (docs/MODEL_HETEROGENEITY.md, Part 2 and
    /// Decisions 3, 5, 9 and 11): six mechanical probes through the captain's own runtime, each recorded as an
    /// observation. Runs have their own token budget and concurrency limit (overall and per inference host), are tagged
    /// as evaluations, write no stage outcome, and change no routing or captain setting.
    /// </summary>
    public class ModelProbeService
    {
        #region Public-Members

        /// <summary>
        /// Error text for depth Full when no battery service is configured.
        /// </summary>
        public const string FullUnavailableMessage = "Full Evaluate needs the fixture battery, which is not configured on this Admiral; use depth Quick.";

        /// <summary>
        /// Reason recorded when an administrator cancels a run.
        /// </summary>
        public const string CancelledByUserReason = "Cancelled by an administrator.";

        /// <summary>
        /// Fixture battery used by Full Evaluate, or null when Full is unavailable.
        /// </summary>
        public BatteryRunService? Battery { get; set; } = null;

        /// <summary>
        /// How long a Full Evaluate may run once it holds its evaluation slot, or null for the
        /// FullRunTimeoutMinutes setting. Replaceable for tests.
        /// </summary>
        public TimeSpan? FullRunTimeoutOverride { get; set; } = null;

        /// <summary>
        /// Context needed to run the 32K speed exchange (its input plus room to answer).
        /// </summary>
        public const int LargeSpeedContextTokens = 34000;

        /// <summary>
        /// Concurrency gate (overall and per inference host).
        /// </summary>
        public EvaluationConcurrencyGate Gate { get; }

        /// <summary>
        /// Reads an Ollama endpoint's served context for a model. Replaceable for tests.
        /// </summary>
        public Func<ModelEndpoint, string?, CancellationToken, Task<int?>> ServedContextReader
        {
            get => _ServedContextReader;
            set => _ServedContextReader = value ?? throw new ArgumentNullException(nameof(ServedContextReader));
        }

        /// <summary>
        /// Creates the per-run tool-calling values. Replaceable for tests.
        /// </summary>
        public Func<ModelProbeToolTask> ToolTaskFactory
        {
            get => _ToolTaskFactory;
            set => _ToolTaskFactory = value ?? throw new ArgumentNullException(nameof(ToolTaskFactory));
        }

        #endregion

        #region Private-Members

        private readonly string _Header = "[ModelProbeService] ";
        private readonly DatabaseDriver _Database;
        private readonly LoggingModule _Logging;
        private readonly IModelProbeExecutor _Executor;
        private readonly Func<EvaluationSettings> _Settings;
        private readonly StageProvenanceProbe? _Provenance;
        private readonly ModelProfileService _ModelProfiles;
        private readonly ConcurrentDictionary<string, Task<ModelProbeRun>> _Running = new ConcurrentDictionary<string, Task<ModelProbeRun>>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, RunStop> _Stops = new ConcurrentDictionary<string, RunStop>(StringComparer.Ordinal);
        private Func<ModelEndpoint, string?, CancellationToken, Task<int?>> _ServedContextReader = ModelProbeServedContext.ReadOllamaAsync;
        private Func<ModelProbeToolTask> _ToolTaskFactory = ModelProbeToolTask.CreateRandom;
        private const int _MaxExcerptChars = 2000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="executor">Runs probe prompts through captain runtimes.</param>
        /// <param name="settings">Reads the current evaluation settings (applied live).</param>
        /// <param name="provenance">Reads the runtime version and model digest, or null to leave them unset.</param>
        /// <exception cref="ArgumentNullException">Thrown when database, logging, executor or settings is null.</exception>
        public ModelProbeService(DatabaseDriver database, LoggingModule logging, IModelProbeExecutor executor, Func<EvaluationSettings> settings, StageProvenanceProbe? provenance = null)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Provenance = provenance;
            _ModelProfiles = new ModelProfileService(database, logging);
            Gate = new EvaluationConcurrencyGate(() => CurrentSettings().MaxConcurrentRuns, () => CurrentSettings().MaxConcurrentRunsPerHost);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start an Evaluate of a captain through its own runtime. Returns the queued run at once; the probes run in the
        /// background.
        /// </summary>
        /// <param name="captain">Captain.</param>
        /// <param name="endpoint">The captain's model endpoint, or null.</param>
        /// <param name="depth">Depth (Quick, or Full with the defaults).</param>
        /// <param name="tenantId">Caller's tenant (used when the captain has none).</param>
        /// <param name="userId">Caller.</param>
        /// <param name="token">Cancellation token (for creating the run, not for the background probes).</param>
        /// <returns>The queued run.</returns>
        /// <exception cref="ArgumentNullException">Thrown when captain is null.</exception>
        /// <exception cref="ArgumentException">Thrown for Full without a battery or with no valid pack installed.</exception>
        public Task<ModelProbeRun> StartCaptainAsync(Captain captain, ModelEndpoint? endpoint, ModelProbeDepthEnum depth, string? tenantId, string? userId, CancellationToken token = default)
        {
            return StartCaptainAsync(captain, endpoint, depth, null, tenantId, userId, token);
        }

        /// <summary>
        /// Start an Evaluate of a captain. Quick runs the mechanical probes; Full adds a fixture battery run, the evaluator's
        /// subjective grading and an advisory profile revision carrying the report. Returns the queued run at once.
        /// </summary>
        /// <param name="captain">Captain.</param>
        /// <param name="endpoint">The captain's model endpoint, or null.</param>
        /// <param name="depth">Depth.</param>
        /// <param name="options">Full Evaluate parameters, or null for the defaults (ignored for Quick).</param>
        /// <param name="tenantId">Caller's tenant (used when the captain has none).</param>
        /// <param name="userId">Caller.</param>
        /// <param name="token">Cancellation token (for creating the run, not for the background work).</param>
        /// <returns>The queued run.</returns>
        /// <exception cref="ArgumentNullException">Thrown when captain is null.</exception>
        /// <exception cref="ArgumentException">Thrown for Full without a battery, an unknown or malformed pack, a persona the
        /// captain is not allowed, or a run count outside 1 to 10.</exception>
        public Task<ModelProbeRun> StartCaptainAsync(Captain captain, ModelEndpoint? endpoint, ModelProbeDepthEnum depth, ModelEvaluationOptions? options, string? tenantId, string? userId, CancellationToken token = default)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            return StartAsync(ModelProbeTargetEnum.Captain, captain, endpoint, depth, options, tenantId, userId, token);
        }

        /// <summary>
        /// Start an Evaluate of a model endpoint through the in-process API endpoint runtime (a transient captain that is
        /// never saved). Returns the queued run at once.
        /// </summary>
        /// <param name="endpoint">Model endpoint.</param>
        /// <param name="depth">Depth (Quick, or Full with the defaults).</param>
        /// <param name="tenantId">Caller's tenant (used when the endpoint has none).</param>
        /// <param name="userId">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The queued run.</returns>
        /// <exception cref="ArgumentNullException">Thrown when endpoint is null.</exception>
        /// <exception cref="ArgumentException">Thrown for Full without a battery or with no valid pack installed.</exception>
        public Task<ModelProbeRun> StartEndpointAsync(ModelEndpoint endpoint, ModelProbeDepthEnum depth, string? tenantId, string? userId, CancellationToken token = default)
        {
            return StartEndpointAsync(endpoint, depth, null, tenantId, userId, token);
        }

        /// <summary>
        /// Start an Evaluate of a model endpoint. Full runs its battery through a saved captain of the tenant that drives
        /// the endpoint (a transient captain cannot be dispatched); without one the battery is reported as could not run.
        /// </summary>
        /// <param name="endpoint">Model endpoint.</param>
        /// <param name="depth">Depth.</param>
        /// <param name="options">Full Evaluate parameters, or null (ignored for Quick).</param>
        /// <param name="tenantId">Caller's tenant (used when the endpoint has none).</param>
        /// <param name="userId">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The queued run.</returns>
        /// <exception cref="ArgumentNullException">Thrown when endpoint is null.</exception>
        /// <exception cref="ArgumentException">Thrown for Full without a battery, an unknown or malformed pack, or a run count
        /// outside 1 to 10.</exception>
        public Task<ModelProbeRun> StartEndpointAsync(ModelEndpoint endpoint, ModelProbeDepthEnum depth, ModelEvaluationOptions? options, string? tenantId, string? userId, CancellationToken token = default)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            Captain transient = new Captain
            {
                Name = "Evaluate " + endpoint.Name,
                Runtime = AgentRuntimeEnum.ApiEndpoint,
                ModelEndpointId = endpoint.Id,
                Model = endpoint.Model,
                TenantId = endpoint.TenantId ?? tenantId,
                UserId = endpoint.UserId ?? userId
            };
            return StartAsync(ModelProbeTargetEnum.ModelEndpoint, transient, endpoint, depth, options, tenantId, userId, token);
        }

        /// <summary>
        /// Wait for a run started by this service to finish, then return it with its results. A run not running here is
        /// read from the database.
        /// </summary>
        /// <param name="runId">Run identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The run, or null when it does not exist.</returns>
        public async Task<ModelProbeRun?> WaitAsync(string runId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(runId)) throw new ArgumentNullException(nameof(runId));
            if (_Running.TryGetValue(runId, out Task<ModelProbeRun>? running))
                await running.WaitAsync(token).ConfigureAwait(false);
            return await ReadAsync(runId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read a run with its results.
        /// </summary>
        /// <param name="runId">Run identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The run, or null.</returns>
        public async Task<ModelProbeRun?> ReadAsync(string runId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(runId)) throw new ArgumentNullException(nameof(runId));
            ModelProbeRun? run = await _Database.ModelProbeRuns.ReadAsync(runId, token).ConfigureAwait(false);
            if (run == null) return null;
            run.Results = await _Database.ModelProbeResults.ReadByRunAsync(runId, token).ConfigureAwait(false);
            return run;
        }

        /// <summary>
        /// Stop a run started by this service: no further probe, battery task or grading starts, the open battery mission
        /// is cancelled (releasing its dock and captain), and the run ends Cancelled with what finished kept (a Full run
        /// keeps a partial report marked incomplete).
        /// </summary>
        /// <param name="runId">Run identifier.</param>
        /// <param name="reason">Reason to record, or null for "cancelled by an administrator".</param>
        /// <returns>True when the run was running here and is now stopping; false when it is not running here.</returns>
        /// <exception cref="ArgumentNullException">Thrown when runId is null or empty.</exception>
        public bool Cancel(string runId, string? reason = null)
        {
            if (String.IsNullOrEmpty(runId)) throw new ArgumentNullException(nameof(runId));
            if (!_Stops.TryGetValue(runId, out RunStop? stop)) return false;
            stop.Stop(String.IsNullOrWhiteSpace(reason) ? CancelledByUserReason : reason!);
            _Logging.Info(_Header + "cancelling " + runId + ": " + stop.Reason);
            return true;
        }

        /// <summary>
        /// Mark runs left Queued or Running by an earlier Admiral process as Failed (call once at startup).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of runs marked.</returns>
        public async Task<int> RecoverInterruptedAsync(CancellationToken token = default)
        {
            int marked = 0;
            foreach (ModelProbeRunStatusEnum status in new[] { ModelProbeRunStatusEnum.Queued, ModelProbeRunStatusEnum.Running })
            {
                while (true)
                {
                    ModelProbeRunQuery query = new ModelProbeRunQuery { Status = status, PageNumber = 1, PageSize = 100 };
                    EnumerationResult<ModelProbeRun> page = await _Database.ModelProbeRuns.EnumerateAsync(query, token).ConfigureAwait(false);
                    List<ModelProbeRun> stale = page.Objects.FindAll(r => !_Running.ContainsKey(r.Id));
                    if (stale.Count == 0) break;
                    foreach (ModelProbeRun run in stale)
                    {
                        run.Status = ModelProbeRunStatusEnum.Failed;
                        run.FailureReason = "The Admiral restarted before the run finished.";
                        run.CompletedUtc = DateTime.UtcNow;
                        await _Database.ModelProbeRuns.UpdateAsync(run, token).ConfigureAwait(false);
                        marked++;
                    }
                }
            }

            return marked;
        }

        /// <summary>
        /// The inference host key a run's per-host limit and budget apply to: the endpoint's host name (every loopback
        /// address is "localhost"), so models served by one machine share one key whatever their port; for a captain
        /// without an endpoint (a vendor CLI) "vendor:" plus the runtime.
        /// </summary>
        /// <param name="captain">Captain.</param>
        /// <param name="endpoint">Its endpoint, or null.</param>
        /// <returns>The key.</returns>
        /// <exception cref="ArgumentNullException">Thrown when captain is null.</exception>
        public static string InferenceHostFor(Captain captain, ModelEndpoint? endpoint)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (endpoint == null) return "vendor:" + captain.Runtime;
            if (Uri.TryCreate(endpoint.BaseUrl?.Trim(), UriKind.Absolute, out Uri? uri) && !String.IsNullOrEmpty(uri.Host))
                return uri.IsLoopback ? "localhost" : uri.Host.ToLowerInvariant();
            return "endpoint:" + endpoint.Id;
        }

        #endregion

        #region Private-Methods

        private EvaluationSettings CurrentSettings()
        {
            return _Settings() ?? new EvaluationSettings();
        }

        private async Task<ModelProbeRun> StartAsync(ModelProbeTargetEnum kind, Captain captain, ModelEndpoint? endpoint, ModelProbeDepthEnum depth, ModelEvaluationOptions? options, string? tenantId, string? userId, CancellationToken token)
        {
            FullPlan? plan = null;
            if (depth == ModelProbeDepthEnum.Full)
            {
                if (Battery == null) throw new ArgumentException(FullUnavailableMessage, nameof(depth));
                plan = await PlanFullAsync(kind, captain, endpoint, options ?? new ModelEvaluationOptions(), (kind == ModelProbeTargetEnum.Captain ? captain.TenantId : endpoint?.TenantId) ?? tenantId, token).ConfigureAwait(false);
            }

            ModelProbeRun run = new ModelProbeRun
            {
                TenantId = (kind == ModelProbeTargetEnum.Captain ? captain.TenantId : endpoint?.TenantId) ?? tenantId,
                UserId = userId,
                TargetKind = kind,
                CaptainId = kind == ModelProbeTargetEnum.Captain ? captain.Id : null,
                CaptainName = kind == ModelProbeTargetEnum.Captain ? captain.Name : null,
                ModelEndpointId = endpoint?.Id,
                Depth = depth,
                Status = ModelProbeRunStatusEnum.Queued,
                IsEvaluation = true,
                ProbeSetVersion = ModelProbeCatalog.ProbeSetVersion,
                Runtime = captain.Runtime.ToString(),
                ModelProvider = endpoint?.Provider.ToString(),
                Model = !String.IsNullOrWhiteSpace(captain.Model) ? captain.Model : endpoint?.Model,
                ConfiguredContextTokens = endpoint?.InferenceOptions?.ContextLength,
                InferenceHost = InferenceHostFor(captain, endpoint),
                TokenBudget = CurrentSettings().RunTokenBudget
            };

            run = await _Database.ModelProbeRuns.CreateAsync(run, token).ConfigureAwait(false);
            _Logging.Info(_Header + "queued " + run.Id + " for " + (run.CaptainId ?? run.ModelEndpointId) + " on " + run.InferenceHost);

            // The background task keeps changing its own instance; the caller gets a separate copy.
            ModelProbeRun returned = await _Database.ModelProbeRuns.ReadAsync(run.Id, token).ConfigureAwait(false) ?? run;
            ModelProbeRun created = run;
            RunStop stop = new RunStop();
            _Stops[run.Id] = stop;
            Task<ModelProbeRun> execution = Task.Run(() => ExecuteAsync(created, captain, endpoint, plan, stop));
            _Running[run.Id] = execution;
            _ = execution.ContinueWith(t =>
            {
                _Running.TryRemove(created.Id, out Task<ModelProbeRun>? _);
                if (_Stops.TryRemove(created.Id, out RunStop? done)) done.Dispose();
            }, TaskScheduler.Default);
            return returned;
        }

        private async Task<ModelProbeRun> ExecuteAsync(ModelProbeRun run, Captain captain, ModelEndpoint? endpoint, FullPlan? plan, RunStop stop)
        {
            try
            {
                using (await Gate.AcquireAsync(run.InferenceHost, stop.Token).ConfigureAwait(false))
                {
                    // A Full Evaluate holds this host's slot for its probes, battery and grading, so models sharing one
                    // machine are evaluated one at a time; its timeout starts once it holds the slot.
                    if (plan != null)
                    {
                        TimeSpan timeout = FullRunTimeoutOverride ?? TimeSpan.FromMinutes(CurrentSettings().FullRunTimeoutMinutes);
                        string limit = timeout.TotalMinutes >= 1 ? Math.Round(timeout.TotalMinutes, 1) + " minutes" : Math.Round(timeout.TotalSeconds) + " seconds";
                        stop.StopAfter(timeout, "The Full Evaluate timed out after " + limit + ".");
                    }

                    run.Status = ModelProbeRunStatusEnum.Running;
                    run.StartedUtc = DateTime.UtcNow;
                    run = await _Database.ModelProbeRuns.UpdateAsync(run).ConfigureAwait(false);

                    ProbeContext context = await CreateContextAsync(run, captain, endpoint, stop).ConfigureAwait(false);
                    List<Func<ProbeContext, Task<ModelProbeResult>>> probes = new List<Func<ProbeContext, Task<ModelProbeResult>>>
                    {
                        OutputContractAsync, ServedContextAsync, AsciiAsync, ToolCallingAsync, SpeedAsync, DeterminismAsync
                    };
                    foreach (Func<ProbeContext, Task<ModelProbeResult>> probe in probes)
                    {
                        if (stop.IsStopped) break;
                        await RecordAsync(context, await probe(context).ConfigureAwait(false)).ConfigureAwait(false);
                    }

                    if (plan != null) await RunFullAsync(context, plan, stop).ConfigureAwait(false);

                    run.Status = stop.IsStopped ? ModelProbeRunStatusEnum.Cancelled : ModelProbeRunStatusEnum.Completed;
                    if (stop.IsStopped) run.FailureReason = stop.Reason;
                    run.CompletedUtc = DateTime.UtcNow;
                    run = await _Database.ModelProbeRuns.UpdateAsync(run).ConfigureAwait(false);
                    _Logging.Info(_Header + run.Id + " " + run.Status.ToString().ToLowerInvariant() + ": " + run.PassedCount + " passed, " + run.FailedCount + " failed, "
                        + run.CouldNotRunCount + " could not run");

                    // The run names the model as served: find or create its profile and revision (best effort). A Full
                    // report, complete or not, becomes an advisory revision of that profile; nothing else changes.
                    if (plan != null) run = await RecordReportAsync(run, endpoint).ConfigureAwait(false);
                    else await _ModelProfiles.TryResolveAsync(ModelProfileService.ObservationFrom(run, endpoint)).ConfigureAwait(false);
                    return run;
                }
            }
            catch (OperationCanceledException) when (stop.IsStopped)
            {
                // Stopped while waiting for the evaluation slot: nothing ran.
                try
                {
                    run.Status = ModelProbeRunStatusEnum.Cancelled;
                    run.FailureReason = stop.Reason;
                    run.CompletedUtc = DateTime.UtcNow;
                    if (plan != null) run.Report = IncompleteReport(run, plan, stop.Reason + " (before it started)");
                    run = await _Database.ModelProbeRuns.UpdateAsync(run).ConfigureAwait(false);
                    if (plan != null) run = await RecordReportAsync(run, endpoint).ConfigureAwait(false);
                }
                catch (Exception update)
                {
                    _Logging.Warn(_Header + "could not record the cancellation of " + run.Id + ": " + update.Message);
                }

                return run;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + run.Id + " failed: " + ex.Message);
                try
                {
                    run.Status = ModelProbeRunStatusEnum.Failed;
                    run.FailureReason = ex.Message;
                    run.CompletedUtc = DateTime.UtcNow;
                    run = await _Database.ModelProbeRuns.UpdateAsync(run).ConfigureAwait(false);
                }
                catch (Exception update)
                {
                    _Logging.Warn(_Header + "could not record the failure of " + run.Id + ": " + update.Message);
                }

                return run;
            }
        }

        private async Task<FullPlan> PlanFullAsync(ModelProbeTargetEnum kind, Captain captain, ModelEndpoint? endpoint, ModelEvaluationOptions options, string? tenantId, CancellationToken token)
        {
            CollectionLimits.RequireConfigItems(options.Packs, "packs");
            CollectionLimits.RequireConfigItems(options.Personas, "personas");
            if (options.RunCount.HasValue && (options.RunCount.Value < 1 || options.RunCount.Value > BatteryRunService.MaxRunCount))
                throw new ArgumentException("runCount must be 1 to " + BatteryRunService.MaxRunCount + ".", nameof(options));

            FullPlan plan = new FullPlan
            {
                TenantId = String.IsNullOrWhiteSpace(tenantId) ? Constants.DefaultTenantId : tenantId!,
                CandidateFamily = ModelFamily.Of(captain, endpoint),
                RunCount = options.RunCount,
                EvaluatorCaptainId = String.IsNullOrWhiteSpace(options.EvaluatorCaptainId) ? null : options.EvaluatorCaptainId!.Trim()
            };

            // Personas: the requested ones, each of which the captain must be allowed; by default all it is allowed.
            List<string>? allowed = AllowedPersonas(captain);
            List<string> requested = (options.Personas ?? new List<string>())
                .Where(p => !String.IsNullOrWhiteSpace(p)).Select(p => PersonaCatalog.NormalizeName(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (string persona in requested)
            {
                if (allowed != null && !allowed.Any(a => PersonaCatalog.Matches(a, persona)))
                    throw new ArgumentException("The captain is not allowed the persona '" + persona + "' (allowed: " + String.Join(", ", allowed) + ").", nameof(options));
            }

            plan.PersonaFilter = requested.Count > 0 ? requested : allowed;

            // Packs: the requested ones, or those matching the languages of the tenant's vessels.
            List<BatteryPack> all = Battery!.LoadPacks();
            List<string> packIds = (options.Packs ?? new List<string>()).Where(p => !String.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct(StringComparer.Ordinal).ToList();
            if (packIds.Count > 0)
            {
                foreach (string id in packIds)
                {
                    BatteryPack? pack = all.FirstOrDefault(p => String.Equals(p.Id, id, StringComparison.Ordinal));
                    if (pack == null) throw new ArgumentException("Unknown battery pack '" + id + "'.", nameof(options));
                    if (!pack.IsValid) throw new ArgumentException("Battery pack '" + id + "' is malformed: " + String.Join(" ", pack.Errors), nameof(options));
                }
            }
            else
            {
                HashSet<string> languages = await VesselLanguagesAsync(plan.TenantId, token).ConfigureAwait(false);
                packIds = all.Where(p => p.IsValid && languages.Contains(PackLanguage(p.Id))).Select(p => p.Id).ToList();
                if (packIds.Count > 0)
                {
                    plan.Notes.Add("Packs chosen from the languages of the tenant's repositories: " + String.Join(", ", packIds) + ".");
                }
                else
                {
                    BatteryPack? fallback = all.FirstOrDefault(p => p.IsValid && String.Equals(p.Id, "csharp", StringComparison.Ordinal)) ?? all.FirstOrDefault(p => p.IsValid);
                    if (fallback == null) throw new ArgumentException("No valid battery pack is installed, so a Full Evaluate cannot run.", nameof(options));
                    packIds.Add(fallback.Id);
                    plan.Notes.Add("No repository language matched a battery pack, so the " + fallback.Id + " pack was used; name packs to choose others.");
                }
            }

            plan.Packs = packIds;
            List<BatteryPack> chosen = all.Where(p => packIds.Contains(p.Id, StringComparer.Ordinal) && p.Manifest != null).ToList();
            List<string> tested = chosen.SelectMany(p => p.Manifest!.Tasks).Select(t => PersonaCatalog.NormalizeName(t.Persona))
                .Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.Ordinal).ToList();
            plan.Personas = plan.PersonaFilter == null
                ? tested
                : plan.PersonaFilter.Where(p => tested.Any(t => PersonaCatalog.Matches(t, p))).ToList();
            if (plan.PersonaFilter != null && plan.Personas.Count == 0)
                throw new ArgumentException("No task of the packs (" + String.Join(", ", packIds) + ") tests the selected personas (" + String.Join(", ", plan.PersonaFilter) + ").", nameof(options));

            // The battery dispatches real missions, so it needs a saved captain: the candidate, or for a model endpoint a
            // captain of the tenant that drives it.
            if (kind == ModelProbeTargetEnum.Captain)
            {
                plan.BatteryCaptain = captain;
                plan.BatteryEndpoint = endpoint;
            }
            else if (endpoint != null)
            {
                List<Captain> captains = await _Database.Captains.EnumerateAsync(plan.TenantId, token).ConfigureAwait(false);
                plan.BatteryCaptain = captains.Where(c => String.Equals(c.ModelEndpointId, endpoint.Id, StringComparison.Ordinal)).OrderBy(c => c.Id, StringComparer.Ordinal).FirstOrDefault();
                plan.BatteryEndpoint = endpoint;
                if (plan.BatteryCaptain == null)
                    plan.BatteryUnavailableReason = "No captain drives model endpoint " + endpoint.Name + ", so the fixture battery could not run (it dispatches real missions); create a captain for the endpoint and evaluate that captain.";
                else
                    plan.Notes.Add("The battery ran through captain " + plan.BatteryCaptain.Name + ", which drives this endpoint.");
            }

            return plan;
        }

        private async Task RunFullAsync(ProbeContext context, FullPlan plan, RunStop stop)
        {
            ModelProbeRun run = context.Run;
            ModelEvaluationReport report = NewReport(run, plan);
            List<BatteryTaskResult> battery = new List<BatteryTaskResult>();
            string? incomplete = null;

            if (stop.IsStopped)
            {
                incomplete = stop.Reason + " (during the probes)";
            }
            else if (plan.BatteryCaptain == null)
            {
                incomplete = plan.BatteryUnavailableReason ?? "The fixture battery could not run.";
            }
            else
            {
                BatteryRunOptions options = new BatteryRunOptions
                {
                    Personas = plan.PersonaFilter,
                    TokenBudget = await BatteryBudgetAsync(run).ConfigureAwait(false),
                    Cancellation = stop.Token,
                    HoldsEvaluationSlot = true
                };

                using (stop.Token.Register(() => options.StopReason = stop.Reason))
                {
                    try
                    {
                        BatteryRun started = await Battery!.StartAsync(plan.BatteryCaptain, plan.BatteryEndpoint, plan.Packs, plan.RunCount, null, true,
                            run.TenantId, run.UserId, null, options, CancellationToken.None).ConfigureAwait(false);
                        run.BatteryRunId = started.Id;
                        report.BatteryRunId = started.Id;
                        report.BatteryCaptainId = plan.BatteryCaptain.Id;
                        run.Report = report;
                        await _Database.ModelProbeRuns.UpdateAsync(run).ConfigureAwait(false);

                        BatteryRun? finished = await Battery.WaitAsync(started.Id).ConfigureAwait(false);
                        battery = finished?.Results ?? new List<BatteryTaskResult>();
                        report.BatteryStatus = (finished?.Status ?? BatteryRunStatusEnum.Failed).ToString();
                        if (finished == null || finished.Status != BatteryRunStatusEnum.Completed)
                            incomplete = "The fixture battery did not finish: " + (finished?.FailureReason ?? options.StoppedEarlyReason ?? "its run could not be read.");
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        // A battery that cannot start (a missing pipeline, a fixture that cannot be created) leaves the
                        // probes and the report standing; it never fails the whole evaluation.
                        _Logging.Warn(_Header + run.Id + " battery could not start: " + ex.Message);
                        incomplete = "The fixture battery could not start: " + ex.Message;
                    }
                }

                report.BatteryTokensUsed = options.TokensUsed;
                run.TokensUsed += options.TokensUsed;
            }

            ModelEvaluationReportBuilder.Fill(report, await _Database.ModelProbeResults.ReadByRunAsync(run.Id).ConfigureAwait(false), battery);

            if (stop.IsStopped)
                report.Narrative = ModelEvaluationNarrative.Skipped("The run was stopped before the evaluator's grading: " + stop.Reason);
            else
                report.Narrative = await GradeAsync(context, plan, report, battery, stop).ConfigureAwait(false);

            if (stop.IsStopped && incomplete == null) incomplete = stop.Reason;
            report.Complete = incomplete == null;
            report.IncompleteReason = incomplete;
            run.Report = report;
        }

        private async Task<ModelEvaluationNarrative> GradeAsync(ProbeContext context, FullPlan plan, ModelEvaluationReport report, List<BatteryTaskResult> battery, RunStop stop)
        {
            ModelProbeRun run = context.Run;
            EvaluatorSelection selection = await SelectEvaluatorAsync(plan, run).ConfigureAwait(false);
            report.EvaluatorCaptainId = selection.Captain?.Id;
            report.EvaluatorCaptainName = selection.Captain?.Name;
            report.EvaluatorFamily = selection.Family;
            report.EvaluatorExplicit = selection.Explicit;
            if (selection.Warning != null) report.Warnings.Add(selection.Warning);
            if (selection.Captain == null) return ModelEvaluationNarrative.Skipped(selection.SkippedReason ?? "No evaluator was available.");

            List<BatteryTaskResult> pending = battery.Where(r => r.Outcome == BatteryTaskOutcomeEnum.PendingEvaluation).Take(ModelEvaluationPrompt.MaxPendingShown).ToList();
            string candidate = run.Runtime + (String.IsNullOrWhiteSpace(run.Model) ? String.Empty : " " + run.Model);
            string prompt = ModelEvaluationPrompt.Build(report, candidate, pending, r => Battery?.RubricFor(r.PackId, r.TaskId));
            ModelProbeExchangeRequest request = new ModelProbeExchangeRequest
            {
                Captain = selection.Captain,
                ModelEndpoint = selection.Endpoint,
                RunId = run.Id,
                Label = "Evaluator grading",
                Prompt = prompt,
                TenantId = run.TenantId,
                UserId = run.UserId,
                TimeoutMs = context.TimeoutMs
            };

            ModelProbeExchange exchange;
            try
            {
                exchange = await _Executor.RunAsync(request, stop.Token).ConfigureAwait(false) ?? ModelProbeExchange.NotStarted("The runtime returned nothing.");
            }
            catch (OperationCanceledException) when (stop.IsStopped)
            {
                return ModelEvaluationNarrative.Skipped("The run was stopped during the evaluator's grading: " + stop.Reason);
            }
            catch (Exception ex)
            {
                exchange = ModelProbeExchange.NotStarted("The evaluator's runtime could not be launched: " + ex.Message);
            }

            if (exchange.CouldNotRunReason != null)
                return ModelEvaluationNarrative.Skipped("The evaluator " + selection.Captain.Name + " could not run, so the subjective grading was skipped: " + exchange.CouldNotRunReason);

            run.TokensUsed += ModelProbeCatalog.EstimateTokens(prompt) + ModelProbeCatalog.EstimateTokens(exchange.Output);
            if (exchange.TimedOut && String.IsNullOrWhiteSpace(exchange.Output))
                return EvaluationNarrativeValidator.Rejected("The evaluator timed out without a reply.", exchange.Output);
            ModelEvaluationNarrative narrative = EvaluationNarrativeValidator.Validate(exchange.Output, report, pending.Select(r => r.Id).ToList());
            foreach (ModelEvaluationSubjectiveGrade grade in narrative.SubjectiveGrades)
            {
                BatteryTaskResult? graded = pending.FirstOrDefault(r => String.Equals(r.Id, grade.ResultId, StringComparison.Ordinal));
                if (graded != null) grade.TaskKey = ModelEvaluationReportBuilder.TaskKey(graded.PackId, graded.TaskId);
            }

            return narrative;
        }

        private async Task<EvaluatorSelection> SelectEvaluatorAsync(FullPlan plan, ModelProbeRun run)
        {
            if (plan.EvaluatorCaptainId != null)
            {
                Captain? named = await _Database.Captains.ReadAsync(plan.EvaluatorCaptainId).ConfigureAwait(false);
                if (named == null)
                    return new EvaluatorSelection { Explicit = true, SkippedReason = "The named evaluator " + plan.EvaluatorCaptainId + " no longer exists, so the subjective grading was skipped." };
                return EvaluatorSelector.Named(plan.CandidateFamily, named, await EndpointOfAsync(named).ConfigureAwait(false));
            }

            List<Captain> idle = await _Database.Captains.EnumerateByStateAsync(plan.TenantId, CaptainStateEnum.Idle).ConfigureAwait(false);
            Dictionary<string, ModelEndpoint?> endpoints = new Dictionary<string, ModelEndpoint?>(StringComparer.Ordinal);
            foreach (Captain captain in idle) endpoints[captain.Id] = await EndpointOfAsync(captain).ConfigureAwait(false);
            return EvaluatorSelector.Select(plan.CandidateFamily, run.CaptainId ?? plan.BatteryCaptain?.Id, idle, c => endpoints.TryGetValue(c.Id, out ModelEndpoint? e) ? e : null);
        }

        private async Task<ModelEndpoint?> EndpointOfAsync(Captain captain)
        {
            if (String.IsNullOrWhiteSpace(captain.ModelEndpointId)) return null;
            return await _Database.ModelEndpoints.ReadAsync(captain.ModelEndpointId!).ConfigureAwait(false);
        }

        private async Task<long> BatteryBudgetAsync(ModelProbeRun run)
        {
            // The Full budget, tightened by what is left of the daily evaluation budgets (overall and for this host).
            EvaluationSettings settings = CurrentSettings();
            long budget = settings.FullRunTokenBudget > 0 ? settings.FullRunTokenBudget : Int64.MaxValue;
            DateTime since = DateTime.UtcNow.AddHours(-24);
            if (settings.DailyTokenBudget > 0)
                budget = Math.Min(budget, settings.DailyTokenBudget - await _Database.ModelProbeRuns.SumTokensUsedSinceAsync(since, null).ConfigureAwait(false) - run.TokensUsed);
            if (settings.DailyTokenBudgetPerHost > 0)
                budget = Math.Min(budget, settings.DailyTokenBudgetPerHost - await _Database.ModelProbeRuns.SumTokensUsedSinceAsync(since, run.InferenceHost).ConfigureAwait(false) - run.TokensUsed);
            if (budget == Int64.MaxValue) return 0;
            return Math.Max(1, budget);
        }

        private async Task<HashSet<string>> VesselLanguagesAsync(string tenantId, CancellationToken token)
        {
            HashSet<string> languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int page = 1; page <= 20; page++)
            {
                VesselHealthEnumerateRequest request = new VesselHealthEnumerateRequest { PageNumber = page, PageSize = 500 };
                EnumerationResult<VesselHealth> result = await _Database.VesselHealth.EnumerateAsync(tenantId, request, token).ConfigureAwait(false);
                foreach (VesselHealth health in result.Objects)
                {
                    string? language = LanguagePack(health.PrimaryLanguage);
                    if (language != null) languages.Add(language);
                }

                if (result.Objects.Count < request.PageSize || page >= result.TotalPages) break;
            }

            return languages;
        }

        private static string? LanguagePack(string? language)
        {
            switch ((language ?? String.Empty).Trim().ToLowerInvariant())
            {
                case "csharp": case "c#": return "csharp";
                case "javascript": case "typescript": return "javascript";
                case "python": return "python";
                case "java": case "kotlin": return "java";
                case "rust": return "rust";
                case "go": return "go";
                case "php": return "php";
                case "ruby": return "ruby";
                default: return null;
            }
        }

        private static string PackLanguage(string packId)
        {
            return LanguagePack(packId) ?? packId;
        }

        private static List<string>? AllowedPersonas(Captain captain)
        {
            if (String.IsNullOrWhiteSpace(captain.AllowedPersonas)) return null;
            try
            {
                List<string>? parsed = JsonSerializer.Deserialize<List<string>>(captain.AllowedPersonas!);
                if (parsed == null) return null;
                List<string> names = parsed.Where(p => !String.IsNullOrWhiteSpace(p)).Select(p => PersonaCatalog.NormalizeName(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                return names.Count == 0 ? null : names;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static ModelEvaluationReport NewReport(ModelProbeRun run, FullPlan plan)
        {
            ModelEvaluationReport report = new ModelEvaluationReport
            {
                RunId = run.Id,
                CandidateFamily = plan.CandidateFamily,
                Packs = new List<string>(plan.Packs),
                Personas = new List<string>(plan.Personas),
                CreatedUtc = DateTime.UtcNow
            };
            report.Notes.AddRange(plan.Notes);
            return report;
        }

        private async Task<ModelProbeRun> RecordReportAsync(ModelProbeRun run, ModelEndpoint? endpoint)
        {
            // An incomplete report (cancel or timeout) is recorded too, marked incomplete; it never fills the measured
            // block and never becomes the current revision.
            ModelProfileResolution? resolution = await _ModelProfiles.TryResolveAsync(ModelProfileService.ObservationFrom(run, endpoint)).ConfigureAwait(false);
            if (run.Report == null || resolution == null) return run;
            try
            {
                await _ModelProfiles.RecordEvaluationAsync(resolution, run.Report).ConfigureAwait(false);
                return await _Database.ModelProbeRuns.UpdateAsync(run).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not record the Full Evaluate report of " + run.Id + " on profile " + resolution.Profile.Id + ": " + ex.Message);
                return run;
            }
        }

        private static ModelEvaluationReport IncompleteReport(ModelProbeRun run, FullPlan plan, string reason)
        {
            ModelEvaluationReport report = NewReport(run, plan);
            ModelEvaluationReportBuilder.Fill(report, new List<ModelProbeResult>(), new List<BatteryTaskResult>());
            report.Narrative = ModelEvaluationNarrative.Skipped(reason);
            report.Complete = false;
            report.IncompleteReason = reason;
            return report;
        }

        private async Task<ProbeContext> CreateContextAsync(ModelProbeRun run, Captain captain, ModelEndpoint? endpoint, RunStop stop)
        {
            EvaluationSettings settings = CurrentSettings();
            ProbeContext context = new ProbeContext(run, captain, endpoint, settings.ProbeTimeoutSeconds * 1000, stop.Token);

            // The budget is the tightest of the run's own budget and what is left of the daily budgets (overall and for
            // this host) at the start of the run.
            long remaining = run.TokenBudget;
            DateTime since = DateTime.UtcNow.AddHours(-24);
            if (settings.DailyTokenBudget > 0)
            {
                long used = await _Database.ModelProbeRuns.SumTokensUsedSinceAsync(since, null).ConfigureAwait(false);
                if (settings.DailyTokenBudget - used < remaining)
                {
                    remaining = settings.DailyTokenBudget - used;
                    context.BudgetName = "daily evaluation token budget (" + settings.DailyTokenBudget + ")";
                }
            }

            if (settings.DailyTokenBudgetPerHost > 0)
            {
                long used = await _Database.ModelProbeRuns.SumTokensUsedSinceAsync(since, run.InferenceHost).ConfigureAwait(false);
                if (settings.DailyTokenBudgetPerHost - used < remaining)
                {
                    remaining = settings.DailyTokenBudgetPerHost - used;
                    context.BudgetName = "daily evaluation token budget for " + run.InferenceHost + " (" + settings.DailyTokenBudgetPerHost + ")";
                }
            }

            context.Remaining = Math.Max(0, remaining);
            return context;
        }

        private async Task<ModelProbeExchange> ExchangeAsync(ProbeContext context, ModelProbeResult? into, string label, string prompt)
        {
            if (context.Stop.IsCancellationRequested) return ModelProbeExchange.NotStarted("The run was stopped before this exchange.");
            long estimate = ModelProbeCatalog.EstimateTokens(prompt) + ModelProbeCatalog.AssumedOutputTokens;
            if (estimate > context.Remaining)
                return ModelProbeExchange.NotStarted("The " + context.BudgetName + " is exhausted: about " + estimate
                    + " tokens needed, " + context.Remaining + " left.");

            ModelProbeExchangeRequest request = new ModelProbeExchangeRequest
            {
                Captain = context.Captain,
                ModelEndpoint = context.Endpoint,
                RunId = context.Run.Id,
                Label = label,
                Prompt = prompt,
                TenantId = context.Run.TenantId,
                UserId = context.Run.UserId,
                TimeoutMs = context.TimeoutMs
            };

            ModelProbeExchange exchange;
            try
            {
                exchange = await _Executor.RunAsync(request, context.Stop).ConfigureAwait(false) ?? ModelProbeExchange.NotStarted("The runtime returned nothing.");
            }
            catch (OperationCanceledException) when (context.Stop.IsCancellationRequested)
            {
                exchange = ModelProbeExchange.NotStarted("The run was stopped during this exchange.");
            }
            catch (Exception ex)
            {
                exchange = ModelProbeExchange.NotStarted("The runtime could not be launched: " + ex.Message);
            }

            if (exchange.CouldNotRunReason != null) return exchange;

            long used = ModelProbeCatalog.EstimateTokens(prompt) + ModelProbeCatalog.EstimateTokens(exchange.Output);
            context.Remaining = Math.Max(0, context.Remaining - used);
            context.Run.TokensUsed += used;
            if (into != null)
            {
                into.TokensUsed += used;
                into.OutputExcerpt = Excerpt(exchange.Output);
            }

            if (!context.ProvenanceRead)
            {
                context.ProvenanceRead = true;
                context.Run.HarborId = exchange.HarborId;
                await ReadProvenanceAsync(context).ConfigureAwait(false);
            }

            return exchange;
        }

        private async Task ReadProvenanceAsync(ProbeContext context)
        {
            if (_Provenance == null) return;
            try
            {
                context.Run.RuntimeVersion = await _Provenance.GetRuntimeVersionAsync(context.Captain, context.Run.HarborId, context.Endpoint).ConfigureAwait(false);
                if (context.Endpoint != null)
                    context.Run.ModelDigest = await _Provenance.GetModelDigestAsync(context.Endpoint, context.Run.Model, context.Run.HarborId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Debug(_Header + "could not read provenance for " + context.Run.Id + ": " + ex.Message);
            }
        }

        private async Task<ModelProbeResult> OutputContractAsync(ProbeContext context)
        {
            ModelProbeResult tally = new ModelProbeResult();
            DateTime started = DateTime.UtcNow;
            ModelProbeExchange worker = await ExchangeAsync(context, tally, "Output contract (Worker)", ModelProbeCatalog.WorkerContractPrompt()).ConfigureAwait(false);
            ModelProbeExchange judge = await ExchangeAsync(context, tally, "Output contract (Judge)", ModelProbeCatalog.JudgeContractPrompt()).ConfigureAwait(false);
            ModelProbeExchange architect = await ExchangeAsync(context, tally, "Output contract (Architect)", ModelProbeCatalog.ArchitectContractPrompt()).ConfigureAwait(false);
            ModelProbeResult result = ModelProbeScorer.ScoreOutputContract(OutputOf(worker), OutputOf(judge), OutputOf(architect),
                worker.CouldNotRunReason ?? judge.CouldNotRunReason ?? architect.CouldNotRunReason);
            return Finish(result, tally, started);
        }

        private async Task<ModelProbeResult> ServedContextAsync(ProbeContext context)
        {
            DateTime started = DateTime.UtcNow;
            int? configured = context.Run.ConfiguredContextTokens;
            string? reason = ModelProbeServedContext.CouldNotRunReason(context.Captain, context.Endpoint, context.Run.HarborId);
            int? served = null;
            if (reason == null && context.Endpoint != null)
            {
                try
                {
                    served = await _ServedContextReader(context.Endpoint, context.Run.Model, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    reason = "The endpoint could not be asked for its served context: " + ex.Message;
                }
            }

            ModelProbeResult result = ModelProbeScorer.ScoreServedContext(configured, served, reason);
            if (result.Metrics.ServedContextTokens.HasValue) context.Run.ServedContextTokens = result.Metrics.ServedContextTokens;
            return Finish(result, new ModelProbeResult(), started);
        }

        private async Task<ModelProbeResult> AsciiAsync(ProbeContext context)
        {
            ModelProbeResult tally = new ModelProbeResult();
            DateTime started = DateTime.UtcNow;
            ModelProbeExchange exchange = await ExchangeAsync(context, tally, "ASCII and format rules", ModelProbeCatalog.AsciiPrompt()).ConfigureAwait(false);
            return Finish(ModelProbeScorer.ScoreAscii(OutputOf(exchange), exchange.CouldNotRunReason), tally, started);
        }

        private async Task<ModelProbeResult> ToolCallingAsync(ProbeContext context)
        {
            ModelProbeResult tally = new ModelProbeResult();
            DateTime started = DateTime.UtcNow;
            ModelProbeToolTask task = _ToolTaskFactory();
            if (context.Endpoint != null && context.Endpoint.InferenceOptions != null && !context.Endpoint.InferenceOptions.ToolsEnabled)
                return Finish(ModelProbeScorer.ScoreToolCalling(null, task, false, "Tools are disabled on the model endpoint."), tally, started);

            ModelProbeExchange exchange = await ExchangeAsync(context, tally, "Tool calling", ModelProbeCatalog.ToolCallingPrompt(task)).ConfigureAwait(false);
            return Finish(ModelProbeScorer.ScoreToolCalling(OutputOf(exchange), task, exchange.TimedOut, exchange.CouldNotRunReason), tally, started);
        }

        private async Task<ModelProbeResult> SpeedAsync(ProbeContext context)
        {
            ModelProbeResult tally = new ModelProbeResult();
            DateTime started = DateTime.UtcNow;
            ModelProbeExchange small = await ExchangeAsync(context, tally, "Speed (4K)", ModelProbeCatalog.SpeedPrompt(ModelProbeCatalog.SpeedSmallInputTokens)).ConfigureAwait(false);
            int? configured = context.Run.ConfiguredContextTokens;
            ModelProbeExchange large = configured.HasValue && configured.Value < LargeSpeedContextTokens
                ? ModelProbeExchange.NotStarted("The configured context (" + configured.Value + " tokens) is below the 32K speed input.")
                : await ExchangeAsync(context, tally, "Speed (32K)", ModelProbeCatalog.SpeedPrompt(ModelProbeCatalog.SpeedLargeInputTokens)).ConfigureAwait(false);
            return Finish(ModelProbeScorer.ScoreSpeed(small, large), tally, started);
        }

        private async Task<ModelProbeResult> DeterminismAsync(ProbeContext context)
        {
            ModelProbeResult tally = new ModelProbeResult();
            DateTime started = DateTime.UtcNow;
            List<string> outputs = new List<string>();
            string? reason = null;
            for (int i = 0; i < ModelProbeCatalog.DeterminismRepeats; i++)
            {
                ModelProbeExchange exchange = await ExchangeAsync(context, tally, "Determinism (" + (i + 1) + " of " + ModelProbeCatalog.DeterminismRepeats + ")", ModelProbeCatalog.DeterminismPrompt()).ConfigureAwait(false);
                if (exchange.CouldNotRunReason != null) reason ??= exchange.CouldNotRunReason;
                else outputs.Add(exchange.Output ?? String.Empty);
            }

            return Finish(ModelProbeScorer.ScoreDeterminism(outputs, reason), tally, started);
        }

        private async Task RecordAsync(ProbeContext context, ModelProbeResult result)
        {
            result.RunId = context.Run.Id;
            result.TenantId = context.Run.TenantId;
            await _Database.ModelProbeResults.CreateAsync(result).ConfigureAwait(false);
            if (result.Outcome == ModelProbeOutcomeEnum.Passed) context.Run.PassedCount++;
            else if (result.Outcome == ModelProbeOutcomeEnum.Failed) context.Run.FailedCount++;
            else context.Run.CouldNotRunCount++;
            await _Database.ModelProbeRuns.UpdateAsync(context.Run).ConfigureAwait(false);
        }

        private static ModelProbeResult Finish(ModelProbeResult result, ModelProbeResult tally, DateTime startedUtc)
        {
            result.TokensUsed = tally.TokensUsed;
            result.OutputExcerpt = tally.OutputExcerpt;
            result.DurationMs = (long)(DateTime.UtcNow - startedUtc).TotalMilliseconds;
            result.CreatedUtc = DateTime.UtcNow;
            return result;
        }

        private static string? OutputOf(ModelProbeExchange exchange)
        {
            return exchange.CouldNotRunReason != null ? null : (exchange.Output ?? String.Empty);
        }

        private static string? Excerpt(string? output)
        {
            if (String.IsNullOrEmpty(output)) return null;
            return output.Length <= _MaxExcerptChars ? output : output.Substring(0, _MaxExcerptChars);
        }

        #endregion

        #region Private-Classes

        private class FullPlan
        {
            public string TenantId { get; set; } = Constants.DefaultTenantId;
            public string CandidateFamily { get; set; } = "";
            public List<string> Packs { get; set; } = new List<string>();
            public List<string>? PersonaFilter { get; set; } = null;
            public List<string> Personas { get; set; } = new List<string>();
            public int? RunCount { get; set; } = null;
            public string? EvaluatorCaptainId { get; set; } = null;
            public Captain? BatteryCaptain { get; set; } = null;
            public ModelEndpoint? BatteryEndpoint { get; set; } = null;
            public string? BatteryUnavailableReason { get; set; } = null;
            public List<string> Notes { get; } = new List<string>();
        }

        private class RunStop : IDisposable
        {
            private readonly CancellationTokenSource _Source = new CancellationTokenSource();
            private readonly object _Lock = new object();
            private string? _Reason = null;
            private Timer? _Timer = null;

            public CancellationToken Token => _Source.Token;
            public bool IsStopped => _Source.IsCancellationRequested;
            public string Reason
            {
                get { lock (_Lock) return _Reason ?? CancelledByUserReason; }
            }

            public void Stop(string reason)
            {
                lock (_Lock)
                {
                    if (_Reason != null) return;
                    _Reason = reason;
                }

                try
                {
                    _Source.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            public void StopAfter(TimeSpan delay, string reason)
            {
                lock (_Lock)
                {
                    _Timer?.Dispose();
                    _Timer = new Timer(_ => Stop(reason), null, delay, Timeout.InfiniteTimeSpan);
                }
            }

            public void Dispose()
            {
                lock (_Lock)
                {
                    _Timer?.Dispose();
                    _Timer = null;
                }

                _Source.Dispose();
            }
        }

        private class ProbeContext
        {
            public ModelProbeRun Run { get; }
            public Captain Captain { get; }
            public ModelEndpoint? Endpoint { get; }
            public int TimeoutMs { get; }
            public long Remaining { get; set; } = 0;
            public string BudgetName { get; set; } = "evaluation run token budget";
            public bool ProvenanceRead { get; set; } = false;

            public CancellationToken Stop { get; }

            public ProbeContext(ModelProbeRun run, Captain captain, ModelEndpoint? endpoint, int timeoutMs, CancellationToken stop)
            {
                Stop = stop;
                Run = run;
                Captain = captain;
                Endpoint = endpoint;
                TimeoutMs = timeoutMs;
            }
        }

        #endregion
    }
}
