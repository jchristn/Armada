namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Repairs a stage's output: contract repairs, format repairs, and output-repair relaunches.
    /// </summary>
    internal sealed class OutputRepairService
    {
        #region Private-Members

        private readonly MissionService _Service;
        private readonly MissionServiceContext _Context;
        private readonly DatabaseDriver _Database;
        private readonly IDockService _Docks;
        private readonly string _Header;
        private readonly LoggingModule _Logging;
        private readonly ArmadaSettings _Settings;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="service">The mission service facade.</param>
        /// <param name="context">State and services shared by the mission services.</param>
        /// <exception cref="ArgumentNullException">A parameter is null.</exception>
        internal OutputRepairService(MissionService service, MissionServiceContext context)
        {
            _Service = service ?? throw new ArgumentNullException(nameof(service));
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _Database = context.Database;
            _Docks = context.Docks;
            _Header = context.Header;
            _Logging = context.Logging;
            _Settings = context.Settings;
        }

        #endregion

        #region Internal-Methods

        internal async Task<bool> TryFormatRepairJudgeAsync(Mission mission, Captain captain, string problem, CancellationToken token)
        {
            string? original = mission.AgentOutput;
            string? restated = await RunFormatTurnAsync(mission, captain, StageOutputContract.For(PersonaCatalog.Judge), problem, null, token).ConfigureAwait(false);
            if (restated == null) return false;
            if (!FormatRepair.TryAcceptJudge(original, restated, out JudgeVerdictEnum verdict, out string? rejection))
            {
                _Logging.Info(_Header + "format repair of judge mission " + mission.Id + " rejected: " + rejection);
                return false;
            }

            await ApplyFormatRepairAsync(mission, captain, restated, "Restated as " + verdict + " after: " + problem.Trim(), token).ConfigureAwait(false);
            return true;
        }

        internal async Task<bool> TryFormatRepairVerifierBlockAsync(Mission mission, Captain captain, string problem, CancellationToken token)
        {
            string? original = mission.AgentOutput;
            string? restated = await RunFormatTurnAsync(mission, captain, VerifierContract.BlockingInstruction().Trim(), problem, null, token).ConfigureAwait(false);
            if (restated == null) return false;
            if (!FormatRepair.TryAcceptVerifierBlock(original, restated, out string? rejection))
            {
                _Logging.Info(_Header + "format repair of " + mission.Persona + " mission " + mission.Id + " rejected: " + rejection);
                return false;
            }

            await ApplyFormatRepairAsync(mission, captain, restated, "Restated the block with its affected case after: " + problem.Trim(), token).ConfigureAwait(false);
            return true;
        }

        internal async Task<bool> TryFormatRepairArchitectPlanAsync(Mission mission, Captain? captain, string problem, IReadOnlyList<string> errors, CancellationToken token)
        {
            if (captain == null && !String.IsNullOrEmpty(mission.CaptainId))
                captain = await _Database.Captains.ReadAsync(mission.CaptainId!, token).ConfigureAwait(false);
            if (captain == null) return false;

            string? original = mission.AgentOutput;
            string? restated = await RunFormatTurnAsync(mission, captain, StageOutputContract.For(PersonaCatalog.Architect), problem, errors, token).ConfigureAwait(false);
            if (restated == null) return false;
            if (!FormatRepair.TryAcceptArchitectPlan(original, restated, _Settings.Concurrency.MaxHierarchyDepth, out string? rejection))
            {
                _Logging.Info(_Header + "format repair of architect mission " + mission.Id + " rejected: " + rejection);
                return false;
            }

            await ApplyFormatRepairAsync(mission, captain, restated, "Restated the plan after: " + problem.Trim(), token).ConfigureAwait(false);
            return true;
        }

        internal static string? JudgeFormatProblem(JudgeVerdictEnum verdict, bool rejectedPass, string? verdictFailureReason, string? output)
        {
            if (rejectedPass) return verdictFailureReason ?? "unsubstantiated approval";
            if (verdict == JudgeVerdictEnum.None) return "the review has no single standalone verdict line";
            if ((verdict == JudgeVerdictEnum.Fail || verdict == JudgeVerdictEnum.NeedsRevision) && !JudgeContract.ExhibitsAffectedCase(output))
                return "a " + StageOutputContract.VerdictToken(verdict) + " verdict without a ## " + JudgeContract.AffectedCaseSection
                    + " section that has a " + JudgeContract.AffectedCaseFileField + ": or " + JudgeContract.AffectedCaseScenarioField + ": line";
            return null;
        }

        internal async Task<bool> TryScheduleOutputRepairAsync(Mission mission, string problem, CancellationToken token)
        {
            // A person decides review-gated stages; never re-run behind their back.
            if (mission.RequiresReview) return false;

            // RS-5.5: the model profile of the captain that broke the contract can override MaxOutputRepairAttempts.
            // RS-5.10: RecoveryPolicyEngine decides whether a repair turn is left.
            RecoveryPolicy repairPolicy = await RecoveryPolicyResolver.ResolveForCaptainIdAsync(_Database, _Settings, mission.CaptainId, token).ConfigureAwait(false);
            int max = repairPolicy.OutputRepairAttempts;
            RecoveryDecision repair = RecoveryPolicyEngine.Decide(new RecoveryFacts(RecoverySiteEnum.OutputContract)
            {
                ReviewGated = mission.RequiresReview,
                OutputRepairs = mission.OutputRepairAttempts
            }, repairPolicy);
            if (repair.First.Action != RecoveryActionEnum.Repair) return false;

            MissionStatusEnum observedStatus = mission.Status;
            int attempt = mission.OutputRepairAttempts + 1;
            string note = "## Output contract repair (attempt " + attempt + " of " + max + ")\n"
                + "Your previous response for this mission could not be used: " + problem.Trim() + "\n"
                + "Do the same job again from the current branch and respond in exactly the format of the Output Contract section at the end of these instructions. Nothing else about the task has changed.";

            mission.OutputRepairAttempts = attempt;
            MissionNotes.Set(mission, MissionNoteKindEnum.OutputRepair, note);
            mission.CaptainId = null;
            mission.DockId = null;
            mission.ProcessId = null;
            mission.AgentOutput = null;
            mission.DiffSnapshot = null;
            mission.FailureReason = null;
            mission.FailureKind = null;
            mission.StartedUtc = null;
            mission.CompletedUtc = null;
            mission.TotalRuntimeMs = null;
            if (!await _Service.Transitions.TryTransitionAsync(mission, new[] { observedStatus }, MissionStatusEnum.Pending, null, token).ConfigureAwait(false))
            {
                _Logging.Info(_Header + "mission " + mission.Id + " left " + observedStatus + " before its output repair was recorded; not repairing");
                mission.OutputRepairAttempts = attempt - 1;
                return false;
            }

            _Logging.Warn(_Header + "mission " + mission.Id + " (" + (mission.Persona ?? "stage") + ") broke its output contract (" + problem.Trim()
                + "); running it again with the contract errors quoted (repair " + attempt + "/" + max + ")");
            return true;
        }

        internal async Task FinishOutputRepairScheduledAsync(Mission mission, Captain captain, Dock? dock, CancellationToken token)
        {
            if (dock != null)
            {
                try { await _Docks.ReclaimAsync(dock.Id, token: CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) { _Logging.Warn(_Header + "error reclaiming dock " + dock.Id + " before output repair of mission " + mission.Id + ": " + ex.Message); }
            }

            await _Context.StageCompletionHandler.ReleaseCaptainAfterCompletionAsync(captain, mission.Id).ConfigureAwait(false);
            _Context.MissionWaitCoordinator.RequestDispatch();
        }

        internal string AppendRepairHistory(string reason, Mission mission)
        {
            if (mission.OutputRepairAttempts <= 0)
                return _Settings.MaxOutputRepairAttempts <= 0 ? reason : reason + " (output repair is unavailable for review-gated stages or was not possible)";
            return reason + " (after " + mission.OutputRepairAttempts + " output-contract repair attempt(s); the stage kept breaking its output format, so check the captain's model and persona template)";
        }

        internal static bool IsRepositoryWorkStage(string? persona)
        {
            return String.IsNullOrEmpty(persona)
                || PersonaCatalog.Matches(persona, PersonaCatalog.Worker)
                || PersonaCatalog.Matches(persona, PersonaCatalog.TestEngineer);
        }

        /// <summary>
        /// A Worker or Test Engineer whose completion line is missing or malformed is judged by its repository, never by its
        /// transcript: work on the dock branch means the work was produced. A stage with nothing on the branch was already
        /// rejected as unfinished before this runs. A commit Armada made for the captain is recorded too.
        /// </summary>
        internal async Task RecordWorkStageRepairsAsync(Mission mission, Captain captain, Dock? dock, bool leftoverCommitted, CancellationToken token)
        {
            if (!IsRepositoryWorkStage(mission.Persona)) return;

            if (leftoverCommitted)
            {
                await RecordContractRepairAsync(mission, captain, ContractRepairKindEnum.CommitMissingRecoveredFromRepo,
                    "The captain left non-scratch changes uncommitted; Armada committed them as " + (mission.CommitHash ?? "the dock head") + ".", token).ConfigureAwait(false);
            }

            bool blockedVerifier = PersonaCatalog.Matches(mission.Persona, PersonaCatalog.TestEngineer) && VerifierContract.IsBlocked(StageSubmissionText.EffectiveOutput(mission));
            ContractRepairKindEnum completion = ContractRepair.ClassifyCompletionLine(StageSubmissionText.EffectiveOutput(mission));
            if (completion == ContractRepairKindEnum.None || blockedVerifier || dock == null) return;

            string? startCommit = _Context.MissionScopeGuard.TryReadDockStartCommit(dock, mission);
            if (!ContractRepair.HasNewWork(mission.DiffSnapshot, startCommit, mission.CommitHash)) return;

            string evidence = !String.IsNullOrEmpty(startCommit) && !String.IsNullOrEmpty(mission.CommitHash)
                && !String.Equals(startCommit!.Trim(), mission.CommitHash!.Trim(), StringComparison.OrdinalIgnoreCase)
                    ? "dock branch moved from " + startCommit.Trim() + " to " + mission.CommitHash.Trim()
                    : "dock diff is not empty";
            await RecordContractRepairAsync(mission, captain, completion,
                (completion == ContractRepairKindEnum.CompletionLineMalformed ? "Completion line malformed" : "Completion line missing")
                + "; the repository shows the work was produced (" + evidence + ").", token).ConfigureAwait(false);
        }

        /// <summary>
        /// A Judge that wrote exactly one PASS, FAIL or NEEDS_REVISION in a nearby non-canonical form gets the canonical line
        /// appended below its original output. Returns the recovered verdict, or None when nothing was repaired.
        /// </summary>
        internal async Task<JudgeVerdictEnum> TryRepairJudgeVerdictAsync(Mission mission, Captain captain, CancellationToken token)
        {
            if (!ContractRepair.TryRecoverVerdict(StageSubmissionText.EffectiveOutput(mission), out JudgeVerdictEnum recovered, out string? sourceLine))
                return JudgeVerdictEnum.None;

            mission.AgentOutput = ContractRepair.AppendRepair(mission.AgentOutput, ContractRepairKindEnum.VerdictNonCanonical, ContractRepair.CanonicalVerdictLine(recovered));
            mission.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
            await RecordContractRepairAsync(mission, captain, ContractRepairKindEnum.VerdictNonCanonical,
                "Read verdict " + recovered + " from the non-canonical line: " + sourceLine, token).ConfigureAwait(false);
            return recovered;
        }

        /// <summary>
        /// An Architect whose single valid plan is not in a closed armada-plan fence gets the plan appended in a canonical
        /// fence below its original output.
        /// </summary>
        internal async Task<bool> TryRepairArchitectPlanAsync(Mission mission, CancellationToken token)
        {
            string? output = StageSubmissionText.EffectiveOutput(mission);
            if (!ContractRepair.TryRecoverArchitectPlan(output, out string? planJson) || planJson == null) return false;

            mission.AgentOutput = ContractRepair.AppendRepair(mission.AgentOutput, ContractRepairKindEnum.PlanBlockNonCanonical,
                "```" + ArchitectPlanParser.FenceInfo + "\n" + planJson + "\n```");
            mission.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
            await RecordContractRepairAsync(mission, null, ContractRepairKindEnum.PlanBlockNonCanonical,
                "The only plan block was not a closed armada-plan fence; it passed the plan contract and was re-fenced.", token).ConfigureAwait(false);
            return true;
        }

        #endregion

        #region Private-Methods

        private async Task<string?> RunFormatTurnAsync(Mission mission, Captain captain, string contract, string problem, IReadOnlyList<string>? errors, CancellationToken token)
        {
            IModelProbeExecutor? executor = _Service.FormatRepairExecutor;
            if (executor == null) return null;

            // A person decides review-gated stages; nothing is repaired behind their back.
            if (mission.RequiresReview) return null;

            // RS-5.5: the format turn is a repair, so a policy of zero repairs turns it off too. At most one runs per
            // stage attempt; a rejected or failed turn falls through to the full rerun.
            RecoveryPolicy policy = await RecoveryPolicyResolver.ResolveForCaptainIdAsync(_Database, _Settings, mission.CaptainId ?? captain.Id, token).ConfigureAwait(false);
            if (policy.OutputRepairAttempts <= 0) return null;

            Captain runner = captain;
            string? normalizerId = _Settings.OutputRepairNormalizerCaptainId;
            if (!String.IsNullOrWhiteSpace(normalizerId))
            {
                Captain? normalizer = await _Database.Captains.ReadAsync(normalizerId!.Trim(), token).ConfigureAwait(false);
                if (normalizer != null) runner = normalizer;
                else _Logging.Warn(_Header + "format repair normalizer captain " + normalizerId + " was not found; using captain " + captain.Id);
            }

            List<string> problems = new List<string>();
            if (errors != null) problems.AddRange(errors);
            if (problems.Count == 0) problems.Add(problem);

            ModelProbeExchangeRequest request = new ModelProbeExchangeRequest
            {
                Captain = runner,
                RunId = "format-repair-" + mission.Id,
                Label = "format repair",
                Prompt = FormatRepair.BuildPrompt(mission.Persona, mission.AgentOutput, contract, problems),
                TenantId = mission.TenantId,
                UserId = mission.UserId,
                TimeoutMs = FormatRepair.TimeoutMs
            };

            ModelProbeExchange exchange;
            try
            {
                exchange = await executor.RunAsync(request, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "format repair call for mission " + mission.Id + " failed: " + ex.Message);
                return null;
            }

            if (exchange.CouldNotRunReason != null || exchange.TimedOut || String.IsNullOrWhiteSpace(exchange.Output))
            {
                _Logging.Info(_Header + "format repair call for mission " + mission.Id + " produced no answer"
                    + (exchange.CouldNotRunReason != null ? ": " + exchange.CouldNotRunReason : exchange.TimedOut ? " (timed out)" : ""));
                return null;
            }

            return exchange.Output.Trim();
        }

        private async Task ApplyFormatRepairAsync(Mission mission, Captain captain, string restated, string detail, CancellationToken token)
        {
            mission.AgentOutput = ContractRepair.AppendRepair(mission.AgentOutput, ContractRepairKindEnum.FormatRepairTurn, restated);
            mission.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
            await RecordContractRepairAsync(mission, captain, ContractRepairKindEnum.FormatRepairTurn, detail, token).ConfigureAwait(false);
        }

        private async Task RecordContractRepairAsync(Mission mission, Captain? captain, ContractRepairKindEnum kind, string detail, CancellationToken token)
        {
            string? captainId = captain?.Id ?? mission.CaptainId;
            _Logging.Info(_Header + "contract repair " + kind + " for mission " + mission.Id + ": " + detail);
            try
            {
                ContractRepairRecord record = new ContractRepairRecord
                {
                    MissionId = mission.Id,
                    Persona = String.IsNullOrEmpty(mission.Persona) ? PersonaCatalog.Worker : mission.Persona,
                    CaptainId = captainId,
                    RepairKind = kind,
                    Detail = detail
                };
                ArmadaEvent repairEvent = new ArmadaEvent(ContractRepair.EventType, "Contract repaired (" + kind + "): " + mission.Title);
                repairEvent.TenantId = mission.TenantId;
                repairEvent.UserId = mission.UserId;
                repairEvent.EntityType = "mission";
                repairEvent.EntityId = mission.Id;
                repairEvent.CaptainId = captainId;
                repairEvent.MissionId = mission.Id;
                repairEvent.VesselId = mission.VesselId;
                repairEvent.VoyageId = mission.VoyageId;
                repairEvent.Payload = JsonSerializer.Serialize(record);
                await _Database.Events.CreateAsync(repairEvent, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Warn(_Header + "could not record contract repair " + kind + " for mission " + mission.Id + ": " + ex.Message);
            }
        }

        #endregion
    }
}
