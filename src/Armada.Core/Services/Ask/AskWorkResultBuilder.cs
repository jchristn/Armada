namespace Armada.Core.Services.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;

    /// <summary>
    /// Builds the <see cref="AskWorkResult"/> of finished tracked work from typed rows of the tracked item's tenant: each
    /// mission of the work snapshot with its vessel, failure reason, the captain's final message (Mission.AgentOutput),
    /// landing outcome, runtime, diff size (counted from the captured unified diff), and its most recent check runs with
    /// their exit codes, summaries, and test results.
    /// </summary>
    public class AskWorkResultBuilder
    {
        #region Public-Members

        /// <summary>
        /// Maximum missions included. Default 20, minimum 1, maximum 200.
        /// </summary>
        public int MaxMissions
        {
            get => _MaxMissions;
            set => _MaxMissions = value < 1 ? 1 : (value > 200 ? 200 : value);
        }

        /// <summary>
        /// Maximum check runs included per mission, newest first. Default 3, minimum 0, maximum 20.
        /// </summary>
        public int MaxChecksPerMission
        {
            get => _MaxChecksPerMission;
            set => _MaxChecksPerMission = value < 0 ? 0 : (value > 20 ? 20 : value);
        }

        /// <summary>
        /// Maximum characters kept of a captain's final message (its beginning). Default 4000, minimum 100, maximum
        /// 100000.
        /// </summary>
        public int MaxFinalMessageChars
        {
            get => _MaxFinalMessageChars;
            set => _MaxFinalMessageChars = value < 100 ? 100 : (value > 100000 ? 100000 : value);
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriver _Database;
        private int _MaxMissions = 20;
        private int _MaxChecksPerMission = 3;
        private int _MaxFinalMessageChars = 4000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when database is null.</exception>
        public AskWorkResultBuilder(DatabaseDriver database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the result of a tracked work item from its current snapshot.
        /// </summary>
        /// <param name="work">Tracked work row.</param>
        /// <param name="snapshot">The work's current snapshot.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when work or snapshot is null.</exception>
        public async Task<AskWorkResult> BuildAsync(AskTrackedWork work, AskWorkSnapshot snapshot, CancellationToken token = default)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            string tenantId = work.TenantId ?? Constants.DefaultTenantId;

            AskWorkResult result = new AskWorkResult();
            result.TrackedWorkId = work.Id;
            result.ElapsedMs = Elapsed(work, snapshot);

            Dictionary<string, string?> vesselNames = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (AskWorkMissionSnapshot row in snapshot.Missions.Take(_MaxMissions))
            {
                token.ThrowIfCancellationRequested();
                Mission? mission = await _Database.Missions.ReadAsync(tenantId, row.Id, token).ConfigureAwait(false);
                AskWorkMissionResult item = new AskWorkMissionResult();
                item.MissionId = row.Id;
                item.Title = row.Title;
                item.Status = row.Status;
                item.CaptainName = row.CaptainName;
                item.FailureReason = row.FailureReason;
                item.LandingOutcome = row.LandingOutcome;
                item.PrUrl = row.PrUrl;
                item.BranchName = row.BranchName;

                if (mission != null)
                {
                    item.DurationMs = mission.TotalRuntimeMs;
                    item.FinalMessage = Head(mission.AgentOutput, _MaxFinalMessageChars);
                    ApplyDiffStats(item, mission.DiffSnapshot);
                    if (!String.IsNullOrEmpty(mission.VesselId))
                    {
                        if (!vesselNames.TryGetValue(mission.VesselId!, out string? vesselName))
                        {
                            Vessel? vessel = await _Database.Vessels.ReadAsync(tenantId, mission.VesselId!, token).ConfigureAwait(false);
                            vesselName = vessel?.Name;
                            vesselNames[mission.VesselId!] = vesselName;
                        }

                        item.VesselName = vesselName;
                    }
                }

                if (_MaxChecksPerMission > 0)
                {
                    CheckRunQuery query = new CheckRunQuery();
                    query.TenantId = tenantId;
                    query.MissionId = row.Id;
                    query.PageNumber = 1;
                    query.PageSize = _MaxChecksPerMission;
                    EnumerationResult<CheckRun> checks = await _Database.CheckRuns.EnumerateAsync(query, token).ConfigureAwait(false);
                    foreach (CheckRun check in checks.Objects.Take(_MaxChecksPerMission)) item.Checks.Add(ToCheckResult(check));
                }

                result.Missions.Add(item);
            }

            return result;
        }

        /// <summary>
        /// Convert a check run to its result row.
        /// </summary>
        /// <param name="check">Check run.</param>
        /// <returns>The result row.</returns>
        /// <exception cref="ArgumentNullException">Thrown when check is null.</exception>
        public static AskWorkCheckResult ToCheckResult(CheckRun check)
        {
            if (check == null) throw new ArgumentNullException(nameof(check));
            AskWorkCheckResult item = new AskWorkCheckResult();
            item.CheckRunId = check.Id;
            item.Label = check.Label;
            item.Type = check.Type.ToString();
            item.Status = check.Status.ToString();
            item.ExitCode = check.ExitCode;
            item.Summary = String.IsNullOrWhiteSpace(check.Summary) ? null : check.Summary!.Trim();
            item.DurationMs = check.DurationMs ?? check.TestSummary?.DurationMs;
            if (check.TestSummary != null)
            {
                item.TestsTotal = check.TestSummary.Total;
                item.TestsPassed = check.TestSummary.Passed;
                item.TestsFailed = check.TestSummary.Failed;
                item.TestsSkipped = check.TestSummary.Skipped;
            }

            return item;
        }

        /// <summary>
        /// Count the files, added lines, and removed lines of a unified diff (as captured in Mission.DiffSnapshot).
        /// </summary>
        /// <param name="item">Mission result to fill in.</param>
        /// <param name="diff">Unified diff, or null.</param>
        public static void ApplyDiffStats(AskWorkMissionResult item, string? diff)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (String.IsNullOrEmpty(diff)) return;
            int files = 0;
            int added = 0;
            int removed = 0;
            bool inHunk = false;
            foreach (string raw in diff.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.StartsWith("diff --git ", StringComparison.Ordinal))
                {
                    files++;
                    inHunk = false;
                }
                else if (line.StartsWith("@@", StringComparison.Ordinal))
                {
                    inHunk = true;
                }
                else if (inHunk && line.StartsWith("+", StringComparison.Ordinal))
                {
                    added++;
                }
                else if (inHunk && line.StartsWith("-", StringComparison.Ordinal))
                {
                    removed++;
                }
            }

            item.FilesChanged = files;
            item.LinesAdded = added;
            item.LinesRemoved = removed;
        }

        #endregion

        #region Private-Methods

        private static long? Elapsed(AskTrackedWork work, AskWorkSnapshot snapshot)
        {
            DateTime? start = snapshot.StartedUtc;
            if (!start.HasValue)
            {
                List<DateTime> starts = snapshot.Missions.Where(m => m.StartedUtc.HasValue).Select(m => m.StartedUtc!.Value).ToList();
                start = starts.Count > 0 ? starts.Min() : work.CreatedUtc;
            }

            DateTime? end = snapshot.CompletedUtc;
            if (!end.HasValue)
            {
                List<DateTime> ends = snapshot.Missions.Where(m => m.CompletedUtc.HasValue).Select(m => m.CompletedUtc!.Value).ToList();
                end = ends.Count > 0 ? ends.Max() : (work.CompletedUtc ?? DateTime.UtcNow);
            }

            double ms = (end.Value - start.Value).TotalMilliseconds;
            return ms >= 0 ? (long)Math.Round(ms) : (long?)null;
        }

        private static string? Head(string? text, int max)
        {
            if (String.IsNullOrWhiteSpace(text)) return null;
            string trimmed = text.Trim();
            return trimmed.Length <= max ? trimmed : trimmed.Substring(0, max);
        }

        #endregion
    }
}
