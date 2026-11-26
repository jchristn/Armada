namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// The format-only repair turn (RS-6.4): before a stage whose report broke its contract is run again, one tool-less
    /// call restates the stage's own output in the contract's format. The restatement is appended below the original
    /// output (both are kept, and the diff is untouched) only when <see cref="FormatRepair"/> accepts it; otherwise the full
    /// rerun follows as the last resort.
    /// </summary>
    public partial class MissionService
    {
        #region Public-Members

        /// <summary>
        /// Runs the format-only repair call through a captain's runtime in a scratch directory. Null turns the format turn
        /// off, so a contract violation goes straight to the full rerun.
        /// </summary>
        public IModelProbeExecutor? FormatRepairExecutor { get; set; } = null;

        #endregion

        #region Private-Methods

        private async Task<bool> TryFormatRepairJudgeAsync(Mission mission, Captain captain, string problem, CancellationToken token)
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

        private async Task<bool> TryFormatRepairVerifierBlockAsync(Mission mission, Captain captain, string problem, CancellationToken token)
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

        private async Task<bool> TryFormatRepairArchitectPlanAsync(Mission mission, Captain? captain, string problem, IReadOnlyList<string> errors, CancellationToken token)
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

        private static string? JudgeFormatProblem(JudgeVerdictEnum verdict, bool rejectedPass, string? verdictFailureReason, string? output)
        {
            if (rejectedPass) return verdictFailureReason ?? "unsubstantiated approval";
            if (verdict == JudgeVerdictEnum.None) return "the review has no single standalone verdict line";
            if ((verdict == JudgeVerdictEnum.Fail || verdict == JudgeVerdictEnum.NeedsRevision) && !JudgeContract.ExhibitsAffectedCase(output))
                return "a " + StageOutputContract.VerdictToken(verdict) + " verdict without a ## " + JudgeContract.AffectedCaseSection
                    + " section that has a " + JudgeContract.AffectedCaseFileField + ": or " + JudgeContract.AffectedCaseScenarioField + ": line";
            return null;
        }

        private async Task<string?> RunFormatTurnAsync(Mission mission, Captain captain, string contract, string problem, IReadOnlyList<string>? errors, CancellationToken token)
        {
            IModelProbeExecutor? executor = FormatRepairExecutor;
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

        #endregion
    }
}
