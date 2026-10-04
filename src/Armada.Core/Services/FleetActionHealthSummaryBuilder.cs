namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Builds the plain-text value of the {{health.summary}} fleet action template variable from a vessel's stored
    /// health findings and dependency rows. Passing and not-applicable findings are omitted; outdated and vulnerable
    /// packages are listed one per line. The text is English and intended for prompts, not for the dashboard.
    /// </summary>
    public class FleetActionHealthSummaryBuilder
    {
        #region Public-Members

        /// <summary>
        /// Text rendered when the vessel has no stored health data.
        /// </summary>
        public static readonly string NoHealthDataText = "No health data has been collected for this vessel yet.";

        /// <summary>
        /// Maximum number of dependency lines included before the list is cut off with a count of the remainder.
        /// Default 50, minimum 1, maximum 1000.
        /// </summary>
        public int MaxDependencyLines
        {
            get => _MaxDependencyLines;
            set => _MaxDependencyLines = value < 1 ? 1 : (value > 1000 ? 1000 : value);
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriver _Database;
        private int _MaxDependencyLines = 50;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="database"/> is null.</exception>
        public FleetActionHealthSummaryBuilder(DatabaseDriver database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the summary for one vessel.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="vesselId">Vessel identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Summary text, never null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an identifier is null or empty.</exception>
        public async Task<string> BuildAsync(string tenantId, string vesselId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));

            List<VesselHealthFinding> findings = await _Database.VesselHealthFindings.ReadByVesselAsync(tenantId, vesselId, token).ConfigureAwait(false);
            List<VesselDependency> dependencies = await _Database.VesselDependencies.ReadByVesselAsync(tenantId, vesselId, token).ConfigureAwait(false);
            return Format(findings, dependencies, MaxDependencyLines);
        }

        /// <summary>
        /// Format findings and dependencies into summary text.
        /// </summary>
        /// <param name="findings">Findings; null treated as empty.</param>
        /// <param name="dependencies">Dependencies; null treated as empty.</param>
        /// <param name="maxDependencyLines">Maximum dependency lines, minimum 1.</param>
        /// <returns>Summary text, never null.</returns>
        public static string Format(List<VesselHealthFinding>? findings, List<VesselDependency>? dependencies, int maxDependencyLines = 50)
        {
            List<VesselHealthFinding> allFindings = findings ?? new List<VesselHealthFinding>();
            List<VesselDependency> allDependencies = dependencies ?? new List<VesselDependency>();
            if (allFindings.Count == 0 && allDependencies.Count == 0) return NoHealthDataText;
            if (maxDependencyLines < 1) maxDependencyLines = 1;

            StringBuilder sb = new StringBuilder();

            List<VesselHealthFinding> attention = allFindings
                .Where(f => f.Criterion != VesselHealthCriterionEnum.Overall
                    && (f.Status == VesselHealthStatusEnum.Fail || f.Status == VesselHealthStatusEnum.Warn || f.Status == VesselHealthStatusEnum.Unknown))
                .OrderBy(f => StatusRank(f.Status))
                .ThenBy(f => f.Criterion.ToString(), StringComparer.Ordinal)
                .ToList();

            if (attention.Count == 0)
            {
                sb.AppendLine("All evaluated health criteria pass.");
            }
            else
            {
                sb.AppendLine("Health findings that need attention:");
                foreach (VesselHealthFinding finding in attention)
                {
                    sb.AppendLine("- " + DescribeCriterion(finding.Criterion) + ": " + DescribeStatus(finding.Status) + DescribeDetail(finding));
                }
            }

            List<VesselDependency> flagged = allDependencies
                .Where(d => d.IsVulnerable || d.Drift != DependencyDriftEnum.None)
                .OrderByDescending(d => d.IsVulnerable)
                .ThenByDescending(d => (int)d.Severity)
                .ThenByDescending(d => (int)d.Drift)
                .ThenBy(d => d.PackageName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (flagged.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Outdated or vulnerable packages:");
                foreach (VesselDependency dependency in flagged.Take(maxDependencyLines))
                {
                    sb.AppendLine("- " + DescribeDependency(dependency));
                }

                if (flagged.Count > maxDependencyLines)
                    sb.AppendLine("- (" + (flagged.Count - maxDependencyLines) + " more not listed)");
            }

            return sb.ToString().TrimEnd();
        }

        #endregion

        #region Private-Methods

        private static int StatusRank(VesselHealthStatusEnum status)
        {
            if (status == VesselHealthStatusEnum.Fail) return 0;
            if (status == VesselHealthStatusEnum.Warn) return 1;
            return 2;
        }

        private static string DescribeStatus(VesselHealthStatusEnum status)
        {
            if (status == VesselHealthStatusEnum.Fail) return "failing";
            if (status == VesselHealthStatusEnum.Warn) return "warning";
            return "could not be determined";
        }

        private static string DescribeCriterion(VesselHealthCriterionEnum criterion)
        {
            switch (criterion)
            {
                case VesselHealthCriterionEnum.GitDivergence: return "Divergence from the default branch";
                case VesselHealthCriterionEnum.WorkingTree: return "Working tree";
                case VesselHealthCriterionEnum.Branches: return "Branches";
                case VesselHealthCriterionEnum.CommitRecency: return "Commit recency";
                case VesselHealthCriterionEnum.Dependencies: return "Outdated dependencies";
                case VesselHealthCriterionEnum.Vulnerabilities: return "Vulnerable dependencies";
                case VesselHealthCriterionEnum.TestInfrastructure: return "Test infrastructure";
                case VesselHealthCriterionEnum.ContinuousIntegration: return "Continuous integration";
                case VesselHealthCriterionEnum.ArmadaReadiness: return "Armada readiness";
                case VesselHealthCriterionEnum.MissionOutcomes: return "Recent mission outcomes";
                default: return criterion.ToString();
            }
        }

        private static string DescribeDetail(VesselHealthFinding finding)
        {
            List<string> parts = new List<string>();
            if (!String.IsNullOrWhiteSpace(finding.DetailCode)) parts.Add(finding.DetailCode!.Trim());
            if (finding.ValueA.HasValue) parts.Add("value " + finding.ValueA.Value);
            if (finding.ValueB.HasValue) parts.Add("secondary value " + finding.ValueB.Value);
            if (parts.Count == 0) return String.Empty;
            return " (" + String.Join(", ", parts) + ")";
        }

        private static string DescribeDependency(VesselDependency dependency)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(dependency.PackageName);
            if (!String.IsNullOrWhiteSpace(dependency.CurrentVersion) || !String.IsNullOrWhiteSpace(dependency.LatestVersion))
            {
                sb.Append(' ');
                sb.Append(String.IsNullOrWhiteSpace(dependency.CurrentVersion) ? "?" : dependency.CurrentVersion);
                if (!String.IsNullOrWhiteSpace(dependency.LatestVersion))
                {
                    sb.Append(" -> ");
                    sb.Append(dependency.LatestVersion);
                }
            }

            List<string> notes = new List<string>();
            if (!String.IsNullOrWhiteSpace(dependency.Ecosystem)) notes.Add(dependency.Ecosystem);
            if (dependency.Drift != DependencyDriftEnum.None) notes.Add(dependency.Drift.ToString().ToLowerInvariant() + " update");
            if (dependency.IsVulnerable)
            {
                notes.Add(dependency.Severity == VulnerabilitySeverityEnum.None
                    ? "vulnerable"
                    : "vulnerable, " + dependency.Severity.ToString().ToLowerInvariant() + " severity");
            }
            if (!String.IsNullOrWhiteSpace(dependency.ProjectPath)) notes.Add("in " + dependency.ProjectPath);

            if (notes.Count > 0) sb.Append(" (" + String.Join("; ", notes) + ")");
            return sb.ToString();
        }

        #endregion
    }
}
