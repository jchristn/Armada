namespace Armada.Core.Services.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Pure milestone detection: compares the previous and current snapshot of tracked work and returns the meaningful
    /// changes (work started, a mission failed, landed, opened a pull request, or could not land, and the whole item
    /// succeeded, failed, or was cancelled), each with a deterministic sentence. When there is no previous snapshot
    /// (first observation, or after a server restart) only a terminal state change is reported, so restarts never repeat
    /// earlier milestones.
    /// </summary>
    public static class AskMilestoneDetector
    {
        #region Public-Methods

        /// <summary>
        /// Detect milestones between two snapshots.
        /// </summary>
        /// <param name="previous">Previous snapshot, or null when unknown.</param>
        /// <param name="current">Current snapshot.</param>
        /// <param name="previousState">Persisted state of the tracked row before this snapshot.</param>
        /// <returns>Milestones in display order; empty when nothing meaningful changed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when current is null.</exception>
        public static List<AskMilestone> Detect(AskWorkSnapshot? previous, AskWorkSnapshot current, AskTrackedWorkStateEnum previousState)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            List<AskMilestone> milestones = new List<AskMilestone>();
            string label = Label(current);

            if (previous != null)
            {
                if (IsStarted(current) && !IsStarted(previous))
                {
                    AskWorkMissionSnapshot? first = current.Missions.FirstOrDefault(m => !String.IsNullOrEmpty(m.CaptainId));
                    string detail = first != null
                        ? ": " + (first.CaptainName ?? first.CaptainId) + " picked up mission \"" + first.Title + "\"."
                        : ".";
                    milestones.Add(new AskMilestone("Started", label + " started" + detail, false));
                }

                Dictionary<string, AskWorkMissionSnapshot> before = previous.Missions
                    .GroupBy(m => m.Id, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

                foreach (AskWorkMissionSnapshot mission in current.Missions)
                {
                    before.TryGetValue(mission.Id, out AskWorkMissionSnapshot? was);
                    string? oldStatus = was?.Status;
                    if (String.Equals(oldStatus, mission.Status, StringComparison.Ordinal) && String.Equals(was?.PrUrl, mission.PrUrl, StringComparison.Ordinal)) continue;

                    string name = "Mission \"" + mission.Title + "\"";
                    if (mission.Status == MissionStatusEnum.Failed.ToString())
                        milestones.Add(new AskMilestone("MissionFailed", name + " failed" + Reason(mission.FailureReason), false));
                    else if (mission.Status == MissionStatusEnum.LandingFailed.ToString())
                        milestones.Add(new AskMilestone("LandingFailed", name + " could not land" + Reason(mission.FailureReason), false));
                    else if (mission.Status == MissionStatusEnum.PullRequestOpen.ToString()
                        || (!String.IsNullOrEmpty(mission.PrUrl) && String.IsNullOrEmpty(was?.PrUrl) && mission.Status != MissionStatusEnum.Complete.ToString()))
                        milestones.Add(new AskMilestone("PullRequestOpened", name + " opened a pull request" + (String.IsNullOrEmpty(mission.PrUrl) ? "." : ": " + mission.PrUrl), false));
                    else if (mission.Status == MissionStatusEnum.WorkProduced.ToString())
                        milestones.Add(new AskMilestone("MissionWorkProduced", name + " produced its work" + (String.IsNullOrEmpty(mission.BranchName) ? "." : " on branch " + mission.BranchName + "."), false));
                    else if (mission.Status == MissionStatusEnum.Complete.ToString())
                        milestones.Add(new AskMilestone("MissionLanded", name + (String.Equals(mission.LandingOutcome, "PullRequestMerged", StringComparison.Ordinal) ? " landed (pull request merged)." : " landed."), false));
                }
            }

            bool wasActive = previous != null ? previous.State == AskTrackedWorkStateEnum.Active : previousState == AskTrackedWorkStateEnum.Active;
            if (wasActive && current.State != AskTrackedWorkStateEnum.Active)
            {
                switch (current.State)
                {
                    case AskTrackedWorkStateEnum.Succeeded:
                        milestones.Add(new AskMilestone("Succeeded", label + " finished" + Tally(current) + ".", true));
                        break;
                    case AskTrackedWorkStateEnum.Failed:
                        milestones.Add(new AskMilestone("Failed", label + " failed" + Tally(current) + Reason(current.ErrorText), true));
                        break;
                    case AskTrackedWorkStateEnum.Cancelled:
                        milestones.Add(new AskMilestone("Cancelled", label + (current.Found ? " was cancelled." : " no longer exists."), true));
                        break;
                }
            }

            return milestones;
        }

        /// <summary>
        /// A one-sentence deterministic summary of a snapshot (used when posting the first update of new work).
        /// </summary>
        /// <param name="snapshot">Snapshot.</param>
        /// <returns>The sentence.</returns>
        public static string Describe(AskWorkSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            return Label(snapshot) + " is " + (snapshot.Status ?? "unknown") + Tally(snapshot) + ".";
        }

        #endregion

        #region Private-Methods

        private static bool IsStarted(AskWorkSnapshot snapshot)
        {
            switch (snapshot.EntityType)
            {
                case AskTrackedEntityTypeEnum.Voyage:
                case AskTrackedEntityTypeEnum.Mission:
                    return snapshot.Missions.Any(m => !String.IsNullOrEmpty(m.CaptainId) || (m.Status != MissionStatusEnum.Pending.ToString()));
                case AskTrackedEntityTypeEnum.FleetActionRun:
                    return snapshot.Status != FleetActionRunStatusEnum.Pending.ToString();
                case AskTrackedEntityTypeEnum.Job:
                    return snapshot.Status != JobStatusEnum.Queued.ToString();
                default:
                    return true;
            }
        }

        private static string Label(AskWorkSnapshot snapshot)
        {
            string noun;
            switch (snapshot.EntityType)
            {
                case AskTrackedEntityTypeEnum.Voyage: noun = "Voyage"; break;
                case AskTrackedEntityTypeEnum.Mission: noun = "Mission"; break;
                case AskTrackedEntityTypeEnum.FleetActionRun: noun = "Fleet action run"; break;
                case AskTrackedEntityTypeEnum.Job: noun = "Job"; break;
                default: noun = "Import batch"; break;
            }

            string title = String.IsNullOrWhiteSpace(snapshot.Title) ? snapshot.EntityId : snapshot.Title;
            return noun + " \"" + title + "\"";
        }

        private static string Tally(AskWorkSnapshot snapshot)
        {
            if (snapshot.TotalCount <= 1 && snapshot.EntityType != AskTrackedEntityTypeEnum.Voyage && snapshot.EntityType != AskTrackedEntityTypeEnum.FleetActionRun) return String.Empty;
            string unit = snapshot.EntityType == AskTrackedEntityTypeEnum.FleetActionRun ? "targets" : (snapshot.EntityType == AskTrackedEntityTypeEnum.VesselImportBatch ? "repositories" : "missions");
            return " (" + snapshot.CompletedCount + " of " + snapshot.TotalCount + " " + unit + " done" + (snapshot.FailedCount > 0 ? ", " + snapshot.FailedCount + " failed" : String.Empty) + ")";
        }

        private static string Reason(string? reason)
        {
            if (String.IsNullOrWhiteSpace(reason)) return ".";
            string trimmed = reason.Trim();
            if (trimmed.Length > 300) trimmed = trimmed.Substring(0, 300) + "...";
            return ": " + trimmed.TrimEnd('.') + ".";
        }

        #endregion
    }
}
