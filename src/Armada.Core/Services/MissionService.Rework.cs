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
    /// Judge and verifier rework: building and writing the rework chain.
    /// </summary>
    public partial class MissionService
    {
        #region Private-Methods

        private async Task<bool> TryScheduleReworkAsync(Mission judge, CancellationToken token, Action<string>? onUnavailable = null)
        {
            bool Unavailable(string reason) { onUnavailable?.Invoke(reason); return false; }
            if (String.IsNullOrEmpty(judge.VoyageId) || String.IsNullOrEmpty(judge.DependsOnMissionId))
                return Unavailable("Automatic Judge rework is unavailable because the review has no voyage or Worker dependency.");
            List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(judge.VoyageId!, token).ConfigureAwait(false);
            Dictionary<string, Mission> byId = voyageMissions.ToDictionary(m => m.Id, StringComparer.Ordinal);

            // The Worker this Judge reviews, and how many rework rounds this chain has had.
            Mission? worker = null;
            Mission? previousJudge = null;
            int rounds = 0;
            bool escalated = false;
            HashSet<string> workerChain = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (Mission? cursor = judge; cursor != null && !String.IsNullOrEmpty(cursor.DependsOnMissionId) && seen.Add(cursor.Id);)
            {
                cursor = byId.TryGetValue(cursor.DependsOnMissionId!, out Mission? up) ? up : null;
                if (cursor == null) break;
                // The earlier review by the same kind of stage (the Judge, or the verifier that blocked), for stagnation.
                if (worker != null && previousJudge == null && PersonaCatalog.Matches(cursor.Persona, judge.Persona)) previousJudge = cursor;
                if (!PersonaCatalog.Matches(cursor.Persona, PersonaCatalog.Worker)) continue;
                if (worker == null) worker = cursor;
                workerChain.Add(cursor.Id);
                // A fresh Worker is the boundary of this implementation slice. Its dependency may be a
                // passing Judge from another Architect slice, whose rework budget is independent.
                if (!IntegrationBarrier.IsRework(cursor)) break;
                rounds++;
                escalated |= IntegrationBarrier.IsEscalatedRework(cursor);
            }

            if (worker == null) return Unavailable("Automatic Judge rework is unavailable because the reviewed Worker could not be found in the dependency chain.");

            // Give the Worker bounded rework, then try one different captain. Unchanged rejected work
            // switches earlier. Escalation also works in voyages with no planning stage.
            if (escalated || rounds > _MaxJudgeReworkRounds)
                return Unavailable("Automatic Judge rework limit reached after " + rounds + " rework round(s)" + (escalated ? ", including an escalated attempt." : "."));
            string? escalateTo = null;
            Mission? previousWorker = voyageMissions.FirstOrDefault(m => m.Id != worker.Id && PersonaCatalog.Matches(m.Persona, PersonaCatalog.Worker)
                && workerChain.Contains(m.Id) && !String.IsNullOrEmpty(m.CommitHash) && m.CommitHash == worker.CommitHash);
            // The same concrete affected case reported again after a rework round means the change did not reach it,
            // however much else changed: switch to another captain now instead of spending another full round.
            string? currentCase = JudgeContract.AffectedCaseKey(judge.AgentOutput);
            bool repeatedCase = previousJudge != null && currentCase != null
                && String.Equals(currentCase, JudgeContract.AffectedCaseKey(previousJudge.AgentOutput), StringComparison.Ordinal);
            bool stagnant = (previousWorker != null || repeatedCase) && !MissionModeContract.IsReadOnly(worker.Mode);
            if (rounds == _MaxJudgeReworkRounds || stagnant)
            {
                string escalationTrigger = rounds == _MaxJudgeReworkRounds
                    ? "Automatic Judge rework budget exhausted for this slice (" + rounds + " of " + _MaxJudgeReworkRounds + " rounds). "
                    : previousWorker != null
                        ? "The Worker produced the same rejected commit after " + rounds + " rework round(s). "
                        : "The Judge reported the same affected case (" + currentCase + ") after " + rounds + " rework round(s). ";
                if (await HasStrictCaptainOverrideAsync(worker, token).ConfigureAwait(false))
                    return Unavailable(escalationTrigger + "Strict captain selection prevents automatic escalation; no captain substitution was made.");
                Captain? alternative = await CaptainEscalationSelector.SelectAsync(_Database, worker, worker.CaptainId, voyageMissions, token).ConfigureAwait(false);
                if (alternative == null) return Unavailable(escalationTrigger + "No alternate eligible Worker captain is available for Judge-requested escalation. Check captain availability, scope, tier and persona priorities.");
                escalateTo = alternative.Id;
            }

            int round = rounds + 1;
            string baseTitle = worker.Title.Replace(" [Worker]", "", StringComparison.Ordinal);
            if (baseTitle.StartsWith(_ReworkTitlePrefix, StringComparison.Ordinal))
            {
                int colon = baseTitle.IndexOf(": ", StringComparison.Ordinal);
                if (colon > 0) baseTitle = baseTitle.Substring(colon + 2);
            }

            // The task without earlier handoffs or an earlier round's rework section (a later round restates its own).
            string taskSpec = ReworkPromptBuilder.StripSection(StripPipelineHandoff(worker.Description));
            Mission rework = new Mission(_ReworkTitlePrefix + round + (escalateTo != null ? _EscalatedMarker : "") + ": " + baseTitle,
                // Numbered demands, one per line; the reviewer's findings are filled in from its handoff artifact at launch.
                taskSpec + ReworkPromptBuilder.BuildSection(round, judge.Persona));
            rework.TenantId = judge.TenantId;
            rework.UserId = judge.UserId;
            rework.VoyageId = judge.VoyageId;
            rework.VesselId = judge.VesselId;
            rework.IsEvaluation = judge.IsEvaluation;
            rework.WorkerEnvelope = worker.WorkerEnvelope ?? judge.WorkerEnvelope;
            rework.Persona = PersonaCatalog.Worker;
            rework.ReworkRound = round;
            rework.ReworkEscalated = escalateTo != null;
            rework.Tier = worker.Tier;
            rework.WaitForVoyageWorkers = worker.WaitForVoyageWorkers;
            rework.RequestedCaptainId = escalateTo;
            rework.DependsOnMissionId = judge.Id;
            rework.RequiresReview = false;
            // The whole chain (rework, repeated stages, new reviewer, re-pointed later stages) is written in one
            // transaction, so an interruption can never leave later stages waiting on the rejected review (L8).
            List<Mission> chain = new List<Mission> { rework };

            // Repeat the actual verification stages between this slice's Worker and Judge. Rework must
            // not silently downgrade FullPipeline to Reviewed. Use the original contract, not stale excerpts.
            List<Mission> verificationStages = new List<Mission>();
            Mission? prior = judge;
            HashSet<string> stageIds = new HashSet<string>(StringComparer.Ordinal);
            while (!String.IsNullOrEmpty(prior.DependsOnMissionId) && stageIds.Add(prior.Id)
                && byId.TryGetValue(prior.DependsOnMissionId, out Mission? preceding) && preceding.Id != worker.Id)
            {
                verificationStages.Add(preceding);
                prior = preceding;
            }
            verificationStages.Reverse();
            string reworkParentId = rework.Id;
            foreach (Mission original in verificationStages)
            {
                Mission repeated = new Mission("[" + original.Persona + "] Rework " + round + ": " + baseTitle, taskSpec);
                repeated.TenantId = original.TenantId;
                repeated.UserId = original.UserId;
                repeated.VoyageId = original.VoyageId;
                repeated.VesselId = original.VesselId;
                repeated.IsEvaluation = original.IsEvaluation;
                repeated.WorkerEnvelope = original.WorkerEnvelope;
                repeated.Persona = original.Persona;
                repeated.Mode = original.Mode;
                repeated.Tier = original.Tier;
                repeated.RequestedCaptainId = original.RequestedCaptainId;
                repeated.DependsOnMissionId = reworkParentId;
                repeated.RequiresReview = original.RequiresReview;
                repeated.ReviewDenyAction = original.ReviewDenyAction;
                chain.Add(repeated);
                reworkParentId = repeated.Id;
            }

            bool reviewerIsJudge = PersonaCatalog.Matches(judge.Persona, PersonaCatalog.Judge);
            Mission nextJudge = new Mission(reviewerIsJudge
                ? "[Judge] Review rework " + round + ": " + baseTitle
                : "[" + judge.Persona + "] Rework " + round + ": " + baseTitle, taskSpec);
            nextJudge.TenantId = judge.TenantId;
            nextJudge.UserId = judge.UserId;
            nextJudge.VoyageId = judge.VoyageId;
            nextJudge.VesselId = judge.VesselId;
            nextJudge.IsEvaluation = judge.IsEvaluation;
            nextJudge.WorkerEnvelope = judge.WorkerEnvelope;
            nextJudge.Persona = reviewerIsJudge ? PersonaCatalog.Judge : judge.Persona;
            nextJudge.Tier = judge.Tier;
            nextJudge.DependsOnMissionId = reworkParentId;
            nextJudge.RequiresReview = judge.RequiresReview;
            nextJudge.ReviewDenyAction = judge.ReviewDenyAction;
            chain.Add(nextJudge);

            // The stages after this reviewer (Recorder, landing) now follow the new one.
            List<MissionDependencyUpdate> repointed = voyageMissions.Where(m => m.DependsOnMissionId == judge.Id)
                .Select(m => new MissionDependencyUpdate(m.Id, nextJudge.Id)).ToList();
            await _Database.Missions.CreateChainAsync(chain, repointed, token).ConfigureAwait(false);

            _Logging.Info(_Header + "judge " + judge.Id + ": rework round " + round + " as Worker mission " + rework.Id + ", re-review " + nextJudge.Id
                + (escalateTo != null ? "; escalating to " + escalateTo + (stagnant ? " after unchanged work" : " after the rework budget") : ""));
            return true;
        }

        private const string _ReworkTitlePrefix = IntegrationBarrier.ReworkTitlePrefix;

        private const string _EscalatedMarker = IntegrationBarrier.EscalatedMarker;

        private static bool IsAncestorOf(Mission candidate, Mission descendant, Dictionary<string, Mission> byId)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (Mission? cursor = descendant; cursor != null && !String.IsNullOrEmpty(cursor.DependsOnMissionId) && seen.Add(cursor.Id);)
            {
                if (cursor.DependsOnMissionId == candidate.Id) return true;
                cursor = byId.TryGetValue(cursor.DependsOnMissionId!, out Mission? up) ? up : null;
            }

            return false;
        }

        #endregion
    }
}
