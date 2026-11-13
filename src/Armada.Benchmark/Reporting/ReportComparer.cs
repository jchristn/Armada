namespace Armada.Benchmark.Reporting
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;

    /// <summary>
    /// Budget and regression checks. Within one report, Observe and Enforce p95 admission must stay within the budget of
    /// Disabled. Between a baseline and a candidate report with the same scenario hash, each arm's p95 may not grow by more
    /// than the tolerance plus an absolute noise floor.
    /// </summary>
    public static class ReportComparer
    {
        #region Public-Members

        /// <summary>Absolute p95 difference in milliseconds treated as noise.</summary>
        public const double NoiseFloorMs = 50;

        #endregion

        #region Public-Methods

        /// <summary>Record budget and accounting findings on a report and mark it incomparable when inputs were lost.</summary>
        /// <param name="report">Report to check.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="report"/> is null.</exception>
        public static void ApplyBudget(BenchmarkReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            report.Incomparable = report.Arms.Any(a => a.Incomplete > 0 || a.Errors > 0);
            foreach (ArmResult arm in report.Arms.Where(a => a.Incomplete > 0 || a.Errors > 0))
                report.Findings.Add(arm.Mode + ": " + arm.Incomplete + " incomplete missions and " + arm.Errors + " request errors; the run is incomparable");
            foreach (ArmResult arm in report.Arms.Where(a => a.AccountingViolations > 0))
                report.Findings.Add(arm.Mode + ": " + arm.AccountingViolations + " accounts admitted past their reservation capacity");
            ArmResult? disabled = report.Arms.FirstOrDefault(a => a.Mode == "Disabled");
            if (disabled == null) return;
            foreach (ArmResult arm in report.Arms.Where(a => a.Mode != "Disabled"))
            {
                double limit = disabled.AdmissionLatency.P95Ms * (1 + report.P95RegressionBudget) + NoiseFloorMs;
                if (arm.AdmissionLatency.P95Ms > limit)
                    report.Findings.Add(arm.Mode + " p95 admission " + Ms(arm.AdmissionLatency.P95Ms) + " exceeds Disabled " + Ms(disabled.AdmissionLatency.P95Ms)
                        + " by more than " + (report.P95RegressionBudget * 100).ToString("F0", CultureInfo.InvariantCulture) + "% plus " + Ms(NoiseFloorMs));
            }
        }

        /// <summary>Compare a candidate with a baseline; returns findings, empty when there is no regression.</summary>
        /// <param name="baseline">Baseline report.</param>
        /// <param name="candidate">Candidate report.</param>
        /// <param name="tolerance">Allowed relative p95 growth (0.20 = 20%).</param>
        /// <returns>Regression and comparability findings.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a report is null.</exception>
        public static List<string> Compare(BenchmarkReport baseline, BenchmarkReport candidate, double tolerance)
        {
            if (baseline == null) throw new ArgumentNullException(nameof(baseline));
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            List<string> findings = new List<string>();
            if (baseline.Provenance.ScenarioHash != candidate.Provenance.ScenarioHash)
            {
                findings.Add("scenario hashes differ; the reports measure different inputs");
                return findings;
            }
            if (candidate.Incomparable) findings.Add("candidate run is incomparable (incomplete missions or request errors)");
            foreach (ArmResult arm in candidate.Arms)
            {
                ArmResult? before = baseline.Arms.FirstOrDefault(a => a.Mode == arm.Mode);
                if (before == null) continue;
                double limit = before.AdmissionLatency.P95Ms * (1 + tolerance) + NoiseFloorMs;
                if (arm.AdmissionLatency.P95Ms > limit)
                    findings.Add(arm.Mode + " p95 " + Ms(before.AdmissionLatency.P95Ms) + " -> " + Ms(arm.AdmissionLatency.P95Ms));
                if (arm.AccountingViolations > 0) findings.Add(arm.Mode + " has accounting violations");
            }
            return findings;
        }

        #endregion

        #region Private-Methods

        private static string Ms(double value) => value.ToString("F1", CultureInfo.InvariantCulture) + " ms";

        #endregion
    }
}
