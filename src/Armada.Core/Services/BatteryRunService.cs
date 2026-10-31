namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;

    /// <summary>
    /// Runs the fixture battery against a captain (docs/MODEL_HETEROGENEITY.md, Part 2): for each pack, run and task it
    /// resets the fixture, dispatches the task through real dispatch, Harbor execution, stage checks and completion
    /// parsing (via <see cref="IBatteryTaskDispatcher"/>), and grades it per the manifest. Runs share the evaluation
    /// concurrency gate (overall and per inference host) with Evaluate, record their seed, sampling settings, run count,
    /// battery version and full configuration revision, and are evaluation work throughout.
    /// </summary>
    public class BatteryRunService
    {
        #region Public-Members

        /// <summary>
        /// Default runs of each task.
        /// </summary>
        public const int DefaultRunCount = 3;

        /// <summary>
        /// Most runs of each task.
        /// </summary>
        public const int MaxRunCount = 10;

        /// <summary>
        /// Longest captain output kept per task attempt.
        /// </summary>
        public const int MaxOutputChars = 65536;

        #endregion

        #region Private-Members

        private readonly string _Header = "[BatteryRunService] ";
        private readonly DatabaseDriver _Database;
        private readonly LoggingModule _Logging;
        private readonly BatteryPackCatalog _Catalog;
        private readonly BatteryFixtureService _Fixtures;
        private readonly IBatteryTaskDispatcher _Dispatcher;
        private readonly Func<EvaluationSettings> _Settings;
        private readonly EvaluationConcurrencyGate _Gate;
        private readonly string _ArmadaVersion;
        private readonly ConcurrentDictionary<string, Task<BatteryRun>> _Running = new ConcurrentDictionary<string, Task<BatteryRun>>(StringComparer.Ordinal);

        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            Converters = { new JsonStringEnumConverter() }
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="catalog">Battery packs.</param>
        /// <param name="fixtures">Fixture repositories and vessels.</param>
        /// <param name="dispatcher">Runs one task through dispatch.</param>
        /// <param name="settings">Reads the evaluation settings (applied live).</param>
        /// <param name="gate">Evaluation concurrency gate shared with Evaluate, or null for a private one.</param>
        /// <param name="armadaVersion">Armada version recorded in the configuration revision.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public BatteryRunService(
            DatabaseDriver database,
            LoggingModule logging,
            BatteryPackCatalog catalog,
            BatteryFixtureService fixtures,
            IBatteryTaskDispatcher dispatcher,
            Func<EvaluationSettings> settings,
            EvaluationConcurrencyGate? gate = null,
            string? armadaVersion = null)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _Fixtures = fixtures ?? throw new ArgumentNullException(nameof(fixtures));
            _Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Gate = gate ?? new EvaluationConcurrencyGate(() => CurrentSettings().MaxConcurrentRuns, () => CurrentSettings().MaxConcurrentRunsPerHost);
            _ArmadaVersion = String.IsNullOrWhiteSpace(armadaVersion) ? Constants.ProductVersion : armadaVersion!;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// One page of packs, valid or not, ordered by pack directory name. Only the page's manifests are read.
        /// </summary>
        /// <param name="pageNumber">Page number (1-based).</param>
        /// <param name="pageSize">Page size (1 to 1000).</param>
        /// <returns>One page of packs.</returns>
        public EnumerationResult<BatteryPack> ListPacks(int pageNumber, int pageSize)
        {
            return _Catalog.LoadPage(pageNumber, pageSize);
        }

        /// <summary>
        /// Every pack, valid or not, ordered by pack directory name.
        /// </summary>
        /// <returns>Packs.</returns>
        public List<BatteryPack> LoadPacks()
        {
            return _Catalog.LoadAll();
        }

        /// <summary>
        /// The evaluator rubric of a task (the text of its rubric file), or null when the task has none or it cannot be read.
        /// </summary>
        /// <param name="packId">Pack.</param>
        /// <param name="taskId">Task.</param>
        /// <returns>Rubric text, or null.</returns>
        public string? RubricFor(string? packId, string? taskId)
        {
            if (String.IsNullOrWhiteSpace(packId) || String.IsNullOrWhiteSpace(taskId)) return null;
            BatteryPack? pack = _Catalog.Find(packId);
            BatteryTaskDefinition? task = pack?.Manifest?.Tasks.FirstOrDefault(t => String.Equals(t.Id, taskId, StringComparison.Ordinal));
            string? rubric = task?.Grader?.Rubric;
            if (pack == null || String.IsNullOrWhiteSpace(rubric)) return null;
            try
            {
                return File.ReadAllText(pack.Resolve(rubric!));
            }
            catch (Exception ex)
            {
                _Logging.Debug(_Header + "could not read the rubric of " + packId + "/" + taskId + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Start a battery run. Validates the packs and resolves (or creates, when asked) each fixture vessel before
        /// returning the queued run; the tasks run in the background.
        /// </summary>
        /// <param name="captain">Captain to run the tasks.</param>
        /// <param name="endpoint">The captain's model endpoint, or null.</param>
        /// <param name="packIds">Packs to run.</param>
        /// <param name="runCount">Runs of each task, or null for 3.</param>
        /// <param name="seed">Seed to record, or null to pick one.</param>
        /// <param name="createFixtureVessels">Whether to create missing fixture vessels.</param>
        /// <param name="tenantId">Tenant the run and fixture vessels belong to.</param>
        /// <param name="userId">Caller.</param>
        /// <param name="token">Cancellation token (for starting the run, not for the background work).</param>
        /// <returns>The queued run.</returns>
        /// <exception cref="ArgumentNullException">Thrown when captain or packIds is null.</exception>
        /// <exception cref="ArgumentException">Thrown for no packs, an unknown or malformed pack, a run count outside 1 to 10,
        /// or a missing fixture vessel that was not asked to be created.</exception>
        public Task<BatteryRun> StartAsync(
            Captain captain,
            ModelEndpoint? endpoint,
            List<string> packIds,
            int? runCount,
            int? seed,
            bool createFixtureVessels,
            string? tenantId,
            string? userId,
            CancellationToken token = default)
        {
            return StartAsync(captain, endpoint, packIds, runCount, seed, createFixtureVessels, tenantId, userId, null, token);
        }

        /// <summary>
        /// Start a battery run whose tasks render the given persona prompt variant. Validates the packs and resolves (or
        /// creates, when asked) each fixture vessel before returning the queued run; the tasks run in the background.
        /// A variant other than Current is recorded in the run's configuration (and so its configuration revision).
        /// </summary>
        /// <param name="captain">Captain to run the tasks.</param>
        /// <param name="endpoint">The captain's model endpoint, or null.</param>
        /// <param name="packIds">Packs to run.</param>
        /// <param name="runCount">Runs of each task, or null for 3.</param>
        /// <param name="seed">Seed to record, or null to pick one.</param>
        /// <param name="createFixtureVessels">Whether to create missing fixture vessels.</param>
        /// <param name="tenantId">Tenant the run and fixture vessels belong to.</param>
        /// <param name="userId">Caller.</param>
        /// <param name="promptVariant">Prompt variant for every task, or null for the captain's own setting.</param>
        /// <param name="token">Cancellation token (for starting the run, not for the background work).</param>
        /// <returns>The queued run.</returns>
        /// <exception cref="ArgumentNullException">Thrown when captain or packIds is null.</exception>
        /// <exception cref="ArgumentException">Thrown for no packs, an unknown or malformed pack, a run count outside 1 to 10,
        /// an undefined prompt variant, or a missing fixture vessel that was not asked to be created.</exception>
        public Task<BatteryRun> StartAsync(
            Captain captain,
            ModelEndpoint? endpoint,
            List<string> packIds,
            int? runCount,
            int? seed,
            bool createFixtureVessels,
            string? tenantId,
            string? userId,
            PromptVariantEnum? promptVariant,
            CancellationToken token = default)
        {
            return StartAsync(captain, endpoint, packIds, runCount, seed, createFixtureVessels, tenantId, userId, promptVariant, null, token);
        }

        /// <summary>
        /// Start a battery run for a Full Evaluate: as the other overloads, plus a persona filter, a token budget, a
        /// cancellation token and the shared evaluation slot (see <see cref="BatteryRunOptions"/>). A run stopped by the
        /// cancellation token or the budget ends Cancelled with what finished kept, and its open mission is cancelled.
        /// </summary>
        /// <param name="captain">Captain to run the tasks.</param>
        /// <param name="endpoint">The captain's model endpoint, or null.</param>
        /// <param name="packIds">Packs to run.</param>
        /// <param name="runCount">Runs of each task, or null for 3.</param>
        /// <param name="seed">Seed to record, or null to pick one.</param>
        /// <param name="createFixtureVessels">Whether to create missing fixture vessels.</param>
        /// <param name="tenantId">Tenant the run and fixture vessels belong to.</param>
        /// <param name="userId">Caller.</param>
        /// <param name="promptVariant">Prompt variant for every task, or null for the captain's own setting.</param>
        /// <param name="options">Full Evaluate options, or null.</param>
        /// <param name="token">Cancellation token (for starting the run, not for the background work).</param>
        /// <returns>The queued run.</returns>
        /// <exception cref="ArgumentNullException">Thrown when captain or packIds is null.</exception>
        /// <exception cref="ArgumentException">Thrown for no packs, an unknown or malformed pack, a run count outside 1 to 10,
        /// an undefined prompt variant, a persona filter no task of the packs matches, or a missing fixture vessel that was
        /// not asked to be created.</exception>
        public async Task<BatteryRun> StartAsync(
            Captain captain,
            ModelEndpoint? endpoint,
            List<string> packIds,
            int? runCount,
            int? seed,
            bool createFixtureVessels,
            string? tenantId,
            string? userId,
            PromptVariantEnum? promptVariant,
            BatteryRunOptions? options,
            CancellationToken token = default)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (packIds == null) throw new ArgumentNullException(nameof(packIds));
            CollectionLimits.RequireConfigItems(packIds, "Packs");
            List<string> ids = packIds.Where(p => !String.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) throw new ArgumentException("Name at least one pack.", nameof(packIds));
            int count = runCount ?? DefaultRunCount;
            if (count < 1 || count > MaxRunCount) throw new ArgumentException("runCount must be 1 to " + MaxRunCount + ".", nameof(runCount));
            if (promptVariant.HasValue && !Enum.IsDefined(promptVariant.Value))
                throw new ArgumentException("promptVariant must be Current, Lean or Compact.", nameof(promptVariant));

            List<BatteryPack> all = _Catalog.LoadAll();
            List<BatteryPack> packs = new List<BatteryPack>();
            foreach (string id in ids)
            {
                BatteryPack? pack = all.FirstOrDefault(p => String.Equals(p.Id, id, StringComparison.Ordinal));
                if (pack == null) throw new ArgumentException("Unknown battery pack '" + id + "'.", nameof(packIds));
                if (!pack.IsValid) throw new ArgumentException("Battery pack '" + id + "' is malformed: " + String.Join(" ", pack.Errors), nameof(packIds));
                packs.Add(pack);
            }

            int tasksPerRun = packs.Sum(p => p.Manifest!.Tasks.Count(t => Selected(t, options)));
            if (tasksPerRun == 0)
                throw new ArgumentException("No task of the packs tests the selected personas (" + String.Join(", ", options?.Personas ?? new List<string>()) + ").", nameof(options));

            string runTenant = captain.TenantId ?? tenantId ?? Constants.DefaultTenantId;
            Dictionary<string, Vessel> vessels = new Dictionary<string, Vessel>(StringComparer.Ordinal);
            foreach (BatteryPack pack in packs)
                vessels[pack.Id] = await _Fixtures.EnsureVesselAsync(pack, runTenant, userId, createFixtureVessels, token).ConfigureAwait(false);

            BatteryRun run = new BatteryRun
            {
                TenantId = runTenant,
                UserId = userId,
                CaptainId = captain.Id,
                CaptainName = captain.Name,
                Packs = packs.Select(p => p.Id).ToList(),
                BatteryVersion = String.Join(",", packs.Select(p => p.Id + "@" + p.Manifest!.Version)),
                RunCount = count,
                Status = BatteryRunStatusEnum.Queued,
                IsEvaluation = true,
                Pipeline = BatteryFixtureService.PipelineName,
                Seed = seed ?? RandomNumberGenerator.GetInt32(1, Int32.MaxValue),
                Runtime = captain.Runtime.ToString(),
                Model = !String.IsNullOrWhiteSpace(captain.Model) ? captain.Model : endpoint?.Model,
                ModelEndpointId = endpoint?.Id,
                InferenceHost = ModelProbeService.InferenceHostFor(captain, endpoint),
                TotalTasks = tasksPerRun * count
            };
            run.SamplingJson = JsonSerializer.Serialize(BuildSampling(captain, endpoint), _Json);
            run.ConfigurationJson = JsonSerializer.Serialize(BuildConfiguration(run, captain, endpoint, packs, promptVariant), _Json);
            run.ConfigurationRevision = Revision(run.ConfigurationJson);

            run = await _Database.BatteryRuns.CreateAsync(run, token).ConfigureAwait(false);
            _Logging.Info(_Header + "queued " + run.Id + " (" + run.BatteryVersion + " x" + count + ") for captain " + captain.Id);

            BatteryRun returned = await _Database.BatteryRuns.ReadAsync(run.Id, token).ConfigureAwait(false) ?? run;
            BatteryRun created = run;
            Task<BatteryRun> execution = Task.Run(() => ExecuteAsync(created, captain, packs, vessels, promptVariant, options ?? new BatteryRunOptions()));
            _Running[run.Id] = execution;
            _ = execution.ContinueWith(t => _Running.TryRemove(created.Id, out Task<BatteryRun>? _), TaskScheduler.Default);
            return returned;
        }

        /// <summary>
        /// Wait for a run started by this service, then return it with its results.
        /// </summary>
        /// <param name="runId">Run identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The run, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when runId is null or empty.</exception>
        public async Task<BatteryRun?> WaitAsync(string runId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(runId)) throw new ArgumentNullException(nameof(runId));
            if (_Running.TryGetValue(runId, out Task<BatteryRun>? running))
                await running.WaitAsync(token).ConfigureAwait(false);
            return await ReadAsync(runId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read a run with its results.
        /// </summary>
        /// <param name="runId">Run identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The run, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when runId is null or empty.</exception>
        public async Task<BatteryRun?> ReadAsync(string runId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(runId)) throw new ArgumentNullException(nameof(runId));
            BatteryRun? run = await _Database.BatteryRuns.ReadAsync(runId, token).ConfigureAwait(false);
            if (run == null) return null;
            run.Results = await _Database.BatteryTaskResults.ReadByRunAsync(runId, token).ConfigureAwait(false);
            return run;
        }

        /// <summary>
        /// Read a run without its results (the REST and MCP run read).
        /// </summary>
        /// <param name="runId">Run identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The run, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when runId is null or empty.</exception>
        public async Task<BatteryRun?> ReadRunAsync(string runId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(runId)) throw new ArgumentNullException(nameof(runId));
            return await _Database.BatteryRuns.ReadAsync(runId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// One page of a run's results, paged in the database. query.Status filters by outcome (Passed, Failed,
        /// CouldNotRun, PendingEvaluation); query.Order orders by creation with the id as the tie-break.
        /// </summary>
        /// <param name="runId">Run identifier.</param>
        /// <param name="query">Paging query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of results.</returns>
        /// <exception cref="ArgumentNullException">Thrown when runId or query is null.</exception>
        /// <exception cref="ArgumentException">Thrown for an unknown outcome filter.</exception>
        public async Task<EnumerationResult<BatteryTaskResult>> EnumerateResultsAsync(string runId, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(runId)) throw new ArgumentNullException(nameof(runId));
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (!String.IsNullOrWhiteSpace(query.Status))
            {
                if (!Enum.TryParse(query.Status.Trim(), true, out BatteryTaskOutcomeEnum outcome) || !Enum.IsDefined(outcome))
                    throw new ArgumentException("Unknown outcome: " + query.Status + ". Use Passed, Failed, CouldNotRun or PendingEvaluation.", nameof(query));
                query.Status = outcome.ToString();
            }

            return await _Database.BatteryTaskResults.EnumerateByRunAsync(runId, query, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Mark runs left Queued or Running by an earlier Admiral process as Failed (call once at startup).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of runs marked.</returns>
        public async Task<int> RecoverInterruptedAsync(CancellationToken token = default)
        {
            int marked = 0;
            foreach (BatteryRunStatusEnum status in new[] { BatteryRunStatusEnum.Queued, BatteryRunStatusEnum.Running })
            {
                while (true)
                {
                    BatteryRunQuery query = new BatteryRunQuery { Status = status, PageNumber = 1, PageSize = 100 };
                    EnumerationResult<BatteryRun> page = await _Database.BatteryRuns.EnumerateAsync(query, token).ConfigureAwait(false);
                    List<BatteryRun> stale = page.Objects.FindAll(r => !_Running.ContainsKey(r.Id));
                    if (stale.Count == 0) break;
                    foreach (BatteryRun run in stale)
                    {
                        run.Status = BatteryRunStatusEnum.Failed;
                        run.FailureReason = "The Admiral restarted before the run finished.";
                        run.CompletedUtc = DateTime.UtcNow;
                        await _Database.BatteryRuns.UpdateAsync(run, token).ConfigureAwait(false);
                        marked++;
                    }
                }
            }

            return marked;
        }

        /// <summary>
        /// The mission description of a task: its prompt, then each attachment in a fenced block.
        /// </summary>
        /// <param name="pack">Pack.</param>
        /// <param name="task">Task.</param>
        /// <returns>Description.</returns>
        /// <exception cref="ArgumentNullException">Thrown when pack or task is null.</exception>
        public static string BuildDescription(BatteryPack pack, BatteryTaskDefinition task)
        {
            if (pack == null) throw new ArgumentNullException(nameof(pack));
            if (task == null) throw new ArgumentNullException(nameof(task));
            StringBuilder sb = new StringBuilder();
            sb.Append((task.Prompt ?? String.Empty).Trim());
            foreach (string attachment in task.Attachments)
            {
                string content = File.ReadAllText(pack.Resolve(attachment)).Replace("\r\n", "\n").TrimEnd();
                string fence = content.Contains("```", StringComparison.Ordinal) ? "~~~~" : "```";
                string language = attachment.EndsWith(".diff", StringComparison.OrdinalIgnoreCase) ? "diff" : String.Empty;
                sb.Append("\n\n").Append(Path.GetFileName(attachment)).Append(":\n").Append(fence).Append(language).Append('\n')
                    .Append(content).Append('\n').Append(fence);
            }

            return sb.ToString();
        }

        #endregion

        #region Private-Methods

        private EvaluationSettings CurrentSettings()
        {
            return _Settings() ?? new EvaluationSettings();
        }

        private async Task<BatteryRun> ExecuteAsync(BatteryRun run, Captain captain, List<BatteryPack> packs, Dictionary<string, Vessel> vessels, PromptVariantEnum? promptVariant, BatteryRunOptions options)
        {
            try
            {
                // A Full Evaluate already holds the slot for this inference host; waiting for a second one would deadlock it.
                using (options.HoldsEvaluationSlot ? null : await _Gate.AcquireAsync(run.InferenceHost, options.Cancellation).ConfigureAwait(false))
                {
                    run.Status = BatteryRunStatusEnum.Running;
                    run.StartedUtc = DateTime.UtcNow;
                    run = await _Database.BatteryRuns.UpdateAsync(run).ConfigureAwait(false);

                    HashSet<string> missingToolchain = new HashSet<string>(StringComparer.Ordinal);
                    string? missingReason = null;
                    foreach (BatteryPack pack in packs)
                    {
                        for (int attempt = 1; attempt <= run.RunCount && options.StoppedEarlyReason == null; attempt++)
                        {
                            foreach (BatteryTaskDefinition task in pack.Manifest!.Tasks)
                            {
                                if (!Selected(task, options)) continue;
                                options.StoppedEarlyReason = StopReasonFor(options);
                                if (options.StoppedEarlyReason != null) break;

                                BatteryTaskResult? result = await RunTaskAsync(run, captain, pack, vessels[pack.Id], task, attempt, missingToolchain, missingReason, promptVariant, options).ConfigureAwait(false);
                                if (result == null)
                                {
                                    options.StoppedEarlyReason = StopReasonFor(options) ?? "The run was cancelled.";
                                    break;
                                }

                                if (result.Outcome == BatteryTaskOutcomeEnum.CouldNotRun && missingReason == null && missingToolchain.Count > 0)
                                    missingReason = result.Reason;

                                await _Database.BatteryTaskResults.CreateAsync(result).ConfigureAwait(false);
                                Count(run, result.Outcome);
                                run = await _Database.BatteryRuns.UpdateAsync(run).ConfigureAwait(false);
                            }
                        }
                    }

                    if (options.StoppedEarlyReason != null)
                    {
                        run.Status = BatteryRunStatusEnum.Cancelled;
                        run.FailureReason = options.StoppedEarlyReason;
                        run.CompletedUtc = DateTime.UtcNow;
                        run = await _Database.BatteryRuns.UpdateAsync(run).ConfigureAwait(false);
                        _Logging.Info(_Header + run.Id + " stopped early: " + options.StoppedEarlyReason);
                        return run;
                    }

                    run.Status = BatteryRunStatusEnum.Completed;
                    run.CompletedUtc = DateTime.UtcNow;
                    run = await _Database.BatteryRuns.UpdateAsync(run).ConfigureAwait(false);
                    _Logging.Info(_Header + run.Id + " completed: " + run.PassedCount + " passed, " + run.FailedCount + " failed, "
                        + run.CouldNotRunCount + " could not run, " + run.PendingEvaluationCount + " pending evaluation");
                    return run;
                }
            }
            catch (Exception ex)
            {
                bool stopped = ex is OperationCanceledException && options.Cancellation.IsCancellationRequested;
                if (stopped) options.StoppedEarlyReason ??= StopReasonFor(options) ?? "The run was cancelled.";
                _Logging.Warn(_Header + run.Id + (stopped ? " stopped: " + options.StoppedEarlyReason : " failed: " + ex.Message));
                try
                {
                    run.Status = stopped ? BatteryRunStatusEnum.Cancelled : BatteryRunStatusEnum.Failed;
                    run.FailureReason = stopped ? options.StoppedEarlyReason : ex.Message;
                    run.CompletedUtc = DateTime.UtcNow;
                    run = await _Database.BatteryRuns.UpdateAsync(run).ConfigureAwait(false);
                }
                catch (Exception update)
                {
                    _Logging.Warn(_Header + "could not record the failure of " + run.Id + ": " + update.Message);
                }

                return run;
            }
        }

        private async Task<BatteryTaskResult?> RunTaskAsync(
            BatteryRun run,
            Captain captain,
            BatteryPack pack,
            Vessel vessel,
            BatteryTaskDefinition task,
            int attempt,
            HashSet<string> missingToolchain,
            string? missingReason,
            PromptVariantEnum? promptVariant,
            BatteryRunOptions options)
        {
            Stopwatch watch = Stopwatch.StartNew();
            BatteryTaskResult result = new BatteryTaskResult
            {
                RunId = run.Id,
                TenantId = run.TenantId,
                PackId = pack.Id,
                PackVersion = pack.Manifest!.Version,
                TaskId = task.Id ?? String.Empty,
                TaskTitle = task.Title,
                Persona = task.Persona,
                Grader = task.Grader?.Type ?? BatteryGraderTypeEnum.Command,
                Attempt = attempt,
                VesselId = vessel.Id
            };

            List<string> needed = pack.ToolchainFor(task);
            List<string> known = needed.Where(n => missingToolchain.Contains(n)).ToList();
            if (known.Count > 0)
            {
                // An earlier task of this run found the tool missing on the captain's host: do not spend a mission on it.
                Apply(result, new BatteryGrade
                {
                    Outcome = BatteryTaskOutcomeEnum.CouldNotRun,
                    Reason = missingReason ?? ("The Harbor lacks the pack's toolchain: " + String.Join(", ", known) + "."),
                    Summary = "Could not run: toolchain missing (" + String.Join(", ", known) + ")."
                });
                return Finish(result, watch);
            }

            try
            {
                using (await _Fixtures.LockAsync(pack.Id).ConfigureAwait(false))
                {
                    result.FixtureCommit = await _Fixtures.ResetAsync(pack, task, run.Id + " attempt " + attempt).ConfigureAwait(false);
                    int timeoutMinutes = task.TimeoutMinutes ?? CurrentSettings().BatteryTaskTimeoutMinutes;
                    BatteryDispatchRequest request = new BatteryDispatchRequest(
                        vessel,
                        captain,
                        "[" + pack.Id + "/" + task.Id + " #" + attempt + "] " + task.Title,
                        BuildDescription(pack, task),
                        PersonaCatalog.NormalizeName(task.Persona),
                        timeoutMinutes * 60000)
                    {
                        PromptVariant = promptVariant
                    };
                    string description = request.Description;
                    BatteryDispatchResult dispatched = await _Dispatcher.RunAsync(request, options.Cancellation).ConfigureAwait(false);
                    options.TokensUsed += ModelProbeCatalog.EstimateTokens(description) + ModelProbeCatalog.EstimateTokens(dispatched.Mission?.AgentOutput);
                    if (dispatched.Cancelled || options.Cancellation.IsCancellationRequested) return null;
                    result.VoyageId = dispatched.VoyageId;
                    result.MissionId = dispatched.Mission?.Id;
                    result.MissionStatus = dispatched.Mission?.Status.ToString();
                    result.Output = Cap(dispatched.Mission?.AgentOutput);

                    BatteryGrade grade = await GradeAsync(pack, task, dispatched, timeoutMinutes, missingToolchain).ConfigureAwait(false);
                    Apply(result, grade);
                }
            }
            catch (OperationCanceledException) when (options.Cancellation.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + run.Id + " task " + pack.Id + "/" + task.Id + " #" + attempt + " could not run: " + ex.Message);
                Apply(result, new BatteryGrade
                {
                    Outcome = BatteryTaskOutcomeEnum.CouldNotRun,
                    Reason = "The battery could not run the task: " + ex.Message,
                    Summary = "Could not run."
                });
            }

            return Finish(result, watch);
        }

        private static async Task<BatteryGrade> GradeAsync(BatteryPack pack, BatteryTaskDefinition task, BatteryDispatchResult dispatched, int timeoutMinutes, HashSet<string> missingToolchain)
        {
            if (dispatched.Mission == null)
                return Failed(dispatched.FailureReason ?? "No mission was created.", "Dispatch failed.");

            // A worktree that is gone (reclaimed dock, unreachable Harbor) cannot be graded: that is not the model's fault.
            IBatteryWorkspace? workspace = dispatched.Workspace;
            if (workspace != null && !await workspace.ExistsAsync().ConfigureAwait(false))
            {
                return new BatteryGrade
                {
                    Outcome = BatteryTaskOutcomeEnum.CouldNotRun,
                    Reason = "The mission's worktree " + workspace.Root + " no longer exists on " + workspace.HostLabel + ", so it cannot be graded.",
                    Summary = "Could not run: worktree gone."
                };
            }

            // Toolchain first: a missing tool is could-not-run whatever the mission did.
            if (workspace != null && pack.ToolchainFor(task).Count > 0)
            {
                BatteryToolchainCheck check = await BatteryTaskGrader.CheckToolchainAsync(pack, task, workspace).ConfigureAwait(false);
                if (check.Missing.Count > 0)
                {
                    foreach (string name in check.Missing) missingToolchain.Add(name);
                    return new BatteryGrade
                    {
                        Outcome = BatteryTaskOutcomeEnum.CouldNotRun,
                        Reason = check.Reason,
                        Summary = "Could not run: toolchain missing (" + String.Join(", ", check.Missing) + ")."
                    };
                }
            }

            Mission mission = dispatched.Mission;
            if (dispatched.TimedOut)
                return Failed("The mission did not finish within " + timeoutMinutes + " minutes (status " + mission.Status + ").", "Timed out.");
            if (mission.Status == MissionStatusEnum.Failed || mission.Status == MissionStatusEnum.Cancelled || mission.Status == MissionStatusEnum.Blocked)
                return Failed("The mission ended " + mission.Status + (String.IsNullOrWhiteSpace(mission.FailureReason) ? "." : ": " + mission.FailureReason), "Mission " + mission.Status + ".");

            return await BatteryTaskGrader.GradeAsync(pack, task, mission.AgentOutput, workspace).ConfigureAwait(false);
        }

        private static bool Selected(BatteryTaskDefinition task, BatteryRunOptions? options)
        {
            if (options?.Personas == null || options.Personas.Count == 0) return true;
            return options.Personas.Any(p => PersonaCatalog.Matches(p, task.Persona));
        }

        private static string? StopReasonFor(BatteryRunOptions options)
        {
            if (options.Cancellation.IsCancellationRequested) return options.StopReason ?? "The run was cancelled.";
            if (options.TokenBudget > 0 && options.TokensUsed >= options.TokenBudget)
                return "The evaluation token budget (" + options.TokenBudget + ") is spent: about " + options.TokensUsed + " tokens used.";
            return null;
        }

        private static BatteryGrade Failed(string reason, string summary)
        {
            return new BatteryGrade { Outcome = BatteryTaskOutcomeEnum.Failed, Reason = reason, Summary = summary, Score = 0 };
        }

        private static void Apply(BatteryTaskResult result, BatteryGrade grade)
        {
            result.Outcome = grade.Outcome;
            result.Score = grade.Score;
            result.Summary = grade.Summary;
            result.Reason = grade.Reason;
            result.Details = grade.Details ?? new BatteryGradeDetails();
        }

        private static BatteryTaskResult Finish(BatteryTaskResult result, Stopwatch watch)
        {
            result.DurationMs = watch.ElapsedMilliseconds;
            result.CompletedUtc = DateTime.UtcNow;
            return result;
        }

        private static void Count(BatteryRun run, BatteryTaskOutcomeEnum outcome)
        {
            switch (outcome)
            {
                case BatteryTaskOutcomeEnum.Passed: run.PassedCount++; break;
                case BatteryTaskOutcomeEnum.Failed: run.FailedCount++; break;
                case BatteryTaskOutcomeEnum.CouldNotRun: run.CouldNotRunCount++; break;
                default: run.PendingEvaluationCount++; break;
            }
        }

        private static string? Cap(string? output)
        {
            if (output == null || output.Length <= MaxOutputChars) return output;
            return output.Substring(0, MaxOutputChars);
        }

        private static Dictionary<string, object?> BuildSampling(Captain captain, ModelEndpoint? endpoint)
        {
            InferenceOptions? options = endpoint?.InferenceOptions;
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["temperature"] = options?.Temperature,
                ["topP"] = options?.TopP,
                ["maxTokens"] = options?.MaxTokens,
                ["contextLength"] = options?.ContextLength,
                ["reasoningEffort"] = captain.ReasoningEffort?.ToString()
            };
        }

        private Dictionary<string, object?> BuildConfiguration(BatteryRun run, Captain captain, ModelEndpoint? endpoint, List<BatteryPack> packs, PromptVariantEnum? promptVariant)
        {
            Dictionary<string, object?> configuration = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["armadaVersion"] = _ArmadaVersion,
                ["pipeline"] = BatteryFixtureService.PipelineName,
                ["runCount"] = run.RunCount,
                ["seed"] = run.Seed,
                ["captain"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["id"] = captain.Id,
                    ["runtime"] = captain.Runtime.ToString(),
                    ["model"] = captain.Model,
                    ["reasoningEffort"] = captain.ReasoningEffort?.ToString(),
                    ["modelEndpointId"] = captain.ModelEndpointId
                },
                ["endpoint"] = endpoint == null ? null : new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["id"] = endpoint.Id,
                    ["provider"] = endpoint.Provider.ToString(),
                    ["model"] = endpoint.Model,
                    ["inferenceOptions"] = endpoint.InferenceOptions
                },
                ["packs"] = packs.Select(p => new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["id"] = p.Id,
                    ["version"] = p.Manifest!.Version,
                    ["manifestHash"] = p.ManifestHash
                }).ToList()
            };

            // The effective persona prompt variant, recorded only when it is not Current so a Current run keeps the
            // configuration revision it had before variants existed.
            PromptVariantEnum effectiveVariant = promptVariant ?? PromptVariantResolver.FromCaptain(captain) ?? PromptVariantEnum.Current;
            if (effectiveVariant != PromptVariantEnum.Current) configuration["promptVariant"] = effectiveVariant.ToString();
            return configuration;
        }

        private static string Revision(string json)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant().Substring(0, 16);
        }

        #endregion
    }
}
