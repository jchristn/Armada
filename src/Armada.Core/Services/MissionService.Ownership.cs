namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Path ownership in mission prompts (CONCURRENCY_IMPROVEMENTS.md 10.2): <c>{ConcurrentSiblingPaths}</c> lists the
    /// paths owned by voyage missions that may run at the same time (not this mission's ancestors or descendants) and
    /// <c>{ParentSummary}</c> what its parent finished.
    /// </summary>
    public partial class MissionService
    {
        private const int _MaxConcurrentPathsListed = 50;

        private async Task AddOwnershipParamsAsync(Mission mission, Dictionary<string, string> templateParams, CancellationToken token)
        {
            if (String.IsNullOrEmpty(mission.VoyageId)) return;
            try
            {
                List<Mission> voyage = await _Database.Missions.EnumerateByVoyageAsync(mission.VoyageId!, token).ConfigureAwait(false);
                templateParams["ConcurrentSiblingPaths"] = ConcurrentSiblingPaths(mission, voyage);
                templateParams["ParentSummary"] = ParentSummary(mission, voyage);
                // 10.4: the Judge sees whether the stage it reviews stayed inside its owned paths.
                if (PersonaCatalog.Matches(mission.Persona, PersonaCatalog.Judge) && !String.IsNullOrEmpty(mission.DependsOnMissionId))
                {
                    Mission? reviewed = voyage.FirstOrDefault(m => m.Id == mission.DependsOnMissionId);
                    if (reviewed != null && !String.IsNullOrEmpty(reviewed.DiffSnapshot) && templateParams.TryGetValue("PreviousStageOutput", out string? evidence))
                    {
                        OwnershipFinding finding = MissionOwnership.Check(reviewed, voyage, MissionOwnership.ChangedFiles(reviewed.DiffSnapshot));
                        if (!finding.Clean)
                            templateParams["PreviousStageOutput"] = evidence + "\n\n## Ownership check (warning, does not fail the mission)\n" + finding.Describe()
                                + " Judge whether the extra changes were necessary; flag unnecessary ones.\n";
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _Logging.Debug(_Header + "could not resolve path ownership for mission " + mission.Id + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Paths owned by active logical missions in the voyage that can run alongside <paramref name="mission"/>, as
        /// prompt lines; an overlap with the mission's own paths is called out.
        /// </summary>
        internal static string ConcurrentSiblingPaths(Mission mission, List<Mission> voyage)
        {
            List<ConcurrentOwnership> owners = MissionOwnership.ConcurrentOwners(mission, voyage);
            if (owners.Count == 0) return MergeConflictPrompt.NoConcurrentPaths;
            List<string> mine = MissionOwnership.Paths(mission.OwnedPathsJson);
            StringBuilder lines = new StringBuilder();
            foreach (ConcurrentOwnership owner in owners.Take(_MaxConcurrentPathsListed))
            {
                string? clash = OwnedPathMatcher.FirstOverlap(mine, owner.Paths);
                lines.Append("\n   - ").Append(String.Join(", ", owner.Paths.Select(p => "`" + p + "`")))
                    .Append(" (").Append(owner.Title).Append(')')
                    .Append(clash == null ? "" : " -- overlaps your owned paths at " + clash + "; leave the overlap to that mission or report a blocker");
            }
            if (owners.Count > _MaxConcurrentPathsListed) lines.Append("\n   - and ").Append(owners.Count - _MaxConcurrentPathsListed).Append(" more concurrent missions");
            return lines.ToString();
        }

        /// <summary>One line on the parent's finished result, or empty when the mission has no parent.</summary>
        internal static string ParentSummary(Mission mission, List<Mission> voyage)
        {
            Mission root = mission;
            Dictionary<string, Mission> byId = voyage.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            for (int hops = 0; String.IsNullOrEmpty(root.ParentMissionId) && !String.IsNullOrEmpty(root.DependsOnMissionId) && hops < 64
                && byId.TryGetValue(root.DependsOnMissionId!, out Mission? upstream); hops++) root = upstream;
            if (String.IsNullOrEmpty(root.ParentMissionId) || !byId.TryGetValue(root.ParentMissionId!, out Mission? parent)) return "";
            Mission terminal = FindTerminalPipelineStage(voyage, parent);
            StringBuilder text = new StringBuilder("\n   Your parent mission \"").Append(parent.Title).Append("\" (").Append(parent.Id).Append(") ")
                .Append(terminal.Status == MissionStatusEnum.Complete || terminal.Status == MissionStatusEnum.WorkProduced ? "finished" : "is " + terminal.Status);
            string? branch = terminal.BranchName ?? parent.BranchName;
            if (!String.IsNullOrEmpty(branch)) text.Append(" on `").Append(branch).Append('`');
            string? commit = terminal.CommitHash ?? parent.CommitHash;
            if (!String.IsNullOrEmpty(commit)) text.Append(" at ").Append(commit.Length > 12 ? commit.Substring(0, 12) : commit);
            List<string> owned = MissionOwnership.Paths(parent.OwnedPathsJson);
            if (owned.Count > 0) text.Append("; it owned ").Append(String.Join(", ", owned.Select(p => "`" + p + "`")));
            text.Append(". Build on its result; do not redo or revert it.\n");
            return text.ToString();
        }

    }
}
