namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
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
    /// Parent and child mission readiness and cancellation.
    /// </summary>
    internal sealed class MissionHierarchyService
    {
        #region Private-Members

        private readonly MissionService _Service;
        private readonly MissionServiceContext _Context;
        private readonly DatabaseDriver _Database;
        private readonly string _Header;
        private readonly LoggingModule _Logging;

        private static readonly MissionStatusEnum[] _ParentFailureStatuses = new[] { MissionStatusEnum.Failed, MissionStatusEnum.Cancelled, MissionStatusEnum.LandingFailed };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="service">The mission service facade.</param>
        /// <param name="context">State and services shared by the mission services.</param>
        /// <exception cref="ArgumentNullException">A parameter is null.</exception>
        internal MissionHierarchyService(MissionService service, MissionServiceContext context)
        {
            _Service = service ?? throw new ArgumentNullException(nameof(service));
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _Database = context.Database;
            _Header = context.Header;
            _Logging = context.Logging;
        }

        #endregion

        #region Internal-Methods

        // Gate 2: a child root waits for its parent's terminal stage. Returns the failed terminal stage when the parent
        // failed, so dispatch cancels the subtree (diagnostics never mutate).
        internal async Task<HierarchyReadiness> EvaluateParentReadinessAsync(Mission mission, CancellationToken token)
        {
            HierarchyReadiness readiness = new HierarchyReadiness();
            if (String.IsNullOrEmpty(mission.ParentMissionId) || !String.IsNullOrEmpty(mission.DependsOnMissionId)) return readiness;
            Mission? parent = await _Database.Missions.ReadAsync(mission.ParentMissionId!, token).ConfigureAwait(false);
            if (parent == null)
            {
                readiness.Blocker = new MissionAssignmentBlocker { Reason = MissionAssignmentBlockerReasonEnum.ParentNotFinished,
                    Summary = "Waiting for parent mission " + mission.ParentMissionId + ", which no longer exists. Edit this mission's parent to continue." };
                return readiness;
            }
            Mission terminal = await TerminalStageAsync(parent, token).ConfigureAwait(false);
            readiness.ParentTerminal = terminal;
            if (_ParentFailureStatuses.Contains(terminal.Status))
            {
                readiness.FailedParent = terminal;
                readiness.Blocker = new MissionAssignmentBlocker { Reason = MissionAssignmentBlockerReasonEnum.ParentNotFinished,
                    Summary = "Parent mission '" + parent.Title + "' ended " + terminal.Status + "; this mission is cancelled with it. Restart the parent to bring it back." };
                readiness.Blocker.SetBlockingMissions(new[] { terminal.Id });
                return readiness;
            }
            if (terminal.Status == MissionStatusEnum.Complete || terminal.Status == MissionStatusEnum.WorkProduced) return readiness;
            readiness.Blocker = new MissionAssignmentBlocker { Reason = MissionAssignmentBlockerReasonEnum.ParentNotFinished,
                Summary = "Waiting for parent mission '" + parent.Title + "' (" + terminal.Status + ") to finish." };
            readiness.Blocker.SetBlockingMissions(new[] { terminal.Id });
            return readiness;
        }

        // D16: the first child in plan order (creation order) continues the parent's branch; later children fork from its
        // head so parallel siblings never share a branch. An explicit ContinueParent on a later child is downgraded.
        internal async Task ApplyHierarchyBranchAsync(Mission mission, Mission parentTerminal, CancellationToken token)
        {
            if (String.IsNullOrEmpty(parentTerminal.BranchName)) return;
            List<Mission> scope = !String.IsNullOrEmpty(mission.VoyageId)
                ? await _Database.Missions.EnumerateByVoyageAsync(mission.VoyageId!, token).ConfigureAwait(false)
                : await _Database.Missions.EnumerateByVesselAsync(mission.VesselId!, token).ConfigureAwait(false);
            List<Mission> siblings = scope.Where(m => m.ParentMissionId == mission.ParentMissionId && String.IsNullOrEmpty(m.DependsOnMissionId))
                .OrderBy(m => m.CreatedUtc).ThenBy(m => m.Id, StringComparer.Ordinal).ToList();
            bool continuesTaken = siblings.Any(m => m.Id != mission.Id && m.BranchStrategy == MissionBranchStrategyEnum.ContinueParent
                && m.Status != MissionStatusEnum.Pending);
            bool first = siblings.Count == 0 || siblings[0].Id == mission.Id;
            MissionBranchStrategyEnum strategy = mission.BranchStrategy == MissionBranchStrategyEnum.ForkFromParent ? MissionBranchStrategyEnum.ForkFromParent
                : first && !continuesTaken ? MissionBranchStrategyEnum.ContinueParent : MissionBranchStrategyEnum.ForkFromParent;
            if (mission.BranchStrategy == MissionBranchStrategyEnum.ContinueParent && strategy != MissionBranchStrategyEnum.ContinueParent)
                _Logging.Info(_Header + "mission " + mission.Id + " forks from its parent's branch instead of continuing it: another child already continues " + parentTerminal.BranchName);
            mission.BranchStrategy = strategy;
            if (strategy == MissionBranchStrategyEnum.ContinueParent) mission.BranchName = parentTerminal.BranchName;
            else mission.BaseBranchName = parentTerminal.BranchName;
        }

        // D18: a failed, cancelled or unlanded parent cancels every Pending descendant with ParentFailed, breadth-first.
        internal async Task CancelDescendantsForFailedParentAsync(Mission child, Mission failedTerminal, CancellationToken token)
        {
            Queue<Mission> pending = new Queue<Mission>();
            pending.Enqueue(child);
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            while (pending.Count > 0)
            {
                Mission current = pending.Dequeue();
                if (!seen.Add(current.Id)) continue;
                Mission? latest = await _Database.Missions.ReadAsync(current.Id, token).ConfigureAwait(false);
                if (latest == null) continue;
                if (latest.Status == MissionStatusEnum.Pending)
                {
                    latest.FailureKind = MissionFailureKindEnum.ParentFailed;
                    latest.FailureReason = "Cancelled because ancestor mission " + failedTerminal.Id + " ended " + failedTerminal.Status + ". Restart that mission to bring this one back.";
                    latest.CompletedUtc = DateTime.UtcNow;
                    if (await _Service.Transitions.TryTransitionAsync(latest, new[] { MissionStatusEnum.Pending }, MissionStatusEnum.Cancelled, null, token).ConfigureAwait(false))
                    {
                        _Logging.Info(_Header + "cancelled mission " + latest.Id + " because ancestor " + failedTerminal.Id + " ended " + failedTerminal.Status);
                        await _Context.MissionHandoffService.CancelDependentPipelineStagesAsync(latest, token).ConfigureAwait(false);
                    }
                }
                foreach (Mission descendant in await ChildRootsAsync(latest, token).ConfigureAwait(false)) pending.Enqueue(descendant);
            }
        }

        // Restarting a parent puts descendants that were cancelled only because of it (ParentFailed) back to Pending, with
        // their stage chains. Missions cancelled for another reason, for example by an operator, stay cancelled.
        internal async Task<List<string>> RestoreDescendantsCancelledForParentAsync(Mission parent, CancellationToken token)
        {
            List<string> restored = new List<string>();
            Queue<Mission> queue = new Queue<Mission>(await ChildRootsAsync(parent, token).ConfigureAwait(false));
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            while (queue.Count > 0)
            {
                Mission child = queue.Dequeue();
                if (!seen.Add(child.Id)) continue;
                if (child.Status != MissionStatusEnum.Cancelled || child.FailureKind != MissionFailureKindEnum.ParentFailed) continue;
                List<Mission> chain = new List<Mission> { child };
                if (!String.IsNullOrEmpty(child.VoyageId))
                {
                    List<Mission> voyage = await _Database.Missions.EnumerateByVoyageAsync(child.VoyageId!, token).ConfigureAwait(false);
                    for (Mission? stage = voyage.FirstOrDefault(m => m.DependsOnMissionId == child.Id); stage != null && !chain.Contains(stage);
                        stage = voyage.FirstOrDefault(m => m.DependsOnMissionId == stage.Id)) chain.Add(stage);
                }
                foreach (Mission member in chain.Where(m => m.Status == MissionStatusEnum.Cancelled))
                {
                    MissionStatusEnum observed = member.Status;
                    member.FailureKind = null; member.FailureReason = null; member.CompletedUtc = null;
                    if (await _Service.Transitions.TryTransitionAsync(member, new[] { observed }, MissionStatusEnum.Pending, null, token).ConfigureAwait(false)) restored.Add(member.Id);
                }
                foreach (Mission descendant in await ChildRootsAsync(child, token).ConfigureAwait(false)) queue.Enqueue(descendant);
            }
            if (restored.Count > 0) _Logging.Info(_Header + "restart of " + parent.Id + " restored " + restored.Count + " descendant mission(s) cancelled with ParentFailed");
            return restored;
        }

        #endregion

        #region Private-Methods

        // The last stage of a logical mission: follow DependsOn links from the root within its voyage or vessel.
        private async Task<Mission> TerminalStageAsync(Mission root, CancellationToken token)
        {
            List<Mission> scope = !String.IsNullOrEmpty(root.VoyageId)
                ? await _Database.Missions.EnumerateByVoyageAsync(root.VoyageId!, token).ConfigureAwait(false)
                : String.IsNullOrEmpty(root.VesselId) ? new List<Mission>() : await _Database.Missions.EnumerateByVesselAsync(root.VesselId!, token).ConfigureAwait(false);
            return ArchitectFanOutService.FindTerminalPipelineStage(scope, root);
        }

        private async Task<List<Mission>> ChildRootsAsync(Mission parent, CancellationToken token)
        {
            List<Mission> scope = !String.IsNullOrEmpty(parent.VoyageId)
                ? await _Database.Missions.EnumerateByVoyageAsync(parent.VoyageId!, token).ConfigureAwait(false)
                : String.IsNullOrEmpty(parent.VesselId) ? new List<Mission>() : await _Database.Missions.EnumerateByVesselAsync(parent.VesselId!, token).ConfigureAwait(false);
            return scope.Where(m => m.ParentMissionId == parent.Id && String.IsNullOrEmpty(m.DependsOnMissionId)).OrderBy(m => m.CreatedUtc).ToList();
        }

        #endregion
    }
}
