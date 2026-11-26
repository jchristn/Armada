namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Escalation in place (RS-5.6): a work stage whose captain could not finish moves, on the same branch, to the next
    /// captain on its persona's escalation ladder or to a captain with a strictly higher effective tier.
    /// </summary>
    public partial class MissionService
    {
        private const string _EscalatedHeading = "## Escalated to ";

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
        public async Task<bool> ApplyEscalationAsync(Mission mission, Captain failedCaptain, EscalationChoice choice, string failureReason, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (failedCaptain == null) throw new ArgumentNullException(nameof(failedCaptain));
            if (choice == null) throw new ArgumentNullException(nameof(choice));
            MissionStatusEnum observedStatus = mission.Status;
            DateTime escalationVersion = mission.LastUpdateUtc;
            MissionNotes.Set(mission, MissionNoteKindEnum.Escalation, _EscalatedHeading + choice.Captain.Name + "\n"
                + failedCaptain.Name + " could not finish this mission (" + (failureReason ?? String.Empty).Trim() + "). Its work so far is committed on this branch and may not build yet. "
                + "Review it with git log and git diff against the base branch, fix what is wrong, and finish the mission.");
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
            MissionTransitionRequest escalation = new MissionTransitionRequest { To = MissionStatusEnum.Pending, ExpectedLastUpdateUtc = escalationVersion };
            escalation.AllowedFrom.Add(observedStatus);
            if (!await Transitions.TryTransitionAsync(mission, escalation, token).ConfigureAwait(false)) return false;
            _Logging.Warn(_Header + "mission " + mission.Id + ": " + failedCaptain.Name + " could not finish (" + failureReason + "); escalating to "
                + choice.Captain.Name + " (" + choice.Describe() + ", hop " + mission.EscalationCount + " of " + _Settings.MaxEscalations + ")");
            return true;
        }
    }
}
