namespace Armada.Core.Services
{
    using System.Text;
    using System.Text.Json;
    using SyslogLogging;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Architect fan-out: creating slices and finishing an interrupted fan-out.
    /// </summary>
    public partial class MissionService
    {
        #region Private-Methods

        private async Task<Mission> CreateArchitectSliceAsync(Mission architect, Mission template, List<Mission> voyageMissions, List<ParsedArchitectMission> parsed, int index, CancellationToken token)
        {
            Mission worker = new Mission(parsed[index].Title + " [Worker]", parsed[index].Description
                + await BuildArchitectSliceContextAsync(architect, parsed, index, token).ConfigureAwait(false));
            worker.ArchitectSourceMissionId = architect.Id;
            worker.ArchitectPlanIndex = index;
            worker.TenantId = architect.TenantId;
            worker.UserId = architect.UserId;
            worker.VoyageId = architect.VoyageId;
            worker.VesselId = architect.VesselId;
            worker.IsEvaluation = architect.IsEvaluation;
            worker.Persona = "Worker";
            worker.Mode = template.Mode;
            worker.WaitForVoyageWorkers = parsed[index].WaitForOtherMissions;
            worker.DependsOnMissionId = architect.Id;
            worker.RequiresReview = template.RequiresReview;
            worker.ReviewDenyAction = template.ReviewDenyAction;
            worker.BranchName = null;
            worker = await _Database.Missions.CreateAsync(worker, token).ConfigureAwait(false);
            await CopyPlaybookSnapshotsAsync(template.Id, worker.Id, token).ConfigureAwait(false);
            _Logging.Info(_Header + "architect created additional worker mission " + worker.Id + ": " + parsed[index].Title);

            await CloneDependentChainAsync(voyageMissions, template, worker, parsed[index].Title, parsed[index].Description, token).ConfigureAwait(false);
            await ApplyArchitectWorkerEnvelopeAsync(architect, worker, parsed[index], voyageMissions, token).ConfigureAwait(false);
            return worker;
        }

        // A slice or stage created by the fan-out carries the playbooks of the mission it is cloned from: the voyage's
        // selections are snapshotted per mission at dispatch, and a mission reads only its own snapshots (RS-2.8).
        private async Task CopyPlaybookSnapshotsAsync(string fromMissionId, string toMissionId, CancellationToken token)
        {
            List<MissionPlaybookSnapshot> snapshots = await _Database.Playbooks.GetMissionSnapshotsAsync(fromMissionId, token).ConfigureAwait(false);
            if (snapshots.Count == 0) return;

            // Content is copied; where a playbook was materialized is per mission and is resolved again at launch.
            List<MissionPlaybookSnapshot> copies = snapshots.Select(s => new MissionPlaybookSnapshot
            {
                PlaybookId = s.PlaybookId,
                FileName = s.FileName,
                Description = s.Description,
                Content = s.Content,
                DeliveryMode = s.DeliveryMode,
                SourceLastUpdateUtc = s.SourceLastUpdateUtc
            }).ToList();
            await _Database.Playbooks.SetMissionSnapshotsAsync(toMissionId, copies, token).ConfigureAwait(false);
        }

        /// <summary>
        /// The Worker slice an Architect's plan entry created, found by its typed source and plan index (never by title,
        /// which a rename or a duplicate plan title would break). The newest live slice wins after a restarted fan-out.
        /// </summary>
        private static Mission? FindArchitectSlice(List<Mission> voyageMissions, string architectId, int planIndex)
        {
            return voyageMissions
                .Where(m => m.ArchitectPlanIndex == planIndex
                    && String.Equals(m.ArchitectSourceMissionId, architectId, StringComparison.Ordinal)
                    && PersonaCatalog.Matches(m.Persona, PersonaCatalog.Worker)
                    && m.Status != MissionStatusEnum.Cancelled)
                .OrderByDescending(m => m.CreatedUtc)
                .FirstOrDefault();
        }

        private async Task MarkArchitectFanOutCompleteAsync(Mission architect, CancellationToken token)
        {
            Mission? current = await _Database.Missions.ReadAsync(architect.Id, token).ConfigureAwait(false);
            if (current == null || current.FanOutCompletedUtc.HasValue) return;
            current.FanOutCompletedUtc = DateTime.UtcNow;
            current.LastUpdateUtc = current.FanOutCompletedUtc.Value;
            if (await _Database.Missions.TryUpdateIfStatusAsync(current, new MissionStatusEnum[] { current.Status }, token).ConfigureAwait(false))
                architect.FanOutCompletedUtc = current.FanOutCompletedUtc;
        }

        /// <summary>
        /// Finish an Architect fan-out that an Admiral restart interrupted (H11). The fan-out writes several rows (the first
        /// slice into the existing Worker, one new Worker and stage chain per further slice, then the plan's dependencies)
        /// and marks the Architect complete only at the end. An unmarked Architect in a running voyage is reconciled
        /// against its own plan: a slice whose Worker (found by its unique plan title) is missing is created, a Worker
        /// missing its stage chain gets one, and dependencies are applied to slices that have not started. Nothing that
        /// exists is created twice, and a slice that already started is never rewired.
        /// </summary>
        private async Task<bool> TryRecoverArchitectFanOutAsync(Mission architect, CancellationToken token)
        {
            if (architect.FanOutCompletedUtc.HasValue) return false;
            if (IsCompletionInFlight(architect.Id)) return false;
            Voyage? voyage = await _Database.Voyages.ReadAsync(architect.VoyageId!, token).ConfigureAwait(false);
            if (voyage == null || voyage.Status == VoyageStatusEnum.Complete || voyage.Status == VoyageStatusEnum.Failed || voyage.Status == VoyageStatusEnum.Cancelled) return false;

            List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(architect.VoyageId!, token).ConfigureAwait(false);

            // Never started: the first slice was not written into the waiting Worker. Run the normal fan-out.
            if (voyageMissions.Any(m => m.DependsOnMissionId == architect.Id && m.Status == MissionStatusEnum.Pending
                && PersonaCatalog.Matches(m.Persona, PersonaCatalog.Worker) && !IsPipelineHandoffPrepared(m, architect)))
            {
                _Logging.Warn(_Header + "re-driving the fan-out of Architect mission " + architect.Id + ", which an interruption left unstarted");
                return await TryHandoffToNextStageAsync(architect, token).ConfigureAwait(false);
            }

            List<ParsedArchitectMission> parsed = ParseArchitectOutput(architect);
            if (parsed.Count == 0) return false;
            Mission? template = FindArchitectSlice(voyageMissions, architect.Id, 0);
            if (template == null) return false;

            int repaired = 0;
            bool templateHasChain = voyageMissions.Any(m => m.DependsOnMissionId == template.Id);
            for (int i = 1; i < parsed.Count; i++)
            {
                Mission? worker = FindArchitectSlice(voyageMissions, architect.Id, i);
                if (worker == null)
                {
                    await CreateArchitectSliceAsync(architect, template, voyageMissions, parsed, i, token).ConfigureAwait(false);
                    repaired++;
                }
                else if (templateHasChain && !voyageMissions.Any(m => m.DependsOnMissionId == worker.Id))
                {
                    await CloneDependentChainAsync(voyageMissions, template, worker, parsed[i].Title, parsed[i].Description, token).ConfigureAwait(false);
                    repaired++;
                }
            }

            await ApplyArchitectMissionDependenciesAsync(architect, parsed, token).ConfigureAwait(false);
            await MarkArchitectFanOutCompleteAsync(architect, token).ConfigureAwait(false);
            if (repaired > 0)
                _Logging.Warn(_Header + "finished the interrupted fan-out of Architect mission " + architect.Id + ": " + repaired + " slice(s) repaired");
            return repaired > 0;
        }

        #endregion
    }
}
