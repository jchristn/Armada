namespace Armada.Tui.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Live work card rules shared with the dashboard (<c>lib/askWork.ts</c>): terminal and failed child statuses,
    /// whether an item is still active, progress, counts by status, routes, and folding a snapshot into its tracked
    /// row. Thread-safe (stateless).
    /// </summary>
    public static class AskWorkLogic
    {
        #region Private-Members

        private static readonly HashSet<string> _TerminalChild = new HashSet<string>(StringComparer.Ordinal)
        {
            "complete", "completed", "landed", "failed", "landingfailed", "cancelled", "succeeded", "skipped", "timedout"
        };

        private static readonly HashSet<string> _FailedChild = new HashSet<string>(StringComparer.Ordinal)
        {
            "failed", "landingfailed", "timedout"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Lower-case a status and drop spaces, underscores, and hyphens.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <returns>Normalized status.</returns>
        public static string NormalizeStatus(string? status)
        {
            if (String.IsNullOrEmpty(status)) return "";
            return new string(status!.Where(c => c != ' ' && c != '_' && c != '-' && c != '\t').ToArray()).ToLowerInvariant();
        }

        /// <summary>
        /// True for child statuses that will not change again.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <returns>True when terminal.</returns>
        public static bool IsTerminalChild(string? status)
        {
            return _TerminalChild.Contains(NormalizeStatus(status));
        }

        /// <summary>
        /// True for child statuses that count as failures.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <returns>True when failed.</returns>
        public static bool IsFailedChild(string? status)
        {
            return _FailedChild.Contains(NormalizeStatus(status));
        }

        /// <summary>
        /// True while a tracked item is still running.
        /// </summary>
        /// <param name="work">Tracked work, or null.</param>
        /// <returns>True when active.</returns>
        public static bool IsActive(AskTrackedWork? work)
        {
            return work != null && work.State == AskTrackedWorkStateEnum.Active;
        }

        /// <summary>
        /// True while a snapshot's item is still running.
        /// </summary>
        /// <param name="snapshot">Snapshot, or null.</param>
        /// <returns>True when active.</returns>
        public static bool IsActive(AskWorkSnapshot? snapshot)
        {
            return snapshot != null && snapshot.State == AskTrackedWorkStateEnum.Active;
        }

        /// <summary>
        /// Progress for the card's bar: from the child rows when present, else the counts, else the counters; null when
        /// nothing is known.
        /// </summary>
        /// <param name="snapshot">Snapshot.</param>
        /// <returns>Progress or null.</returns>
        public static AskWorkProgress? Progress(AskWorkSnapshot? snapshot)
        {
            if (snapshot == null) return null;
            int total = 0;
            int done = 0;
            int failed = 0;
            List<string> rows = ChildStatuses(snapshot);
            if (rows.Count > 0)
            {
                total = rows.Count;
                foreach (string status in rows)
                {
                    if (IsTerminalChild(status)) done++;
                    if (IsFailedChild(status)) failed++;
                }
            }
            else if (snapshot.Counts != null && snapshot.Counts.Count > 0)
            {
                foreach (KeyValuePair<string, int> pair in snapshot.Counts)
                {
                    total += pair.Value;
                    if (IsTerminalChild(pair.Key)) done += pair.Value;
                    if (IsFailedChild(pair.Key)) failed += pair.Value;
                }
            }
            else if (snapshot.TotalCount > 0)
            {
                total = snapshot.TotalCount;
                done = Math.Min(total, snapshot.CompletedCount);
                failed = snapshot.FailedCount;
            }
            else
            {
                return null;
            }

            if (snapshot.TotalCount > total) total = snapshot.TotalCount;
            AskWorkProgress p = new AskWorkProgress();
            p.Total = total;
            p.Done = done;
            p.Failed = failed;
            p.Percent = total > 0 ? (int)Math.Round(done * 100.0 / total) : 0;
            return p;
        }

        /// <summary>
        /// Counts by status in first-appearance order (from the rows when present, else the counts).
        /// </summary>
        /// <param name="snapshot">Snapshot.</param>
        /// <returns>Counts. Never null.</returns>
        public static List<AskStatusCount> StatusCounts(AskWorkSnapshot? snapshot)
        {
            List<AskStatusCount> result = new List<AskStatusCount>();
            if (snapshot == null) return result;
            List<string> rows = ChildStatuses(snapshot);
            if (rows.Count > 0)
            {
                foreach (string status in rows)
                {
                    AskStatusCount? existing = result.FirstOrDefault(c => c.Status == status);
                    if (existing == null) result.Add(new AskStatusCount(status, 1));
                    else existing.Count++;
                }
            }
            else if (snapshot.Counts != null)
            {
                foreach (KeyValuePair<string, int> pair in snapshot.Counts)
                {
                    if (pair.Value <= 0 || pair.Key.Length == 0) continue;
                    result.Add(new AskStatusCount(Char.ToUpperInvariant(pair.Key[0]) + pair.Key.Substring(1), pair.Value));
                }
            }

            return result;
        }

        /// <summary>
        /// The TUI route for a tracked item (the dashboard's <c>workRoute</c>).
        /// </summary>
        /// <param name="entityType">Entity type.</param>
        /// <param name="entityId">Entity id.</param>
        /// <returns>Route.</returns>
        public static string Route(AskTrackedEntityTypeEnum entityType, string entityId)
        {
            string id = Uri.EscapeDataString(entityId ?? "");
            switch (entityType)
            {
                case AskTrackedEntityTypeEnum.Voyage: return "/voyages/" + id;
                case AskTrackedEntityTypeEnum.Mission: return "/missions/" + id;
                case AskTrackedEntityTypeEnum.FleetActionRun: return "/fleet-actions/runs/" + id;
                case AskTrackedEntityTypeEnum.VesselImportBatch: return "/vessels/import?batch=" + id;
                case AskTrackedEntityTypeEnum.Job: return "/jobs";
                default: return "/activity";
            }
        }

        /// <summary>
        /// English label for an entity type (the dashboard's <c>useEntityTypeLabel</c>).
        /// </summary>
        /// <param name="entityType">Entity type.</param>
        /// <returns>English label.</returns>
        public static string EntityLabel(AskTrackedEntityTypeEnum entityType)
        {
            switch (entityType)
            {
                case AskTrackedEntityTypeEnum.Voyage: return "Voyage";
                case AskTrackedEntityTypeEnum.Mission: return "Mission";
                case AskTrackedEntityTypeEnum.FleetActionRun: return "Fleet action run";
                case AskTrackedEntityTypeEnum.Job: return "Background job";
                case AskTrackedEntityTypeEnum.VesselImportBatch: return "Vessel import";
                default: return entityType.ToString();
            }
        }

        /// <summary>
        /// Fold a fresh snapshot into its tracked row (status, state, title, times).
        /// </summary>
        /// <param name="work">Tracked row (modified in place).</param>
        /// <param name="snapshot">Snapshot.</param>
        public static void ApplySnapshot(AskTrackedWork work, AskWorkSnapshot snapshot)
        {
            if (work == null || snapshot == null) return;
            if (!String.IsNullOrEmpty(snapshot.Status)) work.Status = snapshot.Status;
            work.State = snapshot.State;
            if (String.IsNullOrEmpty(work.Title) && !String.IsNullOrEmpty(snapshot.Title)) work.Title = snapshot.Title;
            if (snapshot.CapturedUtc != null) work.LastChangeUtc = snapshot.CapturedUtc;
            if (snapshot.CompletedUtc != null) work.CompletedUtc = snapshot.CompletedUtc;
            work.Snapshot = snapshot;
        }

        #endregion

        #region Private-Methods

        private static List<string> ChildStatuses(AskWorkSnapshot snapshot)
        {
            if (snapshot.Missions != null && snapshot.Missions.Count > 0) return snapshot.Missions.Select(m => m.Status ?? "").ToList();
            if (snapshot.Targets != null && snapshot.Targets.Count > 0) return snapshot.Targets.Select(t => t.Status ?? "").ToList();
            return new List<string>();
        }

        #endregion
    }
}
