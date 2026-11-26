namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Escalation in place (RS-5.6): a work stage whose captain could not finish moves, on the same branch, to the next
    /// captain on its persona's escalation ladder or to a captain with a strictly higher effective tier.
    /// </summary>
    public partial class MissionService
    {
        private static readonly BlackBoxRedactor _EscalationRedactor = new BlackBoxRedactor();

        /// <inheritdoc />
        public async Task<EscalationChoice?> ChooseEscalationAsync(Mission mission, string? failedCaptainId, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            // Planning and review stages are not escalated; only stages that do the work.
            if (PersonaCatalog.Matches(mission.Persona, PersonaCatalog.Judge) || PersonaCatalog.Matches(mission.Persona, PersonaCatalog.Architect)
                || PersonaCatalog.Matches(mission.Persona, PersonaCatalog.ProductManager) || PersonaCatalog.Matches(mission.Persona, PersonaCatalog.Recorder))
                return null;
            if (mission.EscalationCount >= _Settings.MaxEscalations) return null;
            // A strict assignment fixes the captain on purpose (tests that control model identity), so it fails rather
            // than finishing on a different model. Strict missions therefore never count toward G12.
            if (await HasStrictCaptainOverrideAsync(mission, token).ConfigureAwait(false)) return null;
            IReadOnlyList<Mission> voyageMissions = String.IsNullOrEmpty(mission.VoyageId)
                ? new List<Mission>()
                : await _Database.Missions.EnumerateByVoyageAsync(mission.VoyageId!, token).ConfigureAwait(false);
            return await CaptainEscalationSelector.ChooseAsync(_Database, mission, failedCaptainId, voyageMissions, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> ApplyEscalationAsync(Mission mission, Captain failedCaptain, EscalationChoice choice, EscalationMemory memory, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (failedCaptain == null) throw new ArgumentNullException(nameof(failedCaptain));
            if (choice == null) throw new ArgumentNullException(nameof(choice));
            if (memory == null) throw new ArgumentNullException(nameof(memory));
            MissionStatusEnum observedStatus = mission.Status;
            DateTime escalationVersion = mission.LastUpdateUtc;

            // RS-5.7: the escalated captain starts from what the previous attempt left, not only from the branch.
            await RecallPreviousAttemptAsync(mission, failedCaptain, memory, token).ConfigureAwait(false);
            MissionNotes.Set(mission, MissionNoteKindEnum.Escalation, EscalationMemoryRenderer.Render(memory, choice.Captain.Name));
            mission.EscalationCount++;
            mission.EscalatedFromCaptainId = failedCaptain.Id;
            mission.EscalatedFromModel = failedCaptain.Model;
            mission.RequestedCaptainId = choice.Captain.Id;
            mission.CaptainId = null;
            mission.DockId = null;
            mission.ProcessId = null;
            mission.FailureReason = null;
            mission.FailureKind = null;
            mission.StartedUtc = null;
            mission.CompletedUtc = null;
            mission.AgentOutput = null;
            mission.DiffSnapshot = null;
            MissionTransitionRequest escalation = new MissionTransitionRequest { To = MissionStatusEnum.Pending, ExpectedLastUpdateUtc = escalationVersion };
            escalation.AllowedFrom.Add(observedStatus);
            if (!await Transitions.TryTransitionAsync(mission, escalation, token).ConfigureAwait(false)) return false;
            _Logging.Warn(_Header + "mission " + mission.Id + ": " + failedCaptain.Name + " could not finish (" + memory.Reason + "); escalating to "
                + choice.Captain.Name + " (" + choice.Describe() + ", hop " + mission.EscalationCount + " of " + _Settings.MaxEscalations + ")"
                + (memory.HintRuleId != null ? "; hint " + memory.HintRuleId : ""));
            return true;
        }

        private async Task RecallPreviousAttemptAsync(Mission mission, Captain failedCaptain, EscalationMemory memory, CancellationToken token)
        {
            memory.FromCaptainName = failedCaptain.Name;
            memory.FromModel = String.IsNullOrWhiteSpace(failedCaptain.Model) ? null : failedCaptain.Model;
            if (!memory.FailureKind.HasValue && memory.RuntimeFailureKind.HasValue)
                memory.FailureKind = FailureKindMapper.FromRuntimeExit(memory.RuntimeFailureKind.Value, memory.ExitCode);

            string? output = String.IsNullOrEmpty(mission.AgentOutput) ? OnPeekMissionOutput?.Invoke(mission.Id) : mission.AgentOutput;
            if (!String.IsNullOrWhiteSpace(output))
                memory.OutputTail = _EscalationRedactor.Redact(BlackBoxLimits.Tail(output!.TrimEnd(), EscalationMemoryRenderer.MaxOutputTailChars));

            Dock? dock = String.IsNullOrEmpty(mission.DockId) ? null : await _Database.Docks.ReadAsync(mission.DockId!, token).ConfigureAwait(false);
            if (dock != null) memory.ChangedFiles = await TryReadChangedFilesAsync(dock, TryReadDockStartCommit(dock, mission), token).ConfigureAwait(false);

            EscalationMemoryRenderer.ApplyHint(memory);
        }

        // The non-scratch files changed in the dock since startCommit, or null when Git could not answer.
        private async Task<List<string>?> TryReadChangedFilesAsync(Dock dock, string? startCommit, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(dock.WorktreePath) || String.IsNullOrWhiteSpace(startCommit)) return null;
            DockHost? harborHost = HarborHostFor(dock);
            IGitService? git = harborHost != null ? harborHost.Git : _Git;
            if (git == null) return null;
            try
            {
                IReadOnlyList<string> changed = await git.GetChangedFilesSinceAsync(dock.WorktreePath!, startCommit!, token).ConfigureAwait(false);
                return changed.Where(path => !ScratchFileMatcher.IsScratchFile(path)).ToList();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not read the files changed in dock " + dock.Id + " before escalating: " + ex.Message);
                return null;
            }
        }
    }
}
