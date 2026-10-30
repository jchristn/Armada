namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Records one <see cref="StageOutcome"/> per stage attempt (with its provenance, what was observed, and what Armada
    /// did next), classifies its suspected cause with the failure-cause rules, and lets a person correct the cause.
    /// See docs/MODEL_HETEROGENEITY.md, Part 4.
    /// </summary>
    public class StageOutcomeService
    {
        #region Public-Members

        /// <summary>
        /// Longest failure reason kept on an outcome.
        /// </summary>
        public const int MaxFailureReasonLength = 2000;

        #endregion

        #region Private-Members

        private string _Header = "[StageOutcomeService] ";
        private readonly DatabaseDriver _Database;
        private readonly LoggingModule _Logging;
        private readonly StageProvenanceProbe? _Provenance;
        private readonly Func<DockHostResolver?>? _DockHosts;
        private readonly HandoffArtifactService _HandoffArtifacts;
        private readonly ModelProfileService _ModelProfiles;
        private static readonly string _ArmadaVersion = ReadArmadaVersion();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="provenance">Reads the runtime version and model digest, or null to leave them null.</param>
        /// <param name="dockHosts">Resolves the hosts whose git compares an escalated attempt's commit with the finished
        /// work, or null to leave <see cref="StageOutcome.PriorWorkKept"/> null.</param>
        /// <exception cref="ArgumentNullException">Thrown when database or logging is null.</exception>
        public StageOutcomeService(DatabaseDriver database, LoggingModule logging, StageProvenanceProbe? provenance = null, Func<DockHostResolver?>? dockHosts = null)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Provenance = provenance;
            _DockHosts = dockHosts;
            _HandoffArtifacts = new HandoffArtifactService(database, logging);
            _ModelProfiles = new ModelProfileService(database, logging);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Store the built-in failure-cause rules that are not stored yet (matched by name among the rules for every
        /// tenant). An existing rule, edited or not, is left as it is.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of rules added.</returns>
        public async Task<int> EnsureBuiltInRulesAsync(CancellationToken token = default)
        {
            List<FailureCauseRule> existing = await _Database.FailureCauseRules.EnumerateAsync(null, token).ConfigureAwait(false);
            HashSet<string> names = new HashSet<string>(existing.Where(r => r.TenantId == null).Select(r => r.Name), StringComparer.OrdinalIgnoreCase);
            int added = 0;
            foreach (FailureCauseRule rule in FailureCauseRuleCatalog.Build())
            {
                if (names.Contains(rule.Name)) continue;
                await _Database.FailureCauseRules.CreateAsync(rule, token).ConfigureAwait(false);
                added++;
            }

            if (added > 0) _Logging.Info(_Header + "stored " + added + " built-in failure-cause rule(s)");
            return added;
        }

        /// <summary>
        /// Record the outcome of a stage attempt whose exit the caller handled. Reads the mission as it is now (after
        /// Armada acted on the exit) to decide the disposition, then classifies the cause, and stores the attempt's handoff
        /// artifact linked to the outcome (<see cref="HandoffArtifactService"/>). Never throws for a storage problem: the
        /// outcome is diagnostic and must not disturb the exit handling.
        /// </summary>
        /// <param name="capture">What the exit handler captured.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored outcome, or null when nothing was recorded.</returns>
        /// <exception cref="ArgumentNullException">Thrown when capture is null.</exception>
        public async Task<StageOutcome?> RecordAttemptAsync(StageAttemptCapture capture, CancellationToken token = default)
        {
            if (capture == null) throw new ArgumentNullException(nameof(capture));
            if (!capture.Claimed || String.IsNullOrEmpty(capture.MissionId) || capture.Captain == null) return null;

            try
            {
                Mission? mission = await _Database.Missions.ReadAsync(capture.MissionId!, token).ConfigureAwait(false);
                if (mission == null) return null;

                StageOutcome outcome = await BuildAsync(capture, mission, token).ConfigureAwait(false);
                List<FailureCauseRule> rules = await _Database.FailureCauseRules.EnumerateAsync(outcome.TenantId, token).ConfigureAwait(false);
                Apply(outcome, FailureCauseClassifier.Classify(outcome, rules));
                StageOutcome stored = await _Database.StageOutcomes.CreateAsync(outcome, token).ConfigureAwait(false);
                if (stored.Disposition == StageDispositionEnum.Completed)
                    await LinkEscalationsAsync(stored, mission, capture.DockId, token).ConfigureAwait(false);

                // The provenance names the model as served: find or create its profile and revision and point the captain
                // at it. Best effort; a failure is logged and never fails the mission.
                await _ModelProfiles.TryResolveAsync(ModelProfileService.ObservationFrom(stored), token).ConfigureAwait(false);

                // Every attempt also leaves its handoff artifact for the next stage, linked to this outcome.
                await _HandoffArtifacts.RecordAttemptAsync(mission, capture.StageCheck, capture.Captain, stored, token).ConfigureAwait(false);
                return stored;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Warn(_Header + "could not record the stage outcome for mission " + capture.MissionId + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Classify an outcome by hand, or hand it back to the rules. A manual cause is never overwritten by rules.
        /// </summary>
        /// <param name="outcomeId">Outcome identifier.</param>
        /// <param name="cause">Cause to set, or null to clear the manual cause and re-apply the rules.</param>
        /// <param name="note">Optional note (evidence), at most 2000 characters.</param>
        /// <param name="userId">User making the change, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated outcome, or null when it does not exist.</returns>
        /// <exception cref="ArgumentNullException">Thrown when outcomeId is null or empty.</exception>
        public async Task<StageOutcome?> ClassifyManuallyAsync(string outcomeId, FailureCauseEnum? cause, string? note, string? userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(outcomeId)) throw new ArgumentNullException(nameof(outcomeId));
            StageOutcome? outcome = await _Database.StageOutcomes.ReadAsync(outcomeId, token).ConfigureAwait(false);
            if (outcome == null) return null;

            if (cause.HasValue)
            {
                outcome.SuspectedCause = cause.Value;
                outcome.CauseSource = FailureCauseSourceEnum.Manual;
                outcome.CauseRuleId = null;
                outcome.CauseNote = Truncate(note, MaxFailureReasonLength);
                outcome.ClassifiedByUserId = userId;
                outcome.ClassifiedUtc = DateTime.UtcNow;
            }
            else
            {
                outcome.ClassifiedByUserId = null;
                List<FailureCauseRule> rules = await _Database.FailureCauseRules.EnumerateAsync(outcome.TenantId, token).ConfigureAwait(false);
                Apply(outcome, FailureCauseClassifier.Classify(outcome, rules));
            }

            return await _Database.StageOutcomes.UpdateAsync(outcome, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Re-apply the rules to the outcomes matching a filter, so a new or edited rule takes effect on past attempts.
        /// Outcomes classified by hand are left alone.
        /// </summary>
        /// <param name="filter">Which outcomes (paging is ignored; every matching outcome is processed).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>What was examined and changed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when filter is null.</exception>
        public async Task<StageOutcomeReclassifyResult> ReclassifyAsync(StageOutcomeQuery filter, CancellationToken token = default)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            StageOutcomeReclassifyResult result = new StageOutcomeReclassifyResult();
            Dictionary<string, List<FailureCauseRule>> rulesByTenant = new Dictionary<string, List<FailureCauseRule>>(StringComparer.Ordinal);

            // Read every match first: updating a cause while paging would move rows out of a filter on that cause and
            // make later pages skip them.
            List<StageOutcome> matches = new List<StageOutcome>();
            int page = 1;
            while (true)
            {
                StageOutcomeQuery query = CopyFilter(filter);
                query.PageNumber = page;
                query.PageSize = 500;
                EnumerationResult<StageOutcome> batch = await _Database.StageOutcomes.EnumerateAsync(query, token).ConfigureAwait(false);
                if (batch.Objects == null || batch.Objects.Count == 0) break;
                matches.AddRange(batch.Objects);
                if (batch.Objects.Count < query.PageSize) break;
                page++;
            }

            foreach (StageOutcome outcome in matches)
            {
                result.Examined++;
                if (outcome.CauseSource == FailureCauseSourceEnum.Manual)
                {
                    result.SkippedManual++;
                    continue;
                }

                string tenantKey = outcome.TenantId ?? String.Empty;
                if (!rulesByTenant.TryGetValue(tenantKey, out List<FailureCauseRule>? rules))
                {
                    rules = await _Database.FailureCauseRules.EnumerateAsync(outcome.TenantId, token).ConfigureAwait(false);
                    rulesByTenant[tenantKey] = rules;
                }

                FailureCauseClassification classification = FailureCauseClassifier.Classify(outcome, rules);
                if (classification.Cause == outcome.SuspectedCause && classification.RuleId == outcome.CauseRuleId) continue;
                Apply(outcome, classification);
                await _Database.StageOutcomes.UpdateAsync(outcome, token).ConfigureAwait(false);
                result.Changed++;
            }

            return result;
        }

        #endregion

        #region Private-Methods

        private async Task<StageOutcome> BuildAsync(StageAttemptCapture capture, Mission mission, CancellationToken token)
        {
            Captain captain = capture.Captain!;
            StageOutcome outcome = new StageOutcome
            {
                TenantId = mission.TenantId,
                UserId = mission.UserId,
                MissionId = mission.Id,
                VoyageId = mission.VoyageId,
                VesselId = mission.VesselId,
                CaptainId = captain.Id,
                CaptainName = captain.Name,
                Persona = String.IsNullOrWhiteSpace(mission.Persona) ? PersonaCatalog.Worker : mission.Persona,
                ReworkRound = mission.ReworkRound,
                Runtime = captain.Runtime,
                Model = captain.Model,
                ReasoningEffort = captain.ReasoningEffort?.ToString(),
                ArmadaVersion = _ArmadaVersion,
                ExitCode = capture.ExitCode,
                RuntimeFailureKind = capture.RuntimeFailureKind,
                StageCheckStatus = capture.StageCheck?.Status,
                StageCheckReason = capture.StageCheck?.Reason,
                StartedUtc = capture.StartedUtc,
                EndedUtc = capture.EndedUtc,
                IsEvaluation = mission.IsEvaluation
            };
            if (capture.StartedUtc.HasValue && capture.EndedUtc >= capture.StartedUtc.Value)
                outcome.DurationMs = (long)(capture.EndedUtc - capture.StartedUtc.Value).TotalMilliseconds;

            outcome.Attempt = await _Database.StageOutcomes.CountByMissionAsync(mission.Id, token).ConfigureAwait(false) + 1;

            if (!String.IsNullOrEmpty(capture.DockId))
            {
                Dock? dock = await _Database.Docks.ReadAsync(capture.DockId!, token).ConfigureAwait(false);
                outcome.HarborId = dock?.HarborId;
            }

            ModelEndpoint? endpointRead = null;
            if (!String.IsNullOrEmpty(captain.ModelEndpointId))
            {
                ModelEndpoint? endpoint = await _Database.ModelEndpoints.ReadAsync(captain.ModelEndpointId!, token).ConfigureAwait(false);
                endpointRead = endpoint;
                if (endpoint != null)
                {
                    outcome.ModelEndpointId = endpoint.Id;
                    outcome.ModelProvider = endpoint.Provider.ToString();
                    if (String.IsNullOrWhiteSpace(outcome.Model)) outcome.Model = endpoint.Model;
                    if (endpoint.InferenceOptions != null)
                    {
                        outcome.ContextTokens = endpoint.InferenceOptions.ContextLength;
                        outcome.Temperature = endpoint.InferenceOptions.Temperature;
                        outcome.TopP = endpoint.InferenceOptions.TopP;
                        outcome.ToolsEnabled = endpoint.InferenceOptions.ToolsEnabled;
                        outcome.McpToolsEnabled = endpoint.InferenceOptions.McpToolsEnabled;
                    }
                }
            }

            outcome.PromptVersion = await ComputePromptVersionAsync(token).ConfigureAwait(false);
            await ReadProvenanceAsync(captain, outcome, endpointRead, token).ConfigureAwait(false);

            DecideDisposition(capture, mission, outcome);
            outcome.ContractRepairKind = await ReadContractRepairKindAsync(mission, capture, token).ConfigureAwait(false);
            if (PersonaCatalog.Matches(outcome.Persona, PersonaCatalog.Judge))
            {
                outcome.JudgeVerdict = JudgeVerdictParser.Parse(mission.AgentOutput).ToString();
                outcome.ReviewedMissionId = mission.DependsOnMissionId;
            }

            return outcome;
        }

        /// <summary>
        /// The contract repair recorded for this attempt: the latest <c>mission.contract_repaired</c> event of the mission
        /// written since the attempt started. Null without a start time (the window cannot be bounded), without a repair,
        /// or when the event cannot be read; the outcome must still be recorded.
        /// </summary>
        private async Task<ContractRepairKindEnum?> ReadContractRepairKindAsync(Mission mission, StageAttemptCapture capture, CancellationToken token)
        {
            if (!capture.StartedUtc.HasValue) return null;
            try
            {
                EnumerationResult<ArmadaEvent> repairs = await _Database.Events.EnumerateAsync(new EnumerationQuery
                {
                    MissionId = mission.Id,
                    EventType = ContractRepair.EventType,
                    CreatedAfter = capture.StartedUtc.Value,
                    PageSize = 1,
                    Order = EnumerationOrderEnum.CreatedDescending
                }, token).ConfigureAwait(false);
                ArmadaEvent? latest = repairs.Objects.FirstOrDefault();
                if (latest == null || String.IsNullOrWhiteSpace(latest.Payload)) return null;
                ContractRepairRecord? record = JsonSerializer.Deserialize<ContractRepairRecord>(latest.Payload!);
                if (record == null || record.RepairKind == ContractRepairKindEnum.None) return null;
                return record.RepairKind;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Warn(_Header + "could not read the contract repair for mission " + mission.Id + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Decide what Armada did with the attempt from the mission before and after the exit was handled.
        /// </summary>
        internal static void DecideDisposition(StageAttemptCapture capture, Mission after, StageOutcome outcome)
        {
            outcome.FailureKind = after.FailureKind;
            outcome.FailureReason = Truncate(after.FailureReason, MaxFailureReasonLength);
            if (outcome.FailureReason == null && capture.StageCheck != null && capture.StageCheck.IsBlocking)
                outcome.FailureReason = Truncate(capture.StageCheck.FailureMessage, MaxFailureReasonLength);

            string? captainId = capture.Captain?.Id;
            outcome.CommitHash = null;
            if (after.Status == MissionStatusEnum.Cancelled)
                outcome.Disposition = StageDispositionEnum.Cancelled;
            else if (after.Status == MissionStatusEnum.Failed || after.Status == MissionStatusEnum.LandingFailed)
                outcome.Disposition = StageDispositionEnum.Failed;
            else if (after.OutputRepairAttempts > capture.OutputRepairAttemptsBefore)
                outcome.Disposition = StageDispositionEnum.OutputRepair;
            else if (!String.IsNullOrEmpty(capture.EscalatedToCaptainId))
            {
                // The exit handler escalated: the mission is Pending for the new captain, not yet held by it.
                outcome.Disposition = StageDispositionEnum.Escalated;
                outcome.NextCaptainId = capture.EscalatedToCaptainId;
                outcome.CommitHash = capture.EscalatedCommit;
            }
            else if (!String.IsNullOrEmpty(after.CaptainId) && !String.Equals(after.CaptainId, captainId, StringComparison.Ordinal))
            {
                outcome.Disposition = StageDispositionEnum.Escalated;
                outcome.NextCaptainId = after.CaptainId;
                outcome.CommitHash = capture.EscalatedCommit;
            }
            else if (capture.ExitCode.HasValue && capture.ExitCode.Value < 0 && after.RedispatchAttempts > capture.RedispatchAttemptsBefore)
                outcome.Disposition = StageDispositionEnum.Interrupted;
            else if (MissionStateMachine.IsTerminalOrPostWork(after.Status))
            {
                outcome.Disposition = StageDispositionEnum.Completed;
                outcome.CommitHash = after.CommitHash;
            }
            else if (after.Status == MissionStatusEnum.Pending || after.Status == MissionStatusEnum.Assigned || after.Status == MissionStatusEnum.InProgress)
                outcome.Disposition = capture.ExitCode.HasValue && capture.ExitCode.Value < 0 ? StageDispositionEnum.Interrupted : StageDispositionEnum.Relaunched;
            else
                outcome.Disposition = StageDispositionEnum.Pending;
        }

        private async Task ReadProvenanceAsync(Captain captain, StageOutcome outcome, ModelEndpoint? endpoint, CancellationToken token)
        {
            if (_Provenance == null) return;

            // Both probes are cached and bounded by the probe's wait timeout; a failure leaves the value null.
            Task<string?> version = SafeProbeAsync("runtime version", () => _Provenance.GetRuntimeVersionAsync(captain, outcome.HarborId, endpoint, token));
            Task<string?> digest = endpoint == null
                ? Task.FromResult<string?>(null)
                : SafeProbeAsync("model digest", () => _Provenance.GetModelDigestAsync(endpoint, outcome.Model, outcome.HarborId, token));
            outcome.RuntimeVersion = await version.ConfigureAwait(false);
            outcome.ModelDigest = await digest.ConfigureAwait(false);
        }

        private async Task<string?> SafeProbeAsync(string what, Func<Task<string?>> probe)
        {
            try
            {
                return await probe().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Debug(_Header + "could not read the " + what + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Complete the escalation record: each escalated attempt of the mission not yet finished is linked to the attempt
        /// that completed the mission, with whether that attempt kept the escalated work (its commit is an ancestor of the
        /// finished commit) or replaced it.
        /// </summary>
        private async Task LinkEscalationsAsync(StageOutcome finished, Mission mission, string? dockId, CancellationToken token)
        {
            try
            {
                // Read every page first, so a mission with more escalations than one page links all of them.
                List<StageOutcome> escalated = new List<StageOutcome>();
                int page = 1;
                while (true)
                {
                    StageOutcomeQuery query = new StageOutcomeQuery
                    {
                        MissionId = mission.Id,
                        Disposition = StageDispositionEnum.Escalated,
                        PageNumber = page,
                        PageSize = 100
                    };
                    EnumerationResult<StageOutcome> batch = await _Database.StageOutcomes.EnumerateAsync(query, token).ConfigureAwait(false);
                    if (batch.Objects == null || batch.Objects.Count == 0) break;
                    escalated.AddRange(batch.Objects);
                    if (batch.Objects.Count < query.PageSize) break;
                    page++;
                }

                foreach (StageOutcome prior in escalated)
                {
                    if (!String.IsNullOrEmpty(prior.FinishedByOutcomeId) || String.Equals(prior.Id, finished.Id, StringComparison.Ordinal)) continue;
                    prior.FinishedByOutcomeId = finished.Id;
                    prior.FinishedByCaptainId = finished.CaptainId;
                    prior.PriorWorkKept = await ComparePriorWorkAsync(prior.CommitHash, finished.CommitHash ?? mission.BranchName, mission, dockId, token).ConfigureAwait(false);
                    prior.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.StageOutcomes.UpdateAsync(prior, token).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Warn(_Header + "could not complete the escalation record for mission " + mission.Id + ": " + ex.Message);
            }
        }

        private async Task<bool?> ComparePriorWorkAsync(string? priorCommit, string? finalRef, Mission mission, string? dockId, CancellationToken token)
        {
            if (String.IsNullOrEmpty(priorCommit) || String.IsNullOrEmpty(finalRef)) return null;
            if (String.Equals(priorCommit, finalRef, StringComparison.OrdinalIgnoreCase)) return true;
            DockHostResolver? hosts = _DockHosts?.Invoke();
            if (hosts == null) return null;

            try
            {
                Dock? dock = String.IsNullOrEmpty(dockId) ? null : await _Database.Docks.ReadAsync(dockId!, token).ConfigureAwait(false);
                string? repoPath;
                IGitService git;
                if (DockHostResolver.IsHarborDock(dock))
                {
                    repoPath = dock!.RepositoryPath;
                    git = hosts.ForDock(dock).Git;
                }
                else
                {
                    Vessel? vessel = String.IsNullOrEmpty(mission.VesselId) ? null : await _Database.Vessels.ReadAsync(mission.VesselId!, token).ConfigureAwait(false);
                    repoPath = vessel?.LocalPath;
                    git = hosts.Local.Git;
                }

                if (String.IsNullOrEmpty(repoPath)) return null;
                return await git.IsAncestorAsync(repoPath!, priorCommit!, finalRef!, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Debug(_Header + "could not compare escalated commit " + priorCommit + " with " + finalRef + ": " + ex.Message);
                return null;
            }
        }

        private static void Apply(StageOutcome outcome, FailureCauseClassification classification)
        {
            outcome.SuspectedCause = classification.Cause;
            outcome.CauseSource = classification.Source;
            outcome.CauseRuleId = classification.RuleId;
            outcome.CauseNote = classification.Note;
            outcome.ClassifiedUtc = DateTime.UtcNow;
        }

        private async Task<string> ComputePromptVersionAsync(CancellationToken token)
        {
            // Embedded templates change only with the Armada version; stored (edited) templates are hashed by name and
            // last update, so any edit gives a new version.
            StringBuilder sb = new StringBuilder();
            try
            {
                List<PromptTemplate> templates = await _Database.PromptTemplates.EnumerateAsync(token).ConfigureAwait(false);
                foreach (PromptTemplate template in templates.OrderBy(t => t.Name, StringComparer.Ordinal).ThenBy(t => t.Id, StringComparer.Ordinal))
                    sb.Append(template.Name).Append('|').Append(template.TenantId).Append('|').Append(template.LastUpdateUtc.ToString("O")).Append('\n');
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Debug(_Header + "could not read prompt templates for the prompt version: " + ex.Message);
            }

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
            return Constants.ProductVersion + "+" + Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
        }

        private static StageOutcomeQuery CopyFilter(StageOutcomeQuery filter)
        {
            return new StageOutcomeQuery
            {
                TenantId = filter.TenantId,
                MissionId = filter.MissionId,
                VoyageId = filter.VoyageId,
                VesselId = filter.VesselId,
                CaptainId = filter.CaptainId,
                Persona = filter.Persona,
                Model = filter.Model,
                SuspectedCause = filter.SuspectedCause,
                CauseSource = filter.CauseSource,
                Disposition = filter.Disposition,
                IsEvaluation = filter.IsEvaluation,
                FromUtc = filter.FromUtc,
                ToUtc = filter.ToUtc
            };
        }

        private static string? Truncate(string? value, int max)
        {
            if (value == null) return null;
            return value.Length <= max ? value : value.Substring(0, max);
        }

        private static string ReadArmadaVersion()
        {
            string? informational = typeof(StageOutcomeService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return String.IsNullOrWhiteSpace(informational) ? Constants.ProductVersion : informational!;
        }

        #endregion
    }
}
