namespace Armada.Core.Services
{
    using Armada.Core.Enums;
    using Armada.Core.Models;

    public partial class MissionService
    {
        private sealed class CaptainAdmissionSelection
        {
            internal Mission Mission = null!;
            internal CaptainEligibilityContext Eligibility = null!;
            internal List<Captain> Visible = new();
            internal List<Captain> Available = new();
            internal List<Captain> RoutingEligible = new();
            internal Dictionary<string, MissionAssignmentBlocker> CapacityExclusions = new(StringComparer.Ordinal);
            internal Captain? Selected;
            internal List<CaptainRoutingBucket> RoutingBuckets = new List<CaptainRoutingBucket>();
        }

        private async Task<CaptainAdmissionSelection> SelectCaptainAdmissionAsync(Mission mission, CancellationToken token, bool recordEnvelope = false)
        {
            Mission view = mission.AdmissionView();
            if (!String.IsNullOrEmpty(view.TenantId))
                view.UsageRecovery = await _Database.CaptainSelections.ReadRecoveryAsync(view.TenantId, view.Id, token).ConfigureAwait(false);
            await ResolvePreferredCaptainAsync(view, token, persist: false).ConfigureAwait(false);
            CaptainEligibilityContext context = await BuildCaptainEligibilityContextAsync(view, token).ConfigureAwait(false);
            List<Captain> visible = (await _Database.Captains.EnumerateAsync(token).ConfigureAwait(false))
                .Where(c => CaptainVisibleToMission(c, view)).ToList();
            foreach (Captain captain in visible) captain.MaxConcurrency = ConcurrencyClamp.CaptainLimit(captain.MaxConcurrency, _Settings.Concurrency.MaxCaptainConcurrency);
            CaptainAdmissionSelection selection = new()
            {
                Mission = view, Eligibility = context, Visible = visible,
                Available = visible.Where(c => c.HasMissionCapacity).ToList()
            };
            WorkerEnvelopeGate? envelope = context.EnvelopeGate;
            selection.Selected = await FindRoutedCaptainAsync(view, envelope, selection, token).ConfigureAwait(false);
            WorkerEnvelopeOutcomeEnum outcome = WorkerEnvelopeOutcomeEnum.Assigned;
            if (selection.Selected == null && envelope != null)
            {
                selection.Selected = envelope.SelectEscalation(view);
                if (selection.Selected != null) selection.RoutingBuckets.Add(new CaptainRoutingBucket { ReasonCode = "WorkerEnvelopeEscalation", Captains = new List<Captain> { selection.Selected } });
                outcome = selection.Selected != null ? WorkerEnvelopeOutcomeEnum.Escalated
                    : envelope.AnyCompatibleCaptain() ? WorkerEnvelopeOutcomeEnum.Waiting : WorkerEnvelopeOutcomeEnum.NoEligibleCaptain;
            }
            if (recordEnvelope && envelope != null)
                await RecordWorkerEnvelopeDecisionAsync(view, envelope, outcome, selection.Selected, token).ConfigureAwait(false);
            return selection;
        }
    }
}
