namespace Armada.Core.Services
{
    using System.Text;
    using System.Text.Json;
    using SyslogLogging;
    using Armada.Core;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// The final integration slice: its barrier, the cancel or block on a failed slice, and merging sibling work.
    /// </summary>
    public partial class MissionService
    {
        #region Private-Methods

        private async Task<IntegrationBarrierState> EvaluateIntegrationBarrierAsync(Mission mission, CancellationToken token)
        {
            if (mission == null || String.IsNullOrEmpty(mission.VoyageId) || !PersonaCatalog.Matches(mission.Persona, PersonaCatalog.Worker))
                return new IntegrationBarrierState();
            // Structured flag only (set from the architect plan's waitForOtherMissions, or once from the legacy
            // marker-format description when the architect output was parsed). Description wording is never read here.
            if (!mission.WaitForVoyageWorkers && !IntegrationBarrier.IsRework(mission))
                return new IntegrationBarrierState();

            List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(mission.VoyageId, token).ConfigureAwait(false);
            return await IntegrationBarrier.EvaluateAsync(mission, voyageMissions, IsCompletionInFlight,
                () => MergeQueueMissionActivity.GetMissionIdsWithActiveEntriesAsync(_Database, token)).ConfigureAwait(false);
        }

        private const string _IntegrationBlockedPrefix = "Integration not started: ";

        /// <summary>
        /// Stop a final integration slice whose sibling slice failed (H10): it is cancelled as DependencyFailed, naming the
        /// failed mission, and so are its own stages. Restarting the failed mission restores it.
        /// </summary>
        private async Task CancelIntegrationForFailedSliceAsync(Mission integration, Mission failed, CancellationToken token)
        {
            MissionStatusEnum observed = integration.Status;
            bool block = _Settings.BlockDependentsOfFailedStages && observed == MissionStatusEnum.Pending;
            integration.Status = block ? MissionStatusEnum.Blocked : MissionStatusEnum.Cancelled;
            integration.FailureKind = MissionFailureKindEnum.DependencyFailed;
            integration.FailureReason = _IntegrationBlockedPrefix + "mission '" + failed.Title + "' (" + failed.Id + ") in another slice ended "
                + failed.Status + ", so its work cannot be integrated. Restart that mission to bring this integration back, "
                + "or restart this one once the slice is resolved.";
            integration.CompletedUtc = block ? null : DateTime.UtcNow;
            integration.LastUpdateUtc = DateTime.UtcNow;
            if (!await _Database.Missions.TryUpdateIfStatusAsync(integration, new MissionStatusEnum[] { observed }, token).ConfigureAwait(false)) return;
            _Logging.Info(_Header + (block ? "blocked" : "cancelled") + " integration mission " + integration.Id + " because sibling mission " + failed.Id + " ended " + failed.Status);
            await RecordFailureAsync(integration, observed, BlackBoxStepEnum.IntegrationBarrier, null, context =>
            {
                context.Note = (block ? "Non-terminal: held as Blocked" : "Cancelled") + " because sibling slice " + failed.Id + " ended " + failed.Status
                    + (failed.LastBlackBoxEntryId != null ? " (entry " + failed.LastBlackBoxEntryId + ")" : "") + ".";
            }, token).ConfigureAwait(false);
            await CancelDependentPipelineStagesAsync(integration, token).ConfigureAwait(false);
        }

        private async Task CancelIntegrationsBlockedByFailureAsync(Mission failedMission, CancellationToken token)
        {
            List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(failedMission.VoyageId!, token).ConfigureAwait(false);
            foreach (Mission candidate in voyageMissions.Where(m => m.Status == MissionStatusEnum.Pending && IntegrationBarrier.Applies(m, voyageMissions)))
            {
                IntegrationBarrierState state = await IntegrationBarrier.EvaluateAsync(candidate, voyageMissions, IsCompletionInFlight,
                    () => MergeQueueMissionActivity.GetMissionIdsWithActiveEntriesAsync(_Database, token)).ConfigureAwait(false);
                if (state.FailedMission != null) await CancelIntegrationForFailedSliceAsync(candidate, state.FailedMission, token).ConfigureAwait(false);
            }
        }

        private const string _IntegrationMergeStart = "<!-- armada:integration-merge -->";
        private const string _IntegrationMergeEnd = "<!-- /armada:integration-merge -->";

        /// <summary>
        /// Bring the other slices' work onto a final integration slice's branch before it starts (H10): each settled
        /// commit is merged in the dock, a conflicting merge is aborted and left to the integration Worker with its files
        /// named, and slices an operator cancelled are listed as dropped. Returns a failure reason when a commit is not in
        /// the dock's repository and cannot be fetched, so the stage is refused instead of integrating without it.
        /// </summary>
        private async Task<string?> IntegrateSiblingSlicesAsync(Mission mission, Dock dock, CancellationToken token)
        {
            if (String.IsNullOrEmpty(dock.WorktreePath)) return null;
            IntegrationBarrierState state = await EvaluateIntegrationBarrierAsync(mission, token).ConfigureAwait(false);
            if (!state.Applies) return null;

            DockHost? harborHost = HarborHostFor(dock);
            IGitService? git = harborHost != null ? harborHost.Git : _Git;
            List<string> merged = new List<string>();
            List<string> conflicts = new List<string>();
            if (git != null)
            {
                foreach (Mission sibling in state.SettledCommits)
                {
                    string commit = sibling.CommitHash!.Trim();
                    string label = "`" + ShortCommit(commit) + "` " + sibling.Title + " (" + sibling.Id + ")";
                    GitCommitMergeResult result;
                    try
                    {
                        result = await git.MergeCommitAsync(dock.WorktreePath!, commit, sibling.BranchName,
                            "Armada: integrate " + sibling.Title + " (" + sibling.Id + ")", token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        result = new GitCommitMergeResult(GitCommitMergeOutcomeEnum.Failed, ex.Message);
                    }

                    if (result.Outcome == GitCommitMergeOutcomeEnum.Merged) merged.Add(label + ": merged");
                    else if (result.Outcome == GitCommitMergeOutcomeEnum.AlreadyContained) merged.Add(label + ": already on this branch");
                    else if (result.Outcome == GitCommitMergeOutcomeEnum.Conflicted)
                        conflicts.Add(label + ": `git merge " + commit + "` conflicts in " + String.Join(", ", result.ConflictedFiles.Select(f => "`" + f + "`")));
                    else if (result.Outcome == GitCommitMergeOutcomeEnum.Failed)
                        conflicts.Add(label + ": not merged (" + (result.Message ?? "git merge failed") + "); run `git merge " + commit + "`");
                    else
                    {
                        string where = harborHost != null ? "Harbor " + harborHost.HarborId : "the Admiral host";
                        return "Integration could not start: commit " + commit + " from mission '" + sibling.Title + "' (" + sibling.Id + ") is not in the repository on "
                            + where + (String.IsNullOrEmpty(sibling.BranchName) ? "" : ", and branch " + sibling.BranchName + " is not on the vessel's remote")
                            + ", so this slice would have started without that work. Remediation: run the integration where that slice ran, or push its branch to the vessel's remote, then restart this mission.";
                    }
                }
            }

            StringBuilder section = new StringBuilder();
            if (merged.Count > 0 || conflicts.Count > 0 || state.DroppedSlices.Count > 0)
            {
                section.AppendLine(_IntegrationMergeStart);
                section.AppendLine("## Work from the voyage's other slices");
                if (merged.Count > 0)
                {
                    section.AppendLine("Armada merged the finished work of the other slices into this branch before you started:");
                    foreach (string line in merged) section.AppendLine("- " + line);
                }
                if (conflicts.Count > 0)
                {
                    section.AppendLine("These were NOT merged. Merge each one yourself, resolve every conflict so both sides' intent is kept, then build, test and commit:");
                    foreach (string line in conflicts) section.AppendLine("- " + line);
                }
                if (state.DroppedSlices.Count > 0)
                {
                    section.AppendLine("These slices were cancelled by an operator and are out of scope; do not implement or integrate them:");
                    foreach (Mission dropped in state.DroppedSlices) section.AppendLine("- " + dropped.Title + " (" + dropped.Id + ")");
                }
                section.Append(_IntegrationMergeEnd);
            }

            string description = RemoveIntegrationMergeSection(mission.Description);
            if (section.Length > 0) description = description.TrimEnd() + "\n\n" + section.ToString();
            if (!String.Equals(description, mission.Description ?? String.Empty, StringComparison.Ordinal))
            {
                mission.Description = description;
                mission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.TryUpdateIfStatusAsync(mission, new MissionStatusEnum[] { MissionStatusEnum.Assigned }, token).ConfigureAwait(false);
            }
            _Logging.Info(_Header + "integration mission " + mission.Id + ": " + merged.Count + " sibling commit(s) on the branch, "
                + conflicts.Count + " left to the Worker, " + state.DroppedSlices.Count + " dropped slice(s)");
            return null;
        }

        private static string RemoveIntegrationMergeSection(string? description)
        {
            string text = description ?? String.Empty;
            int start = text.IndexOf(_IntegrationMergeStart, StringComparison.Ordinal);
            if (start < 0) return text;
            int end = text.IndexOf(_IntegrationMergeEnd, start, StringComparison.Ordinal);
            if (end < 0) return text;
            return (text.Substring(0, start).TrimEnd() + text.Substring(end + _IntegrationMergeEnd.Length)).TrimEnd();
        }

        private static string ShortCommit(string commit)
        {
            return commit.Length > 10 ? commit.Substring(0, 10) : commit;
        }

        /// <summary>
        /// Legacy fallback for architect output in the [ARMADA:MISSION] marker format, which has no structured
        /// sequencing field: a description that asks to run after the other implementation missions is converted once,
        /// when the architect output is parsed, into <see cref="Mission.WaitForVoyageWorkers"/>. Never applied to
        /// missions created any other way, and never consulted at dispatch time.
        /// </summary>
        private static bool LegacyDescriptionRequestsDeferral(string? description)
        {
            if (String.IsNullOrEmpty(description)) return false;

            string lowered = description.ToLowerInvariant();
            return lowered.Contains("after both implementation missions complete") ||
                lowered.Contains("sequential after both implementation missions") ||
                lowered.Contains("after the implementation missions land") ||
                lowered.Contains("after the implementation details are settled") ||
                lowered.Contains("after implementation details are settled") ||
                lowered.Contains("after the implementation details are finalized");
        }

        #endregion
    }
}
