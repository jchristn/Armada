namespace Armada.Gate
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Runs the 1.5.0 acceptance gate (RELIABILITY_SIMPLICITY_PLAN.md RS-1.5) against an isolated Admiral: creates the
    /// model endpoints, captains, and reference pipelines it needs (idempotently, by name), runs the battery in pipeline
    /// mode for each selected reference pipeline, and scores the measures it can compute from battery results and stage
    /// outcomes. Measures that come from other suites are reported as NotMeasured with where to find them.
    /// </summary>
    public sealed class GateRunner
    {
        #region Private-Members

        private const string NamePrefix = "gate-";
        private readonly ArmadaClient _Client;
        private readonly GatePresetFile _Preset;
        private readonly GateEndpointsFile _Endpoints;
        private readonly Action<string> _Log;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create a runner.
        /// </summary>
        /// <param name="client">Client for the gate Admiral.</param>
        /// <param name="preset">Reference pipelines and roles.</param>
        /// <param name="endpoints">Where the open models are served (local, never logged).</param>
        /// <param name="log">Progress sink.</param>
        /// <exception cref="ArgumentNullException">When an argument is null.</exception>
        public GateRunner(ArmadaClient client, GatePresetFile preset, GateEndpointsFile endpoints, Action<string> log)
        {
            _Client = client ?? throw new ArgumentNullException(nameof(client));
            _Preset = preset ?? throw new ArgumentNullException(nameof(preset));
            _Endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
            _Log = log ?? throw new ArgumentNullException(nameof(log));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create whatever the selected pipelines need and is missing: one model endpoint and captain per open-model role,
        /// one captain per frontier role, and each pipeline with its stages.
        /// </summary>
        /// <param name="pipelines">Pipelines to prepare.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Captain id per role name.</returns>
        /// <exception cref="InvalidOperationException">When a role or its model is not configured.</exception>
        public async Task<Dictionary<string, string>> SetupAsync(List<GatePipeline> pipelines, CancellationToken token = default)
        {
            if (pipelines == null) throw new ArgumentNullException(nameof(pipelines));
            List<string> roleNames = pipelines.SelectMany(p => p.Staffing.Values).Distinct(StringComparer.Ordinal).ToList();
            List<Captain> captains = (await _Client.ListCaptainsAsync(new ArmadaPageQuery { PageSize = 1000 }, token).ConfigureAwait(false))?.Objects ?? new List<Captain>();
            List<ModelEndpoint> endpoints = await _Client.ListModelEndpointsAsync(token).ConfigureAwait(false) ?? new List<ModelEndpoint>();
            Dictionary<string, string> captainByRole = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (string roleName in roleNames)
            {
                token.ThrowIfCancellationRequested();
                GateRole role = _Preset.Roles.FirstOrDefault(r => r.Name == roleName) ?? throw new InvalidOperationException("Role " + roleName + " is not defined in the preset file.");
                string captainName = NamePrefix + role.Name;
                Captain? existing = captains.FirstOrDefault(c => c.Name == captainName);
                if (existing != null)
                {
                    captainByRole[role.Name] = existing.Id;
                    continue;
                }

                Captain captain = new Captain(captainName) { Runtime = Enum.Parse<AgentRuntimeEnum>(role.Runtime, true) };
                if (!String.IsNullOrWhiteSpace(role.Tier)) captain.Tier = Enum.Parse<CaptainTierEnum>(role.Tier!, true);
                if (captain.Runtime == AgentRuntimeEnum.ApiEndpoint)
                {
                    ModelEndpoint endpoint = await EnsureEndpointAsync(role, endpoints, token).ConfigureAwait(false);
                    captain.ModelEndpointId = endpoint.Id;
                    captain.Model = endpoint.Model;
                }
                else
                {
                    captain.Model = role.Model;
                }

                Captain created = await _Client.CreateCaptainAsync(captain, token).ConfigureAwait(false) ?? throw new InvalidOperationException("Creating captain " + captainName + " returned nothing.");
                captainByRole[role.Name] = created.Id;
                _Log("created captain " + captainName + " (" + captain.Runtime + ", " + captain.Model + ")");
            }

            foreach (GatePipeline pipeline in pipelines)
            {
                token.ThrowIfCancellationRequested();
                Pipeline? found = null;
                try { found = await _Client.GetPipelineAsync(pipeline.Name, token).ConfigureAwait(false); }
                catch (ArmadaApiException) { found = null; }
                if (found != null) continue;
                Pipeline created = new Pipeline(pipeline.Name)
                {
                    Description = pipeline.Description,
                    Stages = pipeline.Stages.Select((persona, index) => new PipelineStage(index + 1, persona)).ToList()
                };
                await _Client.CreatePipelineAsync(created, token).ConfigureAwait(false);
                _Log("created pipeline " + pipeline.Name + " (" + String.Join(" -> ", pipeline.Stages) + ")");
            }

            return captainByRole;
        }

        /// <summary>
        /// Run each pipeline's battery run in turn and score the gate.
        /// </summary>
        /// <param name="pipelines">Pipelines to run.</param>
        /// <param name="captainByRole">Captain id per role, from <see cref="SetupAsync"/>.</param>
        /// <param name="packs">Packs to run.</param>
        /// <param name="runsOverride">Runs per pack for every pipeline, or null to use each pipeline's own.</param>
        /// <param name="poll">How often to poll a run.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The report.</returns>
        public async Task<GateReport> RunAsync(List<GatePipeline> pipelines, Dictionary<string, string> captainByRole, List<string> packs, int? runsOverride, TimeSpan poll, CancellationToken token = default)
        {
            if (pipelines == null) throw new ArgumentNullException(nameof(pipelines));
            if (captainByRole == null) throw new ArgumentNullException(nameof(captainByRole));
            if (packs == null || packs.Count == 0) throw new ArgumentException("Name at least one pack.", nameof(packs));

            GateReport report = new GateReport { StartedUtc = DateTime.UtcNow, Packs = packs };
            try { report.ArmadaVersion = (await _Client.GetHealthAsync(token).ConfigureAwait(false))?.Version; }
            catch (ArmadaApiException) { report.ArmadaVersion = null; }

            foreach (GatePipeline pipeline in pipelines)
            {
                token.ThrowIfCancellationRequested();
                List<CaptainAssignmentOverride> staffing = pipeline.Staffing
                    .Select(pair => new CaptainAssignmentOverride(pair.Key, captainByRole[pair.Value], null) { StrictCaptain = true })
                    .ToList();
                string workerCaptain = captainByRole[pipeline.Staffing.TryGetValue("Worker", out string? workerRole) ? workerRole : pipeline.Staffing.Values.First()];
                BatteryRunRequest request = new BatteryRunRequest
                {
                    Packs = packs,
                    CaptainId = workerCaptain,
                    RunCount = Math.Clamp(runsOverride ?? pipeline.RunsPerPack, 1, 10),
                    CreateFixtureVessel = true,
                    Pipeline = pipeline.Name,
                    CaptainOverrides = staffing
                };

                _Log("running " + pipeline.Name + " on " + String.Join(",", packs) + " x" + request.RunCount);
                BatteryRun? run = await _Client.RunBatteryAsync(request, token).ConfigureAwait(false);
                if (run == null) throw new InvalidOperationException("The battery run for " + pipeline.Name + " was not created.");
                try
                {
                    while (run.Status == BatteryRunStatusEnum.Queued || run.Status == BatteryRunStatusEnum.Running)
                    {
                        await Task.Delay(poll, token).ConfigureAwait(false);
                        run = await _Client.GetBatteryRunAsync(run.Id, token).ConfigureAwait(false) ?? run;
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    // Stopping the gate client stops its run on the Admiral too, so no mission keeps executing.
                    _Log("stopping " + run.Id + " on the Admiral");
                    try { await _Client.CancelBatteryRunAsync(run.Id, "The gate client was stopped.", CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception ex) { _Log("could not cancel " + run.Id + ": " + ex.Message); }
                    throw;
                }

                report.Pipelines.Add(new GatePipelineResult
                {
                    Pipeline = pipeline.Name,
                    RunId = run.Id,
                    Status = run.Status.ToString(),
                    Passed = run.PassedCount,
                    Failed = run.FailedCount,
                    CouldNotRun = run.CouldNotRunCount,
                    PendingEvaluation = run.PendingEvaluationCount
                });
                _Log(pipeline.Name + ": " + run.Status + ", passed " + run.PassedCount + ", failed " + run.FailedCount + ", could not run " + run.CouldNotRunCount);
            }

            List<StageOutcome> outcomes = await ReadOutcomesAsync(report.StartedUtc, token).ConfigureAwait(false);
            report.FinishedUtc = DateTime.UtcNow;
            report.Measures = Score(report, outcomes);
            report.Passed = report.Measures.All(m => m.Status != GateMeasureStatusEnum.Fail) && report.Measures.Any(m => m.Status == GateMeasureStatusEnum.Pass);
            return report;
        }

        /// <summary>
        /// Score the measures this runner can compute. Public for tests.
        /// </summary>
        /// <param name="report">Report with pipeline results.</param>
        /// <param name="outcomes">Stage outcomes recorded during the gate.</param>
        /// <returns>The measures, G1 to G12 in order.</returns>
        public static List<GateMeasure> Score(GateReport report, List<StageOutcome> outcomes)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (outcomes == null) throw new ArgumentNullException(nameof(outcomes));
            List<GateMeasure> measures = new List<GateMeasure>();

            int passed = report.Pipelines.Sum(p => p.Passed);
            int graded = passed + report.Pipelines.Sum(p => p.Failed);
            double g1 = graded == 0 ? 0 : (double)passed / graded;
            measures.Add(new GateMeasure
            {
                Id = "G1", Name = "Voyages reaching a correct terminal state without intervention", Threshold = ">= 99%",
                Value = graded == 0 ? "" : g1.ToString("P1", CultureInfo.InvariantCulture),
                Status = graded == 0 ? GateMeasureStatusEnum.NotMeasured : (g1 >= 0.99 ? GateMeasureStatusEnum.Pass : GateMeasureStatusEnum.Fail),
                Detail = passed + " of " + graded + " graded tasks passed (" + String.Join("; ", report.Pipelines.Select(p => p.Pipeline + " " + p.Passed + "/" + (p.Passed + p.Failed))) + ")"
            });

            int interrupted = outcomes.Count(o => o.Disposition == StageDispositionEnum.Interrupted || o.Disposition == StageDispositionEnum.Cancelled);
            measures.Add(new GateMeasure
            {
                Id = "G2", Name = "Missions stuck without an actionable blocker", Threshold = "0",
                Value = interrupted.ToString(CultureInfo.InvariantCulture),
                Status = interrupted == 0 ? GateMeasureStatusEnum.Pass : GateMeasureStatusEnum.Fail,
                Detail = "stage attempts that ended Interrupted or Cancelled (a timed-out battery task is cancelled); unattended runs cancel nothing else"
            });

            measures.Add(NotMeasured("G3", "Duplicate work", "0", "Services.CrashConsistency and the rescue dedup cases; per-attempt duplicate detection lands with RS-2.6 and RS-3.5"));

            int fullReruns = outcomes.Count(o => o.Disposition == StageDispositionEnum.OutputRepair);
            int cheapRepairs = outcomes.Count(o => o.ContractRepairKind.HasValue && o.ContractRepairKind.Value != ContractRepairKindEnum.None);
            measures.Add(new GateMeasure
            {
                Id = "G4", Name = "Format-only failures that caused a full stage rerun", Threshold = "0",
                Value = fullReruns.ToString(CultureInfo.InvariantCulture),
                Status = fullReruns == 0 ? GateMeasureStatusEnum.Pass : GateMeasureStatusEnum.Fail,
                Detail = "stage attempts with disposition OutputRepair"
            });

            int formatFailures = fullReruns + cheapRepairs;
            double g5 = formatFailures == 0 ? 1 : (double)cheapRepairs / formatFailures;
            measures.Add(new GateMeasure
            {
                Id = "G5", Name = "Format-only failures repaired without a full rerun", Threshold = ">= 90%",
                Value = formatFailures == 0 ? "no format failures" : g5.ToString("P1", CultureInfo.InvariantCulture),
                Status = g5 >= 0.90 ? GateMeasureStatusEnum.Pass : GateMeasureStatusEnum.Fail,
                Detail = cheapRepairs + " repaired in place, " + fullReruns + " full reruns"
            });

            List<StageOutcome> runtimeFailures = outcomes.Where(o => o.RuntimeFailureKind.HasValue && o.RuntimeFailureKind.Value != RuntimeFailureKindEnum.Clean).ToList();
            List<StageOutcome> unclassifiedOutcomes = runtimeFailures.Where(o => o.RuntimeFailureKind == RuntimeFailureKindEnum.Unclassified).ToList();
            int unclassified = unclassifiedOutcomes.Count;
            string unclassifiedByModel = String.Join(", ", unclassifiedOutcomes
                .GroupBy(o => (o.Runtime.HasValue ? o.Runtime.Value.ToString() : "unknown runtime") + " / " + (String.IsNullOrEmpty(o.Model) ? "unknown model" : o.Model))
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => g.Key + ": " + g.Count()));
            double g6 = runtimeFailures.Count == 0 ? 0 : (double)unclassified / runtimeFailures.Count;
            measures.Add(new GateMeasure
            {
                Id = "G6", Name = "Runtime failures left unclassified", Threshold = "< 2%",
                Value = runtimeFailures.Count == 0 ? "no runtime failures" : g6.ToString("P1", CultureInfo.InvariantCulture),
                Status = g6 < 0.02 ? GateMeasureStatusEnum.Pass : GateMeasureStatusEnum.Fail,
                Detail = unclassified + " of " + runtimeFailures.Count + " runtime failures Unclassified" + (unclassified == 0 ? "" : " (" + unclassifiedByModel + ")")
            });

            measures.Add(NotMeasured("G7", "Judge PASS on a seeded-defect task", "0", "single-persona Judge runs of each pipeline's Judge model on the judge-negative tasks; pipeline runs skip Judge tasks"));
            measures.Add(NotMeasured("G8", "Open model as final Judge of its own tier's work", "0", "needs the judge strength rule (RS-8.7) and its outcome record"));
            measures.Add(NotMeasured("G9", "Crash consistency", "50 of 50", "Services.CrashConsistency with ARMADA_TEST_PENDING_CRASH_CASES unset once RS-2.3, RS-2.5, RS-2.6, RS-4.3 land, plus the crash-point matrix in RS-14.4"));
            measures.Add(NotMeasured("G10", "Median Pending-to-started with free capacity", "< 2 s", "armada_missions_queue_seconds from the gate Admiral's metrics endpoint (--telemetry)"));
            measures.Add(NotMeasured("G11", "Database queries per dispatch pass with 50 pending missions", ">= 5x fewer than 1.4.0", "Armada.Benchmark admission runner against benchmarks/baselines/1.4.0"));
            measures.Add(NotMeasured("G12", "Escalations to a stronger rung", "100%", "needs escalation records from RS-3.1 and RS-5.6"));
            return measures;
        }

        #endregion

        #region Private-Methods

        private async Task<ModelEndpoint> EnsureEndpointAsync(GateRole role, List<ModelEndpoint> endpoints, CancellationToken token)
        {
            string name = NamePrefix + role.Name;
            ModelEndpoint? existing = endpoints.FirstOrDefault(e => e.Name == name);
            if (existing != null) return existing;

            ModelEndpoint endpoint = new ModelEndpoint { Name = name, Kind = ModelEndpointKindEnum.Inference };
            string? apiKey;
            if (String.Equals(role.Source, "conductor", StringComparison.OrdinalIgnoreCase))
            {
                GateConductor conductor = _Endpoints.Conductor ?? throw new InvalidOperationException("The local endpoint file has no conductor section.");
                if (role.ModelKey == null || !conductor.Models.TryGetValue(role.ModelKey, out GateConductorModel? model))
                    throw new InvalidOperationException("The local endpoint file has no Conductor model " + role.ModelKey + ".");
                endpoint.Provider = ModelProviderEnum.Ollama;
                endpoint.BaseUrl = conductor.BaseUrl.TrimEnd('/') + "/" + model.Vmr.Trim('/') + "/";
                endpoint.Model = model.Model;
                apiKey = conductor.BearerToken;
            }
            else if (String.Equals(role.Source, "ollamaCloud", StringComparison.OrdinalIgnoreCase))
            {
                GateOllamaCloud cloud = _Endpoints.OllamaCloud ?? throw new InvalidOperationException("The local endpoint file has no ollamaCloud section.");
                if (role.ModelKey == null || !cloud.Models.TryGetValue(role.ModelKey, out string? model))
                    throw new InvalidOperationException("The local endpoint file has no Ollama Cloud model " + role.ModelKey + ".");
                endpoint.Provider = ModelProviderEnum.OllamaCloud;
                endpoint.BaseUrl = cloud.BaseUrl;
                endpoint.Model = model;
                apiKey = cloud.ApiKey;
            }
            else
            {
                throw new InvalidOperationException("Role " + role.Name + " has an unknown source " + role.Source + ".");
            }

            ModelEndpoint created = await _Client.CreateModelEndpointAsync(endpoint, apiKey, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Creating endpoint " + name + " returned nothing.");
            _Log("created model endpoint " + name + " (" + endpoint.Provider + ", " + endpoint.Model + ")");
            return created;
        }

        private async Task<List<StageOutcome>> ReadOutcomesAsync(DateTime fromUtc, CancellationToken token)
        {
            List<StageOutcome> all = new List<StageOutcome>();
            for (int page = 1; page <= 100; page++)
            {
                EnumerationResult<StageOutcome>? result = await _Client.EnumerateStageOutcomesAsync(new StageOutcomeQuery
                {
                    IsEvaluation = true,
                    FromUtc = fromUtc,
                    PageNumber = page,
                    PageSize = 1000
                }, token).ConfigureAwait(false);
                if (result == null || result.Objects.Count == 0) break;
                all.AddRange(result.Objects);
                if (result.Objects.Count < 1000) break;
            }

            return all;
        }

        private static GateMeasure NotMeasured(string id, string name, string threshold, string where)
        {
            return new GateMeasure { Id = id, Name = name, Threshold = threshold, Status = GateMeasureStatusEnum.NotMeasured, Detail = "Measured elsewhere: " + where };
        }

        #endregion
    }
}
