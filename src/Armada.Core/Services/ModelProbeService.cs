namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
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
        /// Error text for depth Full.
        /// </summary>
        public const string FullNotImplementedMessage = "Full Evaluate is not implemented until Phase 5 (it adds the fixture battery); use depth Quick.";

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
        /// <param name="depth">Depth; only Quick is implemented.</param>
        /// <param name="tenantId">Caller's tenant (used when the captain has none).</param>
        /// <param name="userId">Caller.</param>
        /// <param name="token">Cancellation token (for creating the run, not for the background probes).</param>
        /// <returns>The queued run.</returns>
        /// <exception cref="ArgumentNullException">Thrown when captain is null.</exception>
        /// <exception cref="ArgumentException">Thrown for depth Full.</exception>
        public Task<ModelProbeRun> StartCaptainAsync(Captain captain, ModelEndpoint? endpoint, ModelProbeDepthEnum depth, string? tenantId, string? userId, CancellationToken token = default)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            return StartAsync(ModelProbeTargetEnum.Captain, captain, endpoint, depth, tenantId, userId, token);
        }

        /// <summary>
        /// Start an Evaluate of a model endpoint through the in-process API endpoint runtime (a transient captain that is
        /// never saved). Returns the queued run at once.
        /// </summary>
        /// <param name="endpoint">Model endpoint.</param>
        /// <param name="depth">Depth; only Quick is implemented.</param>
        /// <param name="tenantId">Caller's tenant (used when the endpoint has none).</param>
        /// <param name="userId">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The queued run.</returns>
        /// <exception cref="ArgumentNullException">Thrown when endpoint is null.</exception>
        /// <exception cref="ArgumentException">Thrown for depth Full.</exception>
        public Task<ModelProbeRun> StartEndpointAsync(ModelEndpoint endpoint, ModelProbeDepthEnum depth, string? tenantId, string? userId, CancellationToken token = default)
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
            return StartAsync(ModelProbeTargetEnum.ModelEndpoint, transient, endpoint, depth, tenantId, userId, token);
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

        private async Task<ModelProbeRun> StartAsync(ModelProbeTargetEnum kind, Captain captain, ModelEndpoint? endpoint, ModelProbeDepthEnum depth, string? tenantId, string? userId, CancellationToken token)
        {
            if (depth == ModelProbeDepthEnum.Full) throw new ArgumentException(FullNotImplementedMessage, nameof(depth));

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
            Task<ModelProbeRun> execution = Task.Run(() => ExecuteAsync(created, captain, endpoint));
            _Running[run.Id] = execution;
            _ = execution.ContinueWith(t => _Running.TryRemove(created.Id, out Task<ModelProbeRun>? _), TaskScheduler.Default);
            return returned;
        }

        private async Task<ModelProbeRun> ExecuteAsync(ModelProbeRun run, Captain captain, ModelEndpoint? endpoint)
        {
            try
            {
                using (await Gate.AcquireAsync(run.InferenceHost).ConfigureAwait(false))
                {
                    run.Status = ModelProbeRunStatusEnum.Running;
                    run.StartedUtc = DateTime.UtcNow;
                    run = await _Database.ModelProbeRuns.UpdateAsync(run).ConfigureAwait(false);

                    ProbeContext context = await CreateContextAsync(run, captain, endpoint).ConfigureAwait(false);
                    await RecordAsync(context, await OutputContractAsync(context).ConfigureAwait(false)).ConfigureAwait(false);
                    await RecordAsync(context, await ServedContextAsync(context).ConfigureAwait(false)).ConfigureAwait(false);
                    await RecordAsync(context, await AsciiAsync(context).ConfigureAwait(false)).ConfigureAwait(false);
                    await RecordAsync(context, await ToolCallingAsync(context).ConfigureAwait(false)).ConfigureAwait(false);
                    await RecordAsync(context, await SpeedAsync(context).ConfigureAwait(false)).ConfigureAwait(false);
                    await RecordAsync(context, await DeterminismAsync(context).ConfigureAwait(false)).ConfigureAwait(false);

                    run.Status = ModelProbeRunStatusEnum.Completed;
                    run.CompletedUtc = DateTime.UtcNow;
                    run = await _Database.ModelProbeRuns.UpdateAsync(run).ConfigureAwait(false);
                    _Logging.Info(_Header + run.Id + " completed: " + run.PassedCount + " passed, " + run.FailedCount + " failed, "
                        + run.CouldNotRunCount + " could not run");

                    // The run names the model as served: find or create its profile and revision (best effort).
                    await _ModelProfiles.TryResolveAsync(ModelProfileService.ObservationFrom(run, endpoint)).ConfigureAwait(false);
                    return run;
                }
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

        private async Task<ProbeContext> CreateContextAsync(ModelProbeRun run, Captain captain, ModelEndpoint? endpoint)
        {
            EvaluationSettings settings = CurrentSettings();
            ProbeContext context = new ProbeContext(run, captain, endpoint, settings.ProbeTimeoutSeconds * 1000);

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
                exchange = await _Executor.RunAsync(request).ConfigureAwait(false) ?? ModelProbeExchange.NotStarted("The runtime returned nothing.");
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

        private class ProbeContext
        {
            public ModelProbeRun Run { get; }
            public Captain Captain { get; }
            public ModelEndpoint? Endpoint { get; }
            public int TimeoutMs { get; }
            public long Remaining { get; set; } = 0;
            public string BudgetName { get; set; } = "evaluation run token budget";
            public bool ProvenanceRead { get; set; } = false;

            public ProbeContext(ModelProbeRun run, Captain captain, ModelEndpoint? endpoint, int timeoutMs)
            {
                Run = run;
                Captain = captain;
                Endpoint = endpoint;
                TimeoutMs = timeoutMs;
            }
        }

        #endregion
    }
}
