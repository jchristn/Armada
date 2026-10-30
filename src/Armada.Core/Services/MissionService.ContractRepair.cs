namespace Armada.Core.Services
{
    using System.Collections.Concurrent;
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Safe contract repair (MODEL_HETEROGENEITY.md, design principle 4): a stage whose work was fine but whose report broke
    /// the contract is repaired deterministically from the repository or its own output, and every repair is recorded.
    /// </summary>
    public partial class MissionService
    {
        #region Private-Members

        /// <summary>
        /// Missions whose leftover work Armada committed during this attempt, read once by the completion handler.
        /// </summary>
        private readonly ConcurrentDictionary<string, byte> _LeftoverCommitted = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        #endregion

        #region Private-Methods

        private static bool IsRepositoryWorkStage(string? persona)
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
        private async Task RecordWorkStageRepairsAsync(Mission mission, Captain captain, Dock? dock, bool leftoverCommitted, CancellationToken token)
        {
            if (!IsRepositoryWorkStage(mission.Persona)) return;

            if (leftoverCommitted)
            {
                await RecordContractRepairAsync(mission, captain, ContractRepairKindEnum.CommitMissingRecoveredFromRepo,
                    "The captain left non-scratch changes uncommitted; Armada committed them as " + (mission.CommitHash ?? "the dock head") + ".", token).ConfigureAwait(false);
            }

            bool blockedVerifier = PersonaCatalog.Matches(mission.Persona, PersonaCatalog.TestEngineer) && VerifierContract.IsBlocked(mission.AgentOutput);
            ContractRepairKindEnum completion = ContractRepair.ClassifyCompletionLine(mission.AgentOutput);
            if (completion == ContractRepairKindEnum.None || blockedVerifier || dock == null) return;

            string? startCommit = TryReadDockStartCommit(dock, mission);
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
        private async Task<JudgeVerdictEnum> TryRepairJudgeVerdictAsync(Mission mission, Captain captain, CancellationToken token)
        {
            if (!ContractRepair.TryRecoverVerdict(mission.AgentOutput, out JudgeVerdictEnum recovered, out string? sourceLine))
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
        private async Task<bool> TryRepairArchitectPlanAsync(Mission mission, CancellationToken token)
        {
            if (!String.IsNullOrEmpty(mission.AgentOutput) && mission.AgentOutput.Contains("[ARMADA:MISSION]", StringComparison.Ordinal)) return false;
            if (!ContractRepair.TryRecoverArchitectPlan(mission.AgentOutput, out string? planJson) || planJson == null) return false;

            mission.AgentOutput = ContractRepair.AppendRepair(mission.AgentOutput, ContractRepairKindEnum.PlanBlockNonCanonical,
                "```" + ArchitectPlanParser.FenceInfo + "\n" + planJson + "\n```");
            mission.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
            await RecordContractRepairAsync(mission, null, ContractRepairKindEnum.PlanBlockNonCanonical,
                "The only plan block was not a closed armada-plan fence; it passed the plan contract and was re-fenced.", token).ConfigureAwait(false);
            return true;
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
