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

        private async Task<Mission> CreateArchitectSliceAsync(Mission architect, Mission template, ArchitectFanOutWork work, List<ParsedArchitectMission> parsed, int index, CancellationToken token)
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
            work.Create(worker);
            await CopyPlaybookSnapshotsAsync(work, template.Id, worker.Id, token).ConfigureAwait(false);

            await CloneDependentChainAsync(work, template, worker, parsed[index].Title, parsed[index].Description, token).ConfigureAwait(false);
            await ApplyArchitectWorkerEnvelopeAsync(architect, worker, parsed[index], work, token).ConfigureAwait(false);
            return worker;
        }

        // A slice or stage created by the fan-out carries the playbooks of the mission it is cloned from: the voyage's
        // selections are snapshotted per mission at dispatch, and a mission reads only its own snapshots (RS-2.8).
        private async Task CopyPlaybookSnapshotsAsync(ArchitectFanOutWork work, string fromMissionId, string toMissionId, CancellationToken token)
        {
            List<MissionPlaybookSnapshot> snapshots = work.PendingSnapshots(fromMissionId)
                ?? await _Database.Playbooks.GetMissionSnapshotsAsync(fromMissionId, token).ConfigureAwait(false);
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
            work.SetSnapshots(toMissionId, copies);
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

        /// <summary>
        /// Write a computed fan-out in one transaction together with the Architect's completion mark (RS-4.3): every slice,
        /// stage, rewiring and ownership change exists, or none does. A slice or the Architect whose status changed since
        /// it was read refuses the whole write; recovery computes the fan-out again from what is stored.
        /// </summary>
        private async Task<bool> CommitArchitectFanOutAsync(Mission architect, ArchitectFanOutWork work, CancellationToken token)
        {
            CrashPoints.Hit(CrashPointNames.FanOutPlanned);
            Mission? current = await _Database.Missions.ReadAsync(architect.Id, token).ConfigureAwait(false);
            if (current == null) return false;
            List<MissionGuardedWrite> extra = new List<MissionGuardedWrite>();
            if (!current.FanOutCompletedUtc.HasValue)
            {
                current.FanOutCompletedUtc = DateTime.UtcNow;
                extra.Add(new MissionGuardedWrite(current, new[] { current.Status }));
            }

            if (!await _Database.Missions.WriteBatchAsync(work.ToBatch(extra.ToArray()), token).ConfigureAwait(false))
            {
                _Logging.Warn(_Header + "the fan-out of Architect mission " + architect.Id + " was not written: a mission it changes moved on meanwhile; recovery will compute it again");
                return false;
            }

            architect.FanOutCompletedUtc = current.FanOutCompletedUtc;
            CrashPoints.Hit(CrashPointNames.FanOutWritten);
            if (work.CreatedCount > 0)
                _Logging.Info(_Header + "architect mission " + architect.Id + " fanned out: " + work.CreatedCount + " mission(s) created");

            foreach (ArmadaEvent evt in work.Events)
            {
                try
                {
                    await _Database.Events.CreateAsync(evt, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
                {
                    _Logging.Warn(_Header + "could not record event " + evt.EventType + " for mission " + evt.MissionId + ": " + ex.Message);
                }
            }
            return true;
        }

        /// <summary>
        /// Finish an Architect fan-out that was not marked complete (H11). The fan-out is written in one transaction with its
        /// completion mark (RS-4.3), so an unmarked Architect normally has written nothing and the normal fan-out runs again.
        /// A partial fan-out written before that change is reconciled against the plan: a slice whose Worker (found by its
        /// plan index) is missing is created, a Worker missing its stage chain gets one, and dependencies are applied to
        /// slices that have not started, all in one transaction. Nothing that exists is created twice, and a slice that
        /// already started is never rewired.
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

            ArchitectFanOutWork work = new ArchitectFanOutWork(voyageMissions);
            int repaired = 0;
            bool templateHasChain = voyageMissions.Any(m => m.DependsOnMissionId == template.Id);
            for (int i = 1; i < parsed.Count; i++)
            {
                Mission? worker = FindArchitectSlice(work.Missions, architect.Id, i);
                if (worker == null)
                {
                    await CreateArchitectSliceAsync(architect, template, work, parsed, i, token).ConfigureAwait(false);
                    repaired++;
                }
                else if (templateHasChain && !work.Missions.Any(m => m.DependsOnMissionId == worker.Id))
                {
                    await CloneDependentChainAsync(work, template, worker, parsed[i].Title, parsed[i].Description, token).ConfigureAwait(false);
                    repaired++;
                }
            }

            ApplyArchitectMissionDependencies(architect, parsed, work);
            if (!await CommitArchitectFanOutAsync(architect, work, token).ConfigureAwait(false)) return false;
            if (repaired > 0)
                _Logging.Warn(_Header + "finished the interrupted fan-out of Architect mission " + architect.Id + ": " + repaired + " slice(s) repaired");
            return repaired > 0;
        }

        #endregion
    }
}
