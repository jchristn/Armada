namespace Armada.Core.Services.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Pure, deterministic wording of the outcome of finished tracked work: the outcome block appended to the final
    /// milestone of an Ask Armada thread (Markdown) and the results given to the thread's captain for its report. Built
    /// only from typed fields (<see cref="AskWorkSnapshot"/> and <see cref="AskWorkResult"/>), never from parsing prose.
    /// </summary>
    public static class AskWorkOutcomeFormatter
    {
        #region Public-Members

        /// <summary>
        /// Maximum mission or target lines in an outcome block; the rest are counted.
        /// </summary>
        public const int MaxOutcomeLines = 10;

        /// <summary>
        /// Maximum characters of the captain's final message quoted in an outcome block.
        /// </summary>
        public const int MaxExcerptChars = 280;

        /// <summary>
        /// Maximum characters of a captain's final message given to the captain in its report context.
        /// </summary>
        public const int MaxReportMessageChars = 2000;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether an item of this type and state gets an outcome block and a report: voyages, missions, and fleet action
        /// runs that succeeded or failed (not cancelled ones, and not jobs or import batches, whose milestone already says
        /// everything they record).
        /// </summary>
        /// <param name="snapshot">Snapshot.</param>
        /// <returns>True when the outcome is reported.</returns>
        public static bool HasOutcome(AskWorkSnapshot snapshot)
        {
            if (snapshot == null) return false;
            if (snapshot.State != AskTrackedWorkStateEnum.Succeeded && snapshot.State != AskTrackedWorkStateEnum.Failed) return false;
            return snapshot.EntityType == AskTrackedEntityTypeEnum.Voyage
                || snapshot.EntityType == AskTrackedEntityTypeEnum.Mission
                || snapshot.EntityType == AskTrackedEntityTypeEnum.FleetActionRun;
        }

        /// <summary>
        /// The Markdown outcome block appended to the final milestone: elapsed time, then one line per mission (status,
        /// landing, pull request, failure reason, runtime) with its check runs (status, exit code, test counts, summary) and
        /// a short excerpt of the captain's final message, or one line per fleet action target (status, exit code,
        /// reason). Failed items are listed first. Empty when the item has no outcome (see <see cref="HasOutcome"/>).
        /// </summary>
        /// <param name="snapshot">Snapshot of the finished work.</param>
        /// <param name="result">Result built for it, or null (snapshot data only).</param>
        /// <returns>The block, or an empty string.</returns>
        public static string FormatOutcome(AskWorkSnapshot snapshot, AskWorkResult? result)
        {
            if (!HasOutcome(snapshot)) return String.Empty;
            StringBuilder builder = new StringBuilder();
            long? elapsed = result?.ElapsedMs;
            builder.Append("**Outcome**");
            if (elapsed.HasValue) builder.Append(" (took " + FormatDuration(elapsed.Value) + ")");
            builder.Append('\n');

            int lines = 0;
            if (snapshot.EntityType == AskTrackedEntityTypeEnum.FleetActionRun)
            {
                List<AskWorkTargetSnapshot> targets = snapshot.Targets.OrderBy(t => TargetFailed(t) ? 0 : 1).ToList();
                foreach (AskWorkTargetSnapshot target in targets.Take(MaxOutcomeLines))
                {
                    builder.Append("- " + TargetLine(target) + "\n");
                    lines++;
                }

                if (targets.Count > MaxOutcomeLines) builder.Append("- and " + (targets.Count - MaxOutcomeLines) + " more target(s)\n");
            }
            else
            {
                List<AskWorkMissionResult> missions = MissionRows(snapshot, result).OrderBy(m => MissionFailed(m) ? 0 : 1).ToList();
                foreach (AskWorkMissionResult mission in missions.Take(MaxOutcomeLines))
                {
                    builder.Append("- " + MissionLine(mission) + "\n");
                    foreach (AskWorkCheckResult check in mission.Checks.Where(c => c.Status != CheckRunStatusEnum.Pending.ToString() && c.Status != CheckRunStatusEnum.Running.ToString()))
                        builder.Append("  - " + CheckLine(check) + "\n");
                    string? excerpt = Excerpt(mission.FinalMessage, MaxExcerptChars);
                    if (excerpt != null) builder.Append("  - Captain's final message: \"" + excerpt + "\"\n");
                    lines++;
                }

                if (missions.Count > MaxOutcomeLines) builder.Append("- and " + (missions.Count - MaxOutcomeLines) + " more mission(s)\n");
            }

            if (lines == 0 && !elapsed.HasValue) return String.Empty;
            return builder.ToString().TrimEnd('\n');
        }

        /// <summary>
        /// Append the outcome block to a milestone sentence (separated by a blank line), or return the sentence unchanged
        /// when there is no outcome.
        /// </summary>
        /// <param name="milestoneText">The milestone sentence.</param>
        /// <param name="snapshot">Snapshot of the finished work.</param>
        /// <param name="result">Result built for it, or null.</param>
        /// <returns>The milestone text.</returns>
        public static string AppendOutcome(string milestoneText, AskWorkSnapshot snapshot, AskWorkResult? result)
        {
            string outcome = FormatOutcome(snapshot, result);
            if (outcome.Length == 0) return milestoneText ?? String.Empty;
            return (milestoneText ?? String.Empty).TrimEnd() + "\n\n" + outcome;
        }

        /// <summary>
        /// The results given to the thread's captain for its report: the work and its state, each mission with its ids,
        /// vessel, captain, status, landing, pull request, branch, runtime, diff size, failure reason, check runs with test
        /// results, and the beginning of its final message; or each fleet action target with its exit code and reason.
        /// </summary>
        /// <param name="snapshot">Snapshot of the finished work.</param>
        /// <param name="result">Result built for it, or null.</param>
        /// <returns>Plain text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when snapshot is null.</exception>
        public static string FormatReportContext(AskWorkSnapshot snapshot, AskWorkResult? result)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            StringBuilder builder = new StringBuilder();
            builder.Append(Noun(snapshot.EntityType) + " \"" + snapshot.Title + "\" (" + snapshot.EntityId + "): " + snapshot.State
                + (String.IsNullOrEmpty(snapshot.Status) ? String.Empty : " (status " + snapshot.Status + ")"));
            if (snapshot.TotalCount > 0)
                builder.Append(", " + snapshot.CompletedCount + " of " + snapshot.TotalCount + " done" + (snapshot.FailedCount > 0 ? ", " + snapshot.FailedCount + " failed" : String.Empty));
            if (result?.ElapsedMs != null) builder.Append(", took " + FormatDuration(result.ElapsedMs.Value));
            builder.Append(".\n");
            if (!String.IsNullOrWhiteSpace(snapshot.ErrorText)) builder.Append("Error: " + Clip(snapshot.ErrorText!.Trim(), 1000) + "\n");

            if (snapshot.EntityType == AskTrackedEntityTypeEnum.FleetActionRun)
            {
                foreach (AskWorkTargetSnapshot target in snapshot.Targets.Take(50))
                    builder.Append("Target " + TargetLine(target) + "\n");
            }

            foreach (AskWorkMissionResult mission in MissionRows(snapshot, result).Take(50))
            {
                builder.Append("Mission \"" + mission.Title + "\" (" + mission.MissionId + ")");
                if (!String.IsNullOrEmpty(mission.VesselName)) builder.Append(" on " + mission.VesselName);
                if (!String.IsNullOrEmpty(mission.CaptainName)) builder.Append(" by captain " + mission.CaptainName);
                builder.Append(": status " + mission.Status);
                if (!String.IsNullOrEmpty(mission.LandingOutcome)) builder.Append(", landing " + mission.LandingOutcome);
                if (!String.IsNullOrEmpty(mission.PrUrl)) builder.Append(", pull request " + mission.PrUrl);
                if (!String.IsNullOrEmpty(mission.BranchName)) builder.Append(", branch " + mission.BranchName);
                if (mission.DurationMs.HasValue) builder.Append(", took " + FormatDuration(mission.DurationMs.Value));
                if (mission.FilesChanged.HasValue)
                    builder.Append(", diff " + mission.FilesChanged + " file(s) +" + (mission.LinesAdded ?? 0) + "/-" + (mission.LinesRemoved ?? 0));
                builder.Append(".\n");
                if (!String.IsNullOrWhiteSpace(mission.FailureReason)) builder.Append("  Failure reason: " + Clip(mission.FailureReason!.Trim(), 1000) + "\n");
                foreach (AskWorkCheckResult check in mission.Checks)
                {
                    builder.Append("  Check " + CheckLine(check) + " (" + check.CheckRunId + ")\n");
                }

                if (!String.IsNullOrWhiteSpace(mission.FinalMessage))
                {
                    builder.Append("  Captain's final message:\n");
                    foreach (string line in Clip(mission.FinalMessage!.Trim(), MaxReportMessageChars).Split('\n'))
                        builder.Append("    " + line.TrimEnd('\r') + "\n");
                }
            }

            return builder.ToString().TrimEnd('\n');
        }

        /// <summary>
        /// A compact duration: "45s", "3m 12s", or "1h 04m".
        /// </summary>
        /// <param name="milliseconds">Duration in milliseconds.</param>
        /// <returns>The text.</returns>
        public static string FormatDuration(long milliseconds)
        {
            if (milliseconds < 0) milliseconds = 0;
            long seconds = (long)Math.Round(milliseconds / 1000.0);
            if (seconds < 60) return seconds.ToString(CultureInfo.InvariantCulture) + "s";
            long minutes = seconds / 60;
            if (minutes < 60) return minutes.ToString(CultureInfo.InvariantCulture) + "m " + (seconds % 60).ToString("00", CultureInfo.InvariantCulture) + "s";
            return (minutes / 60).ToString(CultureInfo.InvariantCulture) + "h " + (minutes % 60).ToString("00", CultureInfo.InvariantCulture) + "m";
        }

        /// <summary>
        /// A one-line excerpt: whitespace collapsed, and text longer than the limit cut at a word boundary with "...".
        /// </summary>
        /// <param name="text">Text, or null.</param>
        /// <param name="max">Maximum characters (at least 20).</param>
        /// <returns>The excerpt, or null when the text is empty.</returns>
        public static string? Excerpt(string? text, int max)
        {
            if (String.IsNullOrWhiteSpace(text)) return null;
            if (max < 20) max = 20;
            string collapsed = String.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (collapsed.Length <= max) return collapsed;
            string cut = collapsed.Substring(0, max);
            int space = cut.LastIndexOf(' ');
            if (space > max / 2) cut = cut.Substring(0, space);
            return cut.TrimEnd(',', ';', ':', '.', ' ') + "...";
        }

        #endregion

        #region Private-Methods

        private static List<AskWorkMissionResult> MissionRows(AskWorkSnapshot snapshot, AskWorkResult? result)
        {
            if (result != null && result.Missions.Count > 0) return result.Missions;
            List<AskWorkMissionResult> rows = new List<AskWorkMissionResult>();
            foreach (AskWorkMissionSnapshot row in snapshot.Missions)
            {
                AskWorkMissionResult item = new AskWorkMissionResult();
                item.MissionId = row.Id;
                item.Title = row.Title;
                item.Status = row.Status;
                item.CaptainName = row.CaptainName;
                item.FailureReason = row.FailureReason;
                item.LandingOutcome = row.LandingOutcome;
                item.PrUrl = row.PrUrl;
                item.BranchName = row.BranchName;
                rows.Add(item);
            }

            return rows;
        }

        private static string MissionLine(AskWorkMissionResult mission)
        {
            StringBuilder line = new StringBuilder();
            line.Append("Mission \"" + mission.Title + "\"");
            if (!String.IsNullOrEmpty(mission.VesselName)) line.Append(" on " + mission.VesselName);
            line.Append(": " + StatusPhrase(mission));
            if (!String.IsNullOrEmpty(mission.PrUrl)) line.Append(" (" + mission.PrUrl + ")");
            if (MissionFailed(mission) && !String.IsNullOrWhiteSpace(mission.FailureReason)) line.Append(": " + Clip(OneLine(mission.FailureReason!), 300).TrimEnd('.'));
            line.Append('.');
            if (mission.DurationMs.HasValue) line.Append(" Took " + FormatDuration(mission.DurationMs.Value) + ".");
            return line.ToString();
        }

        private static string StatusPhrase(AskWorkMissionResult mission)
        {
            MissionStatusEnum status;
            if (!Enum.TryParse<MissionStatusEnum>(mission.Status, out status)) return String.IsNullOrEmpty(mission.Status) ? "unknown" : mission.Status;
            switch (status)
            {
                case MissionStatusEnum.Complete:
                    if (String.Equals(mission.LandingOutcome, "PullRequestMerged", StringComparison.Ordinal)) return "complete, pull request merged";
                    if (String.Equals(mission.LandingOutcome, "Landed", StringComparison.Ordinal)) return "complete, landed";
                    return "complete";
                case MissionStatusEnum.PullRequestOpen: return "pull request open";
                case MissionStatusEnum.WorkProduced: return "work produced" + (String.IsNullOrEmpty(mission.BranchName) ? String.Empty : " on branch " + mission.BranchName);
                case MissionStatusEnum.Failed: return "failed";
                case MissionStatusEnum.LandingFailed: return "could not land";
                case MissionStatusEnum.Cancelled: return "cancelled";
                default: return status.ToString();
            }
        }

        private static string CheckLine(AskWorkCheckResult check)
        {
            StringBuilder line = new StringBuilder();
            line.Append(String.IsNullOrWhiteSpace(check.Label) ? check.Type + " check" : check.Type + " check \"" + check.Label!.Trim() + "\"");
            line.Append(" " + check.Status.ToLowerInvariant());
            List<string> details = new List<string>();
            if (check.ExitCode.HasValue) details.Add("exit code " + check.ExitCode.Value.ToString(CultureInfo.InvariantCulture));
            if (check.DurationMs.HasValue) details.Add(FormatDuration(check.DurationMs.Value));
            if (details.Count > 0) line.Append(" (" + String.Join(", ", details) + ")");
            string? tests = TestsPhrase(check);
            if (tests != null) line.Append(": " + tests);
            else if (!String.IsNullOrWhiteSpace(check.Summary)) line.Append(": " + Clip(OneLine(check.Summary!), 200).TrimEnd('.'));
            line.Append('.');
            return line.ToString();
        }

        private static string? TestsPhrase(AskWorkCheckResult check)
        {
            if (!check.TestsTotal.HasValue && !check.TestsPassed.HasValue && !check.TestsFailed.HasValue) return null;
            List<string> parts = new List<string>();
            if (check.TestsPassed.HasValue) parts.Add(check.TestsPassed.Value.ToString(CultureInfo.InvariantCulture) + " passed");
            if (check.TestsFailed.HasValue) parts.Add(check.TestsFailed.Value.ToString(CultureInfo.InvariantCulture) + " failed");
            if (check.TestsSkipped.HasValue && check.TestsSkipped.Value > 0) parts.Add(check.TestsSkipped.Value.ToString(CultureInfo.InvariantCulture) + " skipped");
            string text = parts.Count > 0 ? String.Join(", ", parts) : String.Empty;
            if (check.TestsTotal.HasValue) text = (text.Length > 0 ? text + " of " : String.Empty) + check.TestsTotal.Value.ToString(CultureInfo.InvariantCulture) + " tests";
            else text += " tests";
            return text;
        }

        private static string TargetLine(AskWorkTargetSnapshot target)
        {
            StringBuilder line = new StringBuilder();
            line.Append((String.IsNullOrEmpty(target.VesselName) ? target.VesselId : target.VesselName) + ": " + target.Status.ToLowerInvariant());
            if (target.ExitCode.HasValue) line.Append(" (exit code " + target.ExitCode.Value.ToString(CultureInfo.InvariantCulture) + ")");
            if (!String.IsNullOrWhiteSpace(target.Reason)) line.Append(": " + Clip(OneLine(target.Reason!), 300).TrimEnd('.'));
            line.Append('.');
            return line.ToString();
        }

        private static bool MissionFailed(AskWorkMissionResult mission)
        {
            return mission.Status == MissionStatusEnum.Failed.ToString()
                || mission.Status == MissionStatusEnum.LandingFailed.ToString()
                || mission.Status == MissionStatusEnum.Cancelled.ToString();
        }

        private static bool TargetFailed(AskWorkTargetSnapshot target)
        {
            return target.Status == FleetActionTargetStatusEnum.Failed.ToString()
                || target.Status == FleetActionTargetStatusEnum.TimedOut.ToString()
                || target.Status == FleetActionTargetStatusEnum.Cancelled.ToString();
        }

        private static string Noun(AskTrackedEntityTypeEnum type)
        {
            switch (type)
            {
                case AskTrackedEntityTypeEnum.Voyage: return "Voyage";
                case AskTrackedEntityTypeEnum.Mission: return "Mission";
                case AskTrackedEntityTypeEnum.FleetActionRun: return "Fleet action run";
                case AskTrackedEntityTypeEnum.Job: return "Job";
                default: return "Import batch";
            }
        }

        private static string OneLine(string text)
        {
            return String.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }

        private static string Clip(string text, int max)
        {
            if (String.IsNullOrEmpty(text)) return String.Empty;
            return text.Length <= max ? text : text.Substring(0, max) + "...";
        }

        #endregion
    }
}
