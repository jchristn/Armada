namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;

    /// <summary>Shared REST/MCP manual handoff contract. A status write cannot substitute for landing.</summary>
    public static class ManualMissionHandoff
    {
        /// <summary>Return why a requested handoff cannot bypass the active agent or exit claim.</summary>
        public static string? Refusal(Mission mission, MissionStatusEnum target)
        {
            if (mission.Status == MissionStatusEnum.Verifying && target != MissionStatusEnum.Cancelled)
                return "Stage verification still owns this mission. Wait for it to finish, or cancel the mission.";
            if (target != MissionStatusEnum.WorkProduced && target != MissionStatusEnum.Complete) return null;
            if (mission.Status == MissionStatusEnum.Verifying || mission.Status == MissionStatusEnum.InProgress
                || mission.Status == MissionStatusEnum.Testing || mission.ProcessId.HasValue)
                return "The agent or its exit/stage-check handoff still owns this mission. Wait for normal handoff before requesting landing.";
            return null;
        }

        /// <summary>
        /// Capture the branch head, conditionally claim manual handoff, then invoke the normal landing handler. Without an
        /// active dock, a WorkProduced or LandingFailed mission asked to become Complete is completed directly (no landing)
        /// only when git on the vessel's Admiral-side repository proves its commit and branch are already contained in the
        /// target branch; otherwise the active-dock error is thrown and the mission is left unchanged.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="admiral">Admiral service (diff capture and landing hooks).</param>
        /// <param name="mission">Mission to hand off.</param>
        /// <param name="target">Requested status; only Complete may take the no-change path.</param>
        /// <param name="git">Git service for the no-change containment check, or null to disable that path.</param>
        /// <param name="settings">Settings (repos directory) for locating the vessel repository, or null.</param>
        /// <param name="transitions">Mission status transitions, or null for the Admiral's.</param>
        /// <exception cref="InvalidOperationException">Landing is unavailable, no active dock exists and no change cannot be proven, or the mission changed concurrently.</exception>
        public static async Task<Mission> LandAsync(DatabaseDriver database, IAdmiralService admiral, Mission mission, MissionStatusEnum target = MissionStatusEnum.WorkProduced, IGitService? git = null, ArmadaSettings? settings = null, MissionTransitions? transitions = null)
        {
            if (admiral.OnMissionComplete == null || admiral.OnCaptureDiff == null)
                throw new InvalidOperationException("Landing is not available; mission status was not changed.");
            transitions ??= admiral.Transitions;
            Dock? dock = String.IsNullOrEmpty(mission.DockId) ? null : await database.Docks.ReadAsync(mission.DockId).ConfigureAwait(false);
            if (dock == null || !dock.Active || String.IsNullOrEmpty(dock.WorktreePath))
            {
                Mission? completed = await TryCompleteWithoutChangesAsync(database, transitions, mission, target, git, settings).ConfigureAwait(false);
                if (completed != null) return completed;
                throw new InvalidOperationException("An active dock is required to resolve and land this mission's branch head.");
            }
            DateTime version = mission.LastUpdateUtc;
            MissionStatusEnum status = mission.Status;
            await admiral.OnCaptureDiff(mission, dock).ConfigureAwait(false);
            if (String.IsNullOrEmpty(mission.CommitHash))
                throw new InvalidOperationException("The mission branch head could not be resolved; mission status was not changed.");
            // CaptureDiff can persist metadata. Re-read that version, while retaining the original status/attempt guard.
            Mission? current = await database.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
            if (current == null || current.Status != status || current.ProcessId != mission.ProcessId || current.StartedUtc != mission.StartedUtc)
                throw new InvalidOperationException("Mission changed during manual handoff; retry against its current state.");
            version = current.LastUpdateUtc;
            current.CommitHash = mission.CommitHash;
            MissionTransitionRequest handoff = new MissionTransitionRequest { To = MissionStatusEnum.WorkProduced, ExpectedLastUpdateUtc = version };
            handoff.AllowedFrom.Add(status);
            if (!await transitions.TryTransitionAsync(current, handoff, CancellationToken.None).ConfigureAwait(false))
                throw new InvalidOperationException("Mission changed during manual handoff; its newer state was preserved.");
            await admiral.OnMissionComplete(current, dock).ConfigureAwait(false);
            return await database.Missions.ReadAsync(current.Id).ConfigureAwait(false) ?? current;
        }

        private static async Task<Mission?> TryCompleteWithoutChangesAsync(DatabaseDriver database, MissionTransitions transitions, Mission mission, MissionStatusEnum target, IGitService? git, ArmadaSettings? settings)
        {
            if (git == null || target != MissionStatusEnum.Complete) return null;
            if (mission.Status != MissionStatusEnum.WorkProduced && mission.Status != MissionStatusEnum.LandingFailed) return null;
            if (Refusal(mission, target) != null) return null;
            if (String.IsNullOrEmpty(mission.CommitHash) || String.IsNullOrEmpty(mission.VesselId)) return null;
            HashSet<string> queued = await MergeQueueMissionActivity.GetMissionIdsWithActiveEntriesAsync(database).ConfigureAwait(false);
            if (queued.Contains(mission.Id)) return null;

            Vessel? vessel = await database.Vessels.ReadAsync(mission.VesselId).ConfigureAwait(false);
            if (vessel == null) return null;
            string? repoPath = ResolveRepoPath(vessel, settings);
            if (repoPath == null) return null;
            string targetBranch = String.IsNullOrWhiteSpace(vessel.DefaultBranch) ? "main" : vessel.DefaultBranch!;

            // Both the recorded head and the branch as it stands now (it may have moved after the head was recorded) must
            // already be in the target branch; any doubt (unknown commit, git failure) keeps the active-dock error.
            if (!await IsContainedAsync(git, repoPath, mission.CommitHash!, targetBranch).ConfigureAwait(false)) return null;
            if (!String.IsNullOrEmpty(mission.BranchName)
                && await git.BranchExistsAsync(repoPath, mission.BranchName!).ConfigureAwait(false)
                && !await IsContainedAsync(git, repoPath, "refs/heads/" + mission.BranchName, targetBranch).ConfigureAwait(false))
                return null;

            Mission? current = await database.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
            if (current == null || current.Status != mission.Status || current.ProcessId != mission.ProcessId
                || current.StartedUtc != mission.StartedUtc || current.CommitHash != mission.CommitHash)
                throw new InvalidOperationException("Mission changed during manual handoff; retry against its current state.");
            DateTime version = current.LastUpdateUtc;
            MissionStatusEnum status = current.Status;
            DateTime now = DateTime.UtcNow;
            current.CompletedUtc = now;
            current.FailureReason = null;
            current.FailureKind = null;
            MissionTransitionRequest completion = new MissionTransitionRequest { To = MissionStatusEnum.Complete, ExpectedLastUpdateUtc = version };
            completion.AllowedFrom.Add(status);
            if (!await transitions.TryTransitionAsync(current, completion, CancellationToken.None).ConfigureAwait(false))
                throw new InvalidOperationException("Mission changed during manual handoff; its newer state was preserved.");

            // Same event the landing handler records on a successful landing; voyage completion rolls up from the status.
            ArmadaEvent completedEvent = new ArmadaEvent("mission.completed", "Mission completed (no changes to land): " + current.Title);
            completedEvent.TenantId = current.TenantId;
            completedEvent.UserId = current.UserId;
            completedEvent.EntityType = "mission";
            completedEvent.EntityId = current.Id;
            completedEvent.CaptainId = current.CaptainId;
            completedEvent.MissionId = current.Id;
            completedEvent.VesselId = current.VesselId;
            completedEvent.VoyageId = current.VoyageId;
            await database.Events.CreateAsync(completedEvent).ConfigureAwait(false);
            return await database.Missions.ReadAsync(current.Id).ConfigureAwait(false) ?? current;
        }

        private static async Task<bool> IsContainedAsync(IGitService git, string repoPath, string source, string targetBranch)
        {
            if (await git.IsAncestorAsync(repoPath, source, "refs/heads/" + targetBranch).ConfigureAwait(false) == true) return true;
            // A working clone may only have the target branch's latest state on origin.
            return await git.IsAncestorAsync(repoPath, source, "refs/remotes/origin/" + targetBranch).ConfigureAwait(false) == true;
        }

        private static string? ResolveRepoPath(Vessel vessel, ArmadaSettings? settings)
        {
            // Same order as ManualLandingReconciler: bare repository, working clone, conventional repos directory location.
            if (!String.IsNullOrEmpty(vessel.LocalPath) && Directory.Exists(vessel.LocalPath)) return vessel.LocalPath;
            if (!String.IsNullOrEmpty(vessel.WorkingDirectory) && Directory.Exists(vessel.WorkingDirectory)) return vessel.WorkingDirectory;
            if (settings != null && !String.IsNullOrEmpty(vessel.Name))
            {
                string candidate = Path.Combine(settings.ReposDirectory, vessel.Name + ".git");
                if (Directory.Exists(candidate)) return candidate;
            }
            return null;
        }
    }
}
