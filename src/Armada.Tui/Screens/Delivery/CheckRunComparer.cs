namespace Armada.Tui.Screens.Delivery
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Port of the dashboard's <c>checkRunComparison.ts</c>: finds each run's baseline (previous Passed or Failed run of
    /// the same vessel and type, preferring the same branch, then profile, then environment), computes deltas, and
    /// formats the comparison lines exactly as the dashboard does. Thread-safe (stateless).
    /// </summary>
    public static class CheckRunComparer
    {
        #region Public-Methods

        /// <summary>
        /// Comparisons for every run that has a baseline, keyed by run id.
        /// </summary>
        /// <param name="runs">Runs.</param>
        /// <returns>Map.</returns>
        public static Dictionary<string, CheckRunComparison> BuildMap(IEnumerable<CheckRun> runs)
        {
            Dictionary<string, CheckRunComparison> map = new Dictionary<string, CheckRunComparison>(StringComparer.Ordinal);
            List<CheckRun> all = (runs ?? Enumerable.Empty<CheckRun>()).ToList();
            foreach (IGrouping<string?, CheckRun> group in all.GroupBy(GroupKey).Where(g => g.Key != null))
            {
                List<CheckRun> groupRuns = group.ToList();
                foreach (CheckRun run in groupRuns)
                {
                    CheckRunComparison? comparison = Build(run, groupRuns);
                    if (comparison != null) map[run.Id] = comparison;
                }
            }

            return map;
        }

        /// <summary>
        /// Compare a run with candidates.
        /// </summary>
        /// <param name="current">Run.</param>
        /// <param name="runs">Candidate runs.</param>
        /// <returns>Comparison or null when there is no baseline.</returns>
        public static CheckRunComparison? Build(CheckRun current, IEnumerable<CheckRun> runs)
        {
            if (current == null || runs == null) return null;
            string? key = GroupKey(current);
            List<CheckRun> comparable = runs
                .Where(r => r.Id != current.Id)
                .Where(r => GroupKey(r) == key && key != null)
                .Where(r => IsOlderThan(r, current))
                .Where(r => r.Status == CheckRunStatusEnum.Passed || r.Status == CheckRunStatusEnum.Failed)
                .OrderByDescending(r => r.CreatedUtc)
                .ThenByDescending(r => r.Id, StringComparer.Ordinal)
                .ToList();
            if (comparable.Count == 0) return null;

            string profile = Normalize(current.WorkflowProfileId);
            string environment = Normalize(current.EnvironmentName);
            string branch = Normalize(current.BranchName);

            CheckRun? exact = comparable.FirstOrDefault(c => profile == Normalize(c.WorkflowProfileId) && environment == Normalize(c.EnvironmentName) && (branch.Length == 0 || branch == Normalize(c.BranchName)));
            if (exact != null)
            {
                string scope = branch.Length > 0 ? "same-branch" : profile.Length > 0 ? "same-profile" : environment.Length > 0 ? "same-environment" : "same-check-type";
                return Create(current, exact, scope);
            }

            if (profile.Length > 0)
            {
                CheckRun? sameProfile = comparable.FirstOrDefault(c => profile == Normalize(c.WorkflowProfileId) && environment == Normalize(c.EnvironmentName));
                if (sameProfile != null) return Create(current, sameProfile, "same-profile");
            }

            if (environment.Length > 0)
            {
                CheckRun? sameEnvironment = comparable.FirstOrDefault(c => environment == Normalize(c.EnvironmentName));
                if (sameEnvironment != null) return Create(current, sameEnvironment, "same-environment");
            }

            return Create(current, comparable[0], "same-check-type");
        }

        /// <summary>
        /// English scope label ("previous same branch").
        /// </summary>
        /// <param name="scope">Scope.</param>
        /// <returns>Label.</returns>
        public static string ScopeLabel(string scope)
        {
            switch (scope)
            {
                case "same-branch": return "previous same branch";
                case "same-profile": return "previous same profile";
                case "same-environment": return "previous same environment";
                default: return "previous same check type";
            }
        }

        /// <summary>
        /// One-line summary ("status Passed -> Failed | +2 failed | lines -1.5% | duration +3 s").
        /// </summary>
        /// <param name="c">Comparison.</param>
        /// <returns>Summary.</returns>
        public static string Summary(CheckRunComparison c)
        {
            List<string> parts = new List<string>();
            if (c.StatusChanged)
            {
                string to = c.Baseline.Status == CheckRunStatusEnum.Passed && c.HasRegression ? "Failed"
                    : c.Baseline.Status == CheckRunStatusEnum.Failed && c.HasImprovement ? "Passed" : "changed";
                parts.Add("status " + c.Baseline.Status + " -> " + to);
            }

            string? failed = CountDelta(c.FailedDelta, "failed");
            if (failed != null) parts.Add(failed);
            string? lines = PercentDelta(c.LinesPctDelta, "lines");
            if (lines != null) parts.Add(lines);
            string? duration = DurationDelta(c.DurationDeltaMs);
            if (duration != null) parts.Add("duration " + duration);
            string? artifacts = CountDelta(c.ArtifactCountDelta, "artifacts");
            if (artifacts != null) parts.Add(artifacts);
            return parts.Count > 0 ? String.Join(" | ", parts) : "No material change";
        }

        /// <summary>
        /// Signed count delta ("+2 failed"), or null when zero or unknown.
        /// </summary>
        /// <param name="value">Delta.</param>
        /// <param name="label">Label.</param>
        /// <returns>Text or null.</returns>
        public static string? CountDelta(double? value, string label)
        {
            if (!value.HasValue || value.Value == 0) return null;
            return (value.Value > 0 ? "+" : "") + value.Value.ToString(CultureInfo.InvariantCulture) + " " + label;
        }

        /// <summary>
        /// Signed percentage delta ("lines +1.25%"), or null when negligible.
        /// </summary>
        /// <param name="value">Delta.</param>
        /// <param name="label">Optional label.</param>
        /// <returns>Text or null.</returns>
        public static string? PercentDelta(double? value, string? label = null)
        {
            if (!value.HasValue || Math.Abs(value.Value) < 0.01) return null;
            double v = value.Value;
            string formatted = (v > 0 ? "+" : "") + v.ToString(Math.Abs(v) % 1 < 0.01 ? "0" : "0.00", CultureInfo.InvariantCulture) + "%";
            return label != null ? label + " " + formatted : formatted;
        }

        /// <summary>
        /// Signed duration delta ("+850 ms", "-1.50 s", "+2 min"), or null when negligible.
        /// </summary>
        /// <param name="ms">Delta in milliseconds.</param>
        /// <returns>Text or null.</returns>
        public static string? DurationDelta(double? ms)
        {
            if (!ms.HasValue || Math.Abs(ms.Value) < 1) return null;
            return (ms.Value > 0 ? "+" : "-") + Duration(Math.Abs(ms.Value));
        }

        /// <summary>
        /// Duration like the dashboard's check detail ("850 ms", "1.50 s", "2 min").
        /// </summary>
        /// <param name="ms">Milliseconds.</param>
        /// <returns>Text.</returns>
        public static string Duration(double ms)
        {
            if (ms < 1000) return Math.Round(ms).ToString(CultureInfo.InvariantCulture) + " ms";
            if (ms >= 60000) return (ms / 60000).ToString(ms % 60000 == 0 ? "0" : "0.00", CultureInfo.InvariantCulture) + " min";
            return (ms / 1000).ToString(ms % 1000 == 0 ? "0" : "0.00", CultureInfo.InvariantCulture) + " s";
        }

        /// <summary>
        /// Parsed result summary ("12 passed, 1 failed | 85% coverage"), or empty.
        /// </summary>
        /// <param name="run">Run.</param>
        /// <returns>Text.</returns>
        public static string ParsingSummary(CheckRun run)
        {
            List<string> parts = new List<string>();
            if (run.TestSummary != null)
            {
                List<string> tests = new List<string>();
                if (run.TestSummary.Passed.HasValue) tests.Add(run.TestSummary.Passed.Value.ToString(CultureInfo.InvariantCulture) + " passed");
                if (run.TestSummary.Failed.HasValue) tests.Add(run.TestSummary.Failed.Value.ToString(CultureInfo.InvariantCulture) + " failed");
                if (run.TestSummary.Skipped.HasValue && run.TestSummary.Skipped.Value > 0) tests.Add(run.TestSummary.Skipped.Value.ToString(CultureInfo.InvariantCulture) + " skipped");
                if (tests.Count > 0) parts.Add(String.Join(", ", tests));
            }

            double? coverage = run.CoverageSummary?.Lines?.Percentage ?? run.CoverageSummary?.Statements?.Percentage;
            if (coverage.HasValue) parts.Add(coverage.Value.ToString(coverage.Value % 1 == 0 ? "0" : "0.00", CultureInfo.InvariantCulture) + "% coverage");
            return String.Join(" | ", parts);
        }

        /// <summary>
        /// Coverage metric text ("85% (170/200)"), or null.
        /// </summary>
        /// <param name="metric">Metric.</param>
        /// <returns>Text or null.</returns>
        public static string? Metric(CheckRunCoverageMetric? metric)
        {
            if (metric == null) return null;
            string? pct = metric.Percentage.HasValue ? metric.Percentage.Value.ToString(metric.Percentage.Value % 1 == 0 ? "0" : "0.00", CultureInfo.InvariantCulture) + "%" : null;
            string? counts = metric.Covered.HasValue && metric.Total.HasValue ? metric.Covered.Value.ToString(CultureInfo.InvariantCulture) + "/" + metric.Total.Value.ToString(CultureInfo.InvariantCulture) : null;
            if (pct == null && counts == null) return null;
            return (pct ?? counts) + (pct != null && counts != null ? " (" + counts + ")" : "");
        }

        #endregion

        #region Private-Methods

        private static CheckRunComparison Create(CheckRun current, CheckRun baseline, string scope)
        {
            double? failedDelta = Delta(current.TestSummary?.Failed, baseline.TestSummary?.Failed);
            double? linesDelta = Delta(current.CoverageSummary?.Lines?.Percentage, baseline.CoverageSummary?.Lines?.Percentage);
            double? statementsDelta = Delta(current.CoverageSummary?.Statements?.Percentage, baseline.CoverageSummary?.Statements?.Percentage);
            CheckRunComparison c = new CheckRunComparison(baseline);
            c.Scope = scope;
            c.StatusChanged = current.Status != baseline.Status;
            c.HasRegression = (baseline.Status == CheckRunStatusEnum.Passed && current.Status == CheckRunStatusEnum.Failed)
                || (failedDelta.HasValue && failedDelta.Value > 0) || (linesDelta.HasValue && linesDelta.Value < 0) || (statementsDelta.HasValue && statementsDelta.Value < 0);
            c.HasImprovement = (baseline.Status == CheckRunStatusEnum.Failed && current.Status == CheckRunStatusEnum.Passed)
                || (failedDelta.HasValue && failedDelta.Value < 0) || (linesDelta.HasValue && linesDelta.Value > 0) || (statementsDelta.HasValue && statementsDelta.Value > 0);
            c.DurationDeltaMs = Delta(current.DurationMs, baseline.DurationMs);
            c.ArtifactCountDelta = (current.Artifacts?.Count ?? 0) - (baseline.Artifacts?.Count ?? 0);
            c.PassedDelta = Delta(current.TestSummary?.Passed, baseline.TestSummary?.Passed);
            c.FailedDelta = failedDelta;
            c.SkippedDelta = Delta(current.TestSummary?.Skipped, baseline.TestSummary?.Skipped);
            c.TotalDelta = Delta(current.TestSummary?.Total, baseline.TestSummary?.Total);
            c.LinesPctDelta = linesDelta;
            c.BranchesPctDelta = Delta(current.CoverageSummary?.Branches?.Percentage, baseline.CoverageSummary?.Branches?.Percentage);
            c.FunctionsPctDelta = Delta(current.CoverageSummary?.Functions?.Percentage, baseline.CoverageSummary?.Functions?.Percentage);
            c.StatementsPctDelta = statementsDelta;
            return c;
        }

        private static double? Delta(double? current, double? baseline)
        {
            if (!current.HasValue || !baseline.HasValue) return null;
            return Math.Round((current.Value - baseline.Value) * 100) / 100;
        }

        private static string? GroupKey(CheckRun run)
        {
            if (String.IsNullOrEmpty(run.VesselId)) return null;
            return run.VesselId + "::" + run.Type;
        }

        private static bool IsOlderThan(CheckRun candidate, CheckRun current)
        {
            if (candidate.CreatedUtc != current.CreatedUtc) return candidate.CreatedUtc < current.CreatedUtc;
            return String.CompareOrdinal(candidate.Id, current.Id) < 0;
        }

        private static string Normalize(string? value)
        {
            return (value ?? "").Trim();
        }

        #endregion
    }
}
